using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresCore.Npc;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Marketplace;
using FiresRPGmaker.Modules.Dialogues;
using FiresRPGmaker.UI.Screens;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Drives the two-page "book" admin config panel (npcbook.prefab) — the structured replacement for the
    /// legacy completecompanionsui panel. Loads the prefab straight from the firesnpcsui bundle (no codegen,
    /// no uitables sidecar), owns a standalone ScreenSpaceOverlay canvas, and binds the book's native nodes
    /// by name. Left page = persistent NPC identity setup; right page = one structured config form per module
    /// tab. Reuses the *ProfileManager save/load APIs and NpcController's model/name apply paths.
    /// </summary>
    public class NpcBookController : MonoBehaviour
    {
        public static NpcBookController Instance { get; private set; }

        // Mirrors the builder's tab list / Screen_<tab> names (FiresNpcBookBuild.Tabs).
        private static readonly string[] Tabs =
        { "Dressing", "Quest", "Trader", "Info", "Dialogue", "Banker", "Gambler", "Leaderboard", "Transmog", "Marketplace", "Mail" };

        private static readonly Color TAB = new Color(0.42f, 0.30f, 0.13f, 0.42f);
        private static readonly Color TAB_ON = new Color(0.62f, 0.45f, 0.16f, 0.92f);

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private Transform _root;
        private Transform _leftPage;
        private Transform _rightPage;
        private NpcController _npc;
        private string _activeTab = "Dressing";
        private bool _built;

        public bool IsOpen => _panelGo != null && _panelGo.activeSelf;

        // ── entry / lifecycle ────────────────────────────────────────────────
        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) return;
            if (Instance == null)
            {
                var host = new GameObject("FiresNPCs_NpcBook");
                DontDestroyOnLoad(host);
                Instance = host.AddComponent<NpcBookController>();
            }
            Instance.Open(npc);
        }

        private void Open(NpcController npc)
        {
            if (!EnsureBuilt()) { Debug.LogError("[NpcBookController] build failed; cannot open book"); return; }
            _npc = npc;
            try { npc.LoadFromZDO(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] LoadFromZDO: " + ex.Message); }
            PopulateLeft();
            SelectTab(TabForType(npc.npcType));
            _panelGo.SetActive(true);
            try { GUIManager.BlockInput(true); } catch { }
            try { GUIManager.ApplyGameFontToAll(_panelGo.transform); } catch { }
        }

        public void Hide()
        {
            if (_panelGo != null) _panelGo.SetActive(false);
            _npc = null;
            try { if (!GUIManager.IsCustomPanelOpen()) GUIManager.BlockInput(false); } catch { }
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
            // Valheim hides/relocks the cursor every frame while a panel is up — force it visible.
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        }

        // ── build / load ─────────────────────────────────────────────────────
        private bool EnsureBuilt()
        {
            if (_built && _panelGo != null) return true;
            var bundle = FiresRPGmaker.uiBundle;
            if (bundle == null) { Debug.LogError("[NpcBookController] uiBundle is null — can't load npcbook.prefab"); return false; }

            GameObject prefab = null;
            foreach (var an in bundle.GetAllAssetNames())
                if (an.EndsWith("npcbook.prefab", StringComparison.OrdinalIgnoreCase)) { prefab = bundle.LoadAsset<GameObject>(an); break; }
            if (prefab == null) { Debug.LogError("[NpcBookController] npcbook.prefab not found in firesnpcsui bundle — rebuild it (Valheim/Build book + render every screen) and the bundle."); return false; }

            BuildCanvas();
            _panelGo = Instantiate(prefab, _canvasGo.transform);
            _panelGo.name = "FiresRPGmaker_NpcPanel";
            _root = _panelGo.transform;

            var rt = _panelGo.GetComponent<RectTransform>();
            if (rt != null) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; }

            _leftPage = FindRec(_root, "LeftPage");
            _rightPage = FindRec(_root, "RightPage");
            if (_leftPage == null || _rightPage == null)
            { Debug.LogError("[NpcBookController] book is missing LeftPage/RightPage — bad prefab"); return false; }

            WireTabs();
            WireButtons();
            WireToggleVisuals();
            _panelGo.SetActive(false);
            _built = true;
            Debug.Log("[NpcBookController] book loaded + wired");
            return true;
        }

        private void BuildCanvas()
        {
            _canvasGo = new GameObject("FiresNPCs_NpcBookCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 210;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void WireTabs()
        {
            foreach (var tab in Tabs)
            {
                var btn = Btn(tab + "Tab");
                if (btn == null) continue;
                string captured = tab;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => SelectTab(captured));
            }
        }

        private void WireButtons()
        {
            Hook("CloseButton", Hide);
            Hook("ApplyButton", ApplySetup);
            Hook("QuestApply", SaveQuest);
            Hook("TraderApply", SaveTrader);
            Hook("InfoApply", SaveInfo);
            Hook("BankerApply", SaveBanker);
            Hook("GamblerApply", SaveGambler);
            Hook("LeaderboardApply", SaveLeaderboard);
            Hook("TransmogApply", SaveTransmog);
            Hook("MarketplaceApply", SaveMarketplace);
            Hook("MailApply", SaveMail);
            Hook("ApplyLook", SaveDressing);
            Hook("AddRewardButton", () => AddRewardRow(null));
            Hook("AddOfferingButton", () => AddOfferingRow(null));
            Hook("DlgSave", SaveDialogue);
            Hook("DlgNew", NewDialogue);
            Hook("DlgDelete", DeleteDialogue);
        }

        // ── tab switching ────────────────────────────────────────────────────
        private void SelectTab(string tab)
        {
            if (string.IsNullOrEmpty(tab) || Array.IndexOf(Tabs, tab) < 0) tab = "Dressing";
            _activeTab = tab;
            foreach (var t in Tabs)
            {
                var screen = FindRec(_rightPage, "Screen_" + t);
                if (screen != null) screen.gameObject.SetActive(t == tab);
                var img = FindRec(_leftPage, t + "Tab")?.GetComponent<Image>();
                if (img != null) img.color = (t == tab) ? TAB_ON : TAB;
            }
            PopulateActiveScreen();
        }

        private static string TabForType(NpcType t) => t switch
        {
            NpcType.QuestNpc => "Quest",
            NpcType.InfoNpc => "Info",
            NpcType.DialogueNpc => "Dialogue",
            NpcType.Trader => "Trader",
            NpcType.Banker => "Banker",
            NpcType.Gambler => "Gambler",
            NpcType.Marketplace => "Marketplace",
            NpcType.Achievement => "Leaderboard",
            NpcType.Leaderboard => "Leaderboard",
            NpcType.Mail => "Mail",
            NpcType.Transmog => "Transmog",
            _ => "Dressing",
        };

        // ── left page (setup) ────────────────────────────────────────────────
        private void PopulateLeft()
        {
            if (_npc == null) return;
            SetInput(_leftPage, "Container_NameOverrideInput",
                string.IsNullOrEmpty(_npc.displayNameOverride) ? _npc.npcProfileName : _npc.displayNameOverride);
            SetInput(_leftPage, "Container_ProfileInput", _npc.npcProfileName);
            SetInput(_leftPage, "Container_ModelOverrideInput", _npc.modelOverride);
            var idle = Tog(_leftPage, "AllowIdleWander_chk"); if (idle != null) idle.isOn = _npc.allowIdleWandering;
            var head = FindRec(_leftPage, "Header")?.GetComponent<TMP_Text>();
            if (head != null) head.text = "Configure NPC: " + (string.IsNullOrEmpty(_npc.displayNameOverride) ? _npc.npcProfileName : _npc.displayNameOverride);
        }

        private void ApplySetup()
        {
            if (_npc == null) return;
            string disp = GetInput(_leftPage, "Container_NameOverrideInput")?.Trim();
            if (!string.IsNullOrEmpty(disp))
            {
                _npc.displayNameOverride = disp;
                try { FiresCore.Bridge.NpcCompanionBridge.RaiseSetDisplayName(_npc.gameObject, disp); } catch { }
            }
            string prof = GetInput(_leftPage, "Container_ProfileInput")?.Trim();
            if (!string.IsNullOrEmpty(prof)) _npc.npcProfileName = prof;

            string newModel = GetInput(_leftPage, "Container_ModelOverrideInput")?.Trim() ?? "";
            string oldModel = _npc.modelOverride ?? "";
            bool wantsReset = string.IsNullOrEmpty(newModel) || newModel.Equals("none", StringComparison.OrdinalIgnoreCase);
            if (wantsReset)
            {
                _npc.modelOverride = "";
                try { _npc.ResetModelToDefault(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] ResetModel: " + ex.Message); }
                SetInput(_leftPage, "Container_ModelOverrideInput", "");
            }
            else if (newModel != oldModel)
            {
                _npc.modelOverride = newModel;
                try { _npc.ApplyModelOverride(newModel); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] ApplyModel: " + ex.Message); }
            }

            var idle = Tog(_leftPage, "AllowIdleWander_chk"); if (idle != null) _npc.allowIdleWandering = idle.isOn;
            _npc.SaveToZDO();
            PopulateLeft();
            Msg("NPC setup saved");
        }

        // ── per-screen populate dispatch ─────────────────────────────────────
        private void PopulateActiveScreen()
        {
            if (_npc == null) return;
            switch (_activeTab)
            {
                case "Trader": PopulateTrader(); break;
                case "Banker": PopulateBanker(); break;
                case "Gambler": PopulateGambler(); break;
                case "Transmog": PopulateTransmog(); break;
                case "Quest": PopulateQuest(); break;
                case "Dialogue": PopulateDialogue(); break;
                case "Info": PopulateInfo(); break;
                case "Leaderboard": PopulateLeaderboard(); break;
                case "Marketplace": PopulateMarketplace(); break;
                case "Mail": PopulateMail(); break;
                case "Dressing": PopulateDressing(); break;
            }
        }

        private Transform Screen(string tab) => FindRec(_rightPage, "Screen_" + tab);

        // ════════════════════════════ TRADER ════════════════════════════════
        private void PopulateTrader()
        {
            string name = _npc.traderProfile;
            SetScreenInput("Trader", "Trader Profile_C", name);
            var p = string.IsNullOrEmpty(name) ? null : TraderProfileManager.GetProfile(name);
            SetScreenInput("Trader", "Trades", p != null ? TraderProfileManager.SerializeTrades(p) : "");
            SetScreenInput("Trader", "Stock_C", p != null && p.DefaultStock != -1 ? p.DefaultStock.ToString() : "");
            SetScreenInput("Trader", "Restock (h)_C", p != null && p.DefaultRestockSeconds != 0 ? (p.DefaultRestockSeconds / 3600).ToString() : "");
        }

        private void SaveTrader()
        {
            string name = GetScreenInput("Trader", "Trader Profile_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a trader profile name"); return; }
            string trades = (GetScreenInput("Trader", "Trades") ?? "").Trim();
            int stock = ParseInt(GetScreenInput("Trader", "Stock_C"), -1);
            int restockH = ParseInt(GetScreenInput("Trader", "Restock (h)_C"), 0);
            int restockS = restockH * 3600;
            // Direct header write so the form's Stock/Restock take effect (SaveProfile re-uses the existing
            // profile header instead). Same folder + reload the manager uses.
            string header = name;
            if (stock != -1) header += $",Stock={stock}";
            if (restockS != 0) header += $",Restock={restockS}";
            var folder = Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Traders");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, SafeName(name) + ".cfg"), $"[{header}]\n{trades}\n");
                TraderProfileManager.Reset(); TraderProfileManager.Initialize();
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveTrader: " + ex.Message); Msg("Save failed (see log)"); return; }
            _npc.traderProfile = name; _npc.npcType = NpcType.Trader; _npc.SaveToZDO();
            Msg("Saved trader profile '" + name + "'");
        }

        // ════════════════════════════ BANKER ════════════════════════════════
        private void PopulateBanker()
        {
            string name = _npc.bankerProfile;
            SetScreenInput("Banker", "Banker Profile_C", name);
            var p = string.IsNullOrEmpty(name) ? null : BankerProfileManager.GetProfile(name);
            SetScreenInput("Banker", "Interest %/min_C", p != null ? p.InterestPerMinute.ToString() : "");
            var cur = p != null && p.CurrencyLimits.Count > 0 ? p.CurrencyLimits.First() : new KeyValuePair<string, int>("Coins", 1000000);
            SetScreenInput("Banker", "Accepted_C", cur.Key);
            SetScreenInput("Banker", "Max Deposit_C", cur.Value.ToString());
        }

        private void SaveBanker()
        {
            string name = GetScreenInput("Banker", "Banker Profile_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a banker profile name"); return; }
            string interest = GetScreenInput("Banker", "Interest %/min_C")?.Trim();
            string cur = GetScreenInput("Banker", "Accepted_C")?.Trim();
            string max = GetScreenInput("Banker", "Max Deposit_C")?.Trim();
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(interest)) sb.AppendLine("interest=" + interest);
            if (!string.IsNullOrEmpty(cur)) sb.AppendLine(cur + ":" + (string.IsNullOrEmpty(max) ? "1000000" : max));
            BankerProfileManager.SaveProfile(name, sb.ToString().TrimEnd());
            _npc.bankerProfile = name; _npc.npcType = NpcType.Banker; _npc.SaveToZDO();
            Msg("Saved banker profile '" + name + "'");
        }

        // ════════════════════════════ GAMBLER ═══════════════════════════════
        private void PopulateGambler()
        {
            string name = _npc.gamblerProfile;
            SetScreenInput("Gambler", "Profile Name_C", name);
            var p = string.IsNullOrEmpty(name) ? null : GamblerProfileManager.GetProfile(name);
            SetScreenInput("Gambler", "Max Rolls_C", p != null ? p.MaxRolls.ToString() : "1");
            SetToggle("Gambler", "Cards_chk", p == null || p.Games.Contains(GamblerGames.Cards));
            SetToggle("Gambler", "Bones_chk", p != null && p.Games.Contains(GamblerGames.Bones));
            SetToggle("Gambler", "Runes_chk", p != null && p.Games.Contains(GamblerGames.Runes));
            SetToggle("Gambler", "Wheel_chk", p != null && p.Games.Contains(GamblerGames.Wheel));
            SetScreenInput("Gambler", "Cost Item_C", p != null ? p.RequiredItem.Prefab : "Coins");
            SetScreenInput("Gambler", "Amount_C", p != null ? p.RequiredItem.Min.ToString() : "100");
            ClearRows("Gambler", "RewardPool");
            if (p != null) foreach (var r in p.Rewards) AddRewardRow(r);
            else RestackRows("Gambler", "RewardPool");
        }

        private void SaveGambler()
        {
            string name = GetScreenInput("Gambler", "Profile Name_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a gambler profile name"); return; }
            int maxRolls = ParseInt(GetScreenInput("Gambler", "Max Rolls_C"), 1);
            var games = new List<string>();
            if (GetToggle("Gambler", "Cards_chk")) games.Add(GamblerGames.Cards);
            if (GetToggle("Gambler", "Bones_chk")) games.Add(GamblerGames.Bones);
            if (GetToggle("Gambler", "Runes_chk")) games.Add(GamblerGames.Runes);
            if (GetToggle("Gambler", "Wheel_chk")) games.Add(GamblerGames.Wheel);

            int cost = ParseInt(GetScreenInput("Gambler", "Amount_C"), 1);
            var profile = new GamblerProfile
            {
                Name = name,
                MaxRolls = maxRolls,
                RequiredItem = new GamblerItemEntry { Prefab = GetScreenInput("Gambler", "Cost Item_C")?.Trim() ?? "Coins", Min = cost, Max = cost },
                Rewards = new List<GamblerItemEntry>(),
            };
            foreach (var row in LiveRows("Gambler", "RewardPool"))
            {
                string item = RowCell(row, "ItemCell")?.Trim();
                if (string.IsNullOrEmpty(item)) continue;
                int mn = ParseInt(RowCell(row, "MinCell"), 1);
                int mx = ParseInt(RowCell(row, "MaxCell"), mn);
                profile.Rewards.Add(new GamblerItemEntry { Prefab = item, Min = mn, Max = mx < mn ? mn : mx });
            }
            string body = GamblerProfileManager.Serialize(profile);
            GamblerProfileManager.SaveProfile(name, maxRolls, body, games);
            _npc.gamblerProfile = name; _npc.npcType = NpcType.Gambler; _npc.SaveToZDO();
            Msg("Saved gambler profile '" + name + "'");
        }

        // ════════════════════════════ TRANSMOG ══════════════════════════════
        private void PopulateTransmog()
        {
            string name = _npc.transmogProfile;
            SetScreenInput("Transmog", "TmProfile", name);
            ClearRows("Transmog", "OfferingPool");
            var opts = string.IsNullOrEmpty(name) ? null : TransmogProfileManager.GetProfileOptions(name);
            if (opts != null) foreach (var o in opts) AddOfferingRow(o);
            else RestackRows("Transmog", "OfferingPool");
        }

        private void SaveTransmog()
        {
            string name = GetScreenInput("Transmog", "TmProfile")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a transmog profile name"); return; }
            var sb = new StringBuilder();
            foreach (var row in LiveRows("Transmog", "OfferingPool"))
            {
                string app = RowCell(row, "AppearanceCell")?.Trim();
                if (string.IsNullOrEmpty(app)) continue;
                string slot = RowCell(row, "SlotCell")?.Trim(); if (string.IsNullOrEmpty(slot)) slot = "Helmet";
                string cur = RowCell(row, "CurrencyCell")?.Trim(); if (string.IsNullOrEmpty(cur)) cur = "Coins";
                int price = ParseInt(RowCell(row, "PriceCell"), 0);
                sb.AppendLine($"{slot}|{app}|{cur}|{price}");
            }
            TransmogProfileManager.SaveProfile(name, sb.ToString().TrimEnd());
            _npc.transmogProfile = name; _npc.npcType = NpcType.Transmog; _npc.SaveToZDO();
            Msg("Saved transmog profile '" + name + "'");
        }

        // ════════════════════════════ QUEST ═════════════════════════════════
        // The quest DB holds many quests under one profile; v1 authors/overwrites a single quest by its
        // [questId] (editing an existing one in place by typing its id). A profile→quest picker is a later pass.
        private void PopulateQuest()
        {
            SetScreenInput("Quest", "Save To Profile_C", _npc.questProfile);
        }

        private void SaveQuest()
        {
            string questId = GetScreenInput("Quest", "Profile ID_C")?.Trim();
            if (string.IsNullOrEmpty(questId)) { Msg("Enter a Quest ID"); return; }
            string name = GetScreenInput("Quest", "Quest Name_C")?.Trim() ?? "";
            string type = GetScreenInput("Quest", "Type_C")?.Trim();
            if (string.IsNullOrEmpty(type)) type = "Kill";
            if (type.Equals("Go To", StringComparison.OrdinalIgnoreCase)) type = "Move";
            string autoStr = GetScreenInput("Quest", "Auto-Complete_C")?.Trim() ?? "";
            bool autocomplete = autoStr.Equals("true", StringComparison.OrdinalIgnoreCase)
                             || autoStr.Equals("on", StringComparison.OrdinalIgnoreCase)
                             || autoStr.Equals("yes", StringComparison.OrdinalIgnoreCase);
            string desc = GetScreenInput("Quest", "Desc")?.Trim() ?? "";
            string target = GetScreenInput("Quest", "Target_C")?.Trim() ?? "";
            string amount = GetScreenInput("Quest", "Amount_C")?.Trim() ?? "1";
            string minLvl = GetScreenInput("Quest", "Min Lvl_C")?.Trim() ?? "";
            string cooldown = GetScreenInput("Quest", "Cooldown Days_C")?.Trim() ?? "0";
            string timeLimit = GetScreenInput("Quest", "Limit Mins_C")?.Trim() ?? "0";
            string rewards = GetScreenInput("Quest", "Rewards_C")?.Trim() ?? "";
            string required = GetScreenInput("Quest", "Required_C")?.Trim() ?? "";
            string onStart = GetScreenInput("Quest", "On Start Event_C")?.Trim() ?? "";
            string onComplete = GetScreenInput("Quest", "On Complete_C")?.Trim() ?? "";
            string profileName = GetScreenInput("Quest", "Or New_C")?.Trim();
            if (string.IsNullOrEmpty(profileName)) profileName = GetScreenInput("Quest", "Save To Profile_C")?.Trim();
            if (string.IsNullOrEmpty(profileName)) profileName = questId;

            var sb = new StringBuilder();
            sb.AppendLine($"[{questId}]");
            sb.AppendLine($"QuestType: {type}");
            sb.AppendLine($"QuestName: {name}");
            sb.AppendLine($"Description: {desc}");
            string tline = $"{target},{amount}";
            if (!string.IsNullOrEmpty(minLvl) && minLvl != "0") tline += $",{minLvl}";
            sb.AppendLine($"QuestTarget: {tline}");
            if (!string.IsNullOrEmpty(rewards)) sb.AppendLine($"QuestReward: {rewards}");
            sb.AppendLine($"CooldownDays: {cooldown}");
            sb.AppendLine($"TimeLimitMinutes: {timeLimit}");
            if (!string.IsNullOrEmpty(required)) sb.AppendLine($"Requirement: {required}");
            sb.AppendLine($"Autocomplete: {(autocomplete ? "true" : "false")}");
            if (!string.IsNullOrEmpty(onStart)) sb.AppendLine($"OnAcceptQuest: {onStart}");
            if (!string.IsNullOrEmpty(onComplete)) sb.AppendLine($"OnCompleteQuest: {onComplete}");

            if (!WriteBlockInPlace(Path.Combine(Paths.ConfigPath, "FiresRPGmaker", "Quests", "QuestDatabase.cfg"),
                                   questId, sb.ToString())) { Msg("Quest save failed (see log)"); return; }
            try
            {
                QuestProfileManager.EnsureProfileExists(profileName);
                QuestProfileManager.AddQuestToProfile(profileName, questId);
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] quest profile: " + ex.Message); }
            _npc.questProfile = profileName; _npc.npcType = NpcType.QuestNpc; _npc.SaveToZDO();
            try { global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase(); } catch { }
            Msg("Saved quest '" + questId + "'");
        }

        // ════════════════════════════ DIALOGUE ══════════════════════════════
        private void PopulateDialogue()
        {
            string uid = _npc.dialogueProfile;
            try { DialogueCodex.Init(); } catch { }
            RawDialogue d = null;
            try { d = string.IsNullOrEmpty(uid) ? null : DialogueCodex.Get(uid); } catch { }
            SetScreenInput("Dialogue", "UID_C", uid);
            SetScreenInput("Dialogue", "BG Image_C", d?.BG_Image ?? "");
            SetScreenInput("Dialogue", "NpcText", d?.Text ?? "");
            string opts = "";
            if (d?.Interactions != null)
            {
                var sb = new StringBuilder();
                foreach (var o in d.Interactions) sb.AppendLine((o.Text ?? "") + " > " + (o.NextUID ?? ""));
                opts = sb.ToString().TrimEnd();
            }
            SetScreenInput("Dialogue", "Options", opts);
        }

        private void SaveDialogue()
        {
            string uid = GetScreenInput("Dialogue", "UID_C")?.Trim();
            if (string.IsNullOrEmpty(uid)) { Msg("Enter a dialogue UID"); return; }
            uid = uid.ToLowerInvariant();
            string bg = GetScreenInput("Dialogue", "BG Image_C")?.Trim() ?? "";
            string npcText = GetScreenInput("Dialogue", "NpcText") ?? "";
            string optionsRaw = GetScreenInput("Dialogue", "Options") ?? "";
            var options = new List<RawPlayerOption>();
            foreach (var raw in optionsRaw.Replace("\r", "").Split('\n'))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                int gt = t.IndexOf('>');
                string left = gt >= 0 ? t.Substring(0, gt).Trim() : t;
                string right = gt >= 0 ? t.Substring(gt + 1).Replace(" ", "") : "";
                options.Add(new RawPlayerOption { Type = InteractionType.Option, Text = left, NextUID = right });
            }
            var dialogue = new RawDialogue { UID = uid, Text = npcText, BG_Image = bg, Interactions = options.ToArray() };
            try
            {
                string cfg = DialogueSyncRPC.SerializeDialogueToCfg(dialogue);
                DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(uid) + ".cfg", cfg);
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveDialogue: " + ex.Message); Msg("Save failed"); return; }
            _npc.dialogueProfile = uid; _npc.npcType = NpcType.DialogueNpc; _npc.SaveToZDO();
            Msg("Saved dialogue '" + uid + "'");
        }

        private void NewDialogue()
        {
            SetScreenInput("Dialogue", "UID_C", "");
            SetScreenInput("Dialogue", "BG Image_C", "");
            SetScreenInput("Dialogue", "NpcText", "");
            SetScreenInput("Dialogue", "Options", "");
        }

        private void DeleteDialogue()
        {
            string uid = GetScreenInput("Dialogue", "UID_C")?.Trim();
            if (string.IsNullOrEmpty(uid)) return;
            try { DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(uid.ToLowerInvariant()) + ".cfg", null); } catch { }
            NewDialogue();
            Msg("Deleted dialogue '" + uid + "'");
        }

        // ════════════════════════════ INFO ══════════════════════════════════
        private void PopulateInfo()
        {
            string profileName = string.IsNullOrEmpty(_npc.infoProfile) ? _npc.npcProfileName : _npc.infoProfile;
            InfoNpcScreenController.InfoProfile p = null;
            try { p = InfoNpcScreenController.GetProfileForName(profileName); } catch { }
            SetScreenInput("Info", "Header Text_C", p?.HeaderText ?? "");
            SetScreenInput("Info", "Image (PNG)_C", p?.PngPath ?? "");
            SetScreenInput("Info", "InfoText", p?.InfoText ?? "");
        }

        private void SaveInfo()
        {
            string profileName = string.IsNullOrEmpty(_npc.infoProfile) ? SafeName(_npc.npcProfileName) : _npc.infoProfile;
            var profile = new InfoNpcScreenController.InfoProfile
            {
                HeaderText = GetScreenInput("Info", "Header Text_C")?.Trim() ?? "",
                PngPath = GetScreenInput("Info", "Image (PNG)_C")?.Trim() ?? "",
                InfoText = GetScreenInput("Info", "InfoText")?.Trim() ?? "",
            };
            try { InfoNpcScreenController.SaveInfoProfile(profileName, profile); }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveInfo: " + ex.Message); Msg("Save failed"); return; }
            _npc.infoProfile = profileName; _npc.npcType = NpcType.InfoNpc; _npc.SaveToZDO();
            Msg("Saved info profile '" + profileName + "'");
        }

        // ════════════════════════════ LEADERBOARD ═══════════════════════════
        // Authors a single achievement (6-line KG block) into the profile cfg; overwrite-by-Name else append.
        private void PopulateLeaderboard()
        {
            string profile = string.IsNullOrEmpty(_npc.leaderboardProfile) ? "server" : _npc.leaderboardProfile;
            SetScreenInput("Leaderboard", "AchProfile", profile);
        }

        private void SaveLeaderboard()
        {
            string profile = GetScreenInput("Leaderboard", "AchProfile")?.Trim();
            if (string.IsNullOrEmpty(profile)) profile = string.IsNullOrEmpty(_npc.leaderboardProfile) ? "server" : _npc.leaderboardProfile;
            profile = profile.ToLowerInvariant();
            string trigger = GetScreenInput("Leaderboard", "Trigger Type_C")?.Trim() ?? "";
            string name = GetScreenInput("Leaderboard", "AchName")?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { Msg("Enter an achievement name"); return; }
            string desc = GetScreenInput("Leaderboard", "AchDesc")?.Trim() ?? "";
            string prefab = GetScreenInput("Leaderboard", "Target Prefab_C")?.Trim() ?? "";
            string amount = GetScreenInput("Leaderboard", "Amount_C")?.Trim() ?? "0";
            string r = GetScreenInput("Leaderboard", "R_C")?.Trim() ?? "190";
            string g = GetScreenInput("Leaderboard", "G_C")?.Trim() ?? "190";
            string b = GetScreenInput("Leaderboard", "B_C")?.Trim() ?? "190";
            string score = GetScreenInput("Leaderboard", "Score_C")?.Trim() ?? "0";
            string target = string.IsNullOrEmpty(prefab) ? amount : (prefab + ", " + amount);
            string block = trigger + "\n" + name + "\n" + desc + "\n" + target + "\n" + r + ", " + g + ", " + b + "\n" + score;

            string folder = AchievementProfileManager.GetProfileFolder();
            if (!WriteAchievementBlock(Path.Combine(folder, profile + ".cfg"), profile, name, block))
            { Msg("Achievement save failed (see log)"); return; }
            try { AchievementProfileManager.ReloadAll(); } catch { }
            _npc.leaderboardProfile = profile; _npc.npcType = NpcType.Leaderboard; _npc.SaveToZDO();
            Msg("Saved achievement '" + name + "' to " + profile);
        }

        // ════════════════════════════ MAIL ══════════════════════════════════
        // v1: appends a mail row to the recipient profile's .cfg + writes the global post-tunables to
        // MarketPlace.cfg. A networked player-to-player broadcast RPC is a later pass.
        private void PopulateMail()
        {
            string profile = _npc.mailProfile;
            SetScreenInput("Mail", "Recipient_C", profile);
            try
            {
                MailProfileManager.EnsureLoaded();
                var mail = string.IsNullOrEmpty(profile) ? null : MailProfileManager.GetMail(profile);
                var first = (mail != null && mail.Count > 0) ? mail[0] : null;
                if (first != null)
                {
                    SetScreenInput("Mail", "Sender_C", first.SenderName);
                    SetScreenInput("Mail", "MailTopic", first.Subject);
                    SetScreenInput("Mail", "MailBody", first.Body);
                }
            }
            catch { }
            SetScreenInput("Mail", "Post Cost_C", ReadCfgValue(MarketplaceCfgPath, "MailPostRecipe"));
            SetScreenInput("Mail", "Wait (s)_C", ReadCfgValue(MarketplaceCfgPath, "MailPostWaitTime"));
        }

        private void SaveMail()
        {
            string sender = GetScreenInput("Mail", "Sender_C")?.Trim() ?? "";
            string recipient = GetScreenInput("Mail", "Recipient_C")?.Trim();
            if (string.IsNullOrEmpty(recipient)) recipient = _npc.mailProfile;
            if (string.IsNullOrEmpty(recipient)) { Msg("Enter a recipient/profile"); return; }
            string topic = GetScreenInput("Mail", "MailTopic")?.Trim() ?? "";
            string body = GetScreenInput("Mail", "MailBody") ?? "";
            string attach = GetScreenInput("Mail", "Attachment_C")?.Trim() ?? "";
            string row = sender + "|just now|" + topic + "|" + body + (string.IsNullOrEmpty(attach) ? "" : "|" + attach);
            try
            {
                MailProfileManager.EnsureLoaded();
                string existing = MailProfileManager.Serialize(MailProfileManager.GetMail(recipient));
                string merged = string.IsNullOrEmpty(existing) ? row : existing + Environment.NewLine + row;
                MailProfileManager.SaveProfile(recipient, merged);
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveMail: " + ex.Message); }
            WriteCfgValue(MarketplaceCfgPath, "Main", "MailPostRecipe", GetScreenInput("Mail", "Post Cost_C")?.Trim());
            WriteCfgValue(MarketplaceCfgPath, "Main", "MailPostWaitTime", GetScreenInput("Mail", "Wait (s)_C")?.Trim());
            _npc.mailProfile = recipient; _npc.npcType = NpcType.Mail; _npc.SaveToZDO();
            Msg("Saved mail to '" + recipient + "'");
        }

        // ════════════════════════════ MARKETPLACE ═══════════════════════════
        // Server-wide MarketPlace.cfg [Main] (no per-NPC profile); the live Marketplace plugin's file
        // watcher re-syncs on Save().
        private void PopulateMarketplace()
        {
            try
            {
                var cfg = new ConfigFile(MarketplaceCfgPath, false) { SaveOnConfigSet = false };
                SetScreenInput("Marketplace", "Item Slot Limit_C", cfg.Bind("Main", "ItemMarketLimit", 15, "").Value.ToString());
                SetScreenInput("Marketplace", "Market Tax %_C", cfg.Bind("Main", "MarketTaxes", 0, "").Value.ToString());
                SetScreenInput("Marketplace", "VIP Tax %_C", cfg.Bind("Main", "VIPplayersTaxes", 0, "").Value.ToString());
                SetScreenInput("Marketplace", "Currencies_C", cfg.Bind("Main", "PossibleCurrencies", "Coins", "").Value);
                SetScreenInput("Marketplace", "VipList", cfg.Bind("Main", "VIPplayersList", "User IDs", "").Value);
                SetScreenInput("Marketplace", "BlockList", cfg.Bind("Main", "BlockedPlayers", "User IDs", "").Value);
                SetScreenInput("Marketplace", "BlockPrefabs", cfg.Bind("Main", "MarketSellBlockedPrefabs", "Coins, SwordCheat", "").Value);
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] PopulateMarketplace: " + ex.Message); }
        }

        private void SaveMarketplace()
        {
            try
            {
                var cfg = new ConfigFile(MarketplaceCfgPath, false) { SaveOnConfigSet = false };
                var eLimit = cfg.Bind("Main", "ItemMarketLimit", 15, "Limit amount of slots player can sell in marketpalce");
                var eTax = cfg.Bind("Main", "MarketTaxes", 0, "Market Taxes From Each Sell");
                var eVipTax = cfg.Bind("Main", "VIPplayersTaxes", 0, "VIP Player Market Taxes");
                var eCurr = cfg.Bind("Main", "PossibleCurrencies", "Coins", "Possible Currencies");
                var eVip = cfg.Bind("Main", "VIPplayersList", "User IDs", "Marketplace VIP Players List");
                var eBlock = cfg.Bind("Main", "BlockedPlayers", "User IDs", "Marketplace Blocked Players");
                var ePref = cfg.Bind("Main", "MarketSellBlockedPrefabs", "Coins, SwordCheat", "Marketplace Blocked Prefabs For Selling");
                eLimit.Value = Mathf.Max(0, ParseInt(GetScreenInput("Marketplace", "Item Slot Limit_C"), eLimit.Value));
                eTax.Value = Mathf.Clamp(ParseInt(GetScreenInput("Marketplace", "Market Tax %_C"), eTax.Value), 0, 100);
                eVipTax.Value = Mathf.Clamp(ParseInt(GetScreenInput("Marketplace", "VIP Tax %_C"), eVipTax.Value), 0, 100);
                eCurr.Value = JoinList(GetScreenInput("Marketplace", "Currencies_C"));
                eVip.Value = JoinList(GetScreenInput("Marketplace", "VipList"));
                eBlock.Value = JoinList(GetScreenInput("Marketplace", "BlockList"));
                ePref.Value = JoinList(GetScreenInput("Marketplace", "BlockPrefabs"));
                cfg.Save();
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveMarketplace: " + ex.Message); Msg("Save failed"); return; }
            _npc.npcType = NpcType.Marketplace; _npc.SaveToZDO();
            Msg("Saved marketplace settings");
        }

        // ── cfg write helpers ────────────────────────────────────────────────
        private static string MarketplaceCfgPath => Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");

        // Overwrite-in-place an INI-style [key] block (header line + lines until the next "[...]"), else append.
        private bool WriteBlockInPlace(string filePath, string blockKey, string newBlock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                var outLines = new List<string>();
                bool replaced = false;
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    int i = 0;
                    while (i < lines.Length)
                    {
                        string t = lines[i].Trim();
                        bool isHeader = t.StartsWith("[") && t.EndsWith("]");
                        string hdr = isHeader ? t.Substring(1, t.Length - 2).Split('=')[0].Trim() : null;
                        if (isHeader && string.Equals(hdr, blockKey, StringComparison.OrdinalIgnoreCase))
                        {
                            i++;
                            while (i < lines.Length)
                            {
                                string n = lines[i].Trim();
                                if (n.StartsWith("[") && n.EndsWith("]")) break;
                                i++;
                            }
                            outLines.AddRange(SplitLines(newBlock));
                            replaced = true;
                            continue;
                        }
                        outLines.Add(lines[i]);
                        i++;
                    }
                }
                if (!replaced)
                {
                    if (outLines.Count > 0 && !string.IsNullOrWhiteSpace(outLines[outLines.Count - 1])) outLines.Add("");
                    outLines.AddRange(SplitLines(newBlock));
                }
                File.WriteAllText(filePath, string.Join(Environment.NewLine, outLines) + Environment.NewLine);
                return true;
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] WriteBlockInPlace: " + ex.Message); return false; }
        }

        // Writes a 6-line achievement block under [profile]; overwrites the block whose Name (line 1) matches, else appends.
        private bool WriteAchievementBlock(string filePath, string profile, string name, string block)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, "[" + profile + "]" + Environment.NewLine + Environment.NewLine + block + Environment.NewLine);
                    return true;
                }
                var raw = new List<string>(File.ReadAllLines(filePath));
                // find the 6-line block whose 2nd line == name and replace those 6 lines
                for (int i = 0; i < raw.Count; i++)
                {
                    string t = raw[i].Trim();
                    if (t.Length == 0 || t.StartsWith("[")) continue;
                    if (i + 1 < raw.Count && raw[i + 1].Trim() == name)
                    {
                        int end = Math.Min(i + 6, raw.Count);
                        raw.RemoveRange(i, end - i);
                        raw.InsertRange(i, SplitLines(block));
                        File.WriteAllText(filePath, string.Join(Environment.NewLine, raw) + Environment.NewLine);
                        return true;
                    }
                }
                // append (ensure a trailing blank line, then the block)
                string existing = File.ReadAllText(filePath).TrimEnd();
                File.WriteAllText(filePath, existing + Environment.NewLine + Environment.NewLine + block + Environment.NewLine);
                return true;
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] WriteAchievementBlock: " + ex.Message); return false; }
        }

        private static string ReadCfgValue(string cfgPath, string key)
        {
            try
            {
                if (!File.Exists(cfgPath)) return "";
                foreach (var raw in File.ReadAllLines(cfgPath))
                {
                    var line = raw.Trim();
                    if (line.StartsWith("#") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return "";
        }

        private static void WriteCfgValue(string cfgPath, string section, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try
            {
                var cfg = new ConfigFile(cfgPath, false) { SaveOnConfigSet = false };
                cfg.Bind(section, key, value, "").Value = value;
                cfg.Save();
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] WriteCfgValue " + key + ": " + ex.Message); }
        }

        private static string[] SplitLines(string s) => s.Replace("\r", "").TrimEnd('\n').Split('\n');
        private static string JoinList(string raw)
            => string.Join(", ", (raw ?? "").Replace("\r", "").Split('\n', ',').Select(x => x.Trim()).Where(x => x.Length > 0));
        private static string SanitizeFileName(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length > 0 ? sb.ToString() : "dialogue";
        }

        // Dressing equipment slots = drag-from-inventory item pickers + fashion JSON — a large interaction
        // surface handled by the existing dressing system; v1 leaves the screen as a visual reference and
        // persists nothing beyond the shared NPC setup. Apply Look just re-saves the ZDO.
        private void PopulateDressing() { }
        private void SaveDressing() { if (_npc != null) _npc.SaveToZDO(); Msg("Appearance saved"); }

        // ── dynamic list rows (gambler reward pool, transmog offerings) ──────
        private void AddRewardRow(GamblerItemEntry data)
        {
            var pool = FindRec(Screen("Gambler"), "RewardPool");
            var tmpl = FindRec(pool, "RewardRowTemplate")?.gameObject;
            if (pool == null || tmpl == null) return;
            var row = Instantiate(tmpl, pool);
            row.name = "Row";
            row.SetActive(true);
            if (data != null)
            {
                SetRowCell(row.transform, "ItemCell", data.Prefab);
                SetRowCell(row.transform, "MinCell", data.Min.ToString());
                SetRowCell(row.transform, "MaxCell", data.Max.ToString());
            }
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); RestackRows("Gambler", "RewardPool"); });
            RestackRows("Gambler", "RewardPool");
        }

        private void AddOfferingRow(TransmogOption data)
        {
            var pool = FindRec(Screen("Transmog"), "OfferingPool");
            var tmpl = FindRec(pool, "OfferingRowTemplate")?.gameObject;
            if (pool == null || tmpl == null) return;
            var row = Instantiate(tmpl, pool);
            row.name = "Row";
            row.SetActive(true);
            if (data != null)
            {
                SetRowCell(row.transform, "SlotCell", data.Slot);
                SetRowCell(row.transform, "AppearanceCell", data.Prefab);
                SetRowCell(row.transform, "CurrencyCell", data.PricePrefab);
                SetRowCell(row.transform, "PriceCell", data.PriceAmount.ToString());
            }
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); RestackRows("Transmog", "OfferingPool"); });
            RestackRows("Transmog", "OfferingPool");
        }

        private void ClearRows(string tab, string poolName)
        {
            var pool = FindRec(Screen(tab), poolName);
            if (pool == null) return;
            for (int i = pool.childCount - 1; i >= 0; i--)
            {
                var c = pool.GetChild(i);
                // Destroy is deferred to end-of-frame; deactivate now so a same-frame repopulate +
                // RestackRows (which counts only active "Row"s) doesn't stack over the doomed rows.
                if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            }
        }

        private List<Transform> LiveRows(string tab, string poolName)
        {
            var list = new List<Transform>();
            var pool = FindRec(Screen(tab), poolName);
            if (pool == null) return list;
            foreach (Transform c in pool)
                if (c.name == "Row" && c.gameObject.activeSelf) list.Add(c);
            return list;
        }

        private void RestackRows(string tab, string poolName)
        {
            var pool = FindRec(Screen(tab), poolName);
            if (pool == null) return;
            const float top = 0.86f, rowH = 0.075f, gap = 0.008f;
            int i = 0;
            foreach (Transform c in pool)
            {
                if (c.name != "Row" || !c.gameObject.activeSelf) continue;
                var rt = c as RectTransform;
                if (rt == null) continue;
                float y1 = top - i * (rowH + gap);
                rt.anchorMin = new Vector2(0f, y1 - rowH);
                rt.anchorMax = new Vector2(1f, y1);
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                i++;
            }
        }

        // ── node lookup helpers (recursive by name; resilient to nesting) ────
        private static Transform FindRec(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindRec(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private TMP_InputField InputIn(Transform scope, string container)
        {
            var t = FindRec(scope, container);
            return t != null ? t.GetComponentInChildren<TMP_InputField>(true) : null;
        }
        private void SetInput(Transform scope, string container, string val) { var i = InputIn(scope, container); if (i != null) i.text = val ?? ""; }
        private string GetInput(Transform scope, string container) { var i = InputIn(scope, container); return i != null ? i.text : null; }
        private void SetScreenInput(string tab, string container, string val) { var i = InputIn(Screen(tab), container); if (i != null) i.text = val ?? ""; }
        private string GetScreenInput(string tab, string container) { var i = InputIn(Screen(tab), container); return i != null ? i.text : null; }

        // Checkboxes have no checkmark graphic, so give each Toggle a color swap on its box for visible state.
        private static readonly Color CHK_ON = new Color(0.60f, 0.44f, 0.16f, 0.95f);
        private static readonly Color CHK_OFF = new Color(0.22f, 0.15f, 0.07f, 0.45f);
        private void WireToggleVisuals()
        {
            WireToggleVisual(Tog(_leftPage, "AllowIdleWander_chk"));
            var g = Screen("Gambler");
            foreach (var n in new[] { "Cards_chk", "Bones_chk", "Runes_chk", "Wheel_chk" })
                WireToggleVisual(Tog(g, n));
        }
        private void WireToggleVisual(Toggle t)
        {
            if (t == null) return;
            var box = t.targetGraphic as Image;
            if (box == null) return;
            UnityEngine.Events.UnityAction<bool> recolor = on => box.color = on ? CHK_ON : CHK_OFF;
            t.onValueChanged.RemoveAllListeners();
            t.onValueChanged.AddListener(recolor);
            recolor(t.isOn);
        }

        private Toggle Tog(Transform scope, string name) { var t = FindRec(scope, name); return t != null ? t.GetComponent<Toggle>() : null; }
        private void SetToggle(string tab, string name, bool on) { var t = Tog(Screen(tab), name); if (t != null) t.isOn = on; }
        private bool GetToggle(string tab, string name) { var t = Tog(Screen(tab), name); return t != null && t.isOn; }

        private Button Btn(string name) { var t = FindRec(_root, name); return t != null ? t.GetComponent<Button>() : null; }
        private void Hook(string name, UnityEngine.Events.UnityAction act)
        {
            var b = Btn(name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(act); }
        }
        private void HookRow(Transform row, string name, UnityEngine.Events.UnityAction act)
        {
            var t = FindRec(row, name);
            var b = t != null ? t.GetComponent<Button>() : null;
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(act); }
        }

        private string RowCell(Transform row, string cell)
        {
            var t = FindRec(row, cell);
            var i = t != null ? t.GetComponentInChildren<TMP_InputField>(true) : null;
            return i != null ? i.text : null;
        }
        private void SetRowCell(Transform row, string cell, string val)
        {
            var t = FindRec(row, cell);
            var i = t != null ? t.GetComponentInChildren<TMP_InputField>(true) : null;
            if (i != null) i.text = val ?? "";
        }

        private static int ParseInt(string s, int def) => int.TryParse((s ?? "").Trim(), out var v) ? v : def;
        private static string SafeName(string n) => System.Text.RegularExpressions.Regex.Replace(n ?? "", "[^a-zA-Z0-9_-]", "_");
        private void Msg(string m)
        {
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, m); } catch { }
            Debug.Log("[NpcBookController] " + m);
        }
    }
}
