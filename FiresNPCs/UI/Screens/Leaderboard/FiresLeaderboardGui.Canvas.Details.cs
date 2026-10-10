using System;
using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Modules.Marketplace;
using Pal = FiresCore.UI.UIFontConfig.Colors;

namespace FiresRPGmaker.UI.Leaderboard
{
    // Phase A of the faithful TDV leaderboard port: the player-details drill-down, the progression view,
    // the per-player detail wrapper, and the adventure timeline. Every TDV sub-panel (3D-preview box,
    // summary stat grid, skills, favorite weapon, completed-achievements pager, milestone timeline) is
    // present and positioned at TDV's exact rects. Panels showing data Fires does not yet capture (skills,
    // favorite weapon, completed achievements, milestones, the live preview render) render a positioned
    // "pending next phase" placeholder — the box is never omitted.
    //
    // Coordinate note: TDV's CreatePanelBox returns the panel ROOT (children placed in root space, origin 0,0,
    // usable size = full panel WxH). Fires' CreatePanelBox returns an inset CONTENT child (origin = panel+8,+8,
    // usable size = panelW-16 x panelH-16). To land children at TDV's exact absolute positions we therefore
    // (a) subtract the 8px content inset from every TDV child x/y offset, and (b) treat usable inner width as
    // panelW-16 / inner height as panelH-16. The PanelInner helpers below capture this once per panel.
    public sealed partial class FiresLeaderboardGui
    {
        // TDV's PlayerCustomView3D weapon-icon scale (40 * 1.10 = 44px). Mirrored for parity.
        private const float PreferredWeaponPreviewScale = 1.10f;

        // ---- Progression / detail wrappers --------------------------------------------------------------

        // TDV BuildCanvasProgressionView: the LOCAL player's adventure timeline + their own details block (no Back).
        private void BuildCanvasProgressionView(Transform parent, Rect rect)
        {
            FiresLeaderboardRanking local = ResolveLocalPlayer();
            if (local == null)
            {
                CreateTextAt(parent, "No local progression yet — your stats will appear once the board has you.", rect, 18f, LbText, TextAnchor.UpperLeft, true);
                return;
            }

            float adventureHeight = 258f;
            float adventureYOffset = -6f;
            float detailsGap = 8f;
            float adventureWidth = Mathf.Max(200f, rect.width - 156f);
            float adventureX = rect.x + Mathf.Max(0f, (rect.width - adventureWidth) * 0.5f);
            GameObject adventurePanel = CreatePanelBox(parent, "Leaderboard.Progression.Adventure", new Rect(adventureX, rect.y + adventureYOffset, adventureWidth, adventureHeight), LbWell, LbEdge);
            CreateTextAt(adventurePanel.transform, "Adventure", new Rect(12f, 8f, adventureWidth - 24f, 22f), 20f, LbText, TextAnchor.MiddleLeft, false);
            BuildTimelineGrid(adventurePanel.transform, new Rect(12f, 36f, adventureWidth - 24f, adventureHeight - 44f), local.Milestones);

            Rect detailsRect = new Rect(rect.x, rect.y + adventureYOffset + adventureHeight + detailsGap, rect.width, rect.height - adventureHeight - detailsGap - adventureYOffset);
            FiresLeaderboardRanking previous = _selectedPlayer;
            _selectedPlayer = local;
            BuildCanvasPlayerDetails(parent, detailsRect, false);
            _selectedPlayer = previous;
        }

        // TDV BuildCanvasDetailView: the SELECTED player's adventure timeline + their details block (with Back).
        private void BuildCanvasDetailView(Transform parent, Rect rect)
        {
            float adventureHeight = 258f;
            float adventureYOffset = -6f;
            float detailsGap = 8f;
            float adventureWidth = Mathf.Max(200f, rect.width - 156f);
            float adventureX = rect.x + Mathf.Max(0f, (rect.width - adventureWidth) * 0.5f);
            GameObject adventurePanel = CreatePanelBox(parent, "Leaderboard.Detail.Adventure", new Rect(adventureX, rect.y + adventureYOffset, adventureWidth, adventureHeight), LbWell, LbEdge);
            CreateTextAt(adventurePanel.transform, "Adventure", new Rect(12f, 8f, adventureWidth - 24f, 22f), 20f, LbText, TextAnchor.MiddleLeft, false);
            BuildTimelineGrid(adventurePanel.transform, new Rect(12f, 36f, adventureWidth - 24f, adventureHeight - 44f), _selectedPlayer?.Milestones);

            BuildCanvasPlayerDetails(parent, new Rect(rect.x, rect.y + adventureYOffset + adventureHeight + detailsGap, rect.width, rect.height - adventureHeight - detailsGap - adventureYOffset), true);
        }

        // ---- Player-details renderer (TDV BuildCanvasPlayerDetails) --------------------------------------

        private void BuildCanvasPlayerDetails(Transform parent, Rect rect, bool showBackButton)
        {
            FiresLeaderboardRanking player = _selectedPlayer;
            if (player == null)
            {
                return;
            }

            if (showBackButton)
            {
                CreateActionButtonAt(parent, "Back", new Rect(rect.x, rect.y, 84f, 24f), delegate
                {
                    _localViewMode = ViewRanking;
                    _detailAchievementsPage = 1;
                    RebuildCanvasView(true);
                }, true, false, 16f, false);
            }

            float gap = 16f;
            float leftWidth = Mathf.Max(380f, rect.width * 0.55f);
            float rightWidth = rect.width - leftWidth - gap;
            float yOffset = 36f;
            GameObject leftPanel = CreatePanelBox(parent, "Leaderboard.Player.Left", new Rect(rect.x, rect.y + yOffset, leftWidth, rect.height - 52f), LbWell, LbEdge);
            GameObject rightPanel = CreatePanelBox(parent, "Leaderboard.Player.Right", new Rect(rect.x + leftWidth + gap, rect.y + yOffset, rightWidth, rect.height - 52f), LbWell, LbEdge);
            string playerHeader = player.PlayerName ?? "Player";
            CreateTextAt(leftPanel.transform, playerHeader, new Rect(12f, 8f, leftWidth - 24f, 22f), 20f, LbText, TextAnchor.MiddleLeft, false);

            // LOCAL: compute the completed list/count live from LeaderboardTracker. REMOTE (Steps 2/3): the COUNT
            // arrives inline in the snapshot (player.AchievementCount), and the completed LIST is fetched lazily from
            // the server (LeaderboardAchievementBoard). Cached → resolve the returned ids to defs; not-yet-fetched →
            // empty page + a binder that requests it and rebuilds this card when the reply lands.
            string profile = AchievementProfile;
            bool isLocal = IsLocalRanking(player);
            List<AchievementDef> completedDetails;
            if (isLocal)
            {
                completedDetails = new List<AchievementDef>(AchievementCompletion.CompletedForLocalPlayer(profile));
            }
            else if (LeaderboardAchievementBoard.TryGet(player.PlayerName, LeaderboardBoard.ViewedSeason, out var completedIds))
            {
                completedDetails = ResolveDefsByIds(profile, completedIds);
            }
            else
            {
                completedDetails = new List<AchievementDef>();
                string who = player.PlayerName;
                int season = LeaderboardBoard.ViewedSeason;
                if (!string.IsNullOrEmpty(who))
                {
                    var binder = rightPanel.AddComponent<LeaderboardAchievementsBinder>();
                    binder.Initialize(who, season, () =>
                    {
                        if (IsVisible && _selectedPlayer != null &&
                            string.Equals(_selectedPlayer.PlayerName, who, StringComparison.OrdinalIgnoreCase) &&
                            LeaderboardBoard.ViewedSeason == season)
                            RebuildCanvasView(true);
                    });
                }
            }
            int totalDefs = AchievementProfileManager.GetAchievements(profile).Count;
            int earnedCount = isLocal ? completedDetails.Count : player.AchievementCount;
            CreateTextAt(rightPanel.transform, "Achievements earned: " + earnedCount + " / " + totalDefs, new Rect(12f, 8f, rightWidth - 24f, 22f), 20f, LbText, TextAnchor.MiddleLeft, false);

            float previewW = leftWidth * 0.48f;
            float previewH = leftPanel.GetComponent<RectTransform>().rect.height - 44f;
            GameObject previewPanel = CreatePanelBox(leftPanel.transform, "Leaderboard.Player.Preview", new Rect(8f, 34f, previewW, previewH), LbWell, LbEdge);
            CreateTextAt(previewPanel.transform, "Player preview", new Rect(10f, 12f, previewPanel.GetComponent<RectTransform>().rect.width - 20f, 24f), 18f, LbMuted, TextAnchor.MiddleCenter, false);
            CreateEmbeddedPlayerPreview(previewPanel.transform, new Rect(10f, 42f, previewPanel.GetComponent<RectTransform>().rect.width - 20f, previewPanel.GetComponent<RectTransform>().rect.height - 52f), player);

            float sideX = leftWidth * 0.48f + 16f;
            float sideWidth = leftWidth - sideX - 26f;
            float sectionGap = 6f;
            float removedWeaponPanelHeight = 88f;
            float resumePanelHeight = 156f + removedWeaponPanelHeight + sectionGap;
            float skillsPanelHeight = 118f;
            GameObject resumePanel = CreatePanelBox(leftPanel.transform, "Leaderboard.Player.Resume", new Rect(sideX, 34f, sideWidth, resumePanelHeight), LbWell, LbEdge);
            CreateTextAt(resumePanel.transform, "Summary", new Rect(8f, 6f, sideWidth - 16f, 18f), 16f, LbMuted, TextAnchor.MiddleLeft, false);
            float resumeContentX = 14f;
            float resumeContentY = 34f;
            float resumeContentWidth = sideWidth - 28f;
            float resumeColumnGap = 18f;
            float resumeColumnWidth = (resumeContentWidth - resumeColumnGap) * 0.5f;
            float resumeRowHeight = 26f;
            CreateTextAt(resumePanel.transform, "Rank", new Rect(resumeContentX, resumeContentY, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "#" + player.Rank, new Rect(resumeContentX, resumeContentY + 18f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "Exploration", new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, (player.MapExplored * 100f).ToString("0.##") + "%", new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY + 18f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "Crafted", new Rect(resumeContentX, resumeContentY + 56f, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, player.Crafted.ToString(), new Rect(resumeContentX, resumeContentY + 74f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "PvP kills", new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY + 56f, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, player.PlayersKilled.ToString(), new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY + 74f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "Monsters", new Rect(resumeContentX, resumeContentY + 112f, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, player.Monsters.ToString(), new Rect(resumeContentX, resumeContentY + 130f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "Deaths", new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY + 112f, resumeColumnWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, player.Deaths.ToString(), new Rect(resumeContentX + resumeColumnWidth + resumeColumnGap, resumeContentY + 130f, resumeColumnWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, "Score", new Rect(resumeContentX, resumeContentY + 168f, resumeContentWidth, resumeRowHeight), 14f, LbMuted, TextAnchor.MiddleLeft, false);
            CreateTextAt(resumePanel.transform, player.Score.ToString(), new Rect(resumeContentX, resumeContentY + 186f, resumeContentWidth, resumeRowHeight), 16f, LbText, TextAnchor.MiddleLeft, false);

            float skillsPanelY = 34f + resumePanelHeight + sectionGap;
            float bottomPanelGap = 6f;
            float skillsBlockWidth = Mathf.Max(120f, (sideWidth - bottomPanelGap) * 0.5f);
            float favoriteWeaponBlockWidth = Mathf.Max(120f, sideWidth - skillsBlockWidth - bottomPanelGap);
            GameObject skillsPanel = CreatePanelBox(leftPanel.transform, "Leaderboard.Player.Skills", new Rect(sideX, skillsPanelY, skillsBlockWidth, skillsPanelHeight), LbWell, LbEdge);
            CreateTextAt(skillsPanel.transform, "Skills", new Rect(8f, 6f, skillsBlockWidth - 16f, 18f), 16f, LbMuted, TextAnchor.MiddleLeft, false);
            GameObject favoriteWeaponPanel = CreatePanelBox(leftPanel.transform, "Leaderboard.Player.FavoriteWeapon", new Rect(sideX + skillsBlockWidth + bottomPanelGap, skillsPanelY, favoriteWeaponBlockWidth, skillsPanelHeight), LbWell, LbEdge);
            CreateTextAt(favoriteWeaponPanel.transform, "Favorite weapon", new Rect(8f, 6f, favoriteWeaponBlockWidth - 16f, 18f), 16f, LbMuted, TextAnchor.MiddleLeft, false);
            float favoriteWeaponIconSize = 40f * PreferredWeaponPreviewScale;
            Rect favoriteWeaponIconRect = new Rect((favoriteWeaponBlockWidth - favoriteWeaponIconSize) * 0.5f, 34f, favoriteWeaponIconSize, favoriteWeaponIconSize);
            Sprite weaponSprite = ResolveWeaponSprite(ResolveFavoriteWeaponId(player));
            GameObject weaponIconObject = weaponSprite != null
                ? CreateWeaponIcon(favoriteWeaponPanel.transform, "Leaderboard.Player.FavoriteWeapon.Icon", favoriteWeaponIconRect, weaponSprite)
                : CreateWeaponPlaceholderIcon(favoriteWeaponPanel.transform, "Leaderboard.Player.FavoriteWeapon.Icon", favoriteWeaponIconRect);
            AttachHoverTooltip(weaponIconObject, _canvasContentRoot != null ? _canvasContentRoot.transform : favoriteWeaponPanel.transform, ResolveFavoriteWeaponDisplayName(player));
            string skillsText = BuildSkillsText(player);
            if (string.IsNullOrWhiteSpace(skillsText))
            {
                skillsText = "No skills synced yet.";
            }
            CreateTextAt(skillsPanel.transform, skillsText, new Rect(8f, 28f, skillsBlockWidth - 16f, skillsPanel.GetComponent<RectTransform>().rect.height - 36f), 14f, LbText, TextAnchor.UpperLeft, true);

            // The paging math below is TDV's verbatim and correct against any count. For the LOCAL row
            // completedDetails is the live client-computed list; for a REMOTE row it's empty (Step 3 wires it),
            // so it resolves to page 1/1 and the empty-branch text.
            int completedDetailsCount = completedDetails.Count;
            bool progressionMode = !showBackButton;
            float rowHeight = 72f;
            float rowGap = 6f;
            float usableHeight = Mathf.Max(1f, rightPanel.GetComponent<RectTransform>().rect.height - 42f);
            int pageSize = Mathf.Max(1, Mathf.FloorToInt((usableHeight + rowGap) / (rowHeight + rowGap)));
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(completedDetailsCount / (float)pageSize));
            int currentPage = Mathf.Clamp(GetCompletedAchievementsPage(progressionMode), 1, totalPages);
            SetCompletedAchievementsPage(progressionMode, currentPage);

            CreateTextAt(rightPanel.transform, currentPage + "/" + totalPages, new Rect(rightWidth - 136f, 8f, 34f, 22f), 14f, LbMuted, TextAnchor.MiddleCenter, false);
            CreateActionButtonAt(rightPanel.transform, "<", new Rect(rightWidth - 100f, 8f, 28f, 22f), delegate
            {
                if (currentPage > 1)
                {
                    SetCompletedAchievementsPage(progressionMode, currentPage - 1);
                    RebuildCanvasView(true);
                }
            }, currentPage > 1, false, 14f, false);
            CreateActionButtonAt(rightPanel.transform, ">", new Rect(rightWidth - 66f, 8f, 28f, 22f), delegate
            {
                if (currentPage < totalPages)
                {
                    SetCompletedAchievementsPage(progressionMode, currentPage + 1);
                    RebuildCanvasView(true);
                }
            }, currentPage < totalPages, false, 14f, false);

            List<AchievementDef> pagedDetails = new List<AchievementDef>();
            int pageStart = (currentPage - 1) * pageSize;
            for (int i = pageStart; i < completedDetails.Count && i < pageStart + pageSize; i++)
            {
                pagedDetails.Add(completedDetails[i]);
            }

            if (pagedDetails.Count == 0)
            {
                CreateTextAt(rightPanel.transform, "No achievements validated yet.", new Rect(12f, 40f, rightWidth - 24f, 24f), 16f, LbText, TextAnchor.UpperLeft, false);
            }
            else
            {
                float rowY = 34f;
                for (int i = 0; i < pagedDetails.Count; i++)
                {
                    float achievementRowWidth = rightWidth - 44f;
                    CreateAchievementRow(rightPanel.transform, new Rect(8f, rowY, achievementRowWidth, rowHeight), pagedDetails[i]);
                    rowY += rowHeight + rowGap;
                }
            }
        }

        // ---- Completed-achievement row (transcribed from TDV LeaderboardGui.Canvas.cs:705-735) -----------

        // A 72px row: optional 44px icon at (8,11) when the prefab resolves to a sprite (else the text shifts left
        // by 54), title @16 top-left, points @12 right "{Score} pts", description @14 wrapped below. TDV's biome
        // label is omitted — Fires' AchievementDef carries no biome data.
        private void CreateAchievementRow(Transform parent, Rect rowRect, AchievementDef def)
        {
            if (def == null) return;
            GameObject rowPanel = CreatePanelBox(parent, "Leaderboard.Player.Achievement", rowRect, LbWell, LbEdge);

            float rowW = rowRect.width;
            float iconSize = 44f;
            float textX = 8f;
            float textWidth = rowW - 16f;

            Sprite icon = ResolveAchievementSprite(def.Prefab);
            if (icon != null)
            {
                CreateWeaponIcon(rowPanel.transform, "Leaderboard.Player.Achievement.Icon", new Rect(8f, 11f, iconSize, iconSize), icon);
                textX += iconSize + 10f;
                textWidth -= iconSize + 10f;
            }

            CreateTextAt(rowPanel.transform, def.Name ?? "Achievement", new Rect(textX, 12f, textWidth - 86f, 18f), 16f, LbText, TextAnchor.MiddleLeft, false);
            CreateTextAt(rowPanel.transform, def.Score + " pts", new Rect(rowW - 82f, 30f, 56f, 16f), 12f, LbMuted, TextAnchor.MiddleRight, false);
            string description = string.IsNullOrWhiteSpace(def.Description) ? "No description." : def.Description;
            CreateTextAt(rowPanel.transform, description, new Rect(textX, 34f, textWidth, 30f), 14f, LbMuted, TextAnchor.UpperLeft, true);
        }

        // Resolve a sprite for an achievement's target prefab: the item icon when it's an ObjectDB item (mirrors
        // ResolveWeaponItemData); else a creature/pickable prefab icon from ZNetScene when one is reasonable; else
        // null (the row renders icon-less and shifts its text left). Rendered via the existing CreateWeaponIcon.
        private static Sprite ResolveAchievementSprite(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            try
            {
                if (ObjectDB.instance != null)
                {
                    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
                    if (itemPrefab != null)
                    {
                        var drop = itemPrefab.GetComponent<ItemDrop>();
                        var icons = drop != null && drop.m_itemData != null && drop.m_itemData.m_shared != null
                            ? drop.m_itemData.m_shared.m_icons
                            : null;
                        if (icons != null && icons.Length > 0 && icons[0] != null) return icons[0];
                    }
                }

                // Fall back to a creature/pickable/world prefab: a Pickable's drop item icon is the natural sprite;
                // otherwise no icon (creature prefabs have no inherent UI sprite).
                if (ZNetScene.instance != null)
                {
                    GameObject scenePrefab = ZNetScene.instance.GetPrefab(prefab);
                    if (scenePrefab != null)
                    {
                        var pickable = scenePrefab.GetComponent<Pickable>();
                        var pickIcons = pickable != null && pickable.m_itemPrefab != null
                            ? pickable.m_itemPrefab.GetComponent<ItemDrop>()
                            : null;
                        var sharedIcons = pickIcons != null && pickIcons.m_itemData != null && pickIcons.m_itemData.m_shared != null
                            ? pickIcons.m_itemData.m_shared.m_icons
                            : null;
                        if (sharedIcons != null && sharedIcons.Length > 0 && sharedIcons[0] != null) return sharedIcons[0];
                    }
                }
            }
            catch { }
            return null;
        }

        // ---- Adventure milestone timeline (TDV BuildTimelineGrid) ----------------------------------------

        // Adventure timeline: an 8x2 grid of the 16 LeaderboardMilestones, each rendered with its label and lit
        // when the player has achieved it (achievedIds = server-computed set from the snapshot). A new/empty
        // player simply shows all 16 slots locked (dim) — the timeline of what's ahead, not "No milestones yet."
        private void BuildTimelineGrid(Transform parent, Rect rect, List<string> achievedIds)
        {
            // Memorial mode: the Adventure panel becomes the obituary, composed from the selected player's OVERALL
            // stats + first-seen date, instead of the milestone grid.
            if (_memorialMode)
            {
                BuildObituaryPanel(parent, rect);
                return;
            }

            var defs = global::FiresRPGmaker.Modules.Marketplace.LeaderboardMilestones.All;
            var achieved = achievedIds != null
                ? new HashSet<string>(achievedIds)
                : new HashSet<string>();

            float horizontalInset = 22f;
            float topInset = 10f;   // clear of the "Adventure" panel title (was -16 for TDV's icon-only slots)
            float interRowGap = 28f;
            float sectionX = rect.x + horizontalInset;
            float sectionWidth = Mathf.Max(1f, rect.width - horizontalInset * 2f);
            float sectionHeight = rect.height;
            int columns = 8;
            int rows = 2;
            float gridY = rect.y + topInset;
            float slotColumnWidth = (sectionWidth - 6f * (columns - 1)) / columns;
            float rowHeight = (sectionHeight - topInset - interRowGap * (rows - 1)) / rows;
            float slotSize = Mathf.Min(slotColumnWidth, rowHeight);
            for (int index = 0; index < columns * rows; index++)
            {
                int row = index / columns;
                int col = index % columns;
                float slotColumnX = sectionX + col * (slotColumnWidth + 6f);
                float rowOffsetY = 0f;  // even rows (the -20 pull was for TDV's tighter icon grid)
                float slotRowY = gridY + row * (rowHeight + interRowGap) + rowOffsetY;
                float slotX = slotColumnX + Mathf.Max(0f, (slotColumnWidth - slotSize) * 0.5f);
                float slotY = slotRowY + Mathf.Max(0f, (rowHeight - slotSize) * 0.5f);
                var def = index < defs.Count ? defs[index] : null;
                bool done = def != null && achieved.Contains(def.Id);
                CreateMilestoneSlotBox(parent, "Leaderboard.Milestone." + index, new Rect(slotX, slotY, slotSize, slotSize), def, done);
            }
        }

        // Renders the auto-composed Norse obituary into the Adventure panel (memorial mode). Pulls the same
        // overall stats the details card shows, plus the skills/weapon strings from the existing helpers, and the
        // first-seen date passed from the tombstone. Title overrides the panel's "Adventure" label visually by
        // sitting at the top of the obituary text.
        private void BuildObituaryPanel(Transform parent, Rect rect)
        {
            FiresLeaderboardRanking player = _selectedPlayer;
            if (player == null)
            {
                CreateTextAt(parent, "Their tale is not yet recorded.", rect, 16f, LbMuted, TextAnchor.UpperLeft, true);
                return;
            }

            string profile = AchievementProfile;
            int totalDefs = AchievementProfileManager.GetAchievements(profile).Count;
            int earnedCount = IsLocalRanking(player)
                ? AchievementCompletion.CompletedForLocalPlayer(profile).Count
                : player.AchievementCount;
            string skills = BuildSkillsText(player);
            string weapon = ResolveFavoriteWeaponDisplayName(player);

            string obituary = ObituaryComposer.Compose(player, _memorialFirstJoinedTicks, earnedCount, totalDefs, skills, weapon);
            CreateTextAt(parent, obituary, rect, 15f, LbText, TextAnchor.UpperLeft, true);
        }

        private GameObject CreateMilestoneSlotBox(Transform parent, string name, Rect rect, global::FiresRPGmaker.Modules.Marketplace.MilestoneDef def, bool completed)
        {
            GameObject slotRoot = new GameObject(name, typeof(RectTransform));
            slotRoot.transform.SetParent(parent, false);
            SetRect(slotRoot.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);
            // Fire 10-08 (leaderboard overhaul, mockup a): a dark cell plate with the milestone's vanilla icon. Done = a gold
            // edge, a gold check and the icon in full colour; ahead = a thin edge and the icon dimmed and grey.
            AddImage(slotRoot, completed ? LbCellDone : LbCell);
            CreateCanvasOutlineRect(slotRoot.transform, name + ".Border", new Rect(0f, 0f, rect.width, rect.height), completed ? LbGold : LbEdge);
            if (def != null)
            {
                Sprite icon = ResolveWeaponSprite(def.Icon);
                float labelH = Mathf.Min(16f, rect.height * 0.28f);
                if (icon != null)
                {
                    float iconSize = Mathf.Max(8f, rect.height - labelH - 8f);
                    GameObject iconObject = new GameObject("Icon", typeof(RectTransform));
                    iconObject.transform.SetParent(slotRoot.transform, false);
                    SetRect(iconObject.GetComponent<RectTransform>(), (rect.width - iconSize) * 0.5f, 3f, iconSize, iconSize);
                    Component image = iconObject.AddComponent(CanvasImageType);
                    SetProperty(image, "sprite", icon);
                    SetProperty(image, "color", completed ? Color.white : new Color(0.45f, 0.45f, 0.45f, 0.55f));
                    SetBoolProperty(image, "raycastTarget", false);
                }
                CreateTextAt(slotRoot.transform, def.Label, new Rect(2f, rect.height - labelH - 2f, rect.width - 4f, labelH), 10f,
                    completed ? LbText : LbMuted, TextAnchor.MiddleCenter, true);
                if (completed)
                    CreateTextAt(slotRoot.transform, "✓", new Rect(rect.width - 14f, 1f, 12f, 12f), 11f, LbGold, TextAnchor.MiddleCenter, true);
            }
            return slotRoot;
        }

        // ---- 3D preview (our own Core widget: FiresCore.UI.UIOverridePlayerPreview) ----------------------

        // Renders the player using our existing Core preview widget — a camera aimed at the live Player.m_localPlayer
        // visual, rendered to a RawImage with drag-to-rotate and a transparent background. Because it renders the LIVE
        // local player, it only works for the local row; a remote row has no live model to aim at, so it shows an
        // honest "Preview unavailable" (remote-appearance rendering would be a separate feature). The dark inner
        // backdrop + parchment frame give the model a readable, on-theme box.
        private GameObject CreateEmbeddedPlayerPreview(Transform parent, Rect rect, FiresLeaderboardRanking ranking)
        {
            if (parent == null) return null;

            GameObject host = new GameObject("Leaderboard.Player.EmbeddedPreview", typeof(RectTransform));
            host.transform.SetParent(parent, false);
            SetRect(host.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);

            GameObject background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(host.transform, false);
            SetRect(background.GetComponent<RectTransform>(), 0f, 0f, rect.width, rect.height);
            AddImage(background, new Color(0f, 0f, 0f, 0.565f));
            CreateCanvasOutlineRect(host.transform, "Leaderboard.Player.EmbeddedPreview.Border", new Rect(0f, 0f, rect.width, rect.height), LbEdge);

            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            bool isLocal = ranking == null
                || (!string.IsNullOrEmpty(localName) && string.Equals(ranking.PlayerName, localName, StringComparison.OrdinalIgnoreCase));

            if (isLocal && Player.m_localPlayer != null && CanvasRawImageType != null)
            {
                GameObject imageObject = new GameObject("Image", typeof(RectTransform));
                imageObject.transform.SetParent(host.transform, false);
                SetRect(imageObject.GetComponent<RectTransform>(), 0f, 0f, rect.width, rect.height);
                Component rawImage = imageObject.AddComponent(CanvasRawImageType);
                SetBoolProperty(rawImage, "raycastTarget", true); // lets drag-to-rotate work where a raycaster exists
                // 0.0.542 (Fire 10-09: "dark, odd angle, world behind"): the live-player camera (UIOverridePlayerPreview)
                // filmed the player where they stand. Like the remote rows, the local row now stages an isolated mannequin
                // (UIMannequinPreview: own stage far above the world, own light, facing the camera) from a snapshot of
                // the local player's current look, so only the player and their gear show.
                var localMannequin = imageObject.AddComponent<FiresCore.UI.UIMannequinPreview>();
                var look = FiresCore.Appearance.PlayerAppearanceCapture.Capture(Player.m_localPlayer);
                localMannequin.SetAppearance(look);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Leaderboard] player preview: local row -> UIMannequinPreview (isolated stage), appearance {(look != null ? "captured" : "unavailable")}");
            }
            else if (!isLocal && CanvasRawImageType != null)
            {
                // Remote player: render a dressed mannequin built from the player's captured appearance. The binder
                // lazily fetches that appearance from the server and feeds the Core widget when it arrives; until then
                // (or if the player never captured a look) the box shows its dark backdrop. The widget owns its own
                // camera/RenderTexture and tears down via OnDestroy when ClearCanvasContent destroys this host.
                GameObject imageObject = new GameObject("Image", typeof(RectTransform));
                imageObject.transform.SetParent(host.transform, false);
                SetRect(imageObject.GetComponent<RectTransform>(), 0f, 0f, rect.width, rect.height);
                Component rawImage = imageObject.AddComponent(CanvasRawImageType);
                SetBoolProperty(rawImage, "raycastTarget", true);
                var mannequin = imageObject.AddComponent<FiresCore.UI.UIMannequinPreview>();
                LeaderboardMannequinBinder binder = host.AddComponent<LeaderboardMannequinBinder>();
                binder.Initialize(ranking != null ? ranking.PlayerName : null, mannequin);
            }
            else
            {
                CreateTextAt(host.transform, "Preview unavailable", new Rect(8f, 0f, Mathf.Max(0f, rect.width - 16f), rect.height), 14f, new Color(0.82f, 0.78f, 0.70f, 1f), TextAnchor.MiddleCenter, true);
            }

            return host;
        }

        private GameObject CreateWeaponPlaceholderIcon(Transform parent, string name, Rect rect)
        {
            GameObject iconObject = new GameObject(name, typeof(RectTransform));
            iconObject.transform.SetParent(parent, false);
            SetRect(iconObject.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);
            Component baseImage = iconObject.AddComponent(CanvasImageType);
            SetProperty(baseImage, "color", LbCell);
            SetBoolProperty(baseImage, "raycastTarget", true);
            CreateCanvasOutlineRect(iconObject.transform, name + ".Border", new Rect(0f, 0f, rect.width, rect.height), LbEdge);
            CreateTextAt(iconObject.transform, "?", new Rect(0f, 0f, rect.width, rect.height), 18f, LbText, TextAnchor.MiddleCenter, false);
            return iconObject;
        }

        // ---- Skills panel data (TDV: local live / remote row.TopSkills) ----------------------------------

        // Up-to-4 lines "Humanized(skill) : level". Local player reads the LIVE Skills component for freshness;
        // remote players use the row's synced TopSkills. Empty → the honest "No skills synced yet." line.
        private string BuildSkillsText(FiresLeaderboardRanking player)
        {
            var lines = new List<string>();
            if (IsLocalRanking(player))
            {
                foreach (var entry in GetLocalTopSkills(4))
                    lines.Add(HumanizeSkill(entry.Skill) + " : " + entry.Level);
            }
            else if (player != null && player.TopSkills != null)
            {
                foreach (var entry in player.TopSkills)
                    lines.Add(HumanizeSkill(entry.Skill) + " : " + entry.Level);
            }
            if (lines.Count == 0) return "No skills synced yet.";
            return string.Join("\n", lines);
        }

        // Read the local player's live skill list, top-4 by level (>0, desc) — the freshest possible source.
        private static List<LeaderboardSkillEntry> GetLocalTopSkills(int count)
        {
            var result = new List<LeaderboardSkillEntry>();
            var local = Player.m_localPlayer;
            var skills = local != null ? local.GetSkills() : null;
            var list = skills != null ? skills.GetSkillList() : null;
            if (list == null) return result;

            var ordered = new List<LeaderboardSkillEntry>();
            foreach (var s in list)
            {
                if (s == null || s.m_info == null) continue;
                int level = Mathf.RoundToInt(s.m_level);
                if (level <= 0) continue;
                ordered.Add(new LeaderboardSkillEntry(s.m_info.m_skill.ToString(), level));
            }
            ordered.Sort((a, b) => b.Level != a.Level ? b.Level.CompareTo(a.Level) : string.CompareOrdinal(a.Skill, b.Skill));
            for (int i = 0; i < ordered.Count && i < count; i++) result.Add(ordered[i]);
            return result;
        }

        // SkillType enum name → spaced words (e.g. "WoodCutting" → "Wood Cutting"); also tries a localized
        // "$skill_<lower>" token first (vanilla provides these for the in-game skills tab).
        private static string HumanizeSkill(string skill)
        {
            if (string.IsNullOrEmpty(skill)) return string.Empty;
            try
            {
                if (Localization.instance != null)
                {
                    string localized = Localization.instance.Localize("$skill_" + skill.ToLowerInvariant());
                    if (!string.IsNullOrEmpty(localized) && !localized.StartsWith("$") && !localized.StartsWith("[")) return localized;
                }
            }
            catch { }
            var sb = new System.Text.StringBuilder(skill.Length + 4);
            for (int i = 0; i < skill.Length; i++)
            {
                char c = skill[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(skill[i - 1])) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ---- Favorite weapon (TDV: icon from ObjectDB + localized name) ----------------------------------

        // The weapon id for a player: the local player prefers the LIVE current weapon (freshest, nicest name);
        // remote players use the row's synced PreferredWeaponId.
        private string ResolveFavoriteWeaponId(FiresLeaderboardRanking player)
        {
            if (IsLocalRanking(player))
            {
                string live = GetLocalCurrentWeaponId();
                if (!string.IsNullOrEmpty(live)) return live;
            }
            return player != null ? (player.PreferredWeaponId ?? string.Empty) : string.Empty;
        }

        private static string GetLocalCurrentWeaponId()
        {
            try
            {
                var local = Player.m_localPlayer;
                var item = local != null ? local.GetCurrentWeapon() : null;
                // Bare hands are no "favourite weapon": the unarmed item has no drop prefab and no icon, which showed "?"
                // (Fire 10-08). Fall through to the server's tracked favourite.
                if (item == null || item.m_dropPrefab == null
                    || (local.m_unarmedWeapon != null && item == local.m_unarmedWeapon.m_itemData)) return string.Empty;
                if (item.m_dropPrefab != null)
                {
                    string n = item.m_dropPrefab.name;
                    if (!string.IsNullOrEmpty(n)) return n.Replace("(Clone)", string.Empty).Trim();
                }
                if (item.m_shared != null) return item.m_shared.m_name;
            }
            catch { }
            return string.Empty;
        }

        // Build the 44px icon box from a resolved weapon sprite; returns the icon root so the caller can attach a
        // hover tooltip (the base image is raycastable so the tooltip binder sees the hover). Mirrors TDV's
        // CreatePreviewVisualAt return-the-icon shape.
        private GameObject CreateWeaponIcon(Transform parent, string name, Rect rect, Sprite sprite)
        {
            GameObject iconObject = new GameObject(name, typeof(RectTransform));
            iconObject.transform.SetParent(parent, false);
            SetRect(iconObject.GetComponent<RectTransform>(), rect.x, rect.y, rect.width, rect.height);
            Component baseImage = iconObject.AddComponent(CanvasImageType);
            SetProperty(baseImage, "color", LbCell);
            SetBoolProperty(baseImage, "raycastTarget", true);
            CreateCanvasOutlineRect(iconObject.transform, name + ".Border", new Rect(0f, 0f, rect.width, rect.height), LbEdge);

            GameObject spriteObject = new GameObject("Sprite", typeof(RectTransform));
            spriteObject.transform.SetParent(iconObject.transform, false);
            SetRect(spriteObject.GetComponent<RectTransform>(), 0f, 0f, rect.width, rect.height);
            Component image = spriteObject.AddComponent(CanvasImageType);
            SetProperty(image, "sprite", sprite);
            SetProperty(image, "color", Color.white);
            SetBoolProperty(image, "raycastTarget", false);
            return iconObject;
        }

        // The display name under the icon: localized item name for a resolved weapon, else the stored id text.
        private string ResolveFavoriteWeaponDisplayName(FiresLeaderboardRanking player)
        {
            string id = ResolveFavoriteWeaponId(player);
            if (string.IsNullOrEmpty(id)) return string.Empty;

            string sharedName = ResolveWeaponSharedName(id);
            if (!string.IsNullOrEmpty(sharedName))
            {
                try
                {
                    string localized = Localization.instance != null ? Localization.instance.Localize(sharedName) : sharedName;
                    if (!string.IsNullOrEmpty(localized)) return localized;
                }
                catch { }
                return sharedName;
            }
            // No prefab resolved (id may already be a display name from the m_shared.m_name fallback). Localize it.
            try
            {
                string localized = Localization.instance != null ? Localization.instance.Localize(id) : id;
                return string.IsNullOrEmpty(localized) ? id : localized;
            }
            catch { return id; }
        }

        private static Sprite ResolveWeaponSprite(string weaponId)
        {
            var item = ResolveWeaponItemData(weaponId);
            if (item == null || item.m_shared == null || item.m_shared.m_icons == null || item.m_shared.m_icons.Length == 0) return null;
            return item.m_shared.m_icons[0];
        }

        private static string ResolveWeaponSharedName(string weaponId)
        {
            var item = ResolveWeaponItemData(weaponId);
            return item != null && item.m_shared != null ? item.m_shared.m_name : null;
        }

        // ObjectDB prefab lookup → the prefab's ItemDrop.m_itemData. Returns null when ObjectDB isn't ready or
        // the id isn't an item prefab (e.g. when only an m_shared display name was stored as the id).
        private static ItemDrop.ItemData ResolveWeaponItemData(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) || ObjectDB.instance == null) return null;
            try
            {
                GameObject prefab = ObjectDB.instance.GetItemPrefab(weaponId);
                if (prefab == null) return null;
                var drop = prefab.GetComponent<ItemDrop>();
                return drop != null ? drop.m_itemData : null;
            }
            catch { return null; }
        }

        // The local player owns the window; a ranking matches them when its name equals the local player name.
        private static bool IsLocalRanking(FiresLeaderboardRanking player)
        {
            var local = Player.m_localPlayer;
            if (player == null || local == null) return false;
            string localName = local.GetPlayerName();
            return !string.IsNullOrEmpty(localName) && string.Equals(player.PlayerName, localName, StringComparison.OrdinalIgnoreCase);
        }

        // Map server-returned completed-achievement ids back to defs (client + server load the same .cfg, so ids
        // match by parse order). Returns the matching defs in profile/def order so the pager reads naturally.
        private static List<AchievementDef> ResolveDefsByIds(string profile, List<int> ids)
        {
            var result = new List<AchievementDef>();
            var defs = AchievementProfileManager.GetAchievements(profile);
            if (defs == null || ids == null || ids.Count == 0) return result;
            var want = new HashSet<int>(ids);
            for (int i = 0; i < defs.Count; i++)
            {
                AchievementDef def = defs[i];
                if (def != null && want.Contains(def.Id)) result.Add(def);
            }
            return result;
        }

        // ---- Helpers ------------------------------------------------------------------------------------

        // The completed-achievements pager uses a separate page counter per view (TDV: progression vs detail).
        private int GetCompletedAchievementsPage(bool progressionMode)
        {
            return progressionMode ? _progressAchievementsPage : _detailAchievementsPage;
        }

        private void SetCompletedAchievementsPage(bool progressionMode, int page)
        {
            if (progressionMode) _progressAchievementsPage = page;
            else _detailAchievementsPage = page;
        }

        // TDV ResolveLocalPlayer: find the local viking in the board rows by name.
        private FiresLeaderboardRanking ResolveLocalPlayer()
        {
            string localName = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : null;
            if (string.IsNullOrEmpty(localName)) return null;
            List<FiresLeaderboardRanking> rankings = _adapter.GetRankings();
            for (int i = 0; i < rankings.Count; i++)
            {
                if (rankings[i] != null && string.Equals(rankings[i].PlayerName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return rankings[i];
                }
            }
            return null;
        }

        // UnityEngine.UI.RawImage, resolved by type-name (the UI assembly is referenced by name to survive bundle/
        // version differences, exactly like the other Canvas* types in FiresLeaderboardGui.Canvas.cs).
        private static readonly Type CanvasRawImageType = ResolveCanvasType("UnityEngine.UI.RawImage", "UnityEngine.UI.RawImage, UnityEngine.UI");

        // ---- Hover tooltip (TDV AttachHoverTooltip + HoverTooltipCanvasBinder) ---------------------------

        // Transcribed from TDV: builds a hidden parchment tooltip and a binder that shows it on hover, positioned
        // next to the target. ThemeTooltipFill() has no parchment mapping, so the opaque ParchmentPanel is used as
        // the tooltip backdrop (ParchmentField is near-transparent and would be invisible).
        private void AttachHoverTooltip(GameObject targetObject, Transform tooltipParent, string tooltipText)
        {
            if (targetObject == null || tooltipParent == null || string.IsNullOrWhiteSpace(tooltipText))
            {
                return;
            }

            RectTransform targetRect = targetObject.GetComponent<RectTransform>();
            if (targetRect == null)
            {
                return;
            }

            int estimatedLines = Mathf.Max(1, Mathf.CeilToInt(tooltipText.Length / 24f));
            float tooltipWidth = Mathf.Clamp(tooltipText.Length * 6.6f + 18f, 96f, 210f);
            float tooltipBaseHeight = Mathf.Clamp(18f + (estimatedLines * 12f), 24f, 42f);
            float tooltipHeight = Mathf.Max(18f, tooltipBaseHeight - 15f);
            GameObject tooltipObject = new GameObject(targetObject.name + ".Tooltip", typeof(RectTransform));
            tooltipObject.transform.SetParent(tooltipParent, false);
            RectTransform tooltipRect = tooltipObject.GetComponent<RectTransform>();
            SetRect(tooltipRect, 0f, 0f, tooltipWidth, tooltipHeight);

            GameObject borderObject = new GameObject(targetObject.name + ".Tooltip.Border", typeof(RectTransform));
            borderObject.transform.SetParent(tooltipObject.transform, false);
            RectTransform borderRect = borderObject.GetComponent<RectTransform>();
            SetRect(borderRect, 0f, 0f, tooltipWidth, tooltipHeight);
            AddImage(borderObject, LbEdge);

            GameObject fillObject = new GameObject(targetObject.name + ".Tooltip.Fill", typeof(RectTransform));
            fillObject.transform.SetParent(borderObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            SetRect(fillRect, 1f, 1f, tooltipWidth - 2f, tooltipHeight - 2f);
            AddImage(fillObject, LbWell);

            GameObject textObject = CloneTextObject(_canvasTextTemplate, "TooltipText", fillObject.transform);
            RectTransform textRect = textObject != null ? textObject.GetComponent<RectTransform>() : null;
            if (textRect != null)
            {
                SetRect(textRect, 6f, 22f, tooltipWidth - 12f, Mathf.Max(tooltipHeight, 18f));
            }

            Component textComponent = textObject != null ? FindFirstText(textObject.transform) : null;
            SetText(textComponent, tooltipText);
            SetFontSize(textComponent, 11f);
            ApplyTextColor(textComponent, LbText);
            SetTextAlignment(textComponent, TextAnchor.UpperLeft);
            SetTextWrapping(textComponent, true);
            SetTextRaycast(textComponent, false);
            tooltipObject.SetActive(false);

            HoverTooltipCanvasBinder binder = targetObject.GetComponent<HoverTooltipCanvasBinder>() ?? targetObject.AddComponent<HoverTooltipCanvasBinder>();
            binder.Initialize(targetRect, tooltipRect, tooltipObject);
        }

        private sealed class HoverTooltipCanvasBinder : MonoBehaviour
        {
            private RectTransform _targetRect;
            private RectTransform _tooltipRect;
            private GameObject _tooltipObject;

            public void Initialize(RectTransform targetRect, RectTransform tooltipRect, GameObject tooltipObject)
            {
                _targetRect = targetRect;
                _tooltipRect = tooltipRect;
                _tooltipObject = tooltipObject;
            }

            private void LateUpdate()
            {
                if (_targetRect == null || _tooltipRect == null || _tooltipObject == null)
                {
                    return;
                }

                bool hovered = _targetRect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_targetRect, Input.mousePosition, null);
                if (_tooltipObject.activeSelf != hovered)
                {
                    _tooltipObject.SetActive(hovered);
                }

                if (!hovered)
                {
                    return;
                }

                RectTransform parentRect = _tooltipRect.parent as RectTransform;
                if (parentRect == null)
                {
                    return;
                }

                Vector3[] corners = new Vector3[4];
                _targetRect.GetWorldCorners(corners);
                Vector3 rightCenterWorld = (corners[2] + corners[3]) * 0.5f;
                Vector3 leftCenterWorld = (corners[0] + corners[1]) * 0.5f;
                Vector3 topCenterWorld = (corners[1] + corners[2]) * 0.5f;
                Vector3 bottomCenterWorld = (corners[0] + corners[3]) * 0.5f;

                Vector2 rightCenterLocal;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, RectTransformUtility.WorldToScreenPoint(null, rightCenterWorld), null, out rightCenterLocal))
                {
                    return;
                }

                float width = _tooltipRect.rect.width;
                float height = _tooltipRect.rect.height;
                float x = rightCenterLocal.x + (parentRect.rect.width * parentRect.pivot.x) + 8f;
                float y = (parentRect.rect.height * (1f - parentRect.pivot.y)) - rightCenterLocal.y - (height * 0.5f);

                if (x + width > parentRect.rect.width - 4f)
                {
                    Vector2 leftCenterLocal;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, RectTransformUtility.WorldToScreenPoint(null, leftCenterWorld), null, out leftCenterLocal))
                    {
                        x = leftCenterLocal.x + (parentRect.rect.width * parentRect.pivot.x) - width - 8f;
                        y = (parentRect.rect.height * (1f - parentRect.pivot.y)) - leftCenterLocal.y - (height * 0.5f);
                    }
                }

                if (x < 4f || x + width > parentRect.rect.width - 4f)
                {
                    Vector2 topCenterLocal;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, RectTransformUtility.WorldToScreenPoint(null, topCenterWorld), null, out topCenterLocal))
                    {
                        x = topCenterLocal.x + (parentRect.rect.width * parentRect.pivot.x) - (width * 0.5f);
                        y = (parentRect.rect.height * (1f - parentRect.pivot.y)) - topCenterLocal.y - height - 6f;
                    }
                }

                if (y < 4f)
                {
                    Vector2 bottomCenterLocal;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, RectTransformUtility.WorldToScreenPoint(null, bottomCenterWorld), null, out bottomCenterLocal))
                    {
                        y = (parentRect.rect.height * (1f - parentRect.pivot.y)) - bottomCenterLocal.y + 6f;
                    }
                }

                x = Mathf.Clamp(x, 4f, Mathf.Max(4f, parentRect.rect.width - width - 4f));
                y = Mathf.Clamp(y, 4f, Mathf.Max(4f, parentRect.rect.height - height - 4f));
                SetRect(_tooltipRect, x, y, width, height);
            }

            private void OnDisable()
            {
                if (_tooltipObject != null)
                {
                    _tooltipObject.SetActive(false);
                }
            }
        }
    }
}
