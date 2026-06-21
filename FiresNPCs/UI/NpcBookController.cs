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

        private static readonly Color TAB = new Color(0.36f, 0.25f, 0.11f, 0.88f);
        private static readonly Color TAB_ON = new Color(0.62f, 0.45f, 0.16f, 0.92f);

        private GameObject _canvasGo;
        private GameObject _panelGo;
        private Transform _root;
        private Transform _leftPage;
        private Transform _rightPage;
        private NpcController _npc;
        private string _activeTab = "Dressing";
        private bool _built;

        // The Dressing tab WIRES the existing working DressingRoomScreenController onto the book's OWN parchment
        // equipment grid via WireFromCodegen — keeping the new look while reusing the proven function (opens the
        // player inventory + shows worn-gear icons + drag-to-equip). Dialogue keeps the book's new-look editor.
        private DressingRoomScreenController _dressingCtrl;
        private bool _dressingWired;
        private bool _patrolBtnAdded;
        private RectTransform _patrolBtnRect;

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
            if (_dressingCtrl != null) { try { _dressingCtrl.Hide(); } catch { } }
            _bookOptionPopup?.Close();
            if (_optionPopup != null) _optionPopup.gameObject.SetActive(false);
            _editingOptionIndex = -1;
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

            // The Dressing tab opens the player InventoryGui, which swallows the UI event system —
            // so the tab buttons' onClick never fires and the user gets stuck on Dressing until they
            // close the whole book. While Dressing is active, detect tab clicks manually and route
            // them to SelectTab (its Hide closes the inventory on the way out).
            if (_activeTab == "Dressing" && Input.GetMouseButtonDown(0))
                HandleManualTabClick();
        }

        // Manual hit-test of the left-page tab buttons, used while InventoryGui blocks the event system.
        private void HandleManualTabClick()
        {
            // While the patrol popup is up it owns all clicks; don't also process tab/button hits behind it.
            if (PatrolRoutePopup.IsOpen) return;

            Vector2 mouse = Input.mousePosition;

            // "Set Patrol Route" button (right page / dressing screen). Its onClick is blocked by the open
            // InventoryGui, so detect it manually: open the patrol popup over the dressing room.
            if (_patrolBtnRect != null && _patrolBtnRect.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_patrolBtnRect, mouse, null))
            {
                if (_npc != null) PatrolRoutePopup.Open(_npc, this);
                return;
            }

            if (_leftPage == null) return;
            foreach (var tab in Tabs)
            {
                if (tab == _activeTab) continue;
                if (!(FindRec(_leftPage, tab + "Tab") is RectTransform rt)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    SelectTab(tab);
                    return;
                }
            }
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

            _optionPopup = FindRec(_root, "OptionPopup");
            WireTabs();
            WireButtons();
            WireToggleVisuals();
            WireDialogueExtras();
            NormalizeInteractive(_panelGo.transform);
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
            // Dressing Apply/ClearAll are owned by the wired DressingRoomScreenController, not the book.
            Hook("AddRewardButton", () => AddRewardRow(null));
            Hook("AddOfferingButton", () => AddOfferingRow(null));
            Hook("DlgSave", SaveDialogue);
            Hook("DlgNew", NewDialogue);
            Hook("DlgDelete", DeleteDialogue);
            Hook("DlgPreview", PreviewDialogue);
            Hook("AddOptionButton", OnAddOption);
            Hook("DoneButton", OnPopupDone);
            Hook("CancelButton", OnPopupCancel);
            Hook("AddTransitionButton", () => { ClonePopupRow("Transition", "TransitionRowTemplate"); StackPopupRows("Transition"); });
            Hook("AddCommandButton", () => { var r = ClonePopupRow("Command", "CommandRowTemplate"); if (r != null) SetRowDropdown(r, "NameDropdown", CommandNames, ""); StackPopupRows("Command"); });
            Hook("AddRandCmdButton", () => { var r = ClonePopupRow("RandCmd", "RandCmdRowTemplate"); if (r != null) { SetRowCell(r, "WeightCell", "1"); SetRowDropdown(r, "NameDropdown", CommandNames, ""); } StackPopupRows("RandCmd"); });
            Hook("AddCondButton", () => { var r = ClonePopupRow("Cond", "CondRowTemplate"); if (r != null) { SetRowCell(r, "GroupCell", "0"); SetRowDropdown(r, "NameDropdown", ConditionNames, ""); } StackPopupRows("Cond"); });
        }

        // ── tab switching ────────────────────────────────────────────────────
        private void SelectTab(string tab)
        {
            if (string.IsNullOrEmpty(tab) || Array.IndexOf(Tabs, tab) < 0) tab = "Dressing";
            _activeTab = tab;
            bool dressing = (tab == "Dressing");
            bool dlg = (tab == "Dialogue");
            foreach (var t in Tabs)
            {
                var screen = FindRec(_rightPage, "Screen_" + t);
                if (screen != null) screen.gameObject.SetActive(t == tab);
                var img = FindRec(_leftPage, t + "Tab")?.GetComponent<Image>();
                if (img != null) img.color = (t == tab) ? TAB_ON : TAB;
            }
            // Dialogue takes both pages: hide the NPC setup, show the dialogue list on the left. Tabs stay pinned.
            FindRec(_leftPage, "SetupGroup")?.gameObject.SetActive(!dlg);
            FindRec(_leftPage, "DialogueListGroup")?.gameObject.SetActive(dlg);
            if (!dlg) { _bookOptionPopup?.Close(); if (_optionPopup != null) _optionPopup.gameObject.SetActive(false); _editingOptionIndex = -1; }
            // Dressing tab: wire the working dressing logic onto the book's own parchment grid, then load gear +
            // open the player inventory. Leaving the tab closes the inventory.
            if (dressing) WireAndShowDressing();
            else if (_dressingCtrl != null) { try { _dressingCtrl.Hide(); } catch { } }
            if (!dressing) PopulateActiveScreen();
        }

        // ── dressing: wire the working controller onto the book's parchment grid ─────────────
        private void WireAndShowDressing()
        {
            var screen = FindRec(_rightPage, "Screen_Dressing")?.gameObject;
            if (screen == null) return;
            if (!_dressingWired)
            {
                _dressingCtrl = screen.GetComponent<DressingRoomScreenController>()
                                ?? screen.AddComponent<DressingRoomScreenController>();
                try { _dressingCtrl.WireFromCodegen(screen); _dressingWired = true; }
                catch (Exception ex) { Debug.LogError("[NpcBookController] wire dressing: " + ex); return; }
            }
            try { _dressingCtrl.Show(_npc); }
            catch (Exception ex) { Debug.LogError("[NpcBookController] show dressing: " + ex); }

            // One-time "Set Patrol Route" button on the dressing screen: opens the patrol popup. The dressing
            // tab keeps the InventoryGui open, which swallows the UI event system, so the click is handled by
            // the book's manual hit-test (HandleManualTabClick), not the button's own onClick. Built with the
            // Core parchment-button helper so it matches the rest of the book.
            if (!_patrolBtnAdded)
            {
                _patrolBtnAdded = true;
                var btn = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(screen.transform, "Set Patrol Route",
                    new Vector2(0.66f, 0.935f), new Vector2(1.0f, 0.985f), null);
                _patrolBtnRect = btn.GetComponent<RectTransform>();
            }
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
            var idle = Tog(_leftPage, "AllowIdleWander_chk");
            if (idle != null) idle.isOn = FiresCore.Bridge.NpcCompanionBridge.GetIdleWander(_npc.gameObject) ?? _npc.allowIdleWandering;
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

            var idle = Tog(_leftPage, "AllowIdleWander_chk");
            if (idle != null)
            {
                _npc.allowIdleWandering = idle.isOn;
                // Saving the flag isn't enough — the companion mod constructs/toggles the actual wander
                // behavior through this bridge (no-op if the companion mod isn't present).
                try { FiresCore.Bridge.NpcCompanionBridge.RaiseApplyIdleWander(_npc.gameObject, idle.isOn); } catch { }
            }
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
                File.WriteAllText(Path.Combine(folder, SafeFileName(name) + ".cfg"), $"[{header}]\n{trades}\n");
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
            var existing = BankerProfileManager.GetProfile(name);
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(interest)) sb.AppendLine("interest=" + interest);
            if (!string.IsNullOrEmpty(cur)) sb.AppendLine(cur + ":" + (string.IsNullOrEmpty(max) ? "1000000" : max));
            // Preserve any other currencies the profile already accepts (the form edits one at a time).
            if (existing != null)
                foreach (var kv in existing.CurrencyLimits)
                    if (!kv.Key.Equals(cur, StringComparison.OrdinalIgnoreCase))
                        sb.AppendLine(kv.Key + ":" + kv.Value);
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

            // KG cfg uses "Key = Value"; the loader only routes a block through the key-value parser when its
            // first lines contain '=' (a ':'-delimited block is mis-detected as positional and corrupted).
            var sb = new StringBuilder();
            sb.AppendLine($"[{questId}]");
            sb.AppendLine($"QuestType = {type}");
            sb.AppendLine($"QuestName = {name}");
            sb.AppendLine($"Description = {desc}");
            if (!string.IsNullOrEmpty(target))
            {
                string tline = $"{target},{amount}";
                if (!string.IsNullOrEmpty(minLvl) && minLvl != "0") tline += $",{minLvl}";
                sb.AppendLine($"QuestTarget = {tline}");
            }
            if (!string.IsNullOrEmpty(rewards)) sb.AppendLine($"QuestReward = {rewards}");
            sb.AppendLine($"CooldownDays = {cooldown}");
            sb.AppendLine($"TimeLimitMinutes = {timeLimit}");
            if (!string.IsNullOrEmpty(required)) sb.AppendLine($"Requirement = {required}");
            sb.AppendLine($"Autocomplete = {(autocomplete ? "true" : "false")}");
            if (!string.IsNullOrEmpty(onStart)) sb.AppendLine($"OnAcceptQuest = {onStart}");
            if (!string.IsNullOrEmpty(onComplete)) sb.AppendLine($"OnCompleteQuest = {onComplete}");

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
        // Full editor: left-page dialogue list, right-page node editor (UID/BG/NPC text + options list), and
        // the option popup. The live model is _dlgOptions (RawPlayerOption objects loaded from the codex);
        // Pass 1 edits the single-value option fields — the Commands/Conditions/Random lists are preserved
        // untouched on each option (edited in Pass 2), so saving is never lossy.
        private readonly List<RawPlayerOption> _dlgOptions = new List<RawPlayerOption>();
        private string _dlgSelectedUid;
        private int _editingOptionIndex = -1;
        private Transform _optionPopup;
        private static readonly string[] OptionTypeNames = { "Option", "Interaction", "InRange", "OutRange" };
        // Ported verbatim from DialogueEditorScreenController (the command/condition dropdown vocabularies).
        private static readonly string[] CommandNames =
        {
            "GiveItem", "RemoveItem", "GiveQuest", "RemoveQuest", "FinishQuest",
            "Teleport", "Spawn", "SpawnRaw", "SpawnXYZ", "Damage", "Heal", "GiveBuff",
            "AddPlayerKey", "RemovePlayerKey", "AddCustomPlayerKey", "RemoveCustomPlayerKey",
            "PlayAnimation", "PlaySound", "ConsoleCommand", "AddPin", "PingMap", "OpenUI",
            "EnterPassword", "SendWebhook"
        };
        private static readonly string[] ConditionNames =
        {
            "HasItem", "NotHasItem", "HasQuest", "NotHasQuest", "QuestFinished", "QuestNotFinished",
            "QuestProgressDone", "QuestProgressNotDone", "HasPlayerKey", "NotHasPlayerKey",
            "GlobalKey", "NotGlobalKey", "HasBuff", "NotHasBuff", "SkillMore", "SkillLess",
            "CustomPlayerKeyMore", "CustomPlayerKeyLess", "HealthMore", "HealthLess",
            "MaxHealthMore", "MaxHealthLess", "StaminaMore", "StaminaLess", "MaxStaminaMore",
            "MaxStaminaLess", "EitrMore", "EitrLess", "MaxEitrMore", "MaxEitrLess"
        };

        // popup list machinery — FUC scroll pools ("<key>Pool" ScrollView inside the popup), rows cloned
        // under the scroll Content + positioned by ReflowScroll (same pattern as the screen pools).
        private Transform ClonePopupRow(string poolKey, string templateName)
        {
            var pool = FindRec(_optionPopup, poolKey + "Pool");
            var tmpl = FindRec(pool, templateName)?.gameObject;
            if (pool == null || tmpl == null) return null;
            var content = PoolContent(pool);
            var row = Instantiate(tmpl, content); row.name = "Row"; row.SetActive(true);
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); StackPopupRows(poolKey); });
            return row.transform;
        }
        private void ClearPopupRows(string poolKey)
        {
            var content = PoolContent(FindRec(_optionPopup, poolKey + "Pool"));
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i); if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
        }
        private void StackPopupRows(string poolKey)
        {
            ReflowScroll(PoolContent(FindRec(_optionPopup, poolKey + "Pool")));
        }
        private List<Transform> PopupRows(string poolKey)
        {
            var list = new List<Transform>();
            var content = PoolContent(FindRec(_optionPopup, poolKey + "Pool"));
            if (content == null) return list;
            foreach (Transform c in content) if (c.name == "Row" && c.gameObject.activeSelf) list.Add(c);
            return list;
        }
        private void SetRowDropdown(Transform row, string name, string[] options, string selected)
        {
            var dd = FindRec(row, name)?.GetComponent<TMP_Dropdown>();
            if (dd == null) return;
            dd.ClearOptions(); dd.AddOptions(new List<string>(options));
            int idx = System.Array.FindIndex(options, x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
            dd.value = idx >= 0 ? idx : 0;
        }
        private string GetRowDropdown(Transform row, string name)
        {
            var dd = FindRec(row, name)?.GetComponent<TMP_Dropdown>();
            return (dd != null && dd.value >= 0 && dd.value < dd.options.Count) ? dd.options[dd.value].text : "";
        }
        // Split "Name,params" on the FIRST comma only (NO Replace(':',',') — that legacy line corrupted colons/URLs).
        private static void SplitNameParams(string s, out string name, out string parms)
        {
            s = s ?? "";
            int c = s.IndexOf(',');
            if (c < 0) { name = s.Trim(); parms = ""; }
            else { name = s.Substring(0, c).Trim(); parms = s.Substring(c + 1); }
        }
        private static string JoinNameParams(string name, string parms)
            => string.IsNullOrEmpty(parms) ? name : name + "," + parms;

        private void PopulateDialogue()
        {
            try { DialogueCodex.Init(); } catch { }
            PopulateDialogueList();
            SelectDialogue(_npc.dialogueProfile);
        }

        private void PopulateDialogueList()
        {
            var pool = FindRec(_leftPage, "DlgListPool");
            var tmpl = FindRec(pool, "DlgListRowTemplate")?.gameObject;
            if (pool == null || tmpl == null) return;
            var content = PoolContent(pool);
            for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i); if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
            string filter = (GetInput(_leftPage, "DlgSearchField") ?? "").Trim().ToLowerInvariant();
            List<RawDialogue> all;
            try { all = DialogueCodex.GetAll().Where(d => d != null && !string.IsNullOrEmpty(d.UID)).OrderBy(d => d.UID).ToList(); }
            catch { all = new List<RawDialogue>(); }
            foreach (var d in all)
            {
                if (filter.Length > 0 && !d.UID.ToLowerInvariant().Contains(filter)) continue;
                string uid = d.UID;
                var row = Instantiate(tmpl, content); row.name = "Row"; row.SetActive(true);
                var uidLabel = FindRec(row.transform, "UidLabel")?.GetComponent<TMP_Text>(); if (uidLabel != null) uidLabel.text = uid;
                HookRow(row.transform, "SelectButton", () => SelectDialogue(uid));
                HookRow(row.transform, "DeleteRowButton", () => DeleteDialogueByUid(uid));
                var selImg = FindRec(row.transform, "SelectButton")?.GetComponent<Image>();
                if (selImg != null) selImg.color = (uid == _dlgSelectedUid) ? new Color(0.56f, 0.42f, 0.18f, 0.55f) : new Color(0.30f, 0.20f, 0.09f, 0.18f);
            }
            ReflowScroll(content);
        }

        private void SelectDialogue(string uid)
        {
            _dlgSelectedUid = string.IsNullOrEmpty(uid) ? null : uid;
            RawDialogue d = null;
            try { d = string.IsNullOrEmpty(uid) ? null : DialogueCodex.Get(uid); } catch { }
            SetScreenInput("Dialogue", "UID_C", uid ?? "");
            SetScreenInput("Dialogue", "BG Image_C", d?.BG_Image ?? "");
            SetScreenInput("Dialogue", "NpcText", d?.Text ?? "");
            _dlgOptions.Clear();
            if (d?.Interactions != null) _dlgOptions.AddRange(d.Interactions);
            RebuildOptionRows();
            SetDlgStatus(d != null ? "Loaded " + uid : "");
            PopulateDialogueList();
        }

        private void RebuildOptionRows()
        {
            var pool = FindRec(Screen("Dialogue"), "OptionsPool");
            var tmpl = FindRec(pool, "OptionRowTemplate")?.gameObject;
            if (pool == null || tmpl == null) return;
            var content = PoolContent(pool);
            for (int i = content.childCount - 1; i >= 0; i--) { var c = content.GetChild(i); if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
            for (int i = 0; i < _dlgOptions.Count; i++)
            {
                int index = i;
                var row = Instantiate(tmpl, content); row.name = "Row"; row.SetActive(true);
                var badge = FindRec(row.transform, "IndexBadge")?.GetComponent<TMP_Text>(); if (badge != null) { badge.text = (i + 1) + "."; badge.raycastTarget = false; }
                var sum = FindRec(row.transform, "SummaryLabel")?.GetComponent<TMP_Text>(); if (sum != null) { sum.text = OptionSummary(_dlgOptions[i]); sum.raycastTarget = false; }

                // The baked row is a PanelXY with NO graphic, so its only raycast targets were the two
                // small Edit/X buttons — which only registered on the top row. The working dialogue-list
                // row instead carries a full-fill SelectButton graphic that registers on EVERY row.
                // Mirror that: give the row a transparent full-fill click surface (added, since the row
                // has no Image) that opens the editor. The small X stays a child on top for remove.
                if (row.GetComponent<Image>() == null)
                {
                    var img = row.gameObject.AddComponent<Image>();
                    img.color = new Color(0f, 0f, 0f, 0f);
                    img.raycastTarget = true;
                    var rowBtn = row.gameObject.AddComponent<Button>();
                    rowBtn.targetGraphic = img;
                    rowBtn.transition = Selectable.Transition.None;
                    rowBtn.onClick.AddListener(() => OpenOptionPopup(index));
                }
                HookRow(row.transform, "EditButton", () => OpenOptionPopup(index));
                HookRow(row.transform, "RemoveButton", () => { if (index < _dlgOptions.Count) { _dlgOptions.RemoveAt(index); RebuildOptionRows(); SetDlgDirty(); } });
            }
            ReflowScroll(content);
            // TEMP [RowDiag] — confirm rows/buttons are positioned where they render (world rects).
            for (int i = 0; i < content.childCount; i++)
            {
                var c = content.GetChild(i); if (c.name != "Row") continue;
                var rt = c as RectTransform; var corners = new Vector3[4]; rt.GetWorldCorners(corners);
                var eb = FindRec(c, "EditButton") as RectTransform; string ebStr = "<none>";
                if (eb != null) { var ec = new Vector3[4]; eb.GetWorldCorners(ec); ebStr = $"x{ec[0].x:F0}-{ec[2].x:F0} y{ec[0].y:F0}-{ec[2].y:F0}"; }
                Debug.Log($"[RowDiag] row#{i} rectY {corners[0].y:F0}-{corners[1].y:F0} edit[{ebStr}]");
            }
        }

        private static string OptionSummary(RawPlayerOption o)
        {
            string t = o.Text ?? "";
            if (t.Length > 30) t = t.Substring(0, 30) + "...";
            string nxt = string.IsNullOrEmpty(o.NextUID) ? "" : " -> " + o.NextUID;
            return "[" + o.Type + "] " + t + nxt;
        }

        private void OnAddOption()
        {
            _dlgOptions.Add(new RawPlayerOption { Type = InteractionType.Option, Text = "New option", AlwaysVisible = true, Color = new Color32(255, 255, 255, 255) });
            RebuildOptionRows();
            SetDlgDirty();
            OpenOptionPopup(_dlgOptions.Count - 1);
        }

        // ---- option popup (Pass 1: single-value fields; Pass 2 adds the nested lists) ----
        private BookOptionPopup _bookOptionPopup;

        // Opens the runtime, draggable, auto-sizing option editor (BookOptionPopup) — the original working popup
        // recolored to parchment + positioned right of the book — instead of the old baked OptionPopup.
        private void OpenOptionPopup(int index)
        {
            Debug.Log($"[NpcBookController] OpenOptionPopup({index}) — options={_dlgOptions.Count}");
            if (index < 0 || index >= _dlgOptions.Count) return;
            _editingOptionIndex = index;
            if (_bookOptionPopup == null)
            {
                var host = new GameObject("BookOptionPopupHost");
                host.transform.SetParent(_root, false);
                _bookOptionPopup = host.AddComponent<BookOptionPopup>();
            }
            var bookRect = _panelGo.GetComponent<RectTransform>();
            _bookOptionPopup.Open(_dlgOptions[index], bookRect, () => { _editingOptionIndex = -1; RebuildOptionRows(); SetDlgDirty(); });
        }

        private void LoadPopupLists(RawPlayerOption o)
        {
            ClearPopupRows("Transition");
            if (o.RandomTransition != null) foreach (var uid in o.RandomTransition) { var row = ClonePopupRow("Transition", "TransitionRowTemplate"); if (row != null) SetRowCell(row, "UidCell", uid); }
            StackPopupRows("Transition");

            ClearPopupRows("Command");
            if (o.Commands != null) foreach (var cmd in o.Commands) { var row = ClonePopupRow("Command", "CommandRowTemplate"); if (row != null) { SplitNameParams(cmd, out var nm, out var pm); SetRowDropdown(row, "NameDropdown", CommandNames, nm); SetRowCell(row, "ParamsCell", pm); } }
            StackPopupRows("Command");

            ClearPopupRows("RandCmd");
            if (o.RandomCommands != null) foreach (var rc in o.RandomCommands) { var row = ClonePopupRow("RandCmd", "RandCmdRowTemplate"); if (row != null) { SplitNameParams(rc.Value, out var nm, out var pm); SetRowCell(row, "WeightCell", rc.Key.ToString()); SetRowDropdown(row, "NameDropdown", CommandNames, nm); SetRowCell(row, "ParamsCell", pm); } }
            StackPopupRows("RandCmd");

            ClearPopupRows("Cond");
            if (o.Conditions != null) for (int g = 0; g < o.Conditions.Count; g++) { var group = o.Conditions[g]; if (group == null) continue; foreach (var member in group) { var row = ClonePopupRow("Cond", "CondRowTemplate"); if (row != null) { SplitNameParams(member, out var nm, out var pm); SetRowCell(row, "GroupCell", g.ToString()); SetRowDropdown(row, "NameDropdown", ConditionNames, nm); SetRowCell(row, "ParamsCell", pm); } } }
            StackPopupRows("Cond");
        }

        private void SavePopupLists(RawPlayerOption o)
        {
            var rt = new List<string>();
            foreach (var row in PopupRows("Transition")) { var u = RowCell(row, "UidCell")?.Trim(); if (!string.IsNullOrEmpty(u)) rt.Add(u); }
            o.RandomTransition = rt.ToArray();

            var cmds = new List<string>();
            foreach (var row in PopupRows("Command")) { string nm = GetRowDropdown(row, "NameDropdown"); string pm = RowCell(row, "ParamsCell") ?? ""; if (!string.IsNullOrEmpty(nm)) cmds.Add(JoinNameParams(nm, pm)); }
            o.Commands = cmds.ToArray();

            var rcmds = new List<KeyValuePair<int, string>>();
            foreach (var row in PopupRows("RandCmd")) { int w = ParseInt(RowCell(row, "WeightCell"), 0); string nm = GetRowDropdown(row, "NameDropdown"); string pm = RowCell(row, "ParamsCell") ?? ""; if (w > 0 && !string.IsNullOrEmpty(nm)) rcmds.Add(new KeyValuePair<int, string>(w, JoinNameParams(nm, pm))); }
            o.RandomCommands = rcmds;

            var groups = new SortedDictionary<int, List<string>>();
            foreach (var row in PopupRows("Cond")) { int gi = ParseInt(RowCell(row, "GroupCell"), 0); string nm = GetRowDropdown(row, "NameDropdown"); string pm = RowCell(row, "ParamsCell") ?? ""; if (string.IsNullOrEmpty(nm)) continue; if (!groups.ContainsKey(gi)) groups[gi] = new List<string>(); groups[gi].Add(JoinNameParams(nm, pm)); }
            o.Conditions = groups.Values.Select(v => v.ToArray()).ToList();
        }

        private void SavePopupToOption(int index)
        {
            if (_optionPopup == null || index < 0 || index >= _dlgOptions.Count) return;
            var o = _dlgOptions[index];
            var dd = FindRec(_optionPopup, "TypeDropdown")?.GetComponent<TMP_Dropdown>();
            if (dd != null && dd.value >= 0 && dd.value < OptionTypeNames.Length) o.Type = (InteractionType)dd.value;
            o.Text = GetPopupInput("Text") ?? "";
            o.Icon = GetPopupInput("Icon") ?? "";
            o.NextUID = (GetPopupInput("Transition") ?? "").Replace(" ", "");
            o.OverrideError = GetPopupInput("Override Error") ?? "";
            var av = Tog(_optionPopup, "AlwaysVisible_chk"); if (av != null) o.AlwaysVisible = av.isOn;
            byte r = (byte)Mathf.Clamp(ParseInt(GetPopupInput("R"), 255), 0, 255);
            byte g = (byte)Mathf.Clamp(ParseInt(GetPopupInput("G"), 255), 0, 255);
            byte b = (byte)Mathf.Clamp(ParseInt(GetPopupInput("B"), 255), 0, 255);
            o.Color = new Color32(r, g, b, 255);
            SavePopupLists(o);
        }

        private void OnPopupDone()
        {
            if (_editingOptionIndex >= 0) SavePopupToOption(_editingOptionIndex);
            _editingOptionIndex = -1;
            if (_optionPopup != null) _optionPopup.gameObject.SetActive(false);
            RebuildOptionRows();
            SetDlgDirty();
        }

        private void OnPopupCancel()
        {
            _editingOptionIndex = -1;
            if (_optionPopup != null) _optionPopup.gameObject.SetActive(false);
        }

        private void UpdateColorSwatch()
        {
            var sw = FindRec(_optionPopup, "ColorSwatch")?.GetComponent<Image>();
            if (sw == null) return;
            sw.color = new Color32((byte)Mathf.Clamp(ParseInt(GetPopupInput("R"), 255), 0, 255),
                                   (byte)Mathf.Clamp(ParseInt(GetPopupInput("G"), 255), 0, 255),
                                   (byte)Mathf.Clamp(ParseInt(GetPopupInput("B"), 255), 0, 255), 255);
        }

        private TMP_InputField PopupInput(string container) { var t = FindRec(_optionPopup, container + "_C"); return t != null ? t.GetComponentInChildren<TMP_InputField>(true) : null; }
        private void SetPopupInput(string container, string val) { var i = PopupInput(container); if (i != null) i.text = val ?? ""; }
        private string GetPopupInput(string container) { var i = PopupInput(container); return i != null ? i.text : null; }

        // ---- save / delete / preview / new ----
        private RawDialogue BuildDialogueFromForm()
        {
            _bookOptionPopup?.FlushIfOpen();
            string uid = (GetScreenInput("Dialogue", "UID_C") ?? "").Trim().ToLowerInvariant();
            string bg = GetScreenInput("Dialogue", "BG Image_C")?.Trim() ?? "";
            string npcText = GetScreenInput("Dialogue", "NpcText") ?? "";
            return new RawDialogue { UID = uid, Text = npcText, BG_Image = bg, Interactions = _dlgOptions.ToArray() };
        }

        private void SaveDialogue()
        {
            var d = BuildDialogueFromForm();
            if (string.IsNullOrEmpty(d.UID)) { Msg("Enter a dialogue UID"); return; }
            try
            {
                string cfg = DialogueSyncRPC.SerializeDialogueToCfg(d);
                DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(d.UID) + ".cfg", cfg);
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveDialogue: " + ex.Message); Msg("Save failed"); return; }
            _npc.dialogueProfile = d.UID; _npc.npcType = NpcType.DialogueNpc; _npc.SaveToZDO();
            _dlgSelectedUid = d.UID;
            PopulateDialogueList();
            SetDlgStatus("Saved " + d.UID);
            Msg("Saved dialogue '" + d.UID + "'");
        }

        private void DeleteDialogue()
        {
            string uid = (GetScreenInput("Dialogue", "UID_C") ?? "").Trim().ToLowerInvariant();
            DeleteDialogueByUid(uid);
        }

        private void DeleteDialogueByUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            try { DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(uid.ToLowerInvariant()) + ".cfg", null); } catch { }
            if (uid == _dlgSelectedUid) NewDialogue();
            PopulateDialogueList();
            SetDlgStatus("Deleted " + uid);
            Msg("Deleted dialogue '" + uid + "'");
        }

        private void PreviewDialogue()
        {
            var d = BuildDialogueFromForm();
            if (string.IsNullOrEmpty(d.UID)) { Msg("Enter a UID to preview"); return; }
            try
            {
                string cfg = DialogueSyncRPC.SerializeDialogueToCfg(d);
                DialogueCodex.ParseFromContent(cfg);
                DialogueUI.Destroy();
                DialogueUI.Show(_npc.npcProfileName, d.UID, _npc.GetComponent<ZNetView>());
                SetDlgStatus("Preview opened");
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] PreviewDialogue: " + ex.Message); Msg("Preview failed"); }
        }

        private void NewDialogue()
        {
            _dlgSelectedUid = null;
            _dlgOptions.Clear();
            SetScreenInput("Dialogue", "UID_C", "new_dialogue");
            SetScreenInput("Dialogue", "BG Image_C", "");
            SetScreenInput("Dialogue", "NpcText", "");
            RebuildOptionRows();
            SetDlgStatus("New dialogue", dirty: true);
        }

        private static readonly Color DLG_STATUS_DIRTY = new Color(0.80f, 0.16f, 0.12f, 1f); // red — unsaved changes
        private static readonly Color DLG_STATUS_OK    = new Color(0.16f, 0.50f, 0.18f, 1f); // green — saved / clean

        private void SetDlgStatus(string s, bool dirty = false)
        {
            var t = FindRec(Screen("Dialogue"), "DlgStatus")?.GetComponent<TMP_Text>();
            if (t == null) return;
            t.text = s;
            t.color = dirty ? DLG_STATUS_DIRTY : DLG_STATUS_OK;
            // Reposition below the Save/Delete/Preview button row — it was baked to the right of Preview
            // (x 0.81-1.0) and clipped in the corner. Runtime move avoids a re-bake; left-aligned strip
            // just under the buttons (their band is y 0-0.07 of this screen).
            var rt = t.rectTransform;
            rt.anchorMin = new Vector2(0.02f, -0.045f);
            rt.anchorMax = new Vector2(0.81f, 0.008f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Left;
        }
        private void SetDlgDirty() => SetDlgStatus("Unsaved changes", dirty: true);

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
            // The mail cfg is one row per physical line; escape body newlines so a multi-line message
            // survives the line-by-line loader (MailProfileManager.LoadOne un-escapes on read).
            string body = (GetScreenInput("Mail", "MailBody") ?? "").Replace("\r", "").Replace("\n", "\\n");
            string attach = GetScreenInput("Mail", "Attachment_C")?.Trim() ?? "";
            string date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string row = sender + "|" + date + "|" + topic + "|" + body + (string.IsNullOrEmpty(attach) ? "" : "|" + attach);
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
                // Walk block-aligned (a block = up to 6 consecutive non-blank, non-'[' lines); replace the
                // block whose 2nd line (Name) matches. Scanning every line could splice across boundaries.
                int i = 0;
                while (i < raw.Count)
                {
                    string t = raw[i].Trim();
                    if (t.Length == 0 || t.StartsWith("[")) { i++; continue; }
                    int start = i, j = i, count = 0;
                    while (j < raw.Count && count < 6)
                    {
                        string tj = raw[j].Trim();
                        if (tj.Length == 0 || tj.StartsWith("[")) break;
                        j++; count++;
                    }
                    if (count >= 2 && raw[start + 1].Trim() == name)
                    {
                        raw.RemoveRange(start, count);
                        raw.InsertRange(start, SplitLines(block));
                        File.WriteAllText(filePath, string.Join(Environment.NewLine, raw) + Environment.NewLine);
                        return true;
                    }
                    i = j;
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
            if (pool == null || tmpl == null) { Debug.LogWarning($"[NpcBookController] AddRewardRow pool={pool != null} tmpl={tmpl != null}"); return; }
            var content = PoolContent(pool);
            var row = Instantiate(tmpl, content);
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
            var content = PoolContent(pool);
            var row = Instantiate(tmpl, content);
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
            var content = PoolContent(FindRec(Screen(tab), poolName));
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var c = content.GetChild(i);
                // Destroy is deferred to end-of-frame; deactivate now so a same-frame repopulate +
                // ReflowScroll (which counts only active "Row"s) doesn't stack over the doomed rows.
                if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            }
        }

        private List<Transform> LiveRows(string tab, string poolName)
        {
            var list = new List<Transform>();
            var content = PoolContent(FindRec(Screen(tab), poolName));
            if (content == null) return list;
            foreach (Transform c in content)
                if (c.name == "Row" && c.gameObject.activeSelf) list.Add(c);
            return list;
        }

        private void RestackRows(string tab, string poolName)
        {
            ReflowScroll(PoolContent(FindRec(Screen(tab), poolName)));
        }

        // Returns the Content node of a list's RUNTIME scroll, building it on first use. The book prefab bakes
        // only an empty anchor container (named e.g. "RewardPool") plus a hidden row template; the actual
        // ScrollRect (RectMask2D viewport — AssetBundle-safe) is created here with UIBuilderHelper, and the
        // baked template is moved under its Content. A stencil Mask baked into the bundle leaves its masked
        // rows invisible in-game (the cause of the empty dialogue list + dead reward +Add), so the scroll must
        // be constructed at runtime — never baked.
        private static Transform PoolContent(Transform anchor)
        {
            if (anchor == null) return null;
            var built = anchor.Find("RuntimeScroll/Viewport/Content");
            if (built != null) return built;

            var sa = FiresCore.UI.UIBuilderHelper.CreateScrollArea(anchor, "RuntimeScroll",
                Vector2.zero, Vector2.one, new Color(0.27f, 0.18f, 0.08f, 0.10f));
            var content = sa.Content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            // Move the baked hidden row template(s) (siblings of the new scroll) under the runtime Content.
            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                var c = anchor.GetChild(i);
                if (c.name.EndsWith("Template", StringComparison.Ordinal)) c.SetParent(content, false);
            }
            return content;
        }

        // Positions each active child of a scroll Content top-down by its LayoutElement height (+ spacing/
        // padding), and resizes Content so the ScrollRect can scroll. Ported from BasePlayerModulePanel —
        // the proven pattern that replaced the hand-rolled absolute-% rows. Inactive children (the hidden
        // template + deactivated-pending-destroy rows) are skipped so they leave no gap.
        private static void ReflowScroll(Transform content)
        {
            if (content == null) return;
            const float spacing = 4f, padding = 6f;
            float y = padding;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                var cle = child.GetComponent<LayoutElement>();
                float h = cle != null && cle.preferredHeight > 0 ? cle.preferredHeight
                        : cle != null && cle.minHeight > 0 ? cle.minHeight
                        : child.sizeDelta.y > 0 ? child.sizeDelta.y : 24f;
                child.anchorMin = new Vector2(0f, 1f);
                child.anchorMax = new Vector2(1f, 1f);
                child.pivot = new Vector2(0.5f, 1f);
                child.offsetMin = new Vector2(padding, child.offsetMin.y);
                child.offsetMax = new Vector2(-padding, child.offsetMax.y);
                child.anchoredPosition = new Vector2(0f, -y);
                child.sizeDelta = new Vector2(-padding * 2f, h);
                y += h + spacing;
            }
            var crt = content as RectTransform;
            if (crt != null) crt.sizeDelta = new Vector2(crt.sizeDelta.x, y + padding);
        }

        // Belt-and-suspenders: make every interactive widget actually respond (raycast target on,
        // interactable, a visible caret on inputs). Cloned rows inherit this from their baked templates.
        private static void NormalizeInteractive(Transform root)
        {
            foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
            {
                if (sel == null) continue;
                sel.interactable = true;
                if (sel.targetGraphic != null) sel.targetGraphic.raycastTarget = true;
            }
            foreach (var inp in root.GetComponentsInChildren<TMP_InputField>(true))
            {
                if (inp == null) continue;
                if (inp.caretWidth <= 0) inp.caretWidth = 2;
                inp.customCaretColor = true;
                if (inp.caretColor.a <= 0f) inp.caretColor = new Color(0.20f, 0.13f, 0.05f, 1f);
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

        private void WireDialogueExtras()
        {
            var search = InputIn(_leftPage, "DlgSearchField");
            if (search != null) { search.onValueChanged.RemoveAllListeners(); search.onValueChanged.AddListener(_ => PopulateDialogueList()); }
            foreach (var ch in new[] { "R", "G", "B" })
            {
                var inp = PopupInput(ch);
                if (inp != null) { inp.onValueChanged.RemoveAllListeners(); inp.onValueChanged.AddListener(_ => UpdateColorSwatch()); }
            }
            WireToggleVisual(Tog(_optionPopup, "AlwaysVisible_chk"));
        }

        private Toggle Tog(Transform scope, string name) { var t = FindRec(scope, name); return t != null ? t.GetComponent<Toggle>() : null; }
        private void SetToggle(string tab, string name, bool on) { var t = Tog(Screen(tab), name); if (t != null) t.isOn = on; }
        private bool GetToggle(string tab, string name) { var t = Tog(Screen(tab), name); return t != null && t.isOn; }

        private Button Btn(string name) { var t = FindRec(_root, name); return t != null ? t.GetComponent<Button>() : null; }
        private void Hook(string name, UnityEngine.Events.UnityAction act)
        {
            var b = Btn(name);
            if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(act); }
            else Debug.LogWarning("[NpcBookController] button not found to hook: " + name);
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
        // Matches TraderProfileManager.MakeSafeFileName (invalid-filename-chars only) so a book write and a
        // manager write of the same profile name land on the same file.
        private static string SafeFileName(string n)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in (n ?? "").Trim()) sb.Append(System.Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "Profile";
        }
        private void Msg(string m)
        {
            try { MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, m); } catch { }
            Debug.Log("[NpcBookController] " + m);
        }
    }
}
