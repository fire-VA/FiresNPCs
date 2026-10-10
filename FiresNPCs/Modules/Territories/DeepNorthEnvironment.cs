using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Territories
{
    /// <summary>
    /// Registers a custom Deep North environment so forced biomes have an actual env to use (freezing theme).
    /// </summary>
    [HarmonyPatch]
internal static class DeepNorthEnvironment
    {
        private const string EnvName = "DeepNorth_Freezing";
        private static Material _crystalLeviathanMat;
        private static bool _envRegistered;

        // 10-05 ([ghost], Fire: "the deep north might be from fires npcs"): this used to APPEND its own "DeepNorth_Setup"
        // biome setup in EnvMan.Awake. The location lists add the real Deep North setup ("Deep north", with its music and
        // weathers) later, in ZoneSystem.Start, and vanilla keeps only the FIRST setup per biome. So the real one was thrown
        // away (vanilla's ERROR "EnvSetup Deep north sets music, but is already defined previously in DeepNorth_Setup") and
        // the Deep North ran on this single freezing env with no music. Now: the env is registered in Awake (every EnvMan,
        // every world), and our entry JOINS whichever Deep North setup the game appends. A setup of our own is made only if
        // none exists once ZoneSystem.Start has added every location list.
        [HarmonyPatch(typeof(EnvMan), "Awake")]
        [HarmonyPostfix]
        private static void EnvMan_Awake_Postfix(EnvMan __instance)
        {
            try
            {
                if (__instance == null) return;
                var env = __instance.m_environments.FirstOrDefault(e => e != null && e.m_name == EnvName) ?? BuildEnv();
                if (env.m_auroraColors == null) env.m_auroraColors = AuroraFrom(__instance);
                if (!__instance.m_environments.Any(e => e != null && e.m_name == EnvName)) __instance.AppendEnvironment(env);
                _envRegistered = true;
                // A Deep North setup that is already there (vanilla scene data) gets our entry now.
                foreach (var b in __instance.m_biomes) if (b != null && b.m_biome == Heightmap.Biome.DeepNorth) AddOurEntry(__instance, b);
            }
            catch (Exception e) { Debug.LogWarning("[DeepNorthEnvironment] env registration failed: " + e.Message); }
        }

        // Every Deep North setup the game appends (the location lists' "Deep north") gets our entry; it stays the first one.
        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.AppendBiomeSetup))]
        [HarmonyPostfix]
        private static void EnvMan_AppendBiomeSetup_Postfix(EnvMan __instance, BiomeEnvSetup __0)   // positional: the parameter name is vanilla's
        {
            try { if (__instance != null && __0 != null && __0.m_biome == Heightmap.Biome.DeepNorth) AddOurEntry(__instance, __0); }
            catch (Exception e) { Debug.LogWarning("[DeepNorthEnvironment] joining the Deep North setup failed: " + e.Message); }
        }

        // Only when NO Deep North setup exists after every location list is in: then ours, so forced biomes still have an env.
        [HarmonyPatch(typeof(ZoneSystem), "Start")]
        [HarmonyPostfix]
        private static void ZoneSystem_Start_Postfix()
        {
            try
            {
                var envMan = EnvMan.instance;
                if (envMan == null || envMan.m_biomes.Any(b => b != null && b.m_biome == Heightmap.Biome.DeepNorth)) return;
                var own = new BiomeEnvSetup { m_name = "DeepNorth_Setup", m_biome = Heightmap.Biome.DeepNorth, m_environments = new List<EnvEntry>() };
                envMan.AppendBiomeSetup(own);   // the postfix above adds our entry
            }
            catch (Exception e) { Debug.LogWarning("[DeepNorthEnvironment] fallback Deep North setup failed: " + e.Message); }
        }

        private static void AddOurEntry(EnvMan envMan, BiomeEnvSetup setup)
        {
            if (setup.m_environments == null) setup.m_environments = new List<EnvEntry>();
            if (setup.m_environments.Exists(e => e != null && e.m_environment == EnvName)) return;
            var env = envMan.m_environments.FirstOrDefault(e => e != null && e.m_name == EnvName);
            if (env == null) return;
            setup.m_environments.Add(new EnvEntry { m_environment = EnvName, m_weight = 1f, m_ashlandsOverride = false, m_deepnorthOverride = true, m_env = env });
        }

  private static bool IsInDeepNorthArea(Vector3 pos)
     {
       try
            {
        if (WorldGenerator.IsDeepnorth(pos.x, pos.z)) return true;
         Heightmap.Biome biome = Heightmap.Biome.None;
    try { biome = Heightmap.FindBiome(pos); } catch { }
     return biome == Heightmap.Biome.DeepNorth;
}
    catch { return false; }
}

        [HarmonyPatch(typeof(Leviathan), "Awake")]
        [HarmonyPostfix]
        private static void Leviathan_Awake_Postfix(Leviathan __instance)
    {
try
      {
        if (__instance == null) return;
 var pos = __instance.transform.position;
           if (!IsInDeepNorthArea(pos)) return;
                ApplyCrystalLeviathan(__instance.gameObject);
   }
    catch { }
        }

        private static void ApplyCrystalLeviathan(GameObject go)
   {
        if (go == null) return;
    if (_crystalLeviathanMat == null)
            {
      // create a bluish crystal-like material based on any renderer's first material
                var sampleRenderer = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.sharedMaterial != null);
       var baseMat = sampleRenderer != null ? sampleRenderer.sharedMaterial : null;
 _crystalLeviathanMat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Standard"));
          _crystalLeviathanMat.name = "DeepNorth_Leviathan_Crystal";
       _crystalLeviathanMat.color = new Color(0.65f, 0.85f, 1f, 1f);
              _crystalLeviathanMat.SetColor("_EmissionColor", new Color(0.2f, 0.4f, 0.6f));
      _crystalLeviathanMat.EnableKeyword("_EMISSION");
        _crystalLeviathanMat.SetFloat("_Glossiness", 0.65f);
  _crystalLeviathanMat.SetFloat("_Metallic", 0.2f);
            }

      var renderers = go.GetComponentsInChildren<Renderer>(true);
   foreach (var r in renderers)
          {
                if (r == null) continue;
  var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                for (int i = 0; i < mats.Length; i++) mats[i] = _crystalLeviathanMat;
  r.sharedMaterials = mats;
     }
        }

        // Valheim 1.0 lerps EnvSetup.m_auroraColors every FixedUpdate (EnvMan.InterpolateEnvironment); this hand-built env
        // left it null, so Utils.Gradient.Lerp threw a NullReferenceException there every frame (H1, 2026-10-01: 3,891 on the
        // client in four minutes). It borrows a vanilla environment's aurora; with none, an invisible one.
        private static Gradient AuroraFrom(EnvMan envMan)
        {
            var source = envMan.m_environments.FirstOrDefault(e => e != null && e.m_name != EnvName && e.m_auroraColors != null);
            if (source != null) return source.m_auroraColors;
            var none = new Gradient();
            none.SetKeys(new[] { new GradientColorKey(Color.black, 0f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) });
            return none;
        }

        private static EnvSetup BuildEnv()
        {
         // A cold, misty, low-light environment
  var env = new EnvSetup
            {
          m_name = EnvName,
                m_default = false,
    m_isWet = true,
          m_isFreezing = true,
 m_isFreezingAtNight = true,
                m_isCold = true,
   m_isColdAtNight = true,
   m_alwaysDark = false,
          m_ambColorDay = new Color(0.45f, 0.55f, 0.65f),
    m_ambColorNight = new Color(0.18f, 0.22f, 0.28f),
          m_fogColorDay = new Color(0.6f, 0.7f, 0.8f),
            m_fogColorNight = new Color(0.2f, 0.25f, 0.32f),
         m_fogColorMorning = new Color(0.55f, 0.65f, 0.75f),
   m_fogColorEvening = new Color(0.5f, 0.6f, 0.7f),
      m_fogColorSunDay = new Color(0.7f, 0.8f, 0.9f),
        m_fogColorSunNight = new Color(0.25f, 0.3f, 0.4f),
    m_fogColorSunMorning = new Color(0.6f, 0.7f, 0.8f),
       m_fogColorSunEvening = new Color(0.55f, 0.65f, 0.75f),
          m_fogDensityDay = 0.025f,
    m_fogDensityNight = 0.04f,
            m_fogDensityMorning = 0.03f,
         m_fogDensityEvening = 0.032f,
            m_sunColorDay = new Color(0.9f, 0.95f, 1f),
           m_sunColorNight = new Color(0.35f, 0.4f, 0.5f),
 m_sunColorMorning = new Color(0.8f, 0.85f, 0.95f),
   m_sunColorEvening = new Color(0.75f, 0.8f, 0.9f),
      m_lightIntensityDay = 0.8f,
           m_lightIntensityNight = 0.15f,
      m_sunAngle = 12f,
    m_windMin = 0.5f,
       m_windMax = 1f,
         m_rainCloudAlpha = 0.7f,
   m_ambientVol = 0.35f,
   m_musicDay = "MountainDay",
         m_musicNight = "MountainNight",
                m_musicMorning = "MountainMorning",
              m_musicEvening = "MountainEvening",
 m_psystems = Array.Empty<GameObject>(),
     m_psystemsOutsideOnly = false,
 m_envObject = null
     };
return env;
        }
    }
}
