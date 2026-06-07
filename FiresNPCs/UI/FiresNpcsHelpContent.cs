using FiresCore.Help;

namespace FiresRPGmaker.UI
{
    // Registers FiresRPGmaker's help content with the shared FiresCore help panel. Sections were lifted
    // verbatim from the former VerdantAscentHelpPanel; only the Add* calls became HelpContentWriter calls
    // and the admin-gated blocks read FiresRPGmaker's own admin state.
    internal static class FiresNpcsHelpContent
    {
        private const string ModId = "Verdant's Ascent";

        // Configures the shared help panel for FiresRPGmaker, registers this mod's sections, and creates
        // the panel. Called once from UIInitializer.
        public static void Install()
        {
            HelpPanel.Title = "Verdant's Ascent — Help & Reference";
            HelpPanel.FontProvider = () => UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaSerif);
            HelpPanel.AdminCheck = IsCurrentPlayerAdmin;
            HelpPanel.InputBlocker = GUIManager.BlockInput;
            Register();
            HelpPanel.Initialize();
        }

        private static void Register()
        {
            HelpRegistry.RegisterSection(ModId, "", "Getting Started", 0, BuildGettingStarted);

            HelpRegistry.RegisterSection(ModId, "Companions", "Companions Overview", 10, BuildCompanionsOverview);
            HelpRegistry.RegisterSection(ModId, "Companions", "Companion Commands", 11, BuildCompanionCommands);
            HelpRegistry.RegisterSection(ModId, "Companions", "Companion Idle Work", 12, BuildCompanionIdleWork);
            HelpRegistry.RegisterSection(ModId, "Companions", "Companion Management", 13, BuildCompanionManagement);
            HelpRegistry.RegisterSection(ModId, "Companions", "Combat & Abilities", 14, BuildCombatAbilities);

            HelpRegistry.RegisterSection(ModId, "Social", "Guilds", 20, BuildGuilds);
            HelpRegistry.RegisterSection(ModId, "Social", "Party", 21, BuildParty);
            HelpRegistry.RegisterSection(ModId, "Social", "Chat Channels", 22, BuildChatChannels);

            HelpRegistry.RegisterSection(ModId, "World", "NPC Types & Setup", 30, BuildNpcTypes);
            HelpRegistry.RegisterSection(ModId, "World", "Quests & Journal", 31, BuildQuestsAndJournal);
            HelpRegistry.RegisterSection(ModId, "World", "Dialogue System", 32, BuildDialogueSystem);
            HelpRegistry.RegisterSection(ModId, "World", "Territories", 33, BuildTerritories);
            HelpRegistry.RegisterSection(ModId, "World", "Compendium", 34, BuildCompendium);

            HelpRegistry.RegisterSection(ModId, "Interface", "Globe HUD Overlay", 40, BuildGlobeHud);
            HelpRegistry.RegisterSection(ModId, "Interface", "UI Customization", 41, BuildUICustomization);
            HelpRegistry.RegisterSection(ModId, "Interface", "Keyboard Shortcuts", 42, BuildKeyboardShortcuts);
            HelpRegistry.RegisterSection(ModId, "Interface", "Troubleshooting", 43, BuildTroubleshooting);

            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Quest Config Format", 50, BuildQuestConfigFormat, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Dialogue Config Format", 51, BuildDialogueConfigFormat, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Territory Config", 52, BuildTerritoryConfig, adminOnly: true);

            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "Config Reference", 60, BuildConfigReference, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "Progression & Key Locking", 61, BuildProgression, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "File & Folder Layout", 62, BuildFileLayout, adminOnly: true);

            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Admin Commands Overview", 70, BuildAdminCommandsOverview, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Companion Admin Commands", 71, BuildCompanionAdminCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Compendium Admin Commands", 72, BuildCompendiumAdminCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "FX Testing Commands", 73, BuildFxTestingCommands, adminOnly: true);
        }

        private static bool IsCurrentPlayerAdmin()
        {
            try
            {
                if (ZNet.instance == null) return false;
                if (ZNet.instance.IsServer() && !ZNet.instance.IsDedicated()) return true;
                var configSync = FiresRPGmaker.Instance?.configSync;
                if (configSync != null && configSync.IsAdmin) return true;
                return false;
            }
            catch { return false; }
        }

        private static void BuildGettingStarted(HelpContentWriter w)
        {
            w.Header("Getting Started");
            w.Paragraph("Verdant's Ascent is a full RPG framework for Valheim. It adds quest-giving NPCs, " +
                "branching dialogue trees, tamed companions with RPG archetypes and abilities, a guild and " +
                "party system, multi-channel chat, territory zones, a discovery compendium, and a custom " +
                "globe-style HUD overlay.");
            w.Divider();

            w.SubHeader("Player Quick Start");
            w.Bullet("1. Tame a creature using the vanilla Valheim taming system.");
            w.Bullet("2. Hold R while looking at the companion to open its command radial.");
            w.Bullet("3. Hold H while looking at the companion to toggle idle work behaviors.");
            w.Bullet("4. Press G to open the Guild screen, or use /gcreate <name> to start one.");
            w.Bullet("5. Press J to open the Quest Journal once you've accepted a quest.");
            w.Divider();

            w.SubHeader("Common Hotkeys");
            w.Bullet("E — Interact with an NPC, chest, workbench, etc.");
            w.Bullet("J — Open the Quest Journal (configurable in Hotkeys.QuestJournalKey)");
            w.Bullet("G — Open the Guild screen (configurable in Hotkeys.GuildScreenKey)");
            w.Bullet("Hold R on a companion — open the companion command radial");
            w.Bullet("Hold H on a companion — open the idle-behavior radial");
            w.Bullet("Shift+Middle Mouse on a target — ping a companion to act on it");
            w.Bullet("Tab (while typing in chat) — cycle chat channels");
            w.Bullet("Escape — close any open Verdant's Ascent panel");
            w.Paragraph("See the Keyboard Shortcuts section for the full reference.");
            w.Divider();

            w.SubHeader("Player Slash Commands");
            w.Bullet("/guild — open the guild screen (same as G)");
            w.Bullet("/gcreate <name> [description] — found a new guild");
            w.Bullet("/ginvite <player> — invite an online player");
            w.Bullet("/g <message> — guild-only chat");
            w.Bullet("/gping — drop a guild-only map ping at the cursor");
            w.Bullet("/invite <player> — invite to your party (separate from guilds)");
            w.Bullet("/leave  /disband — leave or disband your party");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: First-Time Server Setup");
                w.Bullet("Main config: BepInEx/config/com.Fire.FiresRPGmaker.cfg");
                w.Bullet("Set General.ServerAuthority = true to lock config changes to admins.");
                w.Bullet("Per-feature .cfg files live under BepInEx/config/FiresRPGmaker/.");
                w.Bullet("File watchers auto-reload .cfg changes on the server — no restart needed.");
                w.Bullet("Open the NPC admin panel by shift-clicking the NPC (or via admin access).");
                w.Bullet("See Config Reference, Progression & Key Locking, and File & Folder Layout.");
            }
        }

        private static void BuildNpcTypes(HelpContentWriter w)
        {
            w.Header("NPC Types & Setup");
            w.Paragraph("NPCs have four types, each offering a different interaction when a player presses E.");
            w.Divider();

            w.SubHeader("Quest NPC");
            w.Paragraph("Offers quests from a quest profile. Shows a quest interaction screen with " +
                "a list of available quests, details, and Accept/Cancel buttons.");
            w.Bullet("Requires: questProfile assigned to a .cfg profile name");

            w.SubHeader("Info NPC");
            w.Paragraph("Displays an info panel with a header, optional image, and rich text body. " +
                "Useful for lore, signs, or world-building content.");
            w.Bullet("Requires: infoProfile assigned to a ServerInfos config");

            w.SubHeader("Dialogue NPC");
            w.Paragraph("Runs branching dialogue trees with conditions, commands, and option icons. " +
                "Players navigate through dialogue nodes by clicking options.");
            w.Bullet("Requires: dialogueProfile assigned to a dialogue .cfg UID");

            w.SubHeader("Trader NPC");
            w.Paragraph("(Future) Trading interface with buy/sell inventories.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: NPC Configuration");
                w.Bullet("npcProfileName — Display name shown above the NPC");
                w.Bullet("displayNameOverride — Optional floating name override");
                w.Bullet("questProfile — Name of the quest profile from QuestProfiles/");
                w.Bullet("dialogueProfile — UID of the root dialogue node from Dialogues/");
                w.Bullet("infoProfile — Profile name from ServerInfos/");
                w.Bullet("modelOverride — Swap the NPC visual model");
                w.Paragraph("Open the admin panel by shift+clicking the NPC or using admin access. " +
                    "Use the tabs to switch between Quest Config, Info Config, Dialogue Editor, and Dressing Room.");
            }
        }

        private static void BuildQuestsAndJournal(HelpContentWriter w)
        {
            w.Header("Quests & Journal");
            w.Paragraph("Quests are offered by Quest NPCs and tracked in your Quest Journal. " +
                "Complete objectives to earn item and skill rewards.");
            w.Divider();

            w.SubHeader("Quest Lifecycle");
            w.Bullet("Available — quest appears in the NPC's list. Click to see details.");
            w.Bullet("Accepted — click Accept to start tracking. Progress shows in the Quest Journal.");
            w.Bullet("In Progress — kill targets or collect items to fill the objective counter.");
            w.Bullet("Completed — when all objectives are met, the quest auto-completes (if enabled).");
            w.Bullet("Cooldown — repeatable quests enter a cooldown period before becoming available again.");
            w.Divider();

            w.SubHeader("The Quest Journal");
            w.Bullet("Press J to open (Hotkeys.QuestJournalKey)");
            w.Bullet("Lists every active quest with current/target progress per objective");
            w.Bullet("Active quests show a progress bar (e.g., Boar 3/10)");
            w.Bullet("Completed objectives appear in green");
            w.Bullet("You can abandon active quests from the journal");
            w.Divider();

            w.SubHeader("Quest Timers (Top-Left HUD)");
            w.Paragraph("Some quests have a real-world time limit. While you have a timed quest active, " +
                "a countdown HUD appears in the top-left of the screen showing the remaining time. " +
                "If the timer expires, the quest fails and may go on cooldown.");
            w.Divider();

            w.SubHeader("Objectives");
            w.Paragraph("Each quest has one or more targets defined by a Valheim prefab name and an amount. " +
                "Kill-type quests track creature kills automatically.");
            w.Bullet("Example: Boar, 10 — kill 10 Boars");
            w.Bullet("Optional minimum level filter: Boar, 5, 2 — kill 5 Boars of level 2+");
            w.Divider();

            w.SubHeader("Rewards");
            w.Paragraph("Quests can reward items and/or skill experience.");
            w.Bullet("Item rewards: Coins, 50 — gives 50 Coins");
            w.Bullet("Skill XP rewards: Skill_Exp: Swords, 15 — gives 15 Swords XP");
            w.Bullet("Multiple rewards separated by | pipe character");
            w.Divider();

            w.SubHeader("Requirements");
            w.Paragraph("Quests can require prerequisites before they become available.");
            w.Bullet("HasCompletedQuest, QuestId — must have completed another quest first");
            w.Bullet("HasItem, PrefabName, Amount — must have items in inventory");
            w.Paragraph("Quests with unmet requirements show as locked in the quest list.");
            w.Divider();

            w.SubHeader("Status Icons");
            w.Bullet("Yellow — In Progress (currently tracking)");
            w.Bullet("Green — Available to accept");
            w.Bullet("Blue — On Cooldown (shows remaining time)");
            w.Bullet("Red — Missing Requirements");
        }

        private static void BuildQuestConfigFormat(HelpContentWriter w)
        {
            w.Header("Quest Config Format");
            w.Paragraph("Quest definitions are stored in .cfg files under:");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Quests/");
            w.Paragraph("Any .cfg file in this folder is loaded. Multiple quests can share one file.");
            w.Divider();

            w.SubHeader("Bracketed ID Format (Recommended)");
            w.CodeBlock("[MyQuestId]\n" +
                "QuestType = Kill\n" +
                "Name = Hunt the Boar\n" +
                "Description = Slay 10 boars to prove your strength.\n" +
                "QuestTarget = Boar, 10\n" +
                "QuestReward = Coins, 50 | Skill_Exp: Clubs, 15\n" +
                "CooldownDays = 7\n" +
                "TimeLimitMinutes = 0\n" +
                "Requirement = HasCompletedQuest, TutorialQuest\n" +
                "Autocomplete = true");
            w.Divider();

            w.SubHeader("Legacy Line-Based Format");
            w.CodeBlock("[BoarHunt=autocomplete]\n" +
                "Kill\n" +
                "Hunt the Boar\n" +
                "Slay 10 boars to prove your strength.\n" +
                "Boar, 10\n" +
                "Skill_Exp: Clubs, 15 | Item: Coins, 50\n" +
                "7\n" +
                "None");
            w.Paragraph("Line order: QuestType, Name, Description, Targets, Rewards, CooldownDays, Requirements. " +
                "Append =autocomplete to the quest ID to enable auto-completion.");
            w.Divider();

            w.SubHeader("Quest Profiles");
            w.Paragraph("Profiles assign quests to NPCs. Stored in:");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/QuestProfiles/\n\n" +
                "[ProfileName]\n" +
                "Quests = QuestId1, QuestId2, QuestId3");
            w.Paragraph("Set the NPC's questProfile field to the profile name.");
            w.Divider();

            w.SubHeader("Field Reference");
            w.Bullet("QuestType — Kill (more types planned)");
            w.Bullet("QuestTarget — PrefabName, Amount [, MinLevel]");
            w.Bullet("QuestTargetName — Optional display name override for the target");
            w.Bullet("QuestReward — Item: Name, Amt | Skill_Exp: Skill, Amt (pipe-separated)");
            w.Bullet("CooldownDays — Days before quest can be repeated (0 = no repeat)");
            w.Bullet("TimeLimitMinutes — Time limit in minutes (0 = no limit)");
            w.Bullet("Requirement — HasCompletedQuest, Id | HasItem, Name, Amt");
            w.Bullet("Autocomplete — true/false (complete when objectives met)");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Common Mistakes");
                w.Bullet("Quest ID must be unique across ALL .cfg files");
                w.Bullet("Prefab names are case-sensitive (Boar not boar)");
                w.Bullet("Rewards use | as separator, not commas between reward entries");
                w.Bullet("File watcher auto-reloads on save — no restart needed");
            }
        }

        private static void BuildDialogueSystem(HelpContentWriter w)
        {
            w.Header("Dialogue System");
            w.Paragraph("Dialogue NPCs offer branching conversations with conditional options, " +
                "commands, and item icons. The system is compatible with the Marketplace dialogue format.");
            w.Divider();

            w.SubHeader("How Dialogues Work");
            w.Bullet("Interact (E) with a Dialogue NPC to open the dialogue window.");
            w.Bullet("The NPC's text appears at the top, with numbered option buttons below.");
            w.Bullet("Click an option (or press 1-9) to select it.");
            w.Bullet("Options can transition to other dialogue nodes, execute commands, or both.");
            w.Bullet("Press Backspace to go back to the previous node.");
            w.Bullet("Press Escape to close the dialogue.");
            w.Divider();

            w.SubHeader("Conditional Options");
            w.Paragraph("Options can have conditions that control when they appear.");
            w.Bullet("Options that fail conditions are hidden by default.");
            w.Bullet("Set AlwaysVisible: true to show them greyed-out with an error message.");
            w.Bullet("Conditions support AND (comma-separated) and OR (|| separated) logic.");
            w.Divider();

            w.SubHeader("Commands");
            w.Paragraph("Options can trigger game commands when clicked.");
            w.Bullet("GiveItem — Add items to the player's inventory");
            w.Bullet("RemoveItem — Remove items from inventory");
            w.Bullet("GiveQuest / CompleteQuest — Quest management");
            w.Bullet("Teleport — Move the player to coordinates");
            w.Bullet("SetGlobalKey / RemoveGlobalKey — World progression keys");
            w.Bullet("AddPin — Add a map pin");
            w.Bullet("Spawn — Spawn creatures or objects");
            w.Bullet("ShowMessage / PlaySFX — UI feedback");
        }

        private static void BuildDialogueConfigFormat(HelpContentWriter w)
        {
            w.Header("Dialogue Config Format");
            w.Paragraph("Dialogue trees are stored in .cfg files under:");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Dialogues/");
            w.Divider();

            w.SubHeader("Node Format");
            w.CodeBlock("[unique_node_id]\n" +
                "The NPC says this text to the player.\n" +
                "Text: Talk to me | Transition: next_node\n" +
                "Text: Buy sword | Command: GiveItem,SwordIron,1 | Condition: HasItem,Coins,100\n" +
                "Text: Goodbye | Transition: END");
            w.Paragraph("Each node starts with [uid], then the dialogue text, then one or more option lines.");
            w.Divider();

            w.SubHeader("Option Fields (pipe-separated)");
            w.Bullet("Text: — Display text for the option button");
            w.Bullet("Transition: — UID of the next dialogue node (or END)");
            w.Bullet("Command: — Command to execute (GiveItem, Teleport, etc.)");
            w.Bullet("Condition: — Requirement to show the option");
            w.Bullet("Icon: — Prefab name for an icon on the button");
            w.Bullet("AlwaysVisible: true — Show greyed-out when condition fails");
            w.Bullet("OverrideError: — Custom error text when condition fails");
            w.Bullet("Color: R,G,B — Custom text color (byte values 0-255)");
            w.Divider();

            w.SubHeader("Conditions Reference");
            w.CodeBlock("HasItem, PrefabName, Amount\n" +
                "NotHasItem, PrefabName\n" +
                "HasQuest, QuestId\n" +
                "NotHasQuest, QuestId\n" +
                "HasCompletedQuest, QuestId\n" +
                "SkillMore, SkillName, Level\n" +
                "SkillLess, SkillName, Level\n" +
                "GlobalKey, KeyName\n" +
                "NotGlobalKey, KeyName\n" +
                "IsAdmin | IsNotAdmin\n" +
                "Biome, BiomeName");
            w.Paragraph("Multiple conditions on one option use commas (AND). " +
                "Separate condition groups with || for OR logic.");
            w.Divider();

            w.SubHeader("Commands Reference");
            w.CodeBlock("GiveItem, PrefabName, Amount\n" +
                "RemoveItem, PrefabName, Amount\n" +
                "GiveQuest, QuestId\n" +
                "CompleteQuest, QuestId\n" +
                "Teleport, X, Y, Z\n" +
                "Spawn, PrefabName, Amount\n" +
                "SetGlobalKey, KeyName\n" +
                "RemoveGlobalKey, KeyName\n" +
                "AddPin, Name, X, Y, Z\n" +
                "Heal | Damage, Amount\n" +
                "ShowMessage, Text\n" +
                "PlaySFX, ClipName");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Dialogue Tips");
                w.Bullet("Set the NPC's dialogueProfile to the [uid] of the root node");
                w.Bullet("Use @command prefix for auto-execute commands (no button shown)");
                w.Bullet("RandomTransition: uid1,uid2 picks a random next node");
                w.Bullet("RandomCommand: weight,Command for weighted random commands");
                w.Bullet("File watcher auto-reloads dialogue files on save");
            }
        }

        private static void BuildCompanionsOverview(HelpContentWriter w)
        {
            w.Header("Companions Overview");
            w.Paragraph("Companions are tamed creatures that follow you, fight alongside you with " +
                "RPG archetypes and abilities, and perform idle work tasks when left in Stay mode. " +
                "All state is persisted through Valheim's ZDO system, so companions survive logout, " +
                "server restart, and world reload.");
            w.Divider();

            w.SubHeader("Getting a Companion");
            w.Bullet("Tame a creature using the vanilla Valheim taming system (feed it the right food).");
            w.Bullet("Once tamed, it follows you and fights enemies that threaten you.");
            w.Bullet("Companions can be set to Follow or Stay mode (Stay enables idle work).");
            w.Bullet("Wild companions (named NPC squads) also spawn in some biomes — see WildSpawn.");
            w.Divider();

            w.SubHeader("Companion HUD");
            w.Bullet("Billboard — floating health bar and name above each companion's head");
            w.Bullet("Group HUD — right-side panel showing all companion status bars (drag to move)");
            w.Bullet("Map marker — companions appear as pins on the world map");
            w.Bullet("Both HUD elements can be toggled in [Companions] (ShowBillboardHealthBar, ShowGroupHud).");
            w.Divider();

            w.SubHeader("Base Archetypes (8)");
            w.Paragraph("Each companion has a primary archetype that determines its stat bonuses, " +
                "weapon preferences, and ability tree. Equipping a weapon the archetype prefers unlocks " +
                "its ability progression.");
            w.Bullet("Guardian (Tank) — shield + 1H melee. High stamina, taunts, protects allies.");
            w.Bullet("Paladin — shield + mace/sword. Balanced offense/defense, can heal allies.");
            w.Bullet("Berserker — two-handed or dual wield. High damage, rage at low health.");
            w.Bullet("Rogue — daggers/knives. Backstab bonuses, dodge, high mobility.");
            w.Bullet("Monk — unarmed/clubs. Chi abilities, attack speed, stamina regen.");
            w.Bullet("Ranger — bow/crossbow. Long-range, kiting, draw-speed bonuses.");
            w.Bullet("Mage — offensive staff. Eitr regen, elemental damage, AoE.");
            w.Bullet("Healer (Cleric) — support staff. Buffs, heals, eitr pool.");
            w.Divider();

            w.SubHeader("Hybrid Archetypes");
            w.Paragraph("Companions can have a secondary (sub) archetype which combines with the primary " +
                "to form a hybrid with a unique name and special ability. There are 50+ hybrids " +
                "(70% main / 30% sub stat blend). A few examples:");
            w.Bullet("Tank + Paladin → Crusader (Divine Bulwark)");
            w.Bullet("Tank + Berserker → Juggernaut (Unstoppable Charge)");
            w.Bullet("Rogue + Mage → Spellthief");
            w.Bullet("Ranger + Mage → Arcane Archer");
            w.Bullet("Monk + Healer → Chi Healer");
            w.Paragraph("See the Combat & Abilities section for how abilities unlock and how group " +
                "synergy combos work.");
            w.Divider();

            w.SubHeader("Companion Progression");
            w.Paragraph("Companions level up from combat experience and idle work, gaining stat scaling " +
                "and unlocking new abilities. View progression in the Companion Stats screen.");
            w.Bullet("Stat bonuses scale by level (LevelScalingRate, ~2% per level by default)");
            w.Bullet("Abilities unlock at archetype-specific thresholds (Novice → Ultimate)");
            w.Bullet("Skills track per companion (Swords, Bows, BloodMagic, etc.)");
        }

        private static void BuildCompanionCommands(HelpContentWriter w)
        {
            w.Header("Companion Commands");
            w.Paragraph("There are four ways to direct a companion: the command radial, the idle-behavior " +
                "radial, the Shift+MMB target ping, and Shift+key NPC controls. All require you to be " +
                "looking at the companion (or, for ping, looking at the target).");
            w.Divider();

            w.SubHeader("Command Radial — Hold R");
            w.Paragraph("Look at one of your companions and hold R for ~0.15s. A radial menu opens with " +
                "high-level commands. Release R to dismiss.");
            w.Bullet("Follow / Stay — toggle between following you and holding position");
            w.Bullet("Open inventory / equipment screens");
            w.Bullet("View stats and attribute screens");
            w.Bullet("Issue specific orders (return home, dismiss, etc.)");
            w.Divider();

            w.SubHeader("Idle Behavior Radial — Hold H");
            w.Paragraph("Look at a companion and hold H to open the idle-behavior toggle radial. " +
                "Each segment turns one Stay-mode work behavior on or off for that companion.");
            w.Bullet("Cooking, Fishing, Farming, Wood Gathering, Resource Gathering");
            w.Bullet("Chest Deposit, Loot Pickup, Smart Storage");
            w.Bullet("Fire Tending, Workstation Interaction, Crafting Upgrade, Smelter");
            w.Bullet("Building Repair, Bow Training");
            w.Paragraph("Per-companion toggles are stored on the companion's ZDO and persist.");
            w.Divider();

            w.SubHeader("Target Ping — Shift + Middle Mouse");
            w.Paragraph("Hold Shift and click the middle mouse button on something while a companion is " +
                "near you. The companion runs to that target and performs the appropriate action.");
            w.Bullet("Enemy / creature → priority attack target");
            w.Bullet("Chair / bench / stool → pathfind and sit");
            w.Bullet("Archery target → begin bow training");
            w.Bullet("Ground / terrain → move to that position");
            w.Bullet("Destructible → attack it");
            w.Bullet("Container → move to it (loot coming later)");
            w.Paragraph("A visible ping marker appears at the target for ~3 seconds. Commands clear " +
                "after completion or after 30 seconds.");
            w.Divider();

            w.SubHeader("Static NPC Controls");
            w.Paragraph("Companions can be 'placed' as stationary NPCs (innkeepers, vendors, decoration). " +
                "While looking at the companion:");
            w.Bullet("Shift + P — place / station the NPC at its current position");
            w.Bullet("Shift + U — unstation back into companion mode");
            w.Bullet("Shift + I — toggle idle wandering for the placed NPC");
            w.Divider();

            w.SubHeader("Idle Wander (Stay mode)");
            w.Bullet("Default wander radius: 20m (Companions.Behavior.IdleWanderRadius)");
            w.Bullet("Return-home radius: 25m (Companions.Behavior.ReturnHomeRadius)");
            w.Bullet("Stay-mode work search radius: 50m (Companions.Behavior.StayModeWorkRadius)");
            w.Bullet("Each companion can override these via its own ZDO settings.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Behavior Config");
                w.Bullet("IdleWanderRadius — default wander radius (5-100m)");
                w.Bullet("ReturnHomeRadius — auto-return threshold (10-100m)");
                w.Bullet("StayModeWorkRadius — work-task search range (10-100m)");
                w.Bullet("ChestSearchRadius — chest search for smelters/kilns/deposit (5-100m)");
                w.Bullet("ChestAutoSortRadius — auto-sort range for chest organization (5-30m)");
            }
        }

        private static void BuildCompanionIdleWork(HelpContentWriter w)
        {
            w.Header("Companion Idle Work");
            w.Paragraph("When a companion is in Stay mode, it scans for available work within its " +
                "StayModeWorkRadius (default 50m) and performs whichever behaviors are enabled on the " +
                "Hold-H idle-behavior radial. Each behavior can be toggled per-companion.");
            w.Divider();

            w.SubHeader("Gathering Behaviors");
            w.Bullet("Wood Gathering — chops fallen trees and collects logs");
            w.Bullet("Resource Gathering — mines stone/copper/tin and harvests other resources");
            w.Bullet("Loot Pickup — collects dropped items and ground loot within radius");
            w.Bullet("Fishing — fishes from nearby shorelines (requires fishing rod + bait)");
            w.Divider();

            w.SubHeader("Farming");
            w.Bullet("Multi-phase farming: scouts plantable ground, plants seeds, harvests when grown");
            w.Bullet("Spaces plants automatically using the cultivator's spacing rules");
            w.Bullet("Picks the right seed/sapling from nearby chests if available");
            w.Divider();

            w.SubHeader("Storage & Cooking");
            w.Bullet("Chest Deposit — sorts collected items into nearby chests by category");
            w.Bullet("Smart Storage — auto-sorts items across multiple chests when you open one");
            w.Bullet("Cooking — operates cooking stations and ovens, feeds raw food, removes cooked");
            w.Divider();

            w.SubHeader("Crafting & Workstations");
            w.Bullet("Fire Tending — keeps campfires, torches, hearths, and bonfires fueled");
            w.Bullet("Smelter Operator — runs smelters, kilns, blast furnaces (fuel + ore from chests)");
            w.Bullet("Workstation Interaction — operates stations that need periodic input");
            w.Bullet("Crafting Upgrade — runs workbenches and forges to upgrade items");
            w.Bullet("Building Repair — repairs damaged structures within radius (with hammer)");
            w.Divider();

            w.SubHeader("Training");
            w.Bullet("Bow Training — practices on nearby archery targets to gain Bows skill XP");
            w.Divider();

            w.SubHeader("How It Works");
            w.Paragraph("An occupancy manager prevents two companions from using the same " +
                "smelter/workbench/chest at once. Companions pick tasks by proximity and availability. " +
                "Toggle individual behaviors with the Hold-H radial — disabled behaviors are skipped " +
                "even if the work is available.");
        }

        private static void BuildCompanionManagement(HelpContentWriter w)
        {
            w.Header("Companion Management");
            w.Paragraph("Four screens give you detailed control of each companion. Open them from " +
                "the Hold-R command radial, or from buttons inside one screen that link to the others.");
            w.Divider();

            w.SubHeader("Roster Screen");
            w.Bullet("Lists every companion you own (across all worlds on this character)");
            w.Bullet("Shows name, archetype, level, location, and current status");
            w.Bullet("Click a row to open that companion's other screens");
            w.Bullet("Search/filter by name, archetype, or location");
            w.Divider();

            w.SubHeader("Stats Screen");
            w.Bullet("Health, stamina, eitr — current and max values");
            w.Bullet("Damage breakdown (slash/blunt/pierce/fire/frost/poison/spirit/lightning)");
            w.Bullet("Armor, block, parry, movement speed, and other derived stats");
            w.Bullet("Skill levels (Swords, Bows, BloodMagic, etc.) with XP progress bars");
            w.Bullet("Kill count and combat experience");
            w.Divider();

            w.SubHeader("Attribute Screen");
            w.Bullet("Allocate level-up attribute points (Strength/Agility/Constitution/etc.)");
            w.Bullet("Each attribute scales specific stats (e.g., STR raises melee damage)");
            w.Bullet("Reset / respec available via the screen's button");
            w.Divider();

            w.SubHeader("Inventory Screen");
            w.Bullet("Equip weapons, armor, shield, ammo, and consumables");
            w.Bullet("Equipped weapons gate which abilities the companion can use");
            w.Bullet("Drag items between your inventory and the companion's");
            w.Bullet("Companions consume potions, healing items, and food automatically when needed");
        }

        private static void BuildCombatAbilities(HelpContentWriter w)
        {
            w.Header("Combat & Abilities");
            w.Paragraph("Each archetype has an ability tree that unlocks as the companion gains levels. " +
                "Companions cast abilities automatically based on context — there is no manual targeting. " +
                "Effective combat depends on archetype, equipped weapon, level, and group composition.");
            w.Divider();

            w.SubHeader("Ability Unlock Progression");
            w.Bullet("Each archetype has tiers: Novice → Apprentice → Expert → Master → Ultimate");
            w.Bullet("Abilities unlock at level thresholds (visible in the Stats screen)");
            w.Bullet("Some abilities require a specific weapon type to be equipped to fire");
            w.Bullet("Hybrid archetypes get an extra unique ability from their main+sub combination");
            w.Divider();

            w.SubHeader("Group Roles");
            w.Paragraph("When multiple companions follow you, the group role manager assigns " +
                "tactical roles automatically based on archetype:");
            w.Bullet("Frontline (Tank, Paladin, Berserker) — engage and absorb damage");
            w.Bullet("Flank (Rogue, Monk) — flanking attacks, mobility");
            w.Bullet("Backline (Ranger, Mage) — stay at range, kite, AoE");
            w.Bullet("Support (Healer, sub-Healer hybrids) — heal and buff allies");
            w.Divider();

            w.SubHeader("Group Synergy Combos");
            w.Paragraph("Certain ability combinations between two companions trigger synergy bonuses. " +
                "Examples: a Mage's Frost-staff slow followed by a Berserker's heavy strike adds bonus " +
                "damage; a Paladin's blessing followed by a Ranger's shot adds holy damage.");
            w.Divider();

            w.SubHeader("Status Effects");
            w.Paragraph("Companions apply and receive a wide range of mod-defined status effects. " +
                "Categories include:");
            w.Bullet("Defensive — Fortify, Iron Wall, Divine Protection, Arcane Shield, Sanctuary");
            w.Bullet("Offensive — Berserk Rage, Warcry, Holy Smite, Hunter's Mark, Death Mark");
            w.Bullet("Crowd control — Caltrops, Poison, Stealth, Rooted, Slowed");
            w.Bullet("Support / heal — Lay on Hands, Inner Peace, Purify, Divine Hymn");
            w.Bullet("Ultimate — Avatar of Light/War/Life, Immortal Stance, Way of Perfection");
            w.Divider();

            w.SubHeader("Taunt");
            w.Paragraph("Tank-class companions periodically taunt nearby enemies, drawing their " +
                "attention away from you and your other companions. Taunt range scales with level.");
        }

        private static void BuildGuilds(HelpContentWriter w)
        {
            w.Header("Guilds");
            w.Paragraph("Guilds are persistent, server-wide groups of players. They have ranks, a " +
                "shared chat channel, map pings, member rosters, applications, and shared storage. " +
                "Press G or type /guild to open the guild screen.");
            w.Divider();

            w.SubHeader("Joining or Founding a Guild");
            w.Bullet("/gcreate <name> [description] — found a new guild (you become Leader)");
            w.Bullet("Browse open guilds in the Search tab and click Apply");
            w.Bullet("/ginvite <player> — invite an online player (officer rank or higher)");
            w.Bullet("Invited players get a popup; applications are reviewed by officers");
            w.Divider();

            w.SubHeader("Guild Screen — press G");
            w.Bullet("Main — overview, motd, members online, your rank");
            w.Bullet("Members — full roster with ranks; promote/demote/kick if permitted");
            w.Bullet("Chat — guild-only chat history (persisted on the server)");
            w.Bullet("Search — find and apply to other guilds");
            w.Bullet("Create — new-guild form (if AllowGuildCreation is enabled)");
            w.Divider();

            w.SubHeader("Slash Commands");
            w.Bullet("/guild — open the guild screen");
            w.Bullet("/gcreate <name> [description] — found a new guild");
            w.Bullet("/ginvite <player> — invite an online player by name");
            w.Bullet("/gleave — leave your current guild");
            w.Bullet("/gdisband — permanently dissolve your guild (Leader only)");
            w.Bullet("/g <message> — send a one-off message to guild chat");
            w.Bullet("/gping — drop a guild-only ping at your map cursor (Shift+ping also works)");
            w.Divider();

            w.SubHeader("Ranks & Permissions");
            w.Bullet("Member — chat, see roster, leave guild");
            w.Bullet("Officer — invite, kick members, edit motd");
            w.Bullet("Coleader — promote/demote up to officer, edit guild details");
            w.Bullet("Leader — full control, can transfer leadership or disband");
            w.Divider();

            w.SubHeader("Guild Chat");
            w.Paragraph("Either select the Guild channel in the chat-column UI and type normally, " +
                "or prefix any message with /g. Messages appear with a coloured [GuildName] tag and " +
                "are persisted server-side, so the chat tab in the guild screen always shows recent " +
                "history (even what was said while you were offline).");
            w.Divider();

            w.SubHeader("Companion Access & Ownership Transfer");
            w.Bullet("Guildmates can interact with each other's companions (configurable)");
            w.Bullet("Ownership of a companion can be transferred to another guild member");
            w.Bullet("Useful for shared bases where any member can manage the work-companions");
            w.Divider();

            w.SubHeader("Guild Map Pings");
            w.Bullet("Hold Shift while placing a map ping — only guildmates see it");
            w.Bullet("Or use /gping anywhere in the world to ping at the map cursor");
            w.Divider();

            w.SubHeader("Limits");
            w.Bullet("Default 20 members per guild (Guilds.MaxGuildMembers, 1-200)");
            w.Bullet("Default 100 guilds per world (Guilds.MaxGuildsPerWorld, 1-1000)");
            w.Bullet("Whole system can be disabled with Guilds.GuildsEnabled = false");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Guild Server Settings");
                w.Bullet("Guilds.GuildsEnabled — master toggle (synced)");
                w.Bullet("Guilds.AllowGuildCreation — set false to lock the world to existing guilds");
                w.Bullet("Guilds.MaxGuildMembers / MaxGuildsPerWorld — server-wide caps");
                w.Bullet("Guild data persists in BepInEx/config/FiresRPGmaker/Guilds/ on the server");
            }
        }

        private static void BuildParty(HelpContentWriter w)
        {
            w.Header("Party");
            w.Paragraph("Parties are short-form, ad-hoc groups for a single play session — separate " +
                "from guilds. A party gives you grouped status visibility and is the building block " +
                "for shared XP / loot rules.");
            w.Divider();

            w.SubHeader("Slash Commands");
            w.Bullet("/invite <player> — send a YesNo invite popup to an online player");
            w.Bullet("/leave — leave the party (leadership auto-transfers if you were leader)");
            w.Bullet("/disband — tear down the party for everyone (Leader only)");
            w.Divider();

            w.SubHeader("How It Works");
            w.Bullet("Receiving a party invite shows a YesNo popup; one popup at a time per client");
            w.Bullet("Party state is saved to your character data and survives logout");
            w.Bullet("If the leader leaves, leadership transfers to the longest-tenured member");
            w.Bullet("A member who is silently kicked (Sync without them) clears local state");
            w.Divider();

            w.SubHeader("Party vs Guild");
            w.Bullet("Party — temporary, small (a handful of players), no shared storage or chat tab");
            w.Bullet("Guild — persistent, large, shared chat/storage/ranks/applications");
            w.Bullet("You can be in both at the same time; they're independent systems");
        }

        private static void BuildChatChannels(HelpContentWriter w)
        {
            w.Header("Chat Channels");
            w.Paragraph("Verdant's Ascent extends Valheim's chat with a channel selector. The active " +
                "channel determines who sees your message — no slash-command needed for normal typing. " +
                "Your selection is saved between sessions.");
            w.Divider();

            w.SubHeader("Channels");
            w.Bullet("Local — vanilla normal-talk range (~15m). Default channel.");
            w.Bullet("Global — server-wide shout. Auto-prepends the shout behavior.");
            w.Bullet("Group — placeholder for future ad-hoc group routing (currently a stub).");
            w.Bullet("Guild — guildmates only. Wired through the guild RPC pipeline.");
            w.Divider();

            w.SubHeader("Switching Channels");
            w.Bullet("Press Enter to open chat, then Tab to cycle through channels");
            w.Bullet("Or click a channel button on the chat column UI to pick directly");
            w.Bullet("The active channel is shown next to the chat input with its colour");
            w.Bullet("Selection persists across logout (saved to [Chat] ActiveChannel)");
            w.Divider();

            w.SubHeader("One-off /g Messages");
            w.Paragraph("Even if your active channel is Local or Global, you can send a single " +
                "guild-channel message by prefixing with /g — handy for quick coordination without " +
                "switching channels.");
            w.Divider();

            w.SubHeader("Chat Media Embeds");
            w.Paragraph("Image and GIF URLs posted in chat auto-render as inline embeds in the " +
                "chat window. Animated GIFs play in place using the bundled GIF decoder. Useful for " +
                "sharing maps, screenshots, or memes during play.");
        }

        private static void BuildCompendium(HelpContentWriter w)
        {
            w.Header("Compendium");
            w.Paragraph("The Compendium is a discovery-based reference book that fills in as you " +
                "encounter creatures, items, and biomes. Entries are gated by what the player " +
                "actually sees, so it doubles as a progress tracker.");
            w.Divider();

            w.SubHeader("How Discovery Works");
            w.Bullet("Encountering a creature for the first time unlocks its compendium entry");
            w.Bullet("Picking up or being damaged by an item unlocks the item entry");
            w.Bullet("Entering a biome unlocks the biome entry");
            w.Bullet("Locked entries show as silhouettes with hint text");
            w.Divider();

            w.SubHeader("Entry Contents");
            w.Bullet("Creatures — health, damage types, weak/resistant elements, drop table");
            w.Bullet("Items — stats, where to find, recipes that use it");
            w.Bullet("Biomes — typical creatures, weather, hazards");
            w.Divider();

            w.SubHeader("Persistence");
            w.Paragraph("Compendium discoveries are tied to your character and persist across " +
                "worlds and server restarts.");
        }

        private static void BuildProgression(HelpContentWriter w)
        {
            w.Header("Progression & Key Locking");
            w.Paragraph("This admin-facing module controls how Valheim's biome / boss / item gating " +
                "works on the server. It supports both vanilla global keys and per-player private " +
                "keys, and can lock specific player actions behind any key.");
            w.Divider();

            w.SubHeader("Global vs Private Keys");
            w.Bullet("Vanilla global keys are world-wide flags (defeated_eikthyr, etc.)");
            w.Bullet("Private keys are per-player — defeating a boss only unlocks it for you");
            w.Bullet("Progression.UsePrivateKeys — switch the world to per-player progression");
            w.Bullet("Progression.UsePrivateRaids — raid eligibility uses private keys too");
            w.Divider();

            w.SubHeader("Key List Filters");
            w.Bullet("BlockAllGlobalKeys — block every vanilla global key by default");
            w.Bullet("BlockedGlobalKeys / AllowedGlobalKeys — comma-separated whitelist/blacklist");
            w.Bullet("EnforcedGlobalKeys — keys that are always set, regardless of progression");
            w.Bullet("BlockedPrivateKeys / AllowedPrivateKeys / EnforcedPrivateKeys — same for private keys");
            w.Divider();

            w.SubHeader("Action Locks");
            w.Paragraph("Eight actions can each be locked behind a specific key. Players who don't " +
                "have the key can't perform the action — useful for staggered server progression.");
            w.Bullet("LockEquipment — equipment slots gated by LockEquipmentKey");
            w.Bullet("LockCrafting / LockBuilding / LockCooking / LockEating");
            w.Bullet("LockTaming — gate animal taming behind a key");
            w.Bullet("LockGuardianPower — gate forsaken-altar powers");
            w.Bullet("LockBossSummons — gate boss summon altars");
            w.Divider();

            w.SubHeader("Player Feedback");
            w.Bullet("UseBlockedMessage — show a configurable message when an action is blocked");
            w.Bullet("UseBlockedEffect — play a denial visual/audio cue");
            w.Bullet("BlockedMessage — text shown to the player");
            w.Divider();

            w.SubHeader("Admin Bypass");
            w.Bullet("AdminBypassProgression — admins ignore all progression locks (default true)");
            w.Paragraph("All progression settings are synced from the server, so clients don't need " +
                "to edit their config.");
        }

        private static void BuildTerritories(HelpContentWriter w)
        {
            w.Header("Territories");
            w.Paragraph("Territories are defined zones in the world with special rules and effects. " +
                "They appear on the minimap overlay and display a HUD notification on entry.");
            w.Divider();

            w.SubHeader("Territory Effects");
            w.Bullet("NoBuild — Building disabled inside the zone");
            w.Bullet("NoPickaxe — Pickaxe usage disabled");
            w.Bullet("NoPvP — PvP damage disabled");
            w.Bullet("Pushback — Players pushed out of the zone");
            w.Bullet("MoveSpeedMultiplier — Speed boost or slowdown");
            w.Bullet("IncreasedPlayerDamage — Damage multiplier");
            w.Bullet("ForceEnvironment — Override weather/atmosphere");
            w.Bullet("CustomSpawn — Spawn creatures inside the zone");
            w.Bullet("AddMonsterLevel — Add star levels to spawned creatures");
            w.Bullet("DropMultiplier — Loot multiplier");
            w.Bullet("MapReveal — Reveal map area on entry");
            w.Divider();

            w.SubHeader("Map & HUD");
            w.Paragraph("Territories show as colored overlays on the minimap. " +
                "A HUD notification displays the territory name when you enter or leave a zone.");
            w.Bullet("Map overlay can be toggled in config (Territories.EnableMapOverlay)");
            w.Bullet("3D debug visualizers available for admins (Territories.EnableWorldVisualizers)");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Territory Management");
                w.Bullet("AdminBypassTerritoryRestrictions — Admins ignore zone restrictions");
                w.Bullet("Territories are defined in .cfg files under Territories/");
                w.Bullet("See the Territory Config section for the file format");
            }
        }

        private static void BuildTerritoryConfig(HelpContentWriter w)
        {
            w.Header("Territory Config Format");
            w.Paragraph("Territory definitions are stored in .cfg files under:");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Territories/");
            w.Divider();

            w.SubHeader("Format (line-based)");
            w.CodeBlock("[territory_name]\n" +
                "Circle\n" +
                "X, Z, Radius\n" +
                "R, G, B, ShowOnMap\n" +
                "Flag1, Flag2, Key=Value\n" +
                "ALL");
            w.Divider();

            w.SubHeader("Lines Explained");
            w.Bullet("Line 1: [name] — Unique territory identifier");
            w.Bullet("Line 2: Shape — Circle (more shapes planned)");
            w.Bullet("Line 3: Position — X, Z, Radius (world coordinates)");
            w.Bullet("Line 4: Color — R, G, B, ShowOnMap (0-255, true/false)");
            w.Bullet("Line 5: Flags — Comma-separated effects (see Territory Effects)");
            w.Bullet("Line 6: Applies to — ALL or specific player names");
            w.Divider();

            w.SubHeader("Example: Safe Zone");
            w.CodeBlock("[starter_zone]\n" +
                "Circle\n" +
                "0, 0, 50\n" +
                "0, 255, 0, true\n" +
                "NoBuild, NoPickaxe, MoveSpeedMultiplier=1.2\n" +
                "ALL");
            w.Divider();

            w.SubHeader("Custom Spawn Format");
            w.CodeBlock("CustomSpawn=PrefabName, Amount, Level, X, Y, Z, RespawnSeconds");
        }

        private static void BuildGlobeHud(HelpContentWriter w)
        {
            w.Header("Globe HUD Overlay");
            w.Paragraph("Replaces the vanilla health/stamina bars with a globe-style HUD overlay " +
                "showing Health, Stamina, Eitr, and Adrenaline as filled orbs.");
            w.Divider();

            w.SubHeader("Displays");
            w.Bullet("Health — Red globe fill");
            w.Bullet("Stamina — Olive/yellow globe fill");
            w.Bullet("Eitr — Purple globe fill");
            w.Bullet("Adrenaline — Horizontal bar fill");
            w.Divider();

            w.SubHeader("Repositioning");
            w.Paragraph("Open the ESC menu to enter reposition mode.");
            w.Bullet("Drag the HUD to move it anywhere on screen.");
            w.Bullet("Double-click to enter resize mode with corner and edge handles.");
            w.Bullet("Corner handles scale proportionally; edge handles stretch.");
            w.Bullet("Double-click again or close ESC menu to exit resize mode.");
            w.Bullet("Position and scale are saved automatically (persists across sessions).");
            w.Divider();

            w.SubHeader("Low Resource Pulse");
            w.Paragraph("When health, stamina, or eitr drops below 25%, the globe pulses " +
                "to draw your attention.");
        }

        private static void BuildUICustomization(HelpContentWriter w)
        {
            w.Header("UI Customization");
            w.Paragraph("Customize fonts and UI appearance through the BepInEx config.");
            w.Divider();

            w.SubHeader("Font Options");
            w.Bullet("PrimaryFont — Headers and titles (default: Valheim_Prstartk)");
            w.Bullet("DecorativeFont — Quest titles, Viking-themed text (default: Valheim_Norse)");
            w.Bullet("BodyFont — Descriptions and buttons (default: Valheim_AveriaSerif)");
            w.Divider();

            w.SubHeader("Available Fonts");
            w.Bullet("Valheim_Prstartk — Retro 80s pixel-style (Valheim default)");
            w.Bullet("Valheim_AveriaSerif — Elegant serif, clean and readable");
            w.Bullet("Valheim_Norse — Viking runic decorative");
            w.Bullet("Valheim_AveriaSans — Clean sans-serif, modern look");
            w.Bullet("Valheim_Rune — Runic symbols for special use");
            w.Divider();

            w.SubHeader("HUD Toggles");
            w.Bullet("ShowBillboardHealthBar — Floating bars above companions");
            w.Bullet("ShowGroupHud — Right-side companion status panel");
        }

        private static void BuildConfigReference(HelpContentWriter w)
        {
            w.Header("BepInEx Config Reference");
            w.Paragraph("All settings live in BepInEx/config/com.Fire.FiresRPGmaker.cfg. " +
                "Settings tagged [Synced] are pushed from the server and override client values.");
            w.Divider();

            w.SubHeader("General");
            w.Bullet("VerboseLogging — enable detailed debug logs (default: false)");
            w.Bullet("ServerAuthority — lock config to admin-only changes (default: false)");
            w.Divider();

            w.SubHeader("Hotkeys");
            w.Bullet("QuestJournalKey — Quest Journal hotkey (default: J, set None to disable)");
            w.Bullet("GuildScreenKey — Guild screen hotkey (default: G, set None to disable)");
            w.Divider();

            w.SubHeader("Guilds [Synced]");
            w.Bullet("GuildsEnabled — master toggle for the guild system (default: true)");
            w.Bullet("AllowGuildCreation — let players found new guilds (default: true)");
            w.Bullet("MaxGuildMembers — cap per guild, 1-200 (default: 20)");
            w.Bullet("MaxGuildsPerWorld — total guild cap, 1-1000 (default: 100)");
            w.Divider();

            w.SubHeader("Chat");
            w.Bullet("ActiveChannel — last-selected channel (Local/Global/Group/Clan, persists)");
            w.Divider();

            w.SubHeader("Territories");
            w.Bullet("AdminBypassTerritoryRestrictions — admins ignore zone rules (default: true)");
            w.Bullet("EnableWorldVisualizers — 3D debug visualizers (default: false)");
            w.Bullet("EnableMapOverlay — minimap territory overlay (default: true)");
            w.Bullet("EnableDeepNorthMapOverlay — Deep North icy-blue tint (default: true; disable when using mods that draw their own)");
            w.Divider();

            w.SubHeader("Companions");
            w.Bullet("ShowBillboardHealthBar — floating HP bar above companions (default: true)");
            w.Bullet("ShowGroupHud — right-side group HUD panel (default: true)");
            w.Divider();

            w.SubHeader("UI");
            w.Bullet("UseGlobeHud — globe-style health/stamina/eitr/adrenaline HUD (default: true; false reverts to vanilla bars)");
            w.Divider();

            w.SubHeader("UI.GroupHud");
            w.Bullet("MaxRowsPerColumn — rows visible before scrolling, 1-20 (default: 8)");
            w.Bullet("Columns — 1 (single list) or 2 (side-by-side) (default: 1)");
            w.Bullet("DefaultPosX — X offset from top-right (default: -10)");
            w.Bullet("DefaultPosY — Y offset from top-right (default: -200)");
            w.Divider();

            w.SubHeader("Companions.Behavior");
            w.Bullet("IdleWanderRadius — Stay mode wander range, 5-100m (default: 20)");
            w.Bullet("ReturnHomeRadius — auto-return threshold, 10-100m (default: 25)");
            w.Bullet("ChestSearchRadius — chest range for smelters/kilns, 5-100m (default: 50)");
            w.Bullet("StayModeWorkRadius — work-task search range, 10-100m (default: 50)");
            w.Bullet("ChestAutoSortRadius — auto-sort range, 5-30m (default: 10)");
            w.Divider();

            w.SubHeader("Underwater Bubble");
            w.Bullet("MinRadius — bubble min size (default: 5m)");
            w.Bullet("MaxRadius — bubble max size (default: 25m)");
            w.Bullet("MaxFuel — fuel capacity (default: 20)");
            w.Bullet("BaseDrainRate — drain per second (default: 0.005)");
            w.Bullet("DepthDrainMultiplier — extra drain per meter (default: 0.02)");
            w.Bullet("FuelPerItem — fuel per Surtling Core (default: 1)");
            w.Divider();

            w.SubHeader("AutoWater");
            w.Bullet("MaxPaths — water-path generation cap, 250-10000 (default: 2000)");
            w.Bullet("HeightCacheResolution — terrain cache grid, 1024-20480 (default: 4096)");
            w.Bullet("HeightCacheSamplesPerFrame — frame budget for cache build (default: 5000)");
            w.Divider();

            w.SubHeader("Progression [Synced]");
            w.Bullet("UsePrivateKeys — switch to per-player progression keys");
            w.Bullet("UsePrivateRaids — raid eligibility uses private keys");
            w.Bullet("BlockAllGlobalKeys + BlockedGlobalKeys / AllowedGlobalKeys / EnforcedGlobalKeys");
            w.Bullet("BlockedPrivateKeys / AllowedPrivateKeys / EnforcedPrivateKeys");
            w.Bullet("LockEquipment / LockCrafting / LockBuilding / LockCooking / LockEating");
            w.Bullet("LockTaming / LockGuardianPower / LockBossSummons (each with its own *Key)");
            w.Bullet("AdminBypassProgression, UseBlockedMessage, UseBlockedEffect, BlockedMessage");
            w.Paragraph("See the Progression & Key Locking section for the full breakdown.");
            w.Divider();

            w.SubHeader("UI Fonts");
            w.Bullet("PrimaryFont — headers (default: Valheim_Prstartk)");
            w.Bullet("DecorativeFont — decorative text (default: Valheim_Norse)");
            w.Bullet("BodyFont — body text (default: Valheim_AveriaSerif)");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Discord & Anti-Cheat");
                w.Bullet("Discord.* — webhook integration for chat, server events, anti-cheat alerts");
                w.Bullet("Discord.BotListener — optional bot for log-request reactions");
                w.Bullet("VAngarde.* — anti-cheat (mod validation, cheat detection, kick/ban)");
                w.Bullet("ClientLogRelay — admin pipeline for collecting client login artifacts");
                w.Bullet("HeightmapOverride — extend terrain height range past Valheim's ±8 clamp");
            }
        }

        private static void BuildFileLayout(HelpContentWriter w)
        {
            w.Header("File & Folder Layout");
            w.CodeBlock("BepInEx/config/\n" +
                "  com.Fire.FiresRPGmaker.cfg    Main config file\n" +
                "  FiresRPGmaker/\n" +
                "    Quests/                      Quest definition .cfg files\n" +
                "    QuestProfiles/               Quest profile assignment .cfg files\n" +
                "    Dialogues/                   Dialogue tree .cfg files\n" +
                "    Territories/                 Territory zone .cfg files\n" +
                "    ServerInfos/                 Info NPC content files\n" +
                "    Guilds/                      Per-world guild storage (server)\n" +
                "    Compendium/                  Discovery state cache\n" +
                "    VaultData/                   Server-side player data (auto)\n" +
                "    RuntimeSprites/              Custom quest preview images");
            w.Divider();

            w.SubHeader("Per-Player / Per-Character Data");
            w.Bullet("Party state — saved on Player.m_customData (per-character, persists across logout)");
            w.Bullet("Companion ZDOs — owned by the world, follow Valheim's normal save flow");
            w.Bullet("Compendium discoveries — tied to the character");
            w.Bullet("Globe HUD position/scale — saved per client");
            w.Divider();

            w.SubHeader("Asset Bundle");
            w.Paragraph("The mod ships with a vaditems asset bundle containing prefabs, sprites, " +
                "icons, and audio assets. It's loaded automatically at startup.");
            w.Divider();

            w.SubHeader("Logs");
            w.Paragraph("Check BepInEx/LogOutput.log for mod messages. Search for the relevant " +
                "subsystem prefix:");
            w.Bullet("[FiresRPGmaker] — general mod startup / lifecycle");
            w.Bullet("[QuestManager] — quest loading and tracking");
            w.Bullet("[DialogueCodex] — dialogue parsing");
            w.Bullet("[TerritoryLedger] — territory loading and entry");
            w.Bullet("[GuildModule] / [GuildSyncManager] — guild sync and RPCs");
            w.Bullet("[PartyManager] — party invites, sync, RPCs");
            w.Bullet("[CompanionAI] / [Archetype*] — companion AI and abilities");
            w.Bullet("[ChatChannel] — chat channel switching");
            w.Bullet("[ClientLogRelay] / [VAngarde] — admin: client log pipeline / anti-cheat");
            w.Paragraph("Enable General.VerboseLogging = true in config for detailed output.");
        }

        private static void BuildKeyboardShortcuts(HelpContentWriter w)
        {
            w.Header("Keyboard Shortcuts");
            w.Paragraph("Most keys are configurable in BepInEx/config/com.Fire.FiresRPGmaker.cfg under [Hotkeys]. " +
                "Defaults are listed here.");
            w.Divider();

            w.SubHeader("General");
            w.Bullet("E — Interact with an NPC, chest, workbench, sign, etc.");
            w.Bullet("J — Open the Quest Journal (Hotkeys.QuestJournalKey)");
            w.Bullet("G — Open the Guild screen (Hotkeys.GuildScreenKey)");
            w.Bullet("Escape — Close any Verdant's Ascent panel");
            w.Divider();

            w.SubHeader("Companions");
            w.Bullet("Hold R on a companion — open the command radial (~0.15s hold)");
            w.Bullet("Hold H on a companion — open the idle-behavior radial");
            w.Bullet("Shift + Middle Mouse on a target — ping a companion to act on it");
            w.Bullet("Shift + P — place an NPC in the world (companion mode → static)");
            w.Bullet("Shift + U — unstation an NPC back into companion mode");
            w.Bullet("Shift + I — toggle idle wandering for a stationed NPC");
            w.Divider();

            w.SubHeader("Chat");
            w.Bullet("Enter — open the chat input");
            w.Bullet("Tab (while typing) — cycle channels: Local → Global → Group → Guild");
            w.Bullet("Click the channel column to pick a channel directly");
            w.Bullet("/g <message> — send a one-off guild-channel message");
            w.Bullet("Escape — close chat");
            w.Divider();

            w.SubHeader("Dialogue");
            w.Bullet("1-9, 0 — select a dialogue option by number");
            w.Bullet("Backspace — go back to the previous dialogue node");
            w.Bullet("Escape — close the dialogue");
            w.Divider();

            w.SubHeader("Quest Screen");
            w.Bullet("Click a quest in the list to see details");
            w.Bullet("Accept / Cancel buttons to manage quests");
            w.Divider();

            w.SubHeader("Globe HUD (ESC menu open)");
            w.Bullet("Drag — reposition the HUD");
            w.Bullet("Double-click — toggle resize mode");
            w.Bullet("Corner handles — proportional scale");
            w.Bullet("Edge handles — stretch width or height");
        }

        private static void BuildTroubleshooting(HelpContentWriter w)
        {
            w.Header("Troubleshooting");
            w.Divider();

            w.SubHeader("Quests");
            w.Bullet("\"NPC has no quests\" — questProfile must match a profile name in QuestProfiles/, and that profile's Quests= line must list valid quest IDs from Quests/.");
            w.Bullet("Progress not tracking — prefab names are case-sensitive (Boar not boar). Search LogOutput.log for [QuestManager] tracking messages.");
            w.Bullet("Quest screen doubles up — press ESC and reopen the NPC to rebuild the UI state.");
            w.Bullet("Quest timer expired surprise — look for the countdown HUD in the top-left whenever a timed quest is active.");
            w.Divider();

            w.SubHeader("Dialogue");
            w.Bullet("Conditions not working — use HasItem,PrefabName,Amount with no spaces after commas inside the condition.");
            w.Bullet("Options missing — failed conditions hide options by default; set AlwaysVisible: true to show them greyed-out.");
            w.Divider();

            w.SubHeader("Companions");
            w.Bullet("Not following — check Follow vs Stay mode (Hold-R radial). Verify taming is complete.");
            w.Bullet("Won't return home — companion may be beyond ReturnHomeRadius. Move closer or use the radial Return Home option.");
            w.Bullet("Idle work not happening — confirm the behavior is enabled on the Hold-H radial AND the companion is in Stay mode.");
            w.Bullet("Abilities don't fire — check the equipped weapon matches the archetype's required type. View unlock thresholds in the Stats screen.");
            w.Bullet("Radial doesn't open — you must own the companion (not just be near a guildmate's). Aim crosshair at it and hold the key for ~0.15s.");
            w.Divider();

            w.SubHeader("Guilds");
            w.Bullet("Can't see guild messages — ensure Guilds.GuildsEnabled is true, you're in a guild, and either the Guild channel is selected or you prefix with /g.");
            w.Bullet("/gcreate fails — Guilds.AllowGuildCreation may be false on this server. Ask an admin.");
            w.Bullet("Invite says \"player not found\" — the target must be currently online; names are case-insensitive but must match exactly.");
            w.Bullet("Guild screen empty after relog — the client waits for FullSync from the server. Wait a few seconds; check [GuildSyncManager] in the log.");
            w.Divider();

            w.SubHeader("Party");
            w.Bullet("Invite says \"player not found\" — the target must be online; the server router resolves the name to a peer.");
            w.Bullet("Stuck in old party after kick — the Sync message clears state automatically; if not, /leave will reset.");
            w.Divider();

            w.SubHeader("Chat");
            w.Bullet("Group channel says \"coming soon\" — Group is currently a stub. Use Local, Global, or Guild instead.");
            w.Bullet("Tab doesn't cycle — Tab only works while the chat input is focused.");
            w.Bullet("Media embeds not loading — check that the URL ends in .png/.jpg/.gif and the host allows direct linking.");
            w.Divider();

            w.SubHeader("HUD");
            w.Bullet("Globe HUD missing — search LogOutput.log for [GlobeHud]; check UI.UseGlobeHud = true.");
            w.Bullet("Group HUD off-screen after resolution change — set Companions.ShowGroupHud false then true to reset, or edit UI.GroupHud.DefaultPosX/Y.");
            w.Divider();

            w.SubHeader("Config / Sync");
            w.Bullet("Config changes ignored — if ServerAuthority is true, only the server admin can change synced settings. Client edits are overwritten on connect.");
            w.Bullet("File watcher not reloading — watchers only run on the server side. On clients, the server pushes updates via sync.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Log Subsystems");
                w.Bullet("[FiresRPGmaker] — startup, lifecycle, file watchers");
                w.Bullet("[QuestManager] / [QuestTimerManager] — quest loading, tracking, timers");
                w.Bullet("[DialogueCodex] — dialogue parsing");
                w.Bullet("[TerritoryLedger] — territory loading and entry events");
                w.Bullet("[GuildModule] / [GuildSyncManager] — guild sync, RPCs, persistence");
                w.Bullet("[PartyManager] — party invites and routing");
                w.Bullet("[ChatChannel] — channel switching");
                w.Bullet("[CompanionAI] / [Archetype*] / [CompanionProgression] — companion behavior");
                w.Bullet("[GlobeHud] / [GroupHud] — HUD overlays");
                w.Bullet("[VAngarde] — anti-cheat scans, mod validation, kicks");
                w.Bullet("[ClientLogRelay] — client log artifact pipeline (Discord webhook, disk)");
                w.Bullet("Enable General.VerboseLogging = true for detailed output across all subsystems.");

                w.AdminDivider();
                w.AdminHeader("Admin: Discord Client-Log Relay");
                w.Bullet("Requires VAngarde anti-cheat enabled to solicit logs from clients.");
                w.Bullet("Configure Discord.Webhooks.* and Discord.ClientLogs.* in the main config.");
                w.Bullet("Reaction polling: react with the configured emoji (default 📥) on a kick message to request the player's full client log.");
                w.Bullet("Logs are fanned out via ClientLogRelay to the disk consumer and Discord webhook consumer.");
            }
        }

        private static void BuildAdminCommandsOverview(HelpContentWriter w)
        {
            w.Header("Admin Commands Overview");
            w.Paragraph("This section documents the admin-only console commands. Type a command with no arguments for usage info.");
            w.Divider();

            w.SubHeader("Command Categories");
            w.Bullet("Companion management — spawn, level, archetype, heal, debug");
            w.Bullet("Compendium — list, unlock, reset discovery state");
            w.Bullet("Visual effects — list and test FX by name");
            w.Divider();

            w.SubHeader("Important Notes");
            w.Bullet("Some commands only work on a server.");
            w.Bullet("Use with caution — several can affect live gameplay.");
            w.Bullet("Changes may not be reversible.");
        }

        private static void BuildCompanionAdminCommands(HelpContentWriter w)
        {
            w.Header("Companion Admin Commands");

            w.SubHeader("Spawning");
            w.Bullet("companion_spawn [name] — spawn a companion");
            w.Bullet("companion_spawn_random — spawn with a random loadout");

            w.SubHeader("Management");
            w.Bullet("companion_setlevel <level> — set companion level");
            w.Bullet("companion_setarchetype <type> — force an archetype");
            w.Bullet("companion_heal — heal all companions");
            w.Bullet("companion_kill — kill the targeted companion");

            w.SubHeader("Debugging");
            w.Bullet("companion_debug — toggle the debug overlay");
            w.Bullet("companion_list — list all active companions");
            w.Bullet("companion_stats <id> — show detailed stats");

            w.SubHeader("Testing");
            w.Bullet("companion_testability <name> — trigger an ability");
            w.Bullet("companion_testfx <name> — play an effect");
        }

        private static void BuildCompendiumAdminCommands(HelpContentWriter w)
        {
            w.Header("Compendium Admin Commands");

            w.SubHeader("Viewing");
            w.Bullet("compendium_list — list all entries with discovery status");

            w.SubHeader("Discovery");
            w.Bullet("compendium_unlock <key> — unlock a specific entry");
            w.Bullet("compendium_unlock_all — unlock everything");
            w.Bullet("compendium_discover <trigger> — fire a discovery trigger");

            w.SubHeader("Management");
            w.Bullet("compendium_reset — reset all discoveries");

            w.SubHeader("Entry Key Prefixes");
            w.Bullet("basics_* — basic guides");
            w.Bullet("archetype_* — archetype guides");
            w.Bullet("hybrid_* — hybrid class guides");
            w.Bullet("ability_* — ability descriptions");
        }

        private static void BuildFxTestingCommands(HelpContentWriter w)
        {
            w.Header("FX Testing Commands");

            w.SubHeader("Listing Effects");
            w.Bullet("listfx — list all available FX");
            w.Bullet("listfxcategory <category> — list FX by category");

            w.SubHeader("Testing Effects");
            w.Bullet("testfx <name> — play FX at the player position");
            w.Bullet("testfx <name> <duration> — play for a set duration");

            w.SubHeader("Categories");
            w.Bullet("hit — impact effects");
            w.Bullet("spawn — spawn / summon effects");
            w.Bullet("buff — buff visual effects");
            w.Bullet("aoe — area of effect");
            w.Bullet("projectile — projectile trails");

            w.SubHeader("Common FX Names");
            w.CodeBlock("vfx_FireHit\nvfx_IceHit\nvfx_LightningHit\nfx_Guardian_Activate\nvfx_Potion_health_medium");
        }
    }
}
