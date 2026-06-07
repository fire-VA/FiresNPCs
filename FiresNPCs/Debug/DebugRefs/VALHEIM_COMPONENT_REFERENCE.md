# Valheim Component Decompiled Reference — ZRoutedRpc, ZNet, Player

Decompiled from `assembly_valheim.dll` (Version=0.0.0.0) shipped with Valheim.
Source: `C:\Users\travi\OneDrive\Desktop\ValheimDll\assembly_valheim.dll`

This file covers Valheim's networking and player systems that our UI Builder integration
hooks into — ZRoutedRpc for UI layout sync, ZNet for server/client architecture,
and Player for the game state our UI reads from.

**See also:**
- `VALHEIM_UI_REFERENCE.md` — InventoryGui, InventoryGrid — Valheim-specific UI components
- `UNITY_UI_DECOMPILED_REFERENCE.md` — Part 1 (LayoutRebuilder, LayoutGroup, ScrollRect)
- `TMPRO_DECOMPILED_REFERENCE.md` — TMP_Text, TMP_InputField, TMP_Dropdown

### Reference File Index

| File | Contents | Status |
|------|----------|--------|
| `UNITY_UI_DECOMPILED_REFERENCE.md` | LayoutRebuilder, LayoutGroup, ScrollRect | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_2.md` | Selectable, Button, Slider, CanvasScaler, GraphicRaycaster, Mask, RectMask2D | ? Complete |
| `UNITY_UI_DECOMPILED_REFERENCE_3.md` | HorizontalOrVerticalLayoutGroup, HLG/VLG, GridLayoutGroup, ContentSizeFitter, Image, Toggle, Dropdown (Legacy) | ? Complete |
| `TMPRO_DECOMPILED_REFERENCE.md` | TMP_Text, TMP_InputField, TMP_Dropdown — text rendering, input handling, TMP dropdown vs legacy | ? Complete |
| `VALHEIM_UI_REFERENCE.md` | InventoryGui, InventoryGrid — Valheim-specific UI components our parser captures | ? Complete |
| `VALHEIM_COMPONENT_REFERENCE.md` | ZRoutedRpc (for UILayoutSyncRPC), ZNet/ZDO patterns, Player/Game UI hooks our integration uses | ? Complete |

---

## Table of Contents

1. [ZRoutedRpc](#zroutedrpc) — Routed RPC system for cross-peer communication
2. [ZNet](#znet) — Network manager, server/client architecture
3. [Player (Selected Members)](#player-selected-members) — Player state our UI integration reads

---

## ZRoutedRpc

**Key insights for our UI Builder integration:**

- **Singleton via `s_instance`** — accessed through `ZRoutedRpc.instance`. Our `UILayoutSyncRPC` registers handlers here.
- **`Register<T>(name, callback)`** — registers an RPC handler by name. The name is hashed via `GetStableHashCode()` for efficient lookup. Our mod registers custom RPCs like `"UILayoutSync"` through this.
- **`InvokeRoutedRPC(targetPeerID, methodName, params)`** — sends an RPC to a specific peer or to everybody (`ZRoutedRpc.Everybody`). The method name is hashed, parameters are serialized via `ZRpc.Serialize()`.
- **`m_functions` dictionary** — maps method hash ? `RoutedMethodBase`. Supports up to 6 typed parameters via generic `RoutedMethod<T, U, V, ...>` classes.
- **Routing architecture:**
  - If `targetPeerID == self.m_id` ? handled locally via `HandleRoutedRPC()`
  - If `targetPeerID == 0` (Everybody) ? handled locally AND routed to peers
  - Otherwise ? routed to the specific peer
  - Server acts as relay: if server receives an RPC not targeted at itself, it forwards it
- **`RoutedRPCData`** contains: `m_msgID` (unique per message), `m_senderPeerID`, `m_targetPeerID`, `m_targetZDO` (for ZDO-targeted RPCs), `m_methodHash`, `m_parameters` (ZPackage).
- **ZDO-targeted RPCs** — when `m_targetZDO` is not `ZDOID.None`, the RPC is delivered to the `ZNetView` instance owning that ZDO. This is used for object-specific communication (e.g., piece interactions).
- **Our usage pattern:** Register with `ZRoutedRpc.instance.Register<ZPackage>("UILayoutSync", OnReceiveLayout)`, invoke with `ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "UILayoutSync", pkg)`.

```csharp
// Decompiled with JetBrains decompiler
// Type: ZRoutedRpc
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800

using System;
using System.Collections.Generic;

#nullable disable
public class ZRoutedRpc
{
  public static long Everybody;
  public Action<long> m_onNewPeer;
  private int m_rpcMsgID = 1;
  private bool m_server;
  private long m_id;
  private readonly List<ZNetPeer> m_peers = new List<ZNetPeer>();
  private readonly Dictionary<int, RoutedMethodBase> m_functions = new Dictionary<int, RoutedMethodBase>();
  private static ZRoutedRpc s_instance;

  public static ZRoutedRpc instance => ZRoutedRpc.s_instance;

  public ZRoutedRpc(bool server)
  {
    ZRoutedRpc.s_instance = this;
    this.m_server = server;
  }

  public void SetUID(long uid) => this.m_id = uid;

  public void AddPeer(ZNetPeer peer)
  {
    this.m_peers.Add(peer);
    peer.m_rpc.Register<ZPackage>("RoutedRPC", new Action<ZRpc, ZPackage>(this.RPC_RoutedRPC));
    if (this.m_onNewPeer == null)
      return;
    this.m_onNewPeer(peer.m_uid);
  }

  public void RemovePeer(ZNetPeer peer) => this.m_peers.Remove(peer);

  private ZNetPeer GetPeer(long uid)
  {
    foreach (ZNetPeer peer in this.m_peers)
    {
      if (peer.m_uid == uid)
        return peer;
    }
    return (ZNetPeer) null;
  }

  public void InvokeRoutedRPC(long targetPeerID, string methodName, params object[] parameters)
  {
    this.InvokeRoutedRPC(targetPeerID, ZDOID.None, methodName, parameters);
  }

  public void InvokeRoutedRPC(string methodName, params object[] parameters)
  {
    this.InvokeRoutedRPC(this.GetServerPeerID(), methodName, parameters);
  }

  private long GetServerPeerID()
  {
    if (this.m_server)
      return this.m_id;
    return this.m_peers.Count > 0 ? this.m_peers[0].m_uid : 0L;
  }

  public void InvokeRoutedRPC(
    long targetPeerID,
    ZDOID targetZDO,
    string methodName,
    params object[] parameters)
  {
    ZRoutedRpc.RoutedRPCData routedRpcData = new ZRoutedRpc.RoutedRPCData();
    routedRpcData.m_msgID = this.m_id + (long) this.m_rpcMsgID++;
    routedRpcData.m_senderPeerID = this.m_id;
    routedRpcData.m_targetPeerID = targetPeerID;
    routedRpcData.m_targetZDO = targetZDO;
    routedRpcData.m_methodHash = methodName.GetStableHashCode();
    ZRpc.Serialize(parameters, ref routedRpcData.m_parameters);
    routedRpcData.m_parameters.SetPos(0);
    if (targetPeerID == this.m_id || targetPeerID == 0L)
      this.HandleRoutedRPC(routedRpcData);
    if (targetPeerID == this.m_id)
      return;
    this.RouteRPC(routedRpcData);
  }

  private void RouteRPC(ZRoutedRpc.RoutedRPCData rpcData)
  {
    ZPackage pkg = new ZPackage();
    rpcData.Serialize(pkg);
    if (this.m_server)
    {
      if (rpcData.m_targetPeerID != 0L)
      {
        ZNetPeer peer = this.GetPeer(rpcData.m_targetPeerID);
        if (peer == null || !peer.IsReady())
          return;
        peer.m_rpc.Invoke("RoutedRPC", (object) pkg);
      }
      else
      {
        foreach (ZNetPeer peer in this.m_peers)
        {
          if (rpcData.m_senderPeerID != peer.m_uid && peer.IsReady())
            peer.m_rpc.Invoke("RoutedRPC", (object) pkg);
        }
      }
    }
    else
    {
      foreach (ZNetPeer peer in this.m_peers)
      {
        if (peer.IsReady())
          peer.m_rpc.Invoke("RoutedRPC", (object) pkg);
      }
    }
  }

  private void RPC_RoutedRPC(ZRpc rpc, ZPackage pkg)
  {
    ZRoutedRpc.RoutedRPCData routedRpcData = new ZRoutedRpc.RoutedRPCData();
    routedRpcData.Deserialize(pkg);
    if (routedRpcData.m_targetPeerID == this.m_id || routedRpcData.m_targetPeerID == 0L)
      this.HandleRoutedRPC(routedRpcData);
    if (!this.m_server || routedRpcData.m_targetPeerID == this.m_id)
      return;
    this.RouteRPC(routedRpcData);
  }

  private void HandleRoutedRPC(ZRoutedRpc.RoutedRPCData data)
  {
    if (data.m_targetZDO.IsNone())
    {
      RoutedMethodBase routedMethodBase;
      if (!this.m_functions.TryGetValue(data.m_methodHash, out routedMethodBase))
        return;
      routedMethodBase.Invoke(data.m_senderPeerID, data.m_parameters);
    }
    else
    {
      ZDO zdo = ZDOMan.instance.GetZDO(data.m_targetZDO);
      if (zdo == null)
        return;
      ZNetView instance = ZNetScene.instance.FindInstance(zdo);
      if (!((UnityEngine.Object) instance != (UnityEngine.Object) null))
        return;
      instance.HandleRoutedRPC(data);
    }
  }

  public void Register(string name, Action<long> f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod(f));
  }

  public void Register<T>(string name, Action<long, T> f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T>(f));
  }

  public void Register<T, U>(string name, Action<long, T, U> f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T, U>(f));
  }

  public void Register<T, U, V>(string name, Action<long, T, U, V> f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T, U, V>(f));
  }

  public void Register<T, U, V, B>(string name, RoutedMethod<T, U, V, B>.Method f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T, U, V, B>(f));
  }

  public void Register<T, U, V, B, K>(string name, RoutedMethod<T, U, V, B, K>.Method f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T, U, V, B, K>(f));
  }

  public void Register<T, U, V, B, K, M>(string name, RoutedMethod<T, U, V, B, K, M>.Method f)
  {
    this.m_functions.Add(name.GetStableHashCode(), (RoutedMethodBase) new RoutedMethod<T, U, V, B, K, M>(f));
  }

  public class RoutedRPCData
  {
    public long m_msgID;
    public long m_senderPeerID;
    public long m_targetPeerID;
    public ZDOID m_targetZDO;
    public int m_methodHash;
    public ZPackage m_parameters = new ZPackage();

    public void Serialize(ZPackage pkg)
    {
      pkg.Write(this.m_msgID);
      pkg.Write(this.m_senderPeerID);
      pkg.Write(this.m_targetPeerID);
      pkg.Write(this.m_targetZDO);
      pkg.Write(this.m_methodHash);
      pkg.Write(this.m_parameters);
    }

    public void Deserialize(ZPackage pkg)
    {
      this.m_msgID = pkg.ReadLong();
      this.m_senderPeerID = pkg.ReadLong();
      this.m_targetPeerID = pkg.ReadLong();
      this.m_targetZDO = pkg.ReadZDOID();
      this.m_methodHash = pkg.ReadInt();
      this.m_parameters = pkg.ReadPackage();
    }
  }
}
```

---

## ZNet

**Key insights for our UI Builder integration:**

- **Singleton via `m_instance`** — accessed through `ZNet.instance`. Our mod checks `ZNet.instance.IsServer()` to determine server/client role.
- **`m_isServer`** (static) — true for dedicated servers and singleplayer hosts. Our UI sync code uses this to decide whether to broadcast or receive layout changes.
- **`m_routedRpc`** — the `ZRoutedRpc` instance for this network session. Our RPC registrations go through this.
- **`m_zdoMan`** — `ZDOMan` manages all ZDOs (networked object data). Our mod doesn't directly use ZDOs for UI sync — we use `ZRoutedRpc` instead.
- **`m_peers`** — list of connected peers. Server iterates this for broadcasting. Our sync code uses `ZRoutedRpc.Everybody` instead of manual peer iteration.
- **`m_serverSyncedPlayerData`** — dictionary of string key-value pairs synced from client to server. Contains `"platformDisplayName"`, `"possibleEvents"`, `"baseValue"`, etc. Our mod could potentially add custom sync data here.
- **`GetTimeSeconds()`** / `m_netTime` — network-synchronized game time. Used for coordinating timed events across clients.
- **`m_connectionStatus`** — enum tracking connection state. Our mod checks this before attempting RPC calls.
- **Save system** — `SaveWorld()` runs on a background thread via `m_saveThread`. The save process: `PrepareSave()` (main thread) ? `SaveWorldThread()` (background thread). Our mod should not send RPCs during saves.
- **Player list** — `m_players` contains `PlayerInfo` structs with name, character ID, user info, position. Updated every 2 seconds via `SendPeriodicData()`. Our mod can read this for player-aware UI.
- **Admin list** — `m_adminList` / `m_adminListForRpc` for permission checking. `LocalPlayerIsAdminOrHost()` is the check our mod uses for admin-only UI features.
- **`IsDedicated()`** returns `true` always in the decompiled code (dedicated server build). In client builds this would return false. Our mod uses this to skip client-only UI operations on dedicated servers.
- **Password hashing** — uses MD5 (`MD5CryptoServiceProvider`) with a random salt. The salt is generated once per server session and sent to clients during handshake.
- **Connection flow:** Client ? `ServerHandshake` ? Server sends `ClientHandshake(needPassword, salt)` ? Client sends `PeerInfo(uid, version, networkVersion, position, name, passwordHash, sessionTicket)` ? Server validates and sends its own `PeerInfo` ? Connection established.

```csharp
// Decompiled with JetBrains decompiler
// Type: ZNet
// Assembly: assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: C366779D-F99D-449D-A9E1-6B4E0A4D1800

// NOTE: This is a heavily condensed version showing only the members relevant
// to our UI Builder integration. The full class is ~1500 lines. See the workspace
// summary for the complete decompiled source.

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

#nullable disable
public class ZNet : MonoBehaviour
{
  public static Action WorldSaveStarted;
  public static Action WorldSaveFinished;
  private static ZNet m_instance;
  private ZDOMan m_zdoMan;
  private ZRoutedRpc m_routedRpc;
  private double m_netTime = 2040.0;
  private ZDOID m_characterID = ZDOID.None;
  private Vector3 m_referencePosition = Vector3.zero;
  private bool m_publicReferencePosition;
  public Dictionary<string, string> m_serverSyncedPlayerData = new Dictionary<string, string>();
  private static bool m_isServer = true;
  private static World m_world = (World) null;
  private static ZNet.ConnectionStatus m_connectionStatus = ZNet.ConnectionStatus.None;
  private SyncedList m_adminList;
  private SyncedList m_bannedList;
  private SyncedList m_permittedList;
  private List<ZNet.PlayerInfo> m_players = new List<ZNet.PlayerInfo>();
  private List<string> m_adminListForRpc = new List<string>();
  private readonly List<ZNetPeer> m_peers = new List<ZNetPeer>();
  private Thread m_saveThread;
  private bool m_haveStoped;

  public static ZNet instance => ZNet.m_instance;

  private void Awake()
  {
    ZNet.m_instance = this;
    this.m_routedRpc = new ZRoutedRpc(ZNet.m_isServer);
    this.m_zdoMan = new ZDOMan(this.m_zdoSectorsWidth);
    // ... initialization ...
    this.m_routedRpc.SetUID(ZDOMan.GetSessionID());
    if (this.IsServer())
      this.SendPlayerList();
    if (!this.IsDedicated())
      this.m_serverSyncedPlayerData["platformDisplayName"] = PlatformManager.DistributionPlatform.LocalUser.DisplayName;
  }

  public bool IsServer() => ZNet.m_isServer;
  public bool IsDedicated() => true; // Dedicated server build
  public static bool IsSinglePlayer => ZNet.m_isServer && !ZNet.m_openServer;

  public static long GetUID() => ZDOMan.GetSessionID();
  public long GetWorldUID() => ZNet.m_world.m_uid;
  public string GetWorldName() => ZNet.m_world != null ? ZNet.m_world.m_name : (string) null;

  public double GetTimeSeconds() => this.m_netTime;
  public float GetWrappedDayTimeSeconds() => (float) (this.m_netTime % 86400.0);

  public void SetReferencePosition(Vector3 pos) => this.m_referencePosition = pos;
  public Vector3 GetReferencePosition() => this.m_referencePosition;

  public void SetCharacterID(ZDOID id)
  {
    this.m_characterID = id;
    if (!ZNet.m_isServer)
      this.m_peers[0].m_rpc.Invoke("CharacterID", (object) id);
  }

  public bool LocalPlayerIsAdminOrHost()
  {
    return this.IsServer() || this.PlayerIsAdmin(UserInfo.GetLocalUser().UserId);
  }

  public List<ZNet.PlayerInfo> GetPlayerList() => this.m_players;
  public int GetNrOfPlayers() => this.m_players.Count;

  public static ZNet.ConnectionStatus GetConnectionStatus()
  {
    if ((UnityEngine.Object) ZNet.m_instance != (UnityEngine.Object) null && ZNet.m_instance.IsServer())
      return ZNet.ConnectionStatus.Connected;
    return ZNet.m_connectionStatus;
  }

  public ZRpc GetServerRPC() => this.GetServerPeer()?.m_rpc;
  public List<ZNetPeer> GetPeers() => this.m_peers;
  public bool IsSaving() => this.m_saveThread != null;

  public static World GetWorldIfIsHost() => ZNet.m_isServer ? ZNet.m_world : (World) null;

  // --- Server-synced player data (sent every 2 seconds) ---
  private void SendPeriodicData(float dt)
  {
    // ... timer logic ...
    if (this.IsServer())
    {
      this.SendNetTime();
      this.SendPlayerList();
    }
    else
    {
      foreach (ZNetPeer peer in this.m_peers)
      {
        if (peer.IsReady())
          this.SendServerSyncPlayerData(peer);
      }
    }
  }

  private void SendServerSyncPlayerData(ZNetPeer peer)
  {
    ZPackage zpackage = new ZPackage();
    zpackage.Write(this.m_referencePosition);
    zpackage.Write(this.m_publicReferencePosition);
    zpackage.Write(this.m_serverSyncedPlayerData.Count);
    foreach (KeyValuePair<string, string> keyValuePair in this.m_serverSyncedPlayerData)
    {
      zpackage.Write(keyValuePair.Key);
      zpackage.Write(keyValuePair.Value);
    }
    peer.m_rpc.Invoke("ServerSyncedPlayerData", (object) zpackage);
  }

  public enum ConnectionStatus
  {
    None, Connecting, Connected, ErrorVersion, ErrorDisconnected,
    ErrorConnectFailed, ErrorPassword, ErrorAlreadyConnected, ErrorBanned,
    ErrorFull, ErrorPlatformExcluded, ErrorCrossplayPrivilege, ErrorKicked,
  }

  public struct PlayerInfo
  {
    public string m_name;
    public ZDOID m_characterID;
    public ZNet.CrossNetworkUserInfo m_userInfo;
    public string m_serverAssignedDisplayName;
    public bool m_publicPosition;
    public Vector3 m_position;
  }

  public struct CrossNetworkUserInfo : IEquatable<ZNet.CrossNetworkUserInfo>
  {
    public PlatformUserID m_id;
    public string m_displayName;
    // ... equality members ...
  }
}
```

---

## Player (Selected Members)

**Key insights for our UI Builder integration:**

This section covers only the Player members that our UI integration reads from or hooks into.
The full Player class is ~4000 lines. See the workspace summary for the complete decompiled source.

### Static access patterns
- **`Player.m_localPlayer`** — the local player instance. Our UI code always checks this before reading player state.
- **`Player.s_players`** — all players in the world. Used by proximity-based UI features.

### Inventory & Crafting
- **`m_inventory`** — player inventory. Our UI reads this for item display.
- **`GetAvailableRecipes(ref List<Recipe>)`** — fills the list with currently available recipes based on known materials, crafting station, and DLC ownership.
- **`HaveRequirements(Recipe, discover, quality, amount)`** — checks if the player can craft a recipe at a given quality level. Our crafting UI checks this.
- **`ConsumeResources(requirements, quality, itemQuality, multiplier)`** — removes crafting materials from inventory.
- **`GetCurrentCraftingStation()`** — returns the crafting station the player is currently using, or null.

### Stats
- **`GetStamina()` / `GetMaxStamina()`** — stamina values. Our HUD reads these.
- **`GetEitr()` / `GetMaxEitr()`** — eitr (magic) values.
- **`GetAdrenaline()` / `GetMaxAdrenaline()`** — adrenaline values.
- **`GetBodyArmor()`** — total armor from equipped items.
- **`GetMaxCarryWeight()`** — max carry weight with SE modifiers.
- **`IsEncumbered()`** — `totalWeight > maxCarryWeight`.

### Food system
- **`m_foods`** — list of `Player.Food` currently active. Each food has `m_name`, `m_item`, `m_time`, `m_health`, `m_stamina`, `m_eitr`.
- **`CanEat(item, showMessages)`** — checks if the player can eat an item (slot available, not duplicate).
- **`EatFood(item)`** — adds/refreshes a food entry.

### Skills
- **`m_skills`** — `Skills` component. `GetSkillFactor(type)` returns 0-1 skill level.
- **`RaiseSkill(type, value)`** — increases skill XP. Our crafting bonuses use this.

### Equipment modifiers
- **`m_equipmentModifierValues[]`** — cached array of equipment modifier sums, indexed by `s_equipmentModifierSources`. Updated in `UpdateModifiers()`.
- **`GetEquipmentMovementModifier()`, `GetEquipmentAttackStaminaModifier()`, etc.** — convenience accessors into the modifier array.

### UI state
- **`TakeInput()`** — returns false when any UI is blocking input (Chat, Console, TextInput, StoreGui, InventoryGui, Menu, TextViewer, Minimap, FreeFly, Barber). Our UI integration should follow the same pattern.
- **`m_customData`** — `Dictionary<string, string>` for mod-added persistent data. Saved with the player profile. Our mod can store UI preferences here.
- **`ShowTutorial(name)`** / `SetSeenTutorial(name)` / `HaveSeenTutorial(name)` — tutorial system. Our mod could use this for first-time UI hints.
- **`m_knownRecipes`, `m_knownMaterial`, `m_knownStations`** — knowledge state that affects what recipes/pieces are available in the UI.

### Save/Load
- **`Save(ZPackage)`** — serializes all player data including `m_customData`. Format version is 29.
- **`Load(ZPackage)`** — deserializes player data. Handles format migration from older versions (down to version 2).

### Networking
- **`GetPlayerID()`** — unique player ID from ZDO.
- **`GetPlayerName()`** — player name from ZDO.
- **`Message(type, msg, amount, icon)`** — shows a message to the player via MessageHud. Our UI uses this for feedback.
- **`IsPVPEnabled()`** — PVP toggle state from ZDO.

### Key methods for UI interaction

```csharp
// TakeInput() — determines if the player can accept game input
// Our UI integration follows the same blocking pattern
protected override bool TakeInput()
{
    bool input = (!(bool) (UnityEngine.Object) Chat.instance || !Chat.instance.HasFocus())
      && !Console.IsVisible() && !TextInput.IsVisible() && !StoreGui.IsVisible()
      && !InventoryGui.IsVisible() && !Menu.IsVisible()
      && (!(bool) (UnityEngine.Object) TextViewer.instance || !TextViewer.instance.IsVisible())
      && !Minimap.IsOpen() && !GameCamera.InFreeFly()
      && !PlayerCustomizaton.IsBarberGuiVisible();
    if (this.IsDead() || this.InCutscene() || this.IsTeleporting())
      input = false;
    return input;
}

// GetAvailableRecipes — populates the recipe list for the crafting UI
public void GetAvailableRecipes(ref List<Recipe> available)
{
    available.Clear();
    foreach (Recipe recipe in ObjectDB.instance.m_recipes)
    {
      bool flag1 = (UnityEngine.Object) this.m_currentSeason != (UnityEngine.Object) null
        && this.m_currentSeason.Recipes.Contains(recipe);
      if ((recipe.m_enabled || flag1) && (bool) (UnityEngine.Object) recipe.m_item)
      {
        // Filter by search terms (Player.s_FilterCraft)
        // Check DLC ownership
        // Check known recipes or cheat mode
        // Check crafting station requirement
        if (/* all checks pass */)
          available.Add(recipe);
      }
    }
}

// HaveRequirements — checks if player can craft at a given quality level
public bool HaveRequirements(Recipe recipe, bool discover, int qualityLevel, int amount = 1)
{
    if (discover)
    {
      if ((bool) (UnityEngine.Object) recipe.m_craftingStation
        && !this.KnowStationLevel(recipe.m_craftingStation.m_name, recipe.m_minStationLevel))
        return false;
    }
    else if (!this.RequiredCraftingStation(recipe, qualityLevel, true))
      return false;
    return (recipe.m_item.m_itemData.m_shared.m_dlc.Length <= 0
      || DLCMan.instance.IsDLCInstalled(recipe.m_item.m_itemData.m_shared.m_dlc))
      && this.HaveRequirementItems(recipe, discover, qualityLevel, amount);
}

// m_customData — mod-usable persistent storage on the player
[HideInInspector]
public Dictionary<string, string> m_customData = new Dictionary<string, string>();

// Save includes m_customData
public void Save(ZPackage pkg)
{
    // ... other save data ...
    pkg.Write(this.m_customData.Count);
    foreach (KeyValuePair<string, string> keyValuePair in this.m_customData)
    {
      pkg.Write(keyValuePair.Key);
      pkg.Write(keyValuePair.Value);
    }
    // ...
}
```

---

## Actionable Takeaways for UI Builder Integration

### 1. RPC registration happens once after ZNet.Awake
Register custom RPCs in `ZRoutedRpc.instance` after `ZNet` has initialized. The `m_onNewPeer` callback can be used to send initial state to newly connected peers.

### 2. Use `ZRoutedRpc.Everybody` for broadcast, not manual peer iteration
The routing system handles server relay automatically. Sending to `Everybody` on a client goes through the server, which forwards to all other peers.

### 3. Check `ZNet.instance.IsServer()` for role-based behavior
Server handles authoritative state; clients send requests. Our UI sync should follow this pattern — server validates and broadcasts, clients display.

### 4. Player.m_customData persists across sessions
Use `Player.m_localPlayer.m_customData["mymod_key"] = value` for persistent per-character settings. This data is included in the player's save file automatically.

### 5. Follow TakeInput() pattern for UI blocking
When our custom UI is open, it should be checked in the same way as InventoryGui, StoreGui, etc. Either patch TakeInput or ensure our UI sets a state that existing checks cover.

### 6. ZNet save thread runs in background
Don't send RPCs or modify shared state while `ZNet.instance.IsSaving()` is true. The save thread accesses ZDO data directly.

### 7. Server-synced player data updates every 2 seconds
The `m_serverSyncedPlayerData` dictionary is sent periodically, not on every change. For time-sensitive sync, use direct RPCs instead.

### 8. Network version is 36, world save version is 37
These are checked during connection. Our mod doesn't need to worry about these unless we're modifying the save format.
