using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Pets.Progress
{
    /// <summary>The Phase-1 pet skills and the drop / Luck hooks (Tools\COMPANION_PET_SLOT_DESIGN.md §7, Fire 10-09: Phase 1 now).
    /// The status-effect skills (Light step, Squish, Thick bark, Chitin, Stubborn, Hulk, Deep breath, Hop, Lava-born) live in
    /// SE_FiresPet. Here:
    /// - extra drops: "+N% chance of +1 X" pet buffs, and the item skills (Pocket stone, Gemcutter, Mossy, Sticky, Pinecone, Bone-dry,
    ///   Forage) on creature drops (CharacterDrop) and on gathering (DropTable: rocks, trees, guck sacks, tar); Truffle snout and
    ///   Forager on picking; Honey nose on beehives. The extra item goes into the local player's bag;
    /// - Luck: a drop under 25% chance gets a second roll at chance × Luck, i.e. chance × (1 + Luck) overall (cap 5%);
    /// - on a kill: Mend, Moon howl, Battle cry, Cinder, Scrapper;
    /// - status effects: Water-born (wet half as long), Rot ward (poison shorter), Ashen will (burning shorter), Warmth / Flameheart
    ///   (no Cold outdoors), Cold fire (no Freezing); Ember (torches last longer); Memory (less skill loss on death).
    /// Skills act for the LOCAL player's pet (the client whose player gathers / kills). Companions get the SE half through
    /// PetBuffApplier; their gathering skills wait for the companion brain ([generator]).</summary>
    internal static class PetSkills
    {
        // ── the local player's pet, cached per second ──
        private static float s_cacheAt = -10f;
        private static PetDef s_def;
        private static int s_level;
        private static string s_creature;

        private static bool Local(out PetDef def, out int level)
        {
            if (Time.time - s_cacheAt > 1f)
            {
                s_cacheAt = Time.time; s_def = null; s_level = 0; s_creature = null;
                var p = Player.m_localPlayer;
                var item = p != null ? PetManager.LocalPetItem(p) : null;
                if (item != null)
                {
                    s_creature = PetCatalog.CreatureOf(item);
                    s_level = PetProgress.Level(item);
                    if (s_level > 0 && !string.IsNullOrEmpty(s_creature)) s_def = PetBuffTable.For(s_creature, PetFacts.Family(s_creature));
                }
            }
            def = s_def; level = s_level;
            return def != null && level > 0;
        }

        private static bool SkillsOn => PetProgress.SkillsEnabled?.Value ?? true;

        /// <summary>The local pet's live phase-1 skill value, or 0.</summary>
        private static float Skill(string id)
        {
            if (!SkillsOn || !Local(out var def, out int level) || def.Skill == null || def.Skill.Id != id || def.Skill.Phase != 1 || level < def.Skill.MinLevel) return 0f;
            return def.Skill.At(level);
        }

        private static float Buff(PetStat stat, string item = null)
        {
            if (!Local(out var def, out int level)) return 0f;
            float scale = PetProgress.Scale, v = 0f;
            foreach (var b in def.Buffs)
                if (b.Stat == stat && (item == null || b.Items == null || b.Items.Any(i => string.Equals(i, item, StringComparison.OrdinalIgnoreCase))))
                    v += b.At(level, scale);
            return v;
        }

        private static float Luck() => Mathf.Min(0.05f, Buff(PetStat.Luck));

        private static void Give(string prefab, int count, string why)
        {
            var p = Player.m_localPlayer;
            var go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            if (p == null || go == null || count <= 0) return;
            var inv = p.GetInventory();
            if (!inv.CanAddItem(go, count)) { ItemDrop.DropItem(go.GetComponent<ItemDrop>().m_itemData.Clone(), count, p.transform.position + Vector3.up, Quaternion.identity); }
            else inv.AddItem(go, count);
            p.Message(MessageHud.MessageType.TopLeft, $"{PetText.PetName(s_creature)}: +{count} {PetProgress.ItemName(prefab)}");
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] skill {why}: +{count} {prefab}");
        }

        private static bool Roll(float chance) => chance > 0f && UnityEngine.Random.value < chance;

        // The extra rolls for one list of dropped prefab names (creature kill or gathering). Each kind of item rolls once.
        private static void ExtraFor(IEnumerable<string> dropped, bool fromCreature)
        {
            if (!Local(out var def, out _)) return;
            var names = new HashSet<string>(dropped.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0) return;
            foreach (var n in names)
            {
                float c = Buff(PetStat.DropChance, n);
                if (c > 0f && def.Buffs.Any(b => b.Stat == PetStat.DropChance && b.Items != null && b.Items.Contains(n, StringComparer.OrdinalIgnoreCase)) && Roll(c)) Give(n, 1, "drop chance");
            }
            if (!SkillsOn || def.Skill == null) return;
            var s = def.Skill;
            float v = Skill(s.Id);
            if (v <= 0f) return;
            switch (s.Id)
            {
                case "pocket_stone": if (!fromCreature && names.Contains("Stone") && Roll(v)) Give("Stone", 1, s.Name); break;
                case "gemcutter": if (!fromCreature && names.Contains("Crystal") && Roll(v)) Give("Crystal", 1, s.Name); break;
                case "mossy": if (!fromCreature && names.Contains("Guck") && Roll(v)) Give("Guck", 1, s.Name); break;
                case "sticky": if (!fromCreature && names.Contains("Tar") && Roll(v)) Give("Tar", 1, s.Name); break;
                case "pinecone": if (!fromCreature && (names.Contains("Wood") || names.Contains("RoundLog")) && Roll(v)) Give("PineCone", 1, s.Name); break;
                case "bone_dry": if (names.Contains("CharredBone") && Roll(v)) Give("CharredBone", 1, s.Name); break;
                case "forage":
                    string main = PetProgress.SignatureItem(s_creature);
                    if (!string.IsNullOrEmpty(main) && names.Contains(main) && Roll(v)) Give(main, 1, s.Name);
                    break;
            }
        }

        // ── hooks ──
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class RegisterOnAwake
        {
            [HarmonyPostfix] private static void Postfix()
            {
                try { PetProgressRpc.Register(); PetBuffApplier.Ensure(); }
                catch (Exception ex) { FiresRPGmaker.Log?.LogWarning("[FiresPets] register: " + ex.Message); }
            }
        }

        // The pet item tooltip: family, tier, stars, buffs now » next, skill, XP.
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        private static class Tooltip
        {
            [HarmonyPostfix] private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                try { if (item != null && PetApi.IsPet(item)) __result += PetText.ItemTooltip(item); }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] tooltip: " + ex.Message); }
            }
        }

        // ── who gets the credit ([classes-ui] review #3, #5-7): a kill or a gather is credited to the ATTACKER of the last hit.
        // The death / drop roll runs on the victim's (or rock's / tree's) ZDO owner; if that isn't the attacker's client, the
        // owner sends the attacker FiresRPGmaker_PetKill / _PetExtra and the attacker's client rolls its own pet.
        private static readonly AccessTools.FieldRef<Character, HitData> s_lastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        [ThreadStatic] private static Character s_gatherAttacker;

        private static long PeerOf(Character c)
        {
            var nv = c != null ? c.GetComponent<ZNetView>() : null;
            return nv != null && nv.IsValid() ? nv.GetZDO().GetOwner() : 0L;
        }

        /// <summary>Extra-drop rolls for a player: here if it's the local one, else sent to its client.</summary>
        private static void CreditExtras(Character attacker, IEnumerable<string> names, bool fromCreature)
        {
            if (!(attacker is Player)) return;
            var list = names.Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            if (list.Count == 0) return;
            if (attacker == Player.m_localPlayer) { ExtraFor(list, fromCreature); return; }
            long peer = PeerOf(attacker);
            if (peer != 0L) PetProgressRpc.SendExtra(peer, list, fromCreature);
        }

        internal static void OnRemoteExtra(List<string> names, bool fromCreature) { if (Player.m_localPlayer != null) ExtraFor(names, fromCreature); }
        internal static void OnRemoteKill(string victimPrefab)
        {
            var local = Player.m_localPlayer;
            if (local != null) OnKill(local, victimPrefab);
        }

        // Combat XP: the local player DEALT damage (attacker side) ...
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class Hits
        {
            [HarmonyPrefix] private static void Prefix(Character __instance, HitData hit)
            {
                if (hit != null && __instance != null && __instance != Player.m_localPlayer && Player.m_localPlayer != null
                    && hit.GetAttacker() == Player.m_localPlayer && hit.GetTotalDamage() > 0f) PetBuffApplier.NoteFight();
            }
        }

        // ... or TOOK damage (owner side, so remote-owned attackers count too).
        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class Took
        {
            [HarmonyPostfix] private static void Postfix(Character __instance, HitData hit)
            { if (__instance != null && __instance == Player.m_localPlayer && hit != null && hit.GetTotalDamage() > 0f) PetBuffApplier.NoteFight(); }
        }

        // Kill skills: once, on the victim's real death (the owner runs OnDeath once per creature).
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Deaths
        {
            [HarmonyPrefix] private static void Prefix(Character __instance)
            {
                try
                {
                    if (__instance == null || __instance is Player) return;
                    var killer = s_lastHit(__instance)?.GetAttacker() as Player;
                    if (killer == null) return;
                    string prefab = Utils.GetPrefabName(__instance.gameObject);
                    if (killer == Player.m_localPlayer) OnKill(killer, prefab);
                    else { long peer = PeerOf(killer); if (peer != 0L) PetProgressRpc.SendKill(peer, prefab); }
                }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] death hook: " + ex.Message); }
            }
        }

        private static void OnKill(Player local, string victimPrefab)
        {
            if (!SkillsOn || !Local(out var def, out _) || def.Skill == null) return;
            var s = def.Skill; float v = Skill(s.Id);
            if (v <= 0f) return;
            switch (s.Id)
            {
                case "mend": local.Heal(local.GetMaxHealth() * v, true); break;
                case "moon_howl": if (EnvMan.IsNight()) local.AddStamina(v); break;
                case "battle_cry":
                    if (local.GetSEMan().GetStatusEffect(SE_FiresPet.SeHash) is SE_FiresPet se) se.BattleCryUntil = Time.time + 5f;
                    break;
                case "cinder": if (Roll(v)) Give("Coal", 1, s.Name); break;
                case "scrapper": if (victimPrefab.StartsWith("Goblin", StringComparison.Ordinal) && Roll(v)) Give("BlackMetalScrap", 1, s.Name); break;
                default: return;
            }
            FiresRPGmaker.Log?.LogDebug($"[FiresPets] kill skill {s.Id} on {victimPrefab}");
        }

        // Creature drops (rolled on the victim's owner): Luck uses the KILLER's luck (mirrored on the killer's ZDO as
        // fires_pet_luck by PetBuffApplier); the extra rolls go to the killer.
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class CreatureDrops
        {
            [HarmonyPostfix] private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
            {
                try
                {
                    if (__result == null) return;
                    var victim = __instance.GetComponent<Character>();
                    var killer = victim != null ? s_lastHit(victim)?.GetAttacker() as Player : null;
                    if (killer == null) return;
                    float luck = Mathf.Min(0.05f, killer.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(PetBuffApplier.ZdoLuck, 0f) ?? 0f);
                    if (luck > 0f && __instance.m_drops != null)
                        foreach (var d in __instance.m_drops)
                        {
                            if (d?.m_prefab == null || d.m_chance >= 0.25f || __result.Any(kv => kv.Key == d.m_prefab)) continue;
                            if (Roll(d.m_chance * luck))
                            {
                                __result.Add(new KeyValuePair<GameObject, int>(d.m_prefab, Mathf.Max(1, d.m_amountMin)));
                                FiresRPGmaker.Log?.LogDebug($"[FiresPets] Luck: rare drop {d.m_prefab.name} ({d.m_chance:P1} × {luck:P1}) for {killer.GetPlayerName()}");
                            }
                        }
                    CreditExtras(killer, __result.Where(kv => kv.Key != null).Select(kv => kv.Key.name), true);
                }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] creature drops: " + ex.Message); }
            }
        }

        // Gathering: the hit RPCs of rocks / trees / logs / destructibles run on the object's owner and roll their DropTable
        // inside; the hit's attacker is held for that call and gets the extras.
        [HarmonyPatch]
        private static class GatherContext
        {
            static IEnumerable<System.Reflection.MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Destructible), "RPC_Damage");
                yield return AccessTools.Method(typeof(MineRock5), "RPC_Damage");
                yield return AccessTools.Method(typeof(MineRock), "RPC_Hit");
                yield return AccessTools.Method(typeof(TreeBase), "RPC_Damage");
                yield return AccessTools.Method(typeof(TreeLog), "RPC_Damage");
            }
            [HarmonyPrefix] private static void Prefix(HitData hit) { s_gatherAttacker = hit?.GetAttacker(); }
            [HarmonyFinalizer] private static void Finalizer() { s_gatherAttacker = null; }
        }

        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
        private static class GatherDrops
        {
            [HarmonyPostfix] private static void Postfix(List<GameObject> __result)
            {
                try
                {
                    if (__result == null || s_gatherAttacker == null) return;
                    CreditExtras(s_gatherAttacker, __result.Where(g => g != null).Select(g => g.name), false);
                }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] gather drops: " + ex.Message); }
            }
        }

        // Picking (picker's client): only a real pick of an unpicked plant, not a repeat ([classes-ui] review #2).
        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        private static class Picks
        {
            private static readonly AccessTools.FieldRef<Pickable, bool> s_picked = AccessTools.FieldRefAccess<Pickable, bool>("m_picked");
            [HarmonyPrefix] private static void Prefix(Pickable __instance, Humanoid character, bool repeat, out bool __state)
            { __state = !repeat && character == Player.m_localPlayer && __instance.m_itemPrefab != null && !s_picked(__instance); }
            [HarmonyPostfix] private static void Postfix(Pickable __instance, bool __state)
            {
                try
                {
                    if (!__state) return;
                    string item = __instance.m_itemPrefab.name;
                    ExtraFor(new[] { item }, false);
                    float t = Skill("truffle_snout");
                    if (t > 0f && (item.StartsWith("Mushroom", StringComparison.Ordinal) || item.EndsWith("berries", StringComparison.OrdinalIgnoreCase)) && Roll(t)) Give(item, 1, "Truffle snout");
                    float f = Skill("forager");
                    if (f > 0f && (item == "Barley" || item == "Flax") && Roll(f)) Give(item, 1, "Forager");
                }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] pick: " + ex.Message); }
            }
        }

        // Honey nose: only a real harvest (not a repeat, ward access, honey in the hive) ([classes-ui] review #1).
        [HarmonyPatch(typeof(Beehive), nameof(Beehive.Interact))]
        private static class Honey
        {
            [HarmonyPrefix] private static void Prefix(Beehive __instance, Humanoid character, bool repeat, out int __state)
            {
                __state = 0;
                if (repeat || character != Player.m_localPlayer || !PrivateArea.CheckAccess(__instance.transform.position, 0f, false)) return;
                __state = Traverse.Create(__instance).Method("GetHoneyLevel").GetValue<int>();
            }
            [HarmonyPostfix] private static void Postfix(bool __result, int __state)
            {
                try
                {
                    if (!__result || __state <= 0) return;
                    float v = Skill("honey_nose");
                    if (v > 0f && Roll(v)) Give("Honey", 1, "Honey nose");
                }
                catch (Exception ex) { FiresRPGmaker.Log?.LogDebug("[FiresPets] honey: " + ex.Message); }
            }
        }
        // Ember: the equipped torch loses durability slower.
        [HarmonyPatch(typeof(Humanoid), "DrainEquipedItemDurability")]
        private static class Ember
        {
            [HarmonyPrefix] private static void Prefix(Humanoid __instance, ItemDrop.ItemData item, ref float dt)
            {
                if (__instance != Player.m_localPlayer || item?.m_dropPrefab == null || item.m_dropPrefab.name != "Torch") return;
                float v = Skill("ember");
                if (v > 0f) dt *= 1f - Mathf.Clamp01(v);
            }
        }

        // Status effects on the local player: shorter Wet / Poison / Burning, and no Cold / Freezing for the warmth skills.
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        private static class EnvEffects
        {
            [HarmonyPrefix] private static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result)
            {
                try
                {
                    var local = Player.m_localPlayer;
                    if (local == null || __instance != local.GetSEMan()) return true;
                    bool block = (nameHash == SEMan.s_statusEffectCold && !local.InShelter() && (Skill("flameheart") > 0f || Skill("warmth") > 0f || WarmthOn("flameheart") || WarmthOn("warmth")))
                              || (nameHash == SEMan.s_statusEffectFreezing && WarmthOn("cold_fire"));
                    if (!block) return true;
                    __result = null;
                    return false;
                }
                catch { return true; }
            }

            // (no postfix here: this overload calls the StatusEffect one, whose postfix shortens; [classes-ui] review #8)
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        private static class EnvEffectsByObject
        {
            [HarmonyPostfix] private static void Postfix(SEMan __instance, StatusEffect statusEffect, StatusEffect __result)
            { if (statusEffect != null) Shorten(__instance, statusEffect.NameHash(), __result); }
        }

        // Skills with no number (Warmth, Flameheart, Cold fire) count as on from their level.
        private static bool WarmthOn(string id)
        {
            if (!SkillsOn || !Local(out var def, out int level) || def.Skill == null) return false;
            return def.Skill.Id == id && def.Skill.Phase == 1 && level >= def.Skill.MinLevel;
        }

        private static void Shorten(SEMan seman, int hash, StatusEffect se)
        {
            var local = Player.m_localPlayer;
            if (se == null || local == null || seman != local.GetSEMan() || se.m_ttl <= 0f) return;
            float cut = hash == SEMan.s_statusEffectWet && WarmthOn("water_born") ? 0.5f
                      : hash == SEMan.s_statusEffectPoison ? Skill("rot_ward")
                      : hash == SEMan.s_statusEffectBurning ? Skill("ashen_will") : 0f;
            if (cut <= 0f) return;
            se.m_ttl *= 1f - Mathf.Clamp01(cut);
        }

        // Memory: less skill loss on death.
        [HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
        private static class Memory
        {
            [HarmonyPrefix] private static void Prefix(Skills __instance, ref float factor)
            {
                if (Player.m_localPlayer == null || __instance != Player.m_localPlayer.GetSkills()) return;
                float v = Skill("memory");
                if (v > 0f) { factor *= 1f - Mathf.Clamp01(v); FiresRPGmaker.Log?.LogDebug($"[FiresPets] Memory: skill loss × {1f - v:0.00}"); }
            }
        }
    }
}
