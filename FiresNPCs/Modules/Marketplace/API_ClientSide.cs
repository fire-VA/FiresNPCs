using UnityEngine;
using FiresRPGmaker.Modules.Territories;

namespace API
{
    /// <summary>
    /// KG-parity drop-in: third-party mods reflect on <c>API.ClientSide, kg.Marketplace</c> through
    /// their bundled copy of <c>API.Marketplace_API</c> (or directly call into this type when they
    /// reference us). We host the exact same surface so those consumer reflections succeed.
    ///
    /// Surface mirrored from kg.Marketplace 9.8.1 / API/ClientSide.cs:
    ///   <list type="bullet">
    ///   <item>public static bool FillingTerritoryData</item>
    ///   <item>ResetTraderItems()</item>
    ///   <item>OpenQuestJournal()</item>
    ///   <item>IsPlayerInsideTerritory(out string, out int, out int)</item>
    ///   <item>IsObjectInsideTerritoryWithFlag(Vector3, int, out string, out int, out int)</item>
    ///   <item>IsObjectInsideTerritoryWithFlag_Additional(Vector3, int, out string, out int, out int)</item>
    ///   </list>
    /// Method signatures and parameter ordering are FROZEN — KG's API_Marketplace shim binds them
    /// positionally via reflection, so any drift here silently breaks downstream mods.
    /// </summary>
    public static class ClientSide
    {
        /// <summary>Set by NPC editors / hammer-edit flows while a territory is being authored,
        /// so other systems can skip volatile reads. KG default: false.</summary>
        public static bool FillingTerritoryData;

        /// <summary>Refresh per-NPC trader inventories (KG resets trader pools, gambler pools, and
        /// transmog data together; we mirror that — trader pool refresh first, others follow as we add
        /// gambler/transmog parity).</summary>
        public static void ResetTraderItems()
        {
            try
            {
                // Initialize() alone is a no-op behind the _initialized guard — Reset() first, like
                // every internal reload call site (ServerConfigFileWatcher, book Apply, DialogueSyncRPC).
                FiresRPGmaker.Managers.TraderProfileManager.Reset();
                FiresRPGmaker.Managers.TraderProfileManager.Initialize();
            }
            catch { }
        }

        /// <summary>Open the player's quest journal (KG's J-key entry point).</summary>
        public static void OpenQuestJournal()
        {
            try
            {
                FiresRPGmaker.UI.QuestJournal.QuestJournalController.Instance?.Show();
            }
            catch { }
        }

        /// <summary>Returns true when the local player stands inside a territory; outputs its name
        /// + flag bitfields (cast to <c>int</c> for ABI stability — KG uses raw int, not the enum).</summary>
        public static bool IsPlayerInsideTerritory(
            out string name,
            out int flags,
            out int additionalFlags)
        {
            name = "";
            flags = 0;
            additionalFlags = 0;
            var player = Player.m_localPlayer;
            if (player == null) return false;
            var terr = TerritorySyncedState.GetCurrent(player.transform.position);
            if (terr == null) return false;
            name = terr.Name ?? "";
            flags = (int)terr.Flags;
            additionalFlags = (int)terr.AdditionalFlags;
            return true;
        }

        /// <summary>Point query — does this world position lie inside ANY territory carrying the
        /// requested primary flag? Returns the FIRST match.</summary>
        public static bool IsObjectInsideTerritoryWithFlag(
            Vector3 pos,
            int flag,
            out string name,
            out int flags,
            out int additionalFlags)
        {
            name = "";
            flags = 0;
            additionalFlags = 0;
            var target = (TerritoryFlags)flag;
            var all = TerritorySyncedState.ClientTerritories;
            if (all == null) return false;
            foreach (var t in all)
            {
                if (!t.Flags.HasFlag(target)) continue;
                if (!t.IsInside(pos)) continue;
                name = t.Name ?? "";
                flags = (int)t.Flags;
                additionalFlags = (int)t.AdditionalFlags;
                return true;
            }
            return false;
        }

        /// <summary>Same as <see cref="IsObjectInsideTerritoryWithFlag"/> but tests an AdditionalFlag
        /// bit (KG splits the surface because the two enums share no bits and downstream mods bind
        /// each variant separately).</summary>
        public static bool IsObjectInsideTerritoryWithFlag_Additional(
            Vector3 pos,
            int flag,
            out string name,
            out int flags,
            out int additionalFlags)
        {
            name = "";
            flags = 0;
            additionalFlags = 0;
            var target = (AdditionalTerritoryFlags)flag;
            var all = TerritorySyncedState.ClientTerritories;
            if (all == null) return false;
            foreach (var t in all)
            {
                if (!t.AdditionalFlags.HasFlag(target)) continue;
                if (!t.IsInside(pos)) continue;
                name = t.Name ?? "";
                flags = (int)t.Flags;
                additionalFlags = (int)t.AdditionalFlags;
                return true;
            }
            return false;
        }
    }
}
