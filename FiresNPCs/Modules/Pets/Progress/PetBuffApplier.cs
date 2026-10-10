using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>Keeps SE_FiresPet on every pet owner whose ZDO this client owns: the local player (its pet item's level from the
    /// VA Pet slot) and companions (the level mirrored on the owner's ZDO as "fires_pet_level", written with the pet through
    /// PetProgressApi.SetOwnerLevel; [generator]'s companion cell calls it). Once a second it rebuilds the values from config +
    /// level; an owner with no pet, or a level-0 pet, gets none. NPCs never get a buff (their pets are cosmetic).
    /// Also the combat XP: 1 XP/s while the local player fought in the last 5 s, capped per in-game day.</summary>
    internal sealed class PetBuffApplier : MonoBehaviour
    {
        public const string ZdoLevel = "fires_pet_level";
        /// <summary>The local player's pet Luck, mirrored so the owner of a dying creature can roll the KILLER's Luck.</summary>
        public const string ZdoLuck = "fires_pet_luck";
        private static PetBuffApplier s_instance;
        private float _next, _lastFightAt = -100f, _combatCarry;
        private readonly Dictionary<Character, string> _lastKey = new Dictionary<Character, string>();
        // One reusable SE template per owner: SEMan.AddStatusEffect MemberwiseClones it (sharing the native object), so it
        // can't be destroyed while worn; reusing it stops one leaked ScriptableObject per change ([classes-ui] review #17).
        private readonly Dictionary<Character, SE_FiresPet> _template = new Dictionary<Character, SE_FiresPet>();
        private readonly HashSet<string> _warned = new HashSet<string>();

        internal static void Ensure()
        {
            if (s_instance != null) return;
            var go = new GameObject("FiresPetBuffApplier");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<PetBuffApplier>();
        }

        /// <summary>PetSkills calls this when the local player deals or takes damage.</summary>
        internal static void NoteFight() { if (s_instance != null) s_instance._lastFightAt = Time.time; }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            if (ZNet.instance == null) return;
            PetProgressRpc.ExpireStale();
            Prune();
            var player = Player.m_localPlayer;
            try
            {
                if (player != null)   // [classes-ui] review #14: no local player only skips the player half
                {
                    var item = PetManager.LocalPetItem(player);
                    string creature = item != null ? PetCatalog.CreatureOf(item) : null;
                    int level = item != null ? PetProgress.Level(item) : 0;
                    MirrorLevel(player, level, creature);
                    Apply(player, creature, level, item);
                    CombatXp(item);
                }

                foreach (var c in Character.GetAllCharacters())
                {
                    if (c == null || c is Player) continue;
                    var nv = c.GetComponent<ZNetView>();
                    if (nv == null || !nv.IsValid() || !nv.IsOwner()) continue;
                    var zdo = nv.GetZDO();
                    string pet = zdo.GetString(PetApi.ZdoKeyPet, "");
                    if (string.IsNullOrEmpty(pet) && !_lastKey.ContainsKey(c)) continue;
                    if (!c.IsTamed()) continue;   // companions are tamed; RPGmaker NPC pets stay cosmetic
                    Apply(c, pet, zdo.GetInt(ZdoLevel, 0), null);
                }
            }
            catch (System.Exception ex)
            {
                // [classes-ui] review #15: each distinct problem warns once, then stays at Debug.
                if (_warned.Add(ex.Message)) FiresRPGmaker.Log?.LogWarning("[FiresPets] buff tick: " + ex.Message);
                else FiresRPGmaker.Log?.LogDebug("[FiresPets] buff tick: " + ex.Message);
            }
        }

        private static void MirrorLevel(Player player, int level, string creature)
        {
            var nv = player.GetComponent<ZNetView>();
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            var zdo = nv.GetZDO();
            if (zdo.GetInt(ZdoLevel, 0) != level) zdo.Set(ZdoLevel, level);
            float luck = 0f;
            if (level > 0 && !string.IsNullOrEmpty(creature))
                foreach (var b in PetBuffTable.For(creature, PetFacts.Family(creature)).Buffs)
                    if (b.Stat == PetStat.Luck) luck += b.At(level, PetProgress.Scale);
            luck = Mathf.Min(0.05f, luck);
            if (Mathf.Abs(zdo.GetFloat(ZdoLuck, 0f) - luck) > 0.0001f) zdo.Set(ZdoLuck, luck);
        }

        // [classes-ui] review #16: forget owners that are gone.
        private void Prune()
        {
            foreach (var c in new List<Character>(_lastKey.Keys)) if (c == null) _lastKey.Remove(c);
            foreach (var c in new List<Character>(_template.Keys)) if (c == null) { var t = _template[c]; _template.Remove(c); if (t != null) Destroy(t); }
        }

        /// <summary>Puts on / refreshes / takes off the owner's SE_FiresPet.</summary>
        private void Apply(Character owner, string creature, int level, ItemDrop.ItemData item)
        {
            var seman = owner.GetSEMan();
            if (seman == null) return;
            string key = string.IsNullOrEmpty(creature) || level <= 0 ? "" : creature + "#" + level + "#" + (PetProgress.BuffScale?.Value ?? 1f);
            _lastKey.TryGetValue(owner, out var last);
            var existing = seman.GetStatusEffect(SE_FiresPet.SeHash) as SE_FiresPet;
            if (key.Length == 0)
            {
                if (existing != null) { seman.RemoveStatusEffect(SE_FiresPet.SeHash, true); FiresRPGmaker.Log?.LogDebug($"[FiresPets] buff off {owner.m_name} ({(string.IsNullOrEmpty(creature) ? "no pet" : "level 0")})"); }
                _lastKey.Remove(owner);
                return;
            }
            if (existing != null && key == last) return;
            if (existing != null) seman.RemoveStatusEffect(SE_FiresPet.SeHash, true);

            if (!_template.TryGetValue(owner, out var se) || se == null) { se = ScriptableObject.CreateInstance<SE_FiresPet>(); _template[owner] = se; }
            se.name = SE_FiresPet.SeName;
            se.m_nameHash = SE_FiresPet.SeHash;
            Fill(se, creature, level);
            se.m_icon = Icon(creature, item);
            se.m_ttl = 0f;
            seman.AddStatusEffect(se, true);
            _lastKey[owner] = key;
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] buff on {owner.m_name}: {creature} L{level} → {PetText.BuffSummary(se).Replace('\n', ';')}");
        }

        /// <summary>The values for a creature at a level, from the table × BuffScale. Shared by the tooltip and the panel.</summary>
        internal static void Fill(SE_FiresPet se, string creature, int level)
        {
            var def = PetBuffTable.For(creature, PetFacts.Family(creature));
            float scale = PetProgress.Scale;
            se.Creature = creature; se.PetLevel = level;
            se.Values.Clear(); se.m_addArmor = 0f; se.SkillId = null; se.SkillValue = 0f;   // the template is reused
            foreach (var b in def.Buffs)
            {
                if (b.Stat == PetStat.DropChance || b.Stat == PetStat.Luck) continue;   // read by PetSkills from the def
                float v = b.At(level, scale);
                if (b.Stat == PetStat.Armor) se.m_addArmor += v;
                else se.Values[b.Stat] = (se.Values.TryGetValue(b.Stat, out var x) ? x : 0f) + v;
            }
            bool skillsOn = PetProgress.SkillsEnabled?.Value ?? true;
            if (skillsOn && def.Skill != null && def.Skill.Phase == 1 && level >= def.Skill.MinLevel)
            { se.SkillId = def.Skill.Id; se.SkillValue = def.Skill.At(level); }
            se.m_name = PetText.PetName(creature) + " (pet)";
        }

        private static Sprite Icon(string creature, ItemDrop.ItemData item)
        {
            if (item != null) return item.GetIcon();
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(PetCatalog.ItemPrefix + creature) : null;
            return go != null ? go.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
        }

        // 1 XP/s while fighting, capped per in-game day (the day stamp + the day's total live on the item too).
        private void CombatXp(ItemDrop.ItemData item)
        {
            if (item == null || Time.time - _lastFightAt > 5f) return;
            int level = PetProgress.Level(item);
            if (level >= PetProgress.MaxLevel) return;
            int day = EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;
            item.m_customData.TryGetValue(PetProgress.KeyDay, out var dayS);
            item.m_customData.TryGetValue(PetProgress.KeyDayXp, out var dayXpS);
            int dayXp = dayS == day.ToString() && int.TryParse(dayXpS, out int dx) ? dx : 0;
            int cap = PetProgress.CombatXpPerDay?.Value ?? 300;
            if (dayXp >= cap) return;
            _combatCarry += PetProgress.CombatXpPerSecond?.Value ?? 1f;
            int whole = Mathf.FloorToInt(_combatCarry);
            if (whole <= 0) return;
            _combatCarry -= whole;
            int added = PetProgress.AddXp(item, Mathf.Min(whole, cap - dayXp));
            item.m_customData[PetProgress.KeyDay] = day.ToString();
            item.m_customData[PetProgress.KeyDayXp] = (dayXp + added).ToString();
        }
    }

    /// <summary>For other modules (FiresCompanions via PetBridge): mirror a companion's pet item level onto its ZDO, so the
    /// companion's owner client gives the companion the right buff. Call on the ZDO owner after SetPet.</summary>
    public static class PetProgressApi
    {
        public static void SetOwnerLevel(Character owner, int level)
        {
            var nv = owner != null ? owner.GetComponent<ZNetView>() : null;
            if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
            nv.GetZDO().Set(PetBuffApplier.ZdoLevel, Mathf.Clamp(level, 0, PetProgress.MaxLevel));
        }

        public static int LevelOf(ItemDrop.ItemData petItem) => PetProgress.Level(petItem);
    }
}
