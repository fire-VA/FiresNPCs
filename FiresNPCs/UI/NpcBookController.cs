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
        { "Dressing", "Quest", "Trader", "Info", "Dialogue", "Banker", "Gambler", "Buffers", "Transmog", "Marketplace", "Mail" };

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
        internal RectTransform PatrolButtonRect => _patrolBtnRect;   // the Dressing re-layout puts it under the preview
        private RectTransform _closeBtnRect;
        private RectTransform _anchorBtnRect;
        private const float AnchorToggleDebounce = 0.6f;   // seconds; the manual hit-test and onClick both fire per click
        private float _anchorToggleAt = -10f;   // one anchor toggle per click
        private RectTransform _dragHandleRect;
        private bool _dragging;
        private Vector2 _dragOffset;

        // Manual hover-help: the book runs under a swallowed EventSystem (see Update), so FiresHoverTip's
        // IPointerEnter never fires here — we register each hinted field's rect and poll the mouse ourselves.
        private readonly Dictionary<RectTransform, (string topic, string body)> _hoverFields =
            new Dictionary<RectTransform, (string, string)>();
        private RectTransform _hoverActive;
        private float _hoverStart;
        private bool _hoverShown;

        public bool IsOpen => _panelGo != null && _panelGo.activeSelf;
        // really on screen (the kit shell can hide the book by deactivating ITS root, leaving _panelGo activeSelf)
        public bool IsOnScreen => _panelGo != null && _panelGo.activeInHierarchy;

        // ── entry / lifecycle ────────────────────────────────────────────────
        public static void ShowForNpc(NpcController npc)
        {
            if (npc == null) return;
            // Inherent guard — this is the admin config surface. Callers (Shift+E) already gate, but
            // the method is public static and must not be openable by any other path on a non-admin.
            if (!(FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Only admins can configure NPCs.");
                return;
            }
            if (Instance == null)
            {
                var host = new GameObject("FiresNPCs_NpcBook");
                DontDestroyOnLoad(host);
                Instance = host.AddComponent<NpcBookController>();
            }
            Instance.Open(npc);
            if (Broken && Instance._panelGo != null)
            {
                Instance.Hide();
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "NPC book unavailable (UI bundle failed to load): opening Set Patrol Route");
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] book broken: opened the patrol route popup for '{npc.GetDisplayName()}' instead");
                PatrolRoutePopup.Open(npc, null);
            }
            else
            {
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] book opened for '{npc.GetDisplayName()}'");
                // the shell switch now runs inside Open(), BEFORE the first SelectTab (see Open)
                // say which gate kept the classic book
                // 0.0.841 ([lead] log sweep 11-07): the kit book being OFF by config is the normal classic look, not a fault, so that
                // case is Debug; only "kit book ON but its shell did not build" is a real failure and stays a Warning.
                if (Instance._shell == null)
                {
                    string why = $"classic parchment book shown for '{npc.GetDisplayName()}': kit book {(FiresKitBookSkin.On ? "ON but the shell did not build (see the line above)" : "OFF")} (Core Fires UI override {FiresKitBookSkin.SafeOverride()}, Use classic book UI {FiresKitBookSkin.ClassicBook?.Value.ToString() ?? "unbound"})";
                    if (FiresKitBookSkin.On) Debug.LogWarning("[NpcBookController] " + why);
                    else global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] " + why);
                }
                try { FixNullFonts(Instance._panelGo); Instance.TextHealth = TextDiagnose(Instance._shell != null ? Instance._shell.Root.gameObject : Instance._panelGo, true); }   // game font first
                catch (Exception ex) { Debug.LogWarning("[NpcBookController] text check threw: " + ex.Message); }
            }
        }

        // re-sync each TMP's material
        // to its font's own atlas material when they differ, force a mesh update, and report how many texts actually drew
        // characters. The first 3 empty ones are described, plus each distinct font's glyph/character tables and atlas
        internal float TextHealth = -1f;
        internal static int BadShaderTexts = -1;
        internal static float TextDiagnose(GameObject root, bool repair)
        {
            if (root == null) return -1f;
            int total = 0, drawn = 0, resynced = 0, described = 0;
            var empties = new System.Text.StringBuilder();
            var fonts = new Dictionary<TMPro.TMP_FontAsset, int[]>();   // font -> {texts, drawn}
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (tmp == null || string.IsNullOrWhiteSpace(tmp.text)) continue;
                total++;
                if (repair && tmp.font != null && tmp.font.material != null &&
                    (tmp.fontSharedMaterial == null || tmp.fontSharedMaterial.mainTexture != tmp.font.material.mainTexture))
                { tmp.fontSharedMaterial = tmp.font.material; resynced++; }
                tmp.ForceMeshUpdate(true, true);
                bool ok = tmp.textInfo != null && tmp.textInfo.characterCount > 0 && tmp.textInfo.meshInfo != null
                          && tmp.textInfo.meshInfo.Length > 0 && tmp.textInfo.meshInfo[0].vertexCount > 0;
                if (tmp.font != null) { if (!fonts.TryGetValue(tmp.font, out var c)) fonts[tmp.font] = c = new int[2]; c[0]++; if (ok) c[1]++; }
                if (ok) { drawn++; continue; }
                if (described++ < 3)
                {
                    var cv = tmp.canvas;
                    string txt = tmp.text.Length > 20 ? tmp.text.Substring(0, 20) : tmp.text;
                    empties.Append($" | '{tmp.name}' text='{txt}' font={tmp.font?.name ?? "null"} mat={tmp.fontSharedMaterial?.name ?? "null"} shader={tmp.fontSharedMaterial?.shader?.name ?? "null"} alpha={tmp.color.a:0.00} chars={tmp.textInfo?.characterCount ?? -1} canvas={cv?.name ?? "none"}/{cv?.sortingOrder ?? 0}");
                }
            }
            var fontInfo = new System.Text.StringBuilder();
            foreach (var kv in fonts)
            {
                var f = kv.Key; Texture2D atlas = null;
                try { atlas = f.atlasTextures != null && f.atlasTextures.Length > 0 ? f.atlasTextures[0] : null; } catch { }
                fontInfo.Append($" || font '{f.name}': {kv.Value[1]}/{kv.Value[0]} drew; chars={f.characterTable?.Count ?? -1} atlas={(atlas != null ? atlas.width + "x" + atlas.height : "NULL")} mode={f.atlasPopulationMode}");
            }
            // A bundled material shader can have the right NAME but be an empty copy: count visible texts whose shader is unsupported, or is a different
            // INSTANCE of the game font's shader with the same name (= the bundle's copy). Any of those fails the gate.
            var good = GameFont()?.material?.shader;
            int badShader = 0; string badInfo = "";
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (tmp == null || string.IsNullOrWhiteSpace(tmp.text)) continue;
                var sh = tmp.fontSharedMaterial != null ? tmp.fontSharedMaterial.shader : null;
                bool bad = sh == null || !sh.isSupported || (good != null && sh != good && sh.name == good.name);
                if (!bad) continue;
                if (badShader++ == 0) badInfo = $" first: '{tmp.name}' shader '{sh?.name ?? "null"}' #{(sh != null ? sh.GetInstanceID() : 0)} supported={sh?.isSupported} vs game '{good?.name ?? "null"}' #{(good != null ? good.GetInstanceID() : 0)}";
            }
            BadShaderTexts = badShader;
            float ratio = total > 0 ? (float)drawn / total : 0f;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] text check '{root.name}': {drawn}/{total} texts drew characters, {resynced} material(s) re-synced, {badShader} on a dead/bundled shader{badInfo}{fontInfo}{empties}");
            return ratio;
        }

        private static void TextCheckEnd()
        {
        }

        // every TMP text in the baked book with a null font gets a real one (the game's TMP default, else the
        // font of any loaded TMP text), so TMP's layout math (GetPreferredValues) can't NRE. One line with the count.
        // null-font fallback. The bundled NpcAveria material uses an EMPTY TMP shader, so every baked text drew nothing: any TMP with no font, with the bundled NpcAveria font, or whose material shader
        // isn't supported gets the GAME's loaded font + its own material (what the trader/gambler/mail panels already get).
        static TMPro.TMP_FontAsset s_gameFont;
        static bool Usable(TMPro.TMP_FontAsset f) =>
            f != null && f.name.IndexOf("NpcAveria", StringComparison.OrdinalIgnoreCase) < 0 &&
            f.material != null && f.material.shader != null && f.material.shader.isSupported;

        internal static TMPro.TMP_FontAsset GameFont()
        {
            if (Usable(s_gameFont)) return s_gameFont;
            TMPro.TMP_FontAsset f = null;
            try { f = FiresCore.UI.UIFontConfig.GetFontForCategory(FiresCore.UI.UIFontConfig.FontCategory.Primary); } catch { }
            if (!Usable(f)) { f = null; try { f = TMPro.TMP_Settings.defaultFontAsset; } catch { } }
            if (!Usable(f))
            {
                f = null;
                foreach (var any in Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>())
                {
                    if (!Usable(any)) continue;
                    if (f == null) f = any;
                    if (any.name.IndexOf("Averia", StringComparison.OrdinalIgnoreCase) >= 0) { f = any; break; }
                }
            }
            s_gameFont = f;
            return f;
        }

        internal static int FixNullFonts(GameObject root)
        {
            if (root == null) return 0;
            var game = GameFont();
            int nul = 0, bundled = 0, dead = 0;
            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                string why = tmp.font == null ? "null"
                    : tmp.font.name.IndexOf("NpcAveria", StringComparison.OrdinalIgnoreCase) >= 0 ? "bundled"
                    : (tmp.fontSharedMaterial != null && tmp.fontSharedMaterial.shader != null && !tmp.fontSharedMaterial.shader.isSupported) ? "dead" : null;
                if (why == null || game == null) continue;
                tmp.font = game;
                tmp.fontSharedMaterial = game.material;
                if (why == "null") nul++; else if (why == "bundled") bundled++; else dead++;
            }
            int n = nul + bundled + dead;
            if (n > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] fonts in '{root.name}': {n} TMP text(s) moved to the game font '{game?.name}' (null {nul}, bundled NpcAveria {bundled}, unsupported shader {dead})");
            else global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] fonts OK in '{root.name}' (game font '{game?.name ?? "none found"}', nothing to move)");
            return n;
        }

        // ---- the book inside the kit window (NpcBookShell) when the new look is on ----------------------
        // Override ON + [UI] Use classic book UI = false: the LeftPage moves into the shell's SetupColumn and the RightPage
        // (every tab screen + Show Anchor) into its Page, stretched; the parchment art left behind is switched off. The rail
        // switches tabs through SelectTab; the window's x closes the book. Hide() puts everything back, so classic always works.
        private NpcBookShell _shell;
        // the kit window while the shell is on screen (null in the classic book)
        internal void RefitShellTabs(float floor) { try { _shell?.RefitTabs(floor); } catch { } }
        internal RectTransform ShellSetupColumn => _shell?.SetupColumn;   // the Dressing preview sits in its free lower part
        internal RectTransform ShellPage => _shell?.Page;                 // the Dressing page re-layout (golden dressing_3panel)
        internal void SetShellSetupShare(float share) { try { _shell?.SetSetupShare(share); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] setup share: " + ex.Message); } }
        internal void SetShellPillTabs(bool on) { try { _shell?.SetPillTabs(on); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] pill tabs: " + ex.Message); } }
        internal RectTransform ShellWindow => _shell != null && _shell.Root != null && _shell.Root.gameObject.activeInHierarchy ? _shell.WindowRect : null;
        // the book window's drag
        // handle is a topmost transparent strip over its header band (CoreUiBridge.HeaderStrip, kept last sibling), made once per window,
        // instead of the bare KitHeader root that sat under the window's own art.
        internal RectTransform ShellDragHandle
        {
            get
            {
                var win = _shell?.WindowRect; var head = _shell?.HeaderRect;
                if (win == null) return head;
                var existing = win.Find("CoreDragStrip") as RectTransform;
                if (existing != null) return existing;
                float h = head != null && head.rect.height > 1f ? head.rect.height : 96f;
                return CoreUiBridge.HeaderStrip(win, h) ?? head;
            }
        }
        internal RectTransform ShellHeader => _shell?.HeaderRect;
        internal RectTransform ShellTabs => _shell?.TabsRect;
        // Test hooks: every tab node of the shell (rail or pills), the manual hit-test, and the active tab
        internal List<RectTransform> ShellTabNodes() => _shell != null ? new List<RectTransform>(_shell.TabNodes()) : new List<RectTransform>();
        internal string ShellTabAt(Vector2 screen) => _shell?.TabAt(screen);
        internal string ActiveTab => _activeTab;
        // rpg_ui_tour_test hooks: the registered top-level panels (id -> rect), the tab body and the setup column
        private static readonly Dictionary<string, RectTransform> s_testPanels = new Dictionary<string, RectTransform>();
        internal static Dictionary<string, RectTransform> TestPanels => s_testPanels;
        internal RectTransform TestPage => _shell != null ? _shell.Page : null;
        internal RectTransform TestSetupColumn => _shell != null ? _shell.SetupColumn : null;
        internal RectTransform TestPatrolButton => _patrolBtnRect;
        private sealed class Moved { public Transform T, Parent; public int Sibling; public Vector2 AMin, AMax, OMin, OMax, Pivot; }
        private readonly List<Moved> _moved = new List<Moved>();
        private readonly List<Graphic> _artOff = new List<Graphic>();
        // Core 0.2.569+ FiresShell.RegisterScreenPanel(screenKey, id, rect, label, owner, resizable) / DockService.Unregister(id),
        // called softly so an older Core simply has no layout editing. Core then owns the panel's position + localScale; RPGmaker keeps
        // its size and content.
        internal const string BookScreenKey = "FiresNpcBook", DressingScreenKey = "FiresDressing";
        internal const string OpenTab = "Dressing";   // Shift+E always opens the book on the Dressing room
        private static System.Reflection.MethodInfo s_regPanel, s_regLive, s_unregPanel; private static bool s_layoutLooked;
        internal static bool RegisterLayoutPanel(string screenKey, string id, RectTransform rect, string label, RectTransform header = null)
        {
            if (rect == null) return false;
            s_testPanels[id] = rect;   // rpg_ui_tour_test reads the top-level panels from here
            if (!s_layoutLooked)
            {
                s_layoutLooked = true;
                try
                {
                    var shell = Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore");
                    s_regPanel = shell?.GetMethod("RegisterScreenPanel", new[] { typeof(string), typeof(string), typeof(RectTransform), typeof(string), typeof(string), typeof(bool) });
                    // Core 0.2.570 RegisterLivePanel(key, id, panel, header, label, owner)
                    s_regLive = shell?.GetMethod("RegisterLivePanel", new[] { typeof(string), typeof(string), typeof(RectTransform), typeof(RectTransform), typeof(string), typeof(string) });
                    var dock = Type.GetType("FiresCore.UI.Dock.DockService, FiresUnifiedCore");
                    s_unregPanel = dock?.GetMethod("Unregister", new[] { typeof(string) });
                }
                catch { }
                global::FiresRPGmaker.FiresLogger.LogDebug($"[Layout] Core FiresShell.RegisterScreenPanel {(s_regPanel != null ? "found: the NPC book and Dressing room panels are adjustable (Alt)" + (s_regLive != null ? ", live panels (header drag + resize) too" : "") : "not found (Core older than 0.2.569): no layout editing")}");
            }
            if (s_regLive != null && header != null)
            {
                // A transparent raycast Image on the header root itself, as ShellHub.BuildDragHandles' DragBar does
                if (header.GetComponent<Graphic>() == null) { var hit = header.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); hit.raycastTarget = true; }
                try { s_regLive.Invoke(null, new object[] { screenKey, id, rect, header, label, "FiresRPGmaker" }); global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Layout] live panel {screenKey}/{id} '{label}' (header '{header.name}': drag + resize)"); return true; }
                catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] [Layout] live panel {id}: " + (ex.InnerException ?? ex).Message); }
            }
            if (s_regPanel == null) return false;
            try { s_regPanel.Invoke(null, new object[] { screenKey, id, rect, label, "FiresRPGmaker", true }); global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Layout] registered {screenKey}/{id} '{label}'"); return true; }
            catch (Exception ex) { Debug.LogWarning($"[FiresRPGmaker] [Layout] register {id}: " + (ex.InnerException ?? ex).Message); return false; }
        }
        internal static void UnregisterLayoutPanel(string id)
        {
            s_testPanels.Remove(id);
            if (s_unregPanel == null) return;
            try { s_unregPanel.Invoke(null, new object[] { id }); global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Layout] unregistered {id}"); } catch { }
        }

        private void EnterShell()
        {
            // a shell left from an earlier open (root gone or the pages no longer inside it) is dropped and rebuilt, and lost page
            // refs are found again under the book root, so every open takes the kit path.
            if (_shell != null && (_shell.Root == null || _leftPage == null || !_leftPage.IsChildOf(_shell.Root) || _rightPage == null || !_rightPage.IsChildOf(_shell.Root)))
            {
                Debug.LogWarning($"[NpcBookController] stale kit shell from an earlier open (root {(_shell.Root != null ? "ok" : "gone")}): rebuilding it");
                try { LeaveShell(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] stale shell leave: " + ex.Message); }
                _shell = null;
            }
            if (_root != null && (_leftPage == null || _rightPage == null))
            {
                if (_leftPage == null) _leftPage = FindRec(_root, "LeftPage") ?? (_canvasGo != null ? FindRec(_canvasGo.transform, "LeftPage") : null);   // the shell too
                if (_rightPage == null) _rightPage = FindRec(_root, "RightPage") ?? (_canvasGo != null ? FindRec(_canvasGo.transform, "RightPage") : null);
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] pages found again under the book: left {(_leftPage != null ? "ok" : "null")}, right {(_rightPage != null ? "ok" : "null")}");
            }
            if (_shell != null) { global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] kit shell already on"); return; }
            if (_canvasGo == null || _leftPage == null || _rightPage == null)
            {
                Debug.LogWarning($"[NpcBookController] kit shell NOT entered, the classic book shows: canvas {(_canvasGo != null ? "ok" : "null")}, left page {(_leftPage != null ? "ok" : "null")}, right page {(_rightPage != null ? "ok" : "null")}");
                return;
            }
            try
            {
                _shell = NpcBookShell.Build((RectTransform)_canvasGo.transform, Tabs, _activeTab);
                _shell.TabClicked += t => { try { SelectTab(t); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] shell tab " + t + ": " + ex.Message); } };
                _shell.CloseClicked += () => { try { Hide("x button"); } catch { } };
                MoveInto(_leftPage, _shell.SetupColumn);
                MoveInto(_rightPage, _shell.Page);
                foreach (var g in _panelGo.GetComponentsInChildren<Graphic>(true))
                    if (g != null && g.enabled) { g.enabled = false; _artOff.Add(g); }
                // the book's own tab buttons would duplicate the shell's TabRail; hide them while the shell is up
                foreach (var tab in Tabs)
                {
                    var b = FindRec(_leftPage, tab + "Tab") ?? FindRec(_rightPage, tab + "Tab");
                    if (b != null && b.gameObject.activeSelf) { b.gameObject.SetActive(false); _tabsHidden.Add(b.gameObject); }
                }
                // the old TabsBar keeps its 0.495-0.82 band in the setup column even with its
                // buttons hidden; hide it and lift SetupGroup by that band so the setup fields sit right under the NPC name.
                var bar = FindRec(_leftPage, "TabsBar");
                if (bar != null && bar.gameObject.activeSelf) { bar.gameObject.SetActive(false); _tabsHidden.Add(bar.gameObject); }
                if (FindRec(_leftPage, "SetupGroup") is RectTransform sg)
                {
                    _setupGroup = sg; _sgMin = sg.anchorMin; _sgMax = sg.anchorMax;
                    sg.anchorMin = new Vector2(_sgMin.x, _sgMin.y + 0.325f); sg.anchorMax = new Vector2(_sgMax.x, _sgMax.y + 0.325f);
                }
                // The Dialogue tab's list group shares the same TabsBar band; it is lifted with SetupGroup so it starts right under "Configure NPC".
                if (FindRec(_leftPage, "DialogueListGroup") is RectTransform dg)
                {
                    _dlgGroup = dg; _dgMin = dg.anchorMin; _dgMax = dg.anchorMax;
                    float lift = Mathf.Min(0.325f, 1f - _dgMax.y);
                    dg.anchorMin = new Vector2(_dgMin.x, _dgMin.y + lift); dg.anchorMax = new Vector2(_dgMax.x, _dgMax.y + lift);
                }
                // the kit window draws above the HUD (minimap + plates sat on its top-right corner)
                var cv = _canvasGo.GetComponent<Canvas>();
                if (cv != null) { _canvasOrder = cv.sortingOrder; cv.sortingOrder = BookCanvasSortingOrder; }
                // Vanilla's own HUD hide state (Hud.m_userHidden), which Core's minimap shell already follows; restored to its old value on close.
                // A Core HideHudWhile(owner) for every docked element is requested from Core.
                // Core 0.2.548 FiresShell.HideHudWhile(owner) hides every docked element; m_userHidden stays the fallback.
                if (CoreHideHud(true)) _hudHiddenByCore = true;
                else if (Hud.instance != null) { _hudWasHidden = Hud.instance.m_userHidden; Hud.instance.m_userHidden = true; _hudHiddenByUs = true; }
                ApplySkin();
                _shell.SetActive(_activeTab, "", ShellContext());
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] kit shell on: pages moved into NpcBookShell, {_artOff.Count} parchment graphic(s) off");
                RegisterLayoutPanel(BookScreenKey, "npcbook.window", _shell.WindowRect, "NPC book", ShellDragHandle);
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] kit shell failed, classic book stays: " + ex); LeaveShell(); }
        }

        private void MoveInto(Transform t, RectTransform host)
        {
            var rt = (RectTransform)t;
            _moved.Add(new Moved { T = t, Parent = t.parent, Sibling = t.GetSiblingIndex(), AMin = rt.anchorMin, AMax = rt.anchorMax, OMin = rt.offsetMin, OMax = rt.offsetMax, Pivot = rt.pivot });
            t.SetParent(host, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        // each page's own parent + sibling in the book, taken once at build (the pages are found there)
        private Transform _leftParent, _rightParent; private int _leftSibling = -1, _rightSibling = -1;
        private void RememberPageHomes()
        {
            if (_leftPage != null && _leftParent == null) { _leftParent = _leftPage.parent; _leftSibling = _leftPage.GetSiblingIndex(); }
            if (_rightPage != null && _rightParent == null) { _rightParent = _rightPage.parent; _rightSibling = _rightPage.GetSiblingIndex(); }
        }
        private string PageState(Transform page) => page == null ? "LOST" : (_root != null && page.IsChildOf(_root) ? "ok" : "outside the book");
        private void RescuePage(ref Transform page, Transform home, int sibling, string name)
        {
            if (page == null && _canvasGo != null) page = FindRec(_canvasGo.transform, name);   // still somewhere under the book canvas (shell)
            if (page == null || home == null || page.parent == home) return;
            Debug.LogWarning($"[NpcBookController] {name} was under '{(page.parent != null ? page.parent.name : "<none>")}' at shell exit: moved back to '{home.name}'");
            page.SetParent(home, false);
            if (sibling >= 0) page.SetSiblingIndex(Mathf.Min(sibling, home.childCount - 1));
        }

        private void LeaveShell()
        {
            for (int i = _moved.Count - 1; i >= 0; i--)
            {
                var m = _moved[i];
                if (m.T == null) continue;
                m.T.SetParent(m.Parent, false);
                m.T.SetSiblingIndex(m.Sibling);
                var rt = (RectTransform)m.T;
                rt.anchorMin = m.AMin; rt.anchorMax = m.AMax; rt.pivot = m.Pivot; rt.offsetMin = m.OMin; rt.offsetMax = m.OMax;
            }
            _moved.Clear();
            foreach (var g in _artOff) if (g != null) g.enabled = true;
            _artOff.Clear();
            RestoreSmallText();
            RestoreReadability();
            foreach (var go in _tabsHidden) if (go != null) go.SetActive(true);
            _tabsHidden.Clear();
            if (_setupGroup != null) { _setupGroup.anchorMin = _sgMin; _setupGroup.anchorMax = _sgMax; _setupGroup = null; }
            if (_dlgGroup != null) { _dlgGroup.anchorMin = _dgMin; _dlgGroup.anchorMax = _dgMax; if (_dgPosSet) { _dlgGroup.anchoredPosition = _dgPos; _dlgGroup.localScale = Vector3.one; } _dgPosSet = false; _dlgGroup = null; }
            if (_hudHiddenByUs) { if (Hud.instance != null) Hud.instance.m_userHidden = _hudWasHidden; _hudHiddenByUs = false; }
            if (_hudHiddenByCore) { CoreHideHud(false); _hudHiddenByCore = false; }
            if (_canvasOrder != int.MinValue && _canvasGo != null) { var cv = _canvasGo.GetComponent<Canvas>(); if (cv != null) cv.sortingOrder = _canvasOrder; _canvasOrder = int.MinValue; }
            // with the new look OFF (override off or classic on), one ApplyNpcBook per page restores the classic look
            if (!FiresKitBookSkin.On)
            {
                try { if (_leftPage != null) FiresKitBookSkin.ApplyNpcBook(_leftPage); } catch { }
                try { if (_rightPage != null) FiresKitBookSkin.ApplyNpcBook(_rightPage); } catch { }
            }
            // never destroy the book's pages with the shell. A page still inside the shell (or parked outside the book by a
            // page re-layout) goes back under the book's own page parent before the shell is dropped.
            bool hadShell = _shell != null;
            if (hadShell) UnregisterLayoutPanel("npcbook.window");
            RescuePage(ref _leftPage, _leftParent, _leftSibling, "LeftPage");
            RescuePage(ref _rightPage, _rightParent, _rightSibling, "RightPage");
            if (hadShell) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] pages restored to the book (left {PageState(_leftPage)}, right {PageState(_rightPage)})");
            if (_shell != null) { try { _shell.Destroy(); } catch { } _shell = null; }
        }

        private readonly List<GameObject> _tabsHidden = new List<GameObject>();
        private RectTransform _setupGroup; private Vector2 _sgMin, _sgMax;
        private RectTransform _dlgGroup; private Vector2 _dgMin, _dgMax;
        // A plain anchor lift of min(0.325, 1 - anchorMax.y) is ~0 because DialogueListGroup is already anchored at (or near) the page top, so its header/search/list sit low
        // INSIDE the group. Now measured: the group's topmost visible graphic is moved to 8 GUI under the "Configure NPC" text.
        private Vector2 _dgPos; private bool _dgPosSet; private int _dlgLiftFrames;
        private const int DialogueLiftFrames = 6;   // frames the lift is re-measured after later layouts
        private void LiftDialogueGroupUnderHeader()
        {
            var dg = _dlgGroup; if (dg == null || !dg.gameObject.activeInHierarchy || _leftPage == null) return;
            TMP_Text head = null;
            foreach (var t in _leftPage.GetComponentsInChildren<TMP_Text>(false))
                if (t != null && !t.transform.IsChildOf(dg) && (t.text ?? "").StartsWith("Configure NPC", StringComparison.Ordinal)) { head = t; break; }
            if (head == null) return;
            Canvas.ForceUpdateCanvases();
            head.ForceMeshUpdate();
            float headBottom = head.transform.TransformPoint(head.textBounds.min).y;
            float top = float.MinValue; var wc = new Vector3[4];
            foreach (var g in dg.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.enabled || g.color.a < 0.02f || g.rectTransform == dg) continue;
                g.rectTransform.GetWorldCorners(wc); top = Mathf.Max(top, wc[1].y);
            }
            if (top == float.MinValue) return;
            KeepDialogueGroupInsideColumn(dg);
            var parent = dg.parent as RectTransform; float ps = parent != null && parent.lossyScale.y > 0f ? parent.lossyScale.y : 1f;
            float gapWorld = 8f * dg.lossyScale.y;
            float shift = (headBottom - gapWorld - top) / ps;
            if (Mathf.Abs(shift) < 1f) return;
            if (!_dgPosSet) { _dgPos = dg.anchoredPosition; _dgPosSet = true; }
            dg.anchoredPosition += new Vector2(0f, shift);
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] Dialogue list moved {shift:0} units to sit under 'Configure NPC' (header bottom {headBottom:0}, list top was {top:0})");
        }
        // the list group keeps the
        // baked book's x anchors, which in the shell's (narrower, left-pinned) SetupColumn put its left part outside the column. The
        // group's VISIBLE graphics are measured against the setup column and moved right (and shrunk if wider) so they sit inside with
        // a 12-unit pad. Measured like LiftDialogueGroupUnderHeader, x instead of y.
        private void KeepDialogueGroupInsideColumn(RectTransform dg)
        {
            var col = _shell?.SetupColumn; if (dg == null || col == null) return;
            var wc = new Vector3[4]; col.GetWorldCorners(wc);
            float pad = 12f * col.lossyScale.x, cl = wc[0].x + pad, cr = wc[2].x - pad;
            float l = float.MaxValue, r = float.MinValue;
            foreach (var g in dg.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.enabled || g.color.a < 0.02f || g.rectTransform == dg) continue;
                g.rectTransform.GetWorldCorners(wc); l = Mathf.Min(l, wc[0].x); r = Mathf.Max(r, wc[2].x);
            }
            if (l == float.MaxValue) return;
            if (!_dgPosSet) { _dgPos = dg.anchoredPosition; _dgPosSet = true; }
            if (r - l > cr - cl && r > l)
            {
                float k = (cr - cl) / (r - l);
                var ls = dg.localScale; dg.localScale = new Vector3(ls.x * k, ls.y * k, ls.z);
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] 0.0.705 Dialogue list wider than the setup column: scaled x{k:0.00}");
                Canvas.ForceUpdateCanvases();
                l = float.MaxValue; r = float.MinValue;
                foreach (var g in dg.GetComponentsInChildren<Graphic>(false))
                {
                    if (g == null || !g.enabled || g.color.a < 0.02f || g.rectTransform == dg) continue;
                    g.rectTransform.GetWorldCorners(wc); l = Mathf.Min(l, wc[0].x); r = Mathf.Max(r, wc[2].x);
                }
            }
            float dx = l < cl ? cl - l : (r > cr ? cr - r : 0f);
            if (Mathf.Abs(dx) < 0.5f) return;
            var parent = dg.parent as RectTransform; float px = parent != null && parent.lossyScale.x > 0f ? parent.lossyScale.x : 1f;
            dg.anchoredPosition += new Vector2(dx / px, 0f);
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] 0.0.705 Dialogue list moved {dx / px:0} units to sit inside the setup column (left was {l:0}, column {cl:0}..{cr:0})");
        }

        // the pages' hierarchies are checked on every shell pass for (a) a CanvasGroup partly faded (0 < alpha < 1: the Dressing
        // re-layout fades with alpha 0 and restores it, a partial value is a leftover) -> alpha 1, and (b) a large filled Image drawn
        // AFTER text it covers (a backing that landed later in sibling order) -> moved to its parent's first sibling, behind the content.
        // Each hit is logged once with its path, so the next live log names the exact object.
        private readonly HashSet<int> _overlayLogged = new HashSet<int>();
        private void FixPageOverlays()
        {
            foreach (var page in new[] { _leftPage, _rightPage })
            {
                if (page == null) continue;
                foreach (var cg in page.GetComponentsInChildren<CanvasGroup>(false))
                {
                    if (cg == null || cg.alpha <= 0.01f || cg.alpha >= 0.99f) continue;
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] 0.0.705 D5 dimming CanvasGroup '{PathOf(cg.transform)}' alpha {cg.alpha:0.00} -> 1");
                    cg.alpha = 1f;
                }
                var all = page.GetComponentsInChildren<Graphic>(false);   // depth-first = draw order inside one canvas
                var wc = new Vector3[4];
                for (int i = 0; i < all.Length; i++)
                {
                    var img = all[i] as Image;
                    if (img == null || !img.enabled || img.color.a < 0.05f || img.GetComponentInParent<FiresCore.UI.Shell.Kit.KitLeaf>() != null) continue;
                    if (img.GetComponent<Selectable>() != null || img.GetComponent<Mask>() != null) continue;
                    img.rectTransform.GetWorldCorners(wc);
                    var box = new Rect(wc[0].x, wc[0].y, wc[2].x - wc[0].x, wc[2].y - wc[0].y);
                    if (box.width * box.height < 250f * 250f * img.rectTransform.lossyScale.x * img.rectTransform.lossyScale.y) continue;
                    int covered = 0;
                    for (int j = 0; j < i; j++)
                    {
                        if (!(all[j] is TMP_Text t) || !t.enabled || string.IsNullOrWhiteSpace(t.text) || t.transform.IsChildOf(img.transform)) continue;
                        t.rectTransform.GetWorldCorners(wc);
                        if (box.Contains((wc[0] + wc[2]) * 0.5f)) covered++;
                    }
                    if (covered == 0) continue;
                    img.transform.SetAsFirstSibling();
                    if (_overlayLogged.Add(img.GetInstanceID()))
                        global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] 0.0.705 D5 overlay '{PathOf(img.transform)}' (colour {img.color}, {box.width:0}x{box.height:0}) was drawn over {covered} text(s): moved behind its siblings");
                }
            }
        }
        private static string PathOf(Transform t) { var s = t != null ? t.name : ""; for (var p = t != null ? t.parent : null; p != null && s.Length < 200; p = p.parent) s = p.name + "/" + s; return s; }

        internal RectTransform TestDialogueGroup => _dlgGroup != null ? _dlgGroup : (_leftPage != null ? FindRec(_leftPage, "DialogueListGroup") as RectTransform : null);
        internal RectTransform TestLeftPage => _leftPage as RectTransform;
        private const int BookCanvasSortingOrder = 5000;   // above the HUD
        private int _canvasOrder = int.MinValue;
        private bool _hudWasHidden, _hudHiddenByUs;
        private bool _hudHiddenByCore;
        private static System.Reflection.MethodInfo _hideHudWhile; private static bool _hideHudLooked;
        private static bool CoreHideHud(bool hide)
        {
            try
            {
                if (!_hideHudLooked)
                {
                    _hideHudLooked = true;
                    var t = Type.GetType("FiresCore.UI.Shell.FiresShell, FiresUnifiedCore");
                    _hideHudWhile = t?.GetMethod("HideHudWhile", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string), typeof(bool) }, null);
                    global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] Core HideHudWhile " + (_hideHudWhile != null ? "found" : "missing (m_userHidden fallback)"));
                }
                if (_hideHudWhile == null) return false;
                _hideHudWhile.Invoke(null, new object[] { "RPGmaker.NpcBook", hide });
                return true;
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] HideHudWhile failed: " + ex.Message); return false; }
        }

        // ' NpcBook classifier: restyles the moved pages' controls in place (colours + plate sprites; fonts untouched).
        // Applied to both pages (they live under the shell now), after EnterShell and after every SelectTab.
        private void ApplySkin()
        {
            if (_shell == null) return;
            try { if (_leftPage != null) FiresKitBookSkin.ApplyNpcBook(_leftPage); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] skin left page: " + ex.Message); }
            try { if (_rightPage != null) FiresKitBookSkin.ApplyNpcBook(_rightPage); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] skin right page: " + ex.Message); }
            RaiseSmallText();
            ApplyReadability();
            try { FixPageOverlays(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] overlay check: " + ex.Message); }
        }

        // In the same shell pass that raises the text size, the pages' controls get Core's kit colours (FiresTokens only, as Core's Player /
        // Stats hub pages use them): input/dropdown wells PanelRaised + an Edge (dropdown: Gold) outline, labels/values Text, section
        // labels Gold, buttons Selected fill + Text label (the primary Apply/Save ones Gold-outlined). Originals are recorded and put
        // back by RestoreReadability (LeaveShell); outlines we add are removed. Dropdown option lists are left alone.
        private readonly Dictionary<Graphic, Color> _readCols = new Dictionary<Graphic, Color>();
        private readonly List<Outline> _readOutlines = new List<Outline>();
        private void ReadCol(Graphic g, Color c) { if (g == null) return; if (!_readCols.ContainsKey(g)) _readCols[g] = g.color; g.color = c; }
        private void ReadOutline(Graphic g, Color c)
        {
            if (g == null) return;
            var o = g.GetComponent<Outline>();
            if (o == null) { o = g.gameObject.AddComponent<Outline>(); _readOutlines.Add(o); }
            else if (!_readOutlines.Contains(o)) return;   // an outline the page already had stays as it is
            o.effectColor = c; o.effectDistance = new Vector2(1.5f, -1.5f);
        }
        private static bool InTemplate(Transform t)
        {
            var dd = t.GetComponentInParent<TMP_Dropdown>(true);
            return dd != null && dd.template != null && t.IsChildOf(dd.template);
        }
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private void ApplyReadability()
        {
            Color text = FiresCore.UI.Shell.Kit.FiresTokens.Text, gold = FiresCore.UI.Shell.Kit.FiresTokens.Gold, edge = FiresCore.UI.Shell.Kit.FiresTokens.Edge;
            Color raised = FiresCore.UI.Shell.Kit.FiresTokens.PanelRaised, sel = FiresCore.UI.Shell.Kit.FiresTokens.Selected, muted = FiresCore.UI.Shell.Kit.FiresTokens.TextMuted;
            int fields = 0, dds = 0, btns = 0, labels = 0;
            var owned = new HashSet<TMP_Text>();
            foreach (var page in new[] { _leftPage, _rightPage })
            {
                if (page == null) continue;
                foreach (var f in page.GetComponentsInChildren<TMP_InputField>(true))
                {
                    if (f == null || InTemplate(f.transform)) continue;
                    var bg = f.targetGraphic ?? f.GetComponent<Image>();
                    if (bg != null) { ReadCol(bg, new Color(raised.r, raised.g, raised.b, 1f)); ReadOutline(bg, _pickerInputs.Contains(f) ? gold : edge); }   // a field picker's box = the dropdown edge
                    if (f.textComponent != null) { ReadCol(f.textComponent, text); owned.Add(f.textComponent); }
                    if (f.placeholder is TMP_Text ph) { ReadCol(ph, muted); owned.Add(ph); }
                    fields++;
                }
                foreach (var d in page.GetComponentsInChildren<TMP_Dropdown>(true))
                {
                    if (d == null || _fieldPickers.Contains(d)) continue;   // a field picker stays a clear "v" inside its field
                    var bg = d.targetGraphic ?? d.GetComponent<Image>();
                    if (bg != null) { ReadCol(bg, new Color(raised.r, raised.g, raised.b, 1f)); ReadOutline(bg, gold); }
                    if (d.captionText != null) { ReadCol(d.captionText, text); owned.Add(d.captionText); }
                    dds++;
                }
                foreach (var b in page.GetComponentsInChildren<Button>(true))
                {
                    if (b == null || InTemplate(b.transform) || b.GetComponent<TMP_Dropdown>() != null) continue;
                    var lab = b.GetComponentInChildren<TMP_Text>(true);
                    if (lab == null || string.IsNullOrWhiteSpace(lab.text)) continue;   // icon / hit-only buttons keep their look
                    var bg = b.targetGraphic as Graphic ?? b.GetComponent<Image>();
                    if (bg == null || bg.color.a < 0.05f) continue;   // invisible hit areas (tab rails, pills) stay invisible
                    string s = lab.text.Trim();
                    bool primary = s.StartsWith("Apply", StringComparison.OrdinalIgnoreCase) || s.StartsWith("Save", StringComparison.OrdinalIgnoreCase);
                    ReadCol(bg, new Color(sel.r, sel.g, sel.b, 1f)); ReadOutline(bg, primary ? gold : edge);
                    ReadCol(lab, primary ? FiresCore.UI.Shell.Kit.FiresTokens.GoldBright : text); owned.Add(lab);
                    btns++;
                }
                foreach (var t in page.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (t == null || owned.Contains(t) || InTemplate(t.transform) || string.IsNullOrWhiteSpace(t.text)) continue;
                    if (t.GetComponentInParent<TMP_InputField>(true) != null) continue;
                    string s = t.text.Trim();
                    bool section = s.Length > 2 && s == s.ToUpperInvariant() && s.Any(char.IsLetter);   // SETUP / SAVE / DIALOGUES ... headers
                    Color want = section ? gold : text;
                    if (!section && Lum(t.color) >= Lum(text) - 0.02f) continue;   // already bright (gold titles, cream) - left alone
                    ReadCol(t, new Color(want.r, want.g, want.b, t.color.a)); labels++;
                }
            }
            if (fields + dds + btns + labels > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] readability (kit tokens): {fields} field(s), {dds} dropdown(s), {btns} button(s), {labels} label(s) restyled ({_readCols.Count} recorded)");
        }
        private void RestoreReadability()
        {
            foreach (var kv in _readCols) if (kv.Key != null) kv.Key.color = kv.Value;
            _readCols.Clear();
            foreach (var o in _readOutlines) if (o != null) UnityEngine.Object.Destroy(o);
            _readOutlines.Clear();
        }

        // The field IS the dropdown. Every "XDD" arrow picker (ProfileDD on
        // every tab, ModelDD, QuestIdDD) is moved INTO its input's box as a clear hit area over the right edge showing only its
        // "v" (no caption), the input's text stops before the "v", and the box gets the dropdown's Gold edge (ApplyReadability),
        // so it reads exactly like the baked NPC Type dropdown: one field, current value (or empty), small "v" at the right.
        // The input still takes typing (a new Trader / Quest save-target name); the "v" opens the list, a pick fills the field.
        // The option list opens under the whole field (its width), right-aligned to it.
        private readonly HashSet<TMP_Dropdown> _fieldPickers = new HashSet<TMP_Dropdown>();
        private readonly HashSet<TMP_InputField> _pickerInputs = new HashSet<TMP_InputField>();
        internal const float PickerArrowW = 30f;
        internal static bool IsFieldPicker(TMP_Dropdown d) => d != null && Instance != null && Instance._fieldPickers.Contains(d);
        private void MakeFieldPicker(TMP_Dropdown dd, TMP_InputField input)
        {
            if (dd == null || input == null) return;
            var box = (RectTransform)input.transform;
            var rt = (RectTransform)dd.transform;
            if (rt.parent != box) rt.SetParent(box, false);
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(PickerArrowW, 0f); rt.anchoredPosition = Vector2.zero; rt.localScale = Vector3.one;
            rt.SetAsLastSibling();
            var bg = dd.targetGraphic as Graphic ?? dd.GetComponent<Image>();
            if (bg != null)
            {
                if (_readCols.ContainsKey(bg)) _readCols[bg] = new Color(0f, 0f, 0f, 0f);
                bg.color = new Color(0f, 0f, 0f, 0f); bg.raycastTarget = true;   // clear, still clickable
                var o = bg.GetComponent<Outline>(); if (o != null) { _readOutlines.Remove(o); UnityEngine.Object.Destroy(o); }
            }
            if (dd.captionText != null) { dd.captionText.text = ""; dd.captionText.gameObject.SetActive(false); }
            var tpl = dd.template;
            if (tpl != null)
            {
                float w = Mathf.Max(box.rect.width, 200f);
                tpl.anchorMin = new Vector2(1f, tpl.anchorMin.y); tpl.anchorMax = new Vector2(1f, tpl.anchorMax.y);
                tpl.pivot = new Vector2(1f, tpl.pivot.y); tpl.sizeDelta = new Vector2(w, tpl.sizeDelta.y);
                tpl.anchoredPosition = new Vector2(0f, tpl.anchoredPosition.y);
            }
            if (_pickerInputs.Add(input) && input.textViewport != null)
                input.textViewport.offsetMax = new Vector2(input.textViewport.offsetMax.x - PickerArrowW, input.textViewport.offsetMax.y);
            var ibg = input.targetGraphic ?? input.GetComponent<Image>();
            if (ibg != null) { var io = ibg.GetComponent<Outline>(); if (io != null) io.effectColor = FiresCore.UI.Shell.Kit.FiresTokens.Gold; }
            dd.gameObject.SetActive(true);
            if (_fieldPickers.Add(dd)) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] field picker: '{dd.name}' is now the 'v' inside '{input.transform.parent?.name}/{input.name}' (NPC Type look, no caption)");
        }

        // while the kit shell is on,
        // every page text below the book's body size is raised to it (BodyFont, the kit's body size the other tabs' labels use).
        // Originals are recorded and put back by RestoreSmallText (LeaveShell). Dropdown option lists and kit nodes are left alone.
        internal const float BodyFont = 14f;
        private readonly Dictionary<TMPro.TMP_Text, float> _raisedFonts = new Dictionary<TMPro.TMP_Text, float>();
        internal int TestRaisedFonts => _raisedFonts.Count;
        private void RaiseSmallText()
        {
            int n = 0;
            foreach (var page in new[] { _leftPage, _rightPage })
            {
                if (page == null) continue;
                foreach (var t in page.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    if (t == null || t.enableAutoSizing || t.fontSize >= BodyFont || t.fontSize <= 1f) continue;
                    var dd = t.GetComponentInParent<TMPro.TMP_Dropdown>(true);
                    if (dd != null && dd.template != null && t.transform.IsChildOf(dd.template)) continue;   // option list rows keep their 24-unit pitch
                    if (!_raisedFonts.ContainsKey(t)) _raisedFonts[t] = t.fontSize;
                    t.fontSize = BodyFont; n++;
                }
            }
            if (n > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] {n} page text(s) below {BodyFont:0} raised to the book body size ({_raisedFonts.Count} recorded)");
        }
        private void RestoreSmallText()
        {
            foreach (var kv in _raisedFonts) if (kv.Key != null) kv.Key.fontSize = kv.Value;
            _raisedFonts.Clear();
        }

        private string ShellContext() => _npc != null ? $"{_npc.GetDisplayName()} · {_npc.npcType}" : "";

        // Test hook: switch tabs the way the shell's rail does; returns the shell rail's visible tab count (-1 = no shell)
        internal static int TestSelectTab(string tab)
        {
            var b = Instance;
            if (b == null || !b.IsOpen || Array.IndexOf(Tabs, tab) < 0) return -1;
            b.SelectTab(tab);
            if (b._shell == null) return -1;
            int n = 0;
            // count only the shell's own buttons (rail + window), not the moved pages' controls
            foreach (var btn in b._shell.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false))
                if (btn != null && btn.isActiveAndEnabled && !btn.transform.IsChildOf(b._shell.Page) && !btn.transform.IsChildOf(b._shell.SetupColumn)) n++;
            return n;
        }

        internal static bool Broken;
        private static int s_unhooked;

        private void Open(NpcController npc)
        {
            if (!EnsureBuilt()) { Debug.LogError("[NpcBookController] build failed; cannot open book"); return; }
            _npc = npc;
            if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);   // Hide turns the canvas off
            s_openFrame = Time.frameCount;
            // Keep the left "Configure NPC" Profile field in lockstep with module-tab saves: any SaveToZDO
            // for this NPC re-reads the active profile onto the left page. Re-subscribe (idempotent) per open.
            NpcController.ProfileChanged -= OnNpcProfileChanged;
            NpcController.ProfileChanged += OnNpcProfileChanged;
            try { npc.LoadFromZDO(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] LoadFromZDO: " + ex.Message); }
            // A populate that throws is logged with its tab and the book still opens.
            try { PopulateLeft(); } catch (Exception ex) { Debug.LogError("[NpcBookController] PopulateLeft threw (book opens anyway): " + ex); }
            // The Dressing room's first open drew the classic layout when SelectTab(Dressing) ran BEFORE the panel was active and
            // BEFORE Show() entered the kit shell, so DressingThreePanel.Attach saw ShellMode false and zero-size rects. So the panel
            // is made active and the shell is entered first, then the SAME SelectTab path a tab click runs.
            _panelGo.SetActive(true);
            ApplyBookScale();   // user-configurable overall UI scale ([UI] BookScale)
            try { if (FiresKitBookSkin.On) EnterShell(); else LeaveShell(); }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] shell switch: " + ex.Message); }
            // 0.0.809 (Fire 10-31: the NPC / quest editor book gets the same bottom-right resize as our other windows, scaling every
            // element): one of Core's live panels, like the quest journal. [UI] BookScale above stays the base scale; the grip
            // multiplies it and Core saves it per player.
            global::FiresRPGmaker.UI.CoreUiBridge.MakeLivePanelWhenShown(this, "NpcEditorBook", _panelGo.GetComponent<RectTransform>(), hitBounds: BookArtBounds.Ensure(_panelGo.GetComponent<RectTransform>(), _leftPage as RectTransform, _rightPage as RectTransform, () => _shell?.WindowRect));
            Canvas.ForceUpdateCanvases();   // rects known before the Dressing layout measures them
            // the book always opens on Dressing;
            // the NPC's own module tab is one click away in the tab row
            try { SelectTab(OpenTab); } catch (Exception ex) { Debug.LogError($"[NpcBookController] SelectTab {OpenTab} threw (book opens anyway): " + ex); }
            RefreshAnchorLabel();   // the button says Hide Anchor when this NPC's orb is showing
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] opened on '{OpenTab}' for '{npc.GetDisplayName()}' (type {npc.npcType}, its module tab = {TabForType(npc.npcType)}, kit shell {(_shell != null ? "on" : "off")} before the first tab)");
            try { GUIManager.BlockInput(true); } catch { }
            try { GUIManager.ApplyGameFontToAll(_panelGo.transform); } catch { }
            // Valheim's crosshair "HoverName" raycastTarget sits at screen-center and would eat clicks on the
            // book's center band (+Add, currency/reward inputs, X buttons). Core utility suppresses it while open.
            try { FiresCore.UI.CrosshairRaycast.Suppress(_crosshairRaycasts); } catch { }
            VaPlayerPanel(true);
        }

        // VAInventory 1.0.105+ VerdantsAscent.Managers.VAInventoryApi Suppress/ReleasePlayerPanel(owner), called softly by reflection, so
        // without VAInventory (or an older one) this is a no-op. Released on close and on logout (OnDestroy).
        private const string VaPanelOwner = "FiresRPGmaker.NpcBook";
        private static System.Reflection.MethodInfo s_vaSuppress, s_vaRelease;
        private static bool s_vaResolved;
        internal static void VaPlayerPanel(bool suppress)
        {
            try
            {
                if (!s_vaResolved)
                {
                    s_vaResolved = true;
                    var api = Type.GetType("VerdantsAscent.Managers.VAInventoryApi, VAInventory", false);
                    s_vaSuppress = api?.GetMethod("SuppressPlayerPanel", new[] { typeof(string) });
                    s_vaRelease = api?.GetMethod("ReleasePlayerPanel", new[] { typeof(string) });
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcBook] VAInventoryApi {(api != null ? "found" : "not installed (no-op)")}");
                }
                var m = suppress ? s_vaSuppress : s_vaRelease;
                if (m == null) return;
                m.Invoke(null, new object[] { VaPanelOwner });
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcBook] VA player panel {(suppress ? "suppressed" : "released")} while the NPC book {(suppress ? "is open" : "closed")}");
            }
            catch (Exception ex) { Debug.LogWarning($"[NpcBook] VA player panel toggle failed: {ex.Message}"); }
        }

        private readonly List<Graphic> _crosshairRaycasts = new List<Graphic>();

        // User-configurable overall book scale ([UI] BookScale). Scaling the root panel's localScale scales
        // EVERYTHING — text, fields, buttons — evenly from the centered pivot, and the manual hit-tests read
        // scaled world rects so tabs/buttons/drag still line up. Re-applies live when the config changes.
        private bool _bookScaleHooked;
        private void ApplyBookScale()
        {
            if (_panelGo == null) return;
            float s = 1f;
            try { s = Mathf.Clamp(ConfigManager.Instance?.configBookScale?.Value ?? 1f, 0.5f, 2f); } catch { }
            _panelGo.transform.localScale = new Vector3(s, s, 1f);
            if (!_bookScaleHooked)
            {
                _bookScaleHooked = true;
                try { ConfigManager.Instance.configBookScale.SettingChanged += (_, __) => { if (IsOpen) ApplyBookScale(); }; } catch { }
            }
        }

        public void Hide() => Hide("closed");   // also leaves the kit shell

        // A CoreDragStrip under FiresNPCs_NpcBookCanvas could stay over the player panel header after a close: every close path names its reason, and the close always
        // turns the book canvas off, removes its drag strips and releases the HUD hold, logged in one line.
        internal static string LastCloseReason;
        internal GameObject TestCanvas => _canvasGo;   // rpg_npcbook_close_test
        internal bool TestHudHeld => _hudHiddenByCore || _hudHiddenByUs;
        public void Hide(string reason)
        {
            bool hadHud = _hudHiddenByCore || _hudHiddenByUs;
            HideCore();
            int strips = 0;
            if (_canvasGo != null)
            {
                foreach (var t in _canvasGo.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "CoreDragStrip") { t.gameObject.SetActive(false); UnityEngine.Object.Destroy(t.gameObject); strips++; }
                _canvasGo.SetActive(false);
            }
            if (_hudHiddenByUs) { if (Hud.instance != null) Hud.instance.m_userHidden = _hudWasHidden; _hudHiddenByUs = false; }
            if (_hudHiddenByCore) { CoreHideHud(false); _hudHiddenByCore = false; }
            LastCloseReason = reason; s_closeFrame = Time.frameCount;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBook] closed ({reason}): canvas off, drag strips {strips} removed, hud hold {(hadHud ? "released" : "none held")}");
        }

        // Shift+E (the human path, NpcBridgeProviders.OpenConfigPanel) toggles: the same NPC's book open = close it
        private static int s_closeFrame = -10, s_openFrame = -10;
        private static bool TypingInField()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var go = es != null ? es.currentSelectedGameObject : null;
            if (go == null) return false;
            var tmp = go.GetComponent<TMPro.TMP_InputField>(); if (tmp != null && tmp.isFocused) return true;
            var leg = go.GetComponent<InputField>(); return leg != null && leg.isFocused;
        }
        internal static bool ToggleClose(NpcController npc)
        {
            if (Time.frameCount - s_closeFrame <= 1) return true;   // the book's own Shift+E closed it this frame: don't reopen
            if (Instance == null || !Instance.IsOpen || npc == null || Instance._npc != npc) return false;
            Instance.Hide("shift+e toggle");
            return true;
        }

        // safety net (Update): the NPC gone / walked away (> 30 m) / no local player / the hub opened = close; the book
        // not open but its canvas, drag strip or HUD hold still on = tear those down.
        private const float WalkAwayMeters = 30f;
        private bool SafetyNetClose()
        {
            if (!IsOpen)
            {
                bool leftover = _hudHiddenByCore || _hudHiddenByUs || (_canvasGo != null && _canvasGo.activeSelf && _shell != null);
                if (leftover) Hide("safety net: book inactive with holds");
                return leftover;
            }
            string why = null;
            if (_npc == null || !_npc.gameObject.activeInHierarchy) why = "npc gone";
            else if (Player.m_localPlayer == null) why = "no local player";
            else if (Vector3.Distance(Player.m_localPlayer.transform.position, _npc.transform.position) > WalkAwayMeters) why = "walked away";
            else { try { if (FiresCore.UI.Shell.FiresShell.IsOpen) why = "hub opened"; } catch { } }
            if (why == null) return false;
            Hide(why);
            return true;
        }

        private void HideCore()
        {
            // the Dressing room closes FIRST.
            // It restores the left page to its recorded parent, the shell's SetupColumn; leaving the shell first destroyed that column
            // and the left page with it.
            if (_dressingCtrl != null) { try { _dressingCtrl.Hide(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] dressing close: " + ex.Message); } }
            LeaveShell();
            try { FiresCore.UI.CrosshairRaycast.Restore(_crosshairRaycasts); } catch { }
            NpcController.ProfileChanged -= OnNpcProfileChanged;
            _bookOptionPopup?.Close();
            if (_optionPopup != null) _optionPopup.gameObject.SetActive(false);
            _editingOptionIndex = -1;
            if (_hoverShown) { _hoverShown = false; _hoverActive = null; try { FiresCore.UI.FiresHoverTip.HideTip(); } catch { } }
            if (_panelGo != null) _panelGo.SetActive(false);
            VaPlayerPanel(false);
            _npc = null;
            try { if (!GUIManager.IsCustomPanelOpen()) GUIManager.BlockInput(false); } catch { }
        }

        // A SaveToZDO for the NPC we're editing fired — refresh the left page so its Profile field reflects
        // whatever a module tab just saved (the "save on one side shows on the other" guarantee).
        private void OnNpcProfileChanged(NpcController npc)
        {
            if (!IsOpen || npc == null || npc != _npc) return;
            PopulateLeft();
        }

        private void Update()
        {
            if (SafetyNetClose()) return;
            if (!IsOpen) return;
            if (_dlgLiftFrames > 0) { _dlgLiftFrames--; LiftDialogueGroupUnderHeader(); }   // re-measured after later layouts
            // Esc in the Dressing room (vanilla's InventoryGui eats that Esc, so Menu.Show never fires) and Shift+E while
            // the book is up (the game's interact is blocked by the book) close it; not while typing in a field
            if (Time.frameCount - s_openFrame > 1 && !TypingInField())
            {
                if (_activeTab == "Dressing" && Input.GetKeyDown(KeyCode.Escape)) { Hide("esc (dressing)"); return; }
                if (Input.GetKeyDown(KeyCode.E) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) { Hide("shift+e toggle"); return; }
            }
            // The Hud may re-enable the crosshair raycast on its own update; keep it off while the book is open.
            FiresCore.UI.CrosshairRaycast.Reassert(_crosshairRaycasts);
            // Escape owned by the central Menu.Show hook (CloseAllPanels → NpcBookController.Hide) — no
            // self-close here (avoids the menu-opens race).
            // Valheim hides/relocks the cursor every frame while a panel is up — force it visible.
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }

            // The Dressing tab opens the player InventoryGui, which swallows the UI event system —
            // so the tab buttons' onClick never fires and the user gets stuck on Dressing until they
            // close the whole book. While Dressing is active, detect tab clicks manually and route
            // them to SelectTab (its Hide closes the inventory on the way out).
            if (_activeTab == "Dressing" && Input.GetMouseButtonDown(0))
                HandleManualTabClick();

            // The X (close) and Show Anchor buttons are driven by manual hit-testing on EVERY tab —
            // the baked CloseButton's onClick has never fired reliably (event-system swallowing +
            // overlay ordering), so raw Input + RectangleContainsScreenPoint is the one path that
            // always works (same pattern as the tabs / Set Patrol / drag handle).
            if (Input.GetMouseButtonDown(0) && !PatrolRoutePopup.IsOpen)
            {
                Vector2 mouse = Input.mousePosition;
                if (_closeBtnRect != null && Shown(_closeBtnRect)
                    && RectTransformUtility.RectangleContainsScreenPoint(_closeBtnRect, mouse, null))
                {
                    OnCloseClicked();
                    return;
                }
                if (_anchorBtnRect != null && Shown(_anchorBtnRect)
                    && RectTransformUtility.RectangleContainsScreenPoint(_anchorBtnRect, mouse, null))
                {
                    OnShowAnchorClicked();
                    return;
                }
            }

            HandleDrag();
            TickHover();
        }

        // Manual hover-help poll — the swallowed EventSystem means IPointerEnter never fires on book
        // fields, so we hit-test each registered rect against the cursor and drive the shared tooltip
        // ourselves (0.3s dwell, hidden on move-off). Honors the ShowAdminHints toggle via ShowAt.
        private void TickHover()
        {
            if (!FiresCore.UI.FiresHoverTip.Enabled)
            {
                if (_hoverShown) { _hoverShown = false; _hoverActive = null; FiresCore.UI.FiresHoverTip.HideTip(); }
                return;
            }
            Vector2 mouse = Input.mousePosition;
            RectTransform hit = null;
            foreach (var kv in _hoverFields)
            {
                var rt = kv.Key;
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null)) { hit = rt; break; }
            }
            if (hit != _hoverActive)
            {
                _hoverActive = hit;
                _hoverStart = Time.unscaledTime;
                if (_hoverShown) { _hoverShown = false; FiresCore.UI.FiresHoverTip.HideTip(); }
            }
            if (_hoverActive != null && !_hoverShown && Time.unscaledTime - _hoverStart >= 0.3f)
            {
                _hoverShown = true;
                var info = _hoverFields[_hoverActive];
                FiresCore.UI.FiresHoverTip.ShowAt(info.topic, info.body, mouse);
            }
        }

        // Manual hit-test of the left-page tab buttons, used while InventoryGui blocks the event system.
        private void HandleManualTabClick()
        {
            // While the patrol popup is up it owns all clicks; don't also process tab/button hits behind it.
            if (PatrolRoutePopup.IsOpen) return;

            Vector2 mouse = Input.mousePosition;

            // "Set Patrol Route" button (right page / dressing screen). Its onClick is blocked by the open
            // InventoryGui, so detect it manually: open the patrol popup over the dressing room.
            if (_patrolBtnRect != null && Shown(_patrolBtnRect)
                && RectTransformUtility.RectangleContainsScreenPoint(_patrolBtnRect, mouse, null))
            {
                OnSetPatrolClicked();
                return;
            }

            // the kit shell's tab pills (the classic left-page tabs are hidden while the shell is up)
            if (_shell != null) { var sid = _shell.TabAt(mouse); if (sid != null) { global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] tab '{sid}' clicked (manual hit-test, Dressing)"); OnTabClicked(sid); return; } }
            if (_leftPage == null) return;
            foreach (var tab in Tabs)
            {
                if (tab == _activeTab) continue;
                if (!(FindRec(_leftPage, tab + "Tab") is RectTransform rt)) continue;
                if (!Shown(rt)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, null))
                {
                    OnTabClicked(tab);
                    return;
                }
            }
        }

        // Each control runs from whichever sees the click first this frame: the manual hit-test (the mouse while the
        // InventoryGui swallows events) or the button's own onClick (a click the EventSystem delivers: mouse, gamepad, a test).
        private int _bookClickFrame = -1;
        private bool TakeBookClick()
        {
            if (_bookClickFrame == Time.frameCount) return false;
            _bookClickFrame = Time.frameCount;
            return true;
        }

        // the manual hit-tests ignore a button that is
        // faded out (a parent CanvasGroup at alpha ~0, as the Dressing page fades the book's own buttons) - the book's hidden
        // "Show anchor" sat under Apply Changes and fired with it.
        private static bool Shown(RectTransform rt)
        {
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            for (var t = rt.transform; t != null; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && (cg.alpha < 0.01f || !cg.blocksRaycasts)) return false;
                if (cg != null && cg.ignoreParentGroups) break;
            }
            return true;
        }

        private void OnCloseClicked() { if (TakeBookClick()) Hide("x button"); }

        private void OnShowAnchorClicked()
        {
            if (!TakeBookClick()) return;
            // The manual hit-test fires on mouse DOWN and the Button's own onClick on mouse UP, a later frame, so TakeBookClick's one-frame guard let one click
            // toggle twice. One toggle per click: a 0.6 s window, the same as the stats toggles.
            if (Time.unscaledTime - _anchorToggleAt < AnchorToggleDebounce) return;
            _anchorToggleAt = Time.unscaledTime;
            // Show / hide the anchor orb: the same toggle the unified Dressing room has (DressingThreePanel "Show anchor" / "Hide anchor"); the book stays open.
            if (_npc == null) return;
            if (NpcAnchorOrbViz.ActiveGuid != null) NpcAnchorOrbViz.Hide(); else NpcAnchorOrbViz.Show(_npc);
            RefreshAnchorLabel();
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBook] anchor toggle: {(NpcAnchorOrbViz.ActiveGuid != null ? "shown" : "hidden")} for '{_npc.GetDisplayName()}'");
        }

        private void RefreshAnchorLabel()
        {
            if (_anchorBtnRect == null) return;
            string s = NpcAnchorOrbViz.ActiveGuid != null ? "Hide Anchor" : "Show Anchor";
            foreach (var t in _anchorBtnRect.GetComponentsInChildren<TMPro.TMP_Text>(true)) t.text = s;
            foreach (var t in _anchorBtnRect.GetComponentsInChildren<UnityEngine.UI.Text>(true)) t.text = s;
        }

        private void OnSetPatrolClicked()
        {
            if (PatrolRoutePopup.IsOpen || !TakeBookClick()) return;
            if (_npc != null) PatrolRoutePopup.Open(_npc, this);
        }

        private void OnTabClicked(string tab)
        {
            if (tab == _activeTab || PatrolRoutePopup.IsOpen || !TakeBookClick()) return;
            SelectTab(tab);
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
            FixNullFonts(_panelGo);   // before anything measures text
            _panelGo.name = "FiresRPGmaker_NpcPanel";
            _root = _panelGo.transform;

            var rt = _panelGo.GetComponent<RectTransform>();
            if (rt != null) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = Vector2.zero; }

            _leftPage = FindRec(_root, "LeftPage");
            _rightPage = FindRec(_root, "RightPage");
            RememberPageHomes();
            if (_leftPage == null || _rightPage == null)
            { Debug.LogError("[NpcBookController] book is missing LeftPage/RightPage — bad prefab"); return false; }

            _optionPopup = FindRec(_root, "OptionPopup");
            WireTabs();
            WireButtons();
            WireToggleVisuals();
            WireDialogueExtras();
            NormalizeInteractive(_panelGo.transform);
            // npcbook.prefab carries 26 nested Canvases; a GraphicRaycaster only sees graphics of ITS canvas, so
            // without one per nested canvas every tab/button click found "nothing under the screen point" (FDT 582).
            int addedRc = 0;
            foreach (var c in _panelGo.GetComponentsInChildren<Canvas>(true))
                if (c != null && c.GetComponent<GraphicRaycaster>() == null) { c.gameObject.AddComponent<GraphicRaycaster>(); addedRc++; }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[NpcBook] GraphicRaycaster added to {addedRc} nested canvas(es)");

            // Manual-hit-test targets (see Update): the baked X, and the runtime Show Anchor button
            // on the left page beside the tree emblem.
            _closeBtnRect = FindRec(_root, "CloseButton") as RectTransform;
            var closeBtn = _closeBtnRect != null ? _closeBtnRect.GetComponent<UnityEngine.UI.Button>() : null;
            if (closeBtn != null) { closeBtn.onClick.RemoveAllListeners(); closeBtn.onClick.AddListener(OnCloseClicked); }
            EnsureAnchorButton();
            AddDragHandle();
            _panelGo.SetActive(false);
            _built = true;
            // When npcbook.prefab's baked components fail to load ("The referenced script ... is missing!"), its Buttons can't be found and the book
            // renders invisible. Detect that once, say so loudly, and let ShowForNpc fall back to the patrol popup.
            Broken = s_unhooked >= 8 || _panelGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length < 20;
            if (Broken) Debug.LogError($"[NpcBookController] npcbook.prefab did not load its UI scripts ({s_unhooked} baked button(s) not found, {_panelGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length} graphic(s)): the book can't render. Shift+E opens the patrol route popup instead until the firesnpcsui bundle is fixed.");
            global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] book loaded + wired");
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

        // Defines the top-edge grab band (across the two tree emblems): panel-relative, sitting just above the
        // tabs and left of the X (x>=0.78) / Set Patrol Route (x>=0.69) buttons, so dragging there can't conflict
        // with them. The pages are insets at panel-y 0.305..0.708, so the trees live at panel-y ~0.69 — NOT the
        // cover margin above. Dragging is driven from Update via RectangleContainsScreenPoint + raw Input (NOT
        // the EventSystem), because the Dressing tab opens InventoryGui which swallows UI events — same reason
        // the tabs and Set Patrol button use manual hit-testing.
        private void AddDragHandle()
        {
            if (_root == null) return;
            var go = new GameObject("DragHandle", typeof(RectTransform));
            go.transform.SetParent(_root, false);
            _dragHandleRect = (RectTransform)go.transform;
            _dragHandleRect.anchorMin = new Vector2(0.10f, 0.64f);
            _dragHandleRect.anchorMax = new Vector2(0.68f, 0.82f);
            _dragHandleRect.offsetMin = Vector2.zero; _dragHandleRect.offsetMax = Vector2.zero;
        }

        // Manual book-drag (works on every tab, including Dressing where the EventSystem is swallowed). Grab the
        // top-edge band and slide the whole panel; screen-space move (overlay canvas => RectTransform.position is
        // in screen px, like GroupHudController), soft-clamped so the book can't be lost off-screen.
        private void HandleDrag()
        {
            var panelRect = _panelGo != null ? _panelGo.GetComponent<RectTransform>() : null;
            if (panelRect == null) return;
            Vector2 mouse = Input.mousePosition;

            if (_dragging)
            {
                if (!Input.GetMouseButton(0)) { _dragging = false; return; }
                Vector2 p = mouse + _dragOffset;
                p.x = Mathf.Clamp(p.x, UnityEngine.Screen.width * 0.15f, UnityEngine.Screen.width * 0.85f);
                p.y = Mathf.Clamp(p.y, UnityEngine.Screen.height * 0.15f, UnityEngine.Screen.height * 0.85f);
                panelRect.position = p;
                return;
            }

            if (Input.GetMouseButtonDown(0) && _dragHandleRect != null && !global::FiresRPGmaker.UI.Screens.PlayerPanels.BasePlayerModulePanel.AnyOpen   // a click on a module panel over the book (its X) never drags the book
                && RectTransformUtility.RectangleContainsScreenPoint(_dragHandleRect, mouse, null))
            {
                _dragging = true;
                _dragOffset = (Vector2)panelRect.position - mouse;
                global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] book drag start");
            }
        }

        private void WireTabs()
        {
            foreach (var tab in Tabs)
            {
                var btn = Btn(tab + "Tab");
                if (btn == null) continue;
                string captured = tab;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnTabClicked(captured));
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
            Hook("BufferApply", SaveBuffer);
            Hook("BufferEditButton", () => Screens.PlayerPanels.BufferAdminPanel.Show(BufferProfileForEdit()));
            Hook("BufferPreviewButton", () =>
            {
                string p = GetScreenInput("Buffers", "BufferProfile")?.Trim()?.ToLowerInvariant();
                if (string.IsNullOrEmpty(p)) p = "default";
                Screens.PlayerPanels.BufferPlayerPanel.Show(p, _npc != null ? _npc.GetDisplayName() : "Buffs");
            });
            Hook("TransmogApply", SaveTransmog);
            Hook("MarketplaceApply", SaveMarketplace);
            Hook("MailApply", SaveMail);
            // Dressing Apply/ClearAll are owned by the wired DressingRoomScreenController, not the book.
            Hook("AddRewardButton", () => AddRewardRow(null));
            Hook("AddOfferingButton", () => AddOfferingRow(null));
            Hook("AddCurrencyButton", () => { AddCurrencyRow("", 0); });
            Hook("AddMailButton", ClearMailEditFields);
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
            if (_shell != null) { try { _shell.SetActive(tab, "", ShellContext()); } catch { } ApplySkin(); }
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
            if (dlg && _dlgGroup != null) { LiftDialogueGroupUnderHeader(); _dlgLiftFrames = DialogueLiftFrames; }
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
            // Show() runs the Dressing re-layout, which places this button
            // under the preview - so it must exist BEFORE Show (on the first open it was created after, and the layout saw null)
            if (!_patrolBtnAdded)
            {
                _patrolBtnAdded = true;
                var pbtn = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(screen.transform, "Set Patrol Route",
                    new Vector2(0.66f, 0.935f), new Vector2(1.0f, 0.985f), OnSetPatrolClicked);
                StyleBookGoldButton(pbtn);
                _patrolBtnRect = pbtn.GetComponent<RectTransform>();
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
                    new Vector2(0.66f, 0.935f), new Vector2(1.0f, 0.985f), OnSetPatrolClicked);
                StyleBookGoldButton(btn);
                _patrolBtnRect = btn.GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// "Show Anchor" — RIGHT page, just left of the right page's tree emblem (Set Patrol sits to
        /// the tree's right). Clicking CLOSES the book (patrol-flow UX) and shows the golden anchor
        /// orb for the NPC being configured; the orb's hold-Alt right-click menu offers Move /
        /// permanent Remove. onClick=null: driven by the manual hit-test in Update so it works on
        /// every tab (incl. Dressing with InventoryGui open).
        /// </summary>
        private void EnsureAnchorButton()
        {
            if (_anchorBtnRect != null || _rightPage == null) return;
            var btn = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(_rightPage, "Show Anchor",
                new Vector2(0.02f, 0.935f), new Vector2(0.32f, 0.985f), OnShowAnchorClicked);
            StyleBookGoldButton(btn);
            _anchorBtnRect = btn.GetComponent<RectTransform>();
        }

        /// <summary>Restyles a runtime parchment button to the BAKED book-button palette (the warm
        /// gold of Apply Setup / Save Quest Changes) so runtime controls match the prefab's.</summary>
        private static void StyleBookGoldButton(UnityEngine.UI.Button btn)
        {
            if (btn == null) return;
            var normal = new Color(0.53f, 0.42f, 0.19f, 0.95f);
            var colors = btn.colors;
            colors.normalColor = normal;
            colors.highlightedColor = new Color(0.63f, 0.51f, 0.25f, 1f);
            colors.pressedColor = new Color(0.40f, 0.31f, 0.14f, 1f);
            colors.selectedColor = normal;
            btn.colors = colors;
            var img = btn.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = normal;
            var label = btn.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (label != null) label.color = new Color(0.95f, 0.90f, 0.75f, 1f);
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
            NpcType.Buffer => "Buffers",
            _ => "Dressing",
        };

        // ── left page (setup) ────────────────────────────────────────────────
        private void PopulateLeft()
        {
            if (_npc == null) return;
            SetInput(_leftPage, "Container_NameOverrideInput",
                string.IsNullOrEmpty(_npc.displayNameOverride) ? _npc.npcProfileName : _npc.displayNameOverride);
            // The left "Profile" field shows the ACTIVE module profile (the per-npcType field), not the
            // identity name — so it mirrors whatever was last set on a module tab. npcProfileName is the
            // NPC's name/identity and is edited via the "Npc Name" field above.
            SetInput(_leftPage, "Container_ProfileInput", _npc.GetActiveProfile());
            EnsureProfileDropdown("LeftPage", _leftPage, "Container_ProfileInput", _npc.npcType,
                name => { _npc.SetActiveProfile(name); });
            EnsureNpcTypeDropdown();
            try { EnsureModelDropdown(); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] model picker: " + ex.Message); }
            SetInput(_leftPage, "Container_ModelOverrideInput", _npc.modelOverride);
            void Ph(string node, string hint)
            {
                var i = InputIn(_leftPage, node);
                if (i == null) return;
                if (i.placeholder is TMP_Text p2) p2.text = hint;
                RegisterHover(i.gameObject, CleanFieldLabel(node), hint);
            }
            Ph("Container_NameOverrideInput", "display name, e.g. Ylva the Trader");
            Ph("Container_ProfileInput", "active profile for the selected Type");
            Ph("Container_ModelOverrideInput", "creature prefab, e.g. Greydwarf - empty = human");
            var idle = Tog(_leftPage, "AllowIdleWander_chk");
            if (idle != null) idle.isOn = FiresCore.Bridge.NpcCompanionBridge.GetIdleWander(_npc.gameObject) ?? _npc.allowIdleWandering;
            // the baked caption is the raw node name
            if (idle != null) foreach (var lt in idle.GetComponentsInChildren<TMP_Text>(true)) if (lt != null && IsRawKeyText(lt.text)) lt.text = IdleWanderText;
            RelabelRawTexts(_leftPage);
            var head = FindRec(_leftPage, "Header")?.GetComponent<TMP_Text>();
            if (head != null) head.text = "Configure NPC: " + (string.IsNullOrEmpty(_npc.displayNameOverride) ? _npc.npcProfileName : _npc.displayNameOverride);
        }

        // The explicit left-page NPC Type selector. Selecting a type updates the NPC's type (in memory) and
        // re-shows the Profile field/list for that type, so the left page and the matching module tab agree.
        private bool _npcTypeDdWired;
        // the Model Override field gets a picker - reused from the Profile field's ProfileDD (the arrow picks, the input shows the
        // name) - listing exactly FCC's FccApi.AllowedModels() (no second list). A pick fills the input and applies at once
        // (ApplySetup: ApplyModelOverride + SaveToZDO), so the NPC re-skins and the Dressing preview re-frames.
        private TMP_Dropdown _modelDd;
        private static System.Reflection.MethodInfo s_fccAllowed;
        private static bool s_fccLooked;
        private static List<string> FccModels()
        {
            try
            {
                if (!s_fccLooked) { s_fccLooked = true; s_fccAllowed = Type.GetType("FiresCharacterCreator.FccApi, FiresCharacterCreator", false)?.GetMethod("AllowedModels", Type.EmptyTypes); }
                if (s_fccAllowed?.Invoke(null, null) is IEnumerable<string> list) return list.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] FccApi.AllowedModels: " + (ex.InnerException ?? ex).Message); }
            return null;
        }
        // Core 0.2.659 FiresCore.Npc.ExternalModels.Available() (ModelOption{Prefab, Label, Source}; empty without the mod). By reflection, so an
        // older Core just lists FCC's models. The prefab name is the value (it goes straight into the Model Override field).
        internal static List<string> ExternalModelPrefabs()
        {
            var list = new List<string>();
            try
            {
                var m = Type.GetType("FiresCore.Npc.ExternalModels, FiresUnifiedCore", false)?.GetMethod("Available", Type.EmptyTypes);
                if (m?.Invoke(null, null) is System.Collections.IEnumerable all)
                    foreach (var o in all)
                    {
                        var p = o?.GetType().GetField("Prefab")?.GetValue(o) as string ?? o?.GetType().GetProperty("Prefab")?.GetValue(o) as string;
                        if (!string.IsNullOrEmpty(p) && !list.Contains(p)) list.Add(p);
                    }
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] ExternalModels.Available: " + (ex.InnerException ?? ex).Message); }
            return list;
        }
        private void EnsureModelDropdown()
        {
            if (_npc == null || _leftPage == null) return;
            var models = FccModels() ?? new List<string>();
            var external = ExternalModelPrefabs();   // Creatures of Origins etc. (Core ExternalModels), after FCC's
            if (models.Count == 0 && external.Count == 0) return;   // nothing to pick: the free-text field stays as before
            var input = InputIn(_leftPage, "Container_ModelOverrideInput");
            var profDd = FindRec(_leftPage, "ProfileDD")?.GetComponent<TMP_Dropdown>();
            var profC = FindRec(_leftPage, "Container_ProfileInput") as RectTransform;
            var modelC = FindRec(_leftPage, "Container_ModelOverrideInput") as RectTransform;
            if (input == null || profDd == null || modelC == null) return;
            if (_modelDd == null)
            {
                var src = (RectTransform)profDd.transform;
                var go = UnityEngine.Object.Instantiate(profDd.gameObject, src.parent == profC ? modelC : src.parent, false);
                go.name = "ModelDD";
                var rt = (RectTransform)go.transform;
                if (src.parent != profC && profC != null) rt.anchoredPosition = src.anchoredPosition + (modelC.anchoredPosition - profC.anchoredPosition);
                _modelDd = go.GetComponent<TMP_Dropdown>();
                _modelDd.onValueChanged.RemoveAllListeners();
                _modelDd.onValueChanged.AddListener(idx =>
                {
                    if (_modelDd == null || _npc == null || idx <= 0) return;
                    string picked = idx < _modelDd.options.Count ? _modelDd.options[idx].text : null;
                    _modelDd.SetValueWithoutNotify(0);
                    if (string.IsNullOrEmpty(picked)) return;
                    input.SetTextWithoutNotify(picked == HumanModel ? "" : picked);
                    global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] model picked from FCC's list: '{picked}' for {_npc.GetDisplayName()}");
                    ApplySetup();
                });
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] Model Override picker: {models.Count} model(s) from FccApi.AllowedModels()");
            }
            var opts = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(PickCaption), new TMP_Dropdown.OptionData(HumanModel) };
            foreach (var m in models.OrderBy(m => m)) opts.Add(new TMP_Dropdown.OptionData(m));
            foreach (var m in external) if (!models.Contains(m)) opts.Add(new TMP_Dropdown.OptionData(m));
            _modelDd.ClearOptions(); _modelDd.AddOptions(opts); _modelDd.SetValueWithoutNotify(0);
            MakeFieldPicker(_modelDd, input);   // the field is the dropdown (NPC Type look)
        }
        private const string HumanModel = "(human)";

        private void EnsureNpcTypeDropdown()
        {
            var dd = ScreenDd(_leftPage, "NpcType_C");
            if (dd == null) return;
            // The baked prefab predates the None role — inject it at the top so roleless (decorative)
            // NPCs are selectable, and so unknown values fall back to None (index 0) instead of Quest.
            if (!dd.options.Exists(o => string.Equals(o.text, "None", StringComparison.OrdinalIgnoreCase)))
                dd.options.Insert(0, new TMP_Dropdown.OptionData("None"));
            // Feedback (kg type 4) also post-dates the bake — appended so migrated/authored feedback NPCs
            // are selectable without a prefab rebake.
            if (!dd.options.Exists(o => string.Equals(o.text, "Feedback", StringComparison.OrdinalIgnoreCase)))
                dd.options.Add(new TMP_Dropdown.OptionData("Feedback"));
            SetScreenDropdown(_leftPage, "NpcType_C", _npc.npcType.ToString());
            AttachTip(_leftPage, "NpcType_C", "NPC Type",
                "The role this NPC plays. None = decorative (no [E] interaction). Pick a type, then set its profile on the matching tab.");
            if (_npcTypeDdWired) return;
            _npcTypeDdWired = true;
            dd.onValueChanged.AddListener(idx =>
            {
                if (dd == null || _npc == null) return;
                string picked = idx >= 0 && idx < dd.options.Count ? dd.options[idx].text : null;
                if (!Enum.TryParse(picked, out NpcType t)) return;
                // The pick only fills the form; ApplySetup commits the type and the profile.
                SetInput(_leftPage, "Container_ProfileInput", _npc.GetProfileForType(t));
                EnsureProfileDropdown("LeftPage", _leftPage, "Container_ProfileInput", t, null);
            });
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
            // Explicit NPC Type selector wins (the module tabs also set the type when you Save on them).
            string typeStr = GetScreenDropdown(_leftPage, "NpcType_C");
            if (Enum.TryParse(typeStr, out NpcType chosen)) _npc.npcType = chosen;
            // The left Profile field edits the ACTIVE module profile (single source of truth), not the
            // identity name — so a save here and a save on the matching module tab write the same field.
            string prof = GetInput(_leftPage, "Container_ProfileInput")?.Trim();
            if (!string.IsNullOrEmpty(prof)) _npc.SetActiveProfile(prof);

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
                case "Buffers": PopulateBuffer(); break;
                case "Marketplace": PopulateMarketplace(); break;
                case "Mail": PopulateMail(); break;
                case "Dressing": PopulateDressing(); break;
            }
            EnsureActiveScreenProfileDropdown();
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
            SetScreenInput("Trader", "Max Gold_C", p != null && p.MaxGold != -1 ? p.MaxGold.ToString() : "");
            SetScreenInput("Trader", "Gold Restock_C", p != null && p.GoldRestockSeconds != 0 ? p.GoldRestockSeconds.ToString() : "");
            SetInputPlaceholder("Trader", "Trades", "Wood, 10, Coins, 5    one trade per line: cost, amount, reward, amount[, level]");
            SetInputPlaceholder("Trader", "Stock_C", "empty = unlimited");
            SetInputPlaceholder("Trader", "Restock (h)_C", "hours, empty = never");
            SetInputPlaceholder("Trader", "Max Gold_C", "empty = unlimited");
            SetInputPlaceholder("Trader", "Gold Restock_C", "seconds, empty = never");
            EnsureTraderExtras();
            UpdateTraderStatus(null);
        }

        private void SaveTrader()
        {
            string name = GetScreenInput("Trader", "Trader Profile_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a trader profile name"); return; }
            string trades = (GetScreenInput("Trader", "Trades") ?? "").Trim();
            // Numbers are checked (blank = default; -1 = unlimited for stock and max gold).
            if (!TryNumField("Trader", "Stock_C", "Stock", -1, -1, out int stock)
                || !TryNumField("Trader", "Restock (h)_C", "Restock (h)", 0, 0, out int restockH)
                || !TryNumField("Trader", "Max Gold_C", "Max gold", -1, -1, out int maxGold)
                || !TryNumField("Trader", "Gold Restock_C", "Gold restock", 0, 0, out int goldRestock)) return;
            int restockS = restockH * 3600;
            // Direct header write so the form's Stock/Restock/MaxGold take effect (SaveProfile re-uses the
            // existing profile header instead). Same folder + reload the manager uses; keys match the parser.
            string header = name;
            if (stock != -1) header += $",Stock={stock}";
            if (restockS != 0) header += $",Restock={restockS}";
            if (maxGold != -1) header += $",MaxGold={maxGold}";
            if (goldRestock != 0) header += $",GoldRestock={goldRestock}";
            var folder = Path.Combine(FiresCore.Storage.FiresConfigPaths.Traders);
            string traderContent = $"[{header}]\n{trades}\n";
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, SafeFileName(name) + ".cfg"), traderContent);
                TraderProfileManager.Reset(); TraderProfileManager.Initialize();
                global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Traders", SafeFileName(name) + ".cfg", traderContent);
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveTrader: " + ex.Message); Msg("Save failed (see log)"); return; }
            _npc.traderProfile = name; _npc.npcType = NpcType.Trader; _npc.SaveToZDO();

            var unknown = UnknownItemsInTradeLines(trades);
            UpdateTraderStatus(unknown);
            Msg(unknown.Count == 0
                ? "Saved trader profile '" + name + "'"
                : "Saved '" + name + "' - unknown items: " + string.Join(", ", unknown));
        }

        private void EnsureTraderExtras()
        {
            // Trader's Save sits at y 0.10-0.175 with open space below, so Save shrinks left, Reload
            // fills the right of that same row, and the status line goes in the band beneath them.
            SetNodeRect(Screen("Trader"), "TraderApply", 0.05f, 0.10f, 0.62f, 0.175f);
            EnsureStatusLabel("Trader", "TraderStatus", 0.025f, 0.09f);
            EnsureReloadButton("Trader", "TraderReload", "TraderApply", 0.10f, 0.175f, () =>
            {
                TraderProfileManager.Reset();
                TraderProfileManager.Initialize();
                PopulateTrader();
                Msg("Trader profiles reloaded from disk");
            });
        }

        private void UpdateTraderStatus(List<string> unknownItems)
        {
            int trades = 0;
            foreach (var line in (GetScreenInput("Trader", "Trades") ?? "").Split('\n'))
                if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")) trades++;
            string text = trades > 0 ? $"{trades} trade{(trades == 1 ? "" : "s")}" : "No trades yet - one per line";
            if (unknownItems != null && unknownItems.Count > 0)
                text = "Unknown items: " + string.Join(", ", unknownItems) + " (saved anyway)";
            SetStatusLabel("Trader", "TraderStatus", text);
        }

        // Walks every trade line's tokens (both the simple cost,amt,reward,amt[,lvl] form and the
        // multi-item costs : rewards form) — non-numeric tokens are item names, validated as prefabs.
        private List<string> UnknownItemsInTradeLines(string trades)
        {
            var unknown = new List<string>();
            foreach (var rawLine in (trades ?? "").Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                foreach (var rawToken in line.Replace(":", ",").Split(','))
                {
                    var token = rawToken.Trim();
                    if (token.Length == 0 || int.TryParse(token, out _)) continue;
                    if (!ItemPrefabExists(token) && !unknown.Contains(token)) unknown.Add(token);
                }
            }
            return unknown;
        }

        // ════════════════════════════ BANKER ════════════════════════════════
        private void PopulateBanker()
        {
            string name = _npc.bankerProfile;
            SetScreenInput("Banker", "Banker Profile_C", name);
            var p = string.IsNullOrEmpty(name) ? null : BankerProfileManager.GetProfile(name);
            SetScreenInput("Banker", "Interest %/min_C", p != null ? p.InterestPerMinute.ToString() : "");
            // Multi-currency list (restores the legacy multi-currency editing the single Accepted/Max pair dropped).
            ClearInputRows("Banker", "CurrencyPool");
            if (p != null && p.CurrencyLimits.Count > 0)
                foreach (var kv in p.CurrencyLimits) AddCurrencyRow(kv.Key, kv.Value);
            else AddCurrencyRow("Coins", 1000000);
            SetInputPlaceholder("Banker", "Interest %/min_C", "e.g. 0.5   empty = no interest");
            RaisePoolBottom("Banker", "CurrencyPool", 0.155f);
            SetNodeRect(Screen("Banker"), "BankerApply", 0.05f, 0.005f, 0.62f, 0.075f);
            SetNodeRect(Screen("Banker"), "BankerStatus", 0.06f, 0.085f, 0.94f, 0.14f);
            EnsureReloadButton("Banker", "BankerReload", "AddCurrencyButton", 0.005f, 0.075f, () =>
            {
                BankerProfileManager.ReloadAll();
                PopulateBanker();
                Msg("Banker profiles reloaded from disk");
            });
            SetBankerStatus("");
        }

        private void AddCurrencyRow(string currency, int max)
        {
            var row = AddRow("Banker", "CurrencyPool", "CurrencyRowTemplate");
            if (row == null) { Msg("Banker currency pool missing (bad prefab)"); return; }
            SetRowCell(row.transform, "CurrencyCell", currency ?? "");
            if (max > 0) SetRowCell(row.transform, "MaxCell", max.ToString());
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); });
        }

        private void SaveBanker()
        {
            string name = GetScreenInput("Banker", "Banker Profile_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { SetBankerStatus("Enter a banker profile name"); Msg("Enter a banker profile name"); return; }
            // Interest and each currency cap are checked before anything is written.
            string interest = GetScreenInput("Banker", "Interest %/min_C")?.Trim();
            if (!TryFloatField("Banker", "Interest %/min_C", "Interest", 0f, 0f, out float interestN)) { SetBankerStatus("Interest must be a number, 0 or more"); return; }
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(interest)) sb.AppendLine("interest=" + interestN.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var row in LiveInputRows("Banker", "CurrencyPool"))
            {
                string cur = RowCell(row, "CurrencyCell")?.Trim();
                if (string.IsNullOrEmpty(cur)) continue;
                if (!TryNumText(RowCell(row, "MaxCell"), cur + " max", 1000000, 1, out int max)) { SetBankerStatus(cur + ": the max must be a whole number, 1 or more"); return; }
                sb.AppendLine(cur + ":" + max);
            }
            BankerProfileManager.SaveProfile(name, sb.ToString().TrimEnd());
            _npc.bankerProfile = name; _npc.npcType = NpcType.Banker; _npc.SaveToZDO();

            var unknown = new List<string>();
            foreach (var row in LiveInputRows("Banker", "CurrencyPool"))
            {
                string cur = RowCell(row, "CurrencyCell")?.Trim();
                if (!string.IsNullOrEmpty(cur) && !ItemPrefabExists(cur) && !unknown.Contains(cur)) unknown.Add(cur);
            }
            SetBankerStatus(unknown.Count == 0
                ? "Saved '" + name + "'"
                : "Unknown currencies: " + string.Join(", ", unknown) + " (saved anyway)");
            Msg(unknown.Count == 0
                ? "Saved banker profile '" + name + "'"
                : "Saved '" + name + "' - unknown currencies: " + string.Join(", ", unknown));
        }

        private void SetBankerStatus(string s) { var t = FindRec(Screen("Banker"), "BankerStatus")?.GetComponent<TMP_Text>(); if (t != null) t.text = s ?? ""; }

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
            SetScreenInput("Gambler", "Amount_C",
                p == null ? "100"
                : p.RequiredItem.Min == p.RequiredItem.Max ? p.RequiredItem.Min.ToString()
                : p.RequiredItem.Min + "-" + p.RequiredItem.Max);
            ClearInputRows("Gambler", "RewardPool");
            if (p != null) foreach (var r in p.Rewards) AddRewardRow(r);
            EnsureGamblerExtras();
            UpdateGamblerStatus(null);
        }

        private void SaveGambler()
        {
            string name = GetScreenInput("Gambler", "Profile Name_C")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a gambler profile name"); return; }
            if (!TryNumField("Gambler", "Max Rolls_C", "Max rolls", 1, 1, out int maxRolls)) return;
            var games = new List<string>();
            if (GetToggle("Gambler", "Cards_chk")) games.Add(GamblerGames.Cards);
            if (GetToggle("Gambler", "Bones_chk")) games.Add(GamblerGames.Bones);
            if (GetToggle("Gambler", "Runes_chk")) games.Add(GamblerGames.Runes);
            if (GetToggle("Gambler", "Wheel_chk")) games.Add(GamblerGames.Wheel);

            // "Amount" accepts a fixed cost ("100") or a range ("100-250") — the cfg's Min[-Max] grammar.
            string amountRaw = GetScreenInput("Gambler", "Amount_C")?.Trim() ?? "";
            var amountParts = amountRaw.Split('-');
            if (!TryNumText(amountParts[0], "Cost amount", 1, 0, out int costMin)) return;
            int costMax = costMin;
            if (amountParts.Length > 1 && !TryNumText(amountParts[1], "Cost amount (max)", costMin, 0, out costMax)) return;
            if (costMax < costMin) costMax = costMin;
            var profile = new GamblerProfile
            {
                Name = name,
                MaxRolls = maxRolls,
                RequiredItem = new GamblerItemEntry { Prefab = GetScreenInput("Gambler", "Cost Item_C")?.Trim() ?? "Coins", Min = costMin, Max = costMax },
                Rewards = new List<GamblerItemEntry>(),
            };
            foreach (var row in LiveInputRows("Gambler", "RewardPool"))
            {
                string item = RowCell(row, "ItemCell")?.Trim();
                if (string.IsNullOrEmpty(item)) continue;
                if (!TryNumText(RowCell(row, "MinCell"), item + " min", 1, 0, out int mn)
                    || !TryNumText(RowCell(row, "MaxCell"), item + " max", mn, 0, out int mx)) return;
                profile.Rewards.Add(new GamblerItemEntry { Prefab = item, Min = mn, Max = mx < mn ? mn : mx });
            }
            string body = GamblerProfileManager.Serialize(profile);
            GamblerProfileManager.SaveProfile(name, maxRolls, body, games);
            _npc.gamblerProfile = name; _npc.npcType = NpcType.Gambler; _npc.SaveToZDO();

            var unknown = new List<string>();
            if (!ItemPrefabExists(profile.RequiredItem.Prefab)) unknown.Add(profile.RequiredItem.Prefab);
            foreach (var r in profile.Rewards)
                if (!ItemPrefabExists(r.Prefab) && !unknown.Contains(r.Prefab)) unknown.Add(r.Prefab);
            UpdateGamblerStatus(unknown);
            Msg(unknown.Count == 0
                ? "Saved gambler profile '" + name + "'"
                : "Saved '" + name + "' - unknown items: " + string.Join(", ", unknown));
        }

        // ════════════════════════════ TRANSMOG ══════════════════════════════
        private void PopulateTransmog()
        {
            // Transmog setup page opens over the book (no Edit button on the prefab)
            Screens.PlayerPanels.TransmogAdminPanel.Show(string.IsNullOrEmpty(_npc.transmogProfile) ? "default" : _npc.transmogProfile);
            string name = _npc.transmogProfile;
            SetScreenInput("Transmog", "TmProfile", name);
            ClearInputRows("Transmog", "OfferingPool");
            var opts = string.IsNullOrEmpty(name) ? null : TransmogProfileManager.GetProfileOptions(name);
            if (opts != null) foreach (var o in opts) AddOfferingRow(o);
            EnsureTransmogExtras();
            UpdateTransmogStatus(null);
        }

        private void SaveTransmog()
        {
            string name = GetScreenInput("Transmog", "TmProfile")?.Trim();
            if (string.IsNullOrEmpty(name)) { Msg("Enter a transmog profile name"); return; }
            var sb = new StringBuilder();
            var unknown = new List<string>();
            foreach (var row in LiveInputRows("Transmog", "OfferingPool"))
            {
                string app = RowCell(row, "AppearanceCell")?.Trim();
                if (string.IsNullOrEmpty(app)) continue;
                string slot = RowCell(row, "SlotCell")?.Trim(); if (string.IsNullOrEmpty(slot)) slot = "Helmet";
                string cur = RowCell(row, "CurrencyCell")?.Trim(); if (string.IsNullOrEmpty(cur)) cur = "Coins";
                // A negative or non-number price stopped nothing and became 0 (free).
                if (!TryNumText(RowCell(row, "PriceCell"), app + " price", 0, 0, out int price)) { UpdateTransmogStatus(null); return; }
                if (!ItemPrefabExists(app) && !unknown.Contains(app)) unknown.Add(app);
                if (!ItemPrefabExists(cur) && !unknown.Contains(cur)) unknown.Add(cur);
                sb.AppendLine($"{slot}|{app}|{cur}|{price}");
            }
            TransmogProfileManager.SaveProfile(name, sb.ToString().TrimEnd());
            _npc.transmogProfile = name; _npc.npcType = NpcType.Transmog; _npc.SaveToZDO();
            UpdateTransmogStatus(unknown);
            Msg(unknown.Count == 0
                ? "Saved transmog profile '" + name + "'"
                : "Saved '" + name + "' - unknown items: " + string.Join(", ", unknown));
        }

        // ── Gambler/Transmog editor extras (approved 2026-07-07 mockups): odds/status line,
        //    prefab validation, reload-from-disk, slot dropdown ────────────────────────────
        private static readonly string[] TransmogSlots =
            { "Helmet", "Chest", "Legs", "Cape", "Shoulder", "Shield", "Bow", "Tool", "OneHandedWeapon", "TwoHandedWeapon" };

        // These tabs have Save at the very bottom with a scroll above it, so the scroll bottom is
        // raised to open a clean band for the status line between the scroll and the buttons.
        private void EnsureGamblerExtras()
        {
            AttachTip(Screen("Gambler"), "Cards_chk", "Cards", "Offer the card-flip game on this gambler");
            AttachTip(Screen("Gambler"), "Bones_chk", "Bones", "Offer the dice/bones game");
            AttachTip(Screen("Gambler"), "Runes_chk", "Runes", "Offer the rune-draw game");
            AttachTip(Screen("Gambler"), "Wheel_chk", "Wheel", "Offer the spin-the-wheel game");
            RaisePoolBottom("Gambler", "RewardPool", 0.155f);
            SetNodeRect(Screen("Gambler"), "GamblerApply", 0.05f, 0.005f, 0.62f, 0.075f);
            EnsureStatusLabel("Gambler", "GamblerStatus", 0.085f, 0.14f);
            EnsureReloadButton("Gambler", "GamblerReload", "AddRewardButton", 0.005f, 0.075f, () =>
            {
                GamblerProfileManager.ReloadAll();
                PopulateGambler();
                Msg("Gambler profiles reloaded from disk");
            });
        }

        private void EnsureTransmogExtras()
        {
            RaisePoolBottom("Transmog", "OfferingPool", 0.155f);
            SetNodeRect(Screen("Transmog"), "TransmogApply", 0.05f, 0.005f, 0.62f, 0.075f);
            EnsureStatusLabel("Transmog", "TransmogStatus", 0.085f, 0.14f);
            EnsureReloadButton("Transmog", "TransmogReload", "AddOfferingButton", 0.005f, 0.075f, () =>
            {
                TransmogProfileManager.ReloadAll();
                PopulateTransmog();
                Msg("Transmog profiles reloaded from disk");
            });
        }

        // Registers hover help for a named control (dropdown/checkbox/button) inside a scope.
        private void AttachTip(Transform scope, string node, string topic, string body)
        {
            var t = FindRec(scope, node);
            if (t != null) RegisterHover(t.gameObject, topic, body);
        }

        private void SetNodeRect(Transform scope, string node, float xMin, float yMin, float xMax, float yMax)
        {
            var rt = FindRec(scope, node)?.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        // Raise a baked scroll pool's bottom edge so a status line fits below it. Anchor-only move
        // (the baked pools use zero offsets), idempotent.
        private void RaisePoolBottom(string tab, string poolName, float newBottom)
        {
            var rt = FindRec(Screen(tab), poolName)?.GetComponent<RectTransform>();
            if (rt == null) return;
            var min = rt.anchorMin;
            if (min.y >= newBottom) return;
            min.y = newBottom;
            rt.anchorMin = min;
            rt.offsetMin = new Vector2(rt.offsetMin.x, 0f);
        }

        // Status line cloned from the Banker screen's baked BankerStatus label (styling only) and
        // positioned into a clear band. Idempotent — repositions the existing clone on reopen.
        private void EnsureStatusLabel(string tab, string labelName, float yMin, float yMax)
        {
            var scr = Screen(tab);
            if (scr == null) return;
            var existing = FindRec(scr, labelName);
            RectTransform rt;
            if (existing != null) rt = existing.GetComponent<RectTransform>();
            else
            {
                var donor = FindRec(Screen("Banker"), "BankerStatus");
                if (donor == null) return;
                var go = Instantiate(donor.gameObject, scr);
                go.name = labelName;
                var t = go.GetComponent<TMP_Text>();
                if (t != null) { t.text = ""; t.alignment = TextAlignmentOptions.MidlineLeft; }
                rt = go.GetComponent<RectTransform>();
            }
            rt.anchorMin = new Vector2(0.06f, yMin);
            rt.anchorMax = new Vector2(0.94f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void SetStatusLabel(string tab, string labelName, string text)
        {
            var t = FindRec(Screen(tab), labelName)?.GetComponent<TMP_Text>();
            if (t != null) t.text = text ?? "";
        }

        // Reload button cloned from the tab's own baked "+ Add"/Save button, positioned bottom-right
        // beside the (repositioned) Save button. Idempotent.
        private void EnsureReloadButton(string tab, string buttonName, string donorName, float yMin, float yMax, UnityEngine.Events.UnityAction onClick)
        {
            var scr = Screen(tab);
            if (scr == null) return;
            var existing = FindRec(scr, buttonName);
            RectTransform rt;
            if (existing != null) rt = existing.GetComponent<RectTransform>();
            else
            {
                var donor = FindRec(scr, donorName);
                if (donor == null) return;
                var go = Instantiate(donor.gameObject, donor.parent);
                go.name = buttonName;
                var label = go.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Reload";
                var b = go.GetComponent<Button>();
                if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(onClick); }
                rt = go.GetComponent<RectTransform>();
            }
            rt.anchorMin = new Vector2(0.66f, yMin);
            rt.anchorMax = new Vector2(0.95f, yMax);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void UpdateGamblerStatus(List<string> unknownItems)
        {
            int rewards = 0;
            foreach (var row in LiveInputRows("Gambler", "RewardPool"))
                if (!string.IsNullOrEmpty(RowCell(row, "ItemCell")?.Trim())) rewards++;
            int rolls = ParseInt(GetScreenInput("Gambler", "Max Rolls_C"), 1);
            string text = rewards > 0
                ? $"{rewards} reward{(rewards == 1 ? "" : "s")} - each ~{Mathf.RoundToInt(100f / rewards)}% per roll, {rolls} roll{(rolls == 1 ? "" : "s")}/spin"
                : "No rewards yet - add at least one";
            if (unknownItems != null && unknownItems.Count > 0)
                text = "Unknown items: " + string.Join(", ", unknownItems) + " (saved anyway)";
            SetStatusLabel("Gambler", "GamblerStatus", text);
        }

        private void UpdateTransmogStatus(List<string> unknownItems)
        {
            int offers = 0;
            foreach (var row in LiveInputRows("Transmog", "OfferingPool"))
                if (!string.IsNullOrEmpty(RowCell(row, "AppearanceCell")?.Trim())) offers++;
            string text = offers > 0 ? $"{offers} offering{(offers == 1 ? "" : "s")}" : "No offerings yet - add at least one";
            if (unknownItems != null && unknownItems.Count > 0)
                text = "Unknown items: " + string.Join(", ", unknownItems) + " (saved anyway)";
            SetStatusLabel("Transmog", "TransmogStatus", text);
        }

        private static bool ItemPrefabExists(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return false;
            if (ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab(prefab) != null) return true;
            return ZNetScene.instance != null && ZNetScene.instance.GetPrefab(prefab) != null;
        }

        // Replaces an offering row's free-text SlotCell with a dropdown of the known slots. The hidden
        // input stays as the value store (FindRec/RowCell read inactive nodes), so SaveTransmog and
        // foreign slot strings keep working; unknown current values are preserved as an extra option.
        // The dropdown itself is a clone of the dialogue popup's baked NameDropdown — book styling
        // without a runtime builder or a prefab rebake.
        private void AttachSlotDropdown(Transform row)
        {
            if (row == null || FindRec(row, "SlotDD") != null) return;
            var cell = FindRec(row, "SlotCell");
            var input = cell != null ? cell.GetComponentInChildren<TMP_InputField>(true) : null;
            if (cell == null || input == null) return;
            var donor = FindRec(_root, "NameDropdown");
            if (donor == null || donor.GetComponent<TMP_Dropdown>() == null) return;

            string current = string.IsNullOrEmpty(input.text) ? "Helmet" : input.text.Trim();
            var go = Instantiate(donor.gameObject, cell.parent);
            go.name = "SlotDD";
            go.SetActive(true);
            var rt = go.GetComponent<RectTransform>();
            var crt = cell.GetComponent<RectTransform>();
            rt.anchorMin = crt.anchorMin; rt.anchorMax = crt.anchorMax;
            rt.offsetMin = crt.offsetMin; rt.offsetMax = crt.offsetMax;
            rt.pivot = crt.pivot;
            rt.SetSiblingIndex(cell.GetSiblingIndex() + 1);

            var dd = go.GetComponent<TMP_Dropdown>();
            var options = new List<string>(TransmogSlots);
            if (!options.Exists(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase)))
                options.Insert(0, current);
            dd.ClearOptions();
            dd.AddOptions(options);
            int idx = options.FindIndex(o => string.Equals(o, current, StringComparison.OrdinalIgnoreCase));
            dd.SetValueWithoutNotify(Mathf.Max(0, idx));
            if (dd.captionText != null) dd.captionText.text = options[Mathf.Max(0, idx)];
            dd.onValueChanged.RemoveAllListeners();
            dd.onValueChanged.AddListener(i =>
            {
                if (i >= 0 && i < dd.options.Count) input.SetTextWithoutNotify(dd.options[i].text);
            });
            input.SetTextWithoutNotify(current);
            cell.gameObject.SetActive(false);
        }

        // ════════════════════════════ QUEST ═════════════════════════════════
        // The quest DB holds many quests under one profile; v1 authors/overwrites a single quest by its
        // [questId] (editing an existing one in place by typing its id). A profile→quest picker is a later pass.
        private void PopulateQuest()
        {
            if (_npc == null) return;
            string profileName = _npc.questProfile;
            SetScreenInput("Quest", "Save To Profile_C", profileName);

            // Load the NPC's current quest into the authoring form. Without this the form keeps whatever text
            // was last shown (baked blanks on a fresh open), so a reopen looked like the save was lost, and a
            // partial edit (change one field, Save) wrote empty QuestName/Description/Target over the rest —
            // wiping the quest. We read the raw [questId] block so the round-trip matches SaveQuest byte-for-byte.
            string questId = ResolveNpcQuestId(profileName);
            if (!LoadQuestBlockIntoForm(questId))
            {
                // Fresh NPC with no quest yet — seed the id field from the profile so the first Save has a key,
                // and leave the rest blank for authoring.
                // 0.0.813 (Fire 10-31, fresh profile: the quest page "already has a quest for wolves filled out ... I assumed that
                // was an example"; saving it made a real wolf_hunt quest with "spawn pack" / "give title" commands): the UI bundle's
                // input fields carry baked example TEXT, and this branch never cleared it, so Save wrote it. Every field is
                // emptied now; the examples show only as the greyed hints (ApplyQuestHints), which are never saved.
                ClearQuestForm();
                if (!string.IsNullOrEmpty(profileName)) SetScreenInput("Quest", "Profile ID_C", questId ?? profileName);
            }
            ApplyQuestHints();
            EnsureQuestFieldArrows();
        }

        /// <summary>Fills the quest form from the raw [questId] cfg block (byte-for-byte round-trip with SaveQuest),
        /// falling back to the synced in-memory QuestDefinition when the raw block is missing on this machine or is a
        /// kg POSITIONAL block (no '='/':' lines → the raw map parses empty and used to blank the whole form).
        /// Returns false when the quest can't be resolved at all.</summary>
        private bool LoadQuestBlockIntoForm(string questId)
        {
            if (string.IsNullOrEmpty(questId)) return false;
            var block = ReadQuestBlockRaw(questId);
            bool rawUsable = block != null && (block.ContainsKey("QuestType") || block.ContainsKey("QuestName") || block.ContainsKey("Description"));
            if (!rawUsable)
            {
                var def = global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.GetQuestDefinition(questId);
                if (def == null) return false;
                FillQuestFormFromDef(questId, def);
                return true;
            }

            string V(string key) => block.TryGetValue(key, out var v) ? v : "";

            SetScreenInput("Quest", "Profile ID_C", questId);
            SetScreenInput("Quest", "Quest Name_C", block.TryGetValue("QuestName", out var qn) ? qn : V("Name"));

            string type = V("QuestType");
            if (type.Equals("Move", StringComparison.OrdinalIgnoreCase)) type = "Go To";   // internal name -> display
            if (!string.IsNullOrEmpty(type)) SetScreenDropdown(Screen("Quest"), "Type_C", type);

            SetToggle("Quest", "Auto-Complete_chk", V("Autocomplete").Equals("true", StringComparison.OrdinalIgnoreCase));
            SetScreenInput("Quest", "Desc", V("Description"));

            // QuestTarget = prefab,amount[,minLvl]
            string tPrefab = "", tAmount = "", tMin = "";
            string tline = V("QuestTarget");
            if (!string.IsNullOrEmpty(tline))
            {
                var parts = tline.Split(',');
                if (parts.Length > 0) tPrefab = parts[0].Trim();
                if (parts.Length > 1) tAmount = parts[1].Trim();
                if (parts.Length > 2) tMin = parts[2].Trim();
            }
            SetScreenInput("Quest", "Target_C", tPrefab);
            SetScreenInput("Quest", "Amount_C", tAmount);
            SetScreenInput("Quest", "Min Lvl_C", tMin);

            SetScreenInput("Quest", "Cooldown Days_C", V("CooldownDays"));
            SetScreenInput("Quest", "Limit Mins_C", V("TimeLimitMinutes"));
            SetScreenInput("Quest", "Rewards_C", V("QuestReward"));
            SetScreenInput("Quest", "Required_C", V("Requirement"));
            SetScreenInput("Quest", "On Start Event_C", V("OnAcceptQuest"));
            SetScreenInput("Quest", "On Complete_C", V("OnCompleteQuest"));
            SetScreenInput("Quest", "Preview PNG_C", V("PreviewImage"));
            return true;
        }

        // Synthesize form values from the synced QuestDefinition (kg positional quests, or quests whose cfg
        // lives only server-side). Saving converts the quest to key-value format — the server merge upserts it.
        private void FillQuestFormFromDef(string questId, global::FiresRPGmaker.Modules.Quest.QuestDefinition def)
        {
            SetScreenInput("Quest", "Profile ID_C", questId);
            SetScreenInput("Quest", "Quest Name_C", def.Name ?? "");
            string type = def.QuestType ?? "Kill";
            if (type.Equals("Move", StringComparison.OrdinalIgnoreCase)) type = "Go To";
            SetScreenDropdown(Screen("Quest"), "Type_C", type);
            SetToggle("Quest", "Auto-Complete_chk", def.Autocomplete);
            SetScreenInput("Quest", "Desc", def.Description ?? "");
            var t0 = (def.QuestTargets != null && def.QuestTargets.Count > 0) ? def.QuestTargets[0] : null;
            SetScreenInput("Quest", "Target_C", t0?.Prefab ?? "");
            SetScreenInput("Quest", "Amount_C", t0 != null ? t0.Amount.ToString() : "");
            SetScreenInput("Quest", "Min Lvl_C", (t0?.MinLevel ?? 0) > 0 ? t0.MinLevel.ToString() : "");
            SetScreenInput("Quest", "Cooldown Days_C", def.CooldownDays.ToString());
            SetScreenInput("Quest", "Limit Mins_C", def.TimeLimitMinutes.ToString());
            SetScreenInput("Quest", "Rewards_C", def.Rewards != null
                ? string.Join(" | ", def.Rewards.Select(r => $"{(string.IsNullOrEmpty(r.Type) ? "item" : r.Type)}: {r.Target}, {r.Amount}" + (r.Level > 1 ? $", {r.Level}" : "")))
                : "");
            SetScreenInput("Quest", "Required_C", def.Requirements != null ? string.Join(" | ", def.Requirements) : "");
            SetScreenInput("Quest", "On Start Event_C", LifecycleJoinBook(def, "OnAcceptQuest"));
            SetScreenInput("Quest", "On Complete_C", LifecycleJoinBook(def, "OnCompleteQuest"));
            SetScreenInput("Quest", "Preview PNG_C", def.PreviewImage ?? "");
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] '{questId}' loaded from synced quest DB (no raw cfg block on this machine)");
        }

        private static string LifecycleJoinBook(global::FiresRPGmaker.Modules.Quest.QuestDefinition def, string key)
        {
            if (def?.LifecycleEvents == null) return "";
            return def.LifecycleEvents.TryGetValue(key, out var cmds) && cmds != null ? string.Join(" | ", cmds) : "";
        }

        // ── Quest screen extras ───────────────────────────────────────────────────────────────────────────
        // Quest ID arrow = THIS NPC's offered quests (right-click an entry to REMOVE it from the profile).
        // "Add Quest" dropdown (Type's old slot) = every saved quest — pick to load; Save then asks whether
        // to save the quest alone (library) or also add it to the profile so this NPC offers it.
        private TMP_Dropdown _questIdArrowDd;
        private TMP_Dropdown _allQuestsDd;
        private readonly List<string> _questArrowIds = new List<string>();
        private readonly List<string> _allQuestIds = new List<string>();
        private readonly Dictionary<string, string> _questArrowLabelToId = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _questLayoutMoved;
        private Vector3 _oldTypeWorldPos;
        private Transform _oldTypeSlotParent;

        private void EnsureQuestFieldArrows()
        {
            var scr = Screen("Quest");
            if (scr == null) return;

            // The baked header says "Quest Profile ID:" / "Profile ID" but the field IS the quest id
            // (SaveQuest reads it as questId) — relabel so admins stop typing profile names into it.
            foreach (var t in scr.GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.text != null && t.text.Trim().TrimEnd(':') == "Profile ID") { t.text = "Quest ID"; break; }

            MoveTypeUnderName(scr);
            EnsureAllQuestsDropdown(scr);

            if (_questIdArrowDd != null) { RefreshQuestIdArrow(); return; }

            var idNode = FindRec(scr, "Profile ID_C");
            var saveNode = FindRec(scr, "Save To Profile_C");
            var srcDd = saveNode != null ? FindRec(saveNode, "ProfileDD") : null;
            if (srcDd == null) srcDd = FindRec(scr, "ProfileDD");   // fallback: any baked arrow on this screen
            if (idNode == null || srcDd == null)
            { Debug.LogWarning("[NpcBookController] quest-id arrow: baked refs missing (Profile ID_C / ProfileDD)"); return; }

            // Clone the baked arrow into the Quest ID field at the same in-field spot it occupies on the
            // profile field — identical look, identical placement, no coordinate guessing.
            var clone = UnityEngine.Object.Instantiate(srcDd.gameObject, idNode);
            clone.name = "QuestIdDD";
            var srt = (RectTransform)srcDd.transform;
            var rt = (RectTransform)clone.transform;
            rt.anchorMin = srt.anchorMin; rt.anchorMax = srt.anchorMax; rt.pivot = srt.pivot;
            rt.anchoredPosition = srt.anchoredPosition; rt.sizeDelta = srt.sizeDelta;
            _questIdArrowDd = clone.GetComponent<TMP_Dropdown>();
            if (_questIdArrowDd == null) { UnityEngine.Object.Destroy(clone); return; }
            _questIdArrowDd.onValueChanged.RemoveAllListeners();

            // Right-click on a list entry removes that quest from the NPC's profile: the handler component
            // rides the item TEMPLATE, so every cloned row gets it when the dropdown opens.
            var itemToggle = _questIdArrowDd.template != null ? _questIdArrowDd.template.GetComponentInChildren<Toggle>(true) : null;
            if (itemToggle != null && itemToggle.GetComponent<QuestArrowItemRightClick>() == null)
                itemToggle.gameObject.AddComponent<QuestArrowItemRightClick>();
            QuestArrowItemRightClick.OnRightPick = OnQuestArrowRightClick;

            RefreshQuestIdArrow();
            MakeFieldPicker(_questIdArrowDd, idNode.GetComponentInChildren<TMP_InputField>(true));   // the field is the dropdown
            _questIdArrowDd.onValueChanged.AddListener(idx =>
            {
                if (_questIdArrowDd == null || idx <= 0) return;
                _questIdArrowDd.SetValueWithoutNotify(0);   // the arrow just picks (ProfileDD idiom)
                int qi = idx - 1;
                if (qi < 0 || qi >= _questArrowIds.Count) return;
                string id = _questArrowIds[qi];
                if (LoadQuestBlockIntoForm(id)) Msg("Loaded '" + id + "' — edit, then Save Quest Changes");
                else Msg("Quest '" + id + "' not found in the quest DB");
            });
            clone.SetActive(true);
        }

        // Approved layout (user mockup): Type → right column, column-aligned under Quest Name (same row it
        // was on); Type's old slot → the "Add Quest" all-quests dropdown; Auto-Complete shrinks to label
        // size and sits inline RIGHT of the "Quest Description" caption. ALL moves are WORLD-space
        // (transform.position / GetWorldCorners) — the first attempt copied anchoredPosition between nodes
        // with different anchor setups, which moved nothing and overprinted the labels.
        private void MoveTypeUnderName(Transform scr)
        {
            if (_questLayoutMoved) return;
            var typeNode = FindRec(scr, "Type_C") as RectTransform;
            var acNode = FindRec(scr, "Auto-Complete_chk") as RectTransform;
            var nameNode = FindRec(scr, "Quest Name_C") as RectTransform;
            if (typeNode == null || acNode == null || nameNode == null)
            { Debug.LogWarning("[NpcBookController] quest layout: Type_C / Auto-Complete_chk / Quest Name_C missing"); return; }
            _questLayoutMoved = true;
            Canvas.ForceUpdateCanvases();

            var c = new Vector3[4];
            typeNode.GetWorldCorners(c); float typeLeft = c[0].x; float typeCy = (c[0].y + c[1].y) * 0.5f;
            _oldTypeWorldPos = typeNode.position;
            _oldTypeSlotParent = typeNode.parent;
            nameNode.GetWorldCorners(c); float nameLeft = c[0].x;

            // Type: keep its row, align its left edge with the Quest Name column.
            float shiftX = nameLeft - typeLeft;
            typeNode.position += new Vector3(shiftX, 0f, 0f);

            // Labels: the old "Type" caption stays put and captions the Add Quest dropdown; a clone
            // follows the moved Type dropdown with the same world shift.
            foreach (var t in scr.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || t.text == null || t.text.Trim().TrimEnd(':') != "Type") continue;
                var newLbl = UnityEngine.Object.Instantiate(t.gameObject, t.rectTransform.parent);
                newLbl.name = "Lbl_Type_Moved";
                ((RectTransform)newLbl.transform).position = t.rectTransform.position + new Vector3(shiftX, 0f, 0f);
                newLbl.GetComponent<TMP_Text>().text = "Type";
                t.text = "Add Quest";
                break;
            }

            // Auto-Complete: shrink to caption size and park it inline right of "Quest Description".
            TMP_Text descLbl = null;
            foreach (var t in scr.GetComponentsInChildren<TMP_Text>(true))
                if (t != null && t.text != null && t.text.Trim().TrimEnd(':') == "Quest Description") { descLbl = t; break; }
            var acLbl = acNode.GetComponentInChildren<TMP_Text>(true);
            if (descLbl != null)
            {
                float scale = 1f;
                if (acLbl != null && acLbl.fontSize > 0.1f)
                    scale = Mathf.Clamp(descLbl.fontSize / acLbl.fontSize, 0.4f, 1f);
                acNode.localScale = acNode.localScale * scale;
                Canvas.ForceUpdateCanvases();

                var dc = new Vector3[4]; descLbl.rectTransform.GetWorldCorners(dc);
                // Anchor to the end of the actual TEXT, not the label's rect (which spans wide and shoved the
                // toggle off the page edge). preferredWidth is the rendered text width in the label's local
                // space → world via lossyScale. Small gap = half the label height, so it scales with the UI.
                float descLeft = dc[0].x;
                // GetPreferredValues gives the UNWRAPPED single-line text width. preferredWidth reflected the
                // narrow label rect (the caption wraps "Quest / Description"), so it measured ~half and parked
                // the toggle over the text. Use the true rendered width so it lands past the whole word.
                float descTextRight = descLeft + descLbl.GetPreferredValues(descLbl.text).x * descLbl.transform.lossyScale.x;
                float descCy = (dc[0].y + dc[1].y) * 0.5f;
                var ac = new Vector3[4]; acNode.GetWorldCorners(ac);
                float acLeft = ac[0].x; float acCy = (ac[0].y + ac[1].y) * 0.5f;
                float gap = (dc[1].y - dc[0].y) * 0.5f;
                acNode.position += new Vector3((descTextRight + gap) - acLeft, descCy - acCy, 0f);
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] quest layout: shiftX={shiftX:F1} acScale={scale:F2} descTextRight={descTextRight:F1} acPos={acNode.position}");
            }
            else
            {
                // No caption found — leave AC where the bake put it rather than guessing.
                Debug.LogWarning("[NpcBookController] quest layout: 'Quest Description' caption not found — Auto-Complete left in place");
            }
            MakeAutoCompleteClickable(acNode);
        }

        // 0.0.814 (Fire 10-31: "the autocomplete checkbox does not work either in our admin book UI"): the layout above parks the
        // toggle on the "Quest Description" caption row, where the description input (later in the hierarchy, drawn on top) covers
        // it, so clicks landed on the text field and the box never flipped. The toggle gets its own canvas sorted above the page
        // with its own raycaster (the same way Core lifts its dock grips), and every flip logs one line.
        private void MakeAutoCompleteClickable(RectTransform acNode)
        {
            if (acNode == null) return;
            try
            {
                var pageCanvas = acNode.GetComponentInParent<Canvas>();
                var c = acNode.GetComponent<Canvas>() ?? acNode.gameObject.AddComponent<Canvas>();
                c.overrideSorting = true;
                c.sortingOrder = (pageCanvas != null ? pageCanvas.sortingOrder : 0) + 50;
                if (acNode.GetComponent<GraphicRaycaster>() == null) acNode.gameObject.AddComponent<GraphicRaycaster>();
                var tog = acNode.GetComponent<Toggle>();
                if (tog != null)
                {
                    tog.interactable = true;
                    tog.onValueChanged.RemoveListener(OnAutoCompleteChanged);
                    tog.onValueChanged.AddListener(OnAutoCompleteChanged);
                }
                global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] Auto-Complete toggle lifted above the description field (sorting {c.sortingOrder}, toggle {(tog != null ? "found" : "MISSING")})");
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] Auto-Complete toggle lift failed: " + ex.Message); }
        }
        private void OnAutoCompleteChanged(bool on) => global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] Auto-Complete toggled {(on ? "ON" : "OFF")}");

        // "Add Quest" dropdown in Type's old slot: every saved quest; pick → load into the editor.
        private void EnsureAllQuestsDropdown(Transform scr)
        {
            if (_allQuestsDd != null) { RefreshAllQuestsDropdown(); return; }
            if (!_questLayoutMoved || _oldTypeSlotParent == null) return;
            var typeDd = ScreenDd(scr, "Type_C");
            if (typeDd == null) return;

            var go = UnityEngine.Object.Instantiate(typeDd.gameObject, _oldTypeSlotParent);
            go.name = "AllQuestsDD";
            // World-space drop into Type's ORIGINAL slot — pivot-to-pivot of an identical control = exact.
            ((RectTransform)go.transform).position = _oldTypeWorldPos;
            _allQuestsDd = go.GetComponent<TMP_Dropdown>();
            if (_allQuestsDd == null) { UnityEngine.Object.Destroy(go); return; }
            _allQuestsDd.onValueChanged.RemoveAllListeners();
            RefreshAllQuestsDropdown();
            _allQuestsDd.onValueChanged.AddListener(idx =>
            {
                if (_allQuestsDd == null || idx <= 0) return;
                _allQuestsDd.SetValueWithoutNotify(0);
                int qi = idx - 1;
                if (qi < 0 || qi >= _allQuestIds.Count) return;
                string id = _allQuestIds[qi];
                if (LoadQuestBlockIntoForm(id)) Msg("Loaded '" + id + "' — Save to add it to this NPC's profile");
                else Msg("Quest '" + id + "' not found in the quest DB");
            });
            go.SetActive(true);
        }

        // Quest ID arrow list: ONLY the current NPC's profile quests (its live offer list).
        private void RefreshQuestIdArrow()
        {
            if (_questIdArrowDd == null) return;
            _questArrowIds.Clear();
            _questArrowLabelToId.Clear();
            var opts = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(PickCaption) };
            var qm = global::FiresRPGmaker.Modules.Quest.QuestManager.Instance;
            string prof = _npc?.questProfile;
            if (!string.IsNullOrEmpty(prof))
            {
                var p = QuestProfileManager.GetProfile(prof);
                var ids = (p?.QuestIds != null && p.QuestIds.Count > 0)
                    ? p.QuestIds
                    : (qm?.GetQuestsForProfile(prof) ?? new List<string>());
                foreach (var id in ids)
                {
                    var d = qm?.GetQuestDefinition(id);
                    string label = (d != null && !string.IsNullOrEmpty(d.Name)) ? $"{d.Name}  ({id})" : id;
                    _questArrowIds.Add(id);
                    _questArrowLabelToId[label] = id;
                    opts.Add(new TMP_Dropdown.OptionData(label));
                }
            }
            _questIdArrowDd.ClearOptions();
            _questIdArrowDd.AddOptions(opts);
            _questIdArrowDd.SetValueWithoutNotify(0);
            _questIdArrowDd.RefreshShownValue();
            if (_questIdArrowDd.template != null)
                _questIdArrowDd.template.sizeDelta = new Vector2(_questIdArrowDd.template.sizeDelta.x, Mathf.Clamp(opts.Count, 3, 10) * 24 + 8);
        }

        // Every saved quest, sorted by display name — the "Add Quest" list.
        private void RefreshAllQuestsDropdown()
        {
            if (_allQuestsDd == null) return;
            _allQuestIds.Clear();
            var opts = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData(PickCaption) };
            var qm = global::FiresRPGmaker.Modules.Quest.QuestManager.Instance;
            var all = qm != null ? qm.GetAllQuests() : new List<(string QuestId, string Name)>();
            foreach (var q in all.OrderBy(q => string.IsNullOrEmpty(q.Name) ? q.QuestId : q.Name, StringComparer.OrdinalIgnoreCase))
            {
                _allQuestIds.Add(q.QuestId);
                opts.Add(new TMP_Dropdown.OptionData(string.IsNullOrEmpty(q.Name) ? q.QuestId : $"{q.Name}  ({q.QuestId})"));
            }
            _allQuestsDd.ClearOptions();
            _allQuestsDd.AddOptions(opts);
            _allQuestsDd.SetValueWithoutNotify(0);
            _allQuestsDd.RefreshShownValue();
            if (_allQuestsDd.template != null)
            {
                // The clone inherited Type_C's skinny list template (sized for "Kill"/"Collect") — quest
                // names garbled into each other. Open at ~2.5x the control width (like the Quest ID list),
                // expanding rightward over the page; it's a popup, overlap while open is fine.
                var tpl = _allQuestsDd.template;
                float ctrlW = ((RectTransform)_allQuestsDd.transform).rect.width;
                float targetW = Mathf.Max(300f, ctrlW * 2.5f);
                bool stretch = Mathf.Abs(tpl.anchorMin.x - tpl.anchorMax.x) > 0.01f;
                tpl.pivot = new Vector2(0f, tpl.pivot.y);
                tpl.sizeDelta = new Vector2(stretch ? targetW - ctrlW : targetW, Mathf.Clamp(opts.Count, 3, 10) * 24 + 8);
            }
        }

        // Right-click on a Quest ID arrow entry → FAP-style context menu (Core FiresContextMenu.OpenCustom,
        // self-pumped parchment menu): Edit / Remove, with Remove behind an are-you-sure second menu.
        private void OnQuestArrowRightClick(string label)
        {
            if (_npc == null || string.IsNullOrEmpty(label)) return;
            if (!_questArrowLabelToId.TryGetValue(label.Trim(), out var qid)) return;
            string prof = _npc.questProfile;
            try { _questIdArrowDd?.Hide(); } catch { }

            Vector2 pos = UnityEngine.Input.mousePosition;
            var items = new List<global::FiresCore.UI.ContextMenu.ContextMenuItem>
            {
                global::FiresCore.UI.ContextMenu.ContextMenuItem.Row("Edit quest", () =>
                {
                    if (LoadQuestBlockIntoForm(qid)) Msg("Loaded '" + qid + "' — edit, then Save Quest Changes");
                    else Msg("Quest '" + qid + "' not found in the quest DB");
                }),
                global::FiresCore.UI.ContextMenu.ContextMenuItem.Sep(),
                global::FiresCore.UI.ContextMenu.ContextMenuItem.Row("Remove from profile", () =>
                {
                    // Second step: explicit are-you-sure before anything is deleted.
                    var confirm = new List<global::FiresCore.UI.ContextMenu.ContextMenuItem>
                    {
                        global::FiresCore.UI.ContextMenu.ContextMenuItem.Row("Yes — remove it", () =>
                        {
                            if (QuestProfileManager.RemoveQuestFromProfile(prof, qid))
                            {
                                RefreshQuestIdArrow();
                                Msg($"Removed '{qid}' from profile '{prof}' — players no longer see it on this NPC");
                            }
                            else Msg($"'{qid}' is not in profile '{prof}'");
                        }),
                        global::FiresCore.UI.ContextMenu.ContextMenuItem.Row("No — keep it", () => { })
                    };
                    global::FiresCore.UI.ContextMenu.FiresContextMenu.OpenCustom(
                        $"Remove '{qid}' from '{prof}'?", confirm, UnityEngine.Input.mousePosition);
                }, enabled: !string.IsNullOrEmpty(prof))
            };
            global::FiresCore.UI.ContextMenu.FiresContextMenu.OpenCustom(qid, items, pos);
        }

        // Rides the dropdown item template so every cloned row reports right-clicks with its label text.
        private sealed class QuestArrowItemRightClick : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
        {
            public static Action<string> OnRightPick;
            public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
            {
                if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Right) return;
                var label = GetComponentInChildren<TMP_Text>(true);
                if (label != null) OnRightPick?.Invoke(label.text);
            }
        }

        // ── save-choice popup: save the quest alone, or also add it to the profile ──────────────────────
        private GameObject _saveChoicePopup;
        private string _pendingSaveQuestId, _pendingSaveProfile, _pendingSaveCfg;

        private void ShowSaveChoicePopup()
        {
            CloseSaveChoice();
            if (_rightPage == null) { DoSaveQuest(true); return; }
            var go = new GameObject("QuestSaveChoice", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_rightPage, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0.07f, 0.34f); rt.anchorMax = new Vector2(0.93f, 0.62f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>(); img.color = new Color(0.12f, 0.085f, 0.05f, 0.97f); img.raycastTarget = true;
            _saveChoicePopup = go;

            var title = new GameObject("Title", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            title.transform.SetParent(go.transform, false);
            var trt = title.rectTransform;
            trt.anchorMin = new Vector2(0.04f, 0.58f); trt.anchorMax = new Vector2(0.96f, 0.97f);
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            title.text = $"Save '{_pendingSaveQuestId}' — also add it to profile '{_pendingSaveProfile}' so this NPC offers it?";
            title.fontSize = 14f; title.color = new Color(0.93f, 0.83f, 0.62f);
            title.alignment = TextAlignmentOptions.Center; title.enableWordWrapping = true;
            try { var refTmp = Screen("Quest")?.GetComponentInChildren<TMP_Text>(true); if (refTmp != null) title.font = refTmp.font; } catch { }

            var b1 = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(go.transform, "Save + Add",
                new Vector2(0.04f, 0.08f), new Vector2(0.35f, 0.48f), () => { CloseSaveChoice(); DoSaveQuest(true); });
            var b2 = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(go.transform, "Save Only",
                new Vector2(0.37f, 0.08f), new Vector2(0.66f, 0.48f), () => { CloseSaveChoice(); DoSaveQuest(false); });
            var b3 = FiresCore.UI.UIBuilderHelper.CreateParchmentButton(go.transform, "Cancel",
                new Vector2(0.68f, 0.08f), new Vector2(0.96f, 0.48f), CloseSaveChoice);
            StyleBookGoldButton(b1); StyleBookGoldButton(b2); StyleBookGoldButton(b3);
        }

        private void CloseSaveChoice()
        {
            if (_saveChoicePopup != null) { UnityEngine.Object.Destroy(_saveChoicePopup); _saveChoicePopup = null; }
        }

        // The actual write: quest block → local DB + server push; optionally profile membership too.
        private void DoSaveQuest(bool addToProfile)
        {
            string questId = _pendingSaveQuestId, profileName = _pendingSaveProfile, cfg = _pendingSaveCfg;
            if (string.IsNullOrEmpty(questId) || string.IsNullOrEmpty(cfg)) return;
            string questDbPath = Path.Combine(FiresCore.Storage.FiresConfigPaths.QuestDatabase, "QuestDatabase.cfg");
            if (!WriteBlockInPlace(questDbPath, questId, cfg, "Quests", "QuestDatabase.cfg")) { Msg("Quest save failed (see log)"); return; }
            try { global::FiresRPGmaker.Managers.FiresConfigSync.PushSavedConfig("Quests", "QuestDatabase.cfg", File.ReadAllText(questDbPath)); }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] quest DB push: " + ex.Message); }
            if (addToProfile)
            {
                try
                {
                    QuestProfileManager.EnsureProfileExists(profileName);
                    QuestProfileManager.AddQuestToProfile(profileName, questId);
                }
                catch (Exception ex) { Debug.LogWarning("[NpcBookController] quest profile: " + ex.Message); }
            }
            // Deliberately NOT touching the NPC: profile assignment is the LEFT page's job (Apply Setup).
            try { global::FiresRPGmaker.Modules.Quest.QuestManager.Instance?.LoadQuestDatabase(); } catch { }
            RefreshQuestIdArrow();
            RefreshAllQuestsDropdown();
            Msg(addToProfile ? $"Saved '{questId}' + added to profile '{profileName}'"
                             : $"Saved quest '{questId}' (library only — no profile changed)");
        }

        // 0.0.813: the authoring fields ApplyQuestHints describes, emptied (the hints stay as placeholders)
        private static readonly string[] s_questFields =
        {
            "Profile ID_C", "Quest Name_C", "Desc", "Target_C", "Amount_C", "Min Lvl_C", "Cooldown Days_C", "Limit Mins_C",
            "Rewards_C", "Required_C", "On Start Event_C", "On Complete_C", "Preview PNG_C",
        };
        private void ClearQuestForm()
        {
            int cleared = 0;
            foreach (var f in s_questFields)
            {
                string was = GetScreenInput("Quest", f);
                if (!string.IsNullOrEmpty(was)) { SetScreenInput("Quest", f, ""); cleared++; }
            }
            if (cleared > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] new quest page: cleared {cleared} field(s) of baked example text (hints stay greyed, never saved)");
        }

        // P1 hint pass: every quest field teaches its grammar while empty.
        private void ApplyQuestHints()
        {
            SetInputPlaceholder("Quest", "Profile ID_C", "unique quest id, e.g. wolf_hunt");
            SetInputPlaceholder("Quest", "Quest Name_C", "title players see in the journal");
            SetInputPlaceholder("Quest", "Desc", "journal description - supports <color=#hex>");
            SetInputPlaceholder("Quest", "Target_C", "prefab name, e.g. Wolf (Kill) or Raspberry (Collect)");
            SetInputPlaceholder("Quest", "Amount_C", "how many, e.g. 10");
            SetInputPlaceholder("Quest", "Min Lvl_C", "min star level, 0 = any");
            SetInputPlaceholder("Quest", "Cooldown Days_C", "in-game days before repeatable, empty = once");
            SetInputPlaceholder("Quest", "Limit Mins_C", "time limit in minutes, empty = none");
            SetInputPlaceholder("Quest", "Rewards_C", "Item,prefab,amount[,level] ; Skill,name,xp - comma rows, ; separated");
            SetInputPlaceholder("Quest", "Required_C", "conditions to accept, e.g. QuestFinished,intro ; SkillMore,Bows,20");
            SetInputPlaceholder("Quest", "On Start Event_C", "dialogue commands on accept, e.g. GiveItem,Torch,1,1");
            SetInputPlaceholder("Quest", "On Complete_C", "dialogue commands on completion, e.g. GiveBuff,GP_Eikthyr");
            SetInputPlaceholder("Quest", "Preview PNG_C", "optional PNG path or URL for the journal");
            SetInputPlaceholder("Quest", "Save To Profile_C", "profile this quest belongs to");
        }

        // The quest the book's single-quest editor should load for this NPC: prefer a quest whose id matches
        // the profile name (SaveQuest's default id==profile), else the profile's first quest. Null = nothing yet.
        private string ResolveNpcQuestId(string profileName)
        {
            if (string.IsNullOrEmpty(profileName)) return null;
            try
            {
                var qm = global::FiresRPGmaker.Modules.Quest.QuestManager.Instance;
                if (qm != null && qm.GetQuestDefinition(profileName) != null) return profileName;
            }
            catch { }
            var profile = QuestProfileManager.GetProfile(profileName);
            var first = profile?.QuestIds?.FirstOrDefault();
            return string.IsNullOrEmpty(first) ? null : first;
        }

        // Reads the raw [questId] block from the quest database files as key->value pairs (accepts '=' or ':'
        // delimited lines, matching both the book's own saves and KG-style packs). Returns null if not found.
        private Dictionary<string, string> ReadQuestBlockRaw(string questId)
        {
            try
            {
                string dir = FiresCore.Storage.FiresConfigPaths.QuestDatabase;
                if (!Directory.Exists(dir)) return null;
                foreach (var file in Directory.GetFiles(dir, "*.cfg"))
                {
                    var lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string t = lines[i].Trim();
                        if (!t.StartsWith("[") || !t.EndsWith("]")) continue;
                        string hdr = t.Substring(1, t.Length - 2).Split('=')[0].Trim();   // [id] or [id=autocomplete]
                        if (!string.Equals(hdr, questId, StringComparison.OrdinalIgnoreCase)) continue;

                        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            string b = lines[j].Trim();
                            if (b.StartsWith("[") && b.EndsWith("]")) break;
                            if (b.Length == 0 || b.StartsWith("#") || b.StartsWith(";")) continue;
                            int sep = b.IndexOf('=');
                            if (sep < 0) sep = b.IndexOf(':');
                            if (sep <= 0) continue;
                            string k = b.Substring(0, sep).Trim();
                            string v = b.Substring(sep + 1).Trim();
                            if (!map.ContainsKey(k)) map[k] = v;
                        }
                        return map;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] ReadQuestBlockRaw: " + ex.Message); }
            return null;
        }

        private void SaveQuest()
        {
            string questId = GetScreenInput("Quest", "Profile ID_C")?.Trim();
            if (string.IsNullOrEmpty(questId)) { Msg("Enter a Quest ID"); return; }
            string name = GetScreenInput("Quest", "Quest Name_C")?.Trim() ?? "";
            string type = GetScreenDropdown(Screen("Quest"), "Type_C");
            if (string.IsNullOrEmpty(type)) type = "Kill";
            if (type.Equals("Go To", StringComparison.OrdinalIgnoreCase)) type = "Move";
            bool autocomplete = GetToggle("Quest", "Auto-Complete_chk");
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] saving quest: Autocomplete = {(autocomplete ? "true" : "false")} (toggle {(Tog(Screen("Quest"), "Auto-Complete_chk") != null ? "found" : "MISSING")})");   // 0.0.814 round-trip proof
            string desc = GetScreenInput("Quest", "Desc")?.Trim() ?? "";
            string target = GetScreenInput("Quest", "Target_C")?.Trim() ?? "";
            // Blank number fields wrote "QuestTarget = Wolf," / "CooldownDays = ". Blank = the default now;
            // text or a negative number stops the save with the field named.
            if (!TryNumField("Quest", "Amount_C", "Amount", 1, 1, out int amountN)
                || !TryNumField("Quest", "Min Lvl_C", "Min level", 0, 0, out int minLvlN)
                || !TryNumField("Quest", "Cooldown Days_C", "Cooldown days", 0, 0, out int cooldownN)
                || !TryNumField("Quest", "Limit Mins_C", "Time limit (min)", 0, 0, out int timeLimitN)) return;
            string amount = amountN.ToString(), minLvl = minLvlN.ToString(), cooldown = cooldownN.ToString(), timeLimit = timeLimitN.ToString();
            string rewards = GetScreenInput("Quest", "Rewards_C")?.Trim() ?? "";
            string required = GetScreenInput("Quest", "Required_C")?.Trim() ?? "";
            if (global::FiresRPGmaker.Modules.Quest.QuestManager.IsPlaceholderRequirement(required)) required = "";   // 0.0.812: a "-" placeholder is saved as no requirement
            string onStart = GetScreenInput("Quest", "On Start Event_C")?.Trim() ?? "";
            string onComplete = GetScreenInput("Quest", "On Complete_C")?.Trim() ?? "";
            string preview = GetScreenInput("Quest", "Preview PNG_C")?.Trim() ?? "";
            // One profile field now (pick existing via the arrow OR type a new name) — no separate "Or New".
            string profileName = GetScreenInput("Quest", "Save To Profile_C")?.Trim();
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
            if (!string.IsNullOrEmpty(preview)) sb.AppendLine($"PreviewImage = {preview}");

            // Stage the save. If the quest is ALREADY in the target profile (normal "edit this NPC's
            // quest" flow) just save — membership unchanged. Otherwise ask: save the quest alone
            // (library, usable later), or also add it to the profile so this NPC starts offering it.
            _pendingSaveQuestId = questId;
            _pendingSaveProfile = profileName;
            _pendingSaveCfg = sb.ToString();
            bool inProfile = false;
            try
            {
                var existing = QuestProfileManager.GetProfile(profileName);
                inProfile = existing?.QuestIds != null && existing.QuestIds.Contains(questId, StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            if (inProfile) DoSaveQuest(false);
            else ShowSaveChoicePopup();
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
        // ONE canonical vocabulary (BookOptionPopup owns it) — aliased here so the baked popup's
        // dropdowns always list every verb the engine accepts.
        private static readonly string[] CommandNames = BookOptionPopup.CommandNames;
        private static readonly string[] ConditionNames = BookOptionPopup.ConditionNames;

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
            if (_shell != null) { RaiseSmallText(); ApplyReadability(); }   // rows made after the shell pass get the kit body size
            ReflowScroll(content);
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
            global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] OpenOptionPopup({index}) — options={_dlgOptions.Count}");
            if (index < 0 || index >= _dlgOptions.Count) return;
            _editingOptionIndex = index;
            if (_bookOptionPopup == null)
            {
                var host = new GameObject("BookOptionPopupHost");
                host.transform.SetParent(_root, false);
                _bookOptionPopup = host.AddComponent<BookOptionPopup>();
            }
            var bookRect = _panelGo.GetComponent<RectTransform>();
            string dlgUid = (GetScreenInput("Dialogue", "UID_C") ?? "").Trim().ToLowerInvariant();
            _bookOptionPopup.Open(_dlgOptions[index], bookRect, () => { _editingOptionIndex = -1; RebuildOptionRows(); SetDlgDirty(); }, dlgUid);
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
                // The file name is lowercased, as Delete uses (a mixed-case file could never be deleted).
                DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(d.UID.ToLowerInvariant()) + ".cfg", cfg);
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
            string uid = (GetScreenInput("Dialogue", "UID_C") ?? "").Trim();
            DeleteDialogueByUid(uid);
        }

        // Delete asks first: the first press arms it ("press Delete again"), a second press on the same
        // UID within DeleteConfirmSeconds deletes.
        private const float DeleteConfirmSeconds = 4f;
        private string _dlgDeleteArmedUid;
        private float _dlgDeleteArmedAt = -100f;

        private void DeleteDialogueByUid(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return;
            if (!string.Equals(_dlgDeleteArmedUid, uid, StringComparison.OrdinalIgnoreCase) || Time.unscaledTime - _dlgDeleteArmedAt > DeleteConfirmSeconds)
            {
                _dlgDeleteArmedUid = uid; _dlgDeleteArmedAt = Time.unscaledTime;
                SetDlgStatus($"Delete '{uid}'? Press Delete again to confirm.");
                Msg($"Press Delete again within {DeleteConfirmSeconds:0} s to delete dialogue '{uid}'");
                return;
            }
            _dlgDeleteArmedUid = null;
            // The lowercased file (how saves name it now) and, for one saved before, the file in its original case.
            try { DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(uid.ToLowerInvariant()) + ".cfg", null); } catch { }
            if (uid != uid.ToLowerInvariant())
                try { DialogueSyncRPC.PushDialogueToServer("legacy/" + SanitizeFileName(uid) + ".cfg", null); } catch { }
            if (string.Equals(uid, _dlgSelectedUid, StringComparison.OrdinalIgnoreCase)) NewDialogue();
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
            string profileName = InfoNpcScreenController.DefaultProfileNameFor(_npc);
            SetScreenInput("Info", "Info Profile_C", profileName);
            InfoNpcScreenController.InfoProfile p = null;
            try { p = InfoNpcScreenController.GetProfileForName(profileName); } catch { }
            SetScreenInput("Info", "Header Text_C", p?.HeaderText ?? "");
            SetScreenInput("Info", "Image (PNG)_C", p?.PngPath ?? "");
            SetScreenInput("Info", "InfoText", p?.InfoText ?? "");
            SetInputPlaceholder("Info", "Header Text_C", "title shown above the text");
            SetInputPlaceholder("Info", "Image (PNG)_C", "optional PNG path or URL");
            SetInputPlaceholder("Info", "InfoText", "body text - supports <color=#hex> and \\n line breaks");
        }

        private void SaveInfo()
        {
            string profileName = GetScreenInput("Info", "Info Profile_C")?.Trim();
            if (string.IsNullOrEmpty(profileName))
                profileName = InfoNpcScreenController.DefaultProfileNameFor(_npc);
            var profile = new InfoNpcScreenController.InfoProfile
            {
                HeaderText = GetScreenInput("Info", "Header Text_C")?.Trim() ?? "",
                PngPath = GetScreenInput("Info", "Image (PNG)_C")?.Trim() ?? "",
                InfoText = GetScreenInput("Info", "InfoText")?.Trim() ?? "",
            };
            try { InfoNpcScreenController.SaveInfoProfile(profileName, profile); }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveInfo: " + ex.Message); Msg("Save failed"); return; }
            _npc.infoProfile = profileName; _npc.npcType = NpcType.InfoNpc; _npc.SaveToZDO();
            // Push the ServerInfos.cfg CONTENT to the server (on a dedi the admin is a client) — without this
            // the server never gets the edit. SaveToZDO already pushes the NPC's infoProfile via SavedNpcManager;
            // this pushes the profile body. The non-empty unique profileName rides the authoritative push so the
            // server reload no longer reverts it to a blank/shared profile.
            InfoNpcScreenController.PushServerInfosToServer();
            Msg("Saved info profile '" + profileName + "'");
        }

        // ════════════════════════════ BUFFERS ═══════════════════════════════
        // Assigns which buffer profile this NPC sells; the profile's buffs are built in the standalone
        // Buff Builder (BufferAdminPanel). Interacting with this NPC opens the buff shop for the profile.
        private void PopulateBuffer()
        {
            string profile = string.IsNullOrEmpty(_npc.bufferProfile) ? "default" : _npc.bufferProfile;
            SetScreenInput("Buffers", "BufferProfile", profile);
            SetInputPlaceholder("Buffers", "BufferProfile", "buff profile name - build its buffs in the Buff Builder");
        }

        // The Buff Builder opens on the profile typed on the Buffers tab (or the NPC's own), not empty.
        private string BufferProfileForEdit()
        {
            string p = GetScreenInput("Buffers", "BufferProfile")?.Trim();
            if (string.IsNullOrEmpty(p)) p = _npc != null && !string.IsNullOrEmpty(_npc.bufferProfile) ? _npc.bufferProfile : "default";
            return p.ToLowerInvariant();
        }

        private void SaveBuffer()
        {
            string profile = GetScreenInput("Buffers", "BufferProfile")?.Trim();
            if (string.IsNullOrEmpty(profile)) profile = string.IsNullOrEmpty(_npc.bufferProfile) ? "default" : _npc.bufferProfile;
            profile = profile.ToLowerInvariant();
            _npc.bufferProfile = profile; _npc.npcType = NpcType.Buffer; _npc.SaveToZDO();
            Msg("Saved buffer NPC (profile '" + profile + "')");
        }

        // ════════════════════════════ MAIL ══════════════════════════════════
        // v1: appends a mail row to the recipient profile's .cfg + writes the global post-tunables to
        // MarketPlace.cfg. A networked player-to-player broadcast RPC is a later pass.
        // -1 = the edit fields compose a NEW mail; >=0 = editing that saved entry (Save overwrites it).
        private int _editingMailIndex = -1;

        private void PopulateMail()
        {
            string profile = _npc.mailProfile;
            SetScreenInput("Mail", "Recipient_C", profile);
            // The post cost / wait come from the server's MarketPlace.cfg (MarketCfgSync).
            EnsureMarketCfg(PopulateMail);
            string mcfg = global::FiresRPGmaker.Managers.MarketCfgSync.Current();
            if (mcfg != null)
            {
                SetScreenInput("Mail", "Post Cost_C", MarketGet(mcfg, "MailPostRecipe", ""));
                SetScreenInput("Mail", "Wait (s)_C", MarketGet(mcfg, "MailPostWaitTime", ""));
            }
            SetInputPlaceholder("Mail", "Recipient_C", "player name, or * for everyone");
            SetInputPlaceholder("Mail", "Post Cost_C", "e.g. Coins:5 - cost to send (empty = free)");
            SetInputPlaceholder("Mail", "Wait (s)_C", "delivery delay in seconds (empty = instant)");
            PopulateMailEntries(profile);
            ClearMailEditFields();
        }

        // Fills the saved-mails browse list for a profile; each row has Edit (load into the fields) + X (delete).
        private void PopulateMailEntries(string profile)
        {
            ClearRows("Mail", "MailEntryPool");
            try
            {
                MailProfileManager.EnsureLoaded();
                var mail = string.IsNullOrEmpty(profile) ? null : MailProfileManager.GetMail(profile);
                if (mail != null) for (int i = 0; i < mail.Count; i++) AddMailEntryRow(i, mail[i]);
            }
            catch { }
            RestackRows("Mail", "MailEntryPool");
        }

        private void AddMailEntryRow(int index, MailEntry entry)
        {
            var pool = FindRec(Screen("Mail"), "MailEntryPool");
            var tmpl = FindRec(pool, "MailEntryRowTemplate")?.gameObject;
            if (pool == null || tmpl == null) return;
            var row = Instantiate(tmpl, PoolContent(pool)); row.name = "Row"; row.SetActive(true);
            var sum = FindRec(row.transform, "SummaryLabel")?.GetComponent<TMP_Text>();
            if (sum != null) sum.text = (entry.SenderName ?? "?") + " — " + (entry.Subject ?? "");
            int captured = index;
            HookRow(row.transform, "EditButton", () => LoadMailEntry(captured));
            HookRow(row.transform, "RemoveButton", () => DeleteMailEntry(captured));
        }

        private void LoadMailEntry(int index)
        {
            var mail = MailProfileManager.GetMail(GetScreenInput("Mail", "Recipient_C")?.Trim());
            if (mail == null || index < 0 || index >= mail.Count) return;
            var e = mail[index];
            _editingMailIndex = index;
            SetScreenInput("Mail", "Sender_C", e.SenderName);
            SetScreenInput("Mail", "MailTopic", e.Subject);
            SetScreenInput("Mail", "MailBody", e.Body);
            SetScreenInput("Mail", "Attachment_C", SerializeAttachments(e.Attachments));
            Msg("Editing mail #" + (index + 1));
        }

        private void DeleteMailEntry(int index)
        {
            string profile = GetScreenInput("Mail", "Recipient_C")?.Trim();
            if (string.IsNullOrEmpty(profile)) return;
            var mail = new List<MailEntry>(MailProfileManager.GetMail(profile));
            if (index < 0 || index >= mail.Count) return;
            mail.RemoveAt(index);
            MailProfileManager.SaveProfile(profile, string.Join(Environment.NewLine, mail.Select(SerializeMailRow)));
            PopulateMailEntries(profile);
            ClearMailEditFields();
            Msg("Deleted mail");
        }

        private void ClearMailEditFields()
        {
            _editingMailIndex = -1;
            SetScreenInput("Mail", "Sender_C", "");
            SetScreenInput("Mail", "MailTopic", "");
            SetScreenInput("Mail", "MailBody", "");
            SetScreenInput("Mail", "Attachment_C", "");
        }

        private static string SerializeAttachments(List<MailAttachment> atts)
            => (atts == null || atts.Count == 0) ? "" : string.Join(";", atts.Select(a => a.Prefab + ":" + a.Count));

        // One pipe-delimited row, body newlines escaped so the line-per-row loader survives multi-line bodies.
        private static string SerializeMailRow(MailEntry e)
        {
            string body = (e.Body ?? "").Replace("\r", "").Replace("\n", "\\n");
            string att = SerializeAttachments(e.Attachments);
            return e.SenderName + "|" + e.DateString + "|" + e.Subject + "|" + body + (string.IsNullOrEmpty(att) ? "" : "|" + att);
        }

        private void SaveMail()
        {
            string recipient = GetScreenInput("Mail", "Recipient_C")?.Trim();
            if (string.IsNullOrEmpty(recipient)) recipient = _npc.mailProfile;
            if (string.IsNullOrEmpty(recipient)) { Msg("Enter a recipient/profile"); return; }
            if (!TryNumField("Mail", "Wait (s)_C", "Wait (s)", -1, 0, out int waitS)) return;   // blank = instant
            string sender = GetScreenInput("Mail", "Sender_C")?.Trim() ?? "";
            string topic = GetScreenInput("Mail", "MailTopic")?.Trim() ?? "";
            string body = (GetScreenInput("Mail", "MailBody") ?? "").Replace("\r", "").Replace("\n", "\\n");
            string attach = GetScreenInput("Mail", "Attachment_C")?.Trim() ?? "";
            string date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string row = sender + "|" + date + "|" + topic + "|" + body + (string.IsNullOrEmpty(attach) ? "" : "|" + attach);
            try
            {
                MailProfileManager.EnsureLoaded();
                var lines = MailProfileManager.GetMail(recipient).Select(SerializeMailRow).ToList();
                if (_editingMailIndex >= 0 && _editingMailIndex < lines.Count) lines[_editingMailIndex] = row;   // overwrite the edited entry
                else lines.Add(row);                                                                              // else append a new one
                MailProfileManager.SaveProfile(recipient, string.Join(Environment.NewLine, lines));
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveMail: " + ex.Message); }
            // Onto the server's copy, only these two keys. Not received yet = the mail is saved, the settings wait.
            string mcfg = global::FiresRPGmaker.Managers.MarketCfgSync.Current();
            if (mcfg != null)
            {
                mcfg = MarketSet(mcfg, "MailPostRecipe", GetScreenInput("Mail", "Post Cost_C")?.Trim() ?? "");
                mcfg = MarketSet(mcfg, "MailPostWaitTime", waitS < 0 ? "" : waitS.ToString());
                try { global::FiresRPGmaker.Managers.MarketCfgSync.Save(mcfg); } catch (Exception ex) { Debug.LogWarning("[NpcBookController] SaveMail cfg: " + ex.Message); }
            }
            else { _marketCfgAsked = false; EnsureMarketCfg(null); Msg("Mail saved; post cost / wait not saved (the server's settings haven't arrived yet)"); }
            _npc.mailProfile = recipient; _npc.npcType = NpcType.Mail; _npc.SaveToZDO();
            PopulateMailEntries(recipient);
            ClearMailEditFields();
            Msg("Saved mail to '" + recipient + "'");
        }

        // ════════════════════════════ MARKETPLACE ═══════════════════════════
        // Server-wide MarketPlace.cfg [Main] (no per-NPC profile); the live Marketplace plugin's file
        // watcher re-syncs on Save().
        private void PopulateMarketplace()
        {
            // The SERVER's MarketPlace.cfg (MarketCfgSync), never the admin's local file on a dedi client.
            EnsureMarketCfg(PopulateMarketplace);
            string text = global::FiresRPGmaker.Managers.MarketCfgSync.Current();
            if (text != null)
            {
                SetScreenInput("Marketplace", "Item Slot Limit_C", MarketGet(text, "ItemMarketLimit", "15"));
                SetScreenInput("Marketplace", "Market Tax %_C", MarketGet(text, "MarketTaxes", "0"));
                SetScreenInput("Marketplace", "VIP Tax %_C", MarketGet(text, "VIPplayersTaxes", "0"));
                SetScreenInput("Marketplace", "Currencies_C", MarketGet(text, "PossibleCurrencies", "Coins"));
                SetScreenInput("Marketplace", "VipList", MarketGet(text, "VIPplayersList", "User IDs"));
                SetScreenInput("Marketplace", "BlockList", MarketGet(text, "BlockedPlayers", "User IDs"));
                SetScreenInput("Marketplace", "BlockPrefabs", MarketGet(text, "MarketSellBlockedPrefabs", "Coins, SwordCheat"));
            }
            SetInputPlaceholder("Marketplace", "Item Slot Limit_C", "max listings per player, e.g. 10");
            SetInputPlaceholder("Marketplace", "Market Tax %_C", "seller tax 0-100");
            SetInputPlaceholder("Marketplace", "VIP Tax %_C", "reduced tax for VIP players");
            SetInputPlaceholder("Marketplace", "Currencies_C", "accepted currency prefabs, comma separated");
            SetInputPlaceholder("Marketplace", "VipList", "player names or SteamID64s, comma separated");
            SetInputPlaceholder("Marketplace", "BlockList", "players banned from the market");
            SetInputPlaceholder("Marketplace", "BlockPrefabs", "items that can never be listed");
        }

        private void SaveMarketplace()
        {
            // Edits the server's copy (only the keys this tab shows; every other line kept), numbers checked.
            string text = global::FiresRPGmaker.Managers.MarketCfgSync.Current();
            if (text == null) { _marketCfgAsked = false; EnsureMarketCfg(null); Msg("Still waiting for the server's marketplace settings; try Save again in a moment"); return; }
            if (!TryNumField("Marketplace", "Item Slot Limit_C", "Item slot limit", 15, 0, out int limit)
                || !TryNumField("Marketplace", "Market Tax %_C", "Market tax %", 0, 0, out int tax)
                || !TryNumField("Marketplace", "VIP Tax %_C", "VIP tax %", 0, 0, out int vipTax)) return;
            if (tax > 100 || vipTax > 100) { Msg("Taxes are percentages: 0 to 100"); return; }
            try
            {
                text = MarketSet(text, "ItemMarketLimit", limit.ToString());
                text = MarketSet(text, "MarketTaxes", tax.ToString());
                text = MarketSet(text, "VIPplayersTaxes", vipTax.ToString());
                text = MarketSet(text, "PossibleCurrencies", JoinList(GetScreenInput("Marketplace", "Currencies_C")));
                text = MarketSet(text, "VIPplayersList", JoinList(GetScreenInput("Marketplace", "VipList")));
                text = MarketSet(text, "BlockedPlayers", JoinList(GetScreenInput("Marketplace", "BlockList")));
                text = MarketSet(text, "MarketSellBlockedPrefabs", JoinList(GetScreenInput("Marketplace", "BlockPrefabs")));
                global::FiresRPGmaker.Managers.MarketCfgSync.Save(text);
            }
            catch (Exception ex) { Debug.LogError("[NpcBookController] SaveMarketplace: " + ex.Message); Msg("Save failed"); return; }
            _npc.npcType = NpcType.Marketplace; _npc.SaveToZDO();
            Msg("Saved marketplace settings");
        }

        // ── cfg write helpers ────────────────────────────────────────────────
        private static string MarketplaceCfgPath => Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");

        // MarketPlace.cfg through MarketCfgSync. A client asks the server once per book session; when the
        // reply arrives the tab that asked is filled again (its fields stay as they are until then).
        private bool _marketCfgAsked;
        private Action _marketCfgRefill;
        private void EnsureMarketCfg(Action refill)
        {
            if (refill != null) _marketCfgRefill = refill;
            if (global::FiresRPGmaker.Managers.MarketCfgSync.Received || _marketCfgAsked) return;
            _marketCfgAsked = true;
            global::FiresRPGmaker.Managers.MarketCfgSync.Arrived -= OnMarketCfgArrived;
            global::FiresRPGmaker.Managers.MarketCfgSync.Arrived += OnMarketCfgArrived;
            global::FiresRPGmaker.Managers.MarketCfgSync.Request();
        }
        private void OnMarketCfgArrived()
        {
            global::FiresRPGmaker.Managers.MarketCfgSync.Arrived -= OnMarketCfgArrived;
            var refill = _marketCfgRefill; _marketCfgRefill = null;
            if (this != null && _npc != null) refill?.Invoke();
        }
        private static string MarketGet(string text, string key, string def) => global::FiresRPGmaker.Managers.MarketCfgSync.Get(text, "Main", key, def);
        private static string MarketSet(string text, string key, string value) => global::FiresRPGmaker.Managers.MarketCfgSync.Set(text, "Main", key, value);

        // Overwrite-in-place an INI-style [key] block (header line + lines until the next "[...]"), else append.
        // The authoritative current lines of a synced cfg: the RAM sync store on a pure
        // client (the config the admin actually RECEIVED), the on-disk file on the
        // server/host. Writers upsert onto THIS so a client push is never sparser than
        // the server's set — a client keeps the real cfg only in memory, so basing an
        // edit on its (absent) disk copy is what let a single "save" wipe the server.
        private string[] ReadSyncedBaseLines(string syncFolder, string syncFile, string diskPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(syncFolder)
                    && global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
                {
                    foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.Enumerate(syncFolder))
                    {
                        string rel = kv.Key.Replace('\\', '/');
                        string leaf = rel.Substring(rel.LastIndexOf('/') + 1);
                        if (string.Equals(leaf, syncFile, StringComparison.OrdinalIgnoreCase))
                            return SplitLines(kv.Value ?? "");
                    }
                    return new string[0]; // client, store has no such file yet → new file
                }
            }
            catch (Exception ex) { Debug.LogWarning("[NpcBookController] ReadSyncedBaseLines: " + ex.Message); }
            try { return File.Exists(diskPath) ? File.ReadAllLines(diskPath) : new string[0]; }
            catch { return new string[0]; }
        }

        private bool WriteBlockInPlace(string filePath, string blockKey, string newBlock, string syncFolder = null, string syncFile = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                var outLines = new List<string>();
                bool replaced = false;
                string[] baseLines = ReadSyncedBaseLines(syncFolder, syncFile, filePath);
                if (baseLines.Length > 0)
                {
                    var lines = baseLines;
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

        // ── profile dropdowns (a "Saved Profiles" arrow on every profile input) ──────────────
        // Each module tab → its profile-name input node + the NpcType whose saved profiles fill the list.
        private static readonly Dictionary<string, (string node, NpcType type)> ProfileInputNodes =
            new Dictionary<string, (string node, NpcType type)>
            {
                { "Trader",      ("Trader Profile_C",  NpcType.Trader) },
                { "Banker",      ("Banker Profile_C",  NpcType.Banker) },
                { "Gambler",     ("Profile Name_C",    NpcType.Gambler) },
                { "Transmog",    ("TmProfile",         NpcType.Transmog) },
                { "Quest",       ("Save To Profile_C", NpcType.QuestNpc) },
                { "Buffers",     ("BufferProfile",     NpcType.Buffer) },
                { "Mail",        ("Recipient_C",       NpcType.Mail) },
                { "Info",        ("Info Profile_C",    NpcType.InfoNpc) },
            };

        private readonly Dictionary<string, TMP_Dropdown> _profileDd = new Dictionary<string, TMP_Dropdown>();

        // Adds (once) or refreshes the saved-profile arrow on the active tab's profile input. Called at the
        // end of every screen populate so the list is always current (a profile saved on another screen
        // shows here without reopening the book).
        private void EnsureActiveScreenProfileDropdown()
        {
            if (_npc == null || string.IsNullOrEmpty(_activeTab)) return;
            if (!ProfileInputNodes.TryGetValue(_activeTab, out var pi)) return;
            NpcType type = pi.type;
            // Quest tab: "Save To Profile" is only the SAVE TARGET for authoring — picking one must NOT
            // reassign the NPC (that's the left page's Apply Setup) and must NOT re-populate the screen
            // (which would clobber the quest the admin is mid-editing). The arrow just fills the input.
            if (_activeTab == "Quest")
            {
                EnsureProfileDropdown(_activeTab, Screen(_activeTab), pi.node, type, null);
                return;
            }
            EnsureProfileDropdown(_activeTab, Screen(_activeTab), pi.node, type, name =>
            {
                // Picking a saved profile loads it into the form only. The NPC used to be pointed at it
                // (type + profile, in memory) before Apply/Save; now the form is filled from it and the NPC is put back,
                // so the tab's Save (or the left page's Apply) is what commits it.
                string oldProfile = _npc.GetProfileForType(type);
                NpcType oldType = _npc.npcType;
                _npc.SetProfileForType(type, name);
                _npc.npcType = type;
                try { PopulateActiveScreen(); }
                finally { _npc.SetProfileForType(type, oldProfile); _npc.npcType = oldType; }
            });
        }

        // Wires the BAKED "ProfileDD" dropdown next to a profile input (built by FiresNpcBookBuild.ProfileField).
        // Fills it with the module's saved profiles; picking one fills the input and runs onPicked. Options are
        // refreshed every call (so a profile saved elsewhere shows up); the onValueChanged is attached once.
        private void EnsureProfileDropdown(string key, Transform scope, string inputNode, NpcType type, Action<string> onPicked)
        {
            if (_npc == null || scope == null) return;
            var input = InputIn(scope, inputNode);
            var dd = FindRec(scope, "ProfileDD")?.GetComponent<TMP_Dropdown>();
            if (dd == null || input == null) return;
            PopulateProfileDropdown(dd, NpcController.GetProfileNamesForType(type));
            MakeFieldPicker(dd, input);   // the field is the dropdown (NPC Type look)
            if (_profileDd.TryGetValue(key, out var wired) && wired == dd) return;
            _profileDd[key] = dd;
            dd.onValueChanged.AddListener(idx =>
            {
                if (dd == null || idx <= 0) return;
                string picked = idx < dd.options.Count ? dd.options[idx].text : null;
                dd.SetValueWithoutNotify(0);
                if (!string.IsNullOrEmpty(picked)) { input.SetTextWithoutNotify(picked); onPicked?.Invoke(picked); }
            });
        }

        // Index 0 is a short no-op header ("—"); the input field shows the chosen name, the arrow just picks.
        private static void PopulateProfileDropdown(TMP_Dropdown dd, IEnumerable<string> names)
        {
            var sorted = (names ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).OrderBy(n => n).ToList();
            var opts = new List<TMP_Dropdown.OptionData>(sorted.Count + 1) { new TMP_Dropdown.OptionData(PickCaption) };
            foreach (var n in sorted) opts.Add(new TMP_Dropdown.OptionData(n));
            dd.ClearOptions();
            dd.AddOptions(opts);
            dd.SetValueWithoutNotify(0);
        }

        // ── baked labeled-dropdown read/write (Quest Type, Leaderboard Trigger Type, left-page NPC Type) ──────
        private TMP_Dropdown ScreenDd(Transform scope, string node) => FindRec(scope, node)?.GetComponent<TMP_Dropdown>();
        private void SetScreenDropdown(Transform scope, string node, string val)
        {
            var dd = ScreenDd(scope, node);
            if (dd == null) return;
            int idx = dd.options.FindIndex(o => string.Equals(o.text, val, StringComparison.OrdinalIgnoreCase));
            dd.SetValueWithoutNotify(idx >= 0 ? idx : 0);
            if (dd.captionText != null && dd.value < dd.options.Count) dd.captionText.text = dd.options[dd.value].text;
        }
        private string GetScreenDropdown(Transform scope, string node)
        {
            var dd = ScreenDd(scope, node);
            return (dd != null && dd.value >= 0 && dd.value < dd.options.Count) ? dd.options[dd.value].text : "";
        }

        // ── dynamic list rows (gambler reward pool, transmog offerings) ──────
        // Cell layout for runtime-built rows (used when the prefab has no baked row template).
        private void AddRewardRow(GamblerItemEntry data)
        {
            var row = AddRow("Gambler", "RewardPool", "RewardRowTemplate");
            if (row == null) { Msg("Gambler reward pool missing (bad prefab)"); return; }
            if (data != null)
            {
                SetRowCell(row.transform, "ItemCell", data.Prefab);
                SetRowCell(row.transform, "MinCell", data.Min.ToString());
                SetRowCell(row.transform, "MaxCell", data.Max.ToString());
            }
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); UpdateGamblerStatus(null); });
            UpdateGamblerStatus(null);
        }

        private void AddOfferingRow(TransmogOption data)
        {
            var row = AddRow("Transmog", "OfferingPool", "OfferingRowTemplate");
            if (row == null) { Msg("Transmog offering pool missing (bad prefab)"); return; }
            if (data != null)
            {
                SetRowCell(row.transform, "SlotCell", data.Slot);
                SetRowCell(row.transform, "AppearanceCell", data.Prefab);
                SetRowCell(row.transform, "CurrencyCell", data.PricePrefab);
                SetRowCell(row.transform, "PriceCell", data.PriceAmount.ToString());
            }
            AttachSlotDropdown(row.transform);
            HookRow(row.transform, "RemoveButton", () => { row.SetActive(false); Destroy(row); UpdateTransmogStatus(null); });
            UpdateTransmogStatus(null);
        }

        private GameObject AddRow(string tab, string poolName, string templateName)
        {
            var anchor = FindRec(Screen(tab), poolName);
            if (anchor == null) return null;
            var content = InputScroll(anchor);
            if (content == null) return null;
            var tmpl = FindRec(content, templateName)?.gameObject ?? FindRec(anchor, templateName)?.gameObject;
            if (tmpl == null) return null;
            var row = Instantiate(tmpl, content);
            row.name = "Row";
            row.SetActive(true);
            var le = row.GetComponent<LayoutElement>() ?? row.AddComponent<LayoutElement>();
            if (le.preferredHeight <= 0f) le.preferredHeight = 30f;
            if (_shell != null) RaiseSmallText();   // dynamically added rows get the kit body size
            return row;
        }

        // Verbatim copy of the working Dressing Room FashionScroll: ScrollRect + clear-Image/RectMask2D viewport
        // + VerticalLayoutGroup/ContentSizeFitter content. The baked RowTemplate is moved under Content on first
        // build so AddRow can clone it. Same construction that makes the dressing input fields accept typing.
        private static Transform InputScroll(Transform anchor)
        {
            if (anchor == null) return null;
            var built = anchor.Find("InputScroll/Viewport/Content");
            if (built != null) return built;
            var scrollGo = new GameObject("InputScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(anchor, false);
            var srt = (RectTransform)scrollGo.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one; srt.offsetMin = Vector2.zero; srt.offsetMax = Vector2.zero;
            var sr = scrollGo.GetComponent<ScrollRect>();
            var vpGo = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vrt = (RectTransform)vpGo.transform;
            vrt.anchorMin = Vector2.zero; vrt.anchorMax = Vector2.one; vrt.offsetMin = Vector2.zero; vrt.offsetMax = Vector2.zero;
            vpGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            var crt = (RectTransform)cGo.transform;
            crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero; crt.sizeDelta = Vector2.zero;
            var vlg = cGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true; vlg.childControlHeight = true; vlg.childForceExpandHeight = false;
            vlg.spacing = 3f; vlg.padding = new RectOffset(2, 2, 2, 2);
            var csf = cGo.AddComponent<ContentSizeFitter>(); csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vrt; sr.content = crt; sr.horizontal = false; sr.vertical = true; sr.scrollSensitivity = 26f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            for (int i = anchor.childCount - 1; i >= 0; i--)
            {
                var ch = anchor.GetChild(i);
                if (ch.name.EndsWith("Template", StringComparison.Ordinal)) ch.SetParent(crt, false);
            }
            return crt;
        }

        private void ClearInputRows(string tab, string poolName)
        {
            var host = InputScroll(FindRec(Screen(tab), poolName));
            if (host == null) return;
            for (int i = host.childCount - 1; i >= 0; i--)
            {
                var c = host.GetChild(i);
                if (c.name == "Row") { c.gameObject.SetActive(false); Destroy(c.gameObject); }
            }
        }

        private List<Transform> LiveInputRows(string tab, string poolName)
        {
            var list = new List<Transform>();
            var host = InputScroll(FindRec(Screen(tab), poolName));
            if (host == null) return list;
            foreach (Transform c in host) if (c.name == "Row" && c.gameObject.activeSelf) list.Add(c);
            return list;
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
            // Every input / dropdown / checkbox box gets the kit's slot border (FiresTokens.Hairline, the same 1 px line the kit slots use)
            int wells = 0;
            foreach (var sel in root.GetComponentsInChildren<Selectable>(true))
            {
                if (!(sel is TMP_InputField || sel is TMP_Dropdown || sel is Toggle)) continue;
                if (sel.GetComponentInParent<TMP_Dropdown>(true) != null && !(sel is TMP_Dropdown)) continue;   // a dropdown's option rows
                var g = sel.targetGraphic as Image;
                if (g == null || g.GetComponent<Outline>() != null) continue;
                var o = g.gameObject.AddComponent<Outline>();
                o.effectColor = FiresCore.UI.Shell.Kit.FiresTokens.Hairline; o.effectDistance = new Vector2(1f, -1f);
                wells++;
            }
            if (wells > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] field wells: {wells} input/dropdown/checkbox box(es) given the kit border");
        }

        // player-facing text only, never a raw node / config key on screen.
        private const string PickCaption = "";   // the pickers' no-op first row is blank; the field shows the value
        private const string IdleWanderText = "Wander when idle";
        private static readonly Dictionary<string, string> s_friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AllowIdleWander_chk", "Idle wander" }, { "AllowIdleWander", IdleWanderText },
            { "Auto-Complete_chk", "Auto-complete" },
            { "Cards_chk", "Cards" }, { "Bones_chk", "Bones" }, { "Runes_chk", "Runes" }, { "Wheel_chk", "Wheel" },
        };
        internal static string FriendlyFieldLabel(string node) => node != null && s_friendly.TryGetValue(node, out var v) ? v : null;
        // a text that is a raw key: a node name with "_chk" / " chk", or one CamelCase word of 2+ humps (AllowIdleWander)
        internal static bool IsRawKeyText(string t)
        {
            if (string.IsNullOrWhiteSpace(t)) return false;
            t = t.Trim();
            if (t.EndsWith("_chk", StringComparison.Ordinal) || t.EndsWith(" chk", StringComparison.Ordinal)) return true;
            if (!System.Text.RegularExpressions.Regex.IsMatch(t, "^[A-Z][a-zA-Z]+$")) return false;
            int humps = 0; for (int i = 1; i < t.Length; i++) if (char.IsUpper(t[i]) && char.IsLower(t[i - 1])) humps++;
            return humps >= 2 || s_friendly.ContainsKey(t);
        }
        // every visible-or-not text under root that is a raw key gets its friendly name (or a spaced, capitalised form)
        internal static int RelabelRawTexts(Transform root)
        {
            if (root == null) return 0;
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || !IsRawKeyText(t.text) || t.GetComponentInParent<TMP_InputField>(true) != null || t.GetComponentInParent<TMP_Dropdown>(true) != null) continue;   // never a typed value or a picked name
                string raw = t.text.Trim();
                string f = FriendlyFieldLabel(raw);
                if (f == null)
                {
                    string s = raw.Replace("_chk", "").Replace(" chk", "");
                    s = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ').Trim();
                    f = s.Length > 1 ? char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant() : s;
                }
                t.text = f; n++;
            }
            if (n > 0) global::FiresRPGmaker.FiresLogger.LogDebug($"[NpcBookController] {n} raw key text(s) under '{root.name}' replaced by player-facing labels");
            return n;
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

        // A whole-number field. Blank = def; text, or below min, fails with the field named in the message.
        private bool TryNumField(string tab, string container, string label, int def, int min, out int value)
        {
            return TryNumText(GetScreenInput(tab, container), label, def, min, out value);
        }

        private bool TryNumText(string raw, string label, int def, int min, out int value)
        {
            string s = raw?.Trim();
            value = def;
            if (string.IsNullOrEmpty(s)) return true;
            if (!int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value))
            { Msg($"{label}: '{s}' is not a whole number"); return false; }
            if (value < min) { Msg($"{label} must be {min} or more (got {value})"); return false; }
            return true;
        }

        // The same for decimals (interest rates and the like).
        private bool TryFloatField(string tab, string container, string label, float def, float min, out float value)
        {
            string s = GetScreenInput(tab, container)?.Trim();
            value = def;
            if (string.IsNullOrEmpty(s)) return true;
            if (!float.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            { Msg($"{label}: '{s}' is not a number"); return false; }
            if (value < min) { Msg($"{label} must be {min} or more (got {value})"); return false; }
            return true;
        }
        // Format hints on baked inputs (the P1 admin-UX pass): shown while the field is empty AND as a
        // hover tooltip over the field (toggle: UI/ShowAdminHints). Topic = the field's clean label.
        private void SetInputPlaceholder(string tab, string container, string hint)
        {
            var i = InputIn(Screen(tab), container);
            if (i == null) return;
            if (i.placeholder is TMP_Text ph) ph.text = hint ?? "";
            RegisterHover(i.gameObject, CleanFieldLabel(container), hint);
        }

        // Records a field's rect + help for the manual hover poll (TickHover). Deduped by rect, so
        // re-populating a tab just refreshes the text; inactive-tab fields are skipped at poll time.
        private void RegisterHover(GameObject go, string topic, string body)
        {
            if (go == null || string.IsNullOrEmpty(body)) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null) _hoverFields[rt] = (topic, body);
        }

        // "Cost Item_C" → "Cost Item", "Container_ModelOverrideInput" → "Model Override", etc.
        private static string CleanFieldLabel(string node)
        {
            if (string.IsNullOrEmpty(node)) return "";
            string s = node;
            if (s.EndsWith("_C")) s = s.Substring(0, s.Length - 2);
            if (s.StartsWith("Container_")) s = s.Substring("Container_".Length);
            if (s.EndsWith("Input")) s = s.Substring(0, s.Length - "Input".Length);
            var sb = new StringBuilder();
            for (int k = 0; k < s.Length; k++)
            {
                char c = s[k];
                if (k > 0 && char.IsUpper(c) && !char.IsUpper(s[k - 1]) && s[k - 1] != ' ') sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Replace('_', ' ').Trim();
        }

        // Checkboxes have no checkmark graphic, so give each Toggle a color swap on its box for visible state.
        private static readonly Color CHK_ON = new Color(0.60f, 0.44f, 0.16f, 0.95f);
        private static readonly Color CHK_OFF = new Color(0.22f, 0.15f, 0.07f, 1f);   // opaque
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
            // Core's kit ON/OFF switch (KitControls via KitControlsAdapter) replaces the box when
            // Core has it; the Toggle stays the value holder, so SetToggle / GetToggle and every save path are unchanged
            if (t.GetComponent<KitSwapped>() != null || KitControlsAdapter.Kitize(t)) return;
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
            else { s_unhooked++; Debug.LogWarning("[NpcBookController] button not found to hook: " + name); }
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
            global::FiresRPGmaker.FiresLogger.LogDebug("[NpcBookController] " + m);
        }
    }
}
