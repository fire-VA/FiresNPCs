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
    }

    /// <summary>Optional invulnerability — when the StaticNpcInvulnerable config toggle is on, static
    /// NPCs take no damage at all (combat, K/killall, everything). When off (default) damage passes and
    /// the death→respawn flow handles it. Admins flip the toggle to decide whether they're killable.</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class StaticNpc_OptionalInvulnerability
    {
        private static bool Prefix(Character __instance)
        {
            if (!StaticNpcProtection.IsStaticNpc(__instance)) return true;
            bool invulnerable = false;
            try { invulnerable = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configStaticNpcInvulnerable?.Value ?? false; }
            catch { }
            return !invulnerable; // invulnerable → skip Damage (block); otherwise let it through (killable)
        }
    }

    /// <summary>On a static NPC's death, snapshots it and schedules a respawn at its placed position.
    /// Runs only on the server (it owns the ownerless NPC ZDOs); returns true so vanilla death/destroy
    /// proceeds normally (the NPC really dies — it just comes back 60s later). Never fires while the
    /// invulnerability toggle is on, since the NPC can't reach 0 health.</summary>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class StaticNpc_DeathRespawn
    {
        private static void Prefix(Character __instance)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            if (!StaticNpcProtection.IsStaticNpc(__instance)) return;
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

            bool isAdmin = ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost();
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
                nview.ClaimOwnership();
                ZNetScene.instance.Destroy(character.gameObject);
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
