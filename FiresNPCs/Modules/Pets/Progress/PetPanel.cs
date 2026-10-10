using System.Collections.Generic;
using System.Linq;
using FiresRPGmaker.UI.Screens.PlayerPanels;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>The Pet panel (Tools\FiresUI_CompanionPanel\pet_panel.png, Fire-approved 10-09): portrait, stars, XP bar, passive buff
    /// (now » next), passive skill, a food picker with Feed, the next level's materials with have/need, Dismiss and Level up.
    /// Opened by right-clicking the Pet slot » Pet (VAInventory / the companion panel call <see cref="Show"/>), or ShowForLocal().
    /// Feed and Level up are server-checked (PetProgressRpc). Layout: the FiresPetPanel prefab (WishboneNpcPanels builder).</summary>
    public class PetPanel : BasePlayerModulePanel
    {
        private static PetPanel _instance;
        protected override string PanelTitle => "Pet";
        protected override Vector2 PanelSize => new Vector2(500f, 510f);
        protected override string PrefabName => "FiresPetPanel.prefab";

        private static readonly Color Cream = new Color(0.93f, 0.88f, 0.78f, 1f);
        private static readonly Color Good = new Color(0.55f, 0.80f, 0.35f, 1f);
        private static readonly Color Bad = new Color(0.90f, 0.40f, 0.30f, 1f);

        private ItemDrop.ItemData _pet;
        private Character _owner;
        private readonly List<ItemDrop.ItemData> _foods = new List<ItemDrop.ItemData>();
        private int _foodIndex;
        private bool _busy;

        private TMP_Text _title, _subtitle, _stars, _levelText, _xpText, _buffText, _skillText, _foodText, _foodCount, _needsHint, _status, _levelUpText;
        private Image _xpFill, _foodIcon;
        private Button _feed, _levelUp, _dismiss;
        private readonly Transform[] _needs = new Transform[2];
        private RawImage _portrait;
        private readonly Image[] _starImgs = new Image[5];   // the prefab's gold diamonds (no star glyph in AveriaSans)

        /// <summary>Opens the panel for a pet item and its owner (the local player, or a companion whose cell holds it).</summary>
        public static void Show(ItemDrop.ItemData pet, Character owner)
        {
            if (pet == null || !PetApi.IsPet(pet)) { Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "That's not a pet."); return; }
            if (_instance == null)
            {
                var go = new GameObject("FiresPetPanel");
                _instance = go.AddComponent<PetPanel>();
                DontDestroyOnLoad(go);
            }
            _instance._pet = pet;
            _instance._owner = owner != null ? owner : Player.m_localPlayer;
            _instance.Open(PetCatalog.CreatureOf(pet), "");
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] panel open: {PetCatalog.CreatureOf(pet)} L{PetProgress.Level(pet)} xp {PetProgress.Xp(pet)} (owner {(_instance._owner != null ? _instance._owner.m_name : "?")})");
        }

        /// <summary>The local player's equipped pet (the VA Pet slot), if any.</summary>
        public static void ShowForLocal()
        {
            var p = Player.m_localPlayer;
            var item = p != null ? PetManager.LocalPetItem(p) : null;
            if (item == null) { p?.Message(MessageHud.MessageType.Center, "No pet in your Pet slot."); return; }
            Show(item, p);
        }

        protected override void BindPrefab(GameObject root)
        {
            var t = root.transform;
            BufferPlayerPanel.MakeDraggable(t);
            _title = FindTmp(t, "TitleText"); _subtitle = FindTmp(t, "SubtitleText");
            _stars = FindTmp(t, "StarsText"); _levelText = FindTmp(t, "LevelText");
            for (int s = 0; s < _starImgs.Length; s++) _starImgs[s] = Find(t, "Star" + s)?.GetComponent<Image>();
            _xpFill = Find(t, "XpFill")?.GetComponent<Image>(); _xpText = FindTmp(t, "XpText");
            _buffText = FindTmp(t, "BuffText"); _skillText = FindTmp(t, "SkillText");
            _foodIcon = Find(t, "FoodIcon")?.GetComponent<Image>(); _foodCount = FindTmp(t, "FoodCount"); _foodText = FindTmp(t, "FoodText");
            _needsHint = FindTmp(t, "NeedsHint"); _status = FindTmp(t, "StatusText");
            _needs[0] = Find(t, "Need0")?.transform; _needs[1] = Find(t, "Need1")?.transform;
            _feed = FindBtn(t, "FeedButton"); _levelUp = FindBtn(t, "LevelUpButton"); _dismiss = FindBtn(t, "DismissButton");
            _levelUpText = _levelUp != null ? _levelUp.GetComponentInChildren<TMP_Text>(true) : null;
            Wire(_feed, OnFeed); Wire(_levelUp, OnLevelUp); Wire(_dismiss, OnDismiss);
            Wire(FindBtn(t, "FoodPrevButton"), () => StepFood(-1));
            Wire(FindBtn(t, "FoodNextButton"), () => StepFood(+1));

            var portrait = Find(t, "Portrait");
            if (portrait != null)
            {
                var go = new GameObject("PortraitRender", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(portrait.transform, false);
                var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(4, 4); rt.offsetMax = new Vector2(-4, -4);
                _portrait = go.GetComponent<RawImage>(); _portrait.raycastTarget = false; _portrait.enabled = false;
            }
        }

        private static void Wire(Button b, UnityEngine.Events.UnityAction a) { if (b != null) { b.onClick.RemoveAllListeners(); b.onClick.AddListener(a); } }

        protected override void OnOpen(string creature, string _)
        {
            _foodIndex = 0;
            Status("", true);
            Refresh();
            StagePortrait(creature);
        }

        private void StagePortrait(string creature)
        {
            if (_portrait == null) return;
            try
            {
                var r = global::FiresRPGmaker.UI.QuestPreviewRenderer.EnsureInstance();
                if (r.StagePreview(creature) || r.StagePreview(PetCatalog.ItemPrefix + creature)) { _portrait.texture = r.Texture; _portrait.enabled = true; }
            }
            catch (System.Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] portrait: " + ex.Message); }
        }

        protected override void Update()
        {
            base.Update();
            if (!IsOpen) return;
            var r = global::FiresRPGmaker.UI.QuestPreviewRenderer.Instance;
            if (_portrait != null && _portrait.enabled && r != null && _portrait.texture != r.Texture) _portrait.texture = r.Texture;
        }

        private void Refresh()
        {
            if (_pet == null) return;
            string creature = PetCatalog.CreatureOf(_pet);
            int level = PetProgress.Level(_pet), xp = PetProgress.Xp(_pet);
            bool max = level >= PetProgress.MaxLevel;
            int need = max ? 1 : PetProgress.XpFor(level);
            bool mine = _owner == Player.m_localPlayer;

            Set(_title, PetText.PetName(creature) + (mine ? " · your pet" : " · " + (_owner != null ? _owner.GetHoverName() : "pet")));
            Set(_subtitle, PetFacts.Family(creature) + " · Tier " + PetFacts.Tier(creature));
            Set(_stars, PetText.Stars(level));
            for (int s = 0; s < _starImgs.Length; s++)
                if (_starImgs[s] != null) _starImgs[s].color = s < level ? new Color(0.95f, 0.66f, 0.26f, 1f) : new Color(0.35f, 0.29f, 0.22f, 1f);
            Set(_levelText, max ? "Level 5 · ascended" : "Level " + level + " of 5");
            if (_xpFill != null) _xpFill.fillAmount = max ? 1f : Mathf.Clamp01(xp / (float)need);
            Set(_xpText, max ? "max" : xp + " / " + need);
            var buffs = PetText.BuffLines(creature, level, true);
            Set(_buffText, level == 0 ? "<color=#a8987a>Un-upgraded: looks only.</color>\n" + string.Join("\n", buffs) : string.Join("\n", buffs));
            Set(_skillText, PetFacts.Family(creature) + ": " + PetText.SkillLine(creature, level));

            // food picker: taming foods first
            var inv = Player.m_localPlayer?.GetInventory();
            _foods.Clear();
            if (inv != null)
                _foods.AddRange(inv.GetAllItems().Where(i => i?.m_dropPrefab != null && PetProgress.FeedXp(creature, i) > 0)   // review #18
                    .GroupBy(i => i.m_dropPrefab.name).Select(g => g.First())
                    .OrderByDescending(i => PetProgress.IsTamingFood(creature, i)).ThenByDescending(i => PetProgress.FeedXp(creature, i)));
            if (_foodIndex >= _foods.Count) _foodIndex = 0;
            var food = _foods.Count > 0 ? _foods[_foodIndex] : null;
            if (_foodIcon != null) { _foodIcon.sprite = food?.GetIcon(); _foodIcon.enabled = food != null; }
            Set(_foodCount, food != null && inv != null ? PetProgress.CountInBag(inv, food.m_dropPrefab.name).ToString() : "");
            if (food == null) Set(_foodText, "No food in your bag");
            else
            {
                bool taming = PetProgress.IsTamingFood(creature, food);
                Set(_foodText, Loc(food.m_shared.m_name) + " · " + (taming ? "3x XP" : "1x XP") + " <color=#a8987a>(taming foods give 3x)</color>");
            }
            int gain = food != null ? Mathf.Min(PetProgress.FeedXp(creature, food), Mathf.Max(0, need - xp)) : 0;
            Set(_feed != null ? _feed.GetComponentInChildren<TMP_Text>(true) : null, gain > 0 ? $"Feed (+{gain} XP)" : "Feed");
            if (_feed != null) _feed.interactable = !_busy && mine && !max && food != null && xp < need;

            // materials
            var needs = PetProgress.NextNeeds(creature, level);
            bool haveAll = true;
            for (int i = 0; i < _needs.Length; i++)
            {
                var slot = _needs[i];
                if (slot == null) continue;
                bool on = i < needs.Count;
                slot.gameObject.SetActive(on);
                if (!on) continue;
                var n = needs[i];
                int have = inv != null ? PetProgress.CountInBag(inv, n.Prefab) : 0;
                if (have < n.Count) haveAll = false;
                var icon = slot.Find("IconFrame/IconBg/Icon")?.GetComponent<Image>();
                var go = ObjectDB.instance?.GetItemPrefab(n.Prefab);
                if (icon != null) { icon.sprite = go != null ? go.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null; icon.enabled = icon.sprite != null; }
                Set(slot.Find("NameText")?.GetComponent<TMP_Text>(), n.Name + " " + (have >= n.Count ? "<color=#8ccf5a>" : "<color=#e0664c>") + have + "/" + n.Count + "</color>");
            }
            Set(_needsHint, max ? "Ascended: no more levels." : needs.Count < 2 ? "(no trophy or drop: the metal only)" : PetProgress.SignatureItem(creature)?.StartsWith("Trophy") == true ? "" : "(no trophy: its main drop + the metal)");

            bool xpFull = !max && xp >= need;
            if (_levelUp != null) _levelUp.interactable = !_busy && mine && xpFull && haveAll;
            string lack = max ? "max level" : !xpFull ? $"needs {need - xp} XP" : "";
            var missing = needs.Where(n => (inv != null ? PetProgress.CountInBag(inv, n.Prefab) : 0) < n.Count)
                               .Select(n => $"{n.Count - (inv != null ? PetProgress.CountInBag(inv, n.Prefab) : 0)} {n.Name}").ToList();
            if (missing.Count > 0) lack += (lack.Length > 0 ? ", " : "needs ") + string.Join(", ", missing);
            Set(_levelUpText, lack.Length == 0 ? "Level up" : "Level up (" + lack + ")");
            if (!mine) Status("A companion's pet: feed and level it from your own bag once it's back in your Pet slot.", false);
        }

        private void StepFood(int d)
        {
            if (_foods.Count == 0) return;
            _foodIndex = (_foodIndex + d + _foods.Count) % _foods.Count;
            Refresh();
        }

        private void OnFeed()
        {
            var food = _foods.Count > 0 ? _foods[Mathf.Clamp(_foodIndex, 0, _foods.Count - 1)] : null;
            if (food == null || _busy) return;
            _busy = true; Refresh();
            PetProgressRpc.Feed(Player.m_localPlayer, _pet, food, (msg, ok) => { _busy = false; Status(msg, ok); if (IsOpen) Refresh(); });
        }

        private void OnLevelUp()
        {
            if (_busy) return;
            _busy = true; Refresh();
            PetProgressRpc.LevelUp(Player.m_localPlayer, _pet, (msg, ok) => { _busy = false; Status(msg, ok); if (IsOpen) Refresh(); });
        }

        private void OnDismiss()
        {
            if (_owner != null) PetApi.Dismiss(_owner, "pet panel");
            Status("Dismissed: the pet hides until you equip it again.", true);
            FiresRPGmaker.Log?.LogDebug("[FiresPets] panel dismiss");
            Close();
        }

        private void Status(string s, bool ok) { if (_status != null) { _status.text = s; _status.color = ok ? Good : Bad; } }
        private static void Set(TMP_Text t, string s) { if (t != null) t.text = s ?? ""; }
        private static string Loc(string s) => Localization.instance != null ? Localization.instance.Localize(s) : s;
    }
}
