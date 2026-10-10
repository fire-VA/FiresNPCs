using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace FiresRPGmaker.Modules.Marketplace
{
    /// <summary>
    /// THE reader for the marketplace-enforcement globals in MarketPlace.cfg — the same file (and the
    /// same kg key names) the Marketplace admin tab edits: ItemMarketLimit, BlockedPlayers,
    /// MarketSellBlockedPrefabs, PossibleCurrencies. These knobs were previously written by the book
    /// and read by NOTHING ("dead knob" class); <see cref="MarketplaceNetwork"/> now enforces them
    /// server-side (stronger than kg, which only filtered client-side). Same normalized ID/name
    /// matching and mtime hot-reload as <see cref="VipManager"/>.
    /// </summary>
    public static class MarketplaceGlobals
    {
        private static string CfgPath => Path.Combine(Paths.ConfigPath, "Marketplace", "MarketPlace.cfg");

        // kg defaults (Global_Configs.cs): limit 15, blocked prefabs "Coins, SwordCheat", currencies "Coins",
        // max quests 7, party kill-share on.
        private static int _itemMarketLimit = 15;
        private static readonly HashSet<string> _blockedPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _blockedPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Coins", "SwordCheat" };
        private static readonly List<string> _currencies = new List<string> { "Coins" };
        private static int _maxAcceptedQuests = 7;
        private static bool _allowKillQuestsInParty = true;
        private static bool _gamblerWinNotifications;   // kg GamblerEnableWinNotifications, default false
        private static readonly HashSet<string> _blockedChatUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _allowMultipleQuestScore;           // kg AllowMultipleQuestsScore, default false
        private static bool _hideOtherQuestRequirementQuests;   // kg HideOtherQuestRequirementQuests, default false
        private static Vector2 _factionNameplateOffset = Vector2.zero;   // kg FactionNameplateOffset "x,y"
        private static bool _loaded;
        private static float _nextStaleCheck;
        private static DateTime _seenWrite = DateTime.MinValue;

        /// <summary>Max simultaneous listings per seller (kg ItemMarketLimit, default 15; ≤0 = unlimited).</summary>
        public static int ItemMarketLimit { get { EnsureLoaded(); return _itemMarketLimit; } }

        /// <summary>Max simultaneously accepted quests (kg MaxAcceptedQuests, default 7; ≤0 = unlimited).</summary>
        public static int MaxAcceptedQuests { get { EnsureLoaded(); return _maxAcceptedQuests; } }

        /// <summary>Kill-quest credit is shared with party members near the kill (kg AllowKillQuestsInParty, default true).</summary>
        public static bool AllowKillQuestsInParty { get { EnsureLoaded(); return _allowKillQuestsInParty; } }

        /// <summary>Gambler wins are shouted server-wide as [GAMBLER] (kg GamblerEnableWinNotifications, default false).</summary>
        public static bool GamblerEnableWinNotifications { get { EnsureLoaded(); return _gamblerWinNotifications; } }

        /// <summary>kg AllowMultipleQuestsScore (default false): when false, one kill/harvest/craft event
        /// scores only the FIRST active quest matching that target; when true, every matching quest scores.</summary>
        public static bool AllowMultipleQuestScore { get { EnsureLoaded(); return _allowMultipleQuestScore; } }

        /// <summary>kg HideOtherQuestRequirementQuests (default false): hide quests from the selection UI
        /// while they carry an unmet finished-quest requirement.</summary>
        public static bool HideOtherQuestRequirementQuests { get { EnsureLoaded(); return _hideOtherQuestRequirementQuests; } }

        /// <summary>kg FactionNameplateOffset ("x,y", default 0,0): shifts the faction nameplate label.</summary>
        public static Vector2 FactionNameplateOffset { get { EnsureLoaded(); return _factionNameplateOffset; } }

        /// <summary>kg BlockedChatUsers: a listed player cannot SEND chat (kg mutes the sender's own input,
        /// client-side off the synced list — KG_Chat's InputText prefix). Same normalized matching as VIP.</summary>
        public static bool IsMutedChatUser(string userId, string playerName)
        {
            EnsureLoaded();
            if (_blockedChatUsers.Count == 0) return false;
            if (!string.IsNullOrEmpty(userId) && _blockedChatUsers.Contains(NormalizeId(userId))) return true;
            if (!string.IsNullOrEmpty(playerName) && _blockedChatUsers.Contains(playerName.Trim())) return true;
            return false;
        }

        /// <summary>True when this user id / display name is on the BlockedPlayers list (market fully closed to them).</summary>
        public static bool IsBlockedPlayer(string userId, string playerName)
        {
            EnsureLoaded();
            if (_blockedPlayers.Count == 0) return false;
            if (!string.IsNullOrEmpty(userId) && _blockedPlayers.Contains(NormalizeId(userId))) return true;
            if (!string.IsNullOrEmpty(playerName) && _blockedPlayers.Contains(playerName.Trim())) return true;
            return false;
        }

        /// <summary>True when this prefab may not be SOLD (kg MarketSellBlockedPrefabs).</summary>
        public static bool IsBlockedPrefab(string prefab)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(prefab) && _blockedPrefabs.Contains(prefab.Trim());
        }

        /// <summary>
        /// kg currency rule (DB.cs:610-612): a listing's currency must be on the PossibleCurrencies list —
        /// anything else is COERCED to the first allowed currency, never rejected. Empty/missing input
        /// coerces the same way.
        /// </summary>
        public static string CoerceCurrency(string requested)
        {
            EnsureLoaded();
            string fallback = _currencies.Count > 0 ? _currencies[0] : "Coins";
            if (string.IsNullOrEmpty(requested)) return fallback;
            string r = requested.Trim();
            foreach (var c in _currencies)
                if (string.Equals(c, r, StringComparison.OrdinalIgnoreCase)) return c;
            return fallback;
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
            _itemMarketLimit = 15;
            _blockedPlayers.Clear();
            _blockedPrefabs.Clear(); _blockedPrefabs.Add("Coins"); _blockedPrefabs.Add("SwordCheat");
            _currencies.Clear(); _currencies.Add("Coins");
            _maxAcceptedQuests = 7;
            _allowKillQuestsInParty = true;
            _gamblerWinNotifications = false;
            _blockedChatUsers.Clear();
            _allowMultipleQuestScore = false;
            _hideOtherQuestRequirementQuests = false;
            _factionNameplateOffset = Vector2.zero;

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

                    if (key.Equals("ItemMarketLimit", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lim)) _itemMarketLimit = lim;
                    }
                    else if (key.Equals("MaxAcceptedQuests", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mq)) _maxAcceptedQuests = mq;
                    }
                    else if (key.Equals("AllowKillQuestsInParty", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(val, out bool pk)) _allowKillQuestsInParty = pk;
                    }
                    else if (key.Equals("GamblerEnableWinNotifications", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(val, out bool gn)) _gamblerWinNotifications = gn;
                    }
                    else if (key.Equals("AllowMultipleQuestsScore", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(val, out bool mq2)) _allowMultipleQuestScore = mq2;
                    }
                    else if (key.Equals("HideOtherQuestRequirementQuests", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(val, out bool hq)) _hideOtherQuestRequirementQuests = hq;
                    }
                    else if (key.Equals("FactionNameplateOffset", StringComparison.OrdinalIgnoreCase))
                    {
                        // Accept "x,y", "(x, y)" and "{x, y}" forms.
                        var cleaned = val.Trim('(', ')', '{', '}', ' ');
                        var parts = cleaned.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2
                            && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ox)
                            && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float oy))
                            _factionNameplateOffset = new Vector2(ox, oy);
                    }
                    else if (key.Equals("BlockedChatUsers", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var tok in SplitList(val))
                        {
                            // kg's default placeholder "Steam IDs here" tokenizes to noise — never real entries.
                            if (tok.Equals("Steam", StringComparison.OrdinalIgnoreCase) || tok.Equals("IDs", StringComparison.OrdinalIgnoreCase) || tok.Equals("here", StringComparison.OrdinalIgnoreCase)) continue;
                            _blockedChatUsers.Add(NormalizeId(tok));
                            _blockedChatUsers.Add(tok);
                        }
                    }
                    else if (key.Equals("BlockedPlayers", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var tok in SplitList(val))
                        {
                            // kg's default placeholder "User IDs" tokenizes to User + IDs — never real entries.
                            if (tok.Equals("User", StringComparison.OrdinalIgnoreCase) || tok.Equals("IDs", StringComparison.OrdinalIgnoreCase)) continue;
                            _blockedPlayers.Add(NormalizeId(tok));
                            _blockedPlayers.Add(tok);   // raw form so a name token matches by name
                        }
                    }
                    else if (key.Equals("MarketSellBlockedPrefabs", StringComparison.OrdinalIgnoreCase))
                    {
                        _blockedPrefabs.Clear();
                        foreach (var tok in SplitList(val)) _blockedPrefabs.Add(tok);
                    }
                    else if (key.Equals("PossibleCurrencies", StringComparison.OrdinalIgnoreCase))
                    {
                        _currencies.Clear();
                        foreach (var tok in SplitList(val)) if (!_currencies.Contains(tok)) _currencies.Add(tok);
                        if (_currencies.Count == 0) _currencies.Add("Coins");
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[MarketplaceGlobals] read failed: {ex.Message}"); }
        }

        // Same separator family as VipManager's list parsing (kg lists are comma/space separated —
        // the default "User IDs" placeholder relies on the space split). Prefab/currency names never
        // contain spaces, so one splitter serves all four keys.
        private static IEnumerable<string> SplitList(string val)
        {
            foreach (var tok in val.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = tok.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static string NormalizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            int us = id.IndexOf('_');
            return (us >= 0 ? id.Substring(us + 1) : id).Trim();
        }
    }
}
