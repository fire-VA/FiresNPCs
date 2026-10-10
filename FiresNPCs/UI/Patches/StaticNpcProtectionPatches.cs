using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.UI.Patches
{
    /// <summary>
    /// Behaviour rules for hammer-placed static NPCs:
    ///   • <b>Killable, but respawning.</b> A killed static NPC dies normally (combat, the K/killall debug
    ///     commands, etc.) and is re-spawned at its placed position after
    ///     <see cref="StaticNpcRespawnManager.RespawnDelaySeconds"/> — same flow companions use. The
    ///     death patch captures a snapshot and hands it to StaticNpcRespawnManager.
    ///   • <b>Admin-only removal.</b> Only admins may permanently remove a static NPC with the hammer.
    ///     Admin removal uses a direct networked destroy (ClaimOwnership + ZNetScene.Destroy), which does
    ///     NOT trigger Character.OnDeath, so it removes permanently with no respawn. Non-admins are blocked.
    /// </summary>
    internal static class StaticNpcProtection
    {
        /// <summary>True if the character is one of our hammer-placed static quest NPCs. Detected by the
        /// CompanionNpcModule.isStaticPlacement flag (reliable from first frame on every machine) with a
        /// ZDO-flag fallback ("npc_static_placement", written server-side on placement).</summary>
        internal static bool IsStaticNpc(Character c)
        {
            if (c == null) return false;

            var module = c.GetComponent<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();
            if (module != null && module.isStaticPlacement) return true;

            var nview = c.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                var zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool("npc_static_placement", false)) return true;
            }
            return false;
        }

        /// <summary>True when this static NPC is in the STATIC stage (no idle wandering, no patrol
        /// route) — a pure fixture. Reads the synced ZDO flags first (reliable on every machine),
        /// with the live module as fallback.</summary>
        internal static bool IsStaticStage(Character c)
        {
            var nview = c != null ? c.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null)
                return !zdo.GetBool("npc_allow_idle_wander", false)
                       && string.IsNullOrEmpty(zdo.GetString("npc_patrol_route", ""));

            var module = c != null ? c.GetComponent<global::FiresCore.Npc.NpcMode.CompanionNpcModule>() : null;
            if (module != null)
                return !module.allowIdleWandering
                       && c.GetComponent<global::FiresCore.Npc.Patrol.PatrolAssignment>()?.HasRoute != true;
            return true;   // unknown → safest is fixture (blocks damage)
        }
    }

    /// <summary>Optional invulnerability — when the StaticNpcInvulnerable config toggle is on, static
    /// NPCs take no damage at all (combat, K/killall, everything). When off (default) damage passes and
    /// the death→respawn flow handles it. Admins flip the toggle to decide whether they're killable.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class StaticNpc_OptionalInvulnerability
    {
        private static bool Prefix(Character __instance, HitData hit)
        {
            if (!StaticNpcProtection.IsStaticNpc(__instance)) return true;
            // Per-NPC "infinite health": block all damage before any calculation runs. Blocking here
            // means the damage RPC never reaches the owner-side alert hook, so an unkillable
            // patrol/wander body would never notice it was attacked — ping the alert directly for
            // monster attackers (monster and NPC are both server-owned on a dedi, so this runs where
            // CompanionAI reads it). Players poking an unkillable NPC get no reaction, like kg.
            if (IsInfiniteHealth(__instance))
            {
                if (!StaticNpcProtection.IsStaticStage(__instance))
                {
                    var attacker = hit != null ? hit.GetAttacker() : null;
                    if (attacker != null && !attacker.IsPlayer())
                    {
                        var module = __instance.GetComponent<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();
                        if (module != null && module.IsStationedAsNpc) module.OnDirectlyAttacked();
                    }
                }
                return false;
            }
            // STATIC-stage bodies (no idle wander, no patrol route) are unconditional fixtures —
            // immune to combat and the K/killall/killenemies debug commands (all route through
            // Character.Damage) regardless of the master config. Wander/patrol bodies are killable;
            // the anchor keeper owns their respawn.
            if (StaticNpcProtection.IsStaticStage(__instance)) return false;
            bool invulnerable = false;
            try { invulnerable = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configStaticNpcInvulnerable?.Value ?? false; }
            catch { }
            return !invulnerable; // invulnerable → skip Damage (block); otherwise let it through (killable)
        }

        /// <summary>True if this NPC's per-NPC infinite-health flag is set. Reads the ZDO bool (reliable on
        /// every machine from the first frame) with the NpcController as a fast fallback.</summary>
        private static bool IsInfiniteHealth(Character c)
        {
            var npc = c.GetComponent<global::FiresRPGmaker.UI.NpcController>();
            if (npc != null && npc.infiniteHealth) return true;
            var nview = c.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                var zdo = nview.GetZDO();
                if (zdo != null && zdo.GetBool("npc_infinite_health", false)) return true;
            }
            return false;
        }
    }

    /// <summary>Server-start hook: loads the persisted pending-respawn store for the current world so
    /// any respawns that were mid-window when the server last stopped resume their wall-clock timers,
    /// even if no fresh static-NPC death happens this session.</summary>
    [HarmonyPatch(typeof(ZNet), "Start")]
    internal static class StaticNpc_RespawnStoreServerLoad
    {
        private static void Postfix()
        {
            try { global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.EnsureServerStarted(); }
            catch (System.Exception ex) { Debug.LogError("[StaticNpc] respawn-store server load failed: " + ex.Message); }
        }
    }

    /// <summary>
    /// Deliberate-removal catch-all. Admins delete NPCs with WHATEVER tool they have in hand —
    /// the vanilla hammer routes through our RemovePiece gate, but Infinity Hammer's
    /// "remove anything" (and similar admin tools) call <see cref="ZNetScene.Destroy"/> directly,
    /// bypassing the gate: the body died, the anchor lived, and the keeper resurrected it (the
    /// original delete-resurrection bug wearing a new hat — confirmed by zero delete-flow log lines
    /// on a reported delete). ZNetScene.Destroy has exactly three caller classes for our bodies:
    /// vanilla death (marked by our OnDeath prefix), our own delete path (suppressed), and
    /// deliberate external removal — everything in the third bucket now routes the FULL
    /// admin delete (record + registry + anchor + body). Zone unload never passes through here
    /// (RemoveObjects destroys GameObjects directly), so self-heal is unaffected.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
    internal static class StaticNpc_DeliberateRemoveCatchAll
    {
        /// <summary>Set true around our own internal networked destroys (anchor delete path,
        /// RemoveGate) so they don't re-enter the delete flow.</summary>
        internal static bool SuppressRouting;

        /// <summary>ZDO uids currently inside the vanilla death flow (stamped by the OnDeath
        /// prefix on the same machine that will run the death's Destroy).</summary>
        internal static readonly System.Collections.Generic.HashSet<ZDOID> DyingUids =
            new System.Collections.Generic.HashSet<ZDOID>();

        private static void Prefix(GameObject go)
        {
            try
            {
                if (SuppressRouting || go == null) return;
                var nview = go.GetComponent<ZNetView>();
                var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                if (zdo == null) return;
                string anchorGuid = zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "");
                if (string.IsNullOrEmpty(anchorGuid))
                    return;   // not an anchor-owned body
                if (DyingUids.Remove(zdo.m_uid)) return;   // vanilla death — keeper owns the respawn

                Vector3 anchor = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", go.transform.position));
                Debug.Log($"[StaticNpc] deliberate external removal of anchored body ({go.name} @ {anchor}, guid {anchorGuid}) — routing full NPC delete");
                SuppressRouting = true;
                try { global::FiresRPGmaker.Managers.SavedNpcManager.AdminDeletePlacedNpc(anchor, anchorGuid); }
                finally { SuppressRouting = false; }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[StaticNpc] delete catch-all failed: " + ex.Message);
            }
        }
    }

    /// <summary>On a static NPC's death, snapshots it and schedules a respawn at its placed position.
    /// Runs only on the server (it owns the ownerless NPC ZDOs); returns true so vanilla death/destroy
    /// proceeds normally (the NPC really dies — it just comes back 60s later). Never fires while the
    /// invulnerability toggle is on, since the NPC can't reach 0 health.</summary>
    internal static class StaticNpc_DeathRespawn
    {
        /// <summary>Called from <see cref="Modules.RpgVanillaHooks.Character_OnDeath"/> (prefix).</summary>
        internal static void OnCharacterDeath(Character __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!StaticNpcProtection.IsStaticNpc(__instance)) return;

            // Anchor-owned body: the anchor keeper owns respawn. Stamp WHERE it died (patrol mode
            // respawns at the route checkpoint closest to the death spot), mark the uid as inside
            // the DEATH flow (so the deliberate-removal catch-all lets vanilla's ZNetScene.Destroy
            // pass), and skip the legacy pending-respawn flow entirely.
            try
            {
                var nview = __instance.GetComponent<ZNetView>();
                var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
                string anchorGuid = zdo != null ? zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "") : "";
                if (!string.IsNullOrEmpty(anchorGuid))
                {
                    StaticNpc_DeliberateRemoveCatchAll.DyingUids.Add(zdo.m_uid);
                    global::FiresCore.Npc.Anchor.NpcAnchorKeeper.NotifyBodyDeath(anchorGuid, __instance.transform.position);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[StaticNpc_DeathRespawn] anchor death notify failed: " + ex.Message);
            }

            // Legacy (un-anchored) bodies keep the old flow. When the invulnerability master switch is
            // on they are permanent fixtures: never schedule a death-respawn. (They normally can't reach
            // 0 HP through Character.Damage — this also covers non-damage kill paths like SetHealth(0).)
            try { if (global::FiresRPGmaker.Managers.ConfigManager.Instance?.configStaticNpcInvulnerable?.Value ?? true) return; }
            catch { }
            try
            {
                var snap = global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.CaptureFrom(__instance);
                if (snap != null)
                    global::FiresRPGmaker.Modules.Companions.NpcMode.StaticNpcRespawnManager.Instance.Schedule(snap);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[StaticNpc_DeathRespawn] capture/schedule failed: " + ex.Message);
            }
        }
    }

    /// <summary>Admin-gates hammer removal of static NPCs. Replicates RemovePiece's remove-ray to find the
    /// targeted Piece; if it's a static NPC, non-admins are blocked and admins remove it via a direct
    /// networked destroy. The direct destroy does NOT call Character.OnDeath, so admin removal is
    /// permanent (no respawn) — distinct from killing, which respawns.</summary>
    [HarmonyPatch(typeof(Player), "RemovePiece")]
    internal static class StaticNpc_RemoveGate
    {
        private static bool Prefix(Player __instance, ref bool __result)
        {
            var cam = GameCamera.instance;
            if (cam == null) return true;

            int mask = Traverse.Create(__instance).Field("m_removeRayMask").GetValue<int>();
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hitInfo, 50f, mask))
                return true;

            var eye = Traverse.Create(__instance).Field("m_eye").GetValue<Transform>();
            if (eye != null && Vector3.Distance(hitInfo.point, eye.position) >= __instance.m_maxPlaceDistance)
                return true;

            var piece = hitInfo.collider != null ? hitInfo.collider.GetComponentInParent<Piece>() : null;
            if (piece == null) return true;

            var character = piece.GetComponent<Character>();
            if (character == null || !StaticNpcProtection.IsStaticNpc(character))
                return true; // not a static NPC — let vanilla handle normal piece removal

            bool isAdmin = FiresCore.Sync.AdminSyncing.IsLocalAdmin();
            if (!isAdmin)
            {
                __instance.Message(MessageHud.MessageType.Center, "Only admins can remove static NPCs.");
                __result = false;
                return false; // block vanilla removal
            }

            // Admin removal — direct networked destroy. ClaimOwnership first so Destroy propagates the
            // ZDO deletion across the network (ownerless/server-authoritative NPCs need this).
            var nview = character.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && ZNetScene.instance != null)
            {
                // Deliberate admin removal: drop the desired-state record + tombstone the anchor BEFORE the
                // destroy, so the never-vanish reconciler treats this as intentional (not a bug-vanish) and
                // never resurrects it. Key off the placement anchor (npc_stationed_pos), not the live
                // transform — a patrol NPC has wandered off its placed cell.
                var zdo = nview.GetZDO();
                Vector3 anchor = zdo != null
                    ? zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", character.transform.position))
                    : character.transform.position;
                string anchorGuid = zdo != null ? zdo.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "") : "";
                try { global::FiresRPGmaker.Managers.SavedNpcManager.AdminDeletePlacedNpc(anchor, string.IsNullOrEmpty(anchorGuid) ? null : anchorGuid); }
                catch (System.Exception ex) { Debug.LogError("[StaticNpc] delete-registry failed: " + ex.Message); }

                nview.ClaimOwnership();
                // Suppress the deliberate-removal catch-all — AdminDeletePlacedNpc above already ran
                // the full delete flow; this destroy is just the local body teardown.
                StaticNpc_DeliberateRemoveCatchAll.SuppressRouting = true;
                try { ZNetScene.instance.Destroy(character.gameObject); }
                finally { StaticNpc_DeliberateRemoveCatchAll.SuppressRouting = false; }
                __instance.Message(MessageHud.MessageType.Center, "Removed static NPC.");
                __result = true;
            }
            else
            {
                __result = false;
            }
            return false; // skip vanilla
        }
    }
}
