using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker
{
    /// <summary>
    /// Compatibility patch for Balrond's Amazing Nature.
    ///
    /// Addresses two categories of issues:
    ///
    /// 1. (DISABLED - pending re-verification after Balrond update)
    ///    Reflection-based unpatching of Player.AddKnownLocationName, UpdateStats,
    ///    and UpdateEnvStatusEffects to stop spam/popup issues.
    ///
    /// 2. (ACTIVE) Location weather + music strip.
    ///    Balrond's EnvMan_UpdateEnvironment_Patch and LocationConstructor.GenerateLocationStructure
    ///    inject EnvZone and MusicLocation components into vanilla location prefabs that
    ///    never had them (WoodHouse, Ruin, ShipWreck, etc.), causing incorrect weather
    ///    and music to play whenever the player visits those locations.
    ///    Location.Awake_Postfix strips those components from any location that is not
    ///    a dungeon or boss arena - the only vanilla locations that legitimately carry them.
    /// </summary>
    [HarmonyPatch]   // 0.0.502: without the class attribute PatchAll skipped the Location.Awake strip (Check-HarmonyClassAttributes)
    public static class BalrondAmazingNatureCompat
    {
        private const string BalrondGUID = "balrond.astafaraios.BalrondAmazingNature";
        private static bool _applied = false;

        // Detected ONCE in Apply() and read by every Harmony patch in this class.
        // The Harmony attribute scanner auto-applies all [HarmonyPatch] methods in
        // this class regardless of whether Apply() ran, so every patch body must
        // gate itself on this flag - otherwise we'd run our compat-strip code
        // even on installations that don't have Balrond's mod loaded, breaking
        // perfectly fine vanilla locations.
        private static bool _balrondLoaded = false;

        public static void Apply()
        {
            if (_applied) return;
            _applied = true;

            try
            {
                _balrondLoaded = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(BalrondGUID);
                if (_balrondLoaded)
                    Debug.Log("[BalrondCompat] Balrond Amazing Nature detected - location-strip patch ENABLED");
                else
                    Debug.Log("[BalrondCompat] Balrond Amazing Nature not detected - all compat patches disabled");
            }
            catch (Exception ex)
            {
                _balrondLoaded = false;
                Debug.LogWarning($"[BalrondCompat] Mod-presence check failed, disabling compat: {ex.Message}");
            }

            // DISABLED - Balrond Amazing Nature just updated. Re-enable after
            // verifying the new version doesn't have the same issues.
            /*
            try
            {
                if (!_balrondLoaded)
                    return;

                var harmony = new Harmony("com.Fire.FiresRPGmaker.compat.balrond");

                UnpatchAddKnownLocationName(harmony);
                UnpatchPlayerUpdateStatsSpam(harmony);
                UnpatchPlayerEnvStatusSpam(harmony);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BalrondCompat] Failed during compat patching: {ex.Message}");
            }
            */
        }

        // -- Location weather / music strip ----------------------------------------
        // Runs as a Harmony postfix on Location.Awake so it fires after Balrond's
        // LocationConstructor.GenerateLocationStructure has already injected child
        // prefabs into the location hierarchy.
        //
        // CRITICAL gating rules:
        //
        //   1) BALROND PRESENCE.  We ONLY strip when Balrond Amazing Nature is
        //      actually loaded (_balrondLoaded).  Without this gate the patch
        //      would also run on installations that don't have Balrond, ripping
        //      legitimate EnvZone / MusicLocation off vanilla locations and
        //      leaving EnvMan in a stale state during teleports / dungeon
        //      transitions - a known cause of black-screen-on-load lockups.
        //
        //   2) NARROW PREFAB ALLOWLIST.  Even with Balrond loaded, we only strip
        //      from a hard-coded list of vanilla prefab name prefixes that we
        //      have actually observed Balrond inject into incorrectly.  Vanilla
        //      Valheim has legitimate EnvZone / MusicLocation on plenty of
        //      non-dungeon, non-boss locations (Hildir camps, Haldor vendor
        //      location, several Mistlands POIs, ambient frost-cave entrance
        //      effects, ...).  Stripping from those breaks vanilla weather/music
        //      and was almost certainly a contributor to the dungeon-entry
        //      black screen.
        //
        //   3) DUNGEONS AND BOSS ARENAS NEVER STRIPPED - even if they appear in
        //      the allowlist (defense in depth).
        [HarmonyPatch(typeof(Location), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // run after Balrond has already injected
        public static void Location_Awake_StripBalrondWeather(Location __instance)
        {
            // Gate 1 - Balrond not loaded? Do nothing.  Even on installs where
            // this mod has nothing to do, the [HarmonyPatch] attribute would
            // otherwise still fire this body for every Location.Awake.
            if (!_balrondLoaded) return;

            try
            {
                if (__instance == null) return;

                // Gate 3 - never touch dungeons.
                if (__instance.GetComponentInChildren<DungeonGenerator>(includeInactive: true) != null)
                    return;

                // Gate 3 - never touch boss arenas.
                string prefab = __instance.gameObject.name;
                if (IsBossArena(prefab))
                    return;

                // Gate 2 - narrow allowlist.  Only strip from prefabs we have
                // actually confirmed Balrond mis-injects into.  If you spot a
                // new vanilla prefab Balrond breaks, add it here - DO NOT widen
                // this back to "everything".
                if (!IsKnownBalrondAffectedPrefab(prefab))
                    return;

                // Strip every EnvZone in the hierarchy.
                var envZones = __instance.GetComponentsInChildren<EnvZone>(includeInactive: true);
                if (envZones != null && envZones.Length > 0)
                {
                    foreach (var ez in envZones)
                    {
                        if (ez != null)
                            UnityEngine.Object.Destroy(ez);
                    }
                    Debug.Log($"[BalrondCompat] Stripped {envZones.Length} EnvZone(s) from location '{prefab}'");
                }

                // Strip every MusicLocation in the hierarchy.
                var musicLocs = __instance.GetComponentsInChildren<MusicLocation>(includeInactive: true);
                if (musicLocs != null && musicLocs.Length > 0)
                {
                    foreach (var ml in musicLocs)
                    {
                        if (ml != null)
                            UnityEngine.Object.Destroy(ml);
                    }
                    Debug.Log($"[BalrondCompat] Stripped {musicLocs.Length} MusicLocation(s) from location '{prefab}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BalrondCompat] Location_Awake_StripBalrondWeather error: {ex.Message}");
            }
        }

        private static readonly HashSet<string> _bossArenaPrefabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Eikthyrnir",
            "GDKing",
            "Bonemass",
            "Dragonqueen",
            "GoblinKing",
            "Mistlands_DvergrBossEntrance1",
            "FaderLocation",
        };

        private static bool IsBossArena(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            // Strip Unity "(Clone)" suffix if present.
            int paren = prefabName.IndexOf('(');
            string clean = paren > 0 ? prefabName.Substring(0, paren).TrimEnd() : prefabName;
            return _bossArenaPrefabs.Contains(clean);
        }

        // -- Known-Balrond-affected prefab allowlist --------------------------
        // These are vanilla location prefab name PREFIXES we have actually
        // observed Balrond Amazing Nature mis-inject EnvZone or MusicLocation
        // into.  Keep this list NARROW.  Each entry should only be added after
        // confirming that:
        //   (a) Balrond is the one adding the component (vanilla doesn't ship
        //       this prefab with EnvZone/MusicLocation), AND
        //   (b) leaving Balrond's component in place causes a real visible
        //       weather/music problem at that location.
        //
        // Anything not in this list is left completely alone - vanilla and
        // every other mod retain full control over location weather/music.
        private static readonly string[] _balrondAffectedPrefabPrefixes = new[]
        {
            // Wrecks / abandoned cosmetic POIs
            "ShipWreck",
            "AbandonedLogCabin",
            "WoodHouse",
            // Plain ruin variants (NOT "Ruin_dungeon" etc - prefix match catches the cosmetic ones)
            "Ruin1",
            "Ruin2",
            "Ruin3",
            "StoneTower",      // surface ruin towers (the underground StoneTowerLoc1_dungeon variant is a dungeon and is filtered by the DungeonGenerator gate)
            // Forest decorative POIs
            "FireHole",
            "InfestedTree",
            // Fishing / dock surface POIs
            "Fishingvillage",
            // Lore POIs - vanilla ships these without EnvZone/MusicLocation; anything
            // Balrond injects here is wrong.  Covers every biome/boss variant via the
            // common name prefix.
            "Runestone",       // Runestone_Boars, _Greydwarfs, _Elder, _Bonemass, _Mountains, _Plains, _Mistlands, _Crypts, _FaderArrival, etc.
            "Vegvisir",        // Vegvisir_Eikthyr, _GDKing, _Bonemass, _DragonQueen, _GoblinKing, _Queen, _Fader
            "BossStone",       // BossStone_Eikthyr, _TheElder, _Bonemass, _DragonQueen, _Yagluth, _TheQueen, _Fader (the trophy altar at spawn)
            "StoneCircle",     // surface stone-circle POIs (no dungeon variant)
        };

        private static bool IsKnownBalrondAffectedPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return false;
            // Strip Unity "(Clone)" suffix.
            int paren = prefabName.IndexOf('(');
            string clean = paren > 0 ? prefabName.Substring(0, paren).TrimEnd() : prefabName;

            for (int i = 0; i < _balrondAffectedPrefabPrefixes.Length; i++)
            {
                if (clean.StartsWith(_balrondAffectedPrefabPrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /*
        // DISABLED - All unpatch methods commented out until we verify the new version

        private static void UnpatchAddKnownLocationName(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Player), "AddKnownLocationName");
                if (target == null)
                {
                    Debug.LogWarning("[BalrondCompat] Player.AddKnownLocationName not found");
                    return;
                }

                if (TryUnpatchBalrondPrefix(harmony, target, out string owner))
                    Debug.Log($"[BalrondCompat] Removed Balrond's AddKnownLocationName prefix ({owner})");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BalrondCompat] Failed to unpatch AddKnownLocationName: {ex.Message}");
            }
        }

        private static void UnpatchPlayerUpdateStatsSpam(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Player), "UpdateStats", new[] { typeof(float) });
                if (target == null)
                {
                    Debug.LogWarning("[BalrondCompat] Player.UpdateStats(float) not found");
                    return;
                }

                if (TryUnpatchBalrondPostfix(harmony, target, out string owner))
                    Debug.Log($"[BalrondCompat] Removed Balrond's Player.UpdateStats postfix ({owner}) - fixes SE_SitHeal message spam");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BalrondCompat] Failed to unpatch Player.UpdateStats: {ex.Message}");
            }
        }

        private static void UnpatchPlayerEnvStatusSpam(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Player), "UpdateEnvStatusEffects");
                if (target == null)
                {
                    Debug.LogWarning("[BalrondCompat] Player.UpdateEnvStatusEffects not found");
                    return;
                }

                if (TryUnpatchBalrondPostfix(harmony, target, out string owner))
                    Debug.Log($"[BalrondCompat] Removed Balrond's Player.UpdateEnvStatusEffects postfix ({owner}) - fixes env status message spam");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BalrondCompat] Failed to unpatch Player.UpdateEnvStatusEffects: {ex.Message}");
            }
        }

        private static bool TryUnpatchBalrondPrefix(Harmony harmony, MethodBase target, out string owner)
        {
            owner = null;
            var patches = Harmony.GetPatchInfo(target);
            if (patches == null) return false;

            foreach (var prefix in patches.Prefixes)
            {
                if (IsBalrondPatch(prefix))
                {
                    owner = prefix.owner ?? prefix.PatchMethod?.DeclaringType?.FullName ?? "unknown";
                    harmony.Unpatch(target, prefix.PatchMethod);
                    return true;
                }
            }
            return false;
        }

        private static bool TryUnpatchBalrondPostfix(Harmony harmony, MethodBase target, out string owner)
        {
            owner = null;
            var patches = Harmony.GetPatchInfo(target);
            if (patches == null) return false;

            foreach (var postfix in patches.Postfixes)
            {
                if (IsBalrondPatch(postfix))
                {
                    owner = postfix.owner ?? postfix.PatchMethod?.DeclaringType?.FullName ?? "unknown";
                    harmony.Unpatch(target, postfix.PatchMethod);
                    return true;
                }
            }
            return false;
        }

        private static bool IsBalrondPatch(Patch patch)
        {
            if (patch.owner != null &&
                patch.owner.IndexOf("balrond", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            string declTypeName = patch.PatchMethod?.DeclaringType?.FullName ?? "";
            if (declTypeName.IndexOf("Balrond", StringComparison.OrdinalIgnoreCase) >= 0 ||
                declTypeName.IndexOf("AmazingNature", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }
        */
    }
}

