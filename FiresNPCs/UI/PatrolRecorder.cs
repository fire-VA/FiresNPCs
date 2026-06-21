using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using FiresCore.UI;
using FiresCore.Npc.Patrol;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Records a patrol route by sampling the local player's position as they walk, previews it as an
    /// in-world line, and on Save prompts for a name before writing it to
    /// BepInEx/config/FiresNPCs_PatrolRoutes/&lt;name&gt;.cfg and assigning it to the NPC being configured.
    /// The widget runs after the book closes (event system free, so onClick works); HOLD LEFT-ALT while
    /// recording to free the cursor and click the buttons. While the name field is open, player input is
    /// suppressed (see PatrolInputBlockPatch) so typing doesn't also move the character.
    /// </summary>
    public class PatrolRecorder : MonoBehaviour
    {
        public static PatrolRecorder Instance { get; private set; }

        /// <summary>True while the name field is open — PatrolInputBlockPatch reads this to suppress movement.</summary>
        public static bool IsNaming { get; private set; }

        private const float SampleInterval = 0.25f;   // seconds between position checks
        private const float MinPointDistance = 1.5f;   // metres before a new point is plotted

        private NpcController _npc;
        private readonly List<Vector3> _points = new List<Vector3>();
        private float _nextSampleTime;
        private bool _recording;

        private GameObject _canvasGo;
        private GameObject _widgetGo;
        private TMP_Text _titleText;
        private TMP_Text _statusText;
        private LineRenderer _line;

        // Recording-mode controls.
        private GameObject _previewBtn, _saveBtn, _cancelBtn;
        // Name-entry-mode controls.
        private TMP_InputField _nameInput;
        private GameObject _confirmBtn, _backBtn;

        public static void Begin(NpcController npc)
        {
            if (npc == null) return;
            if (Instance == null)
            {
                var go = new GameObject("FiresPatrolRecorder");
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<PatrolRecorder>();
            }
            Instance.StartRecording(npc);
        }

        private void StartRecording(NpcController npc)
        {
            _npc = npc;
            _points.Clear();
            _recording = true;
            IsNaming = false;
            _nextSampleTime = 0f;

            BuildWidget();
            BuildLine();
            SetMode(naming: false);

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                "Begin walking the route. Hold L.Alt to click Preview / Save.");
        }

        private void Update()
        {
            if (!_recording) return;

            // Sample the player's path (paused while naming).
            var player = Player.m_localPlayer;
            if (!IsNaming && player != null && Time.time >= _nextSampleTime)
            {
                _nextSampleTime = Time.time + SampleInterval;
                Vector3 p = player.transform.position;
                if (_points.Count == 0 || Vector3.Distance(_points[_points.Count - 1], p) >= MinPointDistance)
                {
                    _points.Add(p);
                    UpdateLine();
                    if (_statusText != null) _statusText.text = $"Points: {_points.Count}";
                }
            }

            // Free the cursor: always while naming, otherwise only while holding Alt.
            bool interact = IsNaming || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (interact)
            {
                if (!Cursor.visible) Cursor.visible = true;
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            }
        }

        // ── widget ──────────────────────────────────────────────────────────
        private void BuildWidget()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject("FiresPatrolRecorderCanvas");
            DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            // Parchment panel low-centre (below the player), non-blocking.
            _widgetGo = UIBuilderHelper.CreatePanel(_canvasGo.transform, "PatrolWidget",
                new Vector2(0.37f, 0.06f), new Vector2(0.63f, 0.205f), UIFontConfig.Colors.ParchmentPanel);
            var outline = _widgetGo.AddComponent<Outline>();
            outline.effectColor = UIFontConfig.Colors.ParchmentOutline;
            outline.effectDistance = new Vector2(2f, -2f);

            _titleText = UIBuilderHelper.CreateLabel(_widgetGo.transform, "Title", "Patrol Route — hold L.Alt to click", 14,
                UIFontConfig.Colors.ParchmentInk, new Vector2(0.03f, 0.70f), new Vector2(0.97f, 1f),
                TextAlignmentOptions.Center);

            _statusText = UIBuilderHelper.CreateLabel(_widgetGo.transform, "Status", "Points: 0", 13,
                UIFontConfig.Colors.ParchmentLabel, new Vector2(0.03f, 0.42f), new Vector2(0.97f, 0.70f),
                TextAlignmentOptions.Center);

            _previewBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Preview",
                new Vector2(0.04f, 0.06f), new Vector2(0.34f, 0.40f), OnPreview).gameObject;
            _saveBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Save",
                new Vector2(0.36f, 0.06f), new Vector2(0.64f, 0.40f), OnSave).gameObject;
            _cancelBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Cancel",
                new Vector2(0.66f, 0.06f), new Vector2(0.96f, 0.40f), OnCancel).gameObject;

            // Name-entry controls (hidden until Save).
            _nameInput = UIBuilderHelper.CreateInputField(_widgetGo.transform, "NameInput", "Route name…",
                new Vector2(0.06f, 0.44f), new Vector2(0.94f, 0.68f));
            _confirmBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Confirm",
                new Vector2(0.08f, 0.06f), new Vector2(0.48f, 0.40f), OnConfirmName).gameObject;
            _backBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Back",
                new Vector2(0.52f, 0.06f), new Vector2(0.92f, 0.40f), OnBackToRecording).gameObject;
        }

        private void SetMode(bool naming)
        {
            IsNaming = naming;
            if (_statusText != null) _statusText.gameObject.SetActive(!naming);
            if (_previewBtn != null) _previewBtn.SetActive(!naming);
            if (_saveBtn != null) _saveBtn.SetActive(!naming);
            if (_cancelBtn != null) _cancelBtn.SetActive(!naming);
            if (_nameInput != null) _nameInput.gameObject.SetActive(naming);
            if (_confirmBtn != null) _confirmBtn.SetActive(naming);
            if (_backBtn != null) _backBtn.SetActive(naming);
            if (_titleText != null)
                _titleText.text = naming ? "Name this route" : "Patrol Route — hold L.Alt to click";
        }

        // ── line preview ────────────────────────────────────────────────────
        private void BuildLine()
        {
            if (_line != null) return;
            var go = new GameObject("FiresPatrolLine");
            DontDestroyOnLoad(go);
            _line = go.AddComponent<LineRenderer>();
            _line.widthMultiplier = 0.25f;
            _line.material = new Material(Shader.Find("Sprites/Default"));
            _line.startColor = _line.endColor = new Color(1f, 0.85f, 0.2f, 0.9f);
            _line.numCornerVertices = 2;
            _line.positionCount = 0;
            _line.useWorldSpace = true;
        }

        private void UpdateLine()
        {
            if (_line == null) return;
            _line.positionCount = _points.Count;
            for (int i = 0; i < _points.Count; i++)
                _line.SetPosition(i, _points[i] + Vector3.up * 0.3f);
        }

        // ── buttons ─────────────────────────────────────────────────────────
        private void OnPreview()
        {
            UpdateLine();
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                $"Route preview — {_points.Count} points");
        }

        private void OnSave()
        {
            if (_points.Count < 2)
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Walk a route first (need 2+ points)");
                return;
            }

            // Prompt for a name. Default to "Route_<npc>" so a quick Enter still works.
            string suggested = "Route_" + (_npc != null ? _npc.GetDisplayName() : "Npc");
            SetMode(naming: true);
            if (_nameInput != null)
            {
                _nameInput.text = suggested;
                EventSystem.current?.SetSelectedGameObject(_nameInput.gameObject);
                _nameInput.ActivateInputField();
                _nameInput.Select();
                _nameInput.caretPosition = suggested.Length;
            }
        }

        private void OnConfirmName()
        {
            string name = _nameInput != null ? _nameInput.text?.Trim() : null;
            if (string.IsNullOrEmpty(name))
                name = "Route_" + (_npc != null ? _npc.GetDisplayName() : "Npc");

            PatrolRouteSync.SaveAndAssign(name, _points, _npc);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Saved patrol route '{name}'");
            Finish();
        }

        private void OnBackToRecording()
        {
            SetMode(naming: false);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Keep walking — Save when ready");
        }

        private void OnCancel()
        {
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Patrol recording cancelled");
            Finish();
        }

        private void Finish()
        {
            _recording = false;
            IsNaming = false;
            if (_widgetGo != null) Destroy(_canvasGo);
            if (_line != null) Destroy(_line.gameObject);
            _canvasGo = _widgetGo = null;
            _line = null;
            _points.Clear();
        }
    }
}
