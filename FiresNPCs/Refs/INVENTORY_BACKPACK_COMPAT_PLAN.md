# FiresNPCs / Verdant's Ascent — Inventory & Backpack Compatibility Plan

**Goal.** Make sure FiresNPCs systems that touch the player's inventory
or coin balance continue to work after VAInventory grows a Backpack
slot, a Coin Purse slot (infinite-stack), and a backpack-driven dynamic
expansion block. This is a "verify and patch surgically" plan — the
RPG maker doesn't own the inventory grid, so most changes are tiny.

Companion plans:
- `VABackpacks/Refs/BACKPACK_INVENTORY_REFACTOR_PLAN.md`
- `VAInventory/Refs/BACKPACK_AND_COIN_SLOT_PLAN.md`

---

## 1. What the player-side inventory is used for in this mod

Locations that read from or write to `Player.GetInventory()`:

| File | What it does | Risk |
|------|--------------|------|
| `UI/Screens/TraderUIController.cs` | `CountPlayerItems("Coins", 0)`; lists coin total in trader UI; deducts coins on purchase. | Medium — needs to count coins from the new purse slot too. |
| `Modules/Companions/CompanionController.cs` | `tamingItemPrefab = "Coins"` — companion taming reads coins from player inventory. | Low — vanilla `inventory.HaveItem` still finds them. |
| `Modules/Vault/Treasury.cs` | Server-side coin balance store; deposits/withdrawals interact with player inventory. | Medium — withdraw needs a target slot. |
| `Modules/Companions/IdleBehaviors/SmartStorageOrganizer.cs` | Has a `Coins` ItemCategory mapping; companions sort coins into nearby chests. | Low — this is on the chest side, not the player's purse. |
| `Modules/Quest/QuestProgressPatches.cs` + reward pipeline | Quest rewards include `Item: Coins, N` — added to player inventory via `Inventory.AddItem`. | Low — lands in the purse via VAInventory's coin-purse-aware AddItem patch. |
| `UI/QuestJournal/*` | Reads coin count for reward summaries. | Low — same as TraderUI. |
| `UI/Screens/QuestInteractionScreenController.cs` | Same. | Low. |

Companion-side inventories (`CompanionInventory`, the four management
screens under `Modules/Companions/UI/`) are **separate** from the
player's inventory and don't change.

## 2. Coin counting — expected behavior

Vanilla path:

```csharp
int coins = player.GetInventory().CountItems("Coins");
```

`CountItems` walks every item in the player's `Inventory` regardless
of grid position, so coins sitting in the new Coin Purse slot are
naturally included. **No change needed in TraderUIController, the
quest journal, or anywhere else** that uses `CountItems`.

What we should verify (test, not code):

- TraderUI displays the full coin total even when most coins live in
  the purse slot.
- "Buy" deducts from the purse (vanilla `RemoveItem("Coins", n)` walks
  the inventory and removes from any matching stack — the purse stack
  is just a very large one).
- Quest reward "+50 Coins" lands in the purse rather than spawning a
  loose stack in the regular inventory. This relies on VAInventory's
  `Inventory.AddItem` patch routing `Coins` to the purse first.

## 3. Targeted patches (this mod)

### `UI/Screens/TraderUIController.cs`

Currently uses `CountPlayerItems("Coins", 0)`. Confirm that this helper
falls through to `inventory.CountItems` (vanilla) and not a custom
grid-position scan. Search:

```bash
grep -n "CountPlayerItems" UI/Screens/TraderUIController.cs
```

If it scans only grid positions `< InventoryHeightPlayer`, **patch it**
to walk the whole inventory. Otherwise leave alone.

### `Modules/Vault/Treasury.cs`

Withdraw flow currently calls `Inventory.AddItem(coins, amount)`. With
VAInventory's coin-purse-aware patch, that already routes to the purse
first, so the withdrawal lands there. No change.

Deposit flow calls `inventory.RemoveItem("Coins", amount)`. Vanilla
walks all stacks; the purse stack is just a big one. No change.

**Verify:** very large withdrawal amounts (> `int.MaxValue / 2`) don't
overflow the purse stack. The infinite-stack patch in VAInventory
should clamp at `int.MaxValue - 1` or similar; document the cap.

### `Modules/Companions/CompanionController.cs` + `CompanionPatches.cs` — taming flow rework

**Current behavior (broken UX):** Tame requires the player to (1) put
coins in their hotbar, (2) equip them, (3) right-click / use-item on
the companion. Pressing **E** on a wild companion only shows the
prompt _"Give 50 Coins to recruit X"_ — it never actually tries to
tame. The hotbar requirement is awkward, especially with the new coin
purse where coins won't normally be in the hotbar.

**Fix.** Make E-interact on a wild companion check the player's full
inventory (which naturally includes the coin purse) and auto-deduct
+ tame on the spot.

**Files to change:**

1. `Modules/Companions/CompanionController.cs` — refactor `OnUseItem`'s
   coin-count / coin-remove block into a shared helper:

   ```csharp
   /// <summary>
   /// Try to tame this companion using `tamingItemPrefab` from anywhere
   /// in the player's inventory (regular grid, equipment slots, coin
   /// purse — anywhere CountItems can find it). Returns true if taming
   /// completed; false with a center-screen reason message otherwise.
   /// </summary>
   public bool TryAutoTameWithInventoryItems(Player player) { ... }
   ```

   Logic:
   - If already tamed → return false.
   - Walk `player.GetInventory().GetAllItems()` matching by
     `m_dropPrefab?.name == tamingItemPrefab` (case-insensitive). This
     correctly hits the coin-purse stack too because purse items live
     in the same `Inventory`.
   - If `total < tamingItemAmount` → show "Need X (have Y)" and return.
   - Decrement stacks until `tamingItemAmount` is consumed; remove any
     stack that drops to 0 via `inventory.RemoveItem(item)`.
   - Call `TameCompanion(player)`.

   Keep the existing `OnUseItem` path; have it call the new helper so
   right-click-with-coins still works (back-compat for muscle memory).

2. `Modules/Companions/CompanionPatches.cs` — in
   `Tameable_Interact_Prefix`, replace the "Not tamed" branch's
   message-only path with:

   ```csharp
   if (!companion.isTamed)
   {
       // Admin shift+E to instant tame (unchanged)
       if (shiftHeld && companion.IsPlayerAdmin(player))
       {
           companion.AdminTame(player);
           __result = true;
           return false;
       }

       // E-interact: auto-tame if coins available anywhere in inventory.
       if (companion.TryAutoTameWithInventoryItems(player))
       {
           __result = true;
           return false;
       }

       // Helper already showed the "need X have Y" message on failure.
       __result = true;
       return false;
   }
   ```

**Why this works with the coin purse:** `Inventory.GetAllItems()` and
`Inventory.CountItems(...)` walk every stack regardless of grid
position, and the coin purse is just a special-validator slot on the
same player `Inventory`. No special-casing needed.

**Hover text update.** Add a one-line nudge to the hover text when the
player has enough coins:

> "Press E to recruit (50 Coins)"

vs. when they don't:

> "Need 50 Coins to recruit (have 23)"

This goes in `CompanionPatches.GetHoverText`-equivalent (or
`Tameable.GetHoverText` patch) — search for where the existing
"Wild - Give X Coins to recruit" hover text comes from
(`CompanionPatches.cs:2312`) and gate the message on coin count.

**Test plan addition (taming):**
- [ ] E-interact wild companion with 0 coins → "Need 50 Coins (have 0)".
- [ ] E-interact with 50 coins in regular inventory → tames, coins removed.
- [ ] E-interact with 50 coins in the coin purse → tames, purse decremented.
- [ ] E-interact with 30 in purse + 20 in regular → both stacks drained.
- [ ] Right-click (use-item) flow still works for back-compat.
- [ ] Hover text shows "Press E to recruit" when threshold met.

### Quest reward pipeline

Quest rewards format `Item: Coins, 50` go through whatever
`AddItem("Coins", 50)` is wired into. Verify:

```bash
grep -rn 'AddItem("Coins"' Modules/ Managers/ UI/
```

If any path uses a non-vanilla `AddItem` variant (e.g., spawns a
ground-drop instead of inventory), it'll bypass the purse routing.
Convert those to `Inventory.AddItem` so the purse picks them up.

### `Modules/Companions/IdleBehaviors/SmartStorageOrganizer.cs`

Companion sorts coins into nearby chests. With a coin purse on the
player, the player will rarely deposit coins into chests anyway. The
chest-side organizer is unaffected — leave alone.

Optional follow-up: add a "PullCoinsFromChest" rule so a returning
companion can withdraw chest coins into the player's purse. Not part
of this task.

## 4. Companion equipment screen (informational)

`Modules/Companions/UI/CompanionInventoryScreen.cs` and friends manage
each companion's inventory, which is a separate `CompanionInventory`
object — not the player's `Inventory`. **No coin-purse / backpack
slot work needed on the companion side.** If we want companions to
also have a coin purse later, that's a follow-up.

## 5. NPC-mode players

`Modules/Companions/NpcMode/` lets a companion become a static NPC
(innkeeper, vendor). These NPCs don't carry coins on their person —
trader-style transactions interact with the player's purse just like
vanilla traders.

## 6. Help panel (already updated)

The Verdant's Ascent help panel was just refreshed in the last task.
After this work lands, add a one-line note to the **Globe HUD Overlay**
or **Companion Management** section (whichever fits) documenting:

> "Coins automatically stack in your dedicated coin purse slot when
> VAInventory is installed. Quest rewards, trader sales, and companion
> taming costs all use this slot."

Also add a **Backpacks** subsection (one paragraph) under the
Companions or Interface group:

> "When VABackpacks is installed alongside VAInventory, equipping a
> backpack adds a row of extra inventory slots to the equipment panel.
> The slots stay visible while the backpack is equipped and contents
> are stored on the backpack item itself."

These doc edits are deferred to the implementation task — listed here
so we don't forget.

## 7. Test plan (cross-mod)

Run with all three mods enabled:

- [ ] Pick up coins → land in purse, stack to a high count.
- [ ] Quest "+50 Coins" reward → purse total goes up.
- [ ] Open trader → coin total reflects purse contents; buy works.
- [ ] Tame a companion that requires Coins → coins deducted from purse.
- [ ] Equip a backpack → extra slots appear; place quest reward items
      there, log out, log in → items still there.
- [ ] Smart storage organizer in a chest → coins still get sorted to
      a coin chest as before (not affected by purse).
- [ ] Disable VABackpacks, keep VAInventory: coin purse still works.
- [ ] Disable VAInventory, keep VABackpacks: legacy Container path
      still works (backpacks open as a separate window).
- [ ] Companion roster screen still works (companion inventory is
      independent of player slots).

## 8. Deliverables (this mod)

After the VABackpacks + VAInventory plans are implemented, this mod
needs:

1. **Audit pass** of the files in §1 — confirm no custom grid scans
   that would miss the purse slot.
2. **Patch** any `CountPlayerItems` / similar helper that scans only
   the regular grid.
3. **Help panel doc edit** (§6) — small text additions.
4. **Smoke test** the full coin flow (quest reward → trader buy →
   companion tame).

No new features, no architecture changes. Most of the cross-mod
heavy lifting is in VABackpacks and VAInventory.
