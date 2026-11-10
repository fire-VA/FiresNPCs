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
    ///
    /// An "Exact replay (copies everything you do)" switch on this widget (Core's kit pill switch, default OFF). OFF is
    /// the path recording exactly as before. ON also records the replay timeline (PatrolReplayCapture: 15 samples/s plus the attacks,
    /// interactions, emotes, jumps and weapon changes) and saves it inside the same route file (PatrolReplayStore); the NPC then plays
    /// it back as a ghost (NpcReplayPlayer). Flipping the switch restarts the recording so the route and the timeline start together.
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

        // the Exact replay switch (default OFF = the path-only recording, unchanged).
        private bool _exact;
        private GameObject _exactRow;
        private object _exactHandle;              // Core KitControls toggle handle (through KitControlsAdapter)
        private Button _exactFallbackBtn;         // only when Core's kit switch is missing
        internal static bool ExactMode => Instance != null && Instance._exact;   // tests read it
        internal static bool IsRecording => Instance != null && Instance._recording;
        /// <summary>Tests: flips the switch the way a click does (restart + capture), and redraws it.</summary>
        internal void SetExactForTest(bool on) { if (on != _exact) OnExactToggled(on); KitControlsAdapter.Set(_exactHandle, on); }

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
            _exact = false;                       // every recording starts path-only; the switch turns exact on
            PatrolReplayCapture.Discard();

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
                    if (_statusText != null && !_exact) _statusText.text = $"Points: {_points.Count}";
                }
            }

            // exact mode takes a timeline sample whenever one is due and shows what has been captured so far.
            if (_exact && !IsNaming && player != null)
            {
                PatrolReplayCapture.Tick(player);
                if (_statusText != null && PatrolReplayCapture.Timeline != null && Time.frameCount % 10 == 0)
                    _statusText.text = PatrolReplayCapture.Timeline.Summary() + $"  ·  {_points.Count} points";
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

            // Parchment panel low-centre (below the player), non-blocking. One row taller for the Exact replay switch.
            _widgetGo = UIBuilderHelper.CreatePanel(_canvasGo.transform, "PatrolWidget",
                new Vector2(0.35f, 0.05f), new Vector2(0.65f, 0.245f), UIFontConfig.Colors.ParchmentPanel);
            var outline = _widgetGo.AddComponent<Outline>();
            outline.effectColor = UIFontConfig.Colors.ParchmentOutline;
            outline.effectDistance = new Vector2(2f, -2f);

            _titleText = UIBuilderHelper.CreateLabel(_widgetGo.transform, "Title", "Patrol Route — hold L.Alt to click", 14,
                UIFontConfig.Colors.ParchmentInk, new Vector2(0.03f, 0.78f), new Vector2(0.97f, 1f),
                TextAlignmentOptions.Center);

            BuildExactRow();

            _statusText = UIBuilderHelper.CreateLabel(_widgetGo.transform, "Status", "Points: 0", 13,
                UIFontConfig.Colors.ParchmentLabel, new Vector2(0.03f, 0.33f), new Vector2(0.97f, 0.56f),
                TextAlignmentOptions.Center);

            _previewBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Preview",
                new Vector2(0.04f, 0.05f), new Vector2(0.34f, 0.31f), OnPreview).gameObject;
            _saveBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Save",
                new Vector2(0.36f, 0.05f), new Vector2(0.64f, 0.31f), OnSave).gameObject;
            _cancelBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Cancel",
                new Vector2(0.66f, 0.05f), new Vector2(0.96f, 0.31f), OnCancel).gameObject;

            // Name-entry controls (hidden until Save).
            _nameInput = UIBuilderHelper.CreateInputField(_widgetGo.transform, "NameInput", "Route name…",
                new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.56f));
            _confirmBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Confirm",
                new Vector2(0.08f, 0.05f), new Vector2(0.48f, 0.31f), OnConfirmName).gameObject;
            _backBtn = UIBuilderHelper.CreateParchmentButton(_widgetGo.transform, "Back",
                new Vector2(0.52f, 0.05f), new Vector2(0.92f, 0.31f), OnBackToRecording).gameObject;
        }

        // "Exact replay (copies everything you do)" + Core's ONE switch (the config window's pill), right-aligned in its row.
        private void BuildExactRow()
        {
            _exactRow = new GameObject("ExactReplayRow", typeof(RectTransform));
            _exactRow.transform.SetParent(_widgetGo.transform, false);
            var row = (RectTransform)_exactRow.transform;
            row.anchorMin = new Vector2(0.04f, 0.57f);
            row.anchorMax = new Vector2(0.96f, 0.77f);
            row.offsetMin = row.offsetMax = Vector2.zero;

            UIBuilderHelper.CreateLabel(_exactRow.transform, "Label", "Exact replay (copies everything you do)", 13,
                UIFontConfig.Colors.ParchmentInk, new Vector2(0f, 0f), new Vector2(0.70f, 1f), TextAlignmentOptions.Left);

            _exactHandle = KitControlsAdapter.Toggle(row, "PatrolExactReplay", _exact, 0f, 0f, OnExactToggled, 26f, rightAlign: true);
            if (_exactHandle == null)
            {
                // A Core without the kit switch: a button that says the state, so the option is still there.
                _exactFallbackBtn = UIBuilderHelper.CreateParchmentButton(_exactRow.transform, "Exact: OFF",
                    new Vector2(0.72f, 0.05f), new Vector2(1f, 0.95f), () => OnExactToggled(!_exact));
            }
        }

        private void OnExactToggled(bool on)
        {
            _exact = on;
            if (_exactFallbackBtn != null)
            {
                var t = _exactFallbackBtn.GetComponentInChildren<TMP_Text>();
                if (t != null) t.text = on ? "Exact: ON" : "Exact: OFF";
            }

            // Restart, so the path and the timeline begin at the same moment.
            _points.Clear();
            _nextSampleTime = 0f;
            UpdateLine();
            if (on) PatrolReplayCapture.Start(); else PatrolReplayCapture.Discard();
            if (_statusText != null) _statusText.text = on ? "0.0 s, 0 attacks, 0 interactions, 0 emotes" : "Points: 0";
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, on
                ? "Exact replay ON: recording restarted. Moves, attacks, doors and emotes are copied from now."
                : "Exact replay OFF: recording restarted (path only).");
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Replay] recorder exact replay {(on ? "ON" : "OFF")} for '{(_npc != null ? _npc.GetDisplayName() : "?")}'");
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
            if (_exactRow != null) _exactRow.SetActive(!naming);
            PatrolReplayCapture.SetPaused(naming);   // the naming time is not part of the replay
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

        private bool ExactReady => _exact && PatrolReplayCapture.Timeline != null && PatrolReplayCapture.Timeline.Samples.Count >= 2;

        internal void OnSave()   // internal for the replay test (the same call the Save button makes)
        {
            if (_points.Count < 2 && !ExactReady)
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

        /// <summary>tests: types the name into the field (as a player would) and confirms.</summary>
        internal void ConfirmNameForTest(string name)
        {
            if (_nameInput != null) _nameInput.text = name;
            OnConfirmName();
        }

        private void OnConfirmName()
        {
            string name = _nameInput != null ? _nameInput.text?.Trim() : null;
            if (string.IsNullOrEmpty(name))
                name = "Route_" + (_npc != null ? _npc.GetDisplayName() : "Npc");

            if (ExactReady)
            {
                // the exact replay (path points + timeline in one route file), listed and assigned like any route.
                PatrolReplayCapture.Stop();
                var tl = PatrolReplayCapture.Timeline;
                PatrolReplayStore.SaveAndAssign(name, _points, tl, _npc);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Saved exact replay '{name}' ({tl.Summary()})");
                Finish();
                return;
            }

            PatrolRouteSync.SaveAndAssign(name, _points, _npc);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Saved patrol route '{name}'");
            Finish();
        }

        private void OnBackToRecording()
        {
            SetMode(naming: false);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Keep walking — Save when ready");
        }

        internal void OnCancel()
        {
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Patrol recording cancelled");
            Finish();
        }

        private void Finish()
        {
            _recording = false;
            IsNaming = false;
            PatrolReplayCapture.Discard();
            _exact = false;
            _exactHandle = null; _exactRow = null; _exactFallbackBtn = null;
            if (_widgetGo != null) Destroy(_canvasGo);
            if (_line != null) Destroy(_line.gameObject);
            _canvasGo = _widgetGo = null;
            _line = null;
            _points.Clear();
        }
    }
}
