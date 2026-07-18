using UnityEngine;
using FiresCore.Npc.Anchor;

namespace FiresRPGmaker.Integration
{
    /// <summary>
    /// RPGMaker's provider for <see cref="FiresCore.Bridge.NpcAnchorBridge"/> — the anchor⇄body
    /// payload copier. The anchor ZDO mirrors every field the body needs (appearance + module
    /// profiles + config), using the SAME field names, so a body seeded from its anchor is
    /// indistinguishable from the original: NpcController.LoadFromZDO / CompanionNpcModule /
    /// StaticNpcInitializer all read these fields directly.
    ///
    /// Field manifest = the union of NpcController.SaveToZDO, CompanionNpcModule.SaveToZDO's
    /// wander fields, and the appearance replay in the legacy StaticNpcRespawnManager.Respawn.
    /// </summary>
    internal static class NpcAnchorDataBridge
    {
        internal static void Register()
        {
            global::FiresCore.Bridge.NpcAnchorBridge.SeedBody = (anchorZdo, bodyZdo) => Copy(anchorZdo, bodyZdo);
            global::FiresCore.Bridge.NpcAnchorBridge.CaptureToAnchor = (bodyZdo, anchorZdo) => Copy(bodyZdo, anchorZdo);
        }

        private static readonly string[] StringFields =
        {
            // identity + appearance
            "npc_profile", "npc_name_override", "npc_hair", "npc_beard", "npc_model_override", "npc_fashion_json",
            // module profiles
            "npc_dialogue_profile", "npc_quest_profile", "npc_info_profile", "npc_trader_profile",
            "npc_banker_profile", "npc_gambler_profile", "npc_marketplace_profile", "npc_achievement_profile",
            "npc_leaderboard_profile", "npc_buffer_profile", "npc_mail_profile", "npc_transmog_profile",
            "npc_teleporter_profile",
            // mode / patrol
            "npc_patrol_route", "npc_patrol_preset",
            // equipment (visual prefab names + full serialized hand items)
            "npc_equip_helmet", "npc_equip_chest", "npc_equip_legs", "npc_equip_shoulder", "npc_equip_utility",
            "npc_equip_righthand", "npc_equip_lefthand", "npc_equip_rightback", "npc_equip_leftback",
            "npc_equip_righthand_data", "npc_equip_lefthand_data",
        };

        private static readonly string[] BoolFields =
        {
            "npc_allow_idle_wander", "npc_infinite_health", "npc_no_face_player", "npc_exact_placement",
            "npc_name_hidden", "npc_isfemale", "npc_use_territory_bounds",
        };

        private static readonly string[] FloatFields =
        {
            "npc_max_health", "npc_respawn_delay", "npc_text_size", "npc_text_height", "npc_scale",
            "npc_idle_wander_radius", "npc_wander_buffer_zone",
        };

        private static readonly string[] IntFields =
        {
            "npc_type",
        };

        private static void Copy(ZDO from, ZDO to)
        {
            if (from == null || to == null) return;

            foreach (var f in StringFields) to.Set(f, from.GetString(f, ""));
            foreach (var f in BoolFields) to.Set(f, from.GetBool(f, false));
            foreach (var f in FloatFields) to.Set(f, from.GetFloat(f, 0f));
            foreach (var f in IntFields) to.Set(f, from.GetInt(f, 0));

            // Vanilla appearance vectors (hashed ZDOVars) + our eye color.
            to.Set(ZDOVars.s_hairColor, from.GetVec3(ZDOVars.s_hairColor, Vector3.one));
            to.Set(ZDOVars.s_skinColor, from.GetVec3(ZDOVars.s_skinColor, Vector3.one));
            to.Set("npc_eyeColor", from.GetVec3("npc_eyeColor", new Vector3(0.4f, 0.3f, 0.2f)));
            to.Set(ZDOVars.s_modelIndex, from.GetInt(ZDOVars.s_modelIndex, 0));

            // Vanilla VisEquipment item vars — the owner-side visual sync READS these; a seeded body
            // without them looked right at spawn (our npc_equip_* path dressed it) and then got
            // STRIPPED the moment the full companion visual path re-ran (e.g. when wandering starts)
            // because every vanilla hash read back 0. Mirror them so both wardrobes agree.
            foreach (var f in VanillaItemHashFields) to.Set(f, from.GetInt(f, 0));
        }

        // All int-typed in this game build (hair/beard included — stored as name hashes).
        private static readonly int[] VanillaItemHashFields =
        {
            ZDOVars.s_leftItem, ZDOVars.s_leftItemVariant, ZDOVars.s_rightItem,
            ZDOVars.s_leftBackItem, ZDOVars.s_leftBackItemVariant, ZDOVars.s_rightBackItem,
            ZDOVars.s_chestItem, ZDOVars.s_legItem, ZDOVars.s_helmetItem,
            ZDOVars.s_shoulderItem, ZDOVars.s_shoulderItemVariant,
            ZDOVars.s_utilityItem, ZDOVars.s_trinketItem,
            ZDOVars.s_beardItem, ZDOVars.s_hairItem,
        };
    }
}
