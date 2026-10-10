using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Per-player transmog overrides keyed by ITEM PREFAB NAME (KG-style, not by slot).
    /// Each entry says "when this player wears <baseItemName>, render it as <replacedPrefab>".
    /// Persists to <c>BepInEx/config/FiresRPGmaker/Transmogrifications/_picks.csv</c>.
    /// </summary>
    public static class TransmogState
    {
        // KG's TypeColors — used by the UI to color-code each item-type category.
        public static readonly Dictionary<string, Color> TypeColors = new()
        {
            { "All",                 Color.cyan },
            { "OneHandedWeapon",     Color.yellow },
            { "TwoHandedWeapon",     new Color(1f, 0.56f, 0.03f) },
            { "TwoHandedWeaponLeft", new Color(1f, 0.46f, 0.23f) },
            { "Bow",                 Color.green },
            { "Tool",                new Color(1f, 0.33f, 0.42f) },
            { "Shield",              Color.magenta },
            { "Chest",               new Color(0.40f, 0.55f, 1f) },
            { "Helmet",              new Color(0.53f, 0.66f, 1f) },
            { "Legs",                new Color(0.55f, 0.30f, 1f) },
            { "Shoulder",            new Color(0.74f, 0.53f, 1f) },
            { "Utility",             Color.white },
        };

        // Item types KG considers transmoggable.
        public static readonly string[] AvailableTypes = {
            "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow",
            "Tool", "Shield", "Chest", "Helmet", "Legs", "Shoulder", "Utility",
        };

        // (playerName, baseItemPrefab) -> replacedPrefab
        private static readonly Dictionary<string, string> _picks = new();
        private static bool _loaded;

        public static string Get(string playerName, string baseItemPrefab)
        {
            EnsureLoaded();
            _picks.TryGetValue(Key(playerName, baseItemPrefab), out var v);
            return v;
        }

        public static void Set(string playerName, string baseItemPrefab, string replacedPrefab)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(replacedPrefab)) _picks.Remove(Key(playerName, baseItemPrefab));
            else _picks[Key(playerName, baseItemPrefab)] = replacedPrefab;
            Save();
            RefreshLocalEquipment();
        }

        public static bool HasTransmog(string playerName, string baseItemPrefab)
        {
            EnsureLoaded();
            return _picks.ContainsKey(Key(playerName, baseItemPrefab));
        }

        public static IEnumerable<KeyValuePair<string, string>> ForPlayer(string playerName)
        {
            EnsureLoaded();
            string prefix = (playerName ?? "") + "::";
            foreach (var kv in _picks)
            {
                if (kv.Key.StartsWith(prefix))
                    yield return new KeyValuePair<string, string>(kv.Key.Substring(prefix.Length), kv.Value);
            }
        }

        private static string Key(string player, string baseItem) => (player ?? "") + "::" + (baseItem ?? "");
        private static string StorePath => Path.Combine(FiresCore.Storage.FiresConfigPaths.Transmogrifications, "_picks.csv");

        private static void EnsureLoaded()
        {
            if (_loaded) return; _loaded = true;
            var path = StorePath; if (!File.Exists(path)) return;
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var parts = line.Split(',');
                    if (parts.Length != 3) continue;
                    _picks[Key(parts[0], parts[1])] = parts[2];
                }
            }
            catch (System.Exception ex) { Debug.LogError("[TransmogState] load: " + ex.Message); }
        }

        private static void Save()
        {
            try
            {
                var path = StorePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# player,baseItemPrefab,replacedPrefab" };
                foreach (var kv in _picks)
                {
                    var sep = kv.Key.IndexOf("::", System.StringComparison.Ordinal);
                    if (sep < 0) continue;
                    lines.Add(kv.Key.Substring(0, sep) + "," + kv.Key.Substring(sep + 2) + "," + kv.Value);
                }
                File.WriteAllLines(path, lines);
            }
            catch (System.Exception ex) { Debug.LogError("[TransmogState] save: " + ex.Message); }
        }

        private static void RefreshLocalEquipment()
        {
            var p = Player.m_localPlayer; if (p == null) return;
            // VisEquipment.UpdateEquipmentVisuals() reads the ZDO hash for each slot and feeds it directly
            // to SetXxxEquipped — it NEVER calls SetXxxItem(string), so the TransmogVisualPatches sitting
            // on those setters wouldn't fire on transmog Apply (only on unequip/re-equip via
            // Humanoid.SetupEquipment). Call SetupEquipment() reflectively here so every slot's setter
            // re-runs with the original prefab name; our prefix swaps the name to the transmog replacement,
            // SetXxxItem stamps the swapped hash into the ZDO, and the next visual refresh renders the swap.
            var method = AccessTools.Method(typeof(Humanoid), "SetupEquipment");
            try { method?.Invoke(p, null); }
            catch (System.Exception ex) { Debug.LogWarning("[TransmogState] SetupEquipment refresh failed: " + ex.Message); }
        }
    }

    /// <summary>
    /// VisEquipment patches that swap the visualized armor/weapon prefab based on the player's selected
    /// transmog for the BASE ITEM CURRENTLY EQUIPPED IN THAT SLOT. The actual ItemData is untouched.
    /// </summary>
    public static class TransmogVisualPatches
    {
        /// <summary>Returns the transmog replacement prefab name for the currently-equipped item in
        /// the given slot accessor lambda, or the original name if none / not the local player.</summary>
        private static string LookupReplacement(VisEquipment vis, System.Func<Player, ItemDrop.ItemData> equipped, string fallback)
        {
            if (vis == null || Player.m_localPlayer == null) return fallback;
            if (vis.gameObject != Player.m_localPlayer.gameObject) return fallback;
            var item = equipped(Player.m_localPlayer);
            string baseName = item?.m_dropPrefab != null ? item.m_dropPrefab.name : fallback;
            if (string.IsNullOrEmpty(baseName)) return fallback;
            var pick = TransmogState.Get(Player.m_localPlayer.GetPlayerName(), baseName);
            return string.IsNullOrEmpty(pick) ? fallback : pick;
        }
        /// <summary>Valheim 1.0 changed VisEquipment.Set*Item to take an int itemHash instead of a
        /// string name, and a hash cannot be turned back into a name - so this returns 0 for "no
        /// override" and the caller leaves the incoming hash untouched.</summary>
        private static int LookupReplacementHash(VisEquipment vis, System.Func<Player, ItemDrop.ItemData> equipped)
        {
            if (vis == null || Player.m_localPlayer == null) return 0;
            if (vis.gameObject != Player.m_localPlayer.gameObject) return 0;
            var item = equipped(Player.m_localPlayer);
            string baseName = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (string.IsNullOrEmpty(baseName)) return 0;
            var pick = TransmogState.Get(Player.m_localPlayer.GetPlayerName(), baseName);
            return string.IsNullOrEmpty(pick) ? 0 : pick.GetStableHashCode();
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
        public static class SetHelmetItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_helmetItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetChestItem))]
        public static class SetChestItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_chestItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetLegItem))]
        public static class SetLegItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_legItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetShoulderItem))]
        public static class SetShoulderItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_shoulderItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetRightItem))]
        public static class SetRightItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_rightItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetLeftItem))]
        public static class SetLeftItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash, ref int variant)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_leftItem);
                if (repl != 0) itemHash = repl;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetUtilityItem))]
        public static class SetUtilityItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref int itemHash)
            {
                int repl = LookupReplacementHash(__instance, p => p.m_utilityItem);
                if (repl != 0) itemHash = repl;
            }
        }
    }
}
