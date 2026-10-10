using System;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using FiresRPGmaker;
using CompendiumCore = FiresCore.Help.Compendium;
using CompendiumEntry = FiresCore.Help.CompendiumEntry;

namespace FiresRPGmaker.Modules.Compendium
{
    // FiresRPGmaker's compendium facade. The registry, discovery, and known-texts injection live in
    // FiresCore.Help.Compendium; this class owns the mod-specific pieces: the category names, the "Verdant"
    // grouping label, the in-game content (CompendiumEntries), the respawn write-guard wiring, the discovery
    // notification flourish, and the console commands. Admin documentation lives in the Help panel, not here.
    public static class CompendiumManager
    {
        public const string ModLabel = "Verdant";

        public const string CATEGORY_BASICS = "Companion Basics";
        public const string CATEGORY_CONTROLS = "Controls";
        public const string CATEGORY_ARCHETYPES = "Archetypes";
        public const string CATEGORY_HYBRIDS = "Hybrid Classes";
        public const string CATEGORY_ABILITIES = "Abilities";

        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;

            CompendiumCore.SuppressDiscovery = FiresCore.Bridge.CompanionBridge.AreTeleportsSuppressed;
            CompendiumCore.OnDiscovered = OnEntryDiscovered;

            CompendiumEntries.RegisterBasicsEntries();
            CompendiumEntries.RegisterArchetypeEntries();
            CompendiumEntries.RegisterHybridEntries();
            CompendiumEntries.RegisterAbilityEntries();

            _initialized = true;
            Debug.Log($"[CompendiumManager] Initialized with {CompendiumCore.Count} entries");
        }

        // Stamps the mod label so the engine groups this mod's entries together, then registers with Core.
        public static void RegisterEntry(CompendiumEntry entry)
        {
            if (entry == null) return;
            entry.ModLabel = ModLabel;
            CompendiumCore.Register(entry);
        }

        public static void DiscoverByTrigger(string discoveryKey, Player player = null) =>
            CompendiumCore.DiscoverByTrigger(discoveryKey, player);

        public static void SyncOnPlayerSpawn(Player player)
        {
            CompendiumCore.SyncOnPlayerSpawn(player);
            Debug.Log($"[CompendiumManager] Synced {CompendiumCore.DiscoveredCount} discovered entries");
        }

        private static void OnEntryDiscovered(CompendiumEntry entry, Player player)
        {
            if (entry.Category == CATEGORY_BASICS)
                player.Message(MessageHud.MessageType.Center,
                    $"New Compendium Entry: {entry.Title}\n<size=14>Check your Texts tab in inventory</size>");
        }

        #region Console Commands

        public static bool HandleConsoleCommand(string command, string[] args)
        {
            switch (command.ToLower())
            {
                case "compendium_list":
                    ListEntries();
                    return true;

                case "compendium_unlock":
                    if (args.Length > 0)
                        Console.instance?.Print(CompendiumCore.Discover(args[0])
                            ? $"Unlocked: {args[0]}"
                            : $"Could not unlock: {args[0]} (already unlocked or not found)");
                    else
                        Console.instance?.Print("Usage: compendium_unlock <key>");
                    return true;

                case "compendium_unlock_all":
                    int unlocked = CompendiumCore.UnlockAll();
                    Console.instance?.Print($"Unlocked all compendium entries ({unlocked} new, {CompendiumCore.DiscoveredCount} total)");
                    return true;

                case "compendium_reset":
                    CompendiumCore.ResetAll();
                    Console.instance?.Print("Reset all compendium discoveries");
                    return true;

                case "compendium_discover":
                    if (args.Length > 0)
                    {
                        CompendiumCore.DiscoverByTrigger(args[0]);
                        Console.instance?.Print($"Triggered discovery: {args[0]}");
                    }
                    else
                    {
                        Console.instance?.Print("Usage: compendium_discover <trigger_key>");
                    }
                    return true;

                default:
                    return false;
            }
        }

        private static void ListEntries()
        {
            var console = Console.instance;
            if (console == null) return;

            console.Print("=== Compendium Entries ===");
            foreach (var group in CompendiumCore.All.GroupBy(e => e.Category).OrderBy(g => g.Key))
            {
                console.Print($"\n<color=orange>{group.Key}</color>");
                foreach (var entry in group.OrderBy(e => e.SortOrder).ThenBy(e => e.Title))
                {
                    string status = CompendiumCore.IsDiscovered(entry.Key) ? "[x]" : "[ ]";
                    console.Print($"  {status} {entry.Key}: {entry.Title}");
                }
            }
            console.Print($"\nTotal: {CompendiumCore.Count} entries, {CompendiumCore.DiscoveredCount} discovered");
        }

        #endregion
    }

    // Integrates the compendium with the game: syncs discovery on spawn and routes console commands.
    [HarmonyPatch]
    public static class CompendiumPatches
    {
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        public static void Player_OnSpawned_Postfix(Player __instance)
        {
            try
            {
                if (__instance != Player.m_localPlayer) return;
                CompendiumManager.Initialize();
                CompendiumManager.SyncOnPlayerSpawn(__instance);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumPatches] Player_OnSpawned error: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Terminal), "TryRunCommand")]
        [HarmonyPrefix]
        public static bool Terminal_TryRunCommand_Prefix(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return true;

                var parts = text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return true;

                string command = parts[0].ToLower();
                if (command.StartsWith("compendium_"))
                {
                    CompendiumManager.HandleConsoleCommand(command, parts.Skip(1).ToArray());
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumPatches] Terminal command error: {ex.Message}");
            }

            return true;
        }
    }
}
