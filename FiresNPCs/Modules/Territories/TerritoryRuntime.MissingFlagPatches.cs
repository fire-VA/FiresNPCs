using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Closes parity gaps with KG's TerritorySystem_Main_Client.cs found during the 2026-06-08 audit.
    /// Each patch listed here is one KG enforces but we previously didn't, leaving its flag declared in
    /// the enum but silently no-op at runtime.
    ///
    /// PATTERN: every patch shares the same shape — get the player's current territory, check its flag,
    /// short-circuit / modify behaviour. Restriction-bypass (admin + owner-permitted) checks are pulled
    /// from the existing TerritoryRuntime helpers.
    /// </summary>
    internal static class TerritoryRuntime_MissingFlagPatches
    {
        // ─── TIER 1: flags that were exposed in the UI but did nothing ───

        /// <summary>InfiniteStamina — prefix Player.UseStamina to skip the drain.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
        public static class Player_UseStamina_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return true;
                var t = TerritoryRuntime.Current;
                return t == null || !t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.InfiniteStamina);
            }
        }

        /// <summary>InfiniteEitr — prefix Player.UseEitr to skip the drain.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.UseEitr))]
        public static class Player_UseEitr_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return true;
                var t = TerritoryRuntime.Current;
                return t == null || !t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.InfiniteEitr);
            }
        }

        /// <summary>NoBuildDamage — short-circuit WearNTear.Damage in any territory at the WnT's
        /// position that carries the flag (uses world position, not the player's current territory,
        /// because structures don't follow the player).</summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
        public static class WearNTear_Damage_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear __instance)
            {
                if (__instance == null) return true;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                return t == null || !t.Flags.HasFlag(TerritoryFlags.NoBuildDamage);
            }
        }

        /// <summary>NoStructureSupport — postfix WearNTear.HaveSupport to force support=true when the
        /// containing territory has the flag (lets you build floating bases).</summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.HaveSupport))]
        public static class WearNTear_HaveSupport_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(WearNTear __instance, ref bool __result)
            {
                if (__result || __instance == null) return;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                if (t != null && t.Flags.HasFlag(TerritoryFlags.NoStructureSupport)) __result = true;
            }
        }

        /// <summary>MonstersAddStars — when a monster awakens inside a MonstersAddStars territory,
        /// upgrade its level (1-3 stars). Marks the ZDO so respawned chunks don't double-buff.</summary>
        // 0.0.536: Humanoid.Start overrides Character.Start WITHOUT calling base (vanilla 1.0), so a Character.Start
        // postfix never ran for humanoid creatures (Boar, Greydwarf, Troll ...): round 18 monsters_add_stars, level 1.
        [HarmonyPatch(typeof(Humanoid), "Start")]
        public static class Humanoid_Start_AddStars_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Humanoid __instance) => Character_Start_AddStars_Patch.Apply(__instance);
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Start))]
        public static class Character_Start_AddStars_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Character __instance) => Apply(__instance);

            internal static void Apply(Character __instance)
            {
                if (__instance == null || !__instance.IsOwner() || __instance is Player) return;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.MonstersAddStars)) return;
                var zdo = __instance.m_nview != null ? __instance.m_nview.GetZDO() : null;
                if (zdo == null) return;
                if (zdo.GetBool("KGtsLevel")) return; // KG marker — we honor it for cross-mod compat
                zdo.Set("KGtsLevel", true);
                int extraStars = Random.Range(1, 4); // 1..3
                __instance.SetLevel(Mathf.Clamp(__instance.GetLevel() + extraStars, 1, 4));
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{t.Name}' MonstersAddStars {__instance.name} level -> {__instance.GetLevel()} via Start (owner)");
            }
        }

        // MoveSpeedMultiplier: TerritoryRuntime.ApplyMoveSpeedMultiplier scales walk/run/swim speed. The run/jog factor
        // postfixes that were here multiplied by ExponentialValue, which is the colour GRADIENT exponent (default 1), so
        // a gradient territory with this flag ran at speed x multiplier x exponent. Removed 0.0.530.

        // ─── TIER 2: closing loopholes (flag was advertised but trivially bypassed) ───

        /// <summary>NoBuild on REMOVE — KG also patches WearNTear.Remove so you can't take down structures
        /// inside a NoBuild territory (we only blocked TryPlacePiece, leaving the bypass).</summary>
        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
        public static class WearNTear_Remove_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(WearNTear __instance)
            {
                if (__instance == null) return true;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.NoBuild)) return true;
                if (TerritoryRuntime.HasRestrictionBypassPublic(t, Player.m_localPlayer)) return true;
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Removing structures not allowed here");
                return false;
            }
        }

        /// <summary>NoPortals — also gate Player.TeleportTo, not just Door/Place.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        public static class Player_TeleportTo_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer) return true;
                var t = TerritoryRuntime.Current;
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.NoPortals)) return true;
                if (TerritoryRuntime.HasRestrictionBypassPublic(t, __instance)) return true;
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Portals are disabled here");
                __result = false;
                return false;
            }
        }

        /// <summary>NoDeathPenalty — Player.HardDeath returns false (skips skill loss + grave).</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.HardDeath))]
        public static class Player_HardDeath_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance, ref bool __result)
            {
                if (__instance != Player.m_localPlayer) return true;
                var t = TerritoryRuntime.Current;
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.NoDeathPenalty)) return true;
                __result = false;
                return false;
            }
        }

        /// <summary>NoInteractItemStands on ARMOR STANDS (KG patches both ItemStand and ArmorStand).</summary>
        [HarmonyPatch(typeof(ArmorStand), nameof(ArmorStand.UseItem))]
        public static class ArmorStand_UseItem_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(ArmorStand __instance, Humanoid user)
            {
                if (__instance == null) return true;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.NoInteractItemStands)) return true;
                if (TerritoryRuntime.HasRestrictionBypassPublic(t, user as Player)) return true;
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Armor stands locked here");
                return false;
            }
        }

        /// <summary>NoPickaxe on terrain dig — KG patches Attack.SpawnOnHitTerrain (also covered by our
        /// existing TerrainOp/TerrainComp guards, but the Attack path skips those). Belt and suspenders.</summary>
        [HarmonyPatch(typeof(Attack), "SpawnOnHitTerrain")]
        public static class Attack_SpawnOnHitTerrain_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Vector3 hitPoint)
            {
                var t = TerritoryRuntime.GetTerritory(hitPoint);
                if (t == null || !t.Flags.HasFlag(TerritoryFlags.NoPickaxe)) return true;
                if (TerritoryRuntime.HasRestrictionBypassPublic(t, Player.m_localPlayer)) return true;
                return false;
            }
        }

        /// <summary>NoInteract / NoInteractItems on auto-pickup — KG blocks Player.AutoPickup so loose
        /// items aren't sucked up while you walk through a no-interact territory.</summary>
        [HarmonyPatch(typeof(Player), "AutoPickup")]
        public static class Player_AutoPickup_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(Player __instance)
            {
                if (__instance != Player.m_localPlayer) return true;
                var t = TerritoryRuntime.Current;
                if (t == null) return true;
                bool blocked = t.Flags.HasFlag(TerritoryFlags.NoInteractItems) || t.Flags.HasFlag(TerritoryFlags.NoInteract);
                if (!blocked) return true;
                if (TerritoryRuntime.HasRestrictionBypassPublic(t, __instance)) return true;
                return false;
            }
        }

        // ─── TIER 3: spawn-gating gaps ───

        /// <summary>NoMonsters — block CreatureSpawner (placed spawners) inside the flag.</summary>
        [HarmonyPatch(typeof(CreatureSpawner), nameof(CreatureSpawner.Spawn))]
        public static class CreatureSpawner_Spawn_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(CreatureSpawner __instance)
            {
                if (__instance == null) return true;
                var t = TerritoryRuntime.GetTerritory(__instance.transform.position);
                return t == null || !t.Flags.HasFlag(TerritoryFlags.NoMonsters);
            }
        }

        /// <summary>NoMonsters — KG also prefixes SpawnSystem.UpdateSpawning to stop the spawning loop
        /// from even running inside the territory (we already covered SpawnSystem.Spawn but the loop
        /// keeps allocating).</summary>
        /// It used to read the LOCAL player's territory, so a client standing in a NoMonsters area stopped every zone it
        /// owns from spawning, and the dedicated server (no local player) never skipped. Now a zone's loop is skipped only
        /// when the whole zone lies inside NoMonsters areas; SpawnSystem.Spawn still blocks each point inside one.
        [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.UpdateSpawning))]
        public static class SpawnSystem_UpdateSpawning_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(SpawnSystem __instance)
            {
                if (__instance == null) return true;
                var c = __instance.transform.position;
                const float h = ZoneSystem.c_ZoneSize * 0.5f;
                bool covered = NoMonstersAt(c)
                         && NoMonstersAt(c + new Vector3(-h, 0f, -h)) && NoMonstersAt(c + new Vector3(h, 0f, -h))
                         && NoMonstersAt(c + new Vector3(-h, 0f, h)) && NoMonstersAt(c + new Vector3(h, 0f, h));
                if (covered) LogSkip(c);
                return !covered;
            }

            // Evidence for rpg_territory_test (0.0.532): at most once a minute per zone.
            private static readonly System.Collections.Generic.Dictionary<string, float> s_lastSkipLog = new System.Collections.Generic.Dictionary<string, float>();
            private static void LogSkip(Vector3 centre)
            {
                var zone = ZoneSystem.GetZone(centre);
                string key = zone.x + "," + zone.y;
                if (s_lastSkipLog.TryGetValue(key, out float last) && Time.time - last < 60f) return;
                s_lastSkipLog[key] = Time.time;
                string name = TerritoryRuntime.GetTerritory(centre)?.Name ?? "?";
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] NoMonsters: skipping the spawn loop for zone ({zone.x},{zone.y}) ('{name}' covers it)");
            }

            private static bool NoMonstersAt(Vector3 pos)
            {
                var t = TerritoryRuntime.GetTerritory(pos);
                return t != null && t.Flags.HasFlag(TerritoryFlags.NoMonsters);
            }
        }

        // ─── TIER 3 cont: remaining KG parity patches ───

        /// <summary>NoMist — KG toggles ParticleMist.instance.m_ps.emission.enabled in a per-frame check.
        /// We patch the mist's OnEnable + run a polling refresh from the territory tick so entering a
        /// NoMist zone hides existing mist particles, leaving the zone restores them.</summary>
        [HarmonyPatch(typeof(Mister), nameof(Mister.OnEnable))]
        public static class Mister_OnEnable_Patch
        {
            [HarmonyPostfix]
            private static void Postfix(Mister __instance)
            {
                var t = TerritoryRuntime.Current;
                if (t == null || !t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.NoMist)) return;
                try
                {
                    var ps = __instance != null ? __instance.GetComponent<ParticleSystem>() : null;
                    if (ps != null) { var e = ps.emission; e.enabled = false; }
                }
                catch { }
            }
        }

        /// <summary>EWPSpawner / CustomJereSpawner — KG patches SpawnSystem.FindBaseSpawnPoint so that when
        /// the territory has the flag, only spawn points whose name is in the territory's
        /// CustomSpawnerNames list are eligible. When the flag is NOT set, it filters OUT any spawn point
        /// containing "MARKETPLACESPAWNER" (so EWP spawners don't pollute normal world spawning).</summary>
        [HarmonyPatch(typeof(SpawnSystem), "FindBaseSpawnPoint")]
        public static class SpawnSystem_FindBaseSpawnPoint_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(SpawnSystem.SpawnData spawn)
            {
                var t = TerritoryRuntime.Current;
                if (t != null && t.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.EWPSpawner))
                {
                    return t.CustomSpawnerNames != null && t.CustomSpawnerNames.Contains(spawn.m_name);
                }
                // Outside any custom-spawner territory: reject KG-style marker names so EWP-only spawners
                // don't fire ambient creatures in regular biomes.
                return spawn.m_name == null || !spawn.m_name.Contains("MARKETPLACESPAWNER");
            }
        }
    }
}
