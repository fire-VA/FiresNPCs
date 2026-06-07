# Dialogue System Implementation Plan - Full Module

## Progress Tracker

| Phase | Status | Summary |
|-------|--------|---------|
| Phase 1: Data Model & Legacy Parser | DONE | Expanded `RawDialogue`/`RawPlayerOption`, parse full Marketplace `.cfg` format with `@interaction`/`@inrange`/`@outrange`, OR conditions, `[uid=bg]` |
| Phase 2: Condition Engine | DONE | `DialogueConditionEngine.cs` - HasItem, HasQuest, QuestFinished, QuestProgress, GlobalKey, PlayerKey, Skill, Buff, CustomValue, Health/Stamina/Eitr checks + `!` negate + OR groups |
| Phase 3: Command Engine | DONE | `DialogueCommandEngine.cs` - GiveItem, RemoveItem, GiveQuest, RemoveQuest, FinishQuest, Teleport, Spawn, SpawnXYZ, Damage, Heal, GiveBuff, AddPlayerKey, CustomValue, PlayAnimation, PlaySound, ConsoleCommand, AddPin, PingMap, OpenUI, EnterPassword, SendWebhook |
| Phase 4: Dialogue UI | DONE | In-game dialogue panel with option buttons, icons, colors, hotbar keys |
| Phase 5: NPC Integration | DONE | Attach dialogue profiles to NPCs, trigger on interact/range |
| Phase 6: Server Sync | DONE | `DialogueSyncRPC.cs` - client/server RPC handlers, admin push, serializer, Harmony patches on ZNet.Awake |
| Phase 7: Admin UI | DONE | `DialogueEditorScreenController.cs` - sidebar list, form editor, option rows, save/delete/preview, hooked to NpcMainPanel DialogueNpc tab |
| Phase 8: Testing & Polish | DONE | Sample dialogues (Merchant/QuestGiver/Guard), DialogueManager deprecated, documentation |
| **Phase 9: UI Rework** | **IN PROGRESS** | **Restyle to match Marketplace visual, add Close + Back buttons, navigation history stack** |

---

## Reference Files

| File | Purpose |
|------|---------|
| `MARKETPLACE_DIALOGUE_REFERENCE.md` | Full documentation of Marketplace dialogue system (conditions, commands, format, examples, UI architecture) |
| `MARKETPLACE_DIALOGUE_SOURCE.cs` | Full decompiled Marketplace source code (Dialogues_DataTypes, Dialogues_UI, Dialogues_Main_Client, Dialogues_Main_Server) |
| `DIALOGUE_SYSTEM_PLAN.md` | This file - implementation roadmap |

---

## Phase 9: Dialogue UI Rework

### Goal

Restyle `DialogueUI.cs` to visually match the Marketplace dialogue screen while adding quality-of-life improvements that Marketplace lacks (Close button, Back button).

### Visual Reference (Marketplace)

```
+-------------------------------------------------------------------------+
|                          GuildMaster                                     |
|                                                                         |
|    "Hello There! And welcome to the Raven's Rest Guild Hall!"           |
|                                                                         |
|  +--------------------------------------------------------------------+ |
|  | (1) O Cooties said you might have some Tasks for me?               | |
|  +--------------------------------------------------------------------+ |
|  | (2) O Hello!, I'm just introducing myself to the people around     | |
|  |       town, nice to meet you!                                      | |
|  +--------------------------------------------------------------------+ |
+-------------------------------------------------------------------------+
```

Key visual elements from Marketplace:
- **Position**: Bottom-center of screen (dialogue bar style)
- **NPC Name**: Large gold/yellow text, centered, serif/Norse font
- **Dialogue Text**: Centered, wrapped in quotes (`"Hello There!..."`)
- **Thin divider line** between text and options
- **Option rows**: Dark background with thin border, each containing:
  - Number badge in a small circle/pill (e.g., `1`, `2`)
  - Circle icon (item sprite or empty circle)
  - Option text
- **No close button** in Marketplace (ESC only)
- **No back button** in Marketplace (we add this as an improvement)

### Implementation Plan

#### Task 9.1: Panel Position & Layout - DONE
- [x] Move panel anchor from center to bottom-center (15%-85% width, 2% from bottom)
- [x] Height driven by ContentSizeFitter (auto-size to content)
- [x] Add subtle Outline border effect
- [x] Dark semi-transparent background (`PanelBg`)
- [x] VerticalLayoutGroup with tighter padding

#### Task 9.2: NPC Name & Dialogue Text - DONE
- [x] NPC name: Gold color (`0.9, 0.85, 0.3`), centered, Norse font
- [x] Dialogue text: Centered (was left-aligned), wrapped in quotes
- [x] Thin gold divider between text and options

#### Task 9.3: Option Row Restyle - DONE
- [x] Number badge with circle background + outline
- [x] Each row has thin Outline border
- [x] Button hover states configured
- [x] Compact height (32px min)
- [x] Option font size 14pt

#### Task 9.4: Restore Close Button - DONE
- [x] Add small close button (top-right corner of panel)
- [x] Visually subtle (small X or cross icon) so it doesn't dominate
- [x] onClick -> `Hide()`
- [x] Still support ESC key as primary close method
- [x] Excluded from layout via `LayoutElement.ignoreLayout = true`

#### Task 9.5: Add Back Button (Navigation History) - DONE
- [x] Add `_navigationStack` (Stack<string>) to track visited dialogue UIDs
- [x] When `TransitionTo(nextUID)` is called, push `_currentUID` onto the stack before transitioning
- [x] Add "Back" button visible only when stack is non-empty
- [x] onClick -> pop stack, call `GoBack()` which loads previous without pushing again
- [x] Stack clears on `Hide()` and `Show()`
- [x] Back button positioned in layout flow (after divider, before scroll)
- [x] Hotkey: Backspace to go back (in addition to clicking)

#### Task 9.6: Button Color & Style Tuning - DONE
- [x] Close button: Small, semi-transparent red, top-right
- [x] Back button: Subtle warm gold accent, left-aligned
- [x] Option hover: Warmer gold tint on mouse-over (already done in 9.3)
- [x] Disabled option: Muted with red outline + error text (already done in 9.3)

#### Task 9.7: SDK Layout Support
- [ ] Verify `TryBuildFromSdkLayout()` still works with new field expectations
- [ ] Add `back_button` as optional tag in SDK layouts
- [ ] Document new tags for layout authors

#### Task 9.8: Testing & Edge Cases
- [ ] Test with 0 options (auto-close or show text-only)
- [ ] Test with 10+ options (scroll behavior)
- [ ] Test circular transitions with back button (A->B->A->B, back works correctly)
- [ ] Test @interaction-only dialogues (no UI shown)
- [ ] Test failed conditions with AlwaysVisible=true (greyed out + error)
- [ ] Test with long NPC names and long dialogue text
- [ ] Test ESC closes from any depth in navigation stack
- [ ] Test Backspace navigates back

### Architecture Changes

```
DialogueUI (static class)
  _navigationStack: Stack<string>     <- NEW: tracks dialogue UID history
  _backButton: Button                 <- NEW: "Back" button
  _closeButton: Button                <- RESTORED: small close button
  Show(npcName, uid, npcView)         <- clears stack
  TransitionTo(nextUID)               <- pushes current to stack, loads next
  GoBack()                            <- NEW: pops stack, loads previous
  Hide()                              <- clears stack, hides
  BuildHardCodedUI()                  <- restyled (DONE)
    Panel: bottom-center anchor
    NPC name: gold, centered
    Dialogue text: centered, quoted
    Divider: thin gold line
    Options scroll area
    Close button (top-right)          <- Task 9.4
    Back button (near options)        <- Task 9.5
  CreateOptionRow()                   <- restyled (DONE)
    Number badge with circle bg
    Icon (optional)
    Text with error line
  PopulateDialogue()
    Adds back button if stack non-empty
  HandleHotkeys()
    Backspace -> GoBack()             <- Task 9.5
```

### Files to Modify

| File | Changes |
|------|---------|
| `Modules/Dialogues/DialogueUI.cs` | Add navigation stack, close button, back button, style tuning |

### Files to Reference

| File | Purpose |
|------|---------|
| `Modules/Dialogues/MARKETPLACE_DIALOGUE_SOURCE.cs` | Decompiled source showing Marketplace's UI hierarchy and prefab structure |
| `Modules/Dialogues/MARKETPLACE_DIALOGUE_REFERENCE.md` | Full documentation with UI architecture section |

---

## Overview (Phases 1-8 - Complete)

Build a complete dialogue system that:
1. **Parses Marketplace legacy `.cfg` format** - drop-in compatibility for existing dialogue files
2. **Supports our own YAML/cfg format** - for future admin UI authoring
3. **Evaluates conditions** - show/hide/grey-out options based on player state
4. **Executes commands** - give items, start quests, teleport, spawn, damage, heal, etc.
5. **Renders a dialogue UI** - NPC name, dialogue text, background image, clickable option buttons with icons/colors
6. **Syncs server->client** - admin edits on server push to all connected clients
7. **Provides an admin UI** - in-game screen to author dialogues without editing files

---

## File Structure (Current)

```
FiresNPCs/Modules/Dialogues/
  DialogueCodex.cs              Data model + legacy .cfg parser
  DialogueConditionEngine.cs    Runtime condition evaluator
  DialogueCommandEngine.cs      Runtime command executor
  DialogueUI.cs                 In-game dialogue panel (REWORKING)
  DialogueSyncRPC.cs            Server<->client sync
  DIALOGUE_SYSTEM_PLAN.md       This document
  MARKETPLACE_DIALOGUE_REFERENCE.md   Marketplace docs reference
  MARKETPLACE_DIALOGUE_SOURCE.cs      Marketplace decompiled source

FiresNPCs/UI/
  DialogueManager.cs            DEPRECATED - replaced by DialogueCodex + DialogueUI
  Screens/
    DialogueEditorScreenController.cs  Admin editor

FiresNPCs/Config/FiresRPGmaker/Dialogues/
  legacy/
    SampleDialogue.cfg
    SampleMerchant.cfg
    SampleQuestGiver.cfg
    SampleGuard.cfg
```

---

## Implementation Order (Complete History)

### Sprint 1: Core Engine (Phases 1-3) - COMPLETE
1. Expanded `RawPlayerOption` - `InteractionType`, `List<string[]> Conditions`, `GetNextTransition()`, `Reload()`
2. Updated `DialogueCodex` - full Marketplace parser with `@interaction`/`@inrange`/`@outrange`, `[uid=bg]`, OR conditions (`||`), `ReplaceSpacesOutsideQuotes()`, scans all .cfg recursively
3. Built `DialogueConditionEngine` - 20+ condition types, AND/OR grouping, `!` negate, legacy aliases, fail-open on unknowns
4. Built `DialogueCommandEngine` - 18+ commands, quest integration via VaultOfKnowledge, `%playername%` replacement, `Console.TryRunCommand()` for ConsoleCommand
5. Build verified - zero compilation errors

### Sprint 2: UI & NPC Hook (Phases 4-5) - COMPLETE
1. Built `DialogueUI` - programmatic UI construction with warm gold palette, option buttons with icons/colors, hotbar keys (1-9, 0), fade animation, ESC to close, condition-gated options (greyed-out + error text or hidden), @interaction auto-execute
2. Hooked into `NpcController.HandleDialogueNpcInteraction` - tries DialogueCodex first, falls back to legacy DialogueManager
3. Integrated with `GUIManager.IsCustomPanelOpen()` and `CloseAllPanels()` for input blocking and ESC handling
4. Added Harmony patches on `Hud.Update` (drives per-frame update) and `Hud.OnDestroy` (cleanup on logout)
5. Added to font refresh system in `GUIManager.RefreshAllActiveFonts()`
6. Build verified - zero compilation errors

### Sprint 3: Sync & Admin (Phases 6-7) - COMPLETE
1. Built `DialogueSyncRPC` - client-side RPC_FileUpdate handler saves .cfg to disk + reloads DialogueCodex, server-side RPC_SyncFiles validates admin + writes file (triggers file watcher broadcast)
2. `ServerConfigFileWatcher` already watches `Dialogues` folder (confirmed in existing code)
3. Added `DialogueCodex.ParseFromContent()` and `RemoveDialoguesFromContent()` for direct string parsing
4. Added `SerializeDialogueToCfg()` + `PushDialogueToServer()` for admin editor -> server push flow
5. Harmony patches: ZNet.Awake registers RPCs, ZNet.RPC_PeerInfo stub for future per-peer init sync
6. Build verified - zero compilation errors
7. Built `DialogueEditorScreenController` - left sidebar with all dialogue UIDs, right form with UID/BG/Text fields + dynamic option rows (Type/Text/Transition/Commands/Conditions), Save/Delete/Preview buttons, sidebar refresh, dirty tracking, preview opens DialogueUI in-game
8. Hooked into `NpcMainPanelController` - created in `CreateTypeScreens()`, shown for DialogueNpc tab, hidden in `HideAllTypeScreens()`, added to `GUIManager.RefreshAllActiveFonts()`

### Sprint 4: Polish (Phase 8) - COMPLETE
1. Created `SampleMerchant.cfg` - branching shop/quest/lore with buy commands, item conditions, icons, colors, %playername%
2. Created `SampleQuestGiver.cfg` - quest-gated dialogue with skill checks, OR groups, OverrideError, RandomCommands, GiveBuff, !negate
3. Created `SampleGuard.cfg` - item/key gated access, CustomValue counters, HealthMore check, @interaction auto-execute, Damage, Teleport, RandomTransition, circular transitions, hidden options
4. Deprecated `UI/DialogueManager.cs` - added `[Obsolete]` attribute + XML docs pointing to DialogueCodex/DialogueUI
5. Existing `SampleDialogue.cfg` updated by DialogueCodex.WriteSampleIfMissing() on first run

### Sprint 5: UI Rework (Phase 9) - IN PROGRESS
1. DONE - Restyled panel to bottom-center anchor, Marketplace-style layout
2. DONE - NPC name gold/centered, dialogue text centered + quoted, thin divider
3. DONE - Option rows with number badge circles, outlines, hover states
4. DONE - Close button (small X, top-right, semi-transparent red)
5. DONE - Back button with navigation history stack (Stack<string>), Backspace hotkey
6. DONE - Style tuning (close/back/hover colors)
7. DONE - Added PlaySound, EnterPassword, SendWebhook commands for Marketplace .cfg compat
8. DONE - Option row color accent line (Marketplace-style bottom bar per option)
9. TODO - SDK layout support (optional back_button tag)
10. TODO - Edge case testing

### Sprint 5.5: Marketplace Command Compatibility - COMPLETE
1. Added `PlaySound` command — plays sfx prefab at NPC position, fallback to AudioSource
2. Added `EnterPassword` command — uses Valheim's TextInput for password gate dialogues, transitions to success/fail UIDs
3. Added `SendWebhook` command — HTTP POST to Discord-style webhooks with `{playername}` replacement
4. Added option row color accent line — thin bottom bar showing the option's custom Color (Marketplace `[line]` element)
5. Build verified

---

## Key Design Decisions

1. **Parse Marketplace format exactly** - users can drop in existing Marketplace dialogue `.cfg` files and they work
2. **Skip third-party mod integrations** - we don't depend on EpicMMO, Guilds, Battlepass, etc.
3. **Build UI programmatically** - matches our existing pattern (no asset bundle dependency for dialogue UI)
4. **Conditions are string-based** - parsed at load time, evaluated at display time (not compiled to delegates like Marketplace - simpler, good enough for our scale)
5. **Commands execute locally** - most commands affect local player; server-authoritative ones (quests) go through existing RPC
6. **Admin UI follows QuestConfigScreen pattern** - familiar to admins who already use our quest editor
7. **Server sync follows ServerConfigFileWatcher pattern** - consistent with how territories, quests, etc. sync
8. **DialogueCodex stays as the single source of truth** - `DialogueManager.cs` will be deprecated in favor of it
9. **Add Close + Back buttons** - Marketplace only has ESC; we improve UX with explicit navigation controls
10. **Navigation history stack** - enables back-navigation through dialogue trees, which Marketplace does not support
