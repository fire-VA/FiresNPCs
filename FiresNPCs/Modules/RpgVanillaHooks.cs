using HarmonyLib;

namespace FiresRPGmaker.Modules
{
    /// <summary>
    /// ONE Harmony patch class per vanilla method that more than one RPG Maker module needs. Each hook calls the
    /// modules in a fixed order; modules expose plain methods and never patch these methods themselves.
    /// </summary>
    internal static class RpgVanillaHooks
    {
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        internal static class Character_OnDeath
        {
            [HarmonyPrefix]
            private static void Prefix(Character __instance)
                => UI.Patches.StaticNpc_DeathRespawn.OnCharacterDeath(__instance);

            [HarmonyPostfix]
            private static void Postfix(Character __instance)
                => Marketplace.LeaderboardFeedPatches.DeathFeed.OnCharacterDeath(__instance);
        }

        // Player overrides OnDeath without calling base, so a Character.OnDeath hook never sees a player die.
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        internal static class Player_OnDeath
        {
            [HarmonyPrefix]
            private static void Prefix(Player __instance)
                => Territories.TerritoryRuntime.OnPlayerDeathPrefix(__instance);

            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                Quest.QuestLifecyclePatches.OnLocalPlayerDeath(__instance);
                Marketplace.LeaderboardFeedPatches.DeathFeed.OnLocalPlayerDeath(__instance);
            }
        }
    }
}
