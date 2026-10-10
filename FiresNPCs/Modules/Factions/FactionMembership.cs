using System;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Per-player faction membership. Persistence rides the character's customData under the
    /// MP-compatible key <c>MPASN_Faction</c> (a lowercased faction id string), so characters coming
    /// from a Marketplace server keep their faction. The id is also mirrored to the player's ZDO
    /// (<c>MPASN_FactionId</c>, string) so other clients can read it for nameplates / aggro / PvP
    /// without owning the character. The faction's optional Buff is applied as a status effect named
    /// <c>MPASN_FactionBuff</c> (removed + reapplied on every change).
    /// </summary>
    public static class FactionMembership
    {
        public const string CustomDataKey = "MPASN_Faction";     // persistent, MP-compatible (id string)
        public const string ZdoKey = "MPASN_FactionId";          // transient cross-client (id string)
        private const string FactionBuffName = "MPASN_FactionBuff";
        private static readonly int FactionBuffHash = FactionBuffName.GetStableHashCode();

        public static event Action LocalFactionChanged;

        /// <summary>The local player's faction id, or null.</summary>
        public static string LocalFactionId()
        {
            var p = Player.m_localPlayer;
            if (p == null || p.m_customData == null) return null;
            return p.m_customData.TryGetValue(CustomDataKey, out var id) && !string.IsNullOrEmpty(id) ? id : null;
        }

        public static FactionData LocalFaction() => FactionManager.Get(LocalFactionId());

        /// <summary>Any character's faction id from its ZDO (null when none / invalid).</summary>
        public static string FactionIdOf(Character c)
        {
            var nview = c != null ? c.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return null;
            string id = zdo.GetString(ZdoKey, "");
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public static FactionData FactionOf(Character c) => FactionManager.Get(FactionIdOf(c));

        /// <summary>True when both characters are in the same (valid) faction.</summary>
        public static bool SameFaction(Character a, Character b)
        {
            string fa = FactionIdOf(a);
            return !string.IsNullOrEmpty(fa) && string.Equals(fa, FactionIdOf(b), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Assign the local player to a faction (empty/null = leave). Writes customData + ZDO,
        /// re-applies the buff, and tells other clients to refresh the nameplate.</summary>
        public static void SetLocal(string factionId, bool notify = true)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            factionId = string.IsNullOrWhiteSpace(factionId) ? null : factionId.Trim().ToLowerInvariant();

            if (factionId != null && !FactionManager.Exists(factionId))
            {
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, $"Unknown faction '{factionId}'");
                return;
            }

            if (p.m_customData != null)
            {
                if (factionId == null) p.m_customData.Remove(CustomDataKey);
                else p.m_customData[CustomDataKey] = factionId;
            }
            RefreshLocal(notify);

            var f = FactionManager.Get(factionId);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                f != null ? $"Joined <color=#FFD966>{f.Name}</color>" : "Left your faction");
        }

        public static void ClearLocal() => SetLocal(null);

        /// <summary>Re-resolve the local player's faction onto the ZDO + status effect (call on spawn/load
        /// and after any change). Safe to call repeatedly.</summary>
        public static void RefreshLocal(bool notify)
        {
            var p = Player.m_localPlayer;
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (p == null || zdo == null) return;

            string id = LocalFactionId();
            zdo.Set(ZdoKey, id ?? "");

            // Reapply the faction buff.
            try { p.GetSEMan().RemoveStatusEffect(FactionBuffHash, true); } catch { }
            var f = FactionManager.Get(id);
            if (f != null && !string.IsNullOrEmpty(f.Buff)) TryApplyBuff(p, f.Buff);

            // The id rides the ZDO (auto-replicated), so other clients' nameplates pick it up on their
            // next EnemyHud refresh — no broadcast needed. notify drives only the local UI event.
            if (notify) LocalFactionChanged?.Invoke();
        }

        // Best-effort: resolve the faction Buff name to a StatusEffect in ObjectDB (covers vanilla +
        // any registered custom SE). Renamed to the faction-buff marker so RefreshLocal can strip it.
        private static void TryApplyBuff(Player p, string buffName)
        {
            try
            {
                if (ObjectDB.instance == null) return;
                var proto = ObjectDB.instance.GetStatusEffect(buffName.GetStableHashCode());
                if (proto == null) return;
                var se = UnityEngine.Object.Instantiate(proto);
                se.name = FactionBuffName;
                se.m_ttl = 0f;   // infinite while a member
                p.GetSEMan().AddStatusEffect(se, true);
            }
            catch (Exception ex) { Debug.LogWarning($"[Factions] buff '{buffName}' apply failed: {ex.Message}"); }
        }
    }
}
