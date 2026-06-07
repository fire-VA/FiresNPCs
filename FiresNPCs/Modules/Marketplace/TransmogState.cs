using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// Per-player picked transmog appearance per slot. Persists to
    /// <c>BepInEx/config/firesnpcs/transmog/_picks.csv</c>. Looked up by player name + slot in the
    /// VisEquipment patches below — the actual armor item never changes, only the visualized prefab.
    /// </summary>
    public static class TransmogState
    {
        public const string SlotHelmet = "Helmet";
        public const string SlotChest = "Chest";
        public const string SlotLegs = "Legs";
        public const string SlotCape = "Cape";

        private static readonly Dictionary<string, string> _picks = new();
        private static bool _loaded;

        public static string Get(string playerName, string slot)
        {
            EnsureLoaded();
            _picks.TryGetValue(Key(playerName, slot), out var v);
            return v;
        }

        public static void Set(string playerName, string slot, string prefab)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(prefab)) _picks.Remove(Key(playerName, slot));
            else _picks[Key(playerName, slot)] = prefab;
            Save();
            RefreshLocalEquipment();
        }

        private static string Key(string player, string slot) => (player ?? "") + "::" + (slot ?? "");
        private static string StorePath => Path.Combine(BepInEx.Paths.ConfigPath, "firesnpcs", "transmog", "_picks.csv");

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
                var lines = new List<string> { "# player,slot,prefab" };
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
            var vis = p.m_visEquipment; if (vis == null) return;
            // Trigger VisEquipment to rebuild — UpdateEquipmentVisuals is the cheapest way to force a re-equip pass.
            vis.UpdateEquipmentVisuals();
        }
    }

    /// <summary>
    /// VisEquipment patches that swap the visualized armor prefab to the picked transmog while the
    /// player's actual ItemData stays untouched. Each helmet/chest/legs/cape SetXItem is intercepted
    /// and the prefab name is replaced when a transmog is set for that slot.
    /// </summary>
    public static class TransmogVisualPatches
    {
        private static string LocalPick(string slot)
        {
            var p = Player.m_localPlayer;
            return p == null ? null : TransmogState.Get(p.GetPlayerName(), slot);
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetHelmetItem))]
        public static class SetHelmetItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref string name)
            {
                if (__instance == null || Player.m_localPlayer == null) return;
                if (__instance.gameObject != Player.m_localPlayer.gameObject) return;
                var pick = LocalPick(TransmogState.SlotHelmet);
                if (!string.IsNullOrEmpty(pick)) name = pick;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetChestItem))]
        public static class SetChestItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref string name)
            {
                if (__instance == null || Player.m_localPlayer == null) return;
                if (__instance.gameObject != Player.m_localPlayer.gameObject) return;
                var pick = LocalPick(TransmogState.SlotChest);
                if (!string.IsNullOrEmpty(pick)) name = pick;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetLegItem))]
        public static class SetLegItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref string name)
            {
                if (__instance == null || Player.m_localPlayer == null) return;
                if (__instance.gameObject != Player.m_localPlayer.gameObject) return;
                var pick = LocalPick(TransmogState.SlotLegs);
                if (!string.IsNullOrEmpty(pick)) name = pick;
            }
        }

        [HarmonyPatch(typeof(VisEquipment), nameof(VisEquipment.SetShoulderItem))]
        public static class SetShoulderItem_Patch
        {
            [HarmonyPrefix]
            private static void Prefix(VisEquipment __instance, ref string name)
            {
                if (__instance == null || Player.m_localPlayer == null) return;
                if (__instance.gameObject != Player.m_localPlayer.gameObject) return;
                var pick = LocalPick(TransmogState.SlotCape);
                if (!string.IsNullOrEmpty(pick)) name = pick;
            }
        }
    }
}
