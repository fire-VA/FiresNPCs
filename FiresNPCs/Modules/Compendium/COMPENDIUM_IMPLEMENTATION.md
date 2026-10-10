# Verdant's Ascent - Compendium System

## Overview

The Compendium system provides in-game documentation for the Verdant's Ascent mod, integrating seamlessly with Valheim's existing "Texts" dialog (accessed via the inventory screen). Entries are automatically unlocked as players discover new features, archetypes, and hybrids.

## Architecture

### Core Components

1. **CompendiumManager.cs** - Central registry for all compendium entries
2. **CompendiumDiscoveryTracker.cs** - Tracks what players have discovered
3. **CompendiumEntries.cs** - Static definitions of all compendium content

### Entry Categories

| Category | Discovery Trigger | Description |
|----------|------------------|-------------|
| `companion_basics` | First companion recruited | Core companion mechanics |
| `companion_controls` | First companion recruited | Keybinds and controls |
| `archetype_*` | Encounter archetype | Per-archetype guides |
| `hybrid_*` | Companion reaches Level 25+ | Hybrid class guides |
| `ability_*` | Ability unlocks | Ability explanations |
| `admin_*` | Admin status | Admin commands (admin-only) |

### Entry Structure

```csharp
public class CompendiumEntry
{
    public string Key;           // Unique identifier (e.g., "archetype_tank")
    public string Category;      // For organization (e.g., "Archetypes")
    public string Title;         // Display name (e.g., "Tank Archetype")
    public string Content;       // Full text content
    public bool RequiresAdmin;   // Only shown to admins
    public string DiscoveryKey;  // What triggers unlock (null = always available)
}
```

## Integration Points

### Player Known Texts
- Entries are injected into `Player.m_knownTexts` 
- Persists across sessions via Valheim's save system
- Appears in the standard Texts dialog (inventory ? Texts tab)

### Discovery Triggers

1. **Companion Recruitment** ? Unlocks basics, controls
2. **Archetype Detection** ? Unlocks archetype-specific entry
3. **Level 25 Hybrid** ? Unlocks hybrid class entry
4. **Ability Unlock** ? Unlocks ability description
5. **Admin Status** ? Reveals admin entries

### Harmony Patches

- `Player.OnSpawned` - Check for new discoveries on login
- `CompanionController.Start` - Track archetype encounters
- `ArchetypeController.OnHybridUnlocked` - Track hybrid discoveries

## Usage

### For Players
1. Open Inventory (Tab)
2. Click "Texts" button
3. Browse Verdant's Ascent entries (prefixed with [VA])

### For Admins
Admin entries are automatically visible when player has admin status.
Use console command `compendium_unlock_all` to reveal everything.

### Console Commands

| Command | Description |
|---------|-------------|
| `compendium_list` | List all available entries |
| `compendium_unlock <key>` | Manually unlock an entry |
| `compendium_unlock_all` | Unlock all entries (admin) |
| `compendium_reset` | Reset all discoveries |

## Content Guidelines

### Writing Style
- Use Valheim's Norse-inspired tone where appropriate
- Keep entries concise but informative
- Include practical tips and keybinds
- Reference other entries when relevant

### Formatting
- Use `\n\n` for paragraphs
- Use `<color=orange>text</color>` for highlights
- Use `<color=yellow>KEY</color>` for keybinds
- Keep line length reasonable for the UI

## File Organization

```
Modules/
  Compendium/
    COMPENDIUM_IMPLEMENTATION.md  (this file)
    CompendiumManager.cs          (core manager)
    CompendiumDiscoveryTracker.cs (discovery tracking)
    CompendiumEntries.cs          (content definitions)
    Entries/
      BasicsEntries.cs            (companion basics)
      ArchetypeEntries.cs         (archetype guides)
      HybridEntries.cs            (hybrid guides)
      AbilityEntries.cs           (ability descriptions)
      AdminEntries.cs             (admin documentation)
```

## Future Enhancements

- [ ] Custom UI panel with search/filter
- [ ] Entry bookmarking
- [ ] Progress tracking (X/Y entries discovered)
- [ ] Achievement integration
- [ ] Localization support
