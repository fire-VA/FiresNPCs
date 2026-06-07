# Valheim Networking & Player Identity Reference

> Decompiled from assembly_valheim.dll — DO NOT COMPILE

---

## How Platform IDs (Steam IDs) Work

### Key Types
- **`PlatformUserID`** — Wraps a platform string (e.g. `"Steam_76561198012345678"` or raw `"76561198012345678"`)
- **`ISocket.GetHostName()`** — Returns the platform ID string for a connected peer
- **`UserInfo`** — Contains `m_id` (PlatformUserID) and `m_displayName`

### Where the Steam ID Lives

| Context | How to get it | Notes |
|---|---|---|
| **Server sees a client** | `peer.m_socket.GetHostName()` | The canonical way. Returns Steam ID string. |
| **Client sees itself** | `PlatformManager.DistributionPlatform.LocalUser.PlatformUserID` | The LOCAL user's own platform ID. Available on the client. |
| **Client sees itself (display)** | `PlatformManager.DistributionPlatform.LocalUser.DisplayName` | Display name, NOT the ID. |
| **Server player list** | `m_players[i].m_userInfo.m_id` | PlatformUserID built from `peer.m_socket.GetHostName()` |
| **Client player list** | `m_players[i].m_userInfo.m_id` | Received via `RPC_PlayerList` — the server sends each player's platform ID |
| **Admin check (server)** | `rpc.GetSocket().GetHostName()` then `ListContainsId(m_adminList, hostName)` | |
| **Admin check (client)** | `this.PlayerIsAdmin(UserInfo.GetLocalUser().UserId)` | Uses local PlatformUserID against synced admin list |

### CRITICAL: `GetPeerByPlayerName()` Does NOT Return the Local Player

```csharp
// ZNet.GetPeerByPlayerName — searches m_peers list
// The local player is NOT in m_peers on either client or server
// On client: m_peers contains only the server peer
// On server: m_peers contains only remote client peers
// The local player's identity comes from PlatformManager, NOT from peers
```

### How `ZNet.LocalPlayerIsAdminOrHost()` Works

```csharp
public bool LocalPlayerIsAdminOrHost()
{
    return this.IsServer() || this.PlayerIsAdmin(UserInfo.GetLocalUser().UserId);
}
```

Uses `UserInfo.GetLocalUser().UserId` — this is a `PlatformUserID` that the client knows about itself.

### How the Server Builds the Player List

```csharp
// For the HOST player (non-dedicated):
playerInfo.m_userInfo.m_id = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;

// For remote peers:
playerInfo.m_userInfo.m_id = ZNet.m_onlineBackend != OnlineBackendType.Steamworks
    ? new PlatformUserID(peer.m_socket.GetHostName())
    : new PlatformUserID(m_steamPlatform, peer.m_socket.GetHostName());
```

### `PlatformUserID` Format

On Steamworks backend, the ID is constructed as:
```csharp
new PlatformUserID(m_steamPlatform, peer.m_socket.GetHostName())
// m_steamPlatform = new Platform("Steam")
// GetHostName() returns the raw Steam ID string like "76561198012345678"
// PlatformUserID.ToString() returns "Steam_76561198012345678" (prefixed!)
// peer.m_socket.GetHostName() returns "76561198012345678" (raw!)
```

**IMPORTANT**: EWP data file stores raw numeric IDs. Use `EwpDataBridge.StripPlatformPrefix()`
to convert `"Steam_76561198012345678"` ? `"76561198012345678"` when reading from player list.

### Admin List Format

Admin list stores IDs as strings. `ListContainsId` checks both:
- `platform.ToString()` (e.g. `"Steam_76561198012345678"`)
- `platform.m_userID.ToString()` (e.g. `"76561198012345678"`)

So admin lists work with either format.

---

## Correct Way to Get Steam ID in Our Mod

### On the CLIENT (for the local player):
```csharp
// CORRECT — find ourselves in the server-synced player list by character ZDOID
var playerList = ZNet.instance.GetPlayerList();
ZDOID localCharId = ZNet.instance.LocalPlayerCharacterID;
foreach (var info in playerList)
{
    if (info.m_characterID == localCharId)
        return info.m_userInfo.m_id.ToString(); // e.g. "Steam_76561198012345678"
}
```

### On the SERVER/HOST (for the host player):
```csharp
// CORRECT — host is always the first entry in the player list
// (added in UpdatePlayerList from PlatformManager.DistributionPlatform.LocalUser)
var players = ZNet.instance.GetPlayerList();
if (players.Count > 0)
    return players[0].m_userInfo.m_id.ToString();
```

### On the SERVER (for a specific peer by RPC sender):
```csharp
// CORRECT — server resolves peer identity from RPC sender UID
ZNetPeer peer = ZNet.instance.GetPeer(sender);
string platformId = peer?.m_socket?.GetHostName();
```

### WRONG patterns (what we were doing):
```csharp
// WRONG — local player is never in m_peers
var peer = ZNet.instance.GetPeerByPlayerName(Player.m_localPlayer.GetPlayerName());
// Returns null on client, returns null on server for the host player

// WRONG — this is the Valheim player ID, NOT the Steam ID
string id = Player.m_localPlayer.GetPlayerID().ToString();
// Returns something like "9028429027529144" — a Valheim save file ID, not a platform ID

// WRONG — GetServerPeer returns the server connection, not the local player
var serverPeer = ZNet.instance.GetServerPeer();
string host = serverPeer.m_socket?.GetHostName();
// This returns the SERVER's host name/IP, not the local player's Steam ID
```

---

## EWP Data File

The `ewp_data.yaml` file lives at `BepInEx/config/expand_world/ewp_data.yaml` on the SERVER.

- On a dedicated server, only the server process can write to it.
- On a local/host game, the host process can write to it directly.
- Remote clients CANNOT write to this file — they must RPC the server.

### Write Strategy
1. If `ZNet.instance.IsServer()` ? write directly (we ARE the server)
2. If client ? send RPC to server, server resolves Steam ID from `peer.m_socket.GetHostName()` and writes

---

## ZNetPeer Fields

```csharp
public class ZNetPeer
{
    public ISocket m_socket;    // Socket connection — GetHostName() returns platform ID
    public long m_uid;          // Session UID (from ZDOMan.GetSessionID())
    public ZDOID m_characterID; // The player's character ZDO ID
    public string m_playerName; // In-game character name
    public bool m_server;       // True if this peer IS the server (from client's perspective)
}
```

## ISocket Interface

```csharp
public interface ISocket
{
    string GetHostName();       // Returns platform/Steam ID for the connected peer
    string GetEndPointString(); // Returns connection endpoint (IP:port or similar)
    bool IsHost();              // Whether this socket is a host/listen socket
    // ... other methods
}
```
