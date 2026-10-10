using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FiresCore.UI;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Admin faction editor — an IMGUI window in the shared FiresRoundedSkin chrome (the F8 config / FDT
    /// inspector look). LEFT: the faction list + New/Reload. RIGHT: the selected faction's fields — Name,
    /// Icon (with a live preview swatch), Buff, a same-faction-damage toggle, bonus-skill rows, and the five
    /// restriction lists behind sub-tabs. Save writes the MP-shape YAML to Factions/&lt;id&gt;.yml (synced +
    /// reloaded). Edits happen on a discardable clone so nothing goes live until Save. Client-only.
    /// </summary>
    public class FactionEditorPanel : MonoBehaviour
    {
        private const int WindowId = 0xFAC7;
        private const float WindowAlpha = 0.94f;
        private static readonly Vector2 Size = new Vector2(720f, 600f);

        private static FactionEditorPanel _instance;
        public static bool IsOpen => _instance != null && _instance._open;

        public static void Toggle()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return; // client-only (no OnGUI on a dedi)
            EnsureHost();
            if (_instance._open) _instance.Close(); else _instance.OpenInternal(null);
        }

        /// <summary>Closes the window when open (GUIManager.CloseAllPanels: Esc hook + closenpcui).</summary>
        public static void CloseIfOpen()
        {
            if (IsOpen) _instance.Close();
        }

        public static void Open(string factionId)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EnsureHost();
            _instance.OpenInternal(factionId);
        }

        private static void EnsureHost()
        {
            if (_instance != null) return;
            var go = new GameObject("FiresFactionEditorHost");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<FactionEditorPanel>();
        }

        private bool _open;
        private Rect _rect = new Rect(-1f, -1f, 0f, 0f);
        private Vector2 _navScroll, _bodyScroll;
        private float _scale;      // whole-window (text + elements) scale via GUI.matrix; 0 = pick a default
        private bool _scaling;     // dragging the bottom-right resize grip
        private readonly object _textToken = new object();

        private FactionData _working;      // discardable clone of the selected faction
        private string _selectedId;
        private bool _isNew;
        private int _restrictTab;          // 0 Use, 1 Pickup, 2 Build, 3 Craft, 4 Friendly
        private string _addItem = "";
        private string _newId = "";
        private string _addSkillAmount = "10";
        private Skills.SkillType _addSkill = Skills.SkillType.Swords;
        private string _status = "";

        private static readonly string[] RestrictTabs = { "Use", "Pickup", "Build", "Craft", "Friendly" };

        private void Update()
        {
            if (!_open) return;
            // Escape is owned by the central Menu.Show hook (GUIManager.CloseAllPanels): self-closing here ran a
            // frame early and let vanilla's pause menu through (round 21).
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // Gate game hotkeys while any IMGUI text control is focused, so typing doesn't leak to the game.
            if (GUIUtility.keyboardControl != 0) FiresCore.Input.FiresInputBlock.Acquire(_textToken);
            else FiresCore.Input.FiresInputBlock.Release(_textToken);
        }

        private void OpenInternal(string factionId)
        {
            _open = true;
            InputBlock.Block(true);
            LoadFor(factionId ?? FirstId());
            FiresCore.Logging.FiresLogger.LogDebug($"[faction] editor opened ({(_selectedId ?? "no faction")})");
        }

        private void Close()
        {
            _open = false;
            InputBlock.Block(false);
            FiresCore.Input.FiresInputBlock.Release(_textToken);
        }

        private static string FirstId()
        {
            foreach (var id in FactionManager.Ids) return id;
            return null;
        }

        private void LoadFor(string id)
        {
            _isNew = false; _addItem = ""; _status = "";
            if (string.IsNullOrEmpty(id)) { _working = null; _selectedId = null; return; }
            var f = FactionManager.Get(id);
            _working = f?.Clone();
            _selectedId = id;
        }

        private void NewFaction()
        {
            _isNew = true;
            _working = new FactionData { Id = "", Name = "", CanDamageSameFaction = true };
            _selectedId = null; _newId = ""; _addItem = "";
            _status = "Enter an id + name, then Save.";
        }

        private void OnGUI()
        {
            if (!_open) return;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            if (_scale <= 0f) _scale = Mathf.Clamp(Screen.height / 900f, 1f, 2.2f); // readable default per resolution

            // Scale the WHOLE window (text + elements) by _scale, overriding any matrix Valheim left set — the
            // bottom-right grip adjusts it. Everything below runs in LOGICAL (pre-scale) coordinates.
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));
            try
            {
                FiresRoundedSkin.Ensure(WindowAlpha);
                float logW = Screen.width / _scale, logH = Screen.height / _scale;
                if (_rect.x < 0f)
                    _rect = new Rect((logW - Size.x) / 2f, Mathf.Max(20f, (logH - Size.y) / 2f), Size.x, Size.y);
                _rect.width = Size.x; _rect.height = Size.y;
                _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "", FiresRoundedSkin.Window);
                _rect.x = Mathf.Clamp(_rect.x, 0f, logW - 80f);
                _rect.y = Mathf.Clamp(_rect.y, 0f, logH - 40f);
                FiresRoundedSkin.DrawPendingTooltip();
            }
            finally { GUI.matrix = matrix; }
        }

        private void DrawWindow(int id)
        {
            FiresRoundedSkin.ResetTooltip();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Faction editor", FiresRoundedSkin.Title);
            GUILayout.FlexibleSpace();
            bool close = GUILayout.Button("<color=#FFB0B0><b>X</b></color>", FiresRoundedSkin.ButtonSmall, GUILayout.Width(28f));
            GUILayout.EndHorizontal();
            if (close) { Close(); return; }
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            DrawNav();
            GUILayout.Space(8f);
            DrawBody();
            GUILayout.EndHorizontal();

            HandleScaleGrip();
            GUI.DragWindow(new Rect(0f, 0f, _rect.width - 34f, 26f));
        }

        // Bottom-right grip: drag to scale the whole window (text + elements), same idea as the FDT/config window.
        private void HandleScaleGrip()
        {
            var grip = new Rect(_rect.width - 20f, _rect.height - 20f, 18f, 18f);
            GUI.Label(grip, "//", FiresRoundedSkin.Hint);
            var e = Event.current;
            if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition)) { _scaling = true; e.Use(); }
            else if (_scaling && e.type == EventType.MouseDrag)
            {
                _scale = Mathf.Clamp(_scale + (e.delta.x + e.delta.y) * 0.0025f, 0.7f, 2.5f);
                e.Use();
            }
            else if (_scaling && e.type == EventType.MouseUp) { _scaling = false; e.Use(); }
        }

        private void DrawNav()
        {
            GUILayout.BeginVertical(GUILayout.Width(178f));
            GUILayout.Label("Factions", FiresRoundedSkin.Hint);
            _navScroll = GUILayout.BeginScrollView(_navScroll, GUILayout.Height(_rect.height - 158f));
            foreach (var fid in new List<string>(FactionManager.Ids))
            {
                var f = FactionManager.Get(fid);
                string label = f != null && !string.IsNullOrEmpty(f.Name) ? f.Name : fid;
                bool sel = !_isNew && fid == _selectedId;
                if (GUILayout.Button(label, sel ? FiresRoundedSkin.NavSel : FiresRoundedSkin.NavItem)) LoadFor(fid);
            }
            GUILayout.EndScrollView();
            GUILayout.Space(4f);
            if (GUILayout.Button("+ New faction", FiresRoundedSkin.Button)) NewFaction();
            FiresRoundedSkin.MarkHint("Create a blank faction — give it an id + name, then Save.");
            if (GUILayout.Button("Reload from disk", FiresRoundedSkin.ButtonSmall))
            { FactionManager.Reload(); FactionIcons.ClearCache(); LoadFor(_selectedId); }
            FiresRoundedSkin.MarkHint("Re-read all faction files from disk, discarding unsaved edits.");
            GUILayout.EndVertical();
        }

        private void DrawBody()
        {
            GUILayout.BeginVertical();
            if (_working == null)
            {
                GUILayout.Label("No factions yet. Click + New faction to make one.", FiresRoundedSkin.Desc);
                GUILayout.EndVertical();
                return;
            }

            _bodyScroll = GUILayout.BeginScrollView(_bodyScroll, GUILayout.Height(_rect.height - 132f));

            if (_isNew)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Id", FiresRoundedSkin.Label, GUILayout.Width(90f));
                _newId = GUILayout.TextField(_newId ?? "", FiresRoundedSkin.TextInput);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label($"<b>{_working.Name}</b>   <color=#CCB88F><size=10>id: {_working.Id}</size></color>", FiresRoundedSkin.Label);
            }

            Field("Name", ref _working.Name, "Display name shown on nameplates and menus. The id (map key) is what SetFaction/HasFaction use.");
            IconRow();
            Field("Buff", ref _working.Buff, "Status-effect name applied to members while in this faction (e.g. Rested). Blank = none.");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Same-faction", FiresRoundedSkin.Label, GUILayout.Width(90f));
            string dmg = _working.CanDamageSameFaction ? "Can damage: On" : "Can damage: Off";
            if (GUILayout.Button(dmg, FiresRoundedSkin.Button, GUILayout.Width(132f))) _working.CanDamageSameFaction = !_working.CanDamageSameFaction;
            GUILayout.Label("<size=10>off = members can't hurt each other</size>", FiresRoundedSkin.Hint);
            GUILayout.EndHorizontal();
            FiresRoundedSkin.MarkHint("When Off, members of this faction can't damage each other (friendly fire disabled).");

            GUILayout.Space(4f);
            GUILayout.Label("Perks — bonus skills", FiresRoundedSkin.SectionBar);
            FiresRoundedSkin.MarkHint("Flat bonus added to each listed skill's level while a member of this faction.");
            DrawSkills();

            GUILayout.Space(4f);
            GUILayout.Label("Restrictions", FiresRoundedSkin.SectionBar);
            FiresRoundedSkin.MarkHint("Item/piece rules per list. Use/Build/Craft are whitelists (members only); Pickup is a blacklist.");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < RestrictTabs.Length; i++)
                if (GUILayout.Button(RestrictTabs[i], i == _restrictTab ? FiresRoundedSkin.NavSel : FiresRoundedSkin.NavItem))
                { _restrictTab = i; _addItem = ""; }
            GUILayout.EndHorizontal();
            GUILayout.Label("<size=10>" + RestrictHint(_restrictTab) + "</size>", FiresRoundedSkin.Hint);
            DrawList(CurrentList());

            GUILayout.EndScrollView();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", FiresRoundedSkin.Button, GUILayout.Width(90f))) DoSave();
            FiresRoundedSkin.MarkHint("Write this faction to Factions/<id>.yml and sync it to all clients.");
            if (!_isNew)
            {
                bool del = GUILayout.Button("<color=#E8A0A0>Delete</color>", FiresRoundedSkin.Button, GUILayout.Width(80f));
                FiresRoundedSkin.MarkHint("Remove this faction's file and drop it from all clients.");
                if (del) DoDelete();
            }
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(_status, FiresRoundedSkin.Hint);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void Field(string label, ref string val, string hint = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, FiresRoundedSkin.Label, GUILayout.Width(90f));
            val = GUILayout.TextField(val ?? "", FiresRoundedSkin.TextInput);
            GUILayout.EndHorizontal();
            FiresRoundedSkin.MarkHint(hint);
        }

        private void IconRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Icon", FiresRoundedSkin.Label, GUILayout.Width(90f));
            _working.Icon = GUILayout.TextField(_working.Icon ?? "", FiresRoundedSkin.TextInput);
            var rect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
            var sprite = FactionIcons.Resolve(_working.Icon);
            if (sprite != null && sprite.texture != null)
                GUI.DrawTextureWithTexCoords(rect, sprite.texture, NormalizedRect(sprite));
            GUILayout.EndHorizontal();
            FiresRoundedSkin.MarkHint("An item prefab name whose inventory icon becomes the faction crest on nameplates (e.g. SwordIron). Blank = no icon.");
        }

        private static Rect NormalizedRect(Sprite s)
        {
            var r = s.textureRect; var t = s.texture;
            return new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
        }

        private static readonly Color TrashTint = new Color(0.88f, 0.55f, 0.5f);

        // Small square icon button drawing a vanilla sprite (atlas-correct via NormalizedRect), tinted to the
        // theme; falls back to `fallback` text if the sprite isn't loaded. Also feeds the hover tooltip.
        private bool IconButton(string spriteName, string fallback, Color tint, float size, string hint)
        {
            var sprite = FactionIcons.Ui(spriteName);
            bool clicked;
            if (sprite != null && sprite.texture != null)
            {
                clicked = GUILayout.Button(GUIContent.none, FiresRoundedSkin.ButtonSmall, GUILayout.Width(size), GUILayout.Height(size));
                if (Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    const float pad = 3f;
                    var prev = GUI.color; GUI.color = tint;
                    GUI.DrawTextureWithTexCoords(new Rect(r.x + pad, r.y + pad, r.width - pad * 2f, r.height - pad * 2f), sprite.texture, NormalizedRect(sprite));
                    GUI.color = prev;
                }
            }
            else clicked = GUILayout.Button(fallback, FiresRoundedSkin.ButtonSmall, GUILayout.Width(size));
            FiresRoundedSkin.MarkHint(hint);
            return clicked;
        }

        private void DrawSkills()
        {
            Skills.SkillType remove = (Skills.SkillType)int.MinValue;
            foreach (var kv in _working.BonusSkills)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kv.Key.ToString(), FiresRoundedSkin.Field, GUILayout.Width(170f));
                GUILayout.Label("+" + kv.Value, FiresRoundedSkin.ValueText, GUILayout.Width(50f));
                if (IconButton("trash_icon", "x", TrashTint, 24f, "Remove this skill bonus")) remove = kv.Key;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (remove != (Skills.SkillType)int.MinValue) _working.BonusSkills.Remove(remove);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_addSkill.ToString(), FiresRoundedSkin.Button, GUILayout.Width(170f))) _addSkill = NextSkill(_addSkill);
            FiresRoundedSkin.MarkHint("Click to cycle the skill, enter a +/- amount, then Add.");
            _addSkillAmount = GUILayout.TextField(_addSkillAmount ?? "", FiresRoundedSkin.TextInput, GUILayout.Width(50f));
            if (GUILayout.Button("Add", FiresRoundedSkin.ButtonSmall, GUILayout.Width(48f))
                && int.TryParse(_addSkillAmount, out int amt) && amt != 0)
                _working.BonusSkills[_addSkill] = amt;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawList(List<string> list)
        {
            if (list == null) return;
            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(list[i], FiresRoundedSkin.Field);
                if (IconButton("trash_icon", "x", TrashTint, 24f, "Remove from this list")) removeAt = i;
                GUILayout.EndHorizontal();
            }
            if (removeAt >= 0) list.RemoveAt(removeAt);

            GUILayout.BeginHorizontal();
            _addItem = GUILayout.TextField(_addItem ?? "", FiresRoundedSkin.TextInput);
            if (GUILayout.Button("Add", FiresRoundedSkin.ButtonSmall, GUILayout.Width(48f)))
            {
                string v = (_addItem ?? "").Trim();
                if (v.Length > 0 && !list.Contains(v)) { list.Add(v); _addItem = ""; }
            }
            GUILayout.EndHorizontal();
            FiresRoundedSkin.MarkHint("Type an item / piece / monster prefab name and Add it to this list.");
        }

        private List<string> CurrentList()
        {
            switch (_restrictTab)
            {
                case 1: return _working.PickupNames;
                case 2: return _working.BuildNames;
                case 3: return _working.CraftNames;
                case 4: return _working.FriendlyNames;
                default: return _working.UseNames;
            }
        }

        private static string RestrictHint(int tab)
        {
            switch (tab)
            {
                case 1: return "Members of this faction cannot pick these items up.";
                case 2: return "Only members may build these pieces.";
                case 3: return "Only members may craft these items.";
                case 4: return "These monsters won't aggro this faction (mutual).";
                default: return "Only members of a faction that lists an item may equip/use it.";
            }
        }

        private void DoSave()
        {
            if (_isNew)
            {
                string id = (_newId ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(id)) { _status = "Enter an id first."; return; }
                _working.Id = id;
            }
            if (string.IsNullOrWhiteSpace(_working.Name)) _working.Name = _working.Id;
            string savedId = _working.Id;
            FactionManager.SaveFaction(_working);
            FactionIcons.ClearCache();
            LoadFor(savedId);
            _status = $"Saved to Factions/{savedId}.yml";
        }

        private void DoDelete()
        {
            if (_working == null || string.IsNullOrEmpty(_working.Id)) return;
            string id = _working.Id;
            FactionManager.DeleteFaction(id);
            LoadFor(FirstId());
            _status = $"Deleted {id}.";
        }

        private static Skills.SkillType[] _skillValues;
        private static Skills.SkillType NextSkill(Skills.SkillType cur)
        {
            if (_skillValues == null)
            {
                var list = new List<Skills.SkillType>();
                foreach (Skills.SkillType v in Enum.GetValues(typeof(Skills.SkillType)))
                    if (v != Skills.SkillType.None && v != Skills.SkillType.All) list.Add(v);
                _skillValues = list.ToArray();
            }
            int i = Array.IndexOf(_skillValues, cur);
            return _skillValues[(i + 1) % _skillValues.Length];
        }
    }
}
