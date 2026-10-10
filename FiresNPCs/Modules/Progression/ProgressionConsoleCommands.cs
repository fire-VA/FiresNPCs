using System.Linq;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Dialogues;

namespace FiresRPGmaker.Modules.Progression
{
    /// <summary>
    /// Adds console commands for managing private keys and custom player keys.
    /// 
    /// Private key commands (boss progression):
    ///   va_setprivatekey, va_removeprivatekey, va_listprivatekeys, va_resetprivatekeys, va_bosscount
    /// 
    /// Custom player key commands (ZDO int + EWP sync):
    ///   va_addcustomkey, va_removecustomkey, va_listcustomkeys
    ///   - No args = targets yourself (auto-resolved steam ID)
    ///   - Optional [steamId] at the end = admin targets another player
    /// </summary>
    [HarmonyPatch]
    public static class ProgressionConsoleCommands
    {
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        [HarmonyPriority(Priority.Low)]
        public static class Patch_Terminal_InitTerminal
        {
            private static bool _added;

            static void Postfix()
            {
                if (_added) return;
                _added = true;

                // ------ Private Key commands (boss progression) ------

                new Terminal.ConsoleCommand("va_setprivatekey", "[key] - Adds a private key to local player",
                    args =>
                    {
                        if (args.Length >= 2)
                        {
                            string key = args[1].Trim().ToLower();
                            PrivateKeyManager.AddPrivateKey(key);
                            args.Context.AddString($"Added private key: {key}");
                        }
                        else
                            args.Context.AddString("Usage: va_setprivatekey <key>");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_removeprivatekey", "[key] - Removes a private key from local player",
                    args =>
                    {
                        if (args.Length >= 2)
                        {
                            string key = args[1].Trim().ToLower();
                            PrivateKeyManager.RemovePrivateKey(key);
                            args.Context.AddString($"Removed private key: {key}");
                        }
                        else
                            args.Context.AddString("Usage: va_removeprivatekey <key>");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_listprivatekeys", "Lists all private keys for local player",
                    args =>
                    {
                        var keys = PrivateKeyManager.PrivateKeys;
                        args.Context.AddString($"Private keys ({keys.Count}):");
                        foreach (var key in keys.OrderBy(k => k))
                            args.Context.AddString($"  {key}");
                    });

                new Terminal.ConsoleCommand("va_resetprivatekeys", "Clears all private keys for local player",
                    args =>
                    {
                        PrivateKeyManager.ResetPrivateKeys();
                        args.Context.AddString("All private keys cleared.");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_bosscount", "Shows boss keys defeated count",
                    args =>
                    {
                        int priv = PrivateKeyManager.GetPrivateBossCount();
                        int glob = PrivateKeyManager.GetGlobalBossCount();
                        args.Context.AddString($"Private boss keys: {priv}/7, Global boss keys: {glob}/7");
                    });

                // ------ Custom Player Key commands (ZDO + EWP sync) ------
                // Steam ID is auto-resolved from the local player.
                // Optional [steamId] at end for admin-targeting another player.

                new Terminal.ConsoleCommand("va_addcustomkey",
                    "<key> [steamId] - Adds a custom key to your player (ZDO + EWP + private key). Optional steamId to admin-target another player.",
                    args =>
                    {
                        if (args.Length < 2)
                        {
                            args.Context.AddString("Usage: va_addcustomkey <key> [steamId]");
                            return;
                        }
                        string key = args[1].Trim();
                        string steamId = args.Length >= 3 ? args[2].Trim() : EwpDataBridge.GetLocalSteamId();

                        // Write to EWP as presence key: steamId/keyName
                        EwpDataBridge.AddKey(steamId, key);

                        // Also set on local player ZDO and add as private key (if targeting self)
                        if (args.Length < 3)
                        {
                            var player = Player.m_localPlayer;
                            if (player != null)
                            {
                                var nview = player.GetComponent<ZNetView>();
                                if (nview != null && nview.IsValid())
                                    nview.GetZDO().Set(key, 1);
                            }
                            PrivateKeyManager.AddPrivateKey(key);
                        }

                        args.Context.AddString($"Added custom key: {steamId}/{key} (EWP + ZDO + private)");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_removecustomkey",
                    "<key> [steamId] - Removes a custom key from your player (ZDO + EWP + private key). Optional steamId to admin-target another player.",
                    args =>
                    {
                        if (args.Length < 2)
                        {
                            args.Context.AddString("Usage: va_removecustomkey <key> [steamId]");
                            return;
                        }
                        string key = args[1].Trim();
                        string steamId = args.Length >= 3 ? args[2].Trim() : EwpDataBridge.GetLocalSteamId();

                        // Remove from EWP
                        EwpDataBridge.RemoveKey(steamId, key);

                        // Also clear on local player ZDO and remove private key (if targeting self)
                        if (args.Length < 3)
                        {
                            var player = Player.m_localPlayer;
                            if (player != null)
                            {
                                var nview = player.GetComponent<ZNetView>();
                                if (nview != null && nview.IsValid())
                                    nview.GetZDO().Set(key, 0);
                            }
                            PrivateKeyManager.RemovePrivateKey(key);
                        }

                        args.Context.AddString($"Removed custom key: {steamId}/{key} (EWP + ZDO + private)");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_listcustomkeys",
                    "[steamId] - Lists your custom keys (ZDO + EWP). Optional steamId to view another player's EWP keys.",
                    args =>
                    {
                        string steamId = args.Length >= 2 ? args[1].Trim() : EwpDataBridge.GetLocalSteamId();
                        int totalCount = 0;

                        // List ZDO keys from the local player
                        if (args.Length < 2 && Player.m_localPlayer != null)
                        {
                            var nview = Player.m_localPlayer.GetComponent<ZNetView>();
                            if (nview != null && nview.IsValid())
                            {
                                var zdo = nview.GetZDO();
                                // Check known custom keys by scanning ZDO ints
                                // ZDO doesn't expose iteration, so we check the keys we know about
                                args.Context.AddString("- ZDO custom keys (player object):");
                                args.Context.AddString("  (ZDO keys are set on the player object and not enumerable.");
                                args.Context.AddString("   Use 'va_listcustomkeys' with a key name to check: va_hascustomkey <key>)");
                            }
                        }

                        // List EWP keys (server-side file read - only works on server/host)
                        var ewpKeys = EwpDataBridge.ListKeys(steamId);
                        if (ewpKeys.Count > 0)
                        {
                            args.Context.AddString($"- EWP keys for {steamId} ({ewpKeys.Count}):");
                            foreach (var kv in ewpKeys.OrderBy(k => k.Key))
                            {
                                if (kv.Value == 1)
                                    args.Context.AddString($"  {kv.Key}");
                                else
                                    args.Context.AddString($"  {kv.Key} = {kv.Value}");
                            }
                            totalCount += ewpKeys.Count;
                        }

                        // List private keys (in-memory)
                        var privateKeys = PrivateKeyManager.PrivateKeys;
                        if (privateKeys.Count > 0)
                        {
                            args.Context.AddString($"- Private keys ({privateKeys.Count}):");
                            foreach (var pk in privateKeys.OrderBy(k => k))
                                args.Context.AddString($"  {pk}");
                            totalCount += privateKeys.Count;
                        }

                        if (totalCount == 0 && (args.Length >= 2 || ewpKeys.Count == 0))
                        {
                            args.Context.AddString($"No custom/private keys found for {steamId}.");
                            args.Context.AddString("Note: EWP keys can only be listed on the server/host.");
                        }
                    });

                new Terminal.ConsoleCommand("va_clearmykeys",
                    "Clears all your custom keys (private keys + EWP + player unique keys).",
                    args =>
                    {
                        string steamId = EwpDataBridge.GetLocalSteamId();

                        // Clear private keys
                        PrivateKeyManager.ResetPrivateKeys();

                        // Clear EWP keys for this player (server-side)
                        EwpDataBridge.ClearMyKeys(steamId);

                        // Clear player unique keys
                        var player = Player.m_localPlayer;
                        if (player != null)
                            player.m_uniques.Clear();

                        args.Context.AddString($"Cleared all custom keys for {steamId} (private + EWP + unique).");
                    }, isCheat: true);

                new Terminal.ConsoleCommand("va_clearallkeys",
                    "[ADMIN] Clears ALL custom keys from ewp_data.yaml for ALL players + resets local private/unique keys.",
                    args =>
                    {
                        // Clear the entire EWP data file (server-side)
                        EwpDataBridge.ClearAllKeys();

                        // Clear local private keys
                        PrivateKeyManager.ResetPrivateKeys();

                        // Clear local player unique keys
                        var player = Player.m_localPlayer;
                        if (player != null)
                            player.m_uniques.Clear();

                        args.Context.AddString("Cleared ALL keys from ewp_data.yaml and reset local private/unique keys.");
                    }, isCheat: true);
            }
        }
    }
}
