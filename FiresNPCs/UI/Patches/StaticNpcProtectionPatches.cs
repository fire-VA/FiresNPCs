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
            bool pass = PassDamage(__instance, hit);
            // when this prefix BLOCKS
            // the hit, the damage never reaches Character.RPC_Damage, where Core 0.2.681 tells the NPC who hit it. A blocked NPC with
            // "Defend when attacked" ON (ZDO npc_defend) is told here instead - players included - so an unkillable fixture still fights back.
            // Defend OFF: unchanged (the monster-only alert below for wander/patrol bodies).
            if (!pass && IsDefendOn(__instance))
            {
                var attacker = hit != null ? hit.GetAttacker() : null;
                var module = __instance.GetComponent<global::FiresCore.Npc.NpcMode.CompanionNpcModule>();
                if (attacker != null && attacker != __instance && module != null)
                {
                    module.OnDirectlyAttacked(attacker);
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[StaticNpc] '{__instance.m_name}' hit by {attacker.m_name} while unkillable: Core told (Defend when attacked ON)");
                }
            }
            return pass;
        }

        private static bool IsDefendOn(Character c)
        {
            var nview = c.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null && zdo.GetBool("npc_defend", false);
        }

        private static bool PassDamage(Character __instance, HitData hit)
        {
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
            // an Invincible toggle the admin SET in the NPC book (ZDO npc_invincible_set, synced) decides
            // alone, live on every machine: ON was blocked above, OFF = normal damage. Never set -> the default below, unchanged.
            if (IsInvincibleSet(__instance)) return true;
            // Never set (behaviour): STATIC-stage bodies (no idle wander, no patrol route) are unconditional fixtures —
            // immune to combat and the K/killall/killenemies debug commands (all route through Character.Damage) regardless of
            // the master config. Wander/patrol bodies are blocked while StaticNpcInvulnerable is on (its default).
            if (StaticNpcProtection.IsStaticStage(__instance)) return false;
            bool invulnerable = false;
            try { invulnerable = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configStaticNpcInvulnerable?.Value ?? false; }
            catch { }
            return !invulnerable; // invulnerable → skip Damage (block); otherwise let it through (killable)
        }

        /// <summary>(option B): true when an admin set this NPC's Invincible toggle in the NPC book (ZDO npc_invincible_set;
        /// the synced ZDO first, the controller field only as fallback). Migration/defaults never write it.</summary>
        internal static bool IsInvincibleSet(Character c)
        {
            var nview = c.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null) return zdo.GetBool("npc_invincible_set", false);
            var npc = c.GetComponent<global::FiresRPGmaker.UI.NpcController>();
            return npc != null && npc.infiniteHealthSet;
        }

        /// <summary>True if this NPC's per-NPC infinite-health flag is set. Reads the ZDO bool (reliable on
        /// every machine from the first frame) with the NpcController as a fast fallback.</summary>
        private static bool IsInfiniteHealth(Character c)
        {
            // the synced ZDO is authoritative when present (toggles apply live on every machine); the controller field,
            // loaded once from the ZDO on this peer, is only the fallback. Before, a stale controller "true" OR'ed in kept blocking.
            var nview = c.GetComponent<ZNetView>();
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null) return zdo.GetBool("npc_infinite_health", false);
            var npc = c.GetComponent<global::FiresRPGmaker.UI.NpcController>();
            return npc != null && npc.infiniteHealth;
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
                {
                    // a body made this session (birth-stamped) that is removed before its anchor exists
                    // left its SavedNPCs record on the cell for the next NPC there. Route the same full delete for it; any other
                    // anchorless body is left alone exactly as before.
                    if (zdo.GetLong(global::FiresRPGmaker.UI.NpcController.BornTicksKey, 0L) != 0L && !DyingUids.Remove(zdo.m_uid))
                    {
                        Vector3 cell = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", go.transform.position));
                        global::FiresRPGmaker.FiresLogger.LogDebug($"[StaticNpc] removal of a new body before its anchor existed ({go.name} @ {cell}) - dropping its saved record");
                        SuppressRouting = true;
                        try { global::FiresRPGmaker.Managers.SavedNpcManager.AdminDeletePlacedNpc(cell, null); }
                        finally { SuppressRouting = false; }
                    }
                    return;   // not an anchor-owned body
                }
                if (DyingUids.Remove(zdo.m_uid)) return;   // vanilla death — keeper owns the respawn

                Vector3 anchor = zdo.GetVec3("npc_placed_anchor", zdo.GetVec3("npc_stationed_pos", go.transform.position));
                global::FiresRPGmaker.FiresLogger.LogDebug($"[StaticNpc] deliberate external removal of anchored body ({go.name} @ {anchor}, guid {anchorGuid}) — routing full NPC delete");
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
            if (ZNet.instance == null) return;
            if (!StaticNpcProtection.IsStaticNpc(__instance)) return;
            // MP-2b (rpg_npc_fire_list 11: a killed NPC was DELETED, "deliberate external removal ... routing full NPC delete"): the death's
            // ZNetScene.Destroy runs on the body's OWNER, which is a client whenever one opened its Dressing room (claims it) or owns
            // the zone. This used to return on clients BEFORE marking the death, so the catch-all there read the death as an admin delete.
            // The death is marked on every machine; the keeper notify / respawn bookkeeping below stay server-only.
            try
            {
                var nv0 = __instance.GetComponent<ZNetView>();
                var z0 = nv0 != null && nv0.IsValid() ? nv0.GetZDO() : null;
                if (z0 != null && !string.IsNullOrEmpty(z0.GetString(global::FiresCore.Npc.Anchor.NpcAnchorFields.BodyGuidBackRef, "")))
                    StaticNpc_DeliberateRemoveCatchAll.DyingUids.Add(z0.m_uid);
            }
            catch { }
            if (!ZNet.instance.IsServer()) return;

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
        private static readonly int CharacterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "character_noenv");
        private static void Gate(string why) => global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[StaticNpc] remove gate: " + why);

        private static bool Prefix(Player __instance, ref bool __result)
        {
            var cam = GameCamera.instance;
            if (cam == null) return true;

            // (R-C1, dedi: "gate line MISSING", the NPC stayed): vanilla's m_removeRayMask has no character layers, so the ray
            // went THROUGH the NPC's own colliders, hit the ground behind it, found no Piece and fell back to vanilla (which can't remove
            // a Character). A second ray on the character layers finds the NPC; it wins when it is the nearer hit. Every exit logs at
            // Debug, so a miss names its reason.
            int mask = Traverse.Create(__instance).Field("m_removeRayMask").GetValue<int>();
            bool hitPiece = Physics.Raycast(cam.transform.position, cam.transform.forward, out var hitInfo, 50f, mask);
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out var charHit, 50f, CharacterMask)
                && (!hitPiece || charHit.distance < hitInfo.distance)
                && charHit.collider != null && charHit.collider.GetComponentInParent<Character>() is Character hc && StaticNpcProtection.IsStaticNpc(hc))
            { hitInfo = charHit; hitPiece = true; }
            if (!hitPiece) { Gate("no hit within 50 m"); return true; }

            var eye = Traverse.Create(__instance).Field("m_eye").GetValue<Transform>();
            if (eye != null && Vector3.Distance(hitInfo.point, eye.position) >= __instance.m_maxPlaceDistance)
            { Gate($"hit '{hitInfo.collider?.name}' {Vector3.Distance(hitInfo.point, eye.position):F1} m away (max {__instance.m_maxPlaceDistance:F1})"); return true; }

            var piece = hitInfo.collider != null ? hitInfo.collider.GetComponentInParent<Piece>() : null;
            if (piece == null) { Gate($"hit '{hitInfo.collider?.name}' (layer {LayerMask.LayerToName(hitInfo.collider != null ? hitInfo.collider.gameObject.layer : 0)}): no Piece"); return true; }

            var character = piece.GetComponent<Character>();
            if (character == null || !StaticNpcProtection.IsStaticNpc(character))
            { Gate($"piece '{piece.name}' is {(character == null ? "not a Character" : "not a static NPC")}: vanilla removal"); return true; } // not a static NPC — let vanilla handle normal piece removal

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
                // the anchor orb of THIS npc goes at once, not on a zone reload.
                bool orb = !string.IsNullOrEmpty(anchorGuid) && global::FiresRPGmaker.UI.NpcAnchorOrbViz.ActiveGuid == anchorGuid;
                if (orb) global::FiresRPGmaker.UI.NpcAnchorOrbViz.Hide();
                global::FiresRPGmaker.FiresLogger.LogDebug($"[StaticNpc] hammer removed npc '{character.m_name}' and anchor {(string.IsNullOrEmpty(anchorGuid) ? "<cell match>" : anchorGuid)}{(orb ? " (+orb)" : "")}");
                __instance.Message(MessageHud.MessageType.Center, "Removed static NPC.");
                __result = true;
            }
            else
            {
                __result = false;
            }
            return false; // skip vanilla
        }
        // one line
        // per refused removal naming what refused it - the piece's own removal fields, vanilla's checks and every patch on RemovePiece.
        private static float s_lastRefusal;
        private static bool SafeCanBeRemoved(Piece p) { try { return p.CanBeRemoved(); } catch { return true; } }
        private static void Postfix(Player __instance, bool __result)
        {
            if (__result || Time.unscaledTime - s_lastRefusal < 0.5f) return;
            try
            {
                var cam = GameCamera.instance;
                if (cam == null) return;
                int mask = Traverse.Create(__instance).Field("m_removeRayMask").GetValue<int>();
                if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 50f, mask)) return;
                var piece = hit.collider != null ? hit.collider.GetComponentInParent<Piece>() : null;
                if (piece == null || piece.GetComponent<Character>() != null) return;
                s_lastRefusal = Time.unscaledTime;
                var nview = piece.GetComponent<ZNetView>();
                string station = piece.m_craftingStation == null ? "none"
                    : $"'{piece.m_craftingStation.m_name}' in range {CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, hit.point) != null}";
                bool ward = PrivateArea.CheckAccess(hit.point, 0f, false, false);
                bool canRemove = true;
                try { canRemove = (bool)AccessTools.Method(typeof(Player), "CheckCanRemovePiece").Invoke(__instance, new object[] { piece }); } catch { }
                string owners = "";
                try
                {
                    var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(Player), "RemovePiece"));
                    if (info != null) owners = string.Join(", ", System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(info.Prefixes, p => p.owner + ":" + p.PatchMethod.DeclaringType?.Name)));
                }
                catch { }
                global::FiresRPGmaker.FiresLogger.LogDebug($"[RemovePiece] REFUSED '{piece.name}' at {hit.distance:0.0} m: m_canBeRemoved {piece.m_canBeRemoved}, CheckCanRemovePiece {canRemove}, piece.CanBeRemoved {SafeCanBeRemoved(piece)}, no-build location {Location.IsInsideNoBuildLocation(piece.transform.position)}, station {station}, ward access {ward}, creator {piece.GetCreator()} (me {piece.IsCreator()}), placed by player {piece.IsPlacedByPlayer()}, WearNTear {(piece.GetComponent<WearNTear>() != null)}, ZNetView {(nview != null && nview.IsValid() ? (nview.IsOwner() ? "valid, owner" : "valid, not owner") : "INVALID")}, collider '{hit.collider.name}' layer {LayerMask.LayerToName(hit.collider.gameObject.layer)}; RemovePiece prefixes: {owners}");
            }
            catch (System.Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[RemovePiece] refusal probe threw: " + ex.Message); }
        }
    }
}
