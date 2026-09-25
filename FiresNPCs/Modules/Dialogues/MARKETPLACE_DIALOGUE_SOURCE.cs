// =============================================================================
// REFERENCE ONLY - Decompiled Marketplace Dialogue Source Code
// =============================================================================
// This file is NOT compiled. It exists solely as a reference for understanding
// how the Marketplace mod (kg.Marketplace v9.7.6) implements its dialogue system.
//
// Decompiled with JetBrains decompiler from:
//   kg.Marketplace.dll (Version=9.7.6.0)
//
// Contains:
//   - Dialogues_DataTypes  (data model, conditions, commands, serialization)
//   - Dialogues_Main_Client (client init, ESC handling, synced data init)
//   - Dialogues_Main_Server (server-side .cfg parser, file watcher hooks)
//   - Dialogues_UI          (runtime UI, hotbar keys, fade, option rendering)
// =============================================================================

#if false // Never compiled - reference only

using API;
using BepInEx.Bootstrap;
using fastJSON;
using Guilds;
using HarmonyLib;
using Marketplace.ExternalLoads;
using Marketplace.Modules.Global_Options;
using Marketplace.Modules.Leaderboard;
using Marketplace.Modules.NPC;
using Marketplace.Modules.Quests;
using Marketplace.OtherModsAPIs;
using ServerSync;
using Splatform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YamlDotNet.Serialization;

namespace Marketplace.Modules.Dialogues
{

// =============================================================================
// Dialogues_DataTypes - Data model, conditions, commands, serialization
// =============================================================================
public static class Dialogues_DataTypes
{
    internal static readonly CustomSyncedValue<List<Dialogues_DataTypes.RawDialogue>> SyncedDialoguesData =
        new CustomSyncedValue<List<Dialogues_DataTypes.RawDialogue>>(
            Marketplace.Marketplace.configSync, "dialoguesData",
            new List<Dialogues_DataTypes.RawDialogue>());

    internal static readonly Dictionary<string, Dialogues_DataTypes.Dialogue> ClientReadyDialogues =
        new Dictionary<string, Dialogues_DataTypes.Dialogue>();

    public static CustomSyncedValue<Dictionary<string, Dialogues_DataTypes.CustomSpawnZDO>> CustomSpawnZDOData_Synced =
        new CustomSyncedValue<Dictionary<string, Dialogues_DataTypes.CustomSpawnZDO>>(
            Marketplace.Marketplace.configSync, "customSpawnZDOData",
            new Dictionary<string, Dialogues_DataTypes.CustomSpawnZDO>());

    private const byte reverseFlag = 0x80;

    private static OptionCondition Reverse(this OptionCondition condition)
    {
        return condition ^ (OptionCondition)128;
    }

    public delegate bool Dialogue_Condition(
        Market_NPC.NPCcomponent npc,
        out string reason,
        out OptionCondition typeCondition);

    // =========================================================================
    // CustomSpawnZDO - ZDO data sets loaded from .yml files
    // =========================================================================
    [Serializable]
    public class CustomSpawnZDO : ISerializableParameter
    {
        public Dictionary<string, int> Ints;
        public Dictionary<string, float> Floats;
        public Dictionary<string, long> Longs;
        public Dictionary<string, string> Strings;
        public Dictionary<string, bool> Bools;

        public void Serialize(ref ZPackage pkg)
        {
            pkg.Write(Ints != null);
            if (Ints != null)
            {
                pkg.Write(Ints.Count);
                foreach (var kvp in Ints) { pkg.Write(kvp.Key); pkg.Write(kvp.Value); }
            }
            pkg.Write(Floats != null);
            if (Floats != null)
            {
                pkg.Write(Floats.Count);
                foreach (var kvp in Floats) { pkg.Write(kvp.Key); pkg.Write(kvp.Value); }
            }
            pkg.Write(Strings != null);
            if (Strings != null)
            {
                pkg.Write(Strings.Count);
                foreach (var kvp in Strings) { pkg.Write(kvp.Key); pkg.Write(kvp.Value); }
            }
            pkg.Write(Bools != null);
            if (Bools != null)
            {
                pkg.Write(Bools.Count);
                foreach (var kvp in Bools) { pkg.Write(kvp.Key); pkg.Write(kvp.Value); }
            }
            pkg.Write(Longs != null);
            if (Longs != null)
            {
                pkg.Write(Longs.Count);
                foreach (var kvp in Longs) { pkg.Write(kvp.Key); pkg.Write(kvp.Value); }
            }
        }

        public void Deserialize(ref ZPackage pkg)
        {
            if (pkg.ReadBool())
            {
                int c = pkg.ReadInt();
                Ints = new Dictionary<string, int>(c);
                for (int i = 0; i < c; i++) Ints[pkg.ReadString()] = pkg.ReadInt();
            }
            if (pkg.ReadBool())
            {
                int c = pkg.ReadInt();
                Floats = new Dictionary<string, float>(c);
                for (int i = 0; i < c; i++) Floats[pkg.ReadString()] = pkg.ReadSingle();
            }
            if (pkg.ReadBool())
            {
                int c = pkg.ReadInt();
                Strings = new Dictionary<string, string>(c);
                for (int i = 0; i < c; i++) Strings[pkg.ReadString()] = pkg.ReadString();
            }
            if (pkg.ReadBool())
            {
                int c = pkg.ReadInt();
                Bools = new Dictionary<string, bool>(c);
                for (int i = 0; i < c; i++) Bools[pkg.ReadString()] = pkg.ReadBool();
            }
            if (pkg.ReadBool())
            {
                int c = pkg.ReadInt();
                Longs = new Dictionary<string, long>(c);
                for (int i = 0; i < c; i++) Longs[pkg.ReadString()] = pkg.ReadLong();
            }
        }
    }

    // =========================================================================
    // OptionCommand - All command types supported by Marketplace dialogues
    // =========================================================================
    private enum OptionCommand
    {
        OpenUI,
        PlaySound,
        GiveQuest,
        RemoveQuest,
        FinishQuest,
        GiveItem,
        GiveItemWithData,
        SetPlayerData,
        RemoveItem,
        Spawn,
        SpawnXYZ,
        SpawnWithData,
        SpawnXYZWithData,
        Teleport,
        Damage,
        Heal,
        GiveBuff,
        AddPin,
        PingMap,
        AddEpicMMOExp,
        AddCozyheimExp,
        PlayAnimation,
        EnterPassword,
        GuildAddLevel,
        Battlepass_EXP,
        ConsoleCommand,
        AddCustomValue,
        SetCustomValue,
        PlayVideo,
        AddRustyClassesEXP,
        SendWebhook,
        AddPlayerKey,
        RemovePlayerKey,
        SetPlayerGuild,
        SetPlayerGuildRank,
        SetNPCModelLocal,
        SetNPCModelGlobal,
        SetNPCNameLocal,
        SetNPCNameGlobal,
        SetNPCPatrol,
    }

    // =========================================================================
    // OptionCondition - All condition types (byte enum with 0x80 reverse flag)
    // =========================================================================
    public enum OptionCondition : byte
    {
        None = 0,
        HasItem = 1,
        HasBuff = 2,
        SkillMore = 3,
        GlobalKey = 4,
        HasQuest = 5,
        QuestProgressDone = 6,
        QuestFinished = 7,
        EpicMMOLevelMore = 8,
        CozyheimLevelMore = 9,
        HasAchievement = 10,
        HasAchievementScore = 11,
        CustomValueMore = 12,
        ModInstalled = 13,
        IronGateStatMore = 14,
        HasGuild = 15,
        HasGuildWithName = 16,
        GuildLevelMore = 17,
        GuildHasAchievement = 18,
        IsVIP = 20,
        RustyClassesLevelMore = 21,
        HasPlayerKey = 22,
        NPCModelEquals = 23,
        HealthMore = 24,
        MaxHealthMore = 25,
        StaminaMore = 26,
        MaxStaminaMore = 27,
        EitrMore = 28,
        MaxEitrMore = 29,
        PlayerHasAllCustomDataKeys = 30,
        PlayerHasOneOfCustomDataKeys = 31,
        NPCNameEquals = 32,
        // Legacy aliases
        OtherQuest = 100,    // -> QuestFinished
        NotFinished = 101,   // -> QuestNotFinished
        Skill = 102,         // -> SkillMore
        // Reversed (0x80 flag)
        NotHasItem = 129,
        NotHasBuff = 130,
        SkillLess = 131,
        NotGlobalKey = 132,
        NotHasQuest = 133,
        QuestProgressNotDone = 134,
        QuestNotFinished = 135,
        EpicMMOLevelLess = 136,
        CozyheimLevelLess = 137,
        NotHasAchievement = 138,
        NotHasAchievementScore = 139,
        CustomValueLess = 140,
        NotModInstalled = 141,
        IronGateStatLess = 142,
        NotHasGuild = 143,
        NotHasGuildWithName = 144,
        GuildLevelLess = 145,
        GuildNotHasAchievement = 146,
        NotIsVIP = 148,
        RustyClassesLevelLess = 149,
        NotHasPlayerKey = 150,
        NotNPCModelEquals = 151,
        HealthLess = 152,
        MaxHealthLess = 153,
        StaminaLess = 154,
        MaxStaminaLess = 155,
        EitrLess = 156,
        MaxEitrLess = 157,
        NotNPCNameEquals = 160,
    }

    // =========================================================================
    // RawDialogue - Wire format (serialized between server and client)
    // =========================================================================
    public class RawDialogue : ISerializableParameter
    {
        public string UID;
        public string Text;
        public string BG_Image;
        public RawPlayerOption[] Interactions = Array.Empty<RawPlayerOption>();

        public void Serialize(ref ZPackage pkg)
        {
            pkg.Write(UID ?? "default");
            pkg.Write(Text ?? "");
            pkg.Write(BG_Image ?? "");
            pkg.Write(Interactions.Length);
            foreach (var interaction in Interactions)
            {
                pkg.Write((int)interaction.Type);
                pkg.Write(interaction.Text ?? "");
                pkg.Write(interaction.Icon ?? "");
                pkg.Write(interaction.NextUID ?? "");
                pkg.Write(interaction.RandomTransition.Length);
                foreach (string data in interaction.RandomTransition) pkg.Write(data);
                pkg.Write(interaction.Commands.Length);
                foreach (string cmd in interaction.Commands) pkg.Write(cmd);
                pkg.Write(interaction.RandomCommands.Count);
                foreach (var rc in interaction.RandomCommands) { pkg.Write(rc.Key); pkg.Write(rc.Value); }
                pkg.Write(interaction.Conditions.Count);
                foreach (string[] cond in interaction.Conditions)
                {
                    pkg.Write(cond.Length);
                    foreach (string c in cond) pkg.Write(c);
                }
                pkg.Write(interaction.AlwaysVisible);
                pkg.Write(Utils.ColorToVec3((Color)interaction.Color));
                pkg.Write(interaction.OverrideError ?? "");
            }
        }

        public void Deserialize(ref ZPackage pkg)
        {
            UID = pkg.ReadString();
            Text = pkg.ReadString();
            BG_Image = pkg.ReadString();
            int len = pkg.ReadInt();
            Interactions = new RawPlayerOption[len];
            for (int i = 0; i < len; i++)
            {
                Interactions[i] = new RawPlayerOption
                {
                    Type = (InteractionType)pkg.ReadInt(),
                    Text = pkg.ReadString(),
                    Icon = pkg.ReadString(),
                    NextUID = pkg.ReadString(),
                    RandomTransition = new string[pkg.ReadInt()]
                };
                for (int j = 0; j < Interactions[i].RandomTransition.Length; j++)
                    Interactions[i].RandomTransition[j] = pkg.ReadString();

                int cmdLen = pkg.ReadInt();
                Interactions[i].Commands = new string[cmdLen];
                for (int j = 0; j < cmdLen; j++)
                    Interactions[i].Commands[j] = pkg.ReadString();

                int rcCap = pkg.ReadInt();
                Interactions[i].RandomCommands = new List<KeyValuePair<int, string>>(rcCap);
                for (int j = 0; j < rcCap; j++)
                    Interactions[i].RandomCommands.Add(new KeyValuePair<int, string>(pkg.ReadInt(), pkg.ReadString()));

                int condCount = pkg.ReadInt();
                Interactions[i].Conditions = new List<string[]>();
                for (int j = 0; j < condCount; j++)
                {
                    string[] group = new string[pkg.ReadInt()];
                    for (int k = 0; k < group.Length; k++) group[k] = pkg.ReadString();
                    Interactions[i].Conditions.Add(group);
                }
                Interactions[i].AlwaysVisible = pkg.ReadBool();
                Interactions[i].Color = (Color32)Utils.Vec3ToColor(pkg.ReadVector3());
                Interactions[i].OverrideError = pkg.ReadString();
            }
        }

        public enum InteractionType { Option, Interaction, InRange, OutRange }

        public class RawPlayerOption
        {
            public InteractionType Type;
            public string Text;
            public string Icon;
            public string NextUID;
            public string[] RandomTransition = Array.Empty<string>();
            public string[] Commands = Array.Empty<string>();
            public List<KeyValuePair<int, string>> RandomCommands = new List<KeyValuePair<int, string>>();
            public List<string[]> Conditions = new List<string[]>();
            public bool AlwaysVisible = true;
            public Color32 Color = new Color32(255, 255, 255, 255);
            public string OverrideError;
        }
    }

    // =========================================================================
    // Dialogue - Client-ready runtime form (parsed from RawDialogue)
    // =========================================================================
    public class Dialogue
    {
        public string Text;
        public Sprite BG_Image;
        public PlayerInteraction[] Interactions = Array.Empty<PlayerInteraction>();

        // NOTE: ParseCommands, CombineOrConditions, ParseCondition contain
        // the full command/condition dispatch logic. See the switch statements
        // in the decompiled source for the exact implementation of each command
        // and condition type.

        public class PlayerInteraction
        {
            public RawDialogue.InteractionType Type;
            public string Text;
            public Sprite Icon;
            public string NextUID;
            public string[] RandomTransition;
            public Action<Market_NPC.NPCcomponent> Command;
            public Dictionary<int, Action<Market_NPC.NPCcomponent>> RandomCommands;
            public Dialogue_Condition Conditions;
            public bool AlwaysVisible;
            public Color Color = Color.white;
            public string OverrideError;

            public string NextTransition
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(NextUID)) return NextUID;
                    if (RandomTransition.Length == 0) return null;
                    string pick = RandomTransition[UnityEngine.Random.Range(0, RandomTransition.Length)];
                    return !string.IsNullOrWhiteSpace(pick) ? pick : null;
                }
            }

            public void CallCommands(Market_NPC.NPCcomponent npc)
            {
                Command?.Invoke(npc);
                if (RandomCommands == null || RandomCommands.Count <= 0) return;
                foreach (var rc in RandomCommands)
                {
                    if (UnityEngine.Random.Range(0, 100) < rc.Key && Player.m_localPlayer)
                        rc.Value?.Invoke(npc);
                }
            }

            public bool CheckCondition(Market_NPC.NPCcomponent npc, out string reason)
            {
                reason = "";
                if (Conditions == null) return true;
                foreach (Dialogue_Condition cond in Conditions.GetInvocationList().Cast<Dialogue_Condition>())
                {
                    if (!cond(npc, out reason, out _))
                    {
                        if (!string.IsNullOrWhiteSpace(OverrideError)) reason = OverrideError;
                        return false;
                    }
                }
                return true;
            }
        }
    }

    // =========================================================================
    // DialoguePassword - TextReceiver for EnterPassword command
    // =========================================================================
    public class DialoguePassword : TextReceiver
    {
        private string _password, _onSuccess, _onFail;
        private Market_NPC.NPCcomponent _npc;

        public DialoguePassword(Market_NPC.NPCcomponent npc, string title, string password, string onSuccess, string onFail)
        {
            _password = password; _onSuccess = onSuccess; _onFail = onFail; _npc = npc;
            TextInput.instance.RequestText(this, title, 30);
        }

        public string GetText() => "";

        public void SetText(string text)
        {
            if (text == _password)
            {
                if (!string.IsNullOrEmpty(_onSuccess))
                    Dialogues_UI.LoadDialogue(_npc, _onSuccess.ToLower());
            }
            else
            {
                if (!string.IsNullOrEmpty(_onFail))
                    Dialogues_UI.LoadDialogue(_npc, _onFail.ToLower());
            }
        }
    }
}


// =============================================================================
// Dialogues_Main_Client - Client-side init and ESC handler
// =============================================================================
public static class Dialogues_Main_Client
{
    private static void OnInit()
    {
        Dialogues_UI.Init();
        Dialogues_DataTypes.SyncedDialoguesData.ValueChanged += InitDialogues;
        // Global_Updator += Update  (handles ESC key)
    }

    private static void Update(float dt)
    {
        if (Input.GetKeyDown(KeyCode.Escape) && Dialogues_UI.IsVisible())
        {
            Dialogues_UI.Hide();
            Menu.instance.OnClose();
        }
    }

    private static void InitDialogues()
    {
        if (!ZNetScene.instance) return;
        Dialogues_DataTypes.ClientReadyDialogues.Clear();
        foreach (var raw in Dialogues_DataTypes.SyncedDialoguesData.Value)
            Dialogues_DataTypes.ClientReadyDialogues[raw.UID] = (Dialogues_DataTypes.Dialogue)raw;
    }

    // Also patches ZNetScene.Awake to call InitDialogues
}


// =============================================================================
// Dialogues_Main_Server - Server-side .cfg parser (THE CANONICAL PARSER)
// =============================================================================
// KEY INSIGHT: This is the authoritative parser. Note:
//   - Headers: line.StartsWith("[") - strips [ and ] and spaces, splits on =
//   - UID is lowercased: strArray[0].ToLower()
//   - Transitions are lowercased: strArray1[1].Replace(" ", "").ToLower()
//   - Pipe | splits the option line into segments
//   - Each segment is split on : with limit 2
//   - InputType enum parse determines the field type
//   - Commands go through ReplaceSpacesOutsideQuotes()
//   - Conditions are split by Global_Configs.OrConditionSeparator (default "||")
//   - NO special handling for bare conditions without "Condition:" prefix
//   - NO special handling for missing ] on headers
public static class Dialogues_Main_Server
{
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
    }

    private static void ProcessDialogueProfiles(string fPath, IReadOnlyList<string> profiles)
    {
        RawDialogue rawDialogue = null;
        List<RawPlayerOption> rawPlayerOptionList = null;

        for (int index = 0; index < profiles.Count; index++)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(profiles[index])) continue;
                if (profiles[index].StartsWith("#")) continue;

                // --- Header line ---
                if (profiles[index].StartsWith("["))
                {
                    // Finalize previous
                    if (rawDialogue != null)
                    {
                        rawDialogue.Interactions = rawPlayerOptionList.ToArray();
                        SyncedDialoguesData.Value.Add(rawDialogue);
                        rawDialogue = null;
                    }

                    string[] strArray = profiles[index]
                        .Replace("[", "")
                        .Replace("]", "")
                        .Replace(" ", "")
                        .Split('=');

                    rawDialogue = new RawDialogue
                    {
                        UID = strArray[0].ToLower(),
                        BG_Image = strArray.Length > 1 ? strArray[1] : null
                    };
                    rawPlayerOptionList = new List<RawPlayerOption>();
                }
                else if (rawDialogue != null)
                {
                    // --- First content line = NPC text ---
                    if (rawDialogue.Text == null)
                    {
                        rawDialogue.Text = profiles[index].Replace("\\n", "\n");
                    }
                    else
                    {
                        // --- Option/interaction line ---
                        var opt = new RawPlayerOption();
                        var keyValuePairList = new List<KeyValuePair<int, string>>();
                        string str1 = profiles[index];

                        // Check type prefixes
                        if (str1.StartsWith("@interaction"))
                        {
                            opt.Type = InteractionType.Interaction;
                            str1 = str1.Replace("@interaction", "").Trim();
                        }
                        else if (str1.StartsWith("@inrange"))
                        {
                            opt.Type = InteractionType.InRange;
                            str1 = str1.Replace("@inrange", "").Trim();
                        }
                        else if (str1.StartsWith("@outrange"))
                        {
                            opt.Type = InteractionType.OutRange;
                            str1 = str1.Replace("@outrange", "").Trim();
                        }

                        var commands = new List<string>();
                        var conditions = new List<string[]>();

                        // Split on pipe
                        foreach (string seg in str1.Split('|'))
                        {
                            string[] parts = seg.Split(new[] { ':' }, 2);
                            InputType result;
                            if (parts.Length == 2 && Enum.TryParse(parts[0], true, out result))
                            {
                                switch (result)
                                {
                                    case InputType.Text:
                                        opt.Text = parts[1].Trim().Replace("\\n", "\n");
                                        break;
                                    case InputType.Transition:
                                        opt.NextUID = parts[1].Replace(" ", "").ToLower();
                                        break;
                                    case InputType.Command:
                                        commands.Add(parts[1].ReplaceSpacesOutsideQuotes());
                                        break;
                                    case InputType.RandomCommand:
                                        var rcParts = parts[1].Split(new[] { ',' }, 2);
                                        int weight;
                                        if (rcParts.Length == 2 && int.TryParse(rcParts[0].Trim(), out weight))
                                            keyValuePairList.Add(new KeyValuePair<int, string>(weight, rcParts[1].ReplaceSpacesOutsideQuotes()));
                                        break;
                                    case InputType.Icon:
                                        opt.Icon = parts[1].Replace(" ", "");
                                        break;
                                    case InputType.Condition:
                                        var orGroup = new List<string>();
                                        string condPayload = parts[1].ReplaceSpacesOutsideQuotes();
                                        foreach (string orPart in condPayload.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                                        {
                                            if (!string.IsNullOrWhiteSpace(orPart))
                                                orGroup.Add(orPart);
                                        }
                                        conditions.Add(orGroup.ToArray());
                                        break;
                                    case InputType.AlwaysVisible:
                                        opt.AlwaysVisible = bool.Parse(parts[1].Replace(" ", ""));
                                        break;
                                    case InputType.Color:
                                        var cs = parts[1].Split(',');
                                        if (cs.Length == 3)
                                            opt.Color = new Color32(byte.Parse(cs[0]), byte.Parse(cs[1]), byte.Parse(cs[2]), 255);
                                        break;
                                    case InputType.RandomTransition:
                                        opt.RandomTransition = parts[1].Replace(" ", "").ToLower().Split(',');
                                        break;
                                    case InputType.OverrideError:
                                        opt.OverrideError = parts[1].Trim();
                                        break;
                                }
                            }
                        }

                        opt.Commands = commands.ToArray();
                        opt.Conditions = conditions;
                        opt.RandomCommands = keyValuePairList;
                        rawPlayerOptionList.Add(opt);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error with line number
                break;
            }
        }

        // Finalize last
        if (rawDialogue != null)
        {
            rawDialogue.Interactions = rawPlayerOptionList.ToArray();
            SyncedDialoguesData.Value.Add(rawDialogue);
        }
    }

    private static void ReadDialoguesData()
    {
        SyncedDialoguesData.Value.Clear();
        foreach (string file in Directory.GetFiles(Market_Paths.DialoguesFolder, "*.cfg", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file).ToList();
            ProcessDialogueProfiles(file, lines);
        }
        SyncedDialoguesData.Update();
    }
}


// =============================================================================
// Dialogues_UI - Runtime dialogue panel
// =============================================================================
// KEY INSIGHTS:
//   - Uses prefab "NPCDialogueUI" from AssetStorage.asset (asset bundle)
//   - Option prefab is "Dialogue_Option"
//   - Hierarchy: UI -> GO -> NPC_Name, Dialogue_Text, BACKGROUND, and options
//   - Dialogue text wrapped in quotes: $"\"{Localization.instance.Localize(dialogue.Text)}\""
//   - Options show: Indexer/Text (number), Text (label), Text/Icon (icon), line (color bar)
//   - Failed conditions: destroy Button, set line + indexer bg to red, append reason
//   - Hotbar keys 1-9 and 0 mapped to options
//   - Fade in/out via CanvasGroup coroutine
//   - %playername% replacement in text
//   - Patches TextInput.IsVisible to block input while dialogue is open
//   - Patches Player.OnDeath to hide dialogue
public static class Dialogues_UI
{
    private static GameObject UI;
    private static GameObject Dialogue_Element;
    private static readonly List<GameObject> Elements = new List<GameObject>();
    private static Text NPC_Name;
    private static Text Dialogue_Text;
    private static Image BG_Image;
    private static Transform Content;
    private static CanvasGroup CanvasAlpha;
    private static readonly Dictionary<int, Action> HotbarActions = new Dictionary<int, Action>();

    private static readonly KeyCode[] hotbarKeys = new KeyCode[]
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
    };

    public static Dictionary<string, Func<string>> _replacements = new Dictionary<string, Func<string>>
    {
        { "%playername%", () => Player.m_localPlayer?.GetPlayerName() ?? "Player" }
    };

    public static bool IsVisible() => UI && UI.activeSelf;

    public static void Init()
    {
        UI = UnityEngine.Object.Instantiate(AssetStorage.asset.LoadAsset<GameObject>("NPCDialogueUI"));
        CanvasAlpha = UI.GetComponent<CanvasGroup>();
        Dialogue_Element = AssetStorage.asset.LoadAsset<GameObject>("Dialogue_Option");
        UnityEngine.Object.DontDestroyOnLoad(UI);
        UI.SetActive(false);
        NPC_Name = UI.transform.Find("GO/NPC_Name").GetComponent<Text>();
        Dialogue_Text = UI.transform.Find("GO/Dialogue_Text").GetComponent<Text>();
        Content = UI.transform.Find("GO");
        BG_Image = UI.transform.Find("GO/BACKGROUND").GetComponent<Image>();
        Default();
    }

    public static void LoadDialogue(Market_NPC.NPCcomponent npc, string UID)
    {
        Default();

        Dialogues_DataTypes.Dialogue dialogue;
        if (!Dialogues_DataTypes.ClientReadyDialogues.TryGetValue(UID, out dialogue))
        {
            Hide(true);
            return;
        }

        // Execute @interaction commands first
        foreach (var interaction in dialogue.Interactions)
        {
            if (interaction.Type == RawDialogue.InteractionType.Interaction
                && interaction.CheckCondition(npc, out _))
                interaction.CallCommands(npc);
        }

        // If ALL interactions are non-Option, don't show UI
        if (dialogue.Interactions.All(i => i.Type != RawDialogue.InteractionType.Option))
            return;

        UI.SetActive(true);
        SmoothAlpha(Fade.Show, 0.5f);

        // NPC name
        string name = "";
        if (npc)
        {
            name = npc.GetClearNPCName();
            if (string.IsNullOrWhiteSpace(name))
                name = Localization.instance.Localize("$mpasn_" + npc._currentNpcType.ToString());
        }
        NPC_Name.text = name;

        // Dialogue text with quotes and replacements
        Dialogue_Text.text = $"\"{Localization.instance.Localize(dialogue.Text)}\"";
        foreach (var repl in _replacements)
            Dialogue_Text.text = Dialogue_Text.text.Replace(repl.Key, repl.Value());

        // Background image
        if (dialogue.BG_Image != null)
        {
            BG_Image.sprite = dialogue.BG_Image;
            BG_Image.gameObject.SetActive(true);
        }
        else
            BG_Image.gameObject.SetActive(false);

        // Build option elements
        int key = 0;
        foreach (var interaction in dialogue.Interactions)
        {
            var option = interaction;
            if (option.Type != RawDialogue.InteractionType.Option) continue;

            bool failed = false;
            string reason;
            if (!option.CheckCondition(npc, out reason))
            {
                if (option.AlwaysVisible) failed = true;
                else continue; // Hidden
            }

            var go = UnityEngine.Object.Instantiate(Dialogue_Element, Content);
            Elements.Add(go);

            // Text with replacements
            string text = option.Text;
            foreach (var repl in _replacements) text = text.Replace(repl.Key, repl.Value());
            go.transform.Find("Text").GetComponent<Text>().text = Localization.instance.Localize(text);

            // Number index
            go.transform.Find("Indexer/Text").GetComponent<Text>().text = (++key).ToString();

            // Icon
            if (option.Icon != null)
                go.transform.Find("Text/Icon").GetComponent<Image>().sprite = option.Icon;

            // Color bars
            go.transform.Find("line").GetComponent<Image>().color = option.Color;
            go.transform.Find("Indexer/bg").GetComponent<Image>().color = option.Color;

            if (failed)
            {
                // Disabled: destroy button, red color, append reason
                UnityEngine.Object.Destroy(go.GetComponent<Button>());
                go.transform.Find("line").GetComponent<Image>().color = Color.red;
                go.transform.Find("Indexer/bg").GetComponent<Image>().color = Color.red;
                if (!string.IsNullOrWhiteSpace(reason))
                    go.transform.Find("Text").GetComponent<Text>().text +=
                        $"\n<color=red>[</color>{reason}<color=red>]</color>".Localize();
            }
            else
            {
                HotbarActions[key] = OnClick;
                go.GetComponent<Button>().onClick.AddListener(new UnityAction(OnClick));
            }

            void OnClick()
            {
                AssetStorage.AUsrc.Play();
                if (!Player.m_localPlayer) return;

                string r;
                if (!option.CheckCondition(npc, out r))
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        MessageHud.instance.ShowMessage(MessageHud.MessageType.Center,
                            $"<color=red>[</color>{r}<color=red>]</color>");
                    return;
                }

                option.CallCommands(npc);
                string next = option.NextTransition;
                if (next != null)
                    LoadDialogue(npc, next);
                else
                    Hide(true);
            }
        }

        ResetFitters();
    }

    // Patches:
    // - TextInput.IsVisible -> returns true when dialogue is visible (blocks game input)
    // - Player.OnDeath -> hides dialogue
}

} // namespace

#endif // Never compiled
