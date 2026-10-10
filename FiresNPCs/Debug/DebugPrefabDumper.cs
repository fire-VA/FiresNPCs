using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using BepInEx;

namespace FiresRPGmaker
{
    /// <summary>
    /// Debug utility to dump all loaded prefab IDs and their localization names to a text file.
    /// 
    /// USAGE:
    ///   1. Open console (~)
    ///   2. Type: dumpprefabs [filter]
    ///   3. File is saved to BepInEx/config/FiresRPGmaker/Debug/PrefabLocalizationDump.txt
    /// 
    /// Output format per line:
    ///   PrefabName: $localization_key -> Localized Name
    /// </summary>
    public static class DebugPrefabDumper
    {
        private static bool _registered = false;

        public static void RegisterCommands()
        {
            if (_registered) return;

            new Terminal.ConsoleCommand("dumpprefabs",
                "Dumps all loaded prefab IDs and their localization names to a text file. Usage: dumpprefabs [filter]",
                (Terminal.ConsoleEventArgs args) =>
                {
                    try
                    {
                        string filter = (args.Args != null && args.Args.Length > 1)
                            ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim()
                            : "";
                        DumpPrefabLocalizations(args, filter);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DebugPrefabDumper] dumpprefabs exception: {ex}");
                        args.Context.AddString($"[FiresRPGmaker] dumpprefabs error: {ex.Message}");
                    }
                });

            new Terminal.ConsoleCommand("dumpitems",
                "Dumps all prefabs with a valid ItemDrop component to a text file. Usage: dumpitems [filter]",
                (Terminal.ConsoleEventArgs args) =>
                {
                    try
                    {
                        string filter = (args.Args != null && args.Args.Length > 1)
                            ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim()
                            : "";
                        DumpItemDropPrefabs(args, filter);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[DebugPrefabDumper] dumpitems exception: {ex}");
                        args.Context.AddString($"[FiresRPGmaker] dumpitems error: {ex.Message}");
                    }
                });

            _registered = true;
            Debug.Log("[DebugPrefabDumper] Console commands registered: dumpprefabs, dumpitems");
        }

        private static void DumpPrefabLocalizations(Terminal.ConsoleEventArgs args, string filter)
        {
            if (ZNetScene.instance == null)
            {
                args.Context.AddString("[FiresRPGmaker] ZNetScene not available - must be in a world");
                return;
            }

            var allPrefabs = ZNetScene.instance.m_prefabs;
            if (allPrefabs == null || allPrefabs.Count == 0)
            {
                args.Context.AddString("[FiresRPGmaker] No prefabs loaded");
                return;
            }

            args.Context.AddString($"[FiresRPGmaker] Scanning {allPrefabs.Count} prefabs...");

            bool hasFilter = !string.IsNullOrEmpty(filter);
            string filterLower = hasFilter ? filter.ToLowerInvariant() : "";

            var entries = new List<PrefabEntry>();

            foreach (var prefab in allPrefabs)
            {
                if (prefab == null) continue;

                string prefabName = prefab.name;
                if (string.IsNullOrEmpty(prefabName)) continue;

                string locKey = null;
                string localized = null;
                string category = "Other";

                // Try ItemDrop (items, weapons, armor, resources, etc.)
                var itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop != null && itemDrop.m_itemData?.m_shared != null)
                {
                    locKey = itemDrop.m_itemData.m_shared.m_name;
                    category = "Item";
                }

                // Try Piece (building pieces)
                if (locKey == null)
                {
                    var piece = prefab.GetComponent<Piece>();
                    if (piece != null && !string.IsNullOrEmpty(piece.m_name))
                    {
                        locKey = piece.m_name;
                        category = "Piece";
                    }
                }

                // Try Character / Humanoid (creatures, NPCs)
                if (locKey == null)
                {
                    var humanoid = prefab.GetComponent<Humanoid>();
                    if (humanoid != null && !string.IsNullOrEmpty(humanoid.m_name))
                    {
                        locKey = humanoid.m_name;
                        category = "Creature";
                    }
                    else
                    {
                        var character = prefab.GetComponent<Character>();
                        if (character != null && !string.IsNullOrEmpty(character.m_name))
                        {
                            locKey = character.m_name;
                            category = "Creature";
                        }
                    }
                }

                // Try Pickable
                if (locKey == null)
                {
                    var pickable = prefab.GetComponent<Pickable>();
                    if (pickable != null && pickable.m_itemPrefab != null)
                    {
                        var pickItemDrop = pickable.m_itemPrefab.GetComponent<ItemDrop>();
                        if (pickItemDrop != null && pickItemDrop.m_itemData?.m_shared != null)
                        {
                            locKey = pickItemDrop.m_itemData.m_shared.m_name;
                            category = "Pickable";
                        }
                    }
                }

                // Try MineRock / MineRock5
                if (locKey == null)
                {
                    var mineRock = prefab.GetComponent<MineRock>();
                    if (mineRock != null && !string.IsNullOrEmpty(mineRock.m_name))
                    {
                        locKey = mineRock.m_name;
                        category = "MineRock";
                    }
                    else
                    {
                        var mineRock5 = prefab.GetComponent<MineRock5>();
                        if (mineRock5 != null && !string.IsNullOrEmpty(mineRock5.m_name))
                        {
                            locKey = mineRock5.m_name;
                            category = "MineRock";
                        }
                    }
                }

                // Try TreeBase
                if (locKey == null)
                {
                    var treeBase = prefab.GetComponent<TreeBase>();
                    if (treeBase != null)
                    {
                        // TreeBase doesn't have m_name, use Destructible or HoverText if available
                        var hoverable = prefab.GetComponent<HoverText>();
                        if (hoverable != null && !string.IsNullOrEmpty(hoverable.m_text))
                        {
                            locKey = hoverable.m_text;
                        }
                        category = "Tree";
                    }
                }

                // Try Location
                if (locKey == null)
                {
                    var location = prefab.GetComponent<Location>();
                    if (location != null)
                    {
                        category = "Location";
                    }
                }

                // Categorize VFX/SFX/FX
                if (locKey == null)
                {
                    string lower = prefabName.ToLowerInvariant();
                    if (lower.StartsWith("vfx_")) category = "VFX";
                    else if (lower.StartsWith("sfx_")) category = "SFX";
                    else if (lower.StartsWith("fx_")) category = "FX";
                }

                // Localize the key if we found one
                if (!string.IsNullOrEmpty(locKey))
                {
                    if (locKey.StartsWith("$") && Localization.instance != null)
                    {
                        localized = Localization.instance.Localize(locKey);
                        if (localized == locKey) localized = null; // Localize returns the key if not found
                    }
                    else if (!locKey.StartsWith("$"))
                    {
                        localized = locKey;
                        locKey = null; // Not a localization key, it's a direct name
                    }
                }

                // Apply filter
                if (hasFilter)
                {
                    bool matches = prefabName.ToLowerInvariant().Contains(filterLower);
                    if (!matches && locKey != null) matches = locKey.ToLowerInvariant().Contains(filterLower);
                    if (!matches && localized != null) matches = localized.ToLowerInvariant().Contains(filterLower);
                    if (!matches && category.ToLowerInvariant().Contains(filterLower)) matches = true;
                    if (!matches) continue;
                }

                entries.Add(new PrefabEntry
                {
                    PrefabName = prefabName,
                    LocalizationKey = locKey,
                    LocalizedName = localized,
                    Category = category
                });
            }

            // Sort alphabetically by prefab name
            entries = entries.OrderBy(e => e.PrefabName, StringComparer.OrdinalIgnoreCase).ToList();

            // Build output
            var sb = new StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM PREFAB LOCALIZATION DUMP");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Prefabs Scanned: {allPrefabs.Count}");
            sb.AppendLine($"Entries in Dump: {entries.Count}");
            if (hasFilter) sb.AppendLine($"Filter: {filter}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Summary by category
            sb.AppendLine("=== SUMMARY BY CATEGORY ===");
            sb.AppendLine();
            foreach (var group in entries.GroupBy(e => e.Category).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  {group.Key}: {group.Count()}");
            }
            sb.AppendLine();

            // Full list
            sb.AppendLine("=== ALL PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("Format: PrefabName: $localization_key -> Localized Name [Category]");
            sb.AppendLine();

            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.LocalizationKey) && !string.IsNullOrEmpty(entry.LocalizedName))
                {
                    sb.AppendLine($"{entry.PrefabName}: {entry.LocalizationKey} -> {entry.LocalizedName} [{entry.Category}]");
                }
                else if (!string.IsNullOrEmpty(entry.LocalizedName))
                {
                    sb.AppendLine($"{entry.PrefabName}: {entry.LocalizedName} [{entry.Category}]");
                }
                else if (!string.IsNullOrEmpty(entry.LocalizationKey))
                {
                    sb.AppendLine($"{entry.PrefabName}: {entry.LocalizationKey} (no localization) [{entry.Category}]");
                }
                else
                {
                    sb.AppendLine($"{entry.PrefabName}: (no name) [{entry.Category}]");
                }
            }
            sb.AppendLine();

            // Categorized sections
            foreach (var group in entries.GroupBy(e => e.Category).OrderBy(g => g.Key))
            {
                sb.AppendLine($"=== {group.Key.ToUpper()} ({group.Count()}) ===");
                sb.AppendLine();
                foreach (var entry in group.OrderBy(e => e.PrefabName))
                {
                    string name = entry.LocalizedName ?? entry.LocalizationKey ?? "(no name)";
                    sb.AppendLine($"  {entry.PrefabName}: {name}");
                }
                sb.AppendLine();
            }

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "PrefabLocalizationDump.txt");

            File.WriteAllText(outputPath, sb.ToString());

            int withNames = entries.Count(e => !string.IsNullOrEmpty(e.LocalizedName) || !string.IsNullOrEmpty(e.LocalizationKey));
            args.Context.AddString($"[FiresRPGmaker] Dumped {entries.Count} prefabs ({withNames} with names)");
            args.Context.AddString($"[FiresRPGmaker] Saved to: {outputPath}");

            // Show first few in console
            int shown = 0;
            foreach (var entry in entries.Where(e => !string.IsNullOrEmpty(e.LocalizedName)).Take(15))
            {
                string key = entry.LocalizationKey != null ? $"{entry.LocalizationKey} -> " : "";
                args.Context.AddString($"  {entry.PrefabName}: {key}{entry.LocalizedName}");
                shown++;
            }
            if (entries.Count > shown)
            {
                args.Context.AddString($"  ... and {entries.Count - shown} more (see file for full list)");
            }
        }

        private static void DumpItemDropPrefabs(Terminal.ConsoleEventArgs args, string filter)
        {
            if (ZNetScene.instance == null)
            {
                args.Context.AddString("[FiresRPGmaker] ZNetScene not available - must be in a world");
                return;
            }

            var allPrefabs = ZNetScene.instance.m_prefabs;
            if (allPrefabs == null || allPrefabs.Count == 0)
            {
                args.Context.AddString("[FiresRPGmaker] No prefabs loaded");
                return;
            }

            bool hasFilter = !string.IsNullOrEmpty(filter);
            string filterLower = hasFilter ? filter.ToLowerInvariant() : "";

            var items = new List<KeyValuePair<string, string>>();

            foreach (var prefab in allPrefabs)
            {
                if (prefab == null) continue;

                var itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop == null || itemDrop.m_itemData?.m_shared == null) continue;

                string prefabName = prefab.name;
                string sharedName = itemDrop.m_itemData.m_shared.m_name ?? "";

                if (hasFilter)
                {
                    bool matches = prefabName.ToLowerInvariant().Contains(filterLower)
                        || sharedName.ToLowerInvariant().Contains(filterLower);
                    if (!matches) continue;
                }

                items.Add(new KeyValuePair<string, string>(prefabName, sharedName));
            }

            items = items.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"# Item Prefab Dump - {items.Count} items");
            sb.AppendLine($"# Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (hasFilter) sb.AppendLine($"# Filter: {filter}");
            sb.AppendLine();

            foreach (var kv in items)
            {
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            }

            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "ItemPrefabDump.txt");
            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Dumped {items.Count} item prefabs");
            args.Context.AddString($"[FiresRPGmaker] Saved to: {outputPath}");

            int shown = 0;
            foreach (var kv in items.Take(15))
            {
                args.Context.AddString($"  {kv.Key}: {kv.Value}");
                shown++;
            }
            if (items.Count > shown)
            {
                args.Context.AddString($"  ... and {items.Count - shown} more (see file for full list)");
            }
        }

        private class PrefabEntry
        {
            public string PrefabName;
            public string LocalizationKey;
            public string LocalizedName;
            public string Category;
        }
    }
}
