using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// The minimal VIP flag (owner decision 2026-07-07): an admin-managed player list plus the tax
    /// and interest multipliers that go with it. The list + rates live in the SAME MarketPlace.cfg
    /// the Marketplace admin tab already edits, under kg's exact key names (VIPplayersList /
    /// MarketTaxes / VIPplayersTaxes / BankerVIPIncomeMultiplier — the spellings kg's
    /// Global_Configs writes and the corpus carries), which FiresConfigSync pushes to every client —
    /// so both the server (tax/interest math) and clients (IsVIP dialogue condition) read the same
    /// synced values. Defaults when a key is absent match kg (0% tax, no VIP boost) — nothing is
    /// hardcoded; the admin tab's fields are the authority.
    ///
    /// Match is normalized like the admin list: a token equals a player's bare SteamID64, its
    /// Steam_-prefixed form, or its display name (case-insensitive). Substring matching (MP's raw
    /// .Contains) is deliberately NOT used — it false-matches IDs that share a prefix.
    /// </summary>
    public static class VipManager
    {
        private static string CfgPath => Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");

        private static readonly HashSet<string> _vipTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static float _tax = 0f, _vipTax = 0f, _bankerVipMult = 1f;
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _seenWrite = DateTime.MinValue;

        /// <summary>True when the given user id / display name is on the VIP list.</summary>
        public static bool IsVip(string userId, string playerName)
        {
            EnsureLoaded();
            if (_vipTokens.Count == 0) return false;
            if (!string.IsNullOrEmpty(userId) && _vipTokens.Contains(NormalizeId(userId))) return true;
            if (!string.IsNullOrEmpty(playerName) && _vipTokens.Contains(playerName.Trim())) return true;
            return false;
        }

        /// <summary>Client-side "am I VIP" — local Steam id + local player name.</summary>
        public static bool IsLocalPlayerVip()
        {
            string id = null, name = null;
            try { id = global::FiresRPGmaker.Modules.Dialogues.EwpDataBridge.GetLocalSteamId(); } catch { }
            try { name = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : Game.instance?.m_playerProfile?.m_playerName; } catch { }
            return IsVip(id, name);
        }

        /// <summary>Seller tax percent (0-100): the reduced VIP rate when <paramref name="isVip"/>, else the base rate.</summary>
        public static float MarketTaxPercent(bool isVip)
        {
            EnsureLoaded();
            return Mathf.Clamp(isVip ? _vipTax : _tax, 0f, 100f);
        }

        /// <summary>Banker interest multiplier applied on top of the base rate for VIP depositors (1 = no boost).</summary>
        public static float BankerInterestMultiplier(bool isVip)
        {
            EnsureLoaded();
            return isVip ? Mathf.Max(1f, _bankerVipMult) : 1f;
        }

        public static void Reload() { _loaded = false; EnsureLoaded(); }

        private static void EnsureLoaded()
        {
            if (_loaded && Time.unscaledTime >= _nextStaleCheck)
            {
                _nextStaleCheck = Time.unscaledTime + 5f;
                try { if (File.Exists(CfgPath) && File.GetLastWriteTimeUtc(CfgPath) > _seenWrite) _loaded = false; } catch { }
            }
            if (_loaded) return;
            _loaded = true;
            _nextStaleCheck = Time.unscaledTime + 5f;
            _vipTokens.Clear();
            _tax = 0f; _vipTax = 0f; _bankerVipMult = 1f;

            try
            {
                if (!File.Exists(CfgPath)) return;
                _seenWrite = File.GetLastWriteTimeUtc(CfgPath);
                foreach (var rawLine in File.ReadAllLines(CfgPath))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("[")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("VIPplayersList", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var tok in val.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string t = tok.Trim();
                            if (t.Length == 0 || t.Equals("User", StringComparison.OrdinalIgnoreCase) || t.Equals("IDs", StringComparison.OrdinalIgnoreCase)) continue;
                            _vipTokens.Add(NormalizeId(t));
                            _vipTokens.Add(t);   // also keep the raw form so a name token matches by name
                        }
                    }
                    else if (key.Equals("MarketTaxes", StringComparison.OrdinalIgnoreCase)) TryFloat(val, ref _tax);
                    else if (key.Equals("VIPplayersTaxes", StringComparison.OrdinalIgnoreCase)) TryFloat(val, ref _vipTax);
                    else if (key.Equals("BankerVIPIncomeMultiplier", StringComparison.OrdinalIgnoreCase)) TryFloat(val, ref _bankerVipMult);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[VipManager] read failed: {ex.Message}"); }
        }

        private static void TryFloat(string s, ref float target)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) target = v;
        }

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            int us = id.IndexOf('_');
            return (us >= 0 ? id.Substring(us + 1) : id).Trim();
        }
    }
}
