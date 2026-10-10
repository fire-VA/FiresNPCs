using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using BepInEx;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Dialogue loader compatible with Marketplace/KG-style .cfg profiles.
    /// Reads from Config/FiresRPGmaker/Dialogues/ (all .cfg files, including legacy/ subfolder).
    /// 
    /// Supports the full Marketplace dialogue format:
    /// - [uid] or [uid=bg_image] headers
    /// - @interaction / @inrange / @outrange prefixes
    /// - Pipe-separated attributes: Text, Transition, RandomTransition, Command, RandomCommand,
    ///   Condition (with || OR groups), Icon, AlwaysVisible, Color, OverrideError
    /// </summary>
    public static class DialogueCodex
    {
        private static bool _initialized;
        private static readonly Dictionary<string, RawDialogue> _dialogues = new Dictionary<string, RawDialogue>(StringComparer.OrdinalIgnoreCase);

        private static string BaseDir => Path.Combine(FiresCore.Storage.FiresConfigPaths.Dialogues);
        private static string LegacyDir => Path.Combine(BaseDir, "legacy");
        private static string SamplePath => Path.Combine(LegacyDir, "SampleDialogue.cfg");

        /// <summary>OR condition separator used inside a single Condition: value.</summary>
        private const string OR_SEPARATOR = "||";

        public static IReadOnlyDictionary<string, RawDialogue> Dialogues => _dialogues;

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                // Pure clients parse the server-synced in-memory store (no config files on client disk);
                // server/host reads + seeds its folder as before.
                if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
                {
                    Directory.CreateDirectory(LegacyDir);
                    WriteSampleIfMissing();
                }
                LoadAllDialogues();
                Debug.Log($"[FiresRPGmaker] DialogueCodex loaded {_dialogues.Count} dialogues");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] DialogueCodex init failed: {ex.Message}");
            }
        }

        /// <summary>Forces a full reload (used by ServerConfigFileWatcher).</summary>
        public static void Reload()
        {
            _dialogues.Clear();
            _initialized = false;
            Init();
        }

        public static RawDialogue Get(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            _dialogues.TryGetValue(uid, out var dlg);
            return dlg;
        }

        public static IEnumerable<RawDialogue> GetAll() => _dialogues.Values;

        /// <summary>
        /// Parses dialogue entries from raw .cfg content string and merges into the codex.
        /// Used by the sync system to apply server-pushed content without writing to disk first.
        /// </summary>
        public static void ParseFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            try
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                ParseLines(lines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] DialogueCodex.ParseFromContent failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes all dialogues whose UIDs were defined in the given .cfg content.
        /// Used when a file is deleted - the server sends empty content, 
        /// and the client needs to remove those UIDs.
        /// </summary>
        public static void RemoveDialoguesFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            try
            {
                var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (line.StartsWith("["))
                    {
                        string header = line.Substring(1);
                        int closeBracket = header.IndexOf(']');
                        if (closeBracket >= 0)
                            header = header.Substring(0, closeBracket);
                        header = header.Trim();
                        int eqIdx = header.IndexOf('=');
                        string uid = eqIdx > 0
                            ? header.Substring(0, eqIdx).Trim()
                            : header.Replace(" ", string.Empty);
                        _dialogues.Remove(uid);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] DialogueCodex.RemoveDialoguesFromContent failed: {ex.Message}");
            }
        }

        #region Loading

        private static void LoadAllDialogues()
        {
            _dialogues.Clear();
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("Dialogues"))
            {
                try
                {
                    ParseFile(kv.Key, kv.Value);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Dialogue parse failed {kv.Key}: {ex.Message}");
                }
            }
        }

        #endregion

        #region Parser

        private enum InputType
        {
            Text,
            Transition,
            Command,
            RandomCommand,
            Icon,
            Condition,
            AlwaysVisible,
            Color,
            RandomTransition,
            OverrideError,
            Cooldown,
            CooldownKey
        }

        private static void ParseFile(string path, string[] lines)
        {
            ParseLines(lines);
        }

        /// <summary>
        /// Known Marketplace condition prefixes that can appear bare (without "Condition:" key)
        /// after a pipe separator. e.g. "NotHasPlayerKey, DPS" instead of "Condition: NotHasPlayerKey, DPS".
        /// </summary>
        private static readonly HashSet<string> KnownConditionPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "HasItem", "NotHasItem", "HasQuest", "NotHasQuest",
            "QuestFinished", "QuestNotFinished", "QuestProgressDone", "QuestProgressNotDone",
            "GlobalKey", "NotGlobalKey", "HasPlayerKey", "NotHasPlayerKey",
            "SkillMore", "SkillLess", "HasBuff", "NotHasBuff",
            "CustomPlayerKeyMore", "CustomPlayerKeyLess",
            "HealthMore", "HealthLess"
        };

        /// <summary>Checks if a pipe segment is a bare condition (no Condition: prefix).</summary>
        private static bool IsBareCondition(string seg)
        {
            // Bare conditions look like "NotHasPlayerKey, DPS" or "HasItem,Sword,1"
            // The first token (before comma or space) must be a known condition name.
            string firstToken = seg.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return firstToken != null && KnownConditionPrefixes.Contains(firstToken);
        }

        /// <summary>0.0.532 (Fire 10-08, conditions fail closed): a part of an option line the parser doesn't understand
        /// (a typo'd key, a condition without "Condition:" that isn't one of the bare names) used to be dropped silently,
        /// which could leave the option un-gated. It now locks the option through a condition the engine always fails,
        /// with one warning naming the line.</summary>
        private static void LockUnreadable(List<string[]> conditions, string uid, string segment, string line)
        {
            conditions.Add(new[] { "UnreadableDialoguePart," + segment.Replace(",", ";") });
            Debug.LogWarning($"[DialogueCodex] Dialogue '{uid}': '{segment}' isn't understood (a typo, or a condition missing 'Condition:'?); the option stays locked. Line: {line}");
        }
        private static void ParseLines(string[] lines)
        {
            RawDialogue current = null;
            List<RawPlayerOption> options = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                // --- Header line: [uid] or [uid=bg_image] ---
                // Marketplace allows headers without closing bracket: [RoleSelectDps
                if (line.StartsWith("["))
                {
                    // Finalize previous dialogue
                    if (current != null && options != null)
                    {
                        current.Interactions = options.ToArray();
                        _dialogues[current.UID] = current;
                    }

                    // Strip leading [ and optional trailing ]
                    string header = line.Substring(1);
                    int closeBracket = header.IndexOf(']');
                    if (closeBracket >= 0)
                        header = header.Substring(0, closeBracket);
                    header = header.Trim();

                    string uid;
                    string bgImage = string.Empty;

                    int eqIdx = header.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        uid = header.Substring(0, eqIdx).Trim();
                        bgImage = header.Substring(eqIdx + 1).Trim();
                    }
                    else
                    {
                        uid = header.Replace(" ", string.Empty);
                    }

                    current = new RawDialogue { UID = uid, Text = null, BG_Image = bgImage };
                    options = new List<RawPlayerOption>();
                    continue;
                }

                if (current == null) continue;

                // --- First content line = NPC dialogue text ---
                if (current.Text == null)
                {
                    current.Text = line.Replace("\\n", "\n");
                    continue;
                }

                // --- Interaction/option line ---
                var opt = new RawPlayerOption();

                // Check for @interaction / @inrange / @outrange prefix
                if (line.StartsWith("@interaction", StringComparison.OrdinalIgnoreCase))
                {
                    opt.Type = InteractionType.Interaction;
                    line = line.Substring("@interaction".Length).Trim();
                }
                else if (line.StartsWith("@inrange", StringComparison.OrdinalIgnoreCase))
                {
                    opt.Type = InteractionType.InRange;
                    line = line.Substring("@inrange".Length).Trim();
                }
                else if (line.StartsWith("@outrange", StringComparison.OrdinalIgnoreCase))
                {
                    opt.Type = InteractionType.OutRange;
                    line = line.Substring("@outrange".Length).Trim();
                }

                var commands = new List<string>();
                var randomCommands = new List<KeyValuePair<int, string>>();
                var conditions = new List<string[]>();

                foreach (var segment in line.Split('|'))
                {
                    string seg = segment.Trim();
                    if (string.IsNullOrEmpty(seg)) continue;

                    // Check for bare conditions without "Condition:" prefix
                    // e.g. "NotHasPlayerKey, DPS" after a pipe separator
                    if (IsBareCondition(seg))
                    {
                        string condPayload = ReplaceSpacesOutsideQuotes(seg);
                        string[] orParts = condPayload.Split(new[] { OR_SEPARATOR }, StringSplitOptions.RemoveEmptyEntries);
                        var orGroup = new List<string>();
                        foreach (var orPart in orParts)
                        {
                            string trimmed = orPart.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                                orGroup.Add(trimmed);
                        }
                        if (orGroup.Count > 0)
                            conditions.Add(orGroup.ToArray());
                        continue;
                    }

                    string[] parts = seg.Split(new[] { ':' }, 2);
                    if (parts.Length != 2)
                    {
                        // Fallback: handle "Transition uid" without colon (common typo)
                        if (seg.StartsWith("Transition ", StringComparison.OrdinalIgnoreCase))
                        {
                            opt.NextUID = seg.Substring("Transition ".Length).Trim()
                                .Replace(" ", string.Empty);
                        }
                        else LockUnreadable(conditions, current?.UID, seg, line);
                        continue;
                    }

                    string keyStr = parts[0].Trim();
                    string payload = parts[1];

                    if (!Enum.TryParse(keyStr, true, out InputType type)) { LockUnreadable(conditions, current?.UID, seg, line); continue; }

                    switch (type)
                    {
                        case InputType.Text:
                            opt.Text = payload.Trim().Replace("\\n", "\n");
                            break;
                        case InputType.Transition:
                            opt.NextUID = payload.Replace(" ", string.Empty).Trim();
                            break;
                        case InputType.RandomTransition:
                            opt.RandomTransition = payload.Replace(" ", string.Empty).Trim()
                                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            break;
                        case InputType.Command:
                            commands.Add(ReplaceSpacesOutsideQuotes(payload));
                            break;
                        case InputType.RandomCommand:
                            var rcParts = payload.Split(new[] { ',' }, 2);
                            if (rcParts.Length == 2 && int.TryParse(rcParts[0].Trim(), out int weight))
                                randomCommands.Add(new KeyValuePair<int, string>(weight, ReplaceSpacesOutsideQuotes(rcParts[1])));
                            break;
                        case InputType.Condition:
                        {
                            // Split by OR separator (||) into an OR group
                            string condPayload = ReplaceSpacesOutsideQuotes(payload);
                            string[] orParts = condPayload.Split(new[] { OR_SEPARATOR }, StringSplitOptions.RemoveEmptyEntries);
                            var orGroup = new List<string>();
                            foreach (var orPart in orParts)
                            {
                                string trimmed = orPart.Trim();
                                if (!string.IsNullOrEmpty(trimmed))
                                    orGroup.Add(trimmed);
                            }
                            if (orGroup.Count > 0)
                                conditions.Add(orGroup.ToArray());
                            break;
                        }
                        case InputType.Icon:
                            opt.Icon = payload.Trim();
                            break;
                        case InputType.AlwaysVisible:
                            bool.TryParse(payload.Trim(), out opt.AlwaysVisible);
                            break;
                        case InputType.Color:
                            var cs = payload.Split(',');
                            if (cs.Length == 3 &&
                                byte.TryParse(cs[0].Trim(), out var r) &&
                                byte.TryParse(cs[1].Trim(), out var g) &&
                                byte.TryParse(cs[2].Trim(), out var b))
                                opt.Color = new Color32(r, g, b, 255);
                            break;
                        case InputType.OverrideError:
                            opt.OverrideError = payload.Trim();
                            break;
                        case InputType.Cooldown:
                        {
                            // Scope | Scope,seconds | Scope,seconds,show  (comma-separated, no spaces)
                            var parts2 = payload.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts2.Length > 0 && Enum.TryParse(parts2[0].Trim(), true, out CooldownScope scope))
                            {
                                opt.Cooldown = scope;
                                if (parts2.Length > 1 && int.TryParse(parts2[1].Trim(), out int secs))
                                    opt.CooldownSeconds = secs;
                                if (parts2.Length > 2)
                                    opt.ShowWhileCooling = parts2[2].Trim().Equals("show", StringComparison.OrdinalIgnoreCase);
                            }
                            break;
                        }
                        case InputType.CooldownKey:
                            opt.CooldownKey = payload.Trim();
                            break;
                    }
                }

                opt.Commands = commands.ToArray();
                opt.RandomCommands = randomCommands;
                opt.Conditions = conditions;
                options.Add(opt);
            }

            // Finalize last dialogue
            if (current != null && options != null)
            {
                current.Interactions = options.ToArray();
                _dialogues[current.UID] = current;
            }
        }

        /// <summary>
        /// Replaces spaces with commas outside of quoted strings, matching Marketplace behaviour.
        /// "GiveItem, Coins, 50" and "GiveItem Coins 50" both become "GiveItem,Coins,50".
        /// </summary>
        private static string ReplaceSpacesOutsideQuotes(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var sb = new System.Text.StringBuilder(input.Length);
            bool inQuote = false;
            foreach (char c in input)
            {
                if (c == '"') { inQuote = !inQuote; continue; }
                if (c == ' ' && !inQuote) { sb.Append(','); continue; }
                sb.Append(c);
            }
            // Collapse repeated commas
            string result = sb.ToString();
            while (result.Contains(",,"))
                result = result.Replace(",,", ",");
            return result.Trim(',');
        }

        #endregion

        #region Sample File

        private static void WriteSampleIfMissing()
        {
            if (File.Exists(SamplePath)) return;
            try
            {
                Directory.CreateDirectory(LegacyDir);
                string sample =
@"# ===============================================================
# Sample Dialogue - Marketplace-compatible format reference
# Place .cfg files in Config/FiresRPGmaker/Dialogues/ (any subfolder)
#
# FORMAT GUIDE:
#   [uid]             - Starts a new dialogue entry (lowercase, no spaces)
#   [uid=bg_image]    - With optional background image
#   First line        - NPC text shown to the player (\n = newline)
#   Subsequent lines  - Options / interactions (pipe-separated attributes)
#
# OPTION ATTRIBUTES (pipe | separated):
#   Text:...          - Button label
#   Transition:uid    - Jump to another dialogue on click
#   Command:...       - Execute a command (multiple allowed)
#   Condition:...     - Require a condition (multiple = AND; || within = OR)
#   Icon:PrefabName   - Show item icon
#   Color:R,G,B       - Tint the option (0-255)
#   AlwaysVisible:false - Hide instead of grey-out when condition fails
#   OverrideError:msg - Custom error text when condition fails
#   RandomTransition:a,b,c - Pick a random transition target
#   RandomCommand:weight,cmd - Percentage chance to run command
#
# LINE PREFIXES (before the first pipe):
#   @interaction      - Auto-execute commands silently (not shown as button)
#   @inrange          - Triggered when player is near NPC (future)
#   @outrange         - Triggered when player moves away (future)
#
# SUPPORTED CONDITIONS:
#   HasItem,Prefab,Amt[,Lvl] / NotHasItem      - Inventory check
#   HasQuest,QuestID / NotHasQuest              - Quest active check
#   QuestFinished,QuestID / QuestNotFinished    - Quest completion check
#   QuestProgressDone,QuestID / ...NotDone      - Objectives met check
#   GlobalKey,Key / NotGlobalKey                - World key check
#   HasPlayerKey,Key / NotHasPlayerKey          - Player unique key
#   SkillMore,Skill,Level / SkillLess           - Skill level check
#   HasBuff,SE / NotHasBuff                     - Status effect check
#   CustomPlayerKeyMore,Key,Val / CustomPlayerKeyLess   - Player ZDO int check (+ EWP sync)
#   HealthMore,Amt / HealthLess                 - Current HP check
#   !ConditionName - negate any condition
#
# SUPPORTED COMMANDS:
#   GiveItem,Prefab,Amt,Lvl / RemoveItem,Prefab,Amt
#   GiveQuest,QuestID / RemoveQuest / FinishQuest
#   Teleport,X,Y,Z[,AllowOre]  /  Spawn,Prefab,Amt,Lvl
#   SpawnRaw,Prefab,Amount,X,Y,Z,Rotation
#   Damage,Amt  /  Heal,Amt  /  GiveBuff,SE[,Duration]
#   AddPlayerKey,Key / RemovePlayerKey,Key
#   AddCustomPlayerKey,Key / AddCustomPlayerKey,Key/++[,Amt] / Key/--[,Amt] / RemoveCustomPlayerKey,Key
#   PlayAnimation,Trigger / ConsoleCommand,cmd args
#   AddPin,Label,X,Y,Z / PingMap,Msg,X,Y,Z
#   OpenUI[,Type,Profile]
#
# TEXT TOKENS:
#   %playername%      - Replaced with the player's name
#   $localization_key - Valheim localization lookup
# ===============================================================

[sample_merchant]
Welcome to my shop, traveler!\nWhat can I do for you today?
Text: Show me your wares | Command: OpenUI,Trader | Icon: Coins
Text: I'm looking for work | Transition: sample_quests | Icon: TrophyBoar
Text: Heard any rumors? | Transition: sample_rumors | Condition: SkillMore,Sneak,10 | AlwaysVisible: true | OverrideError: The merchant doesn't trust you yet (need Sneak 10).
Text: Farewell | Color: 180,180,180

[sample_quests]
I have a few tasks that need doing.\nInterested?
Text: Tell me more | Command: GiveQuest,sample_quest | Condition: NotHasQuest,sample_quest
Text: I already took your quest | Condition: HasQuest,sample_quest | AlwaysVisible: true | OverrideError: You're already working on this quest.
Text: Never mind | Transition: sample_merchant

[sample_rumors]
They say a troll guards treasure in the Black Forest...
@interaction Command: AddPlayerKey,heard_merchant_rumor
Text: Thanks for the tip! | Command: AddPin,Troll Cave,500,50,500 | Transition: sample_merchant
Text: I'll keep that in mind | Transition: sample_merchant
";
                File.WriteAllText(SamplePath, sample);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Failed to write sample dialogue: {ex.Message}");
            }
        }

        #endregion
    }

    #region Data Types

    public enum InteractionType
    {
        Option,
        Interaction,
        InRange,
        OutRange
    }

    /// <summary>How a dialogue option's cooldown is tracked on the server.</summary>
    public enum CooldownScope
    {
        None,
        PerPlayer,
        Global
    }

    [Serializable]
    public class RawDialogue
    {
        public string UID;
        public string Text;
        public string BG_Image;
        public RawPlayerOption[] Interactions = Array.Empty<RawPlayerOption>();
    }

    [Serializable]
    public class RawPlayerOption
    {
        public InteractionType Type = InteractionType.Option;
        public string Text;
        public string Icon;
        public string NextUID;
        public string[] RandomTransition = Array.Empty<string>();
        public string[] Commands = Array.Empty<string>();
        public List<KeyValuePair<int, string>> RandomCommands = new List<KeyValuePair<int, string>>();
        /// <summary>
        /// Conditions are AND between groups, OR within each group.
        /// Each string[] is one OR-group (any condition in the group passing = group passes).
        /// All groups must pass for the option to be available.
        /// </summary>
        public List<string[]> Conditions = new List<string[]>();
        public bool AlwaysVisible = true;
        public Color32 Color = new Color32(255, 255, 255, 255);
        public string OverrideError;

        /// <summary>Cooldown scope for this option's reward commands. None = no cooldown (default).</summary>
        public CooldownScope Cooldown = CooldownScope.None;
        /// <summary>Cooldown duration in seconds. Only meaningful when <see cref="Cooldown"/> != None.</summary>
        public int CooldownSeconds = 0;
        /// <summary>If true, the option stays visible (disabled) while cooling; if false it is hidden.</summary>
        public bool ShowWhileCooling = false;
        /// <summary>
        /// Stable hidden key frozen into the .cfg on first save so cooldowns survive later text/reward edits.
        /// Empty until frozen; resolved via <see cref="DialogueKeyUtil.OptionKey"/>.
        /// </summary>
        public string CooldownKey;

        /// <summary>
        /// Gets the next transition UID, picking randomly from RandomTransition if NextUID is empty.
        /// Returns null if no transition is defined (dialogue should close).
        /// </summary>
        public string GetNextTransition()
        {
            if (!string.IsNullOrEmpty(NextUID)) return NextUID;
            if (RandomTransition == null || RandomTransition.Length == 0) return null;
            string pick = RandomTransition[UnityEngine.Random.Range(0, RandomTransition.Length)];
            return string.IsNullOrWhiteSpace(pick) ? null : pick;
        }
    }

    #endregion
}
