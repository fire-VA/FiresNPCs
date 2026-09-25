using System;
using UnityEngine;
using FiresCore.Npc;
using FiresCore.Npc.Archetypes;

namespace FiresRPGmaker.Modules.Compendium
{
    /// <summary>
    /// Tracks player discovery of mod features and triggers appropriate compendium unlocks.
    /// 
    /// DISCOVERY TRIGGERS:
    /// - companion_recruited: When player first recruits any companion
    /// - archetype_seen_{type}: When player sees a companion with that archetype
    /// - hybrid_unlocked_{main}_{sub}: When a companion unlocks a hybrid class
    /// - ability_unlocked_{name}: When a companion unlocks a specific ability
    /// </summary>
    public static class CompendiumDiscoveryTracker
    {
        /// <summary>
        /// Called when a player recruits their first companion.
        /// </summary>
        public static void OnCompanionRecruited(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            // Skip during respawn / loading screen - Discover writes to the player's
            // known-texts dictionary (saved into the .fch via Player.Save) and may
            // trigger UI/notification side effects. Player-state writes during the
            // teleport window have been observed to deadlock the zone stream.
            // Discoveries replay correctly the next time the trigger fires.
            if (CompanionPatches.AreCompanionTeleportsSuppressed()) return;

            try
            {
                // Unlock basic companion entries
                CompendiumManager.DiscoverByTrigger("companion_recruited", player);
                
                Debug.Log("[CompendiumDiscoveryTracker] Triggered companion_recruited discovery");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnCompanionRecruited error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a player encounters a companion with a specific archetype.
        /// </summary>
        public static void OnArchetypeSeen(Player player, ArchetypeClass archetype)
        {
            if (player == null || player != Player.m_localPlayer) return;
            if (archetype == ArchetypeClass.None) return;
            if (CompanionPatches.AreCompanionTeleportsSuppressed()) return;

            try
            {
                string triggerKey = $"archetype_seen_{archetype.ToString().ToLower()}";
                CompendiumManager.DiscoverByTrigger(triggerKey, player);
                
                Debug.Log($"[CompendiumDiscoveryTracker] Triggered {triggerKey} discovery");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnArchetypeSeen error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a companion unlocks a hybrid class.
        /// </summary>
        public static void OnHybridUnlocked(Player player, ArchetypeClass mainClass, ArchetypeClass subClass)
        {
            if (CompanionPatches.AreCompanionTeleportsSuppressed()) return;
            if (player == null || player != Player.m_localPlayer) return;
            if (mainClass == ArchetypeClass.None || subClass == ArchetypeClass.None) return;
            
            try
            {
                string triggerKey = $"hybrid_unlocked_{mainClass.ToString().ToLower()}_{subClass.ToString().ToLower()}";
                CompendiumManager.DiscoverByTrigger(triggerKey, player);
                
                // Also trigger generic hybrid discovery
                CompendiumManager.DiscoverByTrigger("hybrid_system_seen", player);
                
                Debug.Log($"[CompendiumDiscoveryTracker] Triggered {triggerKey} discovery");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnHybridUnlocked error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a companion unlocks a specific ability.
        /// </summary>
        public static void OnAbilityUnlocked(Player player, string abilityName)
        {
            if (player == null || player != Player.m_localPlayer) return;
            if (string.IsNullOrEmpty(abilityName)) return;
            
            try
            {
                string triggerKey = $"ability_unlocked_{abilityName.ToLower().Replace(" ", "_")}";
                CompendiumManager.DiscoverByTrigger(triggerKey, player);
                
                Debug.Log($"[CompendiumDiscoveryTracker] Triggered {triggerKey} discovery");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnAbilityUnlocked error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a companion uses a synergy ability.
        /// </summary>
        public static void OnSynergyAbilityUsed(Player player, string synergyName)
        {
            if (player == null || player != Player.m_localPlayer) return;
            if (string.IsNullOrEmpty(synergyName)) return;
            
            try
            {
                // Unlock synergy system entry
                CompendiumManager.DiscoverByTrigger("synergy_system_seen", player);
                
                Debug.Log($"[CompendiumDiscoveryTracker] Triggered synergy discovery for {synergyName}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnSynergyAbilityUsed error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a companion reaches certain level milestones.
        /// </summary>
        public static void OnCompanionLevelUp(Player player, int newLevel)
        {
            if (player == null || player != Player.m_localPlayer) return;
            
            try
            {
                // Level milestone discoveries
                if (newLevel >= 10)
                {
                    CompendiumManager.DiscoverByTrigger("companion_level_10", player);
                }
                if (newLevel >= 25)
                {
                    CompendiumManager.DiscoverByTrigger("companion_level_25", player);
                }
                if (newLevel >= 50)
                {
                    CompendiumManager.DiscoverByTrigger("companion_level_50", player);
                }
                if (newLevel >= 75)
                {
                    CompendiumManager.DiscoverByTrigger("companion_level_75", player);
                }
                if (newLevel >= 100)
                {
                    CompendiumManager.DiscoverByTrigger("companion_level_100", player);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnCompanionLevelUp error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when a companion is assigned to NPC mode.
        /// </summary>
        public static void OnNpcModeActivated(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            
            try
            {
                CompendiumManager.DiscoverByTrigger("npc_mode_seen", player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnNpcModeActivated error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when player uses the companion command system (Shift+MMB).
        /// </summary>
        public static void OnCommandSystemUsed(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            
            try
            {
                CompendiumManager.DiscoverByTrigger("command_system_used", player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnCommandSystemUsed error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when player opens the companion inventory screen.
        /// </summary>
        public static void OnCompanionInventoryOpened(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            
            try
            {
                CompendiumManager.DiscoverByTrigger("inventory_opened", player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnCompanionInventoryOpened error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Called when player views the companion stats/attribute screen.
        /// </summary>
        public static void OnCompanionStatsViewed(Player player)
        {
            if (player == null || player != Player.m_localPlayer) return;
            
            try
            {
                CompendiumManager.DiscoverByTrigger("stats_viewed", player);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CompendiumDiscoveryTracker] OnCompanionStatsViewed error: {ex.Message}");
            }
        }
    }
}
