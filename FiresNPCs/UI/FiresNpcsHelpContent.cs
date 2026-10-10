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

            // Tamed-companion gameplay moved to the standalone FiresCompanions mod (Core engine +
            // FiresCompanions frontend) — it registers its own help group. What remains here is the
            // static/placed-NPC layer and the vault recovery commands.
            HelpRegistry.RegisterSection(ModId, "Companions", "Companions & Static NPCs", 10, BuildCompanionsHandoff);

            HelpRegistry.RegisterSection(ModId, "Social", "Guilds", 20, BuildGuilds);
            HelpRegistry.RegisterSection(ModId, "Social", "Party", 21, BuildParty);
            HelpRegistry.RegisterSection(ModId, "Social", "Chat Channels", 22, BuildChatChannels);
            HelpRegistry.RegisterSection(ModId, "Social", "Player Titles", 23, BuildPlayerTitles);
            HelpRegistry.RegisterSection(ModId, "Social", "Factions", 24, BuildFactions);

            HelpRegistry.RegisterSection(ModId, "World", "NPC Types & Setup", 30, BuildNpcTypes);
            HelpRegistry.RegisterSection(ModId, "World", "Quests & Journal", 31, BuildQuestsAndJournal);
            HelpRegistry.RegisterSection(ModId, "World", "Quest Types & Rewards", 31, BuildQuestTypesAndRewards);
            HelpRegistry.RegisterSection(ModId, "World", "Chain Quests & Lifecycle Events", 31, BuildChainAndLifecycle);
            HelpRegistry.RegisterSection(ModId, "World", "Dialogue System", 32, BuildDialogueSystem);
            HelpRegistry.RegisterSection(ModId, "World", "Dialogue Conditions Reference", 32, BuildDialogueConditionsReference);
            HelpRegistry.RegisterSection(ModId, "World", "Territories", 33, BuildTerritories);
            HelpRegistry.RegisterSection(ModId, "World", "Compendium", 34, BuildCompendium);

            // Marketplace family — all of this landed in the FiresRPGmaker → Marketplace drop-in refactor
            // and previously had ZERO help coverage.
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Trader Setup & Use", 25, BuildTraderSetup);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Trader Config Format", 25, BuildTraderConfigFormat, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Marketplace Listings (Buy/Sell)", 26, BuildMarketplaceListings);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Banker", 27, BuildBanker);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Transmog", 28, BuildTransmog);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Mail", 28, BuildMail);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Buff Station", 28, BuildBuffStation);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Leaderboard & Achievements", 28, BuildLeaderboard);
            HelpRegistry.RegisterSection(ModId, "Marketplace", "Public API Surface", 29, BuildMarketplaceAPI, adminOnly: true);

            HelpRegistry.RegisterSection(ModId, "Games", "Gambler Games", 36, BuildGamblerGames);
            HelpRegistry.RegisterSection(ModId, "Games", "Board Games & Poker", 37, BuildBoardGames);

            HelpRegistry.RegisterSection(ModId, "Interface", "Globe HUD Overlay", 40, BuildGlobeHud);
            HelpRegistry.RegisterSection(ModId, "Interface", "Quest Tracker HUD", 40, BuildQuestTracker);
            HelpRegistry.RegisterSection(ModId, "Interface", "Custom Chat Window", 41, BuildCustomChatWindow);
            HelpRegistry.RegisterSection(ModId, "Interface", "UI Customization", 41, BuildUICustomization);
            HelpRegistry.RegisterSection(ModId, "Interface", "Keyboard Shortcuts", 42, BuildKeyboardShortcuts);
            HelpRegistry.RegisterSection(ModId, "Interface", "Troubleshooting", 43, BuildTroubleshooting);

            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Quest Config Format", 50, BuildQuestConfigFormat, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Dialogue Config Format", 51, BuildDialogueConfigFormat, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Authoring (Admin)", "Territory Config", 52, BuildTerritoryConfig, adminOnly: true);

            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "Config Reference", 60, BuildConfigReference, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "Config Delivery & Push/Pull", 60, BuildConfigDelivery, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "Progression & Key Locking", 61, BuildProgression, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Server (Admin)", "File & Folder Layout", 62, BuildFileLayout, adminOnly: true);

            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Admin Commands Overview", 70, BuildAdminCommandsOverview, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Companion Admin Commands", 71, BuildCompanionAdminCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Quest, Key & Migration Commands", 71, BuildQuestKeyCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Compendium Admin Commands", 72, BuildCompendiumAdminCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Diagnostics & Dump Commands", 73, BuildDiagnosticsCommands, adminOnly: true);
            HelpRegistry.RegisterSection(ModId, "Commands (Admin)", "Parity Test Commands (firesrpg_test)", 74, BuildParityTestCommands, adminOnly: true);
        }

        private static bool IsCurrentPlayerAdmin()
        {
            try
            {
                return FiresCore.Sync.AdminSyncing.IsLocalAdmin();
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
            w.Bullet("4. Guilds come from the FiresGuilds mod: press G there to open its guild screen.");
            w.Bullet("5. Press J to open the Quest Journal once you've accepted a quest.");
            w.Divider();

            w.SubHeader("Common Hotkeys");
            w.Bullet("E — Interact with an NPC, chest, workbench, etc.");
            w.Bullet("J — Open the Quest Journal (configurable in Hotkeys.QuestJournalKey)");
            w.Bullet("L — Open the Leaderboard & Achievements panel (Hotkeys.LeaderboardKey)");
            w.Bullet("Tab (while typing in chat) — cycle chat channels");
            w.Bullet("Escape — close any open Verdant's Ascent panel");
            w.Paragraph("Companion controls (Hold R / Hold H radials, Shift+MMB ping) are documented in " +
                "the FiresCompanions help group. See the Keyboard Shortcuts section for the full reference.");
            w.Divider();

            w.SubHeader("Player Slash Commands");
            w.Bullet("/g <message> — guild-only chat (routes through the active guild system)");
            w.Bullet("/invite <player> — invite to your party (separate from guilds)");
            w.Bullet("/leave  /disband — leave or disband your party");
            w.Bullet("/feedback <message> — send feedback straight to the server's Discord");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: First-Time Server Setup");
                w.Bullet("Main config: BepInEx/config/com.Fire.FiresRPGmaker.cfg");
                w.Bullet("Set General.ServerAuthority = true to lock config changes to admins.");
                w.Bullet("Per-feature content lives under BepInEx/config/FiresRPGmaker/ ON THE SERVER — " +
                    "clients receive it in memory, so client-side files are not read on a dedicated server.");
                w.Bullet("File watchers auto-reload .cfg changes on the server — no restart needed.");
                w.Bullet("Shift+E an NPC to open the admin book and assign its role/profile.");
                w.Bullet("firesnpcs_pull_configs / firesnpcs_push_configs move content between your " +
                    "machine and the server — see Config Delivery & Push/Pull.");
                w.Bullet("See Config Reference, Progression & Key Locking, and File & Folder Layout.");
            }
        }

        private static void BuildNpcTypes(HelpContentWriter w)
        {
            w.Header("NPC Types & Setup");
            w.Paragraph("Every placed NPC has a ROLE that decides what happens when a player presses E on it. " +
                "A freshly placed NPC starts as 'None' (decorative) until an admin assigns a role.");
            w.Divider();

            w.SubHeader("Content Roles");
            w.Bullet("Quest — offers quests from a quest profile (list, details, Accept/Cancel).");
            w.Bullet("Info — shows an info panel: header, optional image, rich-text body. Lore and signage.");
            w.Bullet("Dialogue — runs a branching dialogue tree with conditions, commands, and icons.");
            w.Divider();

            w.SubHeader("Economy Roles");
            w.Bullet("Trader — buy/sell book UI from a Traders/ profile. See Trader Setup & Use.");
            w.Bullet("Banker — currency vault with deposits, withdrawals, and interest. See Banker.");
            w.Bullet("Marketplace — player-to-player listings (Buy / Sell / My Listings). See Marketplace Listings.");
            w.Bullet("Transmog — visual gear override station. See Transmog.");
            w.Bullet("Mail — send items and messages to other players. See Mail.");
            w.Bullet("Buffer — buff station selling status-effect packages. See Buff Station.");
            w.Divider();

            w.SubHeader("Activity Roles");
            w.Bullet("Gambler — dice, rune, and wheel wager games. See Gambler Games.");
            w.Bullet("Leaderboard / Achievement — opens the leaderboard panel on the Stats or Achievements tab.");
            w.Bullet("Teleporter — wayshrine-style travel point.");
            w.Bullet("None — decorative only (dress-up mannequins, scenery characters).");
            w.Divider();

            w.Paragraph("One NPC has exactly one role. Point several NPCs at the same profile file to reuse " +
                "content in multiple places.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: NPC Configuration");
                w.Paragraph("Shift+E while looking at an NPC opens the two-page parchment admin book " +
                    "(UI.UseBookAdminPanel, on by default — disabling it falls back to the legacy panel). " +
                    "Pick the role on the left page; the right page shows that role's form.");
                w.Bullet("npcProfileName — display name shown above the NPC");
                w.Bullet("displayNameOverride — optional floating-name override");
                w.Bullet("questProfile — profile name from QuestProfiles/");
                w.Bullet("dialogueProfile — UID of the root dialogue node from Dialogues/");
                w.Bullet("infoProfile — profile name from ServerInfos/");
                w.Bullet("Trader/Banker/Gambler/etc. profiles — named profile from that role's config folder");
                w.Bullet("modelOverride — swap the NPC's visual model (Dressing Room)");
                w.Paragraph("Any player can see and talk to the NPC; only admins can edit it.");
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

        private static void BuildCompanionsHandoff(HelpContentWriter w)
        {
            w.Header("Companions & Static NPCs");
            w.Paragraph("Tamed companions — the follow/stay AI, archetypes, abilities, radial menus, " +
                "inventory screens, kennel, and wild spawns — ship in the standalone FiresCompanions " +
                "mod. Look for the FiresCompanions group in this help panel's sidebar for the full " +
                "companion guide.");
            w.Divider();

            w.SubHeader("What Lives Here");
            w.Paragraph("Verdant's Ascent provides the STATIC NPC layer — companions and characters " +
                "'placed' as stationary world NPCs (innkeepers, vendors, quest givers, decoration) — " +
                "plus the vault, the per-player save store that remembers your companions.");
            w.Bullet("Shift + P — station a companion as a static NPC at its current position");
            w.Bullet("Shift + U — unstation it back into companion mode");
            w.Bullet("Shift + I — toggle idle wandering for a stationed NPC");
            w.Bullet("Static NPCs respawn with the world and can host any NPC role (see NPC Types & Setup).");
            w.Divider();

            w.SubHeader("Static NPC Config");
            w.Bullet("Companions.StaticNpcs.NameplateStyle — how stationed NPC names render");
            w.Bullet("Companions.StaticNpcs.StaticNpcInvulnerable — placed NPCs can't be killed");
            w.Bullet("Companions.Persistence.PersistentCompanions — companions persist in-world like vanilla tames [Synced]");
            w.Bullet("Companions.Persistence.KeepZonesLoadedForOfflinePlayers — keep offline players' companions alive [Synced]");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Vault Recovery");
                w.Paragraph("The vault-side companion commands (companioninfo, recovercompanion, " +
                    "restorecompanions, purgeallcompanions...) are documented under Commands (Admin) → " +
                    "Companion Admin Commands.");
            }
        }

        private static void BuildGuilds(HelpContentWriter w)
        {
            w.Header("Guilds");

            // 0.0.527 (Fire 10-08): RPG Maker's own guild module is removed. Guilds come from FiresGuilds (or VikingLands.Core).
            if (global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildsDeferred)
            {
                w.Paragraph("Guilds on this installation come from " + global::FiresRPGmaker.Modules.Compat.VikingCoreCompat.GuildOwner + ".");
                w.Bullet("Press G to open the guild screen, and see the FiresGuilds group in this help panel's sidebar " +
                    "for the full guide (ranks, applications, tags, emblems, friendly fire, map pings, settings).");
            }
            else
            {
                w.Paragraph("Guilds come from the FiresGuilds mod, which is not installed here. Install it to get " +
                    "guilds: ranks, a guild screen (G), guild chat, guild-mate map pins and pings.");
            }
            w.SubHeader("What Verdant's Ascent adds to them");
            w.Bullet("The Guild chat channel and the /g message prefix route to the guild mod. See Chat Channels.");
            w.Bullet("Dialogue conditions HasGuild / HasGuildWithName read your guild from the guild mod.");
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
            w.Bullet("Guild — guildmates only. Sent through the guild mod (FiresGuilds).");
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
            w.Bullet("NoBuild — building and removing pieces disabled");
            w.Bullet("NoPickaxe / NoTool — mining / every tool disabled");
            w.Bullet("NoInteract (and NoInteractItems, NoInteractCraftingStation, NoInteractItemStands, NoInteractChests, NoInteractDoors, NoInteractPortals), NoAttack, NoPortals");
            w.Bullet("PveOnly / PvpOnly — PvP forced off / on inside; your own setting comes back when you leave");
            w.Bullet("PushAway — players without permission are pushed out along the ground");
            w.Bullet("MoveSpeedMultiplier=m — walk, run and swim speed x m");
            w.Bullet("Gravity=g (-10 to 10) — 1 = normal, 0 = weightless, 0.5 = half, 3 = three times, negative pulls you up "
                + "(you hover 25 m above the ground). Players only; swimming and climbing keep their own physics; no fall damage below 1");
            w.Bullet("FirstPerson — the camera is forced to first person inside (zooming out is ignored); your own camera "
                + "comes back when you leave. Valcast camera modes still take over while they run. Applies to everyone, admins too");
            w.Bullet("Scale=s (0.25 to 4) — your size: 0.5 = half, 2 = double. Everyone sees it. Speed, jump and reach stay normal; "
                + "growing back waits until there is room above you");
            w.Bullet("IncreasedPlayerDamage=x / IncreasedMonsterDamage=x — damage multipliers; GodMode — no damage from attackers");
            w.Bullet("PeriodicDamage=x / PeriodicHeal=x / PeriodicHealALL=x — damage or healing over time");
            w.Bullet("CustomEnvironment=env1,env2 — weather override; ForceBiome=n; ForceWind=x; SnowMask; NoMist");
            w.Bullet("EWPSpawner=Prefab,amount,level — creatures spawn on entry and respawn after they all die; NoMonsters — no natural spawns");
            w.Bullet("MonstersAddStars=n — extra stars on creatures spawned inside");
            w.Bullet("DropMultiplier=x — loot multiplier");
            w.Bullet("RevealOnMap — reveal the map area on entry");
            w.Bullet("NoDeathPenalty — keep your skills on death; NoItemLoss — keep skills AND inventory, no tombstone");
            w.Bullet("InfiniteStamina, InfiniteEitr, InfiniteFuel, NoBuildDamage, NoStructureSupport, CustomPaint=Paved|Dirt|Cultivated|Grass");
            w.Bullet("OnlyForGuild=Name / OnlyForFaction=id — members of that guild / faction are exempt from the restrictions");
            w.Bullet("ForSale=coins — the area can be bought from the large map (a plot sells once); owners may build");
            w.Bullet("HeightBounds=min,max — the area only applies between those heights (default: up to 600)");
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
                "R, G, B, ShowExternalWater\n" +
                "Flag1, Flag2, Key=Value\n" +
                "ALL");
            w.Divider();

            w.SubHeader("Lines Explained");
            w.Bullet("Line 1: [name] — Unique territory identifier");
            w.Bullet("Line 2: Shape — Circle, Square or Rectangle");
            w.Bullet("Line 3: Position — Circle: X, Z, Radius. Square: X, Z, HalfSide. Rectangle: MinX, MinZ, Width, Depth");
            w.Bullet("Line 4: Color — R, G, B (0-255), then true/false for ShowExternalWater");
            w.Bullet("Line 5: Flags — separated by , | or ; (see Territory Effects); None for no flags");
            w.Bullet("Line 6: Exceptions — ALL means none; otherwise the player names the restrictions don't apply to (admins, owners and OnlyForGuild/OnlyForFaction members are exempt too)");
            w.Divider();

            w.SubHeader("Example: Safe Zone");
            w.CodeBlock("[starter_zone]\n" +
                "Circle\n" +
                "0, 0, 50\n" +
                "0, 255, 0, true\n" +
                "NoBuild, NoPickaxe, MoveSpeedMultiplier=1.2\n" +
                "ALL");
            w.Divider();

            w.SubHeader("Spawner Format (EWPSpawner)");
            w.CodeBlock("EWPSpawner=PrefabName,Amount,Level,respawn=Seconds,xyz=X,Y,Z");
            w.Divider();

            w.SubHeader("Drawing Territories In-Game");
            w.Paragraph("Instead of hand-typing coordinates, walk the world and draw:");
            w.Bullet("drawterritory — create a territory at your current position");
            w.Bullet("startmarking / stopmarking — walk a path; markers are recorded automatically");
            w.Bullet("startoutline / stopoutline — record an outline area as you walk it");
            w.Bullet("listdrawnterritories — list territories created by drawing");
            w.Bullet("removeterritory — remove a drawn territory");
            w.Paragraph("Drawn territories persist alongside the .cfg-defined ones.");
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
            w.Bullet("QuestJournalKey — Quest Journal hotkey (default: J; chords like J + LeftAlt supported)");
            w.Bullet("LeaderboardKey — Leaderboard hotkey (default: L; chords supported)");
            w.Bullet("Set any of them to None to disable.");
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

            w.SubHeader("Companions (static-NPC side)");
            w.Bullet("Companions.ShowBillboardHealthBar / ShowGroupHud — companion HUD toggles");
            w.Bullet("Companions.Behavior.* — wander/work radii (read by the companion engine)");
            w.Bullet("Companions.Persistence.PersistentCompanions — persist in-world like vanilla tames [Synced]");
            w.Bullet("Companions.Persistence.KeepZonesLoadedForOfflinePlayers — keep offline players' companions alive [Synced]");
            w.Bullet("Companions.StaticNpcs.NameplateStyle / StaticNpcInvulnerable — stationed-NPC options");
            w.Paragraph("The full companion system ships in FiresCompanions — see its help group for " +
                "gameplay configuration.");
            w.Divider();

            w.SubHeader("UI");
            w.Bullet("UseGlobeHud — globe-style health/stamina/eitr/adrenaline HUD (default: true)");
            w.Bullet("UseBookAdminPanel — two-page parchment NPC admin book (default: true; false = legacy panel)");
            w.Bullet("ShowAdminHints — admin hint overlays (default: true)");
            w.Bullet("ExtendedInteractRange — longer E-interact reach for NPCs");
            w.Bullet("QuestBookOpenSpeed — journal book open animation speed");
            w.Bullet("NPC Panel / Quest Journal Position X/Y, Size, Locked — panel layout persistence");
            w.Bullet("LeaderboardBackground / LeaderboardBackgroundToggle — leaderboard backdrop");
            w.Divider();

            w.SubHeader("UI.GroupHud / UI.Dialogue / UI.QuestTracker / UI.Territory");
            w.Bullet("GroupHud: MaxRowsPerColumn (8), Columns (1-2), DefaultPosX/Y — companion side panel");
            w.Bullet("Dialogue: DialogueBackgroundOpacity / InfoBackgroundOpacity");
            w.Bullet("QuestTracker: ShowQuestTracker, Collapsed, PosX/PosY — on-screen objective tracker");
            w.Bullet("Territory: HudPosX/HudPosY, HideMinimapBackdrop — territory HUD placement");
            w.Divider();

            w.SubHeader("Main (Marketplace Economy) [Synced]");
            w.Bullet("MarketTaxes — percentage tax applied to marketplace sales");
            w.Bullet("VIPplayersList / VIPplayersTaxes — reduced tax rate for listed players");
            w.Bullet("PossibleCurrencies — currencies accepted for listings");
            w.Bullet("ItemMarketLimit — max simultaneous listings per player");
            w.Bullet("MarketSellBlockedPrefabs — items that can never be listed");
            w.Bullet("BlockedPlayers — players barred from the marketplace");
            w.Divider();

            w.SubHeader("Quests");
            w.Bullet("RewardOverflowDelivery — how quest rewards arrive when your inventory is full");
            w.Divider();

            w.SubHeader("Chat");
            w.Bullet("ActiveChannel — last-selected channel (persists)");
            w.Bullet("CustomChatWindow, PanelOffsetX/Y, PanelScale, ArrangeHotkey — see Custom Chat Window");
            w.Divider();

            w.SubHeader("Games");
            w.Bullet("Chess: Difficulty (Easy/Medium/Hard/Master), UiScale, OffsetX/Y");
            w.Bullet("Fanorona: UiScale, OffsetX/Y");
            w.Bullet("Poker: Difficulty, PlayerTurnSeconds, CashCurrency, CashBuyIn, CashBigBlind, UiScale, OffsetX/Y");
            w.Divider();

            w.SubHeader("Leaderboard / Feedback");
            w.Bullet("Leaderboard.AchievementProfile — which achievement set the world uses");
            w.Bullet("Feedback.WebhookUrl — Discord webhook for /feedback (server-side; keep private)");
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
                w.AdminHeader("Admin: Moved to Standalone Mods");
                w.Bullet("Discord integration (webhooks, chat relay, death screenshots) — now the " +
                    "FiresDiscordIntegration mod; see its help group.");
                w.Bullet("Anti-cheat / mod validation / character enforcement — now the FiresVAngarde " +
                    "mod; see its help group.");
                w.Bullet("Terrain height-range override — now [HeightmapOverride] in FiresUnifiedCore.");
            }
        }

        private static void BuildFileLayout(HelpContentWriter w)
        {
            w.Header("File & Folder Layout");
            w.Paragraph("These folders live ON THE SERVER (or the host in singleplayer). Connected clients " +
                "receive the content in memory — use firesnpcs_pull_configs to copy the server's tree to " +
                "your machine for editing, and firesnpcs_push_configs to send edits back.");
            w.CodeBlock("BepInEx/config/\n" +
                "  com.Fire.FiresRPGmaker.cfg    Main config file\n" +
                "  FiresRPGmaker/\n" +
                "    Quests/                      Quest definition .cfg files\n" +
                "    QuestProfiles/               Quest profile assignments\n" +
                "    QuestEvents/                 Quest lifecycle event hooks\n" +
                "    Dialogues/                   Dialogue tree .cfg files\n" +
                "    Traders/                     Trader profiles\n" +
                "    Bankers/                     Banker profiles\n" +
                "    Gamblers/                    Gambler game profiles\n" +
                "    Marketplace/                 Marketplace listing configs\n" +
                "    Transmogrifications/         Transmog option sets\n" +
                "    Mail/                        Mail profiles\n" +
                "    Buffers/ BufferProfiles/     Buff station content\n" +
                "    LeaderboardAchievements/     Achievement definitions\n" +
                "    Territories/                 Territory zone .cfg files\n" +
                "    Factions/                    Faction definitions (yaml)\n" +
                "    ServerInfos/                 Info NPC content files\n" +
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
            w.Bullet("L — Open the Leaderboard panel (Hotkeys.LeaderboardKey)");
            w.Bullet("Escape — Close any Verdant's Ascent panel");
            w.Paragraph("The J / G / L hotkeys support multi-key CHORDS (e.g. L + LeftAlt) — set them " +
                "in the F8 config window: click the key box, press your combination, click Apply.");
            w.Divider();

            w.SubHeader("Companions & Static NPCs");
            w.Bullet("Companion controls (Hold R / Hold H radials, Shift+MMB ping) — see the " +
                "FiresCompanions help group");
            w.Bullet("Shift + P — station a companion as a static NPC at its current position");
            w.Bullet("Shift + U — unstation an NPC back into companion mode");
            w.Bullet("Shift + I — toggle idle wandering for a stationed NPC");
            w.Bullet("Shift + E on an NPC — open the admin config book (admins only)");
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
            w.Bullet("No guilds at all — guilds come from the FiresGuilds mod; install it (its own help group covers the rest).");
            w.Bullet("Can't see guild messages — you must be in a guild (FiresGuilds), and either select the Guild channel or prefix with /g.");
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
                w.Bullet("[PartyManager] — party invites and routing");
                w.Bullet("[ChatChannel] — channel switching");
                w.Bullet("[CompanionAI] / [Archetype*] / [CompanionProgression] — companion behavior");
                w.Bullet("[GlobeHud] / [GroupHud] — HUD overlays");
                w.Bullet("Enable General.VerboseLogging = true for detailed output across all subsystems.");

                w.AdminDivider();
                w.AdminHeader("Admin: Discord & Anti-Cheat");
                w.Paragraph("Discord relay (chat, deaths, screenshots, client-log requests) is the " +
                    "FiresDiscordIntegration mod, and anti-cheat/mod-validation is the FiresVAngarde " +
                    "mod — both have their own help groups in the sidebar.");
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
            w.Paragraph("Companion state lives in two places: live objects in the world, and the vault " +
                "(per-player saved data). These commands inspect and repair both sides.");
            w.Divider();

            w.SubHeader("Spawning & Inspecting");
            w.Bullet("spawncompanion [prefabName] — spawn a companion NPC in front of you");
            w.Bullet("companioninfo — show your companions as stored in the vault");
            w.Bullet("findcompanion <name> — mark a companion's location on the map");
            w.Bullet("listcompanions — list all loaded companion PREFABS (use companioninfo for yours)");
            w.Bullet("listlostcompanions — vault companions that are not currently in the world");
            w.Divider();

            w.SubHeader("Recovery");
            w.Bullet("recovercompanion <name> — restore a lost companion from vault data");
            w.Bullet("restorecompanions — force-restore your FOLLOWING companions (teleports existing, spawns missing)");
            w.Divider();

            w.SubHeader("Removal (destructive)");
            w.Bullet("deletecompanion <name> — permanently delete one companion");
            w.Bullet("purgeallcompanions CONFIRM — delete ALL your companions from world AND vault");
            w.Bullet("destroyallcompanionnpcs CONFIRM — destroy every CompanionNpc object in loaded chunks");
            w.Divider();

            w.SubHeader("Vault Maintenance");
            w.Bullet("savevault — force a save of vault data");
            w.Bullet("vaultclean — prune stale vault entries");
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

        private static void BuildDiagnosticsCommands(HelpContentWriter w)
        {
            w.Header("Diagnostics & Dump Commands");
            w.Paragraph("Inspection commands for finding prefab names, checking what's loaded, and " +
                "debugging UI state. Dumps print to the console / log.");
            w.Divider();

            w.SubHeader("Content Dumps");
            w.Bullet("dumpitems — every item prefab the game knows");
            w.Bullet("dumpprefabs — every registered prefab");
            w.Bullet("dumprecipes — all crafting recipes");
            w.Bullet("dumpenvironments — weather/environment names (for ForceEnvironment territories)");
            w.Bullet("dumpglobalshaders / dumpskybox — rendering state dumps");
            w.Divider();

            w.SubHeader("Listing");
            w.Bullet("listquests [term] / listquestprofiles / listdialogues — loaded quest & dialogue content");
            w.Bullet("listassets / listicons / listsprites / listshaders — loaded asset inventories");
            w.Bullet("listdestructibles / listminerocks / listpickables / listtrees — world-object categories");
            w.Divider();

            w.SubHeader("UI Debugging");
            w.Bullet("testnpcui / closenpcui / uistatus — open, close, and report the NPC UI state");
            w.Bullet("reloadui — reload UI configuration (dialogues, quest profiles)");
            w.Bullet("dump_chat_ui — dump the chat window hierarchy");
            w.Bullet("firesnpcs_bookview — preview an animated book; 'capture' renders frames to BepInEx/config/FiresRPGmaker/Debug/BookView/");
        }

        // ── Trader / Marketplace family ──────────────────────────────────────

        private static void BuildTraderSetup(HelpContentWriter w)
        {
            w.Header("Trader Setup & Use");
            w.Paragraph("Traders are NPCs that exchange items for currency (or other items). The player-facing UI " +
                "is a two-page open book with synced columns: Icons / Items / Buy / Amount / Sell / Stock / Restock.");
            w.Divider();

            w.SubHeader("Player Flow");
            w.Bullet("Press E to interact with a trader NPC.");
            w.Bullet("Use the Search bar at the top to filter the trade list.");
            w.Bullet("Click x1 / x5 / x10 / x100 to set the quantity multiplier for every row.");
            w.Bullet("Type a number into a row's Amount field to override that row's qty.");
            w.Bullet("Click the Buy price to purchase, or the Sell price to sell. A confirmation prompt appears.");
            w.Bullet("The right page header shows your gold balance (left) and the trader's gold pool (right).");
            w.Divider();

            w.SubHeader("Stock & Restock");
            w.Bullet("Stock column shows how many of an item the trader will sell before it runs out (∞ = unlimited).");
            w.Bullet("Restock shows the cooldown until that item replenishes.");
            w.Bullet("If a trader has a MaxGold pool, selling drains the pool — Sell greys out when empty until the next gold restock cycle.");
            w.Divider();

            w.SubHeader("Admin Setup");
            w.Bullet("Hammer an NPC → choose Trader Npc tab.");
            w.Bullet("Type a profile name in the input field, or pick one from the Saved Profiles dropdown.");
            w.Bullet("Enter one trade per line in the trades box (see Trader Config Format).");
            w.Bullet("Click Apply — the profile is written to disk and the NPC is committed as a Trader.");
        }

        private static void BuildTraderConfigFormat(HelpContentWriter w)
        {
            w.Header("Trader Config Format");
            w.Paragraph("Trader profiles live in:");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Traders/");
            w.Paragraph("Each .cfg file holds one or more [Profile] headers followed by trade lines.");
            w.Divider();

            w.SubHeader("Profile Header");
            w.CodeBlock("[ProfileName, Stock=10, Restock=1800, MaxGold=5000, GoldRestock=3600]");
            w.Bullet("Stock= — default stock for trades that don't override it (-1 = unlimited)");
            w.Bullet("Restock= — default restock cooldown seconds (0 = no restock)");
            w.Bullet("MaxGold= — trader's gold pool cap. NPC drains it on player sales (-1 = unlimited, default)");
            w.Bullet("GoldRestock= — seconds between auto-refills of the gold pool (0 = no refill)");
            w.Divider();

            w.SubHeader("Trade Line — Legacy (4-5 values)");
            w.CodeBlock("cost, costAmount, reward, rewardAmount [, level]");
            w.Paragraph("Example: 'Coins, 100, SwordIron, 1' means player pays 100 Coins and receives 1 SwordIron (a Buy trade). " +
                "Swap order to make it a Sell trade: 'SwordIron, 1, Coins, 100' means player gives 1 SwordIron and receives 100 Coins.");
            w.Divider();

            w.SubHeader("Trade Line — Multi-item Exchange");
            w.CodeBlock("cost1,amt[,lvl], cost2,amt[,lvl], ... = reward1,amt[,lvl], reward2,amt[,lvl], ...");
            w.Paragraph("The = separator lets a single trade exchange multiple cost items for multiple reward items.");
            w.Divider();

            w.SubHeader("Per-trade Stock & Restock");
            w.Paragraph("Append Key=Value tokens to ANY trade line — they override the profile-header defaults:");
            w.CodeBlock("Coins, 200, BronzeNails, 50, Stock=30, Restock=600\n" +
                "TrophyTroll, 1, Coins, 250, Stock=3");
            w.Bullet("Stock= on the trade line — caps THIS trade only");
            w.Bullet("Restock= on the trade line — overrides THIS trade's restock cooldown");
            w.Bullet("Order doesn't matter; can appear before or after the level token.");
            w.Divider();

            w.SubHeader("Buy/Sell Pairing in the UI");
            w.Paragraph("If you write both a Buy trade and a Sell trade for the same item, the UI MERGES them into one row " +
                "showing both prices side-by-side. Example to fully spec an item:");
            w.CodeBlock("Coins, 500, ArmorBronzeChest, 1\n" +
                "ArmorBronzeChest, 1, Coins, 250");
            w.Paragraph("→ One row shows: Buy 500, Sell 250.");
            w.Divider();

            w.SubHeader("Complete Example");
            w.CodeBlock("[BlackForestArmorer, Stock=10, Restock=1800, MaxGold=5000, GoldRestock=3600]\n" +
                "Coins, 500, ArmorBronzeChest, 1\n" +
                "ArmorBronzeChest, 1, Coins, 250\n" +
                "Coins, 200, BronzeNails, 50, Stock=30, Restock=600\n" +
                "Iron, 5, Coins, 1000\n" +
                "TrophyTroll, 1, Coins, 250, Stock=3");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Resolution Order");
                w.Paragraph("Stock & Restock values resolve highest-priority first:");
                w.Bullet("1. Per-trade Key=Value on the line (Stock=, Restock=)");
                w.Bullet("2. Profile header default (Stock=, Restock=)");
                w.Bullet("3. Fallback (Stock=-1 unlimited, Restock=0 no auto-restock)");
                w.Paragraph("MaxGold and GoldRestock ONLY make sense at the profile header — per-trade gold pools aren't supported.");
                w.AdminDivider();
                w.AdminHeader("Admin: KG Compatibility");
                w.Paragraph("Standard KG-Marketplace trader .cfg files load directly. Stock/Restock/MaxGold/GoldRestock are extensions; " +
                    "leaving them off makes the profile behave like a vanilla KG trader (unlimited stock, unlimited gold).");
            }
        }

        private static void BuildMarketplaceListings(HelpContentWriter w)
        {
            w.Header("Marketplace Listings (Buy/Sell)");
            w.Paragraph("The Marketplace UI is KG-parity: three tabs (Buy / Sell / MyListings) with category filter " +
                "(All / Weapons / Armor / Consumable / Tools / Resources), search bar, sortable columns, and tile-density cycle.");
            w.Divider();

            w.SubHeader("Tabs");
            w.Bullet("Buy — listings posted by other players, filtered by category and search");
            w.Bullet("Sell — items in YOUR inventory eligible to list (set price + currency)");
            w.Bullet("My Listings — listings YOU've posted");
            w.Divider();

            w.SubHeader("Sort & Tile Cycle");
            w.Bullet("Click a column header (Item / Qty / Price / Seller) to sort by it");
            w.Bullet("Re-click the same header to toggle ↑ / ↓");
            w.Bullet("Click 'Tiles' top-right to cycle Compact (24 rows) / Normal (14) / Large (8)");
            w.Divider();

            w.SubHeader("Currency Dropdown");
            w.Paragraph("On the Sell tab, the Currency dropdown lists every currency name accepted by any loaded Banker profile " +
                "(plus 'Coins' as a baseline). So your trader can list items in Ruby, SilverNecklace, Amber, etc. if a banker supports them.");
            w.Divider();

            w.SubHeader("Config Files");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Marketplace/<profile>.cfg");
            w.Paragraph("Sample-listing format: Currency,Price,ItemPrefab,Count,SellerName per line.");
        }

        private static void BuildBanker(HelpContentWriter w)
        {
            w.Header("Banker");
            w.Paragraph("Banker NPCs hold currency on your behalf. Each banker profile defines which currencies it accepts, " +
                "the maximum balance per currency, and an optional passive interest rate.");
            w.Divider();

            w.SubHeader("Player Use");
            w.Bullet("Deposit currency → it's removed from your inventory and tracked on your vault data");
            w.Bullet("Withdraw — items come back into your inventory");
            w.Bullet("Interest accrues passively while you're online (configured in seconds per minute)");
            w.Divider();

            w.SubHeader("Config Format");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Bankers/<name>.cfg\n\n" +
                "[profilename]\n" +
                "interest=0.05\n" +
                "Coins:1000000\n" +
                "SilverNecklace:200\n" +
                "Ruby:50\n" +
                "Amber:500");
            w.Bullet("interest= — percent per real-world minute (0.05 = 0.05% per minute)");
            w.Bullet("CurrencyPrefab:maxBalance — one per line");
            w.Divider();

            w.SubHeader("Example Auto-seeded");
            w.Paragraph("First time the Banker tab opens, an example.cfg is dropped into the Bankers/ folder so you have something to test with.");
        }

        private static void BuildTransmog(HelpContentWriter w)
        {
            w.Header("Transmog");
            w.Paragraph("Transmog overrides the VISUAL appearance of an equipped item while preserving its " +
                "underlying stats, set bonus, status effects, durability, and armor value.");
            w.Divider();

            w.SubHeader("Player Flow");
            w.Bullet("Visit a Transmog NPC, click an item in 'Your Items' (left pane).");
            w.Bullet("The right pane filters to transmog options matching that item's slot.");
            w.Bullet("Click an option → cost is consumed → visual swap is applied immediately.");
            w.Bullet("Inventory slots holding a transmog'd item show a cyan 'T' badge in the corner.");
            w.Bullet("Click Reverse Transmog (bottom-left) to clear the visual override.");
            w.Divider();

            w.SubHeader("What Persists");
            w.Bullet("Stats (armor, weapon damage, etc.) come from the ORIGINAL item, never the transmog");
            w.Bullet("Set bonuses key off the original item's set name");
            w.Bullet("Status effects (like Wolf armor's frost resist) come from the original");
            w.Bullet("Durability ticks down on the original — transmog doesn't wear");
            w.Bullet("Inventory icon shows the original item — only the on-character render swaps");
            w.Divider();

            w.SubHeader("Config Format");
            w.CodeBlock("BepInEx/config/FiresRPGmaker/Transmogrifications/<name>.cfg\n\n" +
                "[example]\n" +
                "Helmet|HelmetIron|Coins|150\n" +
                "Chest|ArmorBronzeChest|Coins|100\n" +
                "Legs|ArmorIronLegs|Coins|200\n" +
                "Cape|CapeOdin|Coins|350");
            w.Bullet("Pipe-separated: Slot | prefab | pricePrefab | priceAmount");
            w.Bullet("Slot values: Helmet | Chest | Legs | Cape (also OneHandedWeapon, TwoHandedWeapon, Bow, Tool, Shield, Shoulder, Utility)");
            w.Bullet("KG comma format is also auto-recognized: 'prefab, currency, price, ignoreCategory'");
        }

        private static void BuildMarketplaceAPI(HelpContentWriter w)
        {
            w.Header("Public API Surface");
            w.Paragraph("Other mods can reflect on these static APIs by name. Drop-in compatibility with the " +
                "KG-Marketplace API surface so 3rd-party mods that target KG also work with FiresRPGmaker.");
            w.Divider();

            w.SubHeader("API.ClientSide (assembly-qualified: 'API.ClientSide, FiresRPGmaker')");
            w.Bullet("FillingTerritoryData (static bool) — author-time write gate");
            w.Bullet("ResetTraderItems() — refresh trader inventory cache");
            w.Bullet("OpenQuestJournal() — open the player's journal UI");
            w.Bullet("IsPlayerInsideTerritory(out string name, out int flags, out int additional)");
            w.Bullet("IsObjectInsideTerritoryWithFlag(Vector3, int flag, out string, out int, out int)");
            w.Bullet("IsObjectInsideTerritoryWithFlag_Additional(Vector3, int flag, out string, out int, out int)");
            w.Divider();

            w.SubHeader("ZDO Extension Methods");
            w.Paragraph("Mirror KG's KG-prefixed NPC ZDO setter/getter pairs — same key names so KG-aware mods read/write the same data:");
            w.Bullet("SET_NPC_Type / GET_NPC_Type — KGmarketNPC enum");
            w.Bullet("SET_NPC_Profile / GET_NPC_Profile, SET_NPC_Model, SET_NPC_Name, SET_NPC_Dialogue");
            w.Bullet("33 NPC field extensions total — see Modules/Marketplace/API_ClientSide.cs");
        }

        // ── Quests: types/rewards/lifecycle ─────────────────────────────────

        private static void BuildQuestTypesAndRewards(HelpContentWriter w)
        {
            w.Header("Quest Types & Rewards");
            w.Paragraph("All 7 KG-parity quest types are wired. Each one watches a different in-game event and " +
                "increments the player's Progress when the event matches the quest target.");
            w.Divider();

            w.SubHeader("Quest Types");
            w.Bullet("Kill — target prefab is a creature; fires on death (Character.OnDeath)");
            w.Bullet("Collect — fires when an item enters the inventory (Player.OnInventoryChanged)");
            w.Bullet("Harvest — fires on Pickable.RPC_Pick (berries, mushrooms, flowers)");
            w.Bullet("Craft — fires on InventoryGui.DoCrafting completion");
            w.Bullet("Build — fires on Player.PlacePiece success");
            w.Bullet("Talk — fires when player completes dialogue with the target NPC name");
            w.Bullet("Move — distance check every 3 seconds vs target stored as 'x,y,z' coordinates (10m radius = hit)");
            w.Divider();

            w.SubHeader("Reward Types (12 KG-parity dispatchers)");
            w.Bullet("Item — bank into player's BankItems queue");
            w.Bullet("RandomItem — Target = comma/semicolon pool, picks one uniformly at random");
            w.Bullet("Skill — set the player's skill level to Amount");
            w.Bullet("Skill_EXP — raise the named skill by Amount XP");
            w.Bullet("Pet — spawn a Tameable creature already tamed");
            w.Bullet("SetCustomValue / AddCustomValue — write to player.m_customData[key]");
            w.Bullet("GuildAddLevel — placeholder (Guild leveling not yet implemented)");
            w.Bullet("EpicMMO_EXP, Cozyheim_EXP, Battlepass_EXP, RustyClasses_EXP — mod-bridge stubs (log a warning until partner mods wire in)");
            w.Divider();

            w.SubHeader("Reward Config Example");
            w.CodeBlock("[QuestId]\n" +
                "QuestType = Kill\n" +
                "QuestTarget = Boar, 10\n" +
                "QuestReward = Item: Coins, 50 | Skill_EXP: Clubs, 25 | RandomItem: BoarMeat;Feathers, 5");
        }

        private static void BuildChainAndLifecycle(HelpContentWriter w)
        {
            w.Header("Chain Quests & Lifecycle Events");
            w.Paragraph("KG-parity follow-up quests and event hooks. Use TransferComplition to auto-accept a follow-up " +
                "when a quest completes, and LifecycleEvents to fire dialogue commands at key moments.");
            w.Divider();

            w.SubHeader("TransferComplition (Chain Quests)");
            w.CodeBlock("[QuestA]\n" +
                "QuestType = Kill\n" +
                "QuestTarget = Greyling, 5\n" +
                "TransferComplition = QuestB\n" +
                "TransferComplitionText = Good work — see me for the next task.");
            w.Paragraph("When QuestA auto-completes, QuestB is auto-accepted for the same player (skips if already active). " +
                "TransferComplitionText shows in a center message.");
            w.Divider();

            w.SubHeader("Lifecycle Events");
            w.Paragraph("Each quest can attach commands (the same syntax dialogue options use) to lifecycle moments:");
            w.Bullet("OnAcceptQuest — fires when the quest is accepted");
            w.Bullet("OnCancelQuest — fires when the player cancels a quest");
            w.Bullet("OnCompleteQuest — fires on auto-complete (before TransferComplition)");
            w.Bullet("OnQuestTimeout — fires when a quest's time limit expires");
            w.Bullet("OnDeath — fires for every active quest the player has when they die");
            w.Divider();

            w.SubHeader("Lifecycle Config Example");
            w.CodeBlock("[QuestId]\n" +
                "QuestType = Kill\n" +
                "QuestTarget = Boar, 5\n" +
                "OnAcceptQuest = giveitem Coins 10 | message Good luck!\n" +
                "OnCompleteQuest = playsound Horn1 | message Well done.");
            w.Paragraph("Commands run through DialogueCommandEngine.ExecuteAll — full dialogue command surface available.");
        }

        // ── Dialogue conditions reference ────────────────────────────────────

        private static void BuildDialogueConditionsReference(HelpContentWriter w)
        {
            w.Header("Dialogue Conditions Reference");
            w.Paragraph("Conditions are filters that gate dialogue options. They run case-insensitively. " +
                "Prefix any condition with Not- to invert it (e.g. NotHasItem). " +
                "Multiple conditions on one option are AND-ed; multiple groups (pipe-separated) are OR-ed.");
            w.Divider();

            w.SubHeader("Player State Conditions");
            w.Bullet("HasItem <prefab> [amount] — player has at least N of an item");
            w.Bullet("HasCompletedQuest <questId> — player completed the quest at least once");
            w.Bullet("HasActiveQuest <questId> — quest is in the player's active list");
            w.Bullet("KilledMonster <prefab> [count] — player's kill tally");
            w.Bullet("PlayerLevelMore <level> — vanilla character level");
            w.Bullet("PlayerSkillMore <skill> <level> — skill level check");
            w.Bullet("IsVIP — player is admin (uses configSync.IsAdmin)");
            w.Bullet("IronGateStatMore <statId> <amount> — checks the player's PlayerStats record");
            w.Bullet("PlayerHasAllCustomDataKeys <k1,k2,...> — all keys exist on player.m_customData");
            w.Bullet("PlayerHasOneOfCustomDataKeys <k1,k2,...> — at least one key exists");
            w.Divider();

            w.SubHeader("Achievement / Guild Conditions");
            w.Bullet("HasAchievement <id> — leaderboard achievement check");
            w.Bullet("HasAchievementScore <amount> — total achievement score gate");
            w.Bullet("HasGuild — player is in any guild");
            w.Bullet("HasGuildWithName <name> — specific guild name");
            w.Bullet("GuildLevelMore <level> — stubbed (Guilds module doesn't yet track level)");
            w.Bullet("GuildHasAchievement <id> — stubbed (Guilds module doesn't yet track achievements)");
            w.Divider();

            w.SubHeader("Environment / NPC / Mod Conditions");
            w.Bullet("ModInstalled <pluginGuid> — checks the BepInEx plugin registry");
            w.Bullet("NpcNameEquals <name> — current dialogue NPC has this name");
            w.Bullet("NpcModelEquals <modelPrefab> — current NPC's model override matches");
        }

        // ── Test commands ───────────────────────────────────────────────────

        private static void BuildParityTestCommands(HelpContentWriter w)
        {
            w.Header("Parity Test Commands");
            w.Paragraph("The firesrpg_test family exercises every KG-parity pathway end-to-end and reports PASS/FAIL " +
                "per subsystem. Useful for verifying a fresh install or diagnosing a regression.");
            w.Divider();

            w.SubHeader("Master Sanity Check");
            w.Bullet("firesrpg_test sanity — runs every parity pathway: 7 quest types, 11 reward dispatchers, TransferComplition chain, 5 lifecycle events, API.ClientSide, 17 territory patches, 12 dialogue conditions, Marketplace v3. Prints PASS/FAIL summary. Self-cleaning.");
            w.Divider();

            w.SubHeader("Targeted Probes");
            w.Bullet("firesrpg_test help — full subverb list");
            w.Bullet("firesrpg_test quest_progress <type> <prefab> [amt] — fire a quest progress event");
            w.Bullet("firesrpg_test quest_reward <type> <target> [amt] [level] — grant one reward via QuestRewardDispatcher");
            w.Bullet("firesrpg_test quest_accept <questId> — add quest + fire OnAcceptQuest");
            w.Bullet("firesrpg_test quest_complete <questId> — complete + grants rewards + chain");
            w.Bullet("firesrpg_test quest_list — print your active + completed quests");
            w.Bullet("firesrpg_test lifecycle <questId> <event> — fire one lifecycle event manually");
            w.Bullet("firesrpg_test api — dump API.ClientSide output for the player's position");
            w.Bullet("firesrpg_test territory_flag <flagName> — report which territory has the named flag at your position");
            w.Bullet("firesrpg_test marketplace [profile] — open the Marketplace UI panel directly");
        }

        // ── Social: titles & factions ───────────────────────────────────────

        private static void BuildPlayerTitles(HelpContentWriter w)
        {
            w.Header("Player Titles");
            w.Paragraph("Titles are short honorifics shown on your floating nameplate for everyone to see.");
            w.Bullet("mytag <title> — set your own title");
            w.Bullet("mytag none — remove it");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin");
                w.Bullet("settag <player|me> <title|none> — set (or clear) any player's title");
            }
        }

        private static void BuildFactions(HelpContentWriter w)
        {
            w.Header("Factions");
            w.Paragraph("Factions are server-defined allegiances a character can join. Your faction shows " +
                "on your nameplate with its icon, and can carry a passive faction buff while you're a member.");
            w.Divider();

            w.SubHeader("Joining & Leaving");
            w.Bullet("faction — print your current faction");
            w.Bullet("faction <id> — join that faction");
            w.Bullet("faction none — leave your faction");
            w.Bullet("factionlist — list every faction on this server");
            w.Bullet("Dialogue NPCs can also grant or strip factions (setfaction / removefaction commands), " +
                "so servers can gate membership behind quests or story beats.");
            w.Divider();

            w.SubHeader("What Membership Does");
            w.Bullet("Faction name/icon on your nameplate");
            w.Bullet("Optional passive buff applied while you're a member (reapplied on change)");
            w.Bullet("Membership is saved on your character and survives logout");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Authoring Factions");
                w.Bullet("factioneditor — open the faction editor window (create/edit factions live)");
                w.Bullet("factionreload — reload faction definitions from disk");
                w.Bullet("Definitions live in FiresRPGmaker/Factions/ (yaml, server-synced to clients).");
                w.Bullet("The MP single-file format (Marketplace/Configs/Factions.yml) also loads.");
            }
        }

        // ── Marketplace: mail, buffs, leaderboard ───────────────────────────

        private static void BuildMail(HelpContentWriter w)
        {
            w.Header("Mail");
            w.Paragraph("Mail NPCs let players send messages and item attachments to each other — including " +
                "to players who are offline. Mail waits server-side until the recipient opens a mailbox.");
            w.Divider();

            w.SubHeader("Using a Mailbox");
            w.Bullet("Press E on a Mail NPC to open your mailbox.");
            w.Bullet("Compose: pick a recipient, write a message, optionally attach items.");
            w.Bullet("Attachments are removed from your inventory when sent, and claimed by the recipient.");
            w.Bullet("Unread mail is waiting for you whenever you next open any mailbox.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin");
                w.Bullet("Assign the Mail role to an NPC in the admin book (Shift+E).");
                w.Bullet("Server-seeded mail profiles live in FiresRPGmaker/Mail/.");
                w.Bullet("mail_reset — reload Mail profiles from disk (re-fills claimed attachments; for testing).");
            }
        }

        private static void BuildBuffStation(HelpContentWriter w)
        {
            w.Header("Buff Station");
            w.Paragraph("Buffer NPCs sell status-effect packages — timed buffs you buy with currency. " +
                "Think of them as potion vendors whose potions apply instantly.");
            w.Divider();

            w.SubHeader("Buying Buffs");
            w.Bullet("Press E on a Buffer NPC to open its buff shop.");
            w.Bullet("Each entry shows the effects, duration, and price. Click to buy — the buff applies immediately.");
            w.Bullet("Purchases are validated server-side, so prices and effects are always the server's.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin: Building Buff Profiles");
                w.Bullet("firesnpcs_buff_admin — open the profile builder: browse every status effect in the " +
                    "game and assemble named buff packages.");
                w.Bullet("firesnpcs_buff_shop [profileKey] — open a shop UI for any profile to preview it.");
                w.Bullet("firesnpcs_buff_apply — apply a buff directly (testing).");
                w.Bullet("firesnpcs_buffer_dump / firesnpcs_buffer_list_se — dump profiles / list status effects.");
                w.Bullet("Content lives in FiresRPGmaker/Buffers/ and BufferProfiles/.");
            }
        }

        private static void BuildLeaderboard(HelpContentWriter w)
        {
            w.Header("Leaderboard & Achievements");
            w.Paragraph("The server tracks per-player stats (kills, deaths, building, harvesting, and more) " +
                "and a configurable achievement set. Press L to see where you stand.");
            w.Divider();

            w.SubHeader("The Panel");
            w.Bullet("L (Hotkeys.LeaderboardKey) — open the leaderboard");
            w.Bullet("Stats tab — ranked standings per stat category");
            w.Bullet("Achievements tab — your achievement progress and score");
            w.Bullet("firesnpcs_leaderboard / firesnpcs_achievements — open either tab by command");
            w.Bullet("Leaderboard and Achievement NPC roles open the same panel from the world.");
            w.Divider();

            w.SubHeader("Seasons");
            w.Paragraph("Standings can run in seasons — the server can archive a season and start fresh " +
                "while lifetime achievements persist.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin");
                w.Bullet("Leaderboard.AchievementProfile — which achievement set this world uses");
                w.Bullet("firesnpcs_gen_achievements — generate the achievement definition template");
                w.Bullet("firesnpcs_leaderboard_newseason — archive current standings, start a new season");
                w.Bullet("firesnpcs_leaderboard_wipe — wipe standings entirely");
                w.Bullet("Definitions live in FiresRPGmaker/LeaderboardAchievements/; data in the per-world vault.");
            }
        }

        // ── Games ───────────────────────────────────────────────────────────

        private static void BuildGamblerGames(HelpContentWriter w)
        {
            w.Header("Gambler Games");
            w.Paragraph("Gambler NPCs run wager minigames — stake currency, play, win or lose the pot. " +
                "Each gambler's games, stakes, and odds come from its server-side profile.");
            w.Divider();

            w.SubHeader("The Games");
            w.Bullet("Casting the Bones — dice game");
            w.Bullet("The Gamble for Hísing — dice duel against the Jarl");
            w.Bullet("Cast the Runes — rune-draw game");
            w.Bullet("Wheel of Fortune — spin for a prize from the wheel's item pool");
            w.Divider();

            w.Paragraph("Press E on a Gambler NPC and pick a game. Stakes are taken up front; winnings pay " +
                "out immediately.");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin");
                w.Bullet("Assign the Gambler role in the admin book; profiles live in FiresRPGmaker/Gamblers/.");
                w.Bullet("Preview any game free (client UI only, no cost/reward): firesnpcs_dice [count], " +
                    "firesnpcs_duel, firesnpcs_runes [count], firesnpcs_wheel.");
            }
        }

        private static void BuildBoardGames(HelpContentWriter w)
        {
            w.Header("Board Games & Poker");
            w.Paragraph("Playable tables you can sit at in the world — chess, fanorona, and poker. " +
                "Games run against other seated players or an AI opponent.");
            w.Divider();

            w.SubHeader("Chess");
            w.Bullet("Interact with a placed chess table to sit and play.");
            w.Bullet("Play a seated human opponent, or the AI when alone.");
            w.Bullet("Chess.Difficulty — Easy / Medium / Hard / Master (hot-reloads).");
            w.Divider();

            w.SubHeader("Fanorona");
            w.Paragraph("The Malagasy capture board game, same table flow as chess. UI scale and position " +
                "are configurable under [Fanorona].");
            w.Divider();

            w.SubHeader("Poker");
            w.Bullet("Sit at a poker table to join the hand. Open (cash) tables buy in from your BANK " +
                "balance — deposit currency with a Banker first.");
            w.Bullet("Poker.CashCurrency / CashBuyIn / CashBigBlind — table stakes (server-set).");
            w.Bullet("Poker.PlayerTurnSeconds — act within the timer or auto-fold (default 60s).");
            w.Bullet("Empty seats are filled by AI opponents (Poker.Difficulty).");

            if (IsCurrentPlayerAdmin())
            {
                w.AdminDivider();
                w.AdminHeader("Admin");
                w.Bullet("Tables are placeable pieces — chess ships as an in-world table prefab.");
                w.Bullet("Test commands: firesnpcs_chess, firesnpcs_fanorona, firesnpcs_poker (+ _test/_sim " +
                    "variants for AI-vs-AI soak runs).");
            }
        }

        // ── Interface: tracker & chat window ────────────────────────────────

        private static void BuildQuestTracker(HelpContentWriter w)
        {
            w.Header("Quest Tracker HUD");
            w.Paragraph("A compact on-screen tracker that shows your active quests and live objective " +
                "progress without opening the journal.");
            w.Bullet("UI.QuestTracker.ShowQuestTracker — turn the tracker on/off");
            w.Bullet("Click its header to collapse/expand (UI.QuestTracker.Collapsed persists)");
            w.Bullet("Drag to reposition — the spot is saved (PosX/PosY, UserPositioned)");
            w.Bullet("Objectives tick up in real time as you kill/collect/build");
        }

        private static void BuildCustomChatWindow(HelpContentWriter w)
        {
            w.Header("Custom Chat Window");
            w.Paragraph("Verdant's Ascent can re-skin and re-arrange Valheim's chat into the Fires " +
                "brown-and-gold look with movable panels.");
            w.Bullet("Chat.CustomChatWindow — enable the custom chat window");
            w.Bullet("Chat.ArrangeHotkey — enter arrange mode to drag the chat panel around");
            w.Bullet("Chat.PanelOffsetX / PanelOffsetY / PanelScale — saved placement and size");
            w.Bullet("The channel selector column (Local/Global/Group/Guild) docks to the chat window — " +
                "see Chat Channels for how channels work");
        }

        // ── Server (Admin): config delivery ─────────────────────────────────

        private static void BuildConfigDelivery(HelpContentWriter w)
        {
            w.AdminHeader("Config Delivery & Push/Pull");
            w.Paragraph("On a dedicated server, FiresRPGmaker content (quests, dialogues, traders, ...) " +
                "lives ONLY on the server. Clients receive it in memory on connect — nothing under " +
                "config/FiresRPGmaker/ on a client machine is read while connected to a dedi. That kills " +
                "the old failure mode where a stale local file shadowed the server's content.");
            w.AdminDivider();

            w.SubHeader("The Editing Loop");
            w.Bullet("firesnpcs_pull_configs — download the server's whole FiresRPGmaker config tree to " +
                "your machine for editing in a real editor.");
            w.Bullet("Edit locally, then: firesnpcs_push_configs <folder|all> — push a content folder " +
                "(Quests, QuestProfiles, QuestEvents, Dialogues, Traders, ServerInfos, Bankers, Gamblers, " +
                "Transmogrifications, Mail, Buffers, BufferProfiles, LeaderboardAchievements, Territories) " +
                "back to the server. Admin-gated; the server hot-reloads.");
            w.Bullet("pushcfgs [--force] — legacy pusher for modified files (pushquests is an alias).");
            w.Bullet("firesnpcs_reload [subsystem|all] — reload Marketplace-module .cfg files from disk " +
                "(gambler, marketplace, achievement, leaderboard, mail, transmog, banker).");
            w.Bullet("firesnpcs_reset_examples — re-seed the example content files.");
            w.AdminDivider();

            w.SubHeader("Any Other Mod's Config");
            w.Paragraph("For configs OUTSIDE FiresRPGmaker/ (ExpandWorld yaml, any mod's .cfg), use " +
                "FiresUnifiedCore's generic commands instead: pushconfigs / pullconfigs / deleteconfig " +
                "with wildcard patterns — see the Fires Core help group.");
        }

        // ── Commands (Admin): quests, keys, migration ───────────────────────

        private static void BuildQuestKeyCommands(HelpContentWriter w)
        {
            w.Header("Quest, Key & Migration Commands");

            w.SubHeader("Quests");
            w.Bullet("startquest=Quest Name — start a quest by display name");
            w.Bullet("queststatus — show your current quest state");
            w.Bullet("listquests [term] / listquestprofiles — list loaded content");
            w.Bullet("startsamplequest — start the KillBoarSample debug quest");
            w.Bullet("firesnpcs_dialogue_cooldown_reset — clear dialogue cooldowns");
            w.Divider();

            w.SubHeader("Progression Keys (va_*)");
            w.Bullet("va_listprivatekeys / va_listcustomkeys — list your private/custom keys");
            w.Bullet("va_setprivatekey [key] / va_removeprivatekey [key] — grant/remove a private key");
            w.Bullet("va_addcustomkey / va_removecustomkey — custom key management");
            w.Bullet("va_resetprivatekeys / va_clearmykeys / va_clearallkeys — bulk clears");
            w.Bullet("va_bosscount — show defeated boss-key count");
            w.Divider();

            w.SubHeader("KG-Marketplace Migration");
            w.Bullet("fires_migrate_kg — scan/convert kg Marketplace NPC ZDOs to Fires NPCs " +
                "(scan first, then confirm)");
            w.Bullet("firesnpcs_import_kg — import kg-format content configs");
            w.Divider();

            w.SubHeader("Books");
            w.Bullet("firesnpcs_bookview — preview an animated book; 'capture' renders frames to BepInEx/config/FiresRPGmaker/Debug/BookView/");
        }
    }
}
