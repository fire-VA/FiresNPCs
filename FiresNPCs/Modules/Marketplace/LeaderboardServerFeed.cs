using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresCore.Bridge;

namespace FiresRPGmaker.Modules.Marketplace
{
    public enum LeaderboardStat
    {
        KilledCreature = 0,
        KilledPlayer = 1,
        Built = 2,
        Crafted = 3,
        Death = 4,
        Harvested = 5,
        MapExplored = 6,
        // The weapon the local player held when it landed a killing blow on a non-player Character. Recorded
        // as a per-prefab tally (weapon_kills:*); the "favorite weapon" is the highest-count entry. This is a
        // SIBLING of the kill credit (which QuestProgressPatches owns) — it does NOT record a kill, only the
        // weapon, so there is no double counting.
        WeaponKill = 7,
        // First-entry-per-biome flag for the Adventure milestone timeline (prefab field = Heightmap.Biome name).
        BiomeReached = 8,
        // A creature was tamed near the local player (amount = 1). >0 satisfies the "first tame" milestone.
        Tamed = 9,
        // Hoe terrain ops (raise / level / path / paved road). Coalesced client-side (amount = batch count) so
        // per-tick terraforming doesn't flood the RPC. Tracked SEPARATELY from Built — terraforming isn't building.
        Terraformed = 10,
        // Cultivator ops (cultivate farmland + plant seeds/saplings). Coalesced client-side like Terraformed.
        Cultivated = 11,
        // What killed the LOCAL player (kg TriggerType.KilledBy parity): prefab field = attacker prefab name,
        // or "PLAYER_<name>" for PvP deaths — kg's exact key format (GameEvents.cs). Environment deaths
        // (fall/drown/fire, no attacker) record nothing, matching kg.
        KilledBy = 12,
    }

    /// <summary>
    /// Server-authoritative leaderboard feed. Client capture patches send structured stat events via the
    /// <c>Fires_Leaderboard_RecordStat</c> routed RPC; the server records them into Core's
    /// <see cref="LeaderboardBridge"/>/<c>LeaderboardRepository</c> (the shared per-world LiteDB vault), the
    /// single source of truth for the cross-player board. This replaces the client-local
    /// <see cref="LeaderboardTracker"/> CSV — which stays only until the panel is repointed at the server board.
    ///
    /// Kills are NOT captured here: <see cref="FiresRPGmaker.Modules.Quest.QuestProgressPatches"/> already
    /// credits every kill server-side (with companion attribution) and now also calls <see cref="Send"/>, so
    /// this file's death patch records only the local player's deaths — no double counting.
    /// </summary>
    public static class LeaderboardServerFeed
    {
        public const string RpcName = "Fires_Leaderboard_RecordStat";

        public static void RegisterRpc(ZRoutedRpc rpc)
        {
            rpc?.Register(RpcName, new Action<long, ZPackage>(RPC_RecordStat));
        }

        /// <summary>Owner key for the leaderboard row. The vault DB is already per-world, so the player name
        /// alone is a sufficient, stable-enough key (matches the existing name-keyed board).</summary>
        public static string OwnerKey(string playerName) => (playerName ?? string.Empty).Trim();

        /// <summary>What a placed <see cref="Piece"/> actually is, for leaderboard accounting. Hoe/cultivator
        /// pieces flood <see cref="Piece.SetCreator"/> every terrain tick and are NOT structures.</summary>
        public enum PieceKind { Structure, Terraform, Cultivate }

        /// <summary>Classify a just-placed piece: planting/cultivating → Cultivate, hoe terrain op → Terraform,
        /// anything else → Structure. Shared by the server feed and the legacy local tracker so they agree.</summary>
        public static PieceKind ClassifyPiece(Piece piece)
        {
            if (piece == null) return PieceKind.Structure;
            var go = piece.gameObject;
            if (go.GetComponent<Plant>() != null) return PieceKind.Cultivate;                        // planting seeds/saplings
            if (go.name.IndexOf("cultivate", StringComparison.OrdinalIgnoreCase) >= 0) return PieceKind.Cultivate; // farmland
            if (go.GetComponent<TerrainModifier>() != null) return PieceKind.Terraform;              // raise/level/path
            return PieceKind.Structure;
        }

        // ── Client → server ─────────────────────────────────────────────────────────────────────
        public static void Send(LeaderboardStat stat, string playerName, string prefab, int amount)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(playerName) || amount == 0) return;
            try
            {
                var pkg = new ZPackage();
                pkg.Write(playerName);
                pkg.Write((int)stat);
                pkg.Write(prefab ?? string.Empty);
                pkg.Write(amount);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, RpcName, pkg);
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] Leaderboard Send failed: {ex.Message}"); }
        }

        // ── Server: record into Core's authoritative repo ───────────────────────────────────────
        private static void RPC_RecordStat(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null) return;
            try
            {
                string playerName = pkg.ReadString();
                var stat = (LeaderboardStat)pkg.ReadInt();
                string prefab = pkg.ReadString();
                int amount = pkg.ReadInt();
                if (string.IsNullOrEmpty(playerName)) return;

                // Coalesce into a periodic server-side summary instead of logging every single event (a busy
                // player terraforming / building generated hundreds of lines a minute).
                LeaderboardActivitySummary.Note(stat);

                string ownerKey = OwnerKey(playerName);
                switch (stat)
                {
                    case LeaderboardStat.KilledCreature: LeaderboardBridge.RecordKilledCreature(ownerKey, playerName, prefab, amount); break;
                    // KilledPlayers is read off KilledCreatures["Player"] on LeaderboardEntry, so PvP kills go there.
                    case LeaderboardStat.KilledPlayer:   LeaderboardBridge.RecordKilledCreature(ownerKey, playerName, "Player", amount); break;
                    case LeaderboardStat.Built:          LeaderboardBridge.RecordBuilt(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.Crafted:        LeaderboardBridge.RecordCrafted(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.Death:          LeaderboardBridge.RecordDeath(ownerKey, playerName); break;
                    case LeaderboardStat.Harvested:      LeaderboardBridge.RecordHarvested(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.MapExplored:    LeaderboardBridge.SetMapExplored(ownerKey, playerName, Mathf.Clamp01(amount / 10000f)); break;
                    case LeaderboardStat.WeaponKill:     LeaderboardBridge.RecordWeaponKill(ownerKey, playerName, prefab, amount); break;
                    case LeaderboardStat.BiomeReached:   LeaderboardBridge.RecordBiomeReached(ownerKey, playerName, prefab); break;
                    case LeaderboardStat.Tamed:          LeaderboardBridge.RecordTamed(ownerKey, playerName, amount); break;
                    case LeaderboardStat.Terraformed:    LeaderboardBridge.RecordTerraformed(ownerKey, playerName, amount); break;
                    case LeaderboardStat.Cultivated:     LeaderboardBridge.RecordCultivated(ownerKey, playerName, amount); break;
                    case LeaderboardStat.KilledBy:       LeaderboardBridge.RecordKilledBy(ownerKey, playerName, prefab, amount); break;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] RPC_RecordStat failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Server-side rollup of leaderboard record events. Replaces per-event log spam with ONE line per ~30s of
    /// activity (flushed lazily on the next event past the interval), so a player terraforming or mass-building
    /// doesn't flood the server log. Records still hit the DB immediately — only the LOGGING is batched.
    /// </summary>
    internal static class LeaderboardActivitySummary
    {
        private static readonly Dictionary<LeaderboardStat, int> _counts = new Dictionary<LeaderboardStat, int>();
        private static float _lastFlush;
        private const float FlushSeconds = 30f;

        public static void Note(LeaderboardStat stat)
        {
            _counts.TryGetValue(stat, out int c);
            _counts[stat] = c + 1;
            if (Time.realtimeSinceStartup - _lastFlush < FlushSeconds) return;
            Flush();
        }

        private static void Flush()
        {
            _lastFlush = Time.realtimeSinceStartup;
            if (_counts.Count == 0) return;
            var parts = new List<string>();
            foreach (var kv in _counts) parts.Add(kv.Key + "x" + kv.Value);
            _counts.Clear();
            Debug.Log("[Leaderboard] activity (last 30s): " + string.Join(", ", parts));
        }
    }

    /// <summary>
    /// The leaderboard's game-event hooks, one Harmony patch per vanilla method. Each feeds both the server
    /// leaderboard (structured events) and the local tracker (<see cref="LeaderboardTrackerRecorder"/>). Server-side
    /// kill credit is intentionally absent here (sourced from QuestProgressPatches).
    /// </summary>
    public static class LeaderboardFeedPatches
    {
        /// <summary>Death events, called from <see cref="RpgVanillaHooks"/>: creature deaths arrive through
        /// Character.OnDeath, the local player's own death through Player.OnDeath (Player overrides OnDeath without
        /// calling base, so a Character.OnDeath hook never sees it — why deaths were never recorded before).</summary>
        public static class DeathFeed
        {
            // m_lastHit is protected on Character; read it reflectively to find the killing blow's attacker.
            private static readonly FieldInfo LastHitField =
                typeof(Character).GetField("m_lastHit", BindingFlags.NonPublic | BindingFlags.Instance);

            /// <summary>The local player died: record the death + the killer's identity (kg KilledBy parity;
            /// nothing recorded for environment deaths). Owner-only — a remote player's OnDeath is a no-op.</summary>
            internal static void OnLocalPlayerDeath(Player player)
            {
                var local = Player.m_localPlayer;
                if (player == null || local == null || player != local) return;
                string name = local.GetPlayerName();
                string killerKey = ResolveKillerKey(player);
                LeaderboardTrackerRecorder.OwnDeath(killerKey);
                LeaderboardServerFeed.Send(LeaderboardStat.Death, name, null, 1);
                if (!string.IsNullOrEmpty(killerKey))
                    LeaderboardServerFeed.Send(LeaderboardStat.KilledBy, name, killerKey, 1);
            }

            /// <summary>Something died from the LOCAL player's killing blow: the local tracker counts the kill; the
            /// server feed records only the weapon held (its kill credit comes from QuestProgressPatches).</summary>
            internal static void OnCharacterDeath(Character __instance)
            {
                var local = Player.m_localPlayer;
                if (__instance == null || local == null || __instance == (Character)local) return;
                if (!LocalPlayerLandedKillingBlow(__instance)) return;
                LeaderboardTrackerRecorder.Kill(__instance);
                if (__instance.IsPlayer()) return;
                if (TryGetCurrentWeaponId(local, out string weaponId))
                    LeaderboardServerFeed.Send(LeaderboardStat.WeaponKill, local.GetPlayerName(), weaponId, 1);
            }

            private static bool LocalPlayerLandedKillingBlow(Character victim)
            {
                try
                {
                    var hit = LastHitField?.GetValue(victim) as HitData;
                    return hit != null && hit.GetAttacker() == (Character)Player.m_localPlayer;
                }
                catch { return false; }
            }

            /// <summary>The kg KilledBy key for whatever landed the victim's killing blow: "PLAYER_&lt;name&gt;"
            /// for a player attacker, the attacker's prefab name otherwise, null when there is no attacker
            /// (environment death) — kg's exact contract (GameEvents.cs), so kg achievement cfgs drop in.</summary>
            internal static string ResolveKillerKey(Character victim)
            {
                try
                {
                    var hit = LastHitField?.GetValue(victim) as HitData;
                    var attacker = hit?.GetAttacker();
                    if (attacker == null) return null;
                    if (attacker.IsPlayer() && attacker is Player p) return "PLAYER_" + p.GetPlayerName();
                    return global::Utils.GetPrefabName(attacker.gameObject);
                }
                catch { return null; }
            }
        }

        // Resolve the killer's current weapon to a stable id: the item's m_dropPrefab name (fallback m_shared.m_name).
        private static bool TryGetCurrentWeaponId(Player player, out string weaponId)
        {
            weaponId = null;
            if (player == null) return false;
            try
            {
                // GetCurrentWeapon already resolves right-hand → left-hand → unarmed internally (the m_rightItem/
                // m_leftItem fields are protected and not reachable here), so it IS the right/left fallback.
                ItemDrop.ItemData item = player.GetCurrentWeapon();
                if (item == null) return false;

                if (item.m_dropPrefab != null)
                {
                    string prefabName = item.m_dropPrefab.name;
                    if (!string.IsNullOrEmpty(prefabName))
                        weaponId = prefabName.Replace("(Clone)", string.Empty).Trim();
                }
                if (string.IsNullOrEmpty(weaponId) && item.m_shared != null)
                    weaponId = item.m_shared.m_name;

                return !string.IsNullOrEmpty(weaponId);
            }
            catch { return false; }
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        public static class InventoryGui_DoCrafting
        {
            [HarmonyPostfix]
            private static void Postfix(Player player)
            {
                if (player == null || player != Player.m_localPlayer) return;
                LeaderboardTrackerRecorder.Crafted();
                LeaderboardServerFeed.Send(LeaderboardStat.Crafted, player.GetPlayerName(), null, 1);
            }
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        public static class Piece_SetCreator
        {
            /// <summary>Vanilla only records a creator when this machine owns the piece and none is set yet. Anything
            /// else is a no-op call, like PlacePiece's on a template that already destroyed itself in Awake.</summary>
            [HarmonyPrefix]
            private static void Prefix(Piece __instance, ZNetView ___m_nview, out bool __state)
                => __state = ___m_nview != null && ___m_nview.IsOwner() && __instance.GetCreator() == 0L;

            [HarmonyPostfix]
            private static void Postfix(Piece __instance, long uid, bool __state)
            {
                if (!__state) return;
                var local = Player.m_localPlayer;
                if (local == null || __instance == null) return;
                if (uid != local.GetPlayerID()) return;

                // Hoe / cultivator pieces (raise, level, path, cultivate, saplings) fire this EVERY terrain tick.
                // They are NOT structures — counting them as Built inflated the build score and firing a Built RPC
                // per tick hammered the server. Route them to their own tool-use stats, COALESCED client-side
                // (batched every few seconds) so the RPC fires a handful of times instead of hundreds.
                var kind = LeaderboardServerFeed.ClassifyPiece(__instance);
                LeaderboardTrackerRecorder.Piece(__instance, kind);
                switch (kind)
                {
                    case LeaderboardServerFeed.PieceKind.Cultivate: ToolUseCoalescer.NoteCultivate(); return;
                    case LeaderboardServerFeed.PieceKind.Terraform: ToolUseCoalescer.NoteTerraform(); return;
                    default:
                        LeaderboardServerFeed.Send(LeaderboardStat.Built, local.GetPlayerName(), __instance.m_category.ToString(), 1);
                        return;
                }
            }
        }

        /// <summary>
        /// Client-side batcher for hoe/cultivator ops. Terraforming fires <see cref="Piece.SetCreator"/> on every
        /// terrain tick; sending an RPC per tick floods Steam + the server DB. Instead we tally locally and flush
        /// the accumulated count as ONE Send every <see cref="FlushSeconds"/>. Flush is lazy (on the next op past
        /// the interval) plus a per-frame tail flush from <c>Player.Update</c> so the last batch isn't stranded.
        /// </summary>
        internal static class ToolUseCoalescer
        {
            private const float FlushSeconds = 3f;
            private static int _terraform;
            private static int _cultivate;
            private static float _lastFlush;

            public static void NoteTerraform() { _terraform++; FlushIfDue(); }
            public static void NoteCultivate() { _cultivate++; FlushIfDue(); }

            public static void FlushIfDue()
            {
                if (_terraform == 0 && _cultivate == 0) return;
                if (Time.realtimeSinceStartup - _lastFlush < FlushSeconds) return;
                _lastFlush = Time.realtimeSinceStartup;

                var local = Player.m_localPlayer;
                if (local == null) { _terraform = 0; _cultivate = 0; return; }
                string name = local.GetPlayerName();
                if (_terraform > 0) { LeaderboardServerFeed.Send(LeaderboardStat.Terraformed, name, null, _terraform); _terraform = 0; }
                if (_cultivate > 0) { LeaderboardServerFeed.Send(LeaderboardStat.Cultivated, name, null, _cultivate); _cultivate = 0; }
            }
        }

        // Map exploration — matches KG Marketplace's approach (kg.MarketplaceREF/LeaderBoard_Main_Client.cs:96-128):
        //   (1) Hook on Player.Update, NOT Minimap.Explore. Minimap.Explore only fires when a new pixel uncovers, so
        //       a stationary player (building/idling) never triggers it. Player.Update runs every frame so the
        //       time-accumulator throttle below produces a reliable snapshot regardless of movement.
        //   (2) Normalize by 4/π. Valheim's playable world is a CIRCLE inscribed in the square texture, so the
        //       maximum reachable fraction of `m_explored.Length` is π/4 ≈ 0.785. The 1.27323949f multiplier (== 4/π)
        //       rescales so a fully-explored map reads 100% instead of ~78.5%. Clamped to 1.0 for safety.
        //   (3) Biome-reached milestones ride the same per-frame hook: a throttled poll of the local player's biome,
        //       each biome sent once per session (the server set-once's it), so a stationary player still registers
        //       the biome they logged in or built in. The terraform/cultivate batch's tail flush runs here too.
        [HarmonyPatch(typeof(Player), nameof(Player.Update))]
        public static class Player_Update
        {
            private const float ExploreIntervalSeconds = 30f;
            private const float BiomeIntervalSeconds = 3f;
            private const float CircleNormalization = 1.27323949f; // 4 / π
            private static float _exploreAccum;
            private static float _biomeAccum;
            private static int _lastSentBp = -1; // only send when exploration actually increased
            private static readonly HashSet<Heightmap.Biome> _sentBiomes = new HashSet<Heightmap.Biome>();

            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                if (__instance == null || __instance != Player.m_localPlayer) return;

                // Flush the terraform/cultivate batch once it's been idle past the coalesce window, so the tail of a
                // terraforming session lands within ~3s of the last op.
                ToolUseCoalescer.FlushIfDue();
                UpdateExplored(__instance);
                UpdateBiome(__instance);
            }

            private static void UpdateExplored(Player player)
            {
                if (Minimap.instance == null) return;
                _exploreAccum += Time.deltaTime;
                if (_exploreAccum < ExploreIntervalSeconds) return;
                _exploreAccum = 0f;

                var arr = Minimap.instance.m_explored;
                if (arr == null || arr.Length == 0) return;
                int explored = 0;
                for (int i = 0; i < arr.Length; i++) if (arr[i]) explored++;
                float fraction = Mathf.Min(1f, (float)explored / arr.Length * CircleNormalization);
                LeaderboardTrackerRecorder.Explored(fraction);

                int basisPoints = Mathf.RoundToInt(fraction * 10000f);
                if (basisPoints <= _lastSentBp) return; // unchanged since last 30s snapshot — nothing to record
                _lastSentBp = basisPoints;
                LeaderboardServerFeed.Send(LeaderboardStat.MapExplored, player.GetPlayerName(), null, basisPoints);
            }

            private static void UpdateBiome(Player player)
            {
                _biomeAccum += Time.deltaTime;
                if (_biomeAccum < BiomeIntervalSeconds) return;
                _biomeAccum = 0f;

                Heightmap.Biome biome;
                try { biome = Heightmap.FindBiome(player.transform.position); }
                catch { return; }
                if (biome == Heightmap.Biome.None || !_sentBiomes.Add(biome)) return;
                LeaderboardServerFeed.Send(LeaderboardStat.BiomeReached, player.GetPlayerName(), biome.ToString(), 1);
            }
        }

        // Pickable.Interact's return is the use-animation flag, not a did-pick signal. Credit a non-repeat
        // interact with an un-picked pickable (prefix __state == false) by the local player — the same
        // optimistic, dedi-safe approach the legacy tracker documents (a contested pick can over-credit by 1).
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        public static class Pickable_Interact
        {
            [HarmonyPrefix]
            private static void Prefix(Pickable __instance, out bool __state)
                => __state = __instance != null && __instance.GetPicked();

            [HarmonyPostfix]
            private static void Postfix(Pickable __instance, Humanoid character, bool repeat, bool __state)
            {
                if (repeat || __state || __instance == null || character == null) return;
                var local = Player.m_localPlayer;
                if (local == null || character != (Humanoid)local) return;
                string name = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                LeaderboardTrackerRecorder.Harvested(name);
                LeaderboardServerFeed.Send(LeaderboardStat.Harvested, local.GetPlayerName(), name, 1);
            }
        }

        // First-tame milestone capture. Tameable.Tame fires once when taming completes, on the peer that owns the
        // creature — which is the taming player's client (they're standing next to it feeding it). Credit the
        // local player when the tamed creature is near them (optimistic; the milestone is a binary "first tame").
        [HarmonyPatch(typeof(Tameable), "Tame")]
        public static class Tameable_Tame
        {
            [HarmonyPostfix]
            private static void Postfix(Tameable __instance)
            {
                var local = Player.m_localPlayer;
                if (local == null || __instance == null) return;
                if (Vector3.Distance(local.transform.position, __instance.transform.position) > 40f) return;
                LeaderboardServerFeed.Send(LeaderboardStat.Tamed, local.GetPlayerName(), null, 1);
            }
        }
    }
}
