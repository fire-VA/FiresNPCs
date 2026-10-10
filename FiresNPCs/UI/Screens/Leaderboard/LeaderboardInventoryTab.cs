using System;
using System.Collections.Generic;
using FiresRPGmaker.Modules.Marketplace;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Pal = FiresCore.UI.UIFontConfig.Colors;

namespace FiresRPGmaker.UI.Leaderboard
{
    // Leaderboards inside the inventory: one tab in VAInventory's Crafting-panel tab row (InventoryTabs API,
    // VAInventory 1.0.26+, Tools\VAINVENTORY_TAB_API.md), so the board is one click from opening the inventory.
    // The view is the FiresLeaderboardTab prefab from the firesleaderboardtab bundle (authored in
    // ValheimRip1.0/Assets/Custom/FiresNPCsUI, editable in Unity); this class only binds nodes by name, fills rows
    // and refreshes on show. VAInventory stays optional: registration is reflection and does nothing without it.
    internal static class LeaderboardInventoryTab
    {
        private const string TabId = "firesrpgmaker.leaderboards";
        private const string PrefabName = "FiresLeaderboardTab.prefab";
        private static readonly string[] CellNodes =
            { "RankLabel", "PlayerLabel", "KillsLabel", "PvPLabel", "BuiltLabel", "CraftLabel", "DiedLabel", "MapLabel" };

        private static bool _registered;
        private static RectTransform _view;
        private static GameObject _statsView, _achView;
        private static RectTransform _statsContent, _achContent;
        private static GameObject _statRowTemplate, _achRowTemplate;
        private static TMP_Text _summary, _achSummary;
        private static Button _tabStats, _tabAch;
        private static bool _showingAch;
        private static string _sortBy = "KilledCreatures";
        private static bool _visible, _subscribed;
        private static bool _waitLogged;

        public static void Register()
        {
            if (_registered) return;
            try
            {
                var tabsType = AccessTools.TypeByName("VerdantsAscent.Managers.InventoryTabs");
                var tabType = AccessTools.TypeByName("VerdantsAscent.Managers.InventoryTabs+Tab");
                if (tabsType == null || tabType == null)
                {
                    if (!_waitLogged)
                    {
                        _waitLogged = true;
                        Debug.Log("[LeaderboardInventoryTab] VAInventory InventoryTabs not loaded yet - retrying when the inventory is built");
                    }
                    return;
                }
                var register = tabsType.GetMethod("Register", new[] { tabType });
                if (register == null)
                {
                    Debug.LogWarning("[LeaderboardInventoryTab] InventoryTabs.Register(Tab) not found - VAInventory API changed");
                    return;
                }
                object tab = Activator.CreateInstance(tabType);
                SetField(tab, tabType, "Id", TabId);
                SetField(tab, tabType, "Label", "Leaderboards");
                SetField(tab, tabType, "Order", 40);
                SetField(tab, tabType, "Build", (Action<RectTransform>)Build);
                // VAInventory 1.0.35+: a tab with Open is a LAUNCHER - clicking it calls this and builds no
                // inline view, so the full leaderboard window opens at its own size and the tab row stays live.
                // Both fields are set deliberately: Open wins where it exists, and on an older VAInventory the
                // SetField no-ops and the inline Build view is still used rather than the tab going dead.
                SetField(tab, tabType, "Open", (Action)OpenStandaloneBoard);
                SetField(tab, tabType, "OnVisibility", (Action<bool>)OnVisibility);
                SetField(tab, tabType, "IsAvailable", (Func<bool>)(() => FiresRPGmaker.leaderboardTabBundle != null));
                register.Invoke(null, new[] { tab });
                _registered = true;
                Debug.Log("[LeaderboardInventoryTab] registered '" + TabId + "' with VAInventory");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LeaderboardInventoryTab] registration failed: " + ex.Message);
            }
        }

        private static void OpenStandaloneBoard()
        {
            // The window is built against InventoryGui and its hotkey refuses to open while the inventory is up,
            // so dismiss the inventory first: VAInventory does not do it for us, by design.
            try { if (InventoryGui.instance != null && InventoryGui.IsVisible()) InventoryGui.instance.Hide(); }
            catch (Exception ex) { Debug.LogWarning("[LeaderboardInventoryTab] could not hide the inventory: " + ex.Message); }

            FiresLeaderboardGui.Open();
        }

        private static void SetField(object target, Type type, string field, object value)
            => type.GetField(field)?.SetValue(target, value);

        private static void Build(RectTransform parent)
        {
            var bundle = FiresRPGmaker.leaderboardTabBundle;
            if (bundle == null) { Debug.LogWarning("[LeaderboardInventoryTab] firesleaderboardtab bundle not loaded"); return; }
            GameObject prefab = null;
            string want = "/" + PrefabName.ToLowerInvariant();
            foreach (var assetName in bundle.GetAllAssetNames())
                if (assetName.EndsWith(want, StringComparison.OrdinalIgnoreCase)) { prefab = bundle.LoadAsset<GameObject>(assetName); break; }
            if (prefab == null) { Debug.LogWarning("[LeaderboardInventoryTab] " + PrefabName + " not in the bundle"); return; }

            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "FiresLeaderboardTab";
            // Fonts bind by walking the prefab root, so bind BEFORE the view leaves it.
            FiresCore.UI.UIBuilderHelper.ApplyValheimFontToAll(instance);
            var view = FindDeep(instance.transform, "View") as RectTransform;
            if (view == null)
            {
                Debug.LogWarning("[LeaderboardInventoryTab] prefab has no 'View' node");
                UnityEngine.Object.Destroy(instance);
                return;
            }
            view.SetParent(parent, false);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = Vector2.zero;
            view.offsetMax = Vector2.zero;
            view.localScale = Vector3.one;
            view.gameObject.SetActive(true);
            // The 1920x1080 root with its Canvas is the editing frame; vanilla's Canvas stays the only one.
            UnityEngine.Object.Destroy(instance);
            _view = view;
            Bind(view);
        }

        private static void Bind(Transform t)
        {
            _summary = FindTmp(t, "SummaryText");
            _tabStats = FindBtn(t, "TabStats");
            _tabAch = FindBtn(t, "TabAch");
            if (_tabStats != null) { _tabStats.onClick.RemoveAllListeners(); _tabStats.onClick.AddListener(() => { _showingAch = false; Refresh(); }); }
            if (_tabAch != null) { _tabAch.onClick.RemoveAllListeners(); _tabAch.onClick.AddListener(() => { _showingAch = true; Refresh(); }); }

            _statsView = FindDeep(t, "StatsView")?.gameObject;
            _statsContent = FindDeep(t, "StatsScroll")?.Find("Viewport/Content") as RectTransform;
            _statRowTemplate = FindDeep(t, "StatRowTemplate")?.gameObject;
            if (_statRowTemplate != null) _statRowTemplate.SetActive(false);

            _achView = FindDeep(t, "AchView")?.gameObject;
            _achContent = FindDeep(t, "AchScroll")?.Find("Viewport/Content") as RectTransform;
            _achRowTemplate = FindDeep(t, "AchRowTemplate")?.gameObject;
            if (_achRowTemplate != null) _achRowTemplate.SetActive(false);
            _achSummary = FindTmp(t, "AchSummary");

            WireSort(t, "SortKills", "KilledCreatures");
            WireSort(t, "SortPvP", "KilledPlayers");
            WireSort(t, "SortBuilt", "BuiltStructures");
            WireSort(t, "SortCrafted", "ItemsCrafted");
            WireSort(t, "SortDeaths", "Died");
            WireSort(t, "SortExplored", "MapExplored");

            DropSamples(_statsContent);
            DropSamples(_achContent);
            if (_summary != null) _summary.color = Pal.ParchmentInk;
            if (_achSummary != null) _achSummary.color = Pal.ParchmentLabel;
        }

        private static void DropSamples(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child.name.StartsWith("SampleRow_", StringComparison.Ordinal)) UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static void WireSort(Transform root, string node, string sortKey)
        {
            var btn = FindBtn(root, node);
            if (btn == null) return;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => { _sortBy = sortKey; _showingAch = false; Refresh(); });
        }

        private static void OnVisibility(bool shown)
        {
            _visible = shown;
            if (!shown || _view == null) return;
            EnsureSubscription();
            try { LeaderboardBoard.RequestBoard(); }
            catch (Exception ex) { Debug.LogWarning("[LeaderboardInventoryTab] board request failed: " + ex.Message); }
            Refresh();
        }

        private static void EnsureSubscription()
        {
            if (_subscribed) return;
            _subscribed = true;
            FiresCore.Bridge.LeaderboardBridge.EntryPublished += _ => { if (_visible && _view != null) Refresh(); };
        }

        private static void Refresh()
        {
            if (_view == null) return;
            HighlightTab(_tabStats, !_showingAch);
            HighlightTab(_tabAch, _showingAch);
            if (_statsView != null) _statsView.SetActive(!_showingAch);
            if (_achView != null) _achView.SetActive(_showingAch);
            try
            {
                if (_showingAch) RefreshAchievements(); else RefreshStats();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LeaderboardInventoryTab] refresh failed: " + ex.Message);
            }
        }

        private static void HighlightTab(Button btn, bool active)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = active ? Pal.ParchmentButtonHover : Pal.ParchmentButton;
        }

        private static void RefreshStats()
        {
            if (_statsContent == null || _statRowTemplate == null) return;
            ClearClones(_statsContent, _statRowTemplate);
            var entries = new List<LeaderboardEntry>(LeaderboardBoard.Rows.Count);
            foreach (var r in LeaderboardBoard.Rows)
                entries.Add(new LeaderboardEntry
                {
                    PlayerName = r.PlayerName,
                    KilledCreatures = r.Monsters,
                    KilledPlayers = r.PlayersKilled,
                    BuiltStructures = r.Structures,
                    ItemsCrafted = r.Crafted,
                    Died = r.Deaths,
                    MapExplored = r.MapExplored,
                });
            if (entries.Count == 0)
            {
                if (_summary != null) _summary.text = "No leaderboard data yet - kill, build, craft and explore to climb the board.";
                Reflow(_statsContent, 2f, 6f);
                return;
            }
            var sorted = LeaderboardProfileManager.SortBy(entries, _sortBy);
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            if (_summary != null)
                _summary.text = "Top " + sorted.Count + " by " + _sortBy + (string.IsNullOrEmpty(localName) ? "" : "  -  You: " + localName);
            int rank = 1;
            foreach (var e in sorted)
            {
                bool isMe = !string.IsNullOrEmpty(localName) && string.Equals(localName, e.PlayerName, StringComparison.OrdinalIgnoreCase);
                BuildStatRow(rank, e, isMe);
                rank++;
            }
            Reflow(_statsContent, 2f, 6f);
        }

        private static void BuildStatRow(int rank, LeaderboardEntry e, bool isMe)
        {
            var rowGo = UnityEngine.Object.Instantiate(_statRowTemplate, _statsContent);
            rowGo.name = "LBRow_" + rank;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 26f);
            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = isMe ? Pal.ParchmentItemSelected : Pal.ParchmentPanel;
            string[] vals =
            {
                rank.ToString(),
                e.PlayerName,
                e.KilledCreatures.ToString(),
                e.KilledPlayers.ToString(),
                e.BuiltStructures.ToString(),
                e.ItemsCrafted.ToString(),
                e.Died.ToString(),
                (e.MapExplored * 100f).ToString("0") + "%",
            };
            for (int i = 0; i < CellNodes.Length; i++)
            {
                var cell = FindTmp(rowGo.transform, CellNodes[i]);
                if (cell == null) continue;
                cell.text = vals[i];
                cell.color = Pal.ParchmentInk;
            }
        }

        private static void RefreshAchievements()
        {
            if (_achContent == null || _achRowTemplate == null) return;
            ClearClones(_achContent, _achRowTemplate);
            string profile = global::FiresRPGmaker.Managers.ConfigManager.Instance?.configAchievementProfile?.Value;
            if (string.IsNullOrEmpty(profile)) profile = "server";
            var defs = AchievementProfileManager.GetAchievements(profile);
            if (defs == null || defs.Count == 0)
            {
                if (_achSummary != null) _achSummary.text = "No achievements defined for profile '" + profile + "'.";
                Reflow(_achContent, 3f, 6f);
                return;
            }
            int unlocked = 0, total = 0, earned = 0;
            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
            foreach (var def in defs)
            {
                int progress = LeaderboardTracker.GetProgress(playerName, def.TriggerType, def.Prefab);
                bool done = progress >= def.MinAmount;
                if (done) unlocked++;
                total += def.Score;
                if (done) earned += def.Score;
                BuildAchRow(def, progress, done);
            }
            Reflow(_achContent, 3f, 6f);
            if (_achSummary != null) _achSummary.text = "Unlocked: " + unlocked + "/" + defs.Count + "  -  Score: " + earned + "/" + total;
        }

        private static void BuildAchRow(AchievementDef def, int progress, bool done)
        {
            var rowGo = UnityEngine.Object.Instantiate(_achRowTemplate, _achContent);
            rowGo.name = "Ach_" + def.Id;
            rowGo.SetActive(true);
            SetRowHeight(rowGo, 56f);
            var img = rowGo.GetComponent<Image>();
            if (img != null) img.color = done ? Pal.ParchmentItemSelected : Pal.ParchmentPanel;
            var nameLabel = FindTmp(rowGo.transform, "NameLabel");
            if (nameLabel != null) { nameLabel.text = (done ? "[X] " : "[  ] ") + def.Name; nameLabel.color = done ? new Color(0.13f, 0.42f, 0.13f) : Pal.ParchmentInk; }
            var descLabel = FindTmp(rowGo.transform, "DescLabel");
            if (descLabel != null) { descLabel.text = def.Description; descLabel.color = Pal.ParchmentLabel; }
            float pct = def.MinAmount <= 0 ? 0f : Mathf.Clamp01((float)progress / def.MinAmount);
            var barFill = FindDeep(rowGo.transform, "BarFill") as RectTransform;
            if (barFill != null)
            {
                barFill.anchorMin = new Vector2(0f, barFill.anchorMin.y);
                barFill.anchorMax = new Vector2(pct, barFill.anchorMax.y);
                var fillImg = barFill.GetComponent<Image>();
                if (fillImg != null) fillImg.color = done ? new Color(0.30f, 0.62f, 0.25f, 0.95f) : Pal.ParchmentButton;
            }
            var progressLabel = FindTmp(rowGo.transform, "ProgressLabel");
            if (progressLabel != null) { progressLabel.text = progress + "/" + def.MinAmount; progressLabel.color = Pal.ParchmentLabel; }
            var scoreLabel = FindTmp(rowGo.transform, "ScoreLabel");
            if (scoreLabel != null) { scoreLabel.text = def.Score.ToString(); scoreLabel.color = Pal.ParchmentInk; }
        }

        // Cloned rows do not lay themselves out (the prefab's own children were positioned at save time), so stack
        // them top-down here, the same way BasePlayerModulePanel.ReflowScroll does, and size the content to match.
        private static void Reflow(RectTransform content, float spacing, float padding)
        {
            if (content == null) return;
            float y = padding;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                var le = child.GetComponent<LayoutElement>();
                float h = le != null && le.preferredHeight > 0 ? le.preferredHeight
                        : le != null && le.minHeight > 0 ? le.minHeight
                        : child.sizeDelta.y > 0 ? child.sizeDelta.y : 24f;
                child.anchorMin = new Vector2(0f, 1f);
                child.anchorMax = new Vector2(1f, 1f);
                child.pivot = new Vector2(0.5f, 1f);
                child.anchoredPosition = new Vector2(0f, -y);
                child.sizeDelta = new Vector2(-padding * 2f, h);
                y += h + spacing;
            }
            content.sizeDelta = new Vector2(content.sizeDelta.x, y + padding);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static void ClearClones(RectTransform content, GameObject template)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (child == template) continue;
                UnityEngine.Object.Destroy(child);
                child.transform.SetParent(null, false);
            }
        }

        private static void SetRowHeight(GameObject rowGo, float height)
        {
            var le = rowGo.GetComponent<LayoutElement>() ?? rowGo.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            var rt = rowGo.GetComponent<RectTransform>();
            if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static TMP_Text FindTmp(Transform root, string name) => FindDeep(root, name)?.GetComponent<TMP_Text>();
        private static Button FindBtn(Transform root, string name) => FindDeep(root, name)?.GetComponent<Button>();
    }

    [HarmonyPatch(typeof(InventoryGui), "Awake")]
    internal static class LeaderboardTabLateRegister
    {
        private static void Postfix() => LeaderboardInventoryTab.Register();
    }
}
