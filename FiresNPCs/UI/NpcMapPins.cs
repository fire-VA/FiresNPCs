using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// kg NPC_MapPins parity on our architecture: pinned NPCs (npc_map_pinned, kg KGmarketPinned) show
    /// on every player's map. The SERVER owns discovery — it holds every StaticNpc ZDO, so a periodic
    /// whole-world iterative scan collects (pos, name, icon) for pinned bodies and broadcasts the
    /// compact list to all clients (lighter than kg's ForceSendZDO-every-NPC approach). Clients rebuild
    /// their pin set from the latest payload: PinData added straight to Minimap.m_pins like kg (AddPin
    /// clamps custom pin types), name label, item-sprite icon with a vanilla fallback, never saved.
    /// </summary>
    public static class NpcMapPins
    {
        private const string RpcName = "Fires_NpcMapPins";
        private const string BodyPrefab = "StaticNpc";
        private const float ScanIntervalSeconds = 30f;
        private static readonly Minimap.PinType PinType = (Minimap.PinType)175;   // kg's custom type id

        private struct PinEntry { public Vector3 Pos; public string Name; public string Icon; }

        private static bool _registered;
        private static ZRoutedRpc _registeredOn;
        private static readonly List<PinEntry> _latest = new List<PinEntry>();
        private static readonly List<Minimap.PinData> _activePins = new List<Minimap.PinData>();
        private static float _nextScan;
        private static readonly List<ZDO> _scanBuffer = new List<ZDO>();
        private static int _scanIndex;
        private static bool _scanning;

        public static void EnsureRegistered()
        {
            var current = ZRoutedRpc.instance;
            if (current == null) return;
            if (_registered && _registeredOn == current) return;
            current.Register<ZPackage>(RpcName, new Action<long, ZPackage>(RPC_Pins));
            _registered = true;
            _registeredOn = current;
            _latest.Clear();
            RemoveActivePins();
        }

        // Driven from the server's per-frame loop (Game.Update postfix) so the iterative whole-world
        // scan can pump across frames without a coroutine host.
        [HarmonyPatch(typeof(Game), "Update")]
        private static class ServerScanDriver
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                try
                {
                    if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null) return;
                    EnsureRegistered();

                    if (!_scanning)
                    {
                        if (Time.unscaledTime < _nextScan) return;
                        _scanning = true;
                        _scanBuffer.Clear();
                        _scanIndex = 0;
                    }

                    // Pump one iterative step per frame until the whole-world scan completes (kg pattern).
                    if (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(BodyPrefab, _scanBuffer, ref _scanIndex)) return;

                    _scanning = false;
                    _nextScan = Time.unscaledTime + ScanIntervalSeconds;

                    var entries = new List<PinEntry>();
                    foreach (var zdo in _scanBuffer)
                    {
                        if (zdo == null || !zdo.IsValid()) continue;
                        if (!zdo.GetBool("npc_map_pinned", false)) continue;
                        string name = zdo.GetString("npc_name_override", "");
                        if (string.IsNullOrEmpty(name)) name = zdo.GetString("npc_profile", "NPC");
                        entries.Add(new PinEntry { Pos = zdo.GetPosition(), Name = name, Icon = zdo.GetString("npc_map_pin_icon", "") });
                    }
                    _scanBuffer.Clear();

                    var pkg = new ZPackage();
                    pkg.Write(entries.Count);
                    foreach (var e in entries) { pkg.Write(e.Pos); pkg.Write(e.Name ?? ""); pkg.Write(e.Icon ?? ""); }
                    ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pkg);
                }
                catch (Exception ex) { Debug.LogWarning($"[NpcMapPins] server scan failed: {ex.Message}"); }
            }
        }

        private static void RPC_Pins(long sender, ZPackage pkg)
        {
            if (pkg == null) return;
            try
            {
                _latest.Clear();
                int count = pkg.ReadInt();
                for (int i = 0; i < count; i++)
                {
                    var e = new PinEntry { Pos = pkg.ReadVector3(), Name = pkg.ReadString(), Icon = pkg.ReadString() };
                    _latest.Add(e);
                }
                RefreshPins();
            }
            catch (Exception ex) { Debug.LogWarning($"[NpcMapPins] receive failed: {ex.Message}"); }
        }

        private static void RefreshPins()
        {
            if (Minimap.instance == null || Player.m_localPlayer == null) return;
            RemoveActivePins();
            foreach (var e in _latest)
            {
                var pin = new Minimap.PinData
                {
                    m_type = PinType,
                    m_name = e.Name ?? "",
                    m_pos = e.Pos,
                    m_icon = ResolveIcon(e.Icon),
                    m_save = false,
                    m_checked = false,
                    m_ownerID = 0L,
                };
                if (!string.IsNullOrEmpty(pin.m_name))
                    pin.m_NamePinData = new Minimap.PinNameData(pin);
                Minimap.instance.m_pins.Add(pin);
                _activePins.Add(pin);
            }
        }

        private static void RemoveActivePins()
        {
            if (Minimap.instance != null)
                foreach (var pin in _activePins)
                    try { Minimap.instance.RemovePin(pin); } catch { }
            _activePins.Clear();
        }

        // Icon = an ITEM prefab's inventory sprite (kg lets admins type any icon name); unresolvable or
        // empty falls back to the vanilla Icon3 (house) pin sprite so pins never render blank.
        private static Sprite ResolveIcon(string prefabName)
        {
            try
            {
                if (!string.IsNullOrEmpty(prefabName) && ObjectDB.instance != null)
                {
                    var pf = ObjectDB.instance.GetItemPrefab(prefabName.Trim());
                    var drop = pf != null ? pf.GetComponent<ItemDrop>() : null;
                    var icon = drop != null && drop.m_itemData != null ? drop.m_itemData.GetIcon() : null;
                    if (icon != null) return icon;
                }
            }
            catch { }
            try { return Minimap.instance.GetSprite(Minimap.PinType.Icon3); } catch { return null; }
        }

        // A dying client keeps no stale pins into the next session.
        [HarmonyPatch(typeof(Minimap), "OnDestroy")]
        private static class ClearOnMapDestroy
        {
            [HarmonyPostfix]
            private static void Postfix() { _activePins.Clear(); _latest.Clear(); }
        }
    }
}
