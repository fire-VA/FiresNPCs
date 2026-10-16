using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Factions module entry point (foundation phase): seeds an example config, warms the table, and
    /// registers the self-service console commands. Definitions live in the server-synced
    /// FiresRPGmaker/Factions/ folder (+ Marketplace drop-in); membership rides character customData.
    /// Enforcement (skill bonus, friendly monsters, same-faction PvP, restrictions) layers on in the
    /// enforcement phase — this phase makes factions assignable, queryable, and visible on nameplates.
    /// </summary>
    public static class FactionModule
    {
        private static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                SeedExample();
                FactionManager.Reload();
                Debug.Log("[Factions] module initialized");
            }
            catch (System.Exception ex) { Debug.LogError($"[Factions] init failed: {ex}"); }
        }

        private static void SeedExample()
        {
            string folder = FiresCore.Storage.FiresConfigPaths.GroupedPath("Factions");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "example.yml");
            if (File.Exists(path)) return;
            File.WriteAllText(path, string.Join("\n", new[]
            {
                "# Fires Factions — one entry per faction. Key (lowercased) is the id used by",
                "# SetFaction / HasFaction and stored on the player. Drop MP's Factions.yml into",
                "# BepInEx/config/Marketplace/Configs/ and it loads here unchanged.",
                "",
                "bandits:",
                "  Name: \"The Bandits\"",
                "  Icon: \"\"",
                "  CanDamageSameFaction: false",
                "  BonusSkills:",
                "    Swords: 10",
                "    Sneak: 5",
                "  FriendlyMonsters:",
                "    - Greyling",
                "    - Greydwarf",
                "",
                "merchants:",
                "  Name: \"The Merchant Guild\"",
                "  CanDamageSameFaction: false",
                "  BonusSkills:",
                "    Run: 8",
            }));
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommands
        {
            private static bool _registered;

            [HarmonyPostfix]
            private static void Register()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("faction",
                    "<id|none> - join a faction (or leave). 'faction' with no arg prints your current one.",
                    args =>
                    {
                        if (args.Length < 2)
                        {
                            var f = FactionMembership.LocalFaction();
                            args.Context.AddString(f != null ? "[faction] you are in: " + f.Name : "[faction] you are in no faction.");
                            return;
                        }
                        string id = args[1].Trim();
                        if (id.Equals("none", System.StringComparison.OrdinalIgnoreCase) || id.Equals("clear", System.StringComparison.OrdinalIgnoreCase))
                        { FactionMembership.ClearLocal(); args.Context.AddString("[faction] left your faction."); return; }
                        FactionMembership.SetLocal(id);
                    },
                    optionsFetcher: () => new List<string>(FactionManager.Ids) { "none" });

                new Terminal.ConsoleCommand("factionlist", "- list all loaded factions", args =>
                {
                    var ids = new List<string>(FactionManager.Ids);
                    if (ids.Count == 0) { args.Context.AddString("[faction] no factions loaded (add FiresRPGmaker/Factions/*.yml)."); return; }
                    foreach (var id in ids)
                    {
                        var f = FactionManager.Get(id);
                        args.Context.AddString($"  {id}  —  {f?.Name}");
                    }
                });

                new Terminal.ConsoleCommand("factionreload", "- reload faction definitions from disk", args =>
                {
                    FactionManager.Reload();
                    args.Context.AddString($"[faction] reloaded ({new List<string>(FactionManager.Ids).Count} faction(s)).");
                });

                new Terminal.ConsoleCommand("factioneditor", "- open the admin faction editor window", args =>
                {
                    if (!FiresCore.Sync.AdminSyncing.IsLocalAdmin())
                    { args.Context.AddString("[faction] admin only."); FiresCore.Logging.FiresLogger.LogDebug("[faction] factioneditor refused: not admin (AdminSyncing.IsLocalAdmin false)"); return; }
                    FactionEditorPanel.Toggle();
                });
            }
        }
    }
}
