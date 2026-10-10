using TMPro;
using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// kg-Marketplace-style hover name: a raised world-space TMP label above a static NPC's head,
    /// shown when Companions.StaticNpcs/NameplateStyle = HoverName. Mirrors kg's Market_NPC plate:
    /// local position (0, 3.4 + TextHeight, 0), uniform scale = the NPC's TextSize, and kg's
    /// TextComponent billboard (face the camera's forward, upright). The vanilla EnemyHud plate is
    /// suppressed separately in NpcNameplatePatches while this mode is active. Attached to every
    /// static NPC and self-managing, so admins can flip the config live: it builds lazily on first
    /// need and simply deactivates in the other modes (and on a dedicated server, where there is no
    /// camera).
    /// </summary>
    public class NpcHoverNameplate : MonoBehaviour
    {
        private const float BaseHeight = 2.5f;    // just above the head at TextHeight 0 (owner: "much lower")
        private const float MinHeight = 1.9f;     // kg TextHeights were authored against kg's 3.4 base — never sink into the body
        private const float BaseFontSize = 1.6f;  // world-space TMP size at TextSize 1
        private const float MaxVisibleDistance = 64f;
        private static readonly Color HoverYellow = new Color(1f, 0.867f, 0.333f); // kg-style gold-yellow, readable everywhere

        private NpcController _npc;
        private GameObject _go;
        private TextMeshPro _label;
        private string _lastName;
        private float _lastSize = -1f;
        private float _lastHeight = float.MinValue;

        private void Awake()
        {
            _npc = GetComponent<NpcController>();
        }

        private void LateUpdate()
        {
            var cam = GameCamera.instance;  // absent on a dedicated server and in menus
            bool wantVisible = cam != null
                && _npc != null
                && !_npc.hideNameplate
                && ConfigManager.Instance != null
                && ConfigManager.Instance.configStaticNpcNameplateMode != null
                && ConfigManager.Instance.configStaticNpcNameplateMode.Value == StaticNpcNameplateMode.HoverName;

            if (wantVisible && Vector3.Distance(cam.transform.position, transform.position) > MaxVisibleDistance)
                wantVisible = false;

            if (!wantVisible)
            {
                if (_go != null && _go.activeSelf) _go.SetActive(false);
                return;
            }

            if (_go == null) Build();
            if (_go == null) return;
            if (!_go.activeSelf) _go.SetActive(true);

            string name = _npc.GetDisplayName();
            if (name != _lastName)
            {
                _lastName = name;
                _label.text = name;
            }

            float size = Mathf.Max(0.1f, _npc.nameplateScale);
            if (size != _lastSize)
            {
                _lastSize = size;
                _go.transform.localScale = Vector3.one * size;
            }

            float height = _npc.nameplateYOffset;
            if (height != _lastHeight)
            {
                _lastHeight = height;
                _go.transform.localPosition = new Vector3(0f, Mathf.Max(MinHeight, BaseHeight + height), 0f);
            }

            // kg TextComponent billboard: face the camera's forward, kept upright (z = 0).
            Vector3 e = Quaternion.LookRotation(cam.transform.forward).eulerAngles;
            _go.transform.rotation = Quaternion.Euler(e.x, e.y, 0f);
        }

        private void Build()
        {
            try
            {
                _go = new GameObject("FiresHoverName");
                _go.transform.SetParent(transform, false);
                _go.transform.localPosition = new Vector3(0f, BaseHeight, 0f);
                _label = _go.AddComponent<TextMeshPro>();
                var averia = UIFontConfig.GetFont(UIFontConfig.FontStyle.AveriaLibre);
                if (averia != null) _label.font = averia;
                _label.text = _npc != null ? _npc.GetDisplayName() : "";
                _label.fontSize = BaseFontSize;
                _label.alignment = TextAlignmentOptions.Center;
                _label.color = HoverYellow;
                _label.outlineWidth = 0.15f;
                _label.outlineColor = Color.black;
                _label.rectTransform.sizeDelta = new Vector2(12f, 1.5f);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[NpcHoverNameplate] build failed: " + ex.Message);
                if (_go != null) Destroy(_go);
                _go = null;
                enabled = false;   // don't retry every frame on a broken TMP setup
            }
        }

        private void OnDestroy()
        {
            if (_go != null) Destroy(_go);
        }
    }
}
