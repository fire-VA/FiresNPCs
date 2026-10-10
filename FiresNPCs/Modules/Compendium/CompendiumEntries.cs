using CompendiumEntry = FiresCore.Help.CompendiumEntry;

namespace FiresRPGmaker.Modules.Compendium
{
    /// <summary>
    /// Static definitions of all compendium entries.
    /// Organized by category for maintainability.
    /// </summary>
    public static class CompendiumEntries
    {
        #region Basics Entries
        
        public static void RegisterBasicsEntries()
        {
            // Welcome / Overview
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_welcome",
                Category = CompendiumManager.CATEGORY_BASICS,
                Title = "Welcome to Verdant's Ascent",
                SortOrder = 0,
                DiscoveryKey = "companion_recruited",
                Content = @"<color=#00E5FF>Verdant's Ascent</color> adds intelligent AI companions to Valheim!

Your companions will:
• Fight alongside you in combat
• Level up and grow stronger
• Develop unique archetypes and abilities
• Manage their own equipment and inventory
• Perform idle tasks around your base

<color=yellow>Getting Started:</color>
1. Recruit companions at special NPCs
2. Equip them with weapons and armor
3. Use <color=orange>Shift + Middle Mouse</color> to direct them
4. Watch them grow into powerful allies!

Check the other compendium entries to learn more about each system."
            });
            
            // Controls
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_controls",
                Category = CompendiumManager.CATEGORY_CONTROLS,
                Title = "Companion Controls",
                SortOrder = 1,
                DiscoveryKey = "companion_recruited",
                Content = @"<color=#00E5FF>Companion Control Keybinds</color>

<color=yellow>Movement Commands:</color>
<color=orange>Shift + Middle Mouse</color> - Direct companions to a location
<color=orange>Shift + MMB on enemy</color> - Focus attack on target
<color=orange>Shift + MMB on ground</color> - Move to position
<color=orange>Shift + Right Click</color> - Whistle (summon closest companion)

<color=yellow>Companion Management:</color>
<color=orange>E</color> (on companion) - Open their inventory
<color=orange>Shift + E</color> - Toggle Follow / Stay
<color=orange>Hold R</color> (looking at a companion) - Companion menu:
• Check stats and archetype
• Manage equipment
• Set Home / Dismiss / Guild access
<color=orange>Hold H</color> - Idle-behavior radial

<color=yellow>Companion Status:</color>
The party overlay below the minimap is always shown.
Toggle it with the [GroupHud] ShowGroupHud config option.

<color=yellow>Behavior Modes:</color>
• <color=green>Follow</color> - Stay close and assist
• <color=yellow>Stay</color> - Hold current location"
            });
            
            // Leveling System
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_leveling",
                Category = CompendiumManager.CATEGORY_BASICS,
                Title = "Companion Leveling",
                SortOrder = 2,
                DiscoveryKey = "companion_level_10",
                Content = @"<color=#00E5FF>Companion Progression</color>

Companions gain experience through combat and activities.

<color=yellow>Experience Sources:</color>
• Defeating enemies (scaled by difficulty)
• Completing objectives with you
• Practicing skills during idle time

<color=yellow>Level Milestones:</color>
• <color=#9A9A9A>Level 1-10:</color> Learning basics
• <color=green>Level 11-24:</color> Developing archetype
• <color=yellow>Level 25:</color> Hybrid class unlocks!
• <color=orange>Level 35-50:</color> Expert abilities
• <color=red>Level 75:</color> Master abilities
• <color=#FF5CE1>Level 100:</color> Ultimate ability

<color=yellow>Stat Growth:</color>
Each level increases base stats:
• +Health and Stamina
• +Damage output
• +Defense ratings
• +Archetype-specific bonuses

Higher level companions are significantly more powerful!"
            });
            
            // Equipment
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_equipment",
                Category = CompendiumManager.CATEGORY_BASICS,
                Title = "Companion Equipment",
                SortOrder = 3,
                DiscoveryKey = "inventory_opened",
                Content = @"<color=#00E5FF>Equipment System</color>

Companions can equip weapons and armor just like players.

<color=yellow>Equipment Types:</color>
• <color=orange>Weapons:</color> Determines attack style and archetype
• <color=orange>Shield:</color> Enables blocking (Tank/Paladin)
• <color=orange>Helmet:</color> Head protection
• <color=orange>Chest:</color> Torso armor
• <color=orange>Legs:</color> Lower body protection
• <color=orange>Cape:</color> Back slot equipment

<color=yellow>Archetype Requirements:</color>
Your companion's archetype is determined by their equipped weapon:
• Bow/Crossbow -> Ranger
• Staff -> Mage or Healer
• Shield + Weapon -> Tank or Paladin
• Two-handed -> Berserker
• Knives -> Rogue
• Unarmed/Fists -> Monk

<color=yellow>Quality Matters:</color>
Higher quality equipment provides better stats.
Upgrade your companion's gear as you progress!"
            });
            
            // Idle Behaviors
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_idle",
                Category = CompendiumManager.CATEGORY_BASICS,
                Title = "Idle Behaviors",
                SortOrder = 4,
                DiscoveryKey = "companion_recruited",
                Content = @"<color=#00E5FF>Companion Idle Behaviors</color>

When not in combat, companions perform helpful tasks!

<color=yellow>Available Behaviors:</color>
• <color=orange>Chest Organization:</color> Sort nearby containers
• <color=orange>Resource Gathering:</color> Collect dropped items
• <color=orange>Smelter Operation:</color> Manage furnaces/kilns
• <color=orange>Fire Tending:</color> Keep fires lit
• <color=orange>Workstation Use:</color> Assist at crafting stations
• <color=orange>Bow Practice:</color> Rangers train accuracy

<color=yellow>Behavior Priority:</color>
Companions will:
1. Respond to threats (highest priority)
2. Follow your commands
3. Perform idle tasks when safe

<color=yellow>Base Helpers:</color>
Leave companions at your base in Guard mode to have them help with chores while you're away!"
            });
            
            // NPC Mode
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "basics_npc_mode",
                Category = CompendiumManager.CATEGORY_BASICS,
                Title = "NPC Mode",
                SortOrder = 5,
                DiscoveryKey = "npc_mode_seen",
                Content = @"<color=#00E5FF>NPC Mode</color>

Convert companions into stationary NPCs for your bases!

<color=yellow>NPC Features:</color>
• Stand at a fixed location
• Interact with players who approach
• Can be assigned dialogue
• Useful for roleplaying servers

<color=yellow>Setting Up (admin):</color>
1. Look at the companion and press Shift+P
2. Place it with the hammer-style placement
3. Configure dialogue (optional)

<color=yellow>Returning to Combat (admin):</color>
Look at the stationed NPC and press Shift+U to convert it back to an active companion.

<color=yellow>Server Uses:</color>
Great for:
• Quest givers
• Merchants
• Town guards
• Roleplay characters"
            });
        }
        
        #endregion
        
        #region Archetype Entries
        
        public static void RegisterArchetypeEntries()
        {
            // Archetype Overview
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_overview",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Archetype System",
                SortOrder = 10,
                DiscoveryKey = "companion_recruited",
                Content = @"<color=#00E5FF>Companion Archetypes</color>

Each companion develops a unique archetype based on their equipment.

<color=yellow>The 8 Archetypes:</color>
• <color=blue>Guardian</color> - Shield bearer, protects allies
• <color=yellow>Paladin</color> - Holy warrior with healing
• <color=red>Berserker</color> - Aggressive damage dealer
• <color=purple>Rogue</color> - Stealthy assassin
• <color=#00E5FF>Mage</color> - Elemental magic user
• <color=green>Cleric</color> - Support and restoration
• <color=orange>Ranger</color> - Ranged specialist
• <color=white>Monk</color> - Martial arts master

<color=yellow>Archetype Benefits:</color>
• Unique stat bonuses
• Special abilities
• Combat behaviors
• Role-specific AI

<color=yellow>Hybrid Classes:</color>
At level 25, companions can develop a sub-archetype, creating unique hybrid classes with special abilities!"
            });
            
            // Tank
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_tank",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Guardian Archetype",
                SortOrder = 11,
                DiscoveryKey = "archetype_seen_tank",
                Content = @"<color=blue>Guardian Archetype</color>

The stalwart defender who protects allies from harm.

<color=yellow>Requirements:</color>
• Shield equipped
• One-handed melee weapon

<color=yellow>Stat Bonuses:</color>
• +25% Max Stamina
• +15% Stamina Regeneration
• -30% Block Stamina Cost
• +10% Health
• +15% Armor

<color=yellow>Special Abilities:</color>
• <color=orange>Taunt:</color> Force enemies to attack the guardian
• <color=orange>Fortify:</color> Damage reduction buff
• <color=orange>Iron Wall:</color> Greatly reduced block damage
• <color=orange>Immortal Stance:</color> Brief damage immunity + AoE taunt

<color=yellow>Combat Role:</color>
Guardians excel at:
• Drawing enemy attention
• Protecting squishy allies
• Surviving heavy damage
• Controlling enemy positioning

<color=yellow>Best Paired With:</color>
Clerics (for sustain), Rangers/Mages (protect them)"
            });
            
            // Paladin
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_paladin",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Paladin Archetype",
                SortOrder = 12,
                DiscoveryKey = "archetype_seen_paladin",
                Content = @"<color=yellow>Paladin Archetype</color>

Holy warrior balancing offense, defense, and support.

<color=yellow>Requirements:</color>
• Shield equipped
• Mace or Sword preferred

<color=yellow>Stat Bonuses:</color>
• +10% Max Stamina
• +10% Stamina Regeneration
• -15% Block Stamina Cost
• +15% Health
• +10% Armor

<color=yellow>Special Abilities:</color>
• <color=orange>Divine Protection:</color> Damage reduction aura
• <color=orange>Holy Smite:</color> Spirit damage attacks
• <color=orange>Lay on Hands:</color> Emergency heal (master)

<color=yellow>Combat Role:</color>
Paladins provide:
• Balanced offense and defense
• Group damage mitigation
• Spirit damage vs undead
• Emergency healing support

<color=yellow>Best Paired With:</color>
Any composition - very versatile!"
            });
            
            // Berserker
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_berserker",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Berserker Archetype",
                SortOrder = 13,
                DiscoveryKey = "archetype_seen_berserker",
                Content = @"<color=red>Berserker Archetype</color>

Raging warrior who deals devastating damage.

<color=yellow>Requirements:</color>
• Two-handed weapon OR
• Dual wielding (no shield)

<color=yellow>Stat Bonuses:</color>
• +25% Stamina Regeneration
• -15% Attack Stamina Cost
• +25% Attack Damage
• +15% Attack Speed
• +5% Critical Chance
• -10% Health (glass cannon)
• -15% Armor

<color=yellow>Special Abilities:</color>
• <color=orange>Berserk Rage:</color> Massive damage boost
• <color=orange>Warcry:</color> Buff nearby allies
• <color=orange>Execute:</color> Finish low-health enemies

<color=yellow>Combat Role:</color>
Berserkers excel at:
• Raw damage output
• Clearing groups of enemies
• Finishing wounded targets
• High-risk, high-reward combat

<color=yellow>Best Paired With:</color>
Guardians (to take aggro), Clerics (to sustain)"
            });
            
            // Rogue
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_rogue",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Rogue Archetype",
                SortOrder = 14,
                DiscoveryKey = "archetype_seen_rogue",
                Content = @"<color=purple>Rogue Archetype</color>

Stealthy assassin striking from the shadows.

<color=yellow>Requirements:</color>
• Knives or daggers equipped

<color=yellow>Stat Bonuses:</color>
• +15% Max Stamina
• +20% Stamina Regeneration
• -30% Dodge Stamina Cost
• +10% Attack Damage
• +20% Attack Speed
• +10% Critical Chance
• +50% Critical Damage
• +10% Movement Speed

<color=yellow>Special Abilities:</color>
• <color=orange>Stealth:</color> Become invisible
• <color=orange>Backstab:</color> Bonus damage from behind
• <color=orange>Poison:</color> Apply damaging DoT
• <color=orange>Caltrops:</color> Slow pursuing enemies

<color=yellow>Combat Role:</color>
Rogues excel at:
• Burst damage to priority targets
• Applying debuffs
• Evading damage
• Flanking maneuvers

<color=yellow>Best Paired With:</color>
Guardians (to distract), other DPS (burst windows)"
            });
            
            // Ranger
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_ranger",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Ranger Archetype",
                SortOrder = 15,
                DiscoveryKey = "archetype_seen_ranger",
                Content = @"<color=orange>Ranger Archetype</color>

Expert marksman dealing damage from afar.

<color=yellow>Requirements:</color>
• Bow or Crossbow equipped

<color=yellow>Stat Bonuses:</color>
• +15% Max Stamina
• +15% Stamina Regeneration
• -15% Dodge Stamina Cost
• +15% Ranged Damage
• +10% Attack Speed (draw speed)
• +8% Critical Chance
• +5% Movement Speed

<color=yellow>Special Abilities:</color>
• <color=orange>Hunter's Mark:</color> Increase damage to target
• <color=orange>Eagle Eye:</color> Boost crit and accuracy
• <color=orange>Rain of Arrows:</color> AoE attack (master)
• <color=orange>Perfect Shot:</color> Guaranteed critical

<color=yellow>Combat Role:</color>
Rangers excel at:
• Sustained ranged damage
• Kiting enemies
• Debuffing priority targets
• Safe damage dealing

<color=yellow>Best Paired With:</color>
Guardians (to hold aggro), any melee"
            });
            
            // Mage
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_mage",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Mage Archetype",
                SortOrder = 16,
                DiscoveryKey = "archetype_seen_mage",
                Content = @"<color=#00E5FF>Mage Archetype</color>

Wielder of elemental magic and arcane power.

<color=yellow>Requirements:</color>
• Staff equipped (offensive type)
• Fire, Ice, or Lightning staff

<color=yellow>Stat Bonuses:</color>
• +30% Max Eitr
• +25% Eitr Regeneration
• -15% Eitr Cost
• +30% Magic Damage
• -20% Health (fragile)
• -25% Armor

<color=yellow>Special Abilities:</color>
• <color=orange>Elemental Infusion:</color> Boost elemental damage
• <color=orange>Arcane Shield:</color> Damage absorption barrier
• <color=orange>Meteor:</color> Devastating AoE (master)
• <color=orange>Arcane Form:</color> Ultimate power mode

<color=yellow>Combat Role:</color>
Mages excel at:
• AoE damage
• Elemental effects
• Burst magic damage
• Ranged safety

<color=yellow>Best Paired With:</color>
Guardians (essential protection), Clerics"
            });
            
            // Healer
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_healer",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Cleric Archetype",
                SortOrder = 17,
                DiscoveryKey = "archetype_seen_healer",
                Content = @"<color=green>Cleric Archetype</color>

Dedicated support focused on keeping allies alive.

<color=yellow>Requirements:</color>
• Support staff equipped
• Shield staff or healing staff

<color=yellow>Stat Bonuses:</color>
• +25% Max Eitr
• +30% Eitr Regeneration
• -20% Eitr Cost
• +10% Stamina Regeneration
• -15% Damage Output
• -10% Health
• -15% Armor

<color=yellow>Special Abilities:</color>
• <color=orange>Purify:</color> Cleanse debuffs + heal
• <color=orange>Purifying Circle:</color> AoE heal zone
• <color=orange>Sanctuary:</color> Damage reduction aura
• <color=orange>Divine Hymn:</color> Group heal (master)
• <color=orange>Resurrection:</color> Revive fallen allies

<color=yellow>Combat Role:</color>
Clerics excel at:
• Keeping party alive
• Removing debuffs
• Sustained healing
• Emergency saves

<color=yellow>Best Paired With:</color>
Guardians (heal target), Berserkers (sustain)"
            });
            
            // Monk
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "archetype_monk",
                Category = CompendiumManager.CATEGORY_ARCHETYPES,
                Title = "Monk Archetype",
                SortOrder = 18,
                DiscoveryKey = "archetype_seen_monk",
                Content = @"<color=white>Monk Archetype</color>

Martial artist channeling chi for combat mastery.

<color=yellow>Requirements:</color>
• Unarmed or Fist weapons
• Clubs also work

<color=yellow>Stat Bonuses:</color>
• +20% Max Stamina
• +30% Stamina Regeneration
• -25% Dodge Stamina Cost
• -20% Attack Stamina Cost
• +15% Attack Damage
• +25% Attack Speed
• +8% Critical Chance
• +10% Movement Speed

<color=yellow>Special Abilities:</color>
• <color=orange>Chi Strike:</color> Empowered attacks
• <color=orange>Inner Peace:</color> Healing meditation aura
• <color=orange>Chi Explosion:</color> AoE damage (master)
• <color=orange>Way of Perfection:</color> Ultimate form

<color=yellow>Combat Role:</color>
Monks excel at:
• Sustained melee damage
• Self-sustain via chi
• Mobility and dodging
• Hybrid offense/support

<color=yellow>Best Paired With:</color>
Any - very self-sufficient"
            });
        }
        
        #endregion
        
        #region Hybrid Entries
        
        public static void RegisterHybridEntries()
        {
            // Hybrid System Overview
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "hybrid_overview",
                Category = CompendiumManager.CATEGORY_HYBRIDS,
                Title = "Hybrid Classes",
                SortOrder = 20,
                DiscoveryKey = "hybrid_system_seen",
                Content = @"<color=#00E5FF>Hybrid Class System</color>

At level 25, companions develop a secondary archetype!

<color=yellow>How Hybrids Work:</color>
• Main archetype provides primary abilities (70%)
• Sub archetype adds secondary bonuses (30%)
• Unique hybrid name and special ability unlocked

<color=yellow>Hybrid Determination:</color>
The sub-archetype is based on:
• Combat behavior patterns
• Skills used most often
• Equipment preferences
• Random chance element

<color=yellow>Hybrid Benefits:</color>
• Unique class identity
• Special hybrid-only ability
• Blended stat bonuses
• Expanded combat options

<color=yellow>Examples:</color>
• Guardian + Paladin = <color=yellow>Crusader</color>
• Berserker + Rogue = <color=red>Reaper</color>
• Mage + Ranger = <color=#00E5FF>Storm Caller</color>

Each hybrid has a unique special ability!"
            });
            
            // Register some key hybrid entries
            // Tank Hybrids
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "hybrid_tank_paladin",
                Category = CompendiumManager.CATEGORY_HYBRIDS,
                Title = "Crusader (Guardian/Paladin)",
                SortOrder = 21,
                DiscoveryKey = "hybrid_unlocked_tank_paladin",
                Content = @"<color=yellow>Crusader</color>
<color=#9A9A9A>Guardian + Paladin Hybrid</color>

A holy warrior combining unwavering defense with divine power.

<color=yellow>Hybrid Bonuses:</color>
• +15% Defense
• +2 Health Regeneration
• +10% Block Efficiency

<color=yellow>Special Ability:</color>
<color=orange>Divine Bulwark</color>
Creates a holy shield that blocks all damage for 3 seconds and heals nearby allies.
Cooldown: 90 seconds

<color=yellow>Playstyle:</color>
Crusaders excel at protecting groups while providing steady healing. Position them at the front line where their aura can benefit melee allies."
            });
            
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "hybrid_tank_berserker",
                Category = CompendiumManager.CATEGORY_HYBRIDS,
                Title = "Juggernaut (Guardian/Berserker)",
                SortOrder = 22,
                DiscoveryKey = "hybrid_unlocked_tank_berserker",
                Content = @"<color=red>Juggernaut</color>
<color=#9A9A9A>Guardian + Berserker Hybrid</color>

An unstoppable force that grows stronger as battle rages.

<color=yellow>Hybrid Bonuses:</color>
• +10% Damage
• +10% Defense
• +20% Stamina Regeneration

<color=yellow>Special Ability:</color>
<color=orange>Unstoppable Charge</color>
Charges forward, stunning enemies and gaining damage reduction.
Cooldown: 45 seconds

<color=yellow>Playstyle:</color>
Juggernauts thrive in prolonged fights. They deal more damage than typical guardians while maintaining strong survivability."
            });
            
            // Berserker Hybrids
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "hybrid_berserker_rogue",
                Category = CompendiumManager.CATEGORY_HYBRIDS,
                Title = "Reaper (Berserker/Rogue)",
                SortOrder = 23,
                DiscoveryKey = "hybrid_unlocked_berserker_rogue",
                Content = @"<color=red>Reaper</color>
<color=#9A9A9A>Berserker + Rogue Hybrid</color>

A frenzied killer who strikes vital points in their rage.

<color=yellow>Hybrid Bonuses:</color>
• +15% Damage
• +20% Critical Chance
• +10% Speed

<color=yellow>Special Ability:</color>
<color=orange>Frenzy Strike</color>
Attack speed increases with each hit, critical hits restore stamina.
Cooldown: 30 seconds

<color=yellow>Playstyle:</color>
Reapers are glass cannons that excel at burst damage. Keep them healed and they'll devastate enemies."
            });
            
            // Mage Hybrids
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "hybrid_mage_ranger",
                Category = CompendiumManager.CATEGORY_HYBRIDS,
                Title = "Storm Caller (Mage/Ranger)",
                SortOrder = 24,
                DiscoveryKey = "hybrid_unlocked_mage_ranger",
                Content = @"<color=#00E5FF>Storm Caller</color>
<color=#9A9A9A>Mage + Ranger Hybrid</color>

A mage who commands lightning and wind.

<color=yellow>Hybrid Bonuses:</color>
• +18% Damage
• +1.5 Eitr/s Regeneration

<color=yellow>Special Ability:</color>
<color=orange>Chain Lightning</color>
Lightning arcs between enemies, dealing increasing damage.
Cooldown: 30 seconds

<color=yellow>Playstyle:</color>
Storm Callers excel at AoE damage. Position them where they can hit multiple enemies with their chain attacks."
            });
        }
        
        #endregion
        
        #region Ability Entries
        
        public static void RegisterAbilityEntries()
        {
            // Ability System Overview
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "ability_overview",
                Category = CompendiumManager.CATEGORY_ABILITIES,
                Title = "Ability System",
                SortOrder = 30,
                DiscoveryKey = "companion_level_10",
                Content = @"<color=#00E5FF>Companion Abilities</color>

Companions unlock powerful abilities as they level up.

<color=yellow>Ability Tiers:</color>
• <color=#9A9A9A>Basic (1-15):</color> Core archetype abilities
• <color=green>Advanced (15-35):</color> Specialized techniques
• <color=green>Expert (35-50):</color> Expert techniques
• <color=orange>Master (75):</color> Powerful signature moves
• <color=#FF5CE1>Ultimate (100):</color> Game-changing abilities

<color=yellow>Ability Usage:</color>
Companions use abilities automatically based on:
• Combat situation
• Cooldown availability  
• Target priority
• Health/resource status

<color=yellow>Synergy Abilities:</color>
Some abilities can combine with other companions' abilities for powerful synergy effects!

Check individual archetype entries for specific ability lists."
            });
            
            // Synergy System
            CompendiumManager.RegisterEntry(new CompendiumEntry
            {
                Key = "ability_synergies",
                Category = CompendiumManager.CATEGORY_ABILITIES,
                Title = "Group Synergies",
                SortOrder = 31,
                DiscoveryKey = "synergy_system_seen",
                Content = @"<color=#00E5FF>Group Synergy System</color>

When multiple companions work together, their abilities can combine!

<color=yellow>How Synergies Work:</color>
• Certain ability combinations trigger bonus effects
• Requires specific archetype pairings
• Timing matters - abilities must overlap

<color=yellow>Example Synergies:</color>
• <color=orange>Holy Bastion:</color> Tank + Paladin - group damage immunity + heal
• <color=orange>Primal Storm:</color> Berserker + Mage - AoE elemental burst
• <color=orange>Shadow Dance:</color> Rogue + Monk - stealth strike combo
• <color=orange>Divine Harmony:</color> Paladin + Healer - group heal + protection

<color=yellow>Building Synergies:</color>
Consider your party composition:
• Pair archetypes with complementary abilities
• Position companions to enable combo opportunities
• Watch for synergy visual effects!"
            });
        }
        
        #endregion
        
    }
}
