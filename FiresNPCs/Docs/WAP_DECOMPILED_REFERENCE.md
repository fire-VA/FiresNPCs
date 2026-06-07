# World Advancement Progression — Decompiled Reference

> **DO NOT COMPILE** — This file is a reference dump from VentureValheim.Progression v0.3.12.
> It documents what that mod does so we can build a better replacement integrated into our mod.

---

## What WAP Does Well (Keep)

### 1. Private Boss Keys (Per-Player Progression)
- Each player has their own set of "defeated_X" keys stored in `Player.m_customData["VV_PrivateKeys"]`
- When a boss dies, only players within 100m range receive the key
- Keys are synced to server via `ZRoutedRpc` RPCs
- This enables per-player progression instead of Valheim's global server-wide progression

### 2. Global Key Blocking/Filtering
- Can block ALL global keys from being set (`BlockAllGlobalKeys`)
- Or block/allow specific keys via comma-separated lists
- Can enforce certain keys to always be present
- This prevents a single player's boss kill from unlocking content for everyone

### 3. Private Raids (Per-Player Event System)
- Replaces `RandEventSystem.RefreshPlayerEventData` to use each player's private keys
- Each player only gets raids appropriate to their personal progression
- Player A who killed Eikthyr gets Black Forest raids; Player B who hasn't doesn't

### 4. Skill Level Caps Based on Progression
- Skill ceiling/floor tied to number of bosses defeated
- `BossKeysSkillPerKey` config: e.g., 10 levels per boss = 70 max at endgame
- Death skill drain can be absolute (fixed levels) instead of percentage

### 5. Portal Teleport Restrictions
- Per-material portal unlock keys (copper/tin, iron, silver, black metal, flametal)
- Configurable: which key unlocks which material for teleportation
- Overrides `Inventory.IsTeleportable` postfix

### 6. Trader Item Gating
- Haldor/Hildir/BogWitch items can require specific keys
- Configurable per-item key overrides
- Patches `Trader.GetAvailableItems` and `Trader.Start`

---

## What WAP Does Badly (Fix)

### 1. Locks Equipment/Crafting/Building/Cooking/Eating by DEFAULT
- All lock settings default to `true` — extremely aggressive
- New players can't equip copper gear until they've killed Eikthyr
- This is the #1 complaint and the reason to replace the mod
- **Our fix**: Default everything to UNLOCKED. Only block cheatsword/cheathammer/trollstaff.

### 2. Hard Dependency on Jotunn
- `[BepInDependency("com.jotunn.jotunn", HardDependency)]`
- Uses `SynchronizationManager.Instance.PlayerIsAdmin` for admin bypass
- **Our fix**: Use Valheim's native `ZNet.instance.IsAdmin()` or our own admin check

### 3. Overly Complex Config
- 60+ config entries, many with confusing interactions
- `BlockAllGlobalKeys` + `AllowedGlobalKeys` + `BlockedGlobalKeys` + `EnforcedGlobalKeys` = 4 overlapping lists
- **Our fix**: Simplify. One yaml file, clear override rules.

### 4. Material ? Key Mappings Are Hardcoded
- Massive `Dictionary<string, string>` for materials, food, boss items
- No way to add modded materials without code changes
- **Our fix**: YAML-driven mappings, extensible by server admins

### 5. No EWP Integration
- Keys are stored only in player save data and server memory
- Other mods (Expand World Pro) can't see player progression state
- **Our fix**: Already have EwpDataBridge — mirror all private keys there

### 6. Taming/Summoning Locks Are Annoying
- Default: Can't tame wolves until after Bonemass, can't tame Lox until after Moder
- Default: Must kill bosses in exact order to summon next one
- **Our fix**: Off by default. Available as opt-in feature.

---

## Boss Key Constants (Valheim Vanilla)

```
defeated_eikthyr     — Meadows boss
defeated_gdking      — Black Forest boss (The Elder)
defeated_bonemass    — Swamp boss
defeated_dragon      — Mountain boss (Moder)
defeated_goblinking  — Plains boss (Yagluth)
defeated_queen       — Mistlands boss (The Queen)
defeated_fader       — Ashlands boss (Fader)
```

## Boss Order Map
```
""                  ? 0 (no boss)
defeated_eikthyr    ? 1
defeated_gdking     ? 2
defeated_bonemass   ? 3
defeated_dragon     ? 4
defeated_goblinking ? 5
defeated_queen      ? 6
defeated_fader      ? 7
```

## Guardian Power ? Boss Key Map
```
GP_Eikthyr   ? defeated_eikthyr
GP_TheElder  ? defeated_gdking
GP_Bonemass  ? defeated_bonemass
GP_Moder     ? defeated_dragon
GP_Yagluth   ? defeated_goblinking
GP_Queen     ? defeated_queen
GP_Fader     ? defeated_fader
```

## Boss Item ? Boss Key Map
```
HardAntler   ? defeated_eikthyr
CryptKey     ? defeated_gdking
Wishbone     ? defeated_bonemass
DragonTear   ? defeated_dragon
YagluthDrop  ? defeated_goblinking
DvergrKey    ? defeated_goblinking
QueenDrop    ? defeated_queen
FaderDrop    ? defeated_fader
```

## WAP Harmony Patches (What They Hook)

| Patch Target | Type | Purpose |
|---|---|---|
| `Tameable.DecreaseRemainingTime` | Prefix | Block taming if no key |
| `ItemStand.DelayedPowerActivation` | Prefix | Block guardian power activation |
| `Player.ActivateGuardianPower` | Prefix | Block guardian power use |
| `OfferingBowl.InitiateSpawnBoss` | Prefix | Block boss summoning |
| `Humanoid.EquipItem` | Prefix | Block equipping items |
| `Inventory.GetAmmoItem` | Postfix | Block ammo usage |
| `Door.HaveKey` | Postfix | Block key doors |
| `InventoryGui.DoCrafting` | Prefix | Block crafting |
| `Player.TryPlacePiece` | Prefix | Block building |
| `CookingStation.OnUseItem` | Prefix | Block cooking |
| `Fermenter.AddItem` | Prefix | Block fermenting |
| `Player.CanEat` | Prefix | Block eating |
| `ShipControlls.Interact` | Prefix | Block boats |
| `TeleportWorld.Teleport` | Prefix | Block portals |
| `Inventory.IsTeleportable` | Postfix | Allow teleporting gated mats |
| `ZoneSystem.SetGlobalKey` | Prefix+Postfix | Block/redirect global keys ? private |
| `ZoneSystem.RPC_SetGlobalKey` | Prefix | Block server-side key setting |
| `ZoneSystem.RPC_GlobalKeys` | Postfix | Update skill cache |
| `ZoneSystem.GetGlobalKey` | Prefix | Return private key instead |
| `ZoneSystem.GetGlobalKeys` | Postfix | Merge private keys into result |
| `ZoneSystem.RPC_RemoveGlobalKey` | Postfix | Also remove private key |
| `ZoneSystem.Load` | Postfix | Server init: clean keys, register RPCs |
| `Player.Save` | Prefix | Persist private keys |
| `Player.EquipInventoryItems` | Prefix | Load private keys on login |
| `Terminal.InitTerminal` | Postfix | Add console commands |
| `RandEventSystem.RefreshPlayerEventData` | Prefix | Private raid events |
| `RandEventSystem.HaveGlobalKeys` | Prefix | Always true for private raids |
| `Trader.GetAvailableItems` | Prefix | Unlock all trader items |
| `StoreGui.SelectItem` | Prefix | Fix empty list crash |
| `Trader.Start` | Postfix | Apply custom trader key requirements |
| `Skills.LowerAllSkills` | Prefix | Custom skill drain |
| `Skills.Skill.Raise` | Prefix | Custom skill ceiling |

## WAP RPC Names
```
VV_ServerListKeys
VV_ServerSetPrivateKeys
VV_ServerSetPrivateKey
VV_ServerRemovePrivateKey
VV_SetPrivateKey
VV_RemovePrivateKey
VV_ResetPrivateKeys
```

## WAP Player Save Key
```
VV_PrivateKeys — stored in Player.m_customData, comma-separated key list
```
