using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Marketplace;
using Pal = FiresCore.UI.UIFontConfig.Colors;

namespace FiresRPGmaker.UI.Leaderboard
{
    public sealed partial class FiresLeaderboardGui
    {
        private static readonly Type CanvasButtonType = ResolveCanvasType("UnityEngine.UI.Button", "UnityEngine.UI.Button, UnityEngine.UI");
        private static readonly Type CanvasImageType = ResolveCanvasType("UnityEngine.UI.Image", "UnityEngine.UI.Image, UnityEngine.UI");
        private static readonly Type CanvasTextType = ResolveCanvasType("UnityEngine.UI.Text", "UnityEngine.UI.Text, UnityEngine.UI");
        private static readonly Type CanvasTmpTextType = ResolveCanvasType("TMPro.TextMeshProUGUI", "TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        private static readonly Type CanvasUnityActionType = ResolveCanvasType("UnityEngine.Events.UnityAction", "UnityEngine.Events.UnityAction, UnityEngine.CoreModule");

        private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.35f);

        // House style (Fire 10-08, leaderboard overhaul): the Classes window / VAInventory palette.
        private static readonly Color LbPanel = new Color32(0x24, 0x1a, 0x11, 0xff);
        private static readonly Color LbCell = new Color32(0x2e, 0x21, 0x13, 0xff);
        private static readonly Color LbCellDone = new Color32(0x3a, 0x2a, 0x12, 0xff);
        private static readonly Color LbWell = new Color32(0x1b, 0x14, 0x0d, 0xff);
        private static readonly Color LbEdge = new Color32(0x4a, 0x38, 0x24, 0xff);
        private static readonly Color LbGold = new Color32(0xf0, 0xb4, 0x4c, 0xff);
        private static readonly Color LbText = new Color32(0xef, 0xe2, 0xc4, 0xff);
        private static readonly Color LbMuted = new Color32(0xb3, 0xa1, 0x81, 0xff);

        // Every other row gets a slightly darker tan so the table reads as rows, not one flat parchment slab.
        private static readonly Color RowAltFill = new Color32(0x2e, 0x21, 0x13, 0xff);   // house style: the dark cell

        // The achievement profile the tab + details score against. Resolved from config (default "server"),
        // falling back to the first non-empty loaded profile, else "example". Re-evaluated on each use so a
        // late-arriving ServerSync value or a freshly-generated profile is picked up without a restart.
        private static string AchievementProfile => AchievementProfileManager.ResolveActiveProfile();

        private GameObject _canvasRoot;
        /// <summary>The leaderboard's canvas root (null before the first open); read by the UI test.</summary>
        public static GameObject CanvasRoot => _instance != null ? _instance._canvasRoot : null;
        private RectTransform _canvasRootRect;
        private GameObject _canvasContentRoot;
        private GameObject _canvasButtonTemplate;
        private GameObject _canvasTextTemplate;
        private GameObject _canvasTitleTemplate;
        private bool _canvasReady;
        private string _canvasSignature = string.Empty;

        private static Type ResolveCanvasType(string fullName, params string[] qualifiedNames)
        {
            Type type = AccessTools.TypeByName(fullName);
            if (type != null) return type;
            foreach (string qualified in qualifiedNames)
            {
                type = Type.GetType(qualified, false);
                if (type != null) return type;
            }
            return null;
        }

        // ---- Canvas readiness / lifecycle ---------------------------------------------------------------

        private bool EnsureCanvasUiReady()
        {
            if (_canvasReady && _canvasRoot != null && _canvasContentRoot != null) return true;

            InventoryGui inventoryGui = InventoryGui.instance;
            if (inventoryGui == null) return false;

            Component sourceDialog = Traverse.Create(inventoryGui).Field("m_textsDialog").GetValue<Component>();
            if (sourceDialog == null) sourceDialog = Traverse.Create(inventoryGui).Field("m_skillsDialog").GetValue<Component>();
            if (sourceDialog == null) return false;

            _canvasButtonTemplate = FindGlobalButtonTemplate();
            _canvasTextTemplate = FindBestTextTemplate(sourceDialog.transform, false);
            _canvasTitleTemplate = FindBestTextTemplate(sourceDialog.transform, true) ?? _canvasTextTemplate;
            if (_canvasButtonTemplate == null || _canvasTextTemplate == null) return false;

            Transform parent = ResolveVanillaWindowParent(sourceDialog.transform);
            _canvasRoot = new GameObject("FiresLeaderboard.CanvasRoot", typeof(RectTransform));
            _canvasRoot.transform.SetParent(parent, false);
            _canvasRootRect = _canvasRoot.GetComponent<RectTransform>();
            _canvasRootRect.localScale = Vector3.one;
            _canvasRootRect.localRotation = Quaternion.identity;
            // Size the window in the parent's own units (the HUD canvas), not screen pixels: CenterRect clamped to Screen.width
            // / height, which shrank it too far on small screens (1024x768) and mixed units everywhere but 1080p. The header's
            // buttons sit right-aligned inside it, so a window that fits the canvas keeps all of them on screen (round 16).
            if (parent is RectTransform parentRect && parentRect.rect.width > 0f && parentRect.rect.height > 0f)
            {
                float w = Mathf.Min(BaseWindowWidth * WindowScale, parentRect.rect.width - 40f);
                float h = Mathf.Min(BaseWindowHeight * WindowScale, parentRect.rect.height - 40f);
                _windowRect = new Rect(_windowRect.x, _windowRect.y, w, h);
            }
            SetCenteredRect(_canvasRootRect, _windowRect.width, _windowRect.height);

            GameObject blocker = new GameObject("Overlay", typeof(RectTransform));
            blocker.transform.SetParent(_canvasRoot.transform, false);
            RectTransform blockerRect = blocker.GetComponent<RectTransform>();
            Stretch(blockerRect, Vector2.zero, Vector2.one);
            AddImage(blocker, OverlayColor);
            blocker.transform.SetAsFirstSibling();

            _canvasContentRoot = new GameObject("Content", typeof(RectTransform));
            _canvasContentRoot.transform.SetParent(_canvasRoot.transform, false);
            Stretch(_canvasContentRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

            _canvasRoot.SetActive(false);
            _canvasReady = true;
            return true;
        }

        private void SyncCanvasRootLayout(bool visible)
        {
            if (_canvasRootRect == null) return;
            if (!visible) { SetCanvasRootVisible(false); return; }

            SetCenteredRect(_canvasRootRect, _windowRect.width, _windowRect.height);
            _canvasRootRect.localScale = Vector3.one;
            _canvasRootRect.localRotation = Quaternion.identity;
            RectTransform contentRect = _canvasContentRoot != null ? _canvasContentRoot.GetComponent<RectTransform>() : null;
            if (contentRect != null) Stretch(contentRect, Vector2.zero, Vector2.one);
            SetCanvasRootVisible(true);
        }

        private void SetCanvasRootVisible(bool visible)
        {
            if (_canvasRoot == null) return;
            if (_canvasRoot.activeSelf != visible) _canvasRoot.SetActive(visible);
        }

        private void RefreshCanvasViewIfNeeded()
        {
            if (!IsVisible || !EnsureCanvasUiReady()) return;
            string signature = GetCanvasSignature();
            if (!string.Equals(signature, _canvasSignature, StringComparison.Ordinal)) RebuildCanvasView(true);
        }

        private void RebuildCanvasView(bool force)
        {
            if (!EnsureCanvasUiReady()) return;
            string signature = GetCanvasSignature();
            if (!force && string.Equals(signature, _canvasSignature, StringComparison.Ordinal)) return;
            ClearCanvasContent();
            BuildCanvasWindow();
            _canvasSignature = signature;
        }

        private string GetCanvasSignature()
        {
            var rows = LeaderboardBoard.Rows;
            int count = rows != null ? rows.Count : 0;
            string fingerprint = rows != null
                ? string.Join(",", rows.Take(12).Select(r => (r?.PlayerName ?? string.Empty) + ":" + (r?.Score ?? 0)))
                : string.Empty;
            string selected = _selectedPlayer != null ? (_selectedPlayer.PlayerId ?? _selectedPlayer.PlayerName ?? string.Empty) : string.Empty;
            return (_localViewMode ?? string.Empty) + "|" + count + "|" + fingerprint + "|" + _adapter.SortField + "|" + (_adapter.SortAscending ? "1" : "0")
                + "|" + selected + "|" + _detailAchievementsPage + "|" + _progressAchievementsPage + "|" + _achievementsScrollOffset
                + "|" + LeaderboardBoard.ViewedSeason + "|" + LeaderboardBoard.AvailableSeasons.Count;
        }

        private void ClearCanvasContent()
        {
            if (_canvasContentRoot == null) return;
            for (int i = _canvasContentRoot.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = _canvasContentRoot.transform.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        // ---- Window assembly ----------------------------------------------------------------------------

        private void BuildCanvasWindow()
        {
            float margin = 18f;
            float width = _windowRect.width - (margin * 2f);
            float footerY = _windowRect.height - 54f;

            GameObject headerPanel = CreatePanelBox(_canvasContentRoot.transform, "Leaderboard.Header", new Rect(margin, 16f, width, 68f), LbWell, LbEdge);
            // The three view buttons are laid out from the header's RIGHT edge (16 px margin, 16 px gaps), and the title and
            // subtitle take only the room left of them, so nothing overlaps or runs off at any window width.
            float topButtonY = 13f;
            float rankingX = width - 16f - 120f;
            float progressionX = rankingX - 16f - 130f;
            float achievementsX = progressionX - 16f - 150f;
            float textWidth = Mathf.Max(120f, achievementsX - 18f - 16f);
            CreateTextAt(headerPanel.transform, "Leaderboard", new Rect(18f, 8f, Mathf.Min(520f, textWidth), 28f), 28f, LbGold, TextAnchor.MiddleLeft, false);
            CreateTextAt(headerPanel.transform, BuildSubtitle(), new Rect(18f, 36f, Mathf.Min(760f, textWidth), 18f), 16f, LbMuted, TextAnchor.MiddleLeft, false);
            bool rankingActive = IsRankingMode();
            bool achievementsActive = string.Equals(_localViewMode, ViewAchievements, StringComparison.OrdinalIgnoreCase);
            bool progressionActive = string.Equals(_localViewMode, ViewProgression, StringComparison.OrdinalIgnoreCase);

            CreateActionButtonAt(headerPanel.transform, "Achievements", new Rect(achievementsX, topButtonY, 150f, 28f), delegate
            {
                _selectedPlayer = null;
                _localViewMode = ViewAchievements;
                _detailAchievementsPage = 1;
                _progressAchievementsPage = 1;
                _achievementsScrollOffset = 0;
                _adapter.RequestBoard();
                RebuildCanvasView(true);
            }, true, false, 16f, achievementsActive);

            CreateActionButtonAt(headerPanel.transform, "Progression", new Rect(progressionX, topButtonY, 130f, 28f), delegate
            {
                _selectedPlayer = null;
                _localViewMode = ViewProgression;
                _progressAchievementsPage = 1;
                _adapter.RequestBoard();
                RebuildCanvasView(true);
            }, true, false, 16f, progressionActive);

            CreateActionButtonAt(headerPanel.transform, "Ranking", new Rect(rankingX, topButtonY, 120f, 28f), delegate
            {
                _selectedPlayer = null;
                _localViewMode = ViewRanking;
                _detailAchievementsPage = 1;
                _adapter.RequestBoard();
                RebuildCanvasView(true);
            }, true, false, 16f, rankingActive);

            // Season selector: ◀ Overall | Season 1 | … | Season N (current) ▶. Overall is the all-time aggregate
            // and always present, so the selector always shows. Picking a season re-requests the whole board.
            // Hidden in memorial mode — a tombstone always shows the player's whole-life (overall) record.
            var seasons = LeaderboardBoard.AvailableSeasons;
            if (!_memorialMode && seasons != null && seasons.Count > 1)
            {
                int viewed = LeaderboardBoard.ViewedSeason;
                float selW = 256f;
                float selX = achievementsX - selW - 24f;
                CreateActionButtonAt(headerPanel.transform, "◀", new Rect(selX, topButtonY, 28f, 28f), delegate
                {
                    var av = LeaderboardBoard.AvailableSeasons;
                    int i = av.IndexOf(LeaderboardBoard.ViewedSeason);
                    if (i > 0) _adapter.RequestBoard(av[i - 1]);
                }, true, false, 15f, false);
                string seasonLabel = viewed == LeaderboardBoard.OverallSeason
                    ? "Overall"
                    : "Season " + viewed + (viewed == LeaderboardBoard.CurrentSeason ? " (current)" : "");
                CreateTextAt(headerPanel.transform, seasonLabel, new Rect(selX + 32f, topButtonY, selW - 64f, 28f), 14f, LbGold, TextAnchor.MiddleCenter, false);
                CreateActionButtonAt(headerPanel.transform, "▶", new Rect(selX + selW - 28f, topButtonY, 28f, 28f), delegate
                {
                    var av = LeaderboardBoard.AvailableSeasons;
                    int i = av.IndexOf(LeaderboardBoard.ViewedSeason);
                    if (i >= 0 && i < av.Count - 1) _adapter.RequestBoard(av[i + 1]);
                }, true, false, 15f, false);
            }

            float blockY = 98f;
            float blockHeight = footerY - blockY - 8f;
            GameObject bodyPanel = CreatePanelBox(_canvasContentRoot.transform, "Leaderboard.Body", new Rect(margin, blockY, width, blockHeight), LbWell, LbEdge);
            CreateTextAt(bodyPanel.transform, BuildDynamicBlockTitle(), new Rect(16f, 8f, 360f, 22f), 18f, LbMuted, TextAnchor.MiddleLeft, false);

            Rect innerRect = new Rect(10f, 42f, width - 20f, blockHeight - 56f);
            BuildCanvasDynamicBlock(bodyPanel.transform, innerRect);

            GameObject footerPanel = CreatePanelBox(_canvasContentRoot.transform, "Leaderboard.Footer", new Rect(margin, footerY, width, 36f), LbWell, LbEdge);
            CreateTextAt(footerPanel.transform, BuildFooter(), new Rect(16f, 8f, width - 420f, 20f), 15f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateActionButtonAt(footerPanel.transform, "Close", new Rect(width - 96f, 4f, 80f, 24f), delegate { Close(); }, true, false, 16f, false);
        }

        private bool IsRankingMode()
        {
            return string.Equals(_localViewMode, ViewRanking, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_localViewMode, DetailViewMode, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildSubtitle()
        {
            int count = LeaderboardBoard.Rows != null ? LeaderboardBoard.Rows.Count : 0;
            return count > 0 ? count + " ranked vikings" : "Awaiting board data";
        }

        private string BuildDynamicBlockTitle()
        {
            if (string.Equals(_localViewMode, DetailViewMode, StringComparison.OrdinalIgnoreCase)) return "Player details";
            if (string.Equals(_localViewMode, ViewProgression, StringComparison.OrdinalIgnoreCase)) return "Progression";
            if (string.Equals(_localViewMode, ViewAchievements, StringComparison.OrdinalIgnoreCase)) return "Achievements";
            return "Ranking";
        }

        private string BuildFooter()
        {
            return "Press L to close. Climb the board by killing, building, crafting, and exploring.";
        }

        private void BuildCanvasDynamicBlock(Transform parent, Rect rect)
        {
            if (string.Equals(_localViewMode, DetailViewMode, StringComparison.OrdinalIgnoreCase))
            {
                BuildCanvasDetailView(parent, rect);
                return;
            }
            if (string.Equals(_localViewMode, ViewProgression, StringComparison.OrdinalIgnoreCase))
            {
                BuildCanvasProgressionView(parent, rect);
                return;
            }
            if (string.Equals(_localViewMode, ViewAchievements, StringComparison.OrdinalIgnoreCase))
            {
                BuildCanvasAchievementsView(parent, rect);
                return;
            }
            BuildCanvasRankingView(parent, rect);
        }

        // ---- Ranking view (Fires 8-column set + Score) --------------------------------------------------

        private void BuildCanvasRankingView(Transform parent, Rect rect)
        {
            List<FiresLeaderboardRanking> players = _adapter.GetRankings();
            if (players.Count == 0)
            {
                CreateTextAt(parent, "No leaderboard data yet.", rect, 18f, LbText, TextAnchor.UpperLeft, false);
                return;
            }

            GameObject listPanel = CreatePanelBox(parent, "Leaderboard.Ranking.List", new Rect(rect.x, rect.y, rect.width, rect.height), LbWell, LbEdge);
            float contentWidth = rect.width - 16f;
            float[] cols = GetCanvasRankingColumnWidths(contentWidth - 16f);
            string[] headers = { "#", "Player", "Achv", "Monsters", "Quests", "Structures", "Deaths", "PvP", "Map%", "Score" };
            FiresRankingSortField[] fields =
            {
                FiresRankingSortField.Rank,
                FiresRankingSortField.PlayerName,
                FiresRankingSortField.AchievementCount,
                FiresRankingSortField.Monsters,
                FiresRankingSortField.Quests,
                FiresRankingSortField.Structures,
                FiresRankingSortField.Deaths,
                FiresRankingSortField.PlayersKilled,
                FiresRankingSortField.MapExplored,
                FiresRankingSortField.Score
            };

            float headerHeight = 30f;
            float headerY = 6f;
            float headerX = 8f;
            for (int i = 0; i < headers.Length; i++)
            {
                Rect headerRect = new Rect(headerX, headerY, cols[i], headerHeight);
                bool active = _adapter.SortField == fields[i];
                GameObject headerCell = CreatePanelBox(listPanel.transform, "Leaderboard.Ranking.Header." + i, headerRect, active ? LbCellDone : LbWell, LbEdge);
                string label = active ? headers[i] + (_adapter.SortAscending ? " ▲" : " ▼") : headers[i];
                // Numeric columns are CENTER-aligned (header word + value share the column centerline) — right-aligned
                // here read as misaligned because the wide header word sits left while the narrow number hugs the right.
                // # and Player stay left-aligned. The row cells below use the identical anchor per column.
                TextAnchor anchor = i <= 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
                CreateTextAt(headerCell.transform, label, new Rect(i <= 1 ? 8f : 4f, 4f, cols[i] - 12f, headerHeight - 8f), 15f, active ? LbGold : LbMuted, anchor, false);
                FiresRankingSortField capturedField = fields[i];
                CreateTransparentButtonOverlay(listPanel.transform, headerRect, delegate
                {
                    _adapter.ToggleSort(capturedField);
                    RebuildCanvasView(true);
                });
                headerX += cols[i];
            }

            float rowHeight = 48f;
            float rowGap = 6f;
            float rowY = headerY + headerHeight + 8f;
            int maxRows = Mathf.Max(1, Mathf.FloorToInt((rect.height - rowY - 8f + rowGap) / (rowHeight + rowGap)));
            maxRows = Mathf.Min(maxRows, players.Count);
            for (int i = 0; i < maxRows; i++)
            {
                FiresLeaderboardRanking player = players[i];
                bool selected = _selectedPlayer != null && string.Equals(_selectedPlayer.PlayerName, player.PlayerName, StringComparison.OrdinalIgnoreCase);
                Color fill = selected ? LbCellDone : LbWell;
                float rankingRowWidth = contentWidth - 44f;
                GameObject rowPanel = CreatePanelBox(listPanel.transform, "Leaderboard.Ranking.Row." + i, new Rect(8f, rowY, rankingRowWidth, rowHeight), fill, LbEdge);
                float x = 0f;
                float cellY = 13f;
                float cellHeight = 22f;
                CreateTextAt(rowPanel.transform, "#" + player.Rank, new Rect(x + 8f, cellY, cols[0] - 16f, cellHeight), 14f, LbText, TextAnchor.MiddleLeft, false); x += cols[0];
                CreateTextAt(rowPanel.transform, player.PlayerName ?? "-", new Rect(x + 8f, cellY, cols[1] - 16f, cellHeight), 14f, LbText, TextAnchor.MiddleLeft, false); x += cols[1];
                CreateTextAt(rowPanel.transform, player.AchievementCount.ToString(), new Rect(x + 4f, cellY, cols[2] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[2];
                CreateTextAt(rowPanel.transform, player.Monsters.ToString(), new Rect(x + 4f, cellY, cols[3] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[3];
                CreateTextAt(rowPanel.transform, "0", new Rect(x + 4f, cellY, cols[4] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[4];
                CreateTextAt(rowPanel.transform, player.Structures.ToString(), new Rect(x + 4f, cellY, cols[5] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[5];
                CreateTextAt(rowPanel.transform, player.Deaths.ToString(), new Rect(x + 4f, cellY, cols[6] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[6];
                CreateTextAt(rowPanel.transform, player.PlayersKilled.ToString(), new Rect(x + 4f, cellY, cols[7] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[7];
                CreateTextAt(rowPanel.transform, (player.MapExplored * 100f).ToString("0.##") + "%", new Rect(x + 4f, cellY, cols[8] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false); x += cols[8];
                CreateTextAt(rowPanel.transform, player.Score.ToString(), new Rect(x + 4f, cellY, cols[9] - 12f, cellHeight), 14f, LbText, TextAnchor.MiddleCenter, false);

                FiresLeaderboardRanking clicked = player;
                CreateTransparentButtonOverlay(listPanel.transform, new Rect(8f, rowY, rankingRowWidth, rowHeight), delegate
                {
                    _selectedPlayer = clicked;
                    _localViewMode = DetailViewMode;
                    _detailAchievementsPage = 1;
                    RebuildCanvasView(true);
                });
                rowY += rowHeight + rowGap;
            }
        }

        // ---- Achievements view (retargeted to AchievementProfileManager + LeaderboardTracker) ------------

        private void BuildCanvasAchievementsView(Transform parent, Rect rect)
        {
            var defs = AchievementProfileManager.GetAchievements(AchievementProfile);
            if (defs == null || defs.Count == 0)
            {
                CreateTextAt(parent, "No achievements defined for profile '" + AchievementProfile + "'. Drop a .cfg into config\\firesnpcs\\achievement\\.", rect, 16f, LbText, TextAnchor.UpperLeft, true);
                return;
            }

            GameObject listPanel = CreatePanelBox(parent, "Leaderboard.Achievements.List", new Rect(rect.x, rect.y, rect.width, rect.height), LbWell, LbEdge);
            float contentWidth = rect.width - 16f;
            float[] cols = GetCanvasAchievementColumnWidths(contentWidth - 16f);
            float span = SumColumns(cols);
            string[] headers = { "Achievement", "Score", "Description", "Progress" };

            float headerHeight = 30f;
            float headerY = 6f;
            float startX = 8f;
            float headerX = startX;
            for (int i = 0; i < headers.Length; i++)
            {
                bool rightAlign = i == 1;
                float pad = rightAlign ? 4f : 8f;
                GameObject headerCell = CreateRowStrip(listPanel.transform, "Leaderboard.Achievements.Header." + i, headerX, headerY, cols[i], headerHeight, LbCell, LbEdge);
                CreateTextAt(headerCell.transform, headers[i], new Rect(pad, 4f, cols[i] - pad - 4f, headerHeight - 8f), 15f, LbMuted, rightAlign ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft, false);
                headerX += cols[i];
            }

            string playerName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "Local";
            float rowHeight = 64f;
            float rowGap = 6f;
            float rowY = headerY + headerHeight + 8f;
            int rowsThatFit = Mathf.Max(1, Mathf.FloorToInt((rect.height - rowY - 8f + rowGap) / (rowHeight + rowGap)));

            // Window the list by the mouse-wheel scroll offset. Clamp the offset to what's actually scrollable and
            // publish the max so HandleAchievementsScroll() (in Update) knows the bounds.
            _achievementsMaxScroll = Mathf.Max(0, defs.Count - rowsThatFit);
            _achievementsScrollOffset = Mathf.Clamp(_achievementsScrollOffset, 0, _achievementsMaxScroll);
            int start = _achievementsScrollOffset;
            int end = Mathf.Min(defs.Count, start + rowsThatFit);

            // Position indicator on the rightmost header cell: "start–end / total" + a scroll hint when there's more.
            if (defs.Count > rowsThatFit)
            {
                string range = (start + 1) + "–" + end + " / " + defs.Count + "   (scroll)";
                CreateTextAt(listPanel.transform, range, new Rect(headerX - cols[headers.Length - 1] - 4f, headerY + 4f, cols[headers.Length - 1] - 4f, headerHeight - 8f), 13f, LbMuted, TextAnchor.MiddleRight, false);
            }

            float colName = 0f;
            float colScore = cols[0];
            float colDesc = cols[0] + cols[1];
            float colProgress = cols[0] + cols[1] + cols[2];
            for (int i = start; i < end; i++)
            {
                AchievementDef def = defs[i];
                int progress = LeaderboardTracker.GetProgress(playerName, def.TriggerType, def.Prefab);
                bool done = progress >= def.MinAmount;
                Color fill = done ? LbCellDone : (i % 2 == 0 ? LbWell : RowAltFill);
                GameObject rowStrip = CreateRowStrip(listPanel.transform, "Leaderboard.Achievements.Row." + i, startX, rowY, span, rowHeight, fill, LbEdge);

                CreateTextAt(rowStrip.transform, (done ? "✔ " : string.Empty) + (def.Name ?? "-"), new Rect(colName + 8f, 10f, cols[0] - 14f, 22f), 15f, done ? new Color(0.50f, 0.85f, 0.40f) : LbGold, TextAnchor.MiddleLeft, false);
                CreateTextAt(rowStrip.transform, def.Score.ToString(), new Rect(colScore + 4f, 10f, cols[1] - 8f, 22f), 15f, LbText, TextAnchor.MiddleRight, false);
                CreateTextAt(rowStrip.transform, def.Description ?? string.Empty, new Rect(colDesc + 8f, 8f, cols[2] - 14f, rowHeight - 16f), 13f, LbMuted, TextAnchor.UpperLeft, true);
                BuildCanvasAchievementProgress(rowStrip.transform, new Rect(colProgress + 8f, 8f, cols[3] - 16f, rowHeight - 12f), progress, def.MinAmount, done);
                rowY += rowHeight + rowGap;
            }
        }

        private void BuildCanvasAchievementProgress(Transform parent, Rect rect, int progress, int threshold, bool completed)
        {
            float ratio = threshold <= 0 ? (completed ? 1f : 0f) : Mathf.Clamp01((float)progress / threshold);
            int displayValue = threshold > 0 ? Math.Min(progress, threshold) : progress;
            string progressLabel = threshold <= 0
                ? (completed ? "Completed" : "No tracking")
                : string.Format(CultureInfo.InvariantCulture, "{0}/{1}", displayValue, threshold);
            string percentLabel = completed ? "Done" : string.Format(CultureInfo.InvariantCulture, "{0:0}%", ratio * 100f);

            CreateTextAt(parent, progressLabel, new Rect(rect.x, rect.y, rect.width, 14f), 12f, completed ? LbGold : LbText, TextAnchor.MiddleLeft, false);

            float barY = rect.y + 18f;
            float percentWidth = 48f;
            float percentGap = 8f;
            float barWidth = Mathf.Min(rect.width - percentWidth - percentGap, 180f);
            barWidth = Mathf.Max(110f, barWidth);
            Rect trackRect = new Rect(rect.x, barY, barWidth, 12f);
            CreateCanvasSolidRect(parent, "Leaderboard.Achievements.Progress.Track", trackRect, LbCell);
            CreateCanvasOutlineRect(parent, "Leaderboard.Achievements.Progress.Border", trackRect, LbEdge);
            if (ratio > 0f)
            {
                Rect fillRect = new Rect(trackRect.x + 1f, trackRect.y + 1f, Mathf.Max(0f, (trackRect.width - 2f) * ratio), trackRect.height - 2f);
                CreateCanvasSolidRect(parent, "Leaderboard.Achievements.Progress.Fill", fillRect, completed ? new Color(0.30f, 0.62f, 0.25f, 0.95f) : LbCell);
            }

            CreateTextAt(parent, percentLabel, new Rect(trackRect.xMax + percentGap, barY - 1f, percentWidth, 14f), 12f, completed ? LbGold : LbMuted, TextAnchor.MiddleRight, false);
        }

        // ---- Column widths (verbatim from TDV) ----------------------------------------------------------

        private static float[] GetCanvasRankingColumnWidths(float availableWidth)
        {
            float[] weights = { 0.07f, 0.17f, 0.06f, 0.10f, 0.10f, 0.11f, 0.08f, 0.10f, 0.08f, 0.09f };
            float[] columns = new float[weights.Length];
            float totalWeight = weights.Sum();
            float used = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                columns[i] = Mathf.Max(52f, availableWidth * (weights[i] / totalWeight));
                used += columns[i];
            }

            float delta = availableWidth - used;
            columns[columns.Length - 1] += delta;
            return columns;
        }

        private static float[] GetCanvasAchievementColumnWidths(float availableWidth)
        {
            float[] weights = { 0.30f, 0.09f, 0.31f, 0.30f };
            return BuildWeightedColumns(availableWidth, weights, 72f);
        }

        private static float[] BuildWeightedColumns(float availableWidth, float[] weights, float minWidth)
        {
            float[] columns = new float[weights.Length];
            float totalWeight = weights.Sum();
            float used = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                columns[i] = Mathf.Max(minWidth, availableWidth * (weights[i] / totalWeight));
                used += columns[i];
            }
            columns[columns.Length - 1] += availableWidth - used;
            return columns;
        }

        // ---- Background swap: parchment panel box (replaces TDV vanilla-Bkg clone + flat fallback) -------

        private static GameObject CreatePanelBox(Transform parent, string name, Rect rect, Color fillColor, Color borderColor)
        {
            var outer = new GameObject(name, typeof(RectTransform));
            outer.transform.SetParent(parent, false);
            SetRect(outer.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);

            Component img = outer.GetComponent(CanvasImageType) ?? outer.AddComponent(CanvasImageType);
            Sprite bg = ResolveConfiguredBackground();
            if (bg != null)
            {
                SetProperty(img, "sprite", bg);
                SetTypeSliced(img);
                SetProperty(img, "color", Color.white);
            }
            else
            {
                SetProperty(img, "color", fillColor);
            }
            SetBoolProperty(img, "raycastTarget", false);

            // Draw a parchment-edge frame when painting a solid fill (a configured PNG brings its own border art).
            if (bg == null && borderColor.a > 0f)
                CreateCanvasOutlineRect(outer.transform, name + ".Border", new Rect(0f, 0f, rect.width, rect.height), borderColor);

            var content = new GameObject(name + ".Content", typeof(RectTransform));
            content.transform.SetParent(outer.transform, false);
            SetRect(content.GetComponent<RectTransform>(), 8f, 8f, Mathf.Max(0f, rect.width - 16f), Mathf.Max(0f, rect.height - 16f));
            return content;
        }

        private static bool _backgroundResolved;
        private static Sprite _backgroundSprite;

        // Optional user-supplied parchment PNG (config/FiresUIBackgrounds/), gated on the leaderboard config toggle —
        // mirrors LeaderboardPlayerPanel.ApplyConfigurableBackground. When off/missing, panels paint opaque ParchmentPanel.
        private static Sprite ResolveConfiguredBackground()
        {
            if (_backgroundResolved) return _backgroundSprite;
            _backgroundResolved = true;
            var cm = global::FiresRPGmaker.Managers.ConfigManager.Instance;
            if (cm?.configLeaderboardBackgroundToggle == null || !cm.configLeaderboardBackgroundToggle.Value) return null;
            _backgroundSprite = FiresCore.UI.UIBackgroundLoader.Load(cm.configLeaderboardBackground?.Value);
            return _backgroundSprite;
        }

        private static void SetTypeSliced(Component image)
        {
            if (image == null) return;
            PropertyInfo typeProperty = image.GetType().GetProperty("type", BindingFlags.Public | BindingFlags.Instance);
            if (typeProperty == null || !typeProperty.CanWrite) return;
            try
            {
                object sliced = Enum.ToObject(typeProperty.PropertyType, 1); // UnityEngine.UI.Image.Type.Sliced
                typeProperty.SetValue(image, sliced, null);
            }
            catch { }
        }

        // ---- Buttons / text / primitives (ported reflection layer) --------------------------------------

        private GameObject CreateActionButtonAt(Transform parent, string label, Rect rect, Action action, bool enabled, bool leftAlign, float fontSize, bool active)
        {
            // Built directly (not from a cloned vanilla template): one full-size parchment fill + a centered label +
            // a transparent click overlay, all sharing the exact same rect, so the background and the label can never
            // drift apart (the template-clone approach let them diverge — the cause of the misaligned tab labels).
            GameObject root = new GameObject("LeaderboardButton_" + label.Replace("\n", " "), typeof(RectTransform));
            root.transform.SetParent(parent, false);
            SetRect(root.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);

            Component bg = root.GetComponent(CanvasImageType) ?? root.AddComponent(CanvasImageType);
            SetProperty(bg, "sprite", null);
            SetProperty(bg, "color", active ? LbCellDone : LbCell);
            SetBoolProperty(bg, "raycastTarget", false);
            CreateCanvasOutlineRect(root.transform, "LeaderboardButton.Border", new Rect(0f, 0f, rect.width, rect.height), LbEdge);

            float padL = leftAlign ? 10f : 6f;
            CreateTextAt(root.transform, label, new Rect(padL, 0f, Mathf.Max(0f, rect.width - padL - 6f), rect.height), fontSize,
                enabled ? LbMuted : LbMuted, leftAlign ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, false);

            if (enabled && action != null) CreateTransparentButtonOverlay(root.transform, new Rect(0f, 0f, rect.width, rect.height), action);
            return root;
        }

        private void NormalizeButtonVisuals(GameObject buttonObject, bool active)
        {
            if (buttonObject == null) return;
            foreach (Transform child in buttonObject.transform)
            {
                if (child != null && child.gameObject != null) child.gameObject.SetActive(true);
            }
            if (CanvasImageType != null)
            {
                Component image = buttonObject.GetComponent(CanvasImageType);
                if (image != null)
                {
                    SetProperty(image, "sprite", null);
                    SetProperty(image, "color", active ? LbCellDone : LbCell);
                }
            }
        }

        private void SetButtonLabel(GameObject buttonObject, string label, float fontSize, bool leftAlign, bool active)
        {
            if (buttonObject == null) return;

            Component originalText = FindFirstText(buttonObject.transform);
            if (originalText != null)
            {
                SetText(originalText, string.Empty);
                DisableTextAutoSizing(originalText);
                SetTextRaycast(originalText, false);
            }

            Transform previousOverlay = buttonObject.transform.Find("LeaderboardOverlayLabel");
            if (previousOverlay != null) UnityEngine.Object.DestroyImmediate(previousOverlay.gameObject);

            GameObject template = _canvasTitleTemplate != null ? _canvasTitleTemplate : _canvasTextTemplate;
            GameObject overlayLabel = CloneTextObject(template, "LeaderboardOverlayLabel", buttonObject.transform);
            if (overlayLabel == null) return;

            overlayLabel.SetActive(true);
            RectTransform overlayRect = overlayLabel.GetComponent<RectTransform>();
            if (overlayRect != null)
            {
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.pivot = new Vector2(0.5f, 0.5f);
                overlayRect.offsetMin = new Vector2(leftAlign ? 12f : 4f, 0f);
                overlayRect.offsetMax = new Vector2(-4f, 0f);
                overlayRect.anchoredPosition = Vector2.zero;
                overlayRect.localScale = Vector3.one;
            }

            Component overlayText = FindFirstText(overlayLabel.transform);
            if (overlayText == null) return;

            SetText(overlayText, label);
            DisableTextAutoSizing(overlayText);
            SetTextRaycast(overlayText, false);
            SetFontSize(overlayText, fontSize);
            SetTextAlignment(overlayText, leftAlign ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
            ApplyTextColor(overlayText, LbMuted);
            SetTextWrapping(overlayText, false);

            if (leftAlign)
            {
                ExpandTextRect(overlayText, true);
            }
        }

        private void CreateTextAt(Transform parent, string text, Rect rect, float fontSize, Color color, TextAnchor anchor, bool wrap)
        {
            // Always use the clean body template, never the decorative title template. The title font's material carries
            // a baked contrasting outline (gold-on-dark in vanilla) that reads as hard-on-the-eyes dark-text-with-a-halo
            // on our light parchment. Size + color give headers enough distinction without it.
            GameObject template = _canvasTextTemplate;
            if (template == null) return;

            // The shared text material carries a baked dark outline. On a dark fill that outline reads as a blurry
            // halo (the body-text problem); on a light cream fill it reads crisp — exactly why the buttons look good.
            // Promote the standard dark parchment tones to the cream button-ink so every label gets that crisp
            // "light fill + dark outline" treatment. Meaningful accent colors (e.g. the done-green) pass through.
            color = ReadableInk(color);

            GameObject textObject = CloneTextObject(template, "LeaderboardText", parent);
            RectTransform rectTransform = textObject.GetComponent<RectTransform>();
            if (rectTransform != null) SetRect(rectTransform, rect.x, rect.y, rect.width, rect.height);

            Component textComponent = FindFirstText(textObject.transform);
            SetText(textComponent, text);
            SetFontSize(textComponent, fontSize);
            ApplyTextColor(textComponent, color);
            SetTextAlignment(textComponent, anchor);
            SetTextWrapping(textComponent, wrap);
            SetTextRaycast(textComponent, false);
        }

        private void CreateTransparentButtonOverlay(Transform parent, Rect rect, Action action)
        {
            if (parent == null || _canvasButtonTemplate == null) return;

            GameObject buttonObject = UnityEngine.Object.Instantiate(_canvasButtonTemplate, parent, false);
            // The clickable Button carries its button's name (e.g. "LeaderboardButton_Close"), so lookups by name find it.
            buttonObject.name = parent.name.StartsWith("LeaderboardButton_") ? parent.name : "Leaderboard.TransparentOverlay";
            buttonObject.SetActive(true);
            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            if (rectTransform != null) SetRect(rectTransform, rect.x, rect.y, rect.width, rect.height);

            if (CanvasImageType != null)
            {
                foreach (Component image in buttonObject.GetComponentsInChildren(CanvasImageType, true))
                {
                    SetProperty(image, "sprite", null);
                    SetProperty(image, "color", new Color(1f, 1f, 1f, 0f));
                    SetBoolProperty(image, "raycastTarget", true);
                }
            }

            Component textComponent = FindFirstText(buttonObject.transform);
            if (textComponent != null) SetText(textComponent, string.Empty);

            Component button = CanvasButtonType != null ? buttonObject.GetComponent(CanvasButtonType) : null;
            ClearButtonListeners(button);
            SetButtonInteractable(button, true);
            AddButtonListener(button, action);
        }

        // ---- Layout / image primitives ------------------------------------------------------------------

        private static void SetCenteredRect(RectTransform rect, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (rect == null) return;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetRect(RectTransform rect, float x, float y, float width, float height)
        {
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        private static GameObject CreateCanvasSolidRect(Transform parent, string name, Rect rect, Color color)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            SetRect(gameObject.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);
            AddImage(gameObject, color);
            return gameObject;
        }

        private static void CreateCanvasOutlineRect(Transform parent, string name, Rect rect, Color color)
        {
            CreateCanvasSolidRect(parent, name + ".Top", new Rect(rect.x, rect.y, rect.width, 1f), color);
            CreateCanvasSolidRect(parent, name + ".Bottom", new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), color);
            CreateCanvasSolidRect(parent, name + ".Left", new Rect(rect.x, rect.y, 1f, rect.height), color);
            CreateCanvasSolidRect(parent, name + ".Right", new Rect(rect.x + rect.width - 1f, rect.y, 1f, rect.height), color);
        }

        private static void AddImage(GameObject gameObject, Color color)
        {
            if (gameObject == null || CanvasImageType == null) return;
            Component image = gameObject.GetComponent(CanvasImageType) ?? gameObject.AddComponent(CanvasImageType);
            SetProperty(image, "color", color);
            SetBoolProperty(image, "raycastTarget", false);
        }

        private static float SumColumns(float[] cols)
        {
            float sum = 0f;
            for (int i = 0; i < cols.Length; i++) sum += cols[i];
            return sum;
        }

        // A table header/row strip aligned to the column grid: solid fill + a thin parchment-edge border, with NO
        // content inset (unlike CreatePanelBox), so cells placed at the cumulative column x line up exactly under
        // their headers and stay inside the strip's tint. Add cells directly to the returned object.
        private static GameObject CreateRowStrip(Transform parent, string name, float x, float y, float width, float height, Color fill, Color border)
        {
            GameObject strip = CreateCanvasSolidRect(parent, name, new Rect(x, y, width, height), fill);
            if (border.a > 0f) CreateCanvasOutlineRect(strip.transform, name + ".Border", new Rect(0f, 0f, width, height), border);
            return strip;
        }

        // ---- Template discovery -------------------------------------------------------------------------

        private static Transform ResolveVanillaWindowParent(Transform sourceDialog)
        {
            if (Hud.instance != null)
            {
                var hudRoot = Traverse.Create(Hud.instance).Field("m_rootObject").GetValue<GameObject>();
                if (hudRoot != null && hudRoot.transform.parent != null) return hudRoot.transform.parent;
            }
            return sourceDialog != null && sourceDialog.parent != null ? sourceDialog.parent : sourceDialog;
        }

        private static GameObject CloneTextObject(GameObject template, string name, Transform parent)
        {
            if (template == null) return null;
            GameObject clone = UnityEngine.Object.Instantiate(template, parent, false);
            clone.name = name;
            clone.SetActive(true);
            return clone;
        }

        private static Component FindFirstText(Transform root)
        {
            if (CanvasTmpTextType != null)
            {
                Component tmp = root.GetComponentInChildren(CanvasTmpTextType, true);
                if (tmp != null) return tmp;
            }
            return CanvasTextType != null ? root.GetComponentInChildren(CanvasTextType, true) : null;
        }

        private static GameObject FindGlobalButtonTemplate()
        {
            if (CanvasButtonType == null) return null;
            try
            {
                UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(CanvasButtonType);
                GameObject best = null;
                float bestScore = float.MinValue;
                foreach (UnityEngine.Object obj in objects)
                {
                    Component button = obj as Component;
                    if (button == null || button.gameObject == null) continue;
                    float score = ScoreButtonTemplate(button.gameObject);
                    if (score > bestScore) { bestScore = score; best = button.gameObject; }
                }
                return best;
            }
            catch { return null; }
        }

        private static float ScoreButtonTemplate(GameObject candidate)
        {
            if (candidate == null) return float.MinValue;
            RectTransform rect = candidate.GetComponent<RectTransform>();
            if (rect == null) return float.MinValue;
            Component label = FindFirstText(candidate.transform);
            if (label == null) return float.MinValue;
            float width = Mathf.Abs(rect.rect.width);
            float height = Mathf.Abs(rect.rect.height);
            if (width < 40f || height < 20f) return float.MinValue;

            string text = GetTextValue(label);
            float score = 0f;
            if (!string.IsNullOrWhiteSpace(text))
            {
                score += 20f;
                string normalized = text.Trim().ToLowerInvariant();
                if (normalized.Contains("close")) score += 100f;
                if (normalized.Contains("ok") || normalized.Contains("back")) score += 60f;
            }
            score += Mathf.Clamp(40f - Mathf.Abs(GetFontSize(label) - 18f), 0f, 40f);
            score += Mathf.Clamp(width - 80f, -20f, 80f);
            return score;
        }

        private static GameObject FindBestTextTemplate(Transform root, bool preferLarge)
        {
            Component best = null;
            float bestScore = float.MinValue;
            foreach (Component text in EnumerateTextComponents(root))
            {
                float fontSize = GetFontSize(text);
                float score = preferLarge ? fontSize * 2f : -Mathf.Abs(fontSize - 18f);
                if (score > bestScore) { bestScore = score; best = text; }
            }
            return best != null ? best.gameObject : null;
        }

        private static IEnumerable<Component> EnumerateTextComponents(Transform root)
        {
            if (root == null) yield break;
            if (CanvasTmpTextType != null)
            {
                foreach (Component component in root.GetComponentsInChildren(CanvasTmpTextType, true)) yield return component;
            }
            if (CanvasTextType != null)
            {
                foreach (Component component in root.GetComponentsInChildren(CanvasTextType, true)) yield return component;
            }
        }

        // ---- Reflection setters (ported verbatim) -------------------------------------------------------

        private static void DisableTextAutoSizing(Component text)
        {
            if (text == null) return;
            try
            {
                PropertyInfo autoSizing = text.GetType().GetProperty("enableAutoSizing", BindingFlags.Public | BindingFlags.Instance);
                if (autoSizing != null && autoSizing.CanWrite && autoSizing.PropertyType == typeof(bool)) autoSizing.SetValue(text, false, null);
                PropertyInfo bestFit = text.GetType().GetProperty("resizeTextForBestFit", BindingFlags.Public | BindingFlags.Instance);
                if (bestFit != null && bestFit.CanWrite && bestFit.PropertyType == typeof(bool)) bestFit.SetValue(text, false, null);
            }
            catch { }
        }

        // TDV ExpandTextRect: stretch a left-aligned overlay label to fill its button, with TDV's exact insets.
        private static void ExpandTextRect(Component text, bool leftAlign)
        {
            if (text == null) return;
            RectTransform rect = text.GetComponent<RectTransform>();
            if (rect == null) return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(leftAlign ? 14f : 6f, 2f);
            rect.offsetMax = new Vector2(-6f, -2f);
            rect.anchoredPosition = Vector2.zero;
        }

        private static void SetTextRaycast(Component text, bool enabled)
        {
            if (text == null) return;
            try
            {
                PropertyInfo property = text.GetType().GetProperty("raycastTarget", BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite && property.PropertyType == typeof(bool)) property.SetValue(text, enabled, null);
            }
            catch { }
        }

        private static void SetProperty(Component component, string propertyName, object value)
        {
            if (component == null || string.IsNullOrWhiteSpace(propertyName)) return;
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) property.SetValue(component, value, null);
        }

        private static void SetBoolProperty(Component component, string propertyName, bool value)
        {
            if (component == null) return;
            PropertyInfo property = component.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(bool)) property.SetValue(component, value, null);
        }

        private static void SetButtonInteractable(Component button, bool interactable)
        {
            if (button == null) return;
            PropertyInfo property = button.GetType().GetProperty("interactable", BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite) property.SetValue(button, interactable, null);
        }

        private static void ClearButtonListeners(Component button)
        {
            if (button == null) return;
            object onClick = button.GetType().GetProperty("onClick", BindingFlags.Public | BindingFlags.Instance)?.GetValue(button, null);
            MethodInfo clearMethod = onClick != null ? onClick.GetType().GetMethod("RemoveAllListeners", BindingFlags.Public | BindingFlags.Instance) : null;
            clearMethod?.Invoke(onClick, null);
        }

        private static void AddButtonListener(Component button, Action action)
        {
            if (button == null || action == null || CanvasUnityActionType == null) return;
            object onClick = button.GetType().GetProperty("onClick", BindingFlags.Public | BindingFlags.Instance)?.GetValue(button, null);
            if (onClick == null) return;
            Delegate callback = Delegate.CreateDelegate(CanvasUnityActionType, action.Target, action.Method);
            MethodInfo addMethod = onClick.GetType().GetMethod("AddListener", new[] { CanvasUnityActionType });
            addMethod?.Invoke(onClick, new object[] { callback });
        }

        private static string GetTextValue(Component component)
        {
            if (component == null) return string.Empty;
            PropertyInfo textProperty = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            object value = textProperty != null && textProperty.CanRead ? textProperty.GetValue(component, null) : null;
            return value as string ?? string.Empty;
        }

        private static void SetText(Component component, string text)
        {
            if (component == null) return;
            PropertyInfo textProperty = component.GetType().GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (textProperty != null && textProperty.CanWrite) textProperty.SetValue(component, text ?? string.Empty, null);
        }

        private static void SetTextAlignment(Component component, TextAnchor anchor)
        {
            if (component == null) return;
            PropertyInfo alignmentProperty = component.GetType().GetProperty("alignment", BindingFlags.Public | BindingFlags.Instance);
            if (alignmentProperty == null || !alignmentProperty.CanWrite) return;
            try
            {
                if (alignmentProperty.PropertyType == typeof(TextAnchor))
                {
                    alignmentProperty.SetValue(component, anchor, null);
                }
                else
                {
                    // TMP's `alignment` is TextAlignmentOptions (a horizontal flag | a vertical flag), NOT TextAnchor.
                    // Casting the raw TextAnchor int (e.g. MiddleLeft=3) yields a garbage value, so TMP renders
                    // top-aligned — which made every label sit high. Map to the correct TextAlignmentOptions int.
                    object enumValue = Enum.ToObject(alignmentProperty.PropertyType, ToTmpAlignment(anchor));
                    alignmentProperty.SetValue(component, enumValue, null);
                }
            }
            catch { }
        }

        // TextAnchor -> TMPro.TextAlignmentOptions value. Horizontal: Left=1, Center=2, Right=4. Vertical:
        // Top=256, Middle=512, Bottom=1024. An alignment is (horizontal | vertical), e.g. middle-left = 513.
        private static int ToTmpAlignment(TextAnchor anchor)
        {
            int h, v;
            switch (anchor)
            {
                case TextAnchor.UpperLeft:    h = 1; v = 256; break;
                case TextAnchor.UpperCenter:  h = 2; v = 256; break;
                case TextAnchor.UpperRight:   h = 4; v = 256; break;
                case TextAnchor.MiddleLeft:   h = 1; v = 512; break;
                case TextAnchor.MiddleCenter: h = 2; v = 512; break;
                case TextAnchor.MiddleRight:  h = 4; v = 512; break;
                case TextAnchor.LowerLeft:    h = 1; v = 1024; break;
                case TextAnchor.LowerCenter:  h = 2; v = 1024; break;
                case TextAnchor.LowerRight:   h = 4; v = 1024; break;
                default:                      h = 1; v = 512; break;
            }
            return h | v;
        }

        private static void SetTextWrapping(Component component, bool wrap)
        {
            if (component == null) return;
            PropertyInfo wrapProperty = component.GetType().GetProperty("enableWordWrapping", BindingFlags.Public | BindingFlags.Instance);
            if (wrapProperty != null && wrapProperty.CanWrite && wrapProperty.PropertyType == typeof(bool)) wrapProperty.SetValue(component, wrap, null);

            PropertyInfo horizontalOverflow = component.GetType().GetProperty("horizontalOverflow", BindingFlags.Public | BindingFlags.Instance);
            if (horizontalOverflow != null && horizontalOverflow.CanWrite)
            {
                try { horizontalOverflow.SetValue(component, Enum.ToObject(horizontalOverflow.PropertyType, wrap ? 0 : 1), null); }
                catch { }
            }

            PropertyInfo verticalOverflow = component.GetType().GetProperty("verticalOverflow", BindingFlags.Public | BindingFlags.Instance);
            if (verticalOverflow != null && verticalOverflow.CanWrite)
            {
                try { verticalOverflow.SetValue(component, Enum.ToObject(verticalOverflow.PropertyType, 0), null); }
                catch { }
            }
        }

        private static float GetFontSize(Component component)
        {
            if (component == null) return 14f;
            PropertyInfo property = component.GetType().GetProperty("fontSize", BindingFlags.Public | BindingFlags.Instance);
            object value = property != null && property.CanRead ? property.GetValue(component, null) : null;
            if (value is int intValue) return intValue;
            if (value is float floatValue) return floatValue;
            return 14f;
        }

        private static void SetFontSize(Component component, float size)
        {
            if (component == null) return;
            PropertyInfo property = component.GetType().GetProperty("fontSize", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || !property.CanWrite) return;
            if (property.PropertyType == typeof(int)) property.SetValue(component, Mathf.RoundToInt(size), null);
            else property.SetValue(component, size, null);
        }

        // Promote the three standard dark parchment text tones to the cream button-ink so the baked outline reads
        // crisp instead of muddy. Accent colors (the done-green, custom tints) are left untouched.
        // House style (10-08): the text is light on dark plates now, which reads crisp with the baked dark outline as is.
        private static Color ReadableInk(Color c) => c;

        private static bool ApproximatelyColor(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        private static void ApplyTextColor(object target, Color color)
        {
            if (target == null) return;
            PropertyInfo colorProperty = target.GetType().GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
            if (colorProperty != null && colorProperty.CanWrite) colorProperty.SetValue(target, color, null);
        }
    }
}
