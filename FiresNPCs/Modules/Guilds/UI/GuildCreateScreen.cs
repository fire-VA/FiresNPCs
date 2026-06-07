using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.UI;

namespace FiresRPGmaker.Modules.Guilds.UI
{
    /// <summary>
    /// Compact centered modal for creating a new guild.
    /// Validates name (non-empty, 3-32 chars) before allowing submission.
    /// </summary>
    public class GuildCreateScreen : MonoBehaviour
    {
        public event Action OnClose;
        public event Action OnCreated;

        // Match the info-UI panel opacity for visual consistency.
        private static readonly Color BgColor    = new Color(0.04f, 0.04f, 0.06f, 0.85f);
        private static readonly Color BorderColor = new Color(0.35f, 0.27f, 0.14f, 1f);
        private static readonly Color GoldAccent  = new Color(0.83f, 0.64f, 0.31f, 1f);
        private static readonly Color TextNormal  = new Color(0.85f, 0.80f, 0.65f, 1f);
        private static readonly Color TextMuted   = new Color(0.55f, 0.50f, 0.40f, 1f);
        private static readonly Color TextDanger  = new Color(0.85f, 0.30f, 0.25f, 1f);
        private static readonly Color BtnNormal   = new Color(0.22f, 0.17f, 0.10f, 1f);
        private static readonly Color BtnDanger   = new Color(0.40f, 0.12f, 0.10f, 1f);

        private const float W       = 420f;
        private const float H       = 240f;
        private const float PAD     = 14f;
        private const float FONT_SM = 11f;
        private const float FONT_MD = 13f;
        private const float FONT_LG = 16f;
        private const float BTN_H   = 30f;
        private const float BTN_W   = 110f;

        private GameObject     _canvasRoot;
        private GameObject     _root;
        private bool           _built;
        private bool           _isDragging;
        private Vector2        _dragOffset;
        private TMP_InputField _nameInput;
        private TMP_InputField _descInput;
        private TextMeshProUGUI _errorTxt;
        private Toggle         _openToggle;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public void Show()
        {
            if (!_built) Build();
            if (_canvasRoot != null) _canvasRoot.SetActive(true);
            _root.SetActive(true);
            _nameInput.text = "";
            _descInput.text = "";
            if (_errorTxt != null) _errorTxt.text = "";
            GuildAPI.OnGuildsUpdated += OnGuildsUpdated;

            GUIManager.BlockInput(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            // Diagnostic — confirms BlockInput / cursor / EventSystem state when
            // Show finishes. Remove once root cause is confirmed fixed.
            Debug.Log($"[GuildCreate] Show() — InputBlocked={GUIManager.IsInputBlocked} " +
                      $"Cursor.visible={Cursor.visible} Cursor.lockState={Cursor.lockState} " +
                      $"EventSystem={(EventSystem.current != null ? EventSystem.current.name : "NULL")}");
        }

        public void Hide()
        {
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;
            _isDragging = false;
            GUIManager.BlockInput(false);
        }

        private void OnDestroy() => GuildAPI.OnGuildsUpdated -= OnGuildsUpdated;

        private void OnGuildsUpdated(System.Collections.Generic.IReadOnlyList<Core.GuildData> guilds)
        {
            var own = GuildAPI.GetOwnGuild();
            Debug.Log($"[GuildCreate] OnGuildsUpdated — total guilds={guilds?.Count ?? 0}, ownGuild={(own == null ? "null" : own.Name)}");
            // If we now belong to a guild, creation succeeded.
            if (own != null) { Hide(); OnCreated?.Invoke(); }
        }

        private void Update()
        {
            if (_root == null || !_root.activeInHierarchy) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { OnClose?.Invoke(); return; }
            HandleDrag();
        }

        private void HandleDrag()
        {
            if (_root == null) return;
            var rootRT = _root.GetComponent<RectTransform>();
            if (rootRT == null) return;
            Vector2 mousePos = Input.mousePosition;
            if (Input.GetMouseButtonDown(0) && !_isDragging)
            {
                // Only start a drag if the click is in the title-bar strip AND
                // not on an interactive child. Otherwise this poll-based drag
                // would steal clicks from buttons/inputs/toggles.
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRT, mousePos, null, out var local)
                    && local.y > H / 2f - 40f
                    && RectTransformUtility.RectangleContainsScreenPoint(rootRT, mousePos, null)
                    && !PointerOverInteractive())
                {
                    _isDragging = true;
                    _dragOffset = (Vector2)rootRT.position - mousePos;
                }
            }
            if (_isDragging && Input.GetMouseButton(0))
                rootRT.position = mousePos + _dragOffset;
            if (_isDragging && Input.GetMouseButtonUp(0))
                _isDragging = false;
        }

        private static bool PointerOverInteractive()
        {
            var es = EventSystem.current;
            if (es == null) return false;
            var ped = new PointerEventData(es) { position = Input.mousePosition };
            var results = new List<RaycastResult>(8);
            es.RaycastAll(ped, results);
            for (int i = 0; i < results.Count; i++)
            {
                var t = results[i].gameObject;
                if (t == null) continue;
                if (t.GetComponentInParent<Selectable>() != null) return true;
            }
            return false;
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build()
        {
            // DEDICATED CANVAS — sharing Hud.instance's Canvas caused a Canvas
            // Rebuild Storm (every guild layout change dirtied the entire HUD
            // canvas, tanking FPS). Cursor works because GuildScreenManager is
            // registered in GUIManager.IsCustomPanelOpen() — see GuildMainScreen
            // for the same pattern.
            _canvasRoot = new GameObject("CreatePanelCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasRoot.transform.SetParent(transform, false);

            var canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 25;

            var scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var canvasRect = _canvasRoot.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            // The actual centred panel — child of the canvas root. Subsequent
            // child elements (title, inputs, buttons) attach to _root.
            _root = new GameObject("CreatePanel", typeof(RectTransform));
            _root.transform.SetParent(_canvasRoot.transform, false);
            var bg = _root.AddComponent<Image>();
            var rt = _root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(W, H);
            rt.anchoredPosition = Vector2.zero;

            var bgSprite = CodegenUIWiring.ResolveSpriteByNamePublic("crafting_panel_bkg");
            if (bgSprite != null)
            {
                bg.sprite = bgSprite;
                bg.type = Image.Type.Sliced;
                bg.color = new Color(1f, 1f, 1f, 0.75f);
            }
            else
            {
                bg.color = UIFontConfig.Colors.PanelBackground;
            }

            // Border
            AddBorder(_root, BorderColor);

            float y = -PAD;

            // Title (decorative Norse font)
            var title = AddText(_root.transform, "Found a Guild", FONT_LG, TextNormal, FontStyles.Bold);
            UIBuilderHelper.ApplyValheimFont(title);
            Anchor(title.GetComponent<RectTransform>(), PAD, y, W - PAD * 2, 22f);
            y -= 26f;

            // Name label + input
            AddText(_root.transform, "Guild Name (3–32 characters)", FONT_SM, TextMuted, FontStyles.Normal,
                PAD, y, W - PAD * 2, 16f);
            y -= 18f;
            _nameInput = AddInput(_root.transform, "Enter guild name...", W - PAD * 2, 28f, PAD, y);
            y -= 32f;

            // Description label + input
            AddText(_root.transform, "Description (optional)", FONT_SM, TextMuted, FontStyles.Normal,
                PAD, y, W - PAD * 2, 16f);
            y -= 18f;
            _descInput = AddInput(_root.transform, "Describe your guild...", W - PAD * 2, 28f, PAD, y);
            y -= 32f;

            // Open toggle
            _openToggle = AddToggle(_root.transform, "Open recruitment (anyone can apply)", PAD, y);
            y -= 30f;

            // Error text
            _errorTxt = AddText(_root.transform, "", FONT_SM, TextDanger, FontStyles.Normal,
                PAD, y, W - PAD * 2, 16f);
            y -= 24f;

            // Buttons
            var createBtn = AddButton(_root.transform, "Create Guild", BTN_W * 1.2f, BTN_H, BtnNormal, GoldAccent, PAD, y);
            createBtn.onClick.AddListener(TryCreate);

            var cancelBtn = AddButton(_root.transform, "Cancel", BTN_W, BTN_H, BtnDanger, TextDanger, PAD * 2 + BTN_W * 1.2f, y);
            cancelBtn.onClick.AddListener(() => OnClose?.Invoke());

            _built = true;
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void TryCreate()
        {
            var rawName = _nameInput?.text ?? "<null>";
            var rawDesc = _descInput?.text ?? "<null>";
            Debug.Log($"[GuildCreate] TryCreate fired — rawName='{rawName}' (len {rawName.Length}), rawDesc.len={rawDesc.Length}");

            var name = rawName.Trim();
            if (name.Length < 3)
            {
                _errorTxt.text = "Name must be at least 3 characters.";
                Debug.Log($"[GuildCreate] Rejected: name too short after trim ('{name}', len {name.Length})");
                return;
            }
            if (name.Length > 32)
            {
                _errorTxt.text = "Name must be 32 characters or less.";
                Debug.Log($"[GuildCreate] Rejected: name too long ({name.Length})");
                return;
            }

            var existing = GuildAPI.GetOwnGuild();
            if (existing != null)
            {
                _errorTxt.text = "You are already in a guild.";
                Debug.Log($"[GuildCreate] Rejected: already in guild '{existing.Name}' (id={existing.GuildId})");
                return;
            }

            _errorTxt.text = "";
            var desc = (_descInput?.text ?? "").Trim();
            Debug.Log($"[GuildCreate] Validation OK — calling GuildAPI.CreateGuild(name='{name}', desc.len={desc.Length})");
            GuildAPI.CreateGuild(name, desc);
            Debug.Log("[GuildCreate] GuildAPI.CreateGuild returned (RPC sent — waiting for OnGuildsUpdated)");
            // UI will refresh via OnGuildsUpdated when server confirms.
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private static TextMeshProUGUI AddText(Transform parent, string text, float size, Color color,
            FontStyles style, float x = 0, float y = 0, float w = 100, float h = 20)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
            UIBuilderHelper.ApplyValheimFont(tmp);
            Anchor(tmp.GetComponent<RectTransform>(), x, y, w, h);
            return tmp;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text, float size, Color color, FontStyles style)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text; tmp.fontSize = size; tmp.color = color; tmp.fontStyle = style;
            UIBuilderHelper.ApplyValheimFont(tmp);
            go.AddComponent<RectTransform>();
            return tmp;
        }

        private static TMP_InputField AddInput(Transform parent, string ph, float w, float h, float x, float y)
        {
            var go = new GameObject("Input");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>(); bg.color = new Color(0.06f, 0.05f, 0.03f, 1f);
            Anchor(go.GetComponent<RectTransform>(), x, y, w, h);

            var txtGO = new GameObject("Text"); txtGO.transform.SetParent(go.transform, false);
            var txt = txtGO.AddComponent<TextMeshProUGUI>(); txt.fontSize = 12f;
            txt.color = new Color(0.85f, 0.80f, 0.65f, 1f);
            UIBuilderHelper.ApplyValheimFont(txt);
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(6f, 0); trt.offsetMax = new Vector2(-6f, 0);

            var phGO = new GameObject("PH"); phGO.transform.SetParent(go.transform, false);
            var phTxt = phGO.AddComponent<TextMeshProUGUI>();
            phTxt.text = ph; phTxt.fontSize = 12f;
            phTxt.color = new Color(0.45f, 0.40f, 0.35f, 1f);
            phTxt.fontStyle = FontStyles.Italic;
            UIBuilderHelper.ApplyValheimFont(phTxt);
            var phrt = phTxt.GetComponent<RectTransform>();
            phrt.anchorMin = Vector2.zero; phrt.anchorMax = Vector2.one;
            phrt.offsetMin = new Vector2(6f, 0); phrt.offsetMax = new Vector2(-6f, 0);

            var field = go.AddComponent<TMP_InputField>();
            field.textComponent = txt; field.placeholder = phTxt;
            field.textViewport = go.GetComponent<RectTransform>();
            field.targetGraphic = bg;
            return field;
        }

        private static Toggle AddToggle(Transform parent, string label, float x, float y)
        {
            var go = new GameObject("Toggle");
            go.transform.SetParent(parent, false);
            var bg = go.AddComponent<Image>(); bg.color = new Color(0.15f, 0.12f, 0.08f, 1f);
            Anchor(go.GetComponent<RectTransform>(), x, y, 20f, 20f);

            var check = new GameObject("Check"); check.transform.SetParent(go.transform, false);
            var ci = check.AddComponent<Image>(); ci.color = new Color(0.83f, 0.64f, 0.31f, 1f);
            var crt = check.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = new Vector2(3, 3); crt.offsetMax = new Vector2(-3, -3);

            var tog = go.AddComponent<Toggle>();
            tog.targetGraphic = bg; tog.graphic = ci; tog.isOn = false;

            var lbl = new GameObject("Label"); lbl.transform.SetParent(go.transform, false);
            var ltxt = lbl.AddComponent<TextMeshProUGUI>();
            ltxt.text = label;
            ltxt.fontSize = FONT_SM;
            ltxt.color = TextMuted;
            ltxt.alignment = TextAlignmentOptions.MidlineLeft;
            ltxt.textWrappingMode = TextWrappingModes.NoWrap;
            ltxt.overflowMode = TextOverflowModes.Ellipsis;
            UIBuilderHelper.ApplyValheimFont(ltxt);

            // Anchor to the right edge of the toggle, left-pivot so the
            // text grows away from the box instead of through it.
            var lrt = lbl.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(1, 0.5f);
            lrt.anchorMax = new Vector2(1, 0.5f);
            lrt.pivot = new Vector2(0, 0.5f);
            lrt.anchoredPosition = new Vector2(8f, 0f);
            lrt.sizeDelta = new Vector2(W - PAD * 2 - 28f, 20f);

            return tog;
        }

        private static Button AddButton(Transform parent, string label, float w, float h,
            Color bgColor, Color textColor, float x, float y)
        {
            var go = new GameObject("Btn");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = bgColor;
            Anchor(go.GetComponent<RectTransform>(), x, y, w, h);

            var lbl = new GameObject("Lbl"); lbl.transform.SetParent(go.transform, false);
            var txt = lbl.AddComponent<TextMeshProUGUI>();
            txt.text = label; txt.fontSize = 12f; txt.color = textColor;
            txt.alignment = TextAlignmentOptions.Center;
            UIBuilderHelper.ApplyValheimFont(txt);
            var lrt = txt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>(); btn.targetGraphic = img;
            return btn;
        }

        private static void AddBorder(GameObject target, Color color)
        {
            var b = new GameObject("Border"); b.transform.SetParent(target.transform, false);
            var img = b.AddComponent<Image>(); img.color = color;
            var rt = b.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-1, -1); rt.offsetMax = new Vector2(1, 1);
            b.transform.SetAsFirstSibling();
        }

        private static void Anchor(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot     = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
