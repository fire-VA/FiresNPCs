using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using FiresRPGmaker.Managers;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresRPGmaker.Modules.Territories;
using FiresRPGmaker.UI.QuestJournal;
using FiresRPGmaker.UI.Books;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Right-side on-screen quest tracker, kg AcceptedQuestsUI content model in the FUC rounded skin. Sits
    /// under the territory box (shares its width); collapses to a single book-icon chip parked at the same
    /// spot. Hides when you have no accepted quests and on the vanilla Ctrl+F3 hide-HUD toggle. Client-only,
    /// read-only: reads the local player's ActiveQuests via VaultOfKnowledge and polls ~0.6s (no progress
    /// event exists). Book icon toggles collapse; clicking a quest name opens the full journal.
    /// </summary>
    public class QuestTrackerHud : MonoBehaviour
    {
        private static QuestTrackerHud _instance;
        private float _nextPoll;
        private float _nextPersist;
        private const float PollInterval = 0.6f;
        private static float BoxWidth => TerritoryHudOverlay.BoxWidth;

        private RectTransform _root;
        private GameObject _panel;
        private RectTransform _panelRect;
        private RectTransform _rowsRoot;
        private GameObject _bookChip;
        private Font _font;
        private bool _collapsed;
        private string _lastSig;
        private readonly Dictionary<string, Text> _timerLabels = new();
        private float _appliedPosX = float.NaN, _appliedPosY = float.NaN;
        private const float StackGapPx = 6f;     // gap between the territory box's bottom and the tracker's top

        private static readonly Color PanelFill      = new Color(0.125f, 0.09f, 0.055f, 0.92f);
        private static readonly Color ChipFill       = new Color(0.145f, 0.10f, 0.06f, 0.95f);
        private static readonly Color GoldBorder     = new Color(0.69f, 0.51f, 0.25f, 1f);
        private static readonly Color TitleGold      = new Color(0.913f, 0.784f, 0.415f);
        private static readonly Color QuestNameColor = new Color(0.922f, 0.831f, 0.31f);
        private static readonly Color TimerRed       = new Color(0.878f, 0.376f, 0.227f);
        private static readonly Color Cream          = new Color(0.913f, 0.867f, 0.769f);
        private static readonly Color Divider        = new Color(0.49f, 0.353f, 0.172f, 0.5f);
        private static readonly Color PillGreen      = new Color(0.341f, 0.761f, 0.341f, 1f);
        private static readonly Color PillGreenText  = new Color(0.07f, 0.20f, 0.07f);
        private static readonly Color PillOrange     = new Color(0.878f, 0.584f, 0.184f, 1f);
        private static readonly Color PillOrangeText = new Color(0.227f, 0.165f, 0.07f);

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresRPGmaker_QuestTrackerHud");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<QuestTrackerHud>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(this); return; }
            _instance = this;
            _collapsed = ConfigManager.Instance?.configQuestTrackerCollapsed?.Value ?? false;
        }

        private void Update()
        {
            // Durably persist client-local quest progress: the existing kill/craft/build hooks mutate the store's
            // in-memory ActiveQuestData (shared refs via GetPlayerData), and this flushes those per-quest keys so
            // progress survives a relog (kg-parity: progress lives in Player.m_customData, not the server blob).
            if (Time.time >= _nextPersist)
            {
                _nextPersist = Time.time + 3f;
                try { global::FiresRPGmaker.Modules.Vault.ActiveQuestStore.PersistAll(); } catch { }
            }

            if (Time.time < _nextPoll) return;
            _nextPoll = Time.time + PollInterval;
            try { Tick(); } catch { }
        }

        private void Tick()
        {
            var cfg = ConfigManager.Instance;
            bool show = cfg?.configShowQuestTracker?.Value ?? true;
            var hud = global::Hud.instance;
            var local = Player.m_localPlayer;

            if (!show || hud == null || local == null || global::Hud.IsUserHidden())
            {
                if (_root != null) _root.gameObject.SetActive(false);
                return;
            }

            EnsureUi(hud);
            if (_root == null) return;

            var quests = GatherQuests(local);
            if (quests.Count == 0)
            {
                _root.gameObject.SetActive(false); // hide at zero
                return;
            }
            _root.gameObject.SetActive(true);

            // Position: auto-stack directly under the group HUD (following its dynamic height) until the player
            // drags the tracker — then honour their saved spot. Skip auto-positioning while the pause menu is
            // open so a live drag isn't fought by the poll.
            if (cfg?.configQuestTrackerUserPositioned?.Value ?? false)
            {
                float posX = cfg?.configQuestTrackerPosX?.Value ?? -12f;
                float posY = cfg?.configQuestTrackerPosY?.Value ?? -410f;
                if (posX != _appliedPosX || posY != _appliedPosY)
                {
                    _root.anchoredPosition = new Vector2(posX, posY);
                    _appliedPosX = posX; _appliedPosY = posY;
                }
            }
            else if (!Menu.IsVisible())
            {
                AutoPositionBelowTerritory();
            }

            _bookChip.SetActive(_collapsed);
            _panel.SetActive(!_collapsed);

            if (!_collapsed)
            {
                string sig = BuildSignature(quests);
                if (sig != _lastSig)
                {
                    _lastSig = sig;
                    RebuildRows(quests);
                }
                UpdateTimers(local);
            }
        }

        private List<(QuestDefinition def, ActiveQuestData active)> GatherQuests(Player local)
        {
            var result = new List<(QuestDefinition, ActiveQuestData)>();
            var vok = VaultOfKnowledge.Instance;
            var qm = QuestManager.Instance;
            if (vok == null || qm == null) return result;
            var data = vok.GetPlayerData(local.GetPlayerID());
            if (data?.ActiveQuests == null) return result;
            foreach (var aq in data.ActiveQuests)
            {
                if (aq == null || string.IsNullOrEmpty(aq.QuestId)) continue;
                var def = qm.GetQuestDefinition(aq.QuestId);
                if (def == null) continue;
                result.Add((def, aq));
            }
            result.Sort((a, b) => string.Compare(
                string.IsNullOrEmpty(a.Item1.Name) ? a.Item1.QuestId : a.Item1.Name,
                string.IsNullOrEmpty(b.Item1.Name) ? b.Item1.QuestId : b.Item1.Name,
                StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private string BuildSignature(List<(QuestDefinition def, ActiveQuestData active)> quests)
        {
            var sb = new StringBuilder();
            foreach (var (def, active) in quests)
            {
                sb.Append(def.QuestId).Append(':').Append(active.Progress).Append('|');
                if (def.QuestTargets != null)
                    foreach (var t in def.QuestTargets)
                        sb.Append(QuestProgressTracker.GetProgressForTarget(def, active, t)).Append(',');
                sb.Append(';');
            }
            return sb.ToString();
        }

        // ── UI build ───────────────────────────────────────────────────────────────────────
        private void EnsureUi(global::Hud hud)
        {
            if (_root != null || hud == null) return;
            _font = ResolveFont(hud);

            var rootGo = new GameObject("FiresQuestTracker", typeof(RectTransform));
            _root = rootGo.GetComponent<RectTransform>();
            _root.SetParent(hud.transform, false);
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(1f, 1f);
            _root.sizeDelta = Vector2.zero;
            _root.anchoredPosition = new Vector2(-12f, -300f); // sane default until the first auto/manual placement

            var canvas = rootGo.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5;
            rootGo.AddComponent<GraphicRaycaster>();

            BuildPanel();
            _bookChip = CreateBookButton(_root, ToggleCollapsed);
            var crt = _bookChip.GetComponent<RectTransform>();
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(38f, 38f);

            _root.gameObject.SetActive(false);
        }

        private void SavePosition(Vector2 pos)
        {
            var cfg = ConfigManager.Instance;
            if (cfg?.configQuestTrackerPosX != null) cfg.configQuestTrackerPosX.Value = pos.x;
            if (cfg?.configQuestTrackerPosY != null) cfg.configQuestTrackerPosY.Value = pos.y;
            // Dragging opts out of auto-stacking: the tracker now stays where the player dropped it.
            if (cfg?.configQuestTrackerUserPositioned != null) cfg.configQuestTrackerUserPositioned.Value = true;
            _appliedPosX = pos.x; _appliedPosY = pos.y;
        }

        // Stack the tracker directly under the territory box (minimap → territory → quest → group HUD), falling
        // back to just under the minimap when no territory box is showing. World-space (via corners) so it
        // survives whatever anchor space each panel uses.
        private void AutoPositionBelowTerritory()
        {
            RectTransform anchor = null;
            var terr = global::FiresRPGmaker.Modules.Territories.TerritoryHudOverlay.BoxRect;
            if (terr != null && terr.gameObject.activeInHierarchy) anchor = terr;
            else
            {
                var mm = Minimap.instance;
                if (mm != null && mm.m_smallRoot != null) anchor = mm.m_smallRoot.transform as RectTransform;
            }
            if (anchor == null) return;

            var wc = new Vector3[4];
            anchor.GetWorldCorners(wc); // 0=BL, 1=TL, 2=TR, 3=BR
            float unitsPerPixel = anchor.rect.height > 1f ? (wc[1].y - wc[0].y) / anchor.rect.height : 1f;
            float gap = StackGapPx * unitsPerPixel;
            // _root's pivot is top-right (1,1), so setting its world position places our top-right corner there.
            _root.position = new Vector3(wc[3].x, wc[3].y - gap, _root.position.z);
        }

        private void BuildPanel()
        {
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _panel = panelGo;
            _panelRect = panelGo.GetComponent<RectTransform>();
            _panelRect.SetParent(_root, false);
            _panelRect.anchorMin = _panelRect.anchorMax = _panelRect.pivot = new Vector2(1f, 1f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(BoxWidth, 60f);

            var bg = panelGo.GetComponent<Image>();
            bg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(13, PanelFill, GoldBorder, 2);
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;

            // Drag-to-reposition — only grab-able with the pause menu open (DraggableHud gates it). Persists to config.
            var drag = panelGo.AddComponent<DraggableHud>();
            drag.Target = _root; drag.DragGraphic = bg; drag.OnMoved = SavePosition;

            var vlg = panelGo.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(0, 0, 0, 8);
            vlg.spacing = 0f;
            vlg.childControlWidth = true; vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            var fit = panelGo.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildHeader(_panelRect);
            AddPanelDivider(_panelRect);

            var rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(_panelRect, false);
            _rowsRoot = rowsGo.GetComponent<RectTransform>();
            var rvlg = rowsGo.AddComponent<VerticalLayoutGroup>();
            rvlg.padding = new RectOffset(10, 10, 8, 2);
            rvlg.spacing = 9f;
            rvlg.childControlWidth = true; rvlg.childControlHeight = true;
            rvlg.childForceExpandWidth = true; rvlg.childForceExpandHeight = false;
            var rfit = rowsGo.AddComponent<ContentSizeFitter>();
            rfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildHeader(Transform panelParent)
        {
            var hGo = new GameObject("Header", typeof(RectTransform));
            hGo.transform.SetParent(panelParent, false);
            var hlg = hGo.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(12, 8, 5, 5);
            hlg.spacing = 6f;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            var hle = hGo.AddComponent<LayoutElement>();
            hle.minHeight = 30f; hle.preferredHeight = 30f;

            var title = CreateText("Title", hGo.transform, 15, TitleGold, FontStyle.Bold, TextAnchor.MiddleLeft);
            title.text = "Quests";
            var tle = title.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;

            var book = CreateBookButton(hGo.transform, ToggleCollapsed);
            var ble = book.AddComponent<LayoutElement>();
            ble.minWidth = ble.preferredWidth = 24f;
            ble.minHeight = ble.preferredHeight = 24f;
        }

        private void AddPanelDivider(Transform parent)
        {
            var d = new GameObject("HeaderDiv", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            d.transform.SetParent(parent, false);
            d.GetComponent<Image>().color = Divider;
            d.GetComponent<Image>().raycastTarget = false;
            var le = d.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1.5f;
        }

        // ── Rows ───────────────────────────────────────────────────────────────────────────
        private void RebuildRows(List<(QuestDefinition def, ActiveQuestData active)> quests)
        {
            for (int i = _rowsRoot.childCount - 1; i >= 0; i--)
                Destroy(_rowsRoot.GetChild(i).gameObject);
            _timerLabels.Clear();

            bool first = true;
            foreach (var (def, active) in quests)
            {
                if (!first) AddRowDivider();
                first = false;
                AddQuestRow(def, active);
            }
        }

        private void AddRowDivider()
        {
            var d = new GameObject("Div", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            d.transform.SetParent(_rowsRoot, false);
            d.GetComponent<Image>().color = Divider;
            d.GetComponent<Image>().raycastTarget = false;
            var le = d.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = 1f;
        }

        private void AddQuestRow(QuestDefinition def, ActiveQuestData active)
        {
            var qGo = new GameObject("Quest", typeof(RectTransform));
            qGo.transform.SetParent(_rowsRoot, false);
            var qvlg = qGo.AddComponent<VerticalLayoutGroup>();
            qvlg.spacing = 3f;
            qvlg.childControlWidth = true; qvlg.childControlHeight = true;
            qvlg.childForceExpandWidth = true; qvlg.childForceExpandHeight = false;
            var qfit = qGo.AddComponent<ContentSizeFitter>();
            qfit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            qfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Title row: clickable name + optional timer.
            var titleRow = new GameObject("TitleRow", typeof(RectTransform));
            titleRow.transform.SetParent(qGo.transform, false);
            var thlg = titleRow.AddComponent<HorizontalLayoutGroup>();
            thlg.spacing = 6f;
            thlg.childControlWidth = true; thlg.childControlHeight = true;
            thlg.childForceExpandWidth = false; thlg.childForceExpandHeight = false;
            thlg.childAlignment = TextAnchor.MiddleLeft;

            string name = string.IsNullOrEmpty(def.Name) ? def.QuestId : def.Name;
            var nameText = CreateText("Name", titleRow.transform, 14, QuestNameColor, FontStyle.Bold, TextAnchor.MiddleLeft);
            nameText.text = "[ " + name + " ]";
            nameText.raycastTarget = true;
            var nbtn = nameText.gameObject.AddComponent<Button>();
            nbtn.transition = Selectable.Transition.None;
            nbtn.targetGraphic = nameText;
            nbtn.onClick.AddListener(() => { try { QuestBookJournal.Toggle(); } catch { } });
            var nle = nameText.gameObject.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1f;

            var timer = CreateText("Timer", titleRow.transform, 12, TimerRed, FontStyle.Normal, TextAnchor.MiddleRight);
            timer.text = string.Empty;
            _timerLabels[def.QuestId] = timer;

            // Objective lines.
            if (def.QuestTargets != null)
                foreach (var target in def.QuestTargets)
                    AddObjectiveLine(qGo.transform, def, active, target);
        }

        private void AddObjectiveLine(Transform parent, QuestDefinition def, ActiveQuestData active, QuestTarget target)
        {
            var row = new GameObject("Obj", typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            int cur = QuestProgressTracker.GetProgressForTarget(def, active, target);
            int goal = Math.Max(1, target.Amount);
            bool complete = cur >= goal;

            // Verb comes from THIS objective's own type when it has one (e.g. an "Item:" collect line on a
            // "Talk" quest), else the quest-level type. Fixes "Talk to Wood" showing on collect objectives.
            string objType = !string.IsNullOrEmpty(target.Type) ? target.Type : def.QuestType;
            string typeStr = PrettyType(objType);
            string targetName = QuestProgressTracker.GetTargetDisplayName(target);
            string stars = StarSuffix(objType, target.MinLevel);
            string txt = $"<color=#FFA400>- {typeStr}</color> <color=#e9ddc4>{targetName}</color>{stars}";

            var line = CreateText("T", row.transform, 12, Color.white, FontStyle.Normal, TextAnchor.MiddleLeft);
            line.text = txt;
            line.raycastTarget = false;
            var lle = line.gameObject.AddComponent<LayoutElement>();
            lle.flexibleWidth = 1f;

            AddScorePill(row.transform, cur, goal, complete);
        }

        private void AddScorePill(Transform parent, int cur, int goal, bool complete)
        {
            var pill = new GameObject("Pill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pill.transform.SetParent(parent, false);
            var img = pill.GetComponent<Image>();
            img.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(6, complete ? PillGreen : PillOrange, null, 0);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;

            var hlg = pill.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(7, 7, 1, 1);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true; hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;
            var fit = pill.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var ple = pill.AddComponent<LayoutElement>();
            ple.minWidth = 0f;

            var t = CreateText("s", pill.transform, 11, complete ? PillGreenText : PillOrangeText, FontStyle.Bold, TextAnchor.MiddleCenter);
            t.text = cur + " / " + goal;
            t.raycastTarget = false;
        }

        // ── Book icon (procedural — cream pages + gold spine, scales to the chip) ────────────
        private GameObject CreateBookButton(Transform parent, Action onClick)
        {
            var go = new GameObject("BookBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(8, ChipFill, GoldBorder, 2);
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.transition = Selectable.Transition.None;
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            BuildBookGlyph(go.transform);
            return go;
        }

        private void BuildBookGlyph(Transform parent)
        {
            var pages = new GameObject("pages", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            pages.transform.SetParent(parent, false);
            var prt = pages.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0f); prt.anchorMax = new Vector2(1f, 1f);
            prt.offsetMin = new Vector2(7f, 6f); prt.offsetMax = new Vector2(-6f, -6f);
            var pimg = pages.GetComponent<Image>();
            pimg.sprite = FiresCore.UI.FiresRoundedSkin.RoundedSprite(2, Cream, null, 0);
            pimg.type = Image.Type.Sliced;
            pimg.raycastTarget = false;

            var spine = new GameObject("spine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            spine.transform.SetParent(pages.transform, false);
            var srt = spine.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0f, 1f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(3f, 0f); srt.anchoredPosition = new Vector2(3f, 0f);
            var simg = spine.GetComponent<Image>();
            simg.color = GoldBorder; simg.raycastTarget = false;

            for (int i = 0; i < 2; i++)
            {
                var lineGo = new GameObject("l", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                lineGo.transform.SetParent(pages.transform, false);
                var lrt = lineGo.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 0.5f); lrt.anchorMax = new Vector2(1f, 0.5f);
                lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.offsetMin = new Vector2(8f, 0f); lrt.offsetMax = new Vector2(-3f, 0f);
                lrt.sizeDelta = new Vector2(0f, 2f);
                lrt.anchoredPosition = new Vector2(0f, i == 0 ? 3.5f : -3.5f);
                var limg = lineGo.GetComponent<Image>();
                limg.color = new Color(0.42f, 0.3f, 0.16f, 0.85f);
                limg.raycastTarget = false;
            }
        }

        private void ToggleCollapsed()
        {
            _collapsed = !_collapsed;
            var cfg = ConfigManager.Instance;
            if (cfg?.configQuestTrackerCollapsed != null) cfg.configQuestTrackerCollapsed.Value = _collapsed;
            _lastSig = null; // force a fresh rebuild on next expand
            if (_bookChip != null) _bookChip.SetActive(_collapsed);
            if (_panel != null) _panel.SetActive(!_collapsed);
        }

        // ── Timers ─────────────────────────────────────────────────────────────────────────
        private void UpdateTimers(Player local)
        {
            if (_timerLabels.Count == 0) return;
            Dictionary<string, string> map = null;
            try
            {
                if (local.m_customData.TryGetValue("FiresRPGmaker_QuestTimers", out string raw) && !string.IsNullOrEmpty(raw))
                    map = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
            }
            catch { map = null; }

            foreach (var kv in _timerLabels)
            {
                if (kv.Value == null) continue;
                if (map != null && map.TryGetValue(kv.Key, out var iso)
                    && DateTime.TryParseExact(iso, "o", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var exp))
                {
                    var rem = exp - DateTime.UtcNow;
                    kv.Value.text = rem.TotalSeconds > 0 ? "(" + FormatRemaining(rem) + ")" : string.Empty;
                }
                else kv.Value.text = string.Empty;
            }
        }

        private static string FormatRemaining(TimeSpan r)
        {
            if (r.TotalHours >= 1) return $"{(int)r.TotalHours}h {r.Minutes}m";
            if (r.TotalMinutes >= 1) return $"{r.Minutes}m {r.Seconds}s";
            return $"{r.Seconds}s";
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────
        private static string PrettyType(string t)
        {
            if (string.IsNullOrEmpty(t)) return string.Empty;
            switch (t.ToLowerInvariant())
            {
                case "kill":    return "Kill";
                case "collect": return "Collect";
                case "harvest": return "Harvest";
                case "craft":   return "Craft";
                case "build":   return "Build";
                case "move":    return "Reach";
                case "talk":    return "Talk to";
                default:        return char.ToUpper(t[0]) + t.Substring(1);
            }
        }

        private static string StarSuffix(string type, int minLevel)
        {
            if (minLevel <= 0) return string.Empty;
            int stars = string.Equals(type, "Kill", StringComparison.OrdinalIgnoreCase) ? minLevel - 1 : minLevel;
            if (stars <= 0) return string.Empty;
            return $" <color=#4fd24f>({stars}★)</color>";
        }

        private Text CreateText(string name, Transform parent, int size, Color color, FontStyle style, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        private static Font ResolveFont(global::Hud hud)
        {
            try
            {
                var t = hud.GetComponentsInChildren<Text>(true)?.FirstOrDefault(x => x != null && x.font != null);
                if (t != null) return t.font;
            }
            catch { }
            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }
}
