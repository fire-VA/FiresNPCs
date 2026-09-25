using System;
using System.Collections.Generic;

namespace FiresRPGmaker.Modules.Progression
{
    /// <summary>
    /// Runtime configuration for the private key progression system.
    /// Values are populated from ConfigManager on startup and config reload.
    /// </summary>
    public class ProgressionConfig
    {
        private static ProgressionConfig _instance;
        public static ProgressionConfig Instance => _instance ?? (_instance = new ProgressionConfig());

        // -- Keys --
        public bool UsePrivateKeys { get; set; } = true;
        public bool BlockAllGlobalKeys { get; set; } = true;
        public HashSet<string> BlockedGlobalKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllowedGlobalKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EnforcedGlobalKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> BlockedPrivateKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllowedPrivateKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> EnforcedPrivateKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // -- Raids --
        public bool UsePrivateRaids { get; set; } = true;

        // -- Blocked action feedback --
        public bool UseBlockedMessage { get; set; } = true;
        public string BlockedMessage { get; set; } = "You are not ready for this yet.";
        public bool UseBlockedEffect { get; set; } = false;

        // -- Locking features (all OFF by default) --
        // Each lock has a bool toggle and a required key name.
        // When the toggle is ON, the player must have the specified key
        // (checked against global keys, private keys, custom player keys, and EWP keys).
        // If the key name is empty, the lock has no effect even when toggled on.
        public bool LockEquipment { get; set; } = false;
        public string LockEquipmentKey { get; set; } = "";
        public bool LockCrafting { get; set; } = false;
        public string LockCraftingKey { get; set; } = "";
        public bool LockBuilding { get; set; } = false;
        public string LockBuildingKey { get; set; } = "";
        public bool LockCooking { get; set; } = false;
        public string LockCookingKey { get; set; } = "";
        public bool LockEating { get; set; } = false;
        public string LockEatingKey { get; set; } = "";
        public bool LockTaming { get; set; } = false;
        public string LockTamingKey { get; set; } = "";
        public bool LockGuardianPower { get; set; } = false;
        public string LockGuardianPowerKey { get; set; } = "";
        public bool LockBossSummons { get; set; } = false;
        public string LockBossSummonsKey { get; set; } = "";

        // -- Admin --
        public bool AdminBypass { get; set; } = true;

        /// <summary>
        /// Refresh from comma-separated config strings.
        /// Call this when config entries change.
        /// </summary>
        public void UpdateFromStrings(
            string blockedGlobal, string allowedGlobal, string enforcedGlobal,
            string blockedPrivate, string allowedPrivate, string enforcedPrivate)
        {
            BlockedGlobalKeys  = PrivateKeyManager.ParseKeySet(blockedGlobal);
            AllowedGlobalKeys  = PrivateKeyManager.ParseKeySet(allowedGlobal);
            EnforcedGlobalKeys = PrivateKeyManager.ParseKeySet(enforcedGlobal);
            BlockedPrivateKeys  = PrivateKeyManager.ParseKeySet(blockedPrivate);
            AllowedPrivateKeys  = PrivateKeyManager.ParseKeySet(allowedPrivate);
            EnforcedPrivateKeys = PrivateKeyManager.ParseKeySet(enforcedPrivate);
        }

        /// <summary>
        /// Returns true if the given lock is active and the player does NOT have the required key.
        /// Checks all key sources: global keys, private keys, custom player keys (ZDO), and EWP keys.
        /// </summary>
        public bool IsLockedByKey(bool lockEnabled, string requiredKey)
        {
            if (!lockEnabled) return false;
            if (string.IsNullOrWhiteSpace(requiredKey)) return false;

            // Check global keys
            if (PrivateKeyManager.HasGlobalKey(requiredKey)) return false;

            // Check private keys (per-player boss progression)
            if (PrivateKeyManager.HasPrivateKey(requiredKey)) return false;

            // Check custom player keys (ZDO int on the player)
            if (Player.m_localPlayer != null)
            {
                var nview = Player.m_localPlayer.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    int zdoVal = nview.GetZDO().GetInt(requiredKey);
                    if (zdoVal > 0) return false;
                }
            }

            // Check EWP keys
            string steamId = Dialogues.EwpDataBridge.GetLocalSteamId();
            if (Dialogues.EwpDataBridge.HasKey(steamId, requiredKey)) return false;

            // Player doesn't have the key - blocked
            return true;
        }
    }
}
