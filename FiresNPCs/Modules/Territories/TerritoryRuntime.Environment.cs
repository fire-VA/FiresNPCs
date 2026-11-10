using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresCore.Lifecycle;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    internal static partial class TerritoryRuntime
    {
        private static string _lastForcedEnv;
        private static bool _windForced;


   private static float? _origSnowHeight;

      private static readonly HashSet<string> _paintedTerritoryHeightmaps = new();
        private static readonly HashSet<string> _biomePaintedTerritories = new();
        private static readonly Dictionary<int, float> _biomeRefreshTimestamps = new();
        private static bool _isDedicatedServer;
        private static bool _isDedicatedServerChecked;

   private static readonly HashSet<string> _snowColoredHeightmaps = new();
   private static readonly HashSet<string> _snowRefreshedTerritoryNames = new();

        // PERFORMANCE: Flags and cached data
        private static bool _hasAnyForceBiomeTerritories = false;
        private static bool _hasAnyCustomPaintTerritories = false;
        private static bool _hasAnySnowMaskOnlyTerritories = false;

        // Snow mask tracking - separate from biome forcing
        private static readonly Dictionary<Heightmap, float> _snowMaskedHeightmaps = new();
        private static float _snowMaskBoundsMinX = float.MaxValue;
        private static float _snowMaskBoundsMaxX = float.MinValue;
        private static float _snowMaskBoundsMinZ = float.MaxValue;
        private static float _snowMaskBoundsMaxZ = float.MinValue;
        private static int _territoryCacheVersion = -1;
        private static int _currentTerritoriesVersion = 0;

      // PERFORMANCE: Cached territories with pre-computed bounds for fast spatial filtering
        // Now includes ALL ForceBiome territories, not just snow ones
        private static ForceBiomeTerritoryData[] _cachedForceBiomeTerritories = null;
        private static float _forceBiomeBoundsMinX = float.MaxValue;
        private static float _forceBiomeBoundsMaxX = float.MinValue;
        private static float _forceBiomeBoundsMinZ = float.MaxValue;
        private static float _forceBiomeBoundsMaxZ = float.MinValue;

     // Cached reflection
     private static MethodInfo _rebuildRenderMeshMethod;
  private static FieldInfo _cornerBiomesField;
        private static FieldInfo _isDistantLodField;
        private static FieldInfo _renderMeshField;
        private static FieldInfo _tempColorsField;
      private static bool _reflectionCached;

      // Cached reflection for smooth environment transitions
        private static MethodInfo _queueEnvironmentByNameMethod;
        private static FieldInfo _forceEnvField;
      private static bool _envManReflectionCached;

        /// <summary>
/// Pre-computed territory data for fast lookups - supports all biome types
        /// </summary>
        private struct ForceBiomeTerritoryData
        {
       public Territory Territory;
    public float MinX, MaxX, MinZ, MaxZ; // AABB for fast rejection
            public Heightmap.Biome ForcedBiome;
  public bool RequiresSnowShader;
            public bool IsSnowMaskOnly; // True if SnowMask flag without ForceBiome - visual only, no terrain height changes
 }

        internal static void InvalidateSnowTerritoryCache()
 {
            _currentTerritoriesVersion++;
            _cachedSnowMaskTerritories = null;
            // The ForceBiome cache was only rebuilt from the Heightmap.Regenerate postfix; on a join the territories arrive AFTER the ground
            // around the player is built, so nothing rebuilt it (every ForceBiome patch fast-exits on _hasAnyForceBiomeTerritories) until a
            // re-save regenerated the heightmaps. It is rebuilt here, at once, whenever the territory list changes (sync received, ledger load /
            // edit, API boxes).
            try { EnsureTerritoryCacheValid(); }
            catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] ForceBiome cache rebuild failed: " + ex.Message); }
            try { s_biomeCacheValid = false; } catch { }
            RefreshDeepGround();
        }

        // 0.0.774 ([seasons] for Fire's deep snow): FAT colours ground Deep North where Core's SnowField.DeepGroundAt is true, which
        // includes Winter / ForceBiome territories (TerritorySnowFeed). A territory made, edited or removed at runtime kept its old ground
        // colour until the chunks rebuilt: tell Core to drop its base cache, then FAT to re-colour, both by name (an older Core / FAT =
        // no-op). Without Core's InvalidateBaseCache its cache catches up within 10 s, so FAT is asked again 10 s later.
        private static System.Reflection.MethodInfo s_coreInvalidate, s_fatRefresh;
        private static bool s_deepLooked;
        private static void RefreshDeepGround()
        {
            try
            {
                if (!s_deepLooked)
                {
                    s_deepLooked = true;
                    s_coreInvalidate = AccessTools.TypeByName("FiresCore.Snow.SnowField")?.GetMethod("InvalidateBaseCache", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    // 0.0.808: FAT is optional, so it is found without HarmonyX's "Could not find type" warning
                    s_fatRefresh = global::FiresRPGmaker.Utilities.QuietTypes.Find("VerdantsAscent.FatApi")?.GetMethod("RefreshBiomeOverrides", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                }
                if (s_fatRefresh == null) return;
                s_coreInvalidate?.Invoke(null, null);
                s_fatRefresh.Invoke(null, null);
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] deep ground refresh: Core base cache {(s_coreInvalidate != null ? "dropped" : "not found (FAT asked again in 10 s)")}, FAT re-colour asked");
                if (s_coreInvalidate == null && ZNetScene.instance != null) ZNetScene.instance.StartCoroutine(RefreshFatLater());
            }
            catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] deep ground refresh failed: " + ex.Message); }
        }

        private static IEnumerator RefreshFatLater()
        {
            yield return new WaitForSeconds(10f);
            try { s_fatRefresh?.Invoke(null, null); } catch { }
        }

    private static void EnsureReflectionCached()
        {
            if (_reflectionCached) return;
_reflectionCached = true;
            try
     {
          const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
          const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
 _rebuildRenderMeshMethod = typeof(Heightmap).GetMethod("RebuildRenderMesh", flags);
        _cornerBiomesField = typeof(Heightmap).GetField("m_cornerBiomes", flags);
        _isDistantLodField = typeof(Heightmap).GetField("m_isDistantLod", flags);
        _renderMeshField = typeof(Heightmap).GetField("m_renderMesh", flags);
        _tempColorsField = typeof(Heightmap).GetField("s_tempColors", staticFlags);
   }
            catch { }
        }

        /// <summary>
        /// Cache reflection for EnvMan to enable smooth environment transitions.
     /// </summary>
        private static void EnsureEnvManReflectionCached()
        {
         if (_envManReflectionCached) return;
            _envManReflectionCached = true;
          try
       {
     const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        // QueueEnvironment(string name) is private
           _queueEnvironmentByNameMethod = typeof(EnvMan).GetMethod("QueueEnvironment", flags, null, new[] { typeof(string) }, null);
       // m_forceEnv is private
    _forceEnvField = typeof(EnvMan).GetField("m_forceEnv", flags);
          }
   catch { }
 }

        /// <summary>
        /// Smoothly transitions to the specified environment using vanilla's interpolation system.
        /// This queues the environment change which triggers the m_transitionDuration interpolation.
        /// </summary>
        private static void SetEnvironmentSmooth(string envName)
        {
            if (EnvMan.instance == null) return;
         if (string.IsNullOrEmpty(envName)) return;

    EnsureEnvManReflectionCached();

     try
      {
  // First, clear any forced environment so the queue system works
        if (_forceEnvField != null)
                {
                  string currentForce = _forceEnvField.GetValue(EnvMan.instance) as string;
         if (!string.IsNullOrEmpty(currentForce))
     {
_forceEnvField.SetValue(EnvMan.instance, string.Empty);
  }
      }

                // Now queue the environment for smooth transition
     if (_queueEnvironmentByNameMethod != null)
 {
          _queueEnvironmentByNameMethod.Invoke(EnvMan.instance, new object[] { envName });
      }
          else
        {
 // Fallback to SetForceEnvironment if reflection failed
          EnvMan.instance.SetForceEnvironment(envName);
      }
            }
            catch
  {
      // Fallback to SetForceEnvironment
          EnvMan.instance.SetForceEnvironment(envName);
            }
        }

        /// <summary>
        /// Clears any forced environment and lets vanilla handle the biome-based environment smoothly.
 /// </summary>
        private static void ClearForcedEnvironmentSmooth()
      {
            if (EnvMan.instance == null) return;

            EnsureEnvManReflectionCached();

     try
   {
       if (_forceEnvField != null)
    {
   _forceEnvField.SetValue(EnvMan.instance, string.Empty);
  }
   else
                {
       EnvMan.instance.SetForceEnvironment(string.Empty);
                }
            }
            catch
            {
          EnvMan.instance.SetForceEnvironment(string.Empty);
   }
     }

 private static IReadOnlyList<Territory> GetCurrentTerritories()
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
          return TerritoryLedger.Territories;
      return TerritoryApi.WithExternal(TerritorySyncedState.ClientTerritories); // API boxes on clients
        }

        /// <summary>
     /// Light-weight cache validation - builds cached ForceBiome territory data for ALL biome types
        /// </summary>
        private static void EnsureTerritoryCacheValid()
   {
  if (_territoryCacheVersion == _currentTerritoriesVersion)
    return;

        _territoryCacheVersion = _currentTerritoriesVersion;
        ProbeCacheBuiltAt = Time.realtimeSinceStartup;   // diagnostic
  _hasAnyForceBiomeTerritories = false;
  _hasAnyCustomPaintTerritories = false;

            // Reset bounds
          _forceBiomeBoundsMinX = float.MaxValue;
          _forceBiomeBoundsMaxX = float.MinValue;
    _forceBiomeBoundsMinZ = float.MaxValue;
            _forceBiomeBoundsMaxZ = float.MinValue;

         var territories = GetCurrentTerritories();
       if (territories == null || territories.Count == 0)
      {
                _cachedForceBiomeTerritories = Array.Empty<ForceBiomeTerritoryData>();
   return;
            }

            var biomeList = new List<ForceBiomeTerritoryData>(territories.Count);

            // Reset snow mask bounds
            _snowMaskBoundsMinX = float.MaxValue;
            _snowMaskBoundsMaxX = float.MinValue;
            _snowMaskBoundsMinZ = float.MaxValue;
            _snowMaskBoundsMaxZ = float.MinValue;

     foreach (var terr in territories)
    {
                if (terr == null) continue;

    if (terr.Flags.HasFlag(TerritoryFlags.CustomPaint))
   _hasAnyCustomPaintTerritories = true;

          // Check if this territory forces a biome (any biome, not just snow)
      bool hasSnowMask = terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.SnowMask);
       bool hasForceBiome = terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0;

    // SnowMask without ForceBiome = visual only (no biome changes at all)
    bool isSnowMaskOnly = hasSnowMask && !hasForceBiome;

    // Track snow mask only territories separately - they don't force biomes
    if (isSnowMaskOnly)
    {
        _hasAnySnowMaskOnlyTerritories = true;
        var smBounds = GetTerritoryBounds(terr);
        if (smBounds.min.x < _snowMaskBoundsMinX) _snowMaskBoundsMinX = smBounds.min.x;
        if (smBounds.max.x > _snowMaskBoundsMaxX) _snowMaskBoundsMaxX = smBounds.max.x;
        if (smBounds.min.y < _snowMaskBoundsMinZ) _snowMaskBoundsMinZ = smBounds.min.y;
        if (smBounds.max.y > _snowMaskBoundsMaxZ) _snowMaskBoundsMaxZ = smBounds.max.y;
        // Don't add to biome list - SnowMask only applies visual effects, not biome changes
        continue;
    }

    // Only actual ForceBiome territories go in the biome list
    if (!hasForceBiome) continue;

         _hasAnyForceBiomeTerritories = true;

// Determine the forced biome
       Heightmap.Biome forcedBiome = (Heightmap.Biome)terr.OverridenBiome;

             // Pre-compute AABB bounds for this territory
        var bounds = GetTerritoryBounds(terr);
        var data = new ForceBiomeTerritoryData
        {
Territory = terr,
      MinX = bounds.min.x,
     MaxX = bounds.max.x,
    MinZ = bounds.min.y,
        MaxZ = bounds.max.y,
 ForcedBiome = forcedBiome,
        RequiresSnowShader = BiomeRequiresSnowShader(forcedBiome),
     IsSnowMaskOnly = false // No longer used - SnowMask-only territories are handled separately
   };
       biomeList.Add(data);

       // Update global bounds
       if (data.MinX < _forceBiomeBoundsMinX) _forceBiomeBoundsMinX = data.MinX;
   if (data.MaxX > _forceBiomeBoundsMaxX) _forceBiomeBoundsMaxX = data.MaxX;
       if (data.MinZ < _forceBiomeBoundsMinZ) _forceBiomeBoundsMinZ = data.MinZ;
      if (data.MaxZ > _forceBiomeBoundsMaxZ) _forceBiomeBoundsMaxZ = data.MaxZ;
    }

            // FAT asks ForcedSectorAt from terrain threads, which may only READ the sector table: build every sector here
            foreach (var d in biomeList)
                if (!d.IsSnowMaskOnly && d.Territory != null) { ForcedSector(d.ForcedBiome); ForcedSector(d.ForcedBiome, d.Territory.OverridenAltBiome); }
            _cachedForceBiomeTerritories = biomeList.ToArray();
            try { TerritoryBiomeBlend.Rebuild(biomeList.Select(x => x.Territory)); } catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[BiomeBlend] rebuild failed: " + ex.Message); }
            if (TerritoryBiomeBlend.SuperFlatHooked) global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] superflat content area registered: {biomeList.Count} forced territor{(biomeList.Count == 1 ? "y" : "ies")}");
     }

        private static bool IsDedicatedServer()
        {
   if (!_isDedicatedServerChecked)
   {
            _isDedicatedServerChecked = true;
                _isDedicatedServer = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
      }
        return _isDedicatedServer;
        }

        private static TerrainModifier.PaintType? GetBiomePaintType(Heightmap.Biome biome)
        {
   switch (biome)
    {
              case Heightmap.Biome.Meadows:
case Heightmap.Biome.Mountain:
       case Heightmap.Biome.DeepNorth:
            case Heightmap.Biome.Plains:
           return TerrainModifier.PaintType.Reset;
  case Heightmap.Biome.BlackForest:
       case Heightmap.Biome.Swamp:
    case Heightmap.Biome.Mistlands:
    return TerrainModifier.PaintType.Dirt;
          case Heightmap.Biome.AshLands:
   return TerrainModifier.PaintType.Paved;
      default:
          return TerrainModifier.PaintType.Reset;
  }
        }

     private static bool BiomeRequiresSnowShader(Heightmap.Biome biome)
        {
            return biome == Heightmap.Biome.DeepNorth || biome == Heightmap.Biome.Mountain;
        }

        private static (Vector2 min, Vector2 max) GetTerritoryBounds(Territory terr)
        {
      if (terr == null) return (Vector2.zero, Vector2.zero);

  float minX, maxX, minZ, maxZ;
            if (terr.Shape == TerritoryShape.Polygon && terr.IsPolygon) return (terr.PolyMin, terr.PolyMax);
   switch (terr.Shape)
       {
         case TerritoryShape.Rectangle:
      float x0 = terr.X != null && terr.X.Length > 0 ? terr.X[0] : 0f;
         float x1 = terr.X != null && terr.X.Length > 1 ? x0 + terr.X[1] : x0; // X[1] is the width
    float z0 = terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : 0f;
  float z1 = terr.Y != null && terr.Y.Length > 1 ? z0 + terr.Y[1] : z0; // Y[1] is the depth
         minX = x0 < x1 ? x0 : x1;
        maxX = x0 > x1 ? x0 : x1;
        minZ = z0 < z1 ? z0 : z1;
         maxZ = z0 > z1 ? z0 : z1;
             break;
     default:
        float cx = terr.X != null && terr.X.Length > 0 ? terr.X[0] : 0f;
         float cz = terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : 0f;
     float r = terr.Radius > 0 ? terr.Radius : 2f;
         minX = cx - r;
       maxX = cx + r;
    minZ = cz - r;
       maxZ = cz + r;
break;
    }
            return (new Vector2(minX, minZ), new Vector2(maxX, maxZ));
     }

        private static void TryApplyTerritoryPaint(Territory terr, Vector3 refPos, Heightmap specificHeightmap = null)
      {
        if (terr == null || ZNet.instance == null || IsDedicatedServer()) return;

     if (specificHeightmap == null)
       {
 string territoryKey = $"{terr.UID}:attempted";
         if (_paintedTerritoryHeightmaps.Contains(territoryKey))
return;
            }

 try
         {
     float cx = refPos.x;
         float cz = refPos.z;
 float radius = 2f;

      if (terr.Shape == TerritoryShape.Polygon && terr.IsPolygon)   // Centre of its bounds, radius = half the bounds diagonal
      {
          var pc = terr.Center2D(); cx = pc.x; cz = pc.y;
          var ps = terr.PolyMax - terr.PolyMin;
          radius = Mathf.Max(2f, ps.magnitude * 0.5f);
      }
      else       if (terr.Shape == TerritoryShape.Rectangle)
          {
      float x0 = terr.X != null && terr.X.Length > 0 ? terr.X[0] : 0f;
        float x1 = terr.X != null && terr.X.Length > 1 ? x0 + terr.X[1] : x0; // X[1] is the width
    float z0 = terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : 0f;
         float z1 = terr.Y != null && terr.Y.Length > 1 ? z0 + terr.Y[1] : z0; // Y[1] is the depth
   float minX = x0 < x1 ? x0 : x1;
         float maxX = x0 > x1 ? x0 : x1;
        float minZ = z0 < z1 ? z0 : z1;
         float maxZ = z0 > z1 ? z0 : z1;
        cx = (minX + maxX) * 0.5f;
          cz = (minZ + maxZ) * 0.5f;
         float width = maxX - minX;
      float height = maxZ - minZ;
  radius = (float)Math.Max(2f, Math.Sqrt(width * width + height * height) * 0.5f);
  }
     else
{
               cx = terr.X != null && terr.X.Length > 0 ? terr.X[0] : cx;
         cz = terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : cz;
  radius = terr.Radius > 0 ? terr.Radius : 2f;
             }

                var center = new Vector3(cx, refPos.y, cz);
                // 0.0.772 (halo ring): vanilla TerrainModifier paints the heightmap at a world-Y; a ring floor is FAT's voxel terrain, so it stands down there
                if (global::FiresRPGmaker.Utilities.RingUp.OnRing(center)) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{terr.Name}' ground paint skipped: on a halo ring (FAT voxel floor, no heightmap)"); return; }
     try { Heightmap.GetHeight(center, out var h); center.y = h; } catch { }

       var paint = TerrainModifier.PaintType.Reset;
             switch (terr.PaintGround)
       {
        case PaintType.Paved: paint = TerrainModifier.PaintType.Paved; break;
       case PaintType.Cultivated: paint = TerrainModifier.PaintType.Cultivate; break;
         case PaintType.Dirt: paint = TerrainModifier.PaintType.Dirt; break;
     case PaintType.ClearVegetation: paint = TerrainModifier.PaintType.ClearVegetation; break;
      default: paint = TerrainModifier.PaintType.Reset; break;
       }

                // Ground that isn't loaded yet is retried every 2 s while the player is inside; "attempted" is only
                // recorded once every heightmap under the area has been painted.
                if (specificHeightmap == null && Time.time < _nextPaintRetry) return;
                _nextPaintRetry = Time.time + 2f;

                var hmaps = new List<Heightmap>();
            if (specificHeightmap != null)
  hmaps.Add(specificHeightmap);
         else
        {
         Heightmap.FindHeightmap(center, radius + 5f, hmaps);
      }

       bool allPainted = true;
       foreach (var hm in hmaps)
        {
       if (hm == null) continue;

       string key = $"{terr.UID}:{hm.transform.position.x:F0}:{hm.transform.position.z:F0}";
    if (_paintedTerritoryHeightmaps.Contains(key))
         continue;

                    //  paint untouched ground too, through vanilla's own GetAndCreateTerrainCompiler (what the hoe and
                    // the cultivator use), but only once the zone is fully loaded. ZNetScene.IsAreaReady is the check
                    // vanilla's teleport waits on: the zone is loaded and every object in it has spawned, so the area's
                    // existing terrain compiler is already found and never duplicated. Before, only ground that had been
                    // edited already got painted (an existing TerrainComp at the territory centre).
                    if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(hm.transform.position))
                    {
                        allPainted = false;
                        continue;
                    }
                    TerrainComp tc = hm.GetAndCreateTerrainCompiler();
                    if (tc == null) { allPainted = false; continue; }

    try
        {
       var go = new GameObject("TerritoryAutoPaint");
  go.transform.position = center;

   bool prevForceDisable = TerrainOp.m_forceDisableTerrainOps;
     TerrainOp.m_forceDisableTerrainOps = true;

      TerrainOp top = null;
                 try { top = go.AddComponent<TerrainOp>(); }
           catch
                 {
      TerrainOp.m_forceDisableTerrainOps = prevForceDisable;
    NetworkObjectHelper.SafeDestroy(go);
          continue;
           }

     TerrainOp.m_forceDisableTerrainOps = prevForceDisable;
                   if (top == null) { NetworkObjectHelper.SafeDestroy(go); continue; }
         if (top.m_settings == null) top.m_settings = new TerrainOp.Settings();

            top.m_settings.m_paintCleared = true;
            top.m_settings.m_paintHeightCheck = false;
            top.m_settings.m_paintRadius = radius;
      top.m_settings.m_paintType = paint;
        top.m_settings.m_level = false;
         top.m_settings.m_raise = false;
       top.m_settings.m_smooth = false;

         tc.ApplyOperation(top);
   _paintedTerritoryHeightmaps.Add(key);
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{terr.Name}' CustomPaint {paint} r={radius:0} painted the heightmap at ({hm.transform.position.x:0},{hm.transform.position.z:0})");

     NetworkObjectHelper.SafeDestroy(go);
       }
     catch { }
        }
                if (specificHeightmap == null && allPainted)
                    _paintedTerritoryHeightmaps.Add($"{terr.UID}:attempted");
            }
            catch { }
        }

        private static float _nextPaintRetry;

        private static void TryApplyBiomePaint(Territory terr, Vector3 refPos)
        {
       if (terr == null || ZNet.instance == null || IsDedicatedServer()) return;
       if (terr.OverridenBiome == 0) return;

         var biome = (Heightmap.Biome)terr.OverridenBiome;
       if (BiomeRequiresSnowShader(biome))
   {
       _snowRefreshedTerritoryNames.Add(terr.Name ?? string.Empty);
            }
        }

        internal static void ApplyAllCustomPaints(bool clearCache = false)
        {
     if (ZNet.instance == null) return;
          if (clearCache)
     {
                _paintedTerritoryHeightmaps.Clear();
      _biomePaintedTerritories.Clear();
       _biomeRefreshTimestamps.Clear();
    _snowColoredHeightmaps.Clear();
          _snowRefreshedTerritoryNames.Clear();
            }
        }

        // The player's own PvP choice from before a PvpOnly/PveOnly territory forced it. It used to stay forced after
        // leaving; it is given back on exit, or when the player walks into an area without either flag.
        private static Player _pvpForcedFor;
        private static bool _pvpBeforeForce;

        private static void RestoreForcedPvp(Player player)
        {
            if (_pvpForcedFor == null) return;
            bool sameBody = ReferenceEquals(_pvpForcedFor, player);
            _pvpForcedFor = null;
            if (!sameBody || player == null) return; // a respawn or a new world: nothing of ours to give back
            if (player.IsPVPEnabled() != _pvpBeforeForce)
            {
                player.SetPVP(_pvpBeforeForce);
                TerritoryClientHelpers.SetInventoryPvpToggle(_pvpBeforeForce);
            }
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] PvP given back to {player.GetPlayerName()}: {(_pvpBeforeForce ? "on" : "off")}");
        }

   private static void ApplyTerritoryEffects(Player player)
        {
         if (Current == null || player == null)
{
  RestoreFriction();
     // Reset movement speed when leaving territory
   if (_baseWalkSpeed > 0f && player != null)
        {
   player.m_walkSpeed = _baseWalkSpeed;
           player.m_runSpeed = _baseRunSpeed;
  player.m_swimSpeed = _baseSwimSpeed;
            }
            RestoreForcedPvp(player);
    // Clear forced environment - let vanilla smoothly transition back
   if (_lastForcedEnv != null && EnvMan.instance != null)
          {
 ClearForcedEnvironmentSmooth();
    _lastForcedEnv = null;
 }
 if (_windForced && EnvMan.instance != null)
         {
    EnvMan.instance.ResetDebugWind();
         _windForced = false;
   }
    return;
    }

            // Apply movement speed multiplier
      ApplyMoveSpeedMultiplier(player);

   // PVP toggles
       bool? desiredPvp = null;
    if (Current.Flags.HasFlag(TerritoryFlags.PveOnly)) desiredPvp = false;
            if (Current.Flags.HasFlag(TerritoryFlags.PvpOnly)) desiredPvp = true;
            if (desiredPvp.HasValue)
            {
                if (!ReferenceEquals(_pvpForcedFor, player))
                {
                    _pvpForcedFor = player;
                    _pvpBeforeForce = player.IsPVPEnabled();
                }
                if (player.IsPVPEnabled() != desiredPvp.Value)
                {
                    player.SetPVP(desiredPvp.Value);
                    TerritoryClientHelpers.SetInventoryPvpToggle(desiredPvp.Value);
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] '{Current.Name}' forced PvP {(desiredPvp.Value ? "on" : "off")} for {player.GetPlayerName()}");
                }
            }
            else
            {
                RestoreForcedPvp(player);
            }

  // Push-away: straight out from the middle of the area, along the ground. Pos3D's y is the HeightBounds midpoint
  // (0 when unset) and a Rectangle's X/Y are its corner, so the old direction mostly lifted players or pushed from a corner.
            if (!AdminBypass(player) && Current.Flags.HasFlag(TerritoryFlags.PushAway) && !IsPermitted(Current, player))
            {
                var rb = player.GetComponent<Rigidbody>();
                var mid = Current.Center2D();
                var dir = new Vector3(player.transform.position.x - mid.x, 0f, player.transform.position.z - mid.y);
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
        var push = dir * (Time.fixedDeltaTime * 7f);
             if (rb != null) rb.MovePosition(player.transform.position + push);
      else player.transform.position += push;
            }

            // Force wind / environment - use smooth transitions
            if (EnvMan.instance != null)
        {
       if (Current.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.ForceWind))
    {
            EnvMan.instance.SetDebugWind(0f, Current.Wind <= 0f ? 1f : Current.Wind);
             _windForced = true;
     }
       else if (_windForced)
      {
          EnvMan.instance.ResetDebugWind();
  _windForced = false;
         }

       // Handle custom environment OR biome-based environment with SMOOTH transitions
    string desiredEnv = null;

      if (Current.Flags.HasFlag(TerritoryFlags.CustomEnvironment) && Current.CustomEnvironment != null && Current.CustomEnvironment.Length > 0)
   {
        // Custom environment specified directly
      double t = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
        int idx = (int)(t / 1200d) % Current.CustomEnvironment.Length;
     desiredEnv = Current.CustomEnvironment[idx];
     }
           else if (Current.Flags.HasFlag(TerritoryFlags.ForceBiome) && Current.OverridenBiome != 0)
       {
          // Get environment from biome - DeepNorth uses our custom environment
        var biome = (Heightmap.Biome)Current.OverridenBiome;
         desiredEnv = GetEnvironmentForBiome(biome);
                }

           if (!string.IsNullOrEmpty(desiredEnv))
    {
       if (_lastForcedEnv != desiredEnv)
           {
            // Use smooth transition via QueueEnvironment
   SetEnvironmentSmooth(desiredEnv);
          _lastForcedEnv = desiredEnv;
        }
                }
        else if (_lastForcedEnv != null)
           {
 // Clear forced environment - vanilla will smoothly transition back
      ClearForcedEnvironmentSmooth();
             _lastForcedEnv = null;
                }
      }

          // 0.0.770: the snow-mask shader globals (_GlobalSnowAmount / _SnowAmount / _AddSnow) were read by no vanilla 1.0 shader;
          // removed. Snow on Winter / snow-ForceBiome territories now comes from Core's SnowField (TerritorySnowFeed).

       // Periodic effects
            var interval = Current.PeriodicHealInterval > 0f ? Current.PeriodicHealInterval : 1f;
  if (Time.time >= _nextPeriodicTime)
            {
    if (Current.Flags.HasFlag(TerritoryFlags.PeriodicDamage) && !IsPermitted(Current, player))
   {
                 var hit = new HitData();
hit.m_damage.m_damage = Current.PeriodicDamageValue > 0 ? Current.PeriodicDamageValue : 5f; // Plain damage; as fire it became a slow burn
                    player.Damage(hit);
        }
       if (Current.Flags.HasFlag(TerritoryFlags.PeriodicHealALL))
          player.Heal(Current.PeriodicHealValue > 0 ? Current.PeriodicHealValue : 5f);
   if (Current.Flags.HasFlag(TerritoryFlags.PeriodicHeal) && IsPermitted(Current, player))
   player.Heal(Current.PeriodicHealValue > 0 ? Current.PeriodicHealValue : 5f);
                if (Current.Flags.HasFlag(TerritoryFlags.ForceBiome) && Current.OverridenBiome == (int)Heightmap.Biome.DeepNorth)
      {
     if (player.IsSwimming() || player.InWater())
     {
    var frost = new HitData();
  frost.m_damage.m_frost = 5f;
              player.Damage(frost);
             }
          }
           _nextPeriodicTime = Time.time + interval;
     }

      ApplySlipperyIce(player);

            if (IsDedicatedServer()) return;

   if (Current.Flags.HasFlag(TerritoryFlags.CustomPaint))
    TryApplyTerritoryPaint(Current, player.transform.position);
   if (Current.Flags.HasFlag(TerritoryFlags.ForceBiome) && Current.OverridenBiome != 0)
      TryApplyBiomePaint(Current, player.transform.position);
   }

        /// <summary>
     /// Returns an appropriate environment name for a given biome.
  /// Used when a territory forces a biome but doesn't specify a custom environment.
   /// </summary>
    private static string GetEnvironmentForBiome(Heightmap.Biome biome)
        {
       switch (biome)
          {
           case Heightmap.Biome.DeepNorth:
           return "DeepNorth_Freezing"; // Our custom environment
  case Heightmap.Biome.Mountain:
      return "Snow"; // Vanilla mountain environment
         case Heightmap.Biome.Swamp:
      return "SwampRain"; // Vanilla swamp environment
         case Heightmap.Biome.AshLands:
        return "Ashrain"; // Vanilla ashlands environment
         case Heightmap.Biome.Mistlands:
            return "Mistlands_clear"; // Vanilla mistlands environment
         case Heightmap.Biome.Plains:
 return "LightRain"; // Vanilla plains-like environment
                case Heightmap.Biome.BlackForest:
           return "Clear"; // Vanilla clear environment
    case Heightmap.Biome.Meadows:
   return "Clear";
         default:
 return null; // Let vanilla handle it
          }
  }

    private static void ApplySlipperyIce(Player player)
      {
       if (player == null) return;
          bool forceDeepNorth = Current != null && Current.Flags.HasFlag(TerritoryFlags.ForceBiome) && Current.OverridenBiome == (int)Heightmap.Biome.DeepNorth;
   if (!forceDeepNorth) forceDeepNorth = IsInDeepNorthArea(player.transform.position);
      if (!forceDeepNorth) { RestoreFriction(); return; }

            var pos = player.transform.position;
     float ground = pos.y;
  try { Heightmap.GetHeight(pos, out ground); } catch { }
            bool iceRing = global::FiresRPGmaker.Utilities.RingUp.OnRing(pos, out Vector3 iceUp);   // 0.0.772: a ring has no sea or heightmap ice; only ice rocks count, found along -up
            float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            float depth = waterLevel - ground;
         bool shallowWater = !iceRing && player.InWater() && depth <= 4f && depth >= -1f;
   bool nearSeaIce = !iceRing && ground >= 28f && ground <= 32f && !player.IsSwimming();
      bool groundIce = !iceRing && player.IsOnGround() && !player.InWater() && !player.IsSwimming() && ground < 32f && Mathf.PerlinNoise(pos.x * 0.05f, pos.z * 0.05f) > 0.35f;

bool onIceRock = false;
  if (player.IsOnGround())
   {
   if (Physics.Raycast(pos + iceUp * 0.5f, -iceUp, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
   {
    var hitName = hit.collider != null ? hit.collider.gameObject.name : string.Empty;
   if (!string.IsNullOrEmpty(hitName) && IcePrefabs.Any(n => hitName.StartsWith(n, StringComparison.OrdinalIgnoreCase)))
              onIceRock = true;
          }
   }

   bool shouldSlip = shallowWater || nearSeaIce || groundIce || onIceRock;
     var col = player.GetComponent<CapsuleCollider>();
       if (col == null) { RestoreFriction(); return; }

   if (shouldSlip)
  {
                if (_origPhysMat == null) _origPhysMat = col.sharedMaterial;
         if (_icePhysMat == null)
        {
        _icePhysMat = new PhysicsMaterial("DeepNorth_Ice")
  {
            dynamicFriction = 0.02f,
   staticFriction = 0.01f,
       frictionCombine = PhysicsMaterialCombine.Minimum,
              bounceCombine = PhysicsMaterialCombine.Minimum,
       bounciness = 0f
  };
           }
     _lastCollider = col;
     if (col.sharedMaterial != _icePhysMat) col.sharedMaterial = _icePhysMat;
              var rb = player.GetComponent<Rigidbody>();
     if (rb != null && !rb.isKinematic)
   {
            if (!_origDrag.HasValue) _origDrag = rb.linearDamping;
if (!_origAngularDrag.HasValue) _origAngularDrag = rb.angularDamping;
           rb.linearDamping = 0.05f;
      rb.angularDamping = 0.05f;
     }
            }
            else RestoreFriction();
        }

      private static void RestoreFriction()
        {
            try
 {
    if (_lastCollider != null && _origPhysMat != null)
       if (_lastCollider.sharedMaterial != _origPhysMat)
    _lastCollider.sharedMaterial = _origPhysMat;
     var rb = _lastCollider != null ? _lastCollider.GetComponent<Rigidbody>() : null;
          if (rb != null && !rb.isKinematic)
  {
      if (_origDrag.HasValue) rb.linearDamping = _origDrag.Value;
        if (_origAngularDrag.HasValue) rb.angularDamping = _origAngularDrag.Value;
          }
            }
      catch { }
   }

        private static void TryRevealOnMap(Territory terr)
        {
       if (terr == null || !TerritoryClientHelpers.IsMinimapAvailable()) return;
       if (_mapRevealedTerritories.Contains(terr.UID)) return;

             Vector2 center2D;
             if (terr.RevealHasCustomCenter)
     center2D = new Vector2(terr.RevealCenterX, terr.RevealCenterZ);
       else
             {
         int cx = terr.X != null && terr.X.Length > 0 ? terr.X[0] : 0;
              int cz = terr.Y != null && terr.Y.Length > 0 ? terr.Y[0] : 0;
        center2D = new Vector2(cx, cz);
        }

    float radius = terr.RevealRadius > 0f ? terr.RevealRadius : (terr.Radius > 0 ? terr.Radius : 50f);
   if (radius <= 0f) radius = 50f;

             Vector3 center3D = new Vector3(center2D.x, 0f, center2D.y);
   if (TerritoryClientHelpers.TryMinimapExplore(center3D, radius))
          _mapRevealedTerritories.Add(terr.UID);
         }

        // ==================== HARMONY PATCHES ====================

        private const TerritoryFlags GroundHeightFlags =
            TerritoryFlags.ForceGroundHeight | TerritoryFlags.AddGroundHeight | TerritoryFlags.LimitZoneHeight;

        private static int _groundHeightCacheVersion = -1;
        private static bool _anyGroundHeightTerritory;

        private static bool AnyGroundHeightTerritory()
        {
            if (_groundHeightCacheVersion == _currentTerritoriesVersion) return _anyGroundHeightTerritory;
            _groundHeightCacheVersion = _currentTerritoriesVersion;
            _anyGroundHeightTerritory = false;
            var territories = GetCurrentTerritories();
            if (territories == null) return false;
            foreach (var territory in territories)
            {
                if ((territory.Flags & GroundHeightFlags) == 0) continue;
                _anyGroundHeightTerritory = true;
                break;
            }
            return _anyGroundHeightTerritory;
        }

        // GetWorldHeight runs thousands of times per terrain streamer tick; the territory scan only matters when some
        // territory reshapes the ground, so every other call leaves after one cached bool.
        [HarmonyPatch(typeof(Heightmap), "GetWorldHeight")]
        [HarmonyPostfix]
        private static void Heightmap_GetWorldHeight_Postfix(Vector3 worldPos, ref float height, ref bool __result)
        {
            if (!__result || !AnyGroundHeightTerritory()) return;
            var terr = GetTerritory(worldPos);
            if (terr == null) return;
            if ((terr.Flags & TerritoryFlags.ForceGroundHeight) != 0) { height = terr.OverridenHeight; return; }
            if ((terr.Flags & TerritoryFlags.AddGroundHeight) != 0) height += terr.OverridenHeight;
            if ((terr.Flags & TerritoryFlags.LimitZoneHeight) != 0) height = Mathf.Min(height, terr.OverridenHeight);
        }

    [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.GetBiome))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run AFTER BetterContinents and other mods
        private static void Heightmap_GetBiome_Postfix(Heightmap __instance, Vector3 point, ref Heightmap.Biome __result)
        {
            // CRITICAL: Guard against null instance - can happen during early world loading
            // or when other mods (like BetterContinents) modify the patch chain
            if (__instance == null || FatOwnsBiome) return;   // FAT answers the heightmap's biome through ForcedSectorAt

            // CRITICAL: Skip during world generation - ZNet.instance is null or game not ready
            // This is when BetterContinents is processing and we must not interfere
            if (ZNet.instance == null) return;

            // CRITICAL: Guard against uninitialized Heightmap - BetterContinents accesses m_cornerBiomes
            // which can be null during world generation. Check if the heightmap is properly initialized.
            // This prevents NullReferenceException in the Harmony patch chain.
            EnsureReflectionCached();
            if (_cornerBiomesField != null)
            {
                try
                {
                    var cornerBiomes = _cornerBiomesField.GetValue(__instance) as Heightmap.Biome[];
                    if (cornerBiomes == null || cornerBiomes.Length < 4) return;
                }
                catch { return; }
            }

            // Fast exit if no territories are configured
            if (!_hasAnyForceBiomeTerritories && !_hasAnySnowMaskOnlyTerritories) return;

            try
            {
                var terr = GetTerritory(point);
                if (terr == null) return;

                // ForceBiome takes priority - use the specified biome
                if (terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0)
                {
                    __result = (Heightmap.Biome)terr.OverridenBiome;
                    return;
                }

                // NOTE: SnowMask WITHOUT ForceBiome does NOT change the biome here.
                // This preserves the original weather and biome display name.
                // Snow visuals are handled separately:
                // 1. Terrain snow: Heightmap_RebuildRenderMesh_Postfix applies Mountain vertex colors
                // 2. Shader globals: _GlobalSnowAmount, _AddSnow set in ApplyTerritoryEffects
            }
            catch
            {
                // Silently ignore errors - don't crash the game over biome lookups
            }
        }

        // Valheim 1.0 changed EnvMan.GetBiome to return BiomeSector - a CLASS wrapping the Heightmap.Biome enum plus alt-biome data -
        // instead of the enum itself. GetBiome hands back either a shared BiomeSector.Empty* static or WorldGenerator's cached sector
        // for that position, so writing __result.Biome would mutate state the whole session shares. Return a separate instance instead,
        // cached per biome because this runs every frame. One sector per (biome, alt name); an alt is added as the sector's
        // modifier, as vanilla's own alt-biome sectors carry it (AltBiomes list) - weather, spawns, vegetation and the sector name
        // ("Heath") follow it
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Heightmap.Biome, string), BiomeSector> _forcedSectors
            = new System.Collections.Concurrent.ConcurrentDictionary<(Heightmap.Biome, string), BiomeSector>();

        /// <summary>True once FAT answers the world's biome and sector through <see cref="ForcedSectorAt"/>
        /// (FatApi.AddBiomeSectorOverride, FAT 0.2.1008+); RPGmaker's own generator patches then step aside.</summary>
        internal static volatile bool FatOwnsBiome;

        // A forced sector whose alt biome was not loaded yet (AltBiomeList fills during startup); rebuilt on the main thread.
        private static bool s_altSectorPending;

        /// <summary>Main thread only: builds the plain and the alt sector of every cached ForceBiome territory.</summary>
        private static void RebuildForcedSectors()
        {
            s_altSectorPending = false;
            var cache = _cachedForceBiomeTerritories;
            if (cache == null) return;
            foreach (var d in cache)
                if (!d.IsSnowMaskOnly && d.Territory != null) { ForcedSector(d.ForcedBiome); ForcedSector(d.ForcedBiome, d.Territory.OverridenAltBiome); }
        }

        /// <summary>The forced sector (with its alt biome) at a world point, or null outside every ForceBiome territory.
        /// FAT's biome authority calls this from terrain threads: it reads only the synced cache and the sector table
        /// built on the main thread, never GetBiome / GetBiomeSector, and returns one shared sector per biome + alt.</summary>
        internal static BiomeSector ForcedSectorAt(float wx, float wz)
        {
            if (BypassForceBiome) return null;   // the height blend and regen dry runs ask for the world's own biome
            var cache = _cachedForceBiomeTerritories;
            if (cache == null || cache.Length == 0) return null;
            if (wx < _forceBiomeBoundsMinX || wx > _forceBiomeBoundsMaxX || wz < _forceBiomeBoundsMinZ || wz > _forceBiomeBoundsMaxZ) return null;
            var pos = new Vector2(wx, wz);
            for (int i = 0; i < cache.Length; i++)
            {
                ref var data = ref cache[i];
                if (data.IsSnowMaskOnly || wx < data.MinX || wx > data.MaxX || wz < data.MinZ || wz > data.MaxZ) continue;
                var t = data.Territory;
                try
                {
                    if (t != null && t.IsInside2D(pos))
                    {
                        if (_forcedSectors.TryGetValue((data.ForcedBiome, t.OverridenAltBiome ?? string.Empty), out var s)) return s;
                        return _forcedSectors.TryGetValue((data.ForcedBiome, string.Empty), out var plain) ? plain : null;   // alt not loaded (yet)
                    }
                }
                catch { }
            }
            return null;
        }
        private static BiomeSector ForcedSector(Heightmap.Biome biome, string alt = null)
        {
            var key = (biome, alt ?? string.Empty);
            if (!_forcedSectors.TryGetValue(key, out var sector))
            {
                sector = new BiomeSector(null, biome);
                var a = string.IsNullOrEmpty(alt) ? null : TerritoryBiomes.FindAlt(alt);
                if (a != null)
                {
                    try { sector.AddModifier(a); }
                    catch (Exception ex) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] alt biome '{alt}' not added to the forced {biome} sector: {ex.Message}"); }
                }
                // an alt not loaded yet must not be cached without it: the next main-thread call builds it again
                if (!string.IsNullOrEmpty(alt) && a == null) { s_altSectorPending = true; return sector; }
                _forcedSectors[key] = sector;
                global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] forced sector built: {biome}{(a != null ? " + alt '" + a.m_name + "'" : "")} ({SafeName(sector)})");
            }
            return sector;
        }
        /// <summary>ForceBiome territories must grow their full biome on the superflat too (FAT 0.2.1000
        /// FatApi.AddSuperFlatContentArea): true inside a ForceBiome territory. Reads only the synced ForceBiome cache (same on every peer),
        /// never GetBiome / GetBiomeSector; thread-safe (the cache array is swapped whole).</summary>
        internal static int ForceBiomeAreaCount() { var c = _cachedForceBiomeTerritories; return c == null ? 0 : c.Length; }   // territory_tools_test

        internal static bool InsideForceBiomeArea(float wx, float wz)
        {
            var cache = _cachedForceBiomeTerritories;
            if (cache == null || cache.Length == 0) return false;
            if (wx < _forceBiomeBoundsMinX || wx > _forceBiomeBoundsMaxX || wz < _forceBiomeBoundsMinZ || wz > _forceBiomeBoundsMaxZ) return false;
            for (int i = 0; i < cache.Length; i++)
            {
                var d = cache[i];
                if (d.IsSnowMaskOnly || wx < d.MinX || wx > d.MaxX || wz < d.MinZ || wz > d.MaxZ) continue;
                try { if (d.Territory != null && d.Territory.IsInside2D(wx, wz)) return true; } catch { }
            }
            return false;
        }

        /// <summary>Set (per thread) while TerritoryBiomeBlend asks the generator for the ORIGINAL biome at a point.</summary>
        [ThreadStatic] internal static bool BypassForceBiome;

        private static string SafeName(BiomeSector s) { try { return s.GetName(false); } catch { return "?"; } }

        // The generator's sector (what zone generation, spawns and locations ask) is the forced one inside a ForceBiome area
        [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiomeSector), typeof(float), typeof(float), typeof(bool))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void WorldGenerator_GetBiomeSector_Postfix(float wx, float wy, ref BiomeSector __result)
        {
            if (BypassForceBiome || FatOwnsBiome) return;
            if (ZNet.instance == null || !_hasAnyForceBiomeTerritories) return;
            var cache = _cachedForceBiomeTerritories;
            if (cache == null || cache.Length == 0) return;
            if (wx < _forceBiomeBoundsMinX || wx > _forceBiomeBoundsMaxX || wy < _forceBiomeBoundsMinZ || wy > _forceBiomeBoundsMaxZ) return;
            var pos = new Vector2(wx, wy);
            for (int i = 0; i < cache.Length; i++)
            {
                ref var data = ref cache[i];
                if (data.IsSnowMaskOnly || wx < data.MinX || wx > data.MaxX || wy < data.MinZ || wy > data.MaxZ) continue;
                try
                {
                    if (data.Territory != null && data.Territory.IsInside2D(pos))
                    {
                        __result = ForcedSector(data.ForcedBiome, data.Territory.OverridenAltBiome);
                        return;
                    }
                }
                catch { }
            }
        }
        // The camera-territory cache for EnvMan_GetBiome_Postfix (see there).
        private static bool s_biomeCacheValid;
        private static Vector3 s_biomeCachePos;
        private static float s_biomeCacheTime, s_biomeNextReport;
        private static Territory s_biomeCacheTerr;
        private static int s_biomeHits, s_biomeMisses;

        // ( inside 'territory2' (ForceBiome=Plains) the ground and weather were Plains but the plate and the minimap pill still said
        // "Swamp"): the player's biome - what Core's plate / data pill and vanilla's own biome checks read - reports the forced biome
        // too. Per player position; the territory lookup is the indexed one.
        private static bool s_playerBiomeLogged;
        [HarmonyPatch(typeof(Player), nameof(Player.GetCurrentBiome))]
        [HarmonyPostfix]
        private static void Player_GetCurrentBiome_Postfix(Player __instance, ref Heightmap.Biome __result)
        {
            if (!_hasAnyForceBiomeTerritories || __instance == null || FatOwnsBiome) return;   // with FAT, vanilla's own lookups already answer through FAT (paint circles win there)
            try
            {
                var terr = GetTerritory(__instance.transform.position);
                if (terr == null || !terr.Flags.HasFlag(TerritoryFlags.ForceBiome) || terr.OverridenBiome == 0) return;
                var forced = (Heightmap.Biome)terr.OverridenBiome;
                if (!s_playerBiomeLogged && forced != __result)
                {
                    s_playerBiomeLogged = true;
                    global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] player biome: '{terr.Name}' forces {forced} (the world says {__result}) - the plate / pill show {forced}");
                }
                __result = forced;
            }
            catch { }
        }

        [HarmonyPatch(typeof(EnvMan), "GetBiome")]
        [HarmonyPostfix]
        private static void EnvMan_GetBiome_Postfix(EnvMan __instance, ref BiomeSector __result)
        {
            // CRITICAL: Guard against null instance - can happen during early initialization
            if (__instance == null) return;

            // With FAT, EnvMan's sector comes from GetBiomeSector, which FAT answers (paint circles win over territories), so this
            // never answers differently from FAT. It only finishes the sectors whose alt was not loaded at the last cache rebuild.
            if (FatOwnsBiome) { if (s_altSectorPending) RebuildForcedSectors(); return; }

            // Fast exit if no territories are configured to force biomes
            if (!_hasAnyForceBiomeTerritories) return;

            try
            {
                var cam = Utils.GetMainCamera();
                if (cam == null) return;

                // ITTO R4: 49 lookups/s, 26 us each: the camera's territory is reused while the camera moved < 1 m and <
                // 0.25 s passed. A forced biome can switch up to 0.25 s later at a border; nothing else changes.
                Vector3 camPos = cam.transform.position;
                float t = Time.time;
                Territory terr;
                if (s_biomeCacheValid && (camPos - s_biomeCachePos).sqrMagnitude < 1f && t - s_biomeCacheTime < 0.25f)
                {
                    terr = s_biomeCacheTerr; s_biomeHits++;
                }
                else
                {
                    terr = GetTerritory(camPos);
                    s_biomeCacheTerr = terr; s_biomeCachePos = camPos; s_biomeCacheTime = t; s_biomeCacheValid = true; s_biomeMisses++;
                }
                if (t >= s_biomeNextReport)
                {
                    if (s_biomeNextReport > 0f && s_biomeHits + s_biomeMisses > 0)
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] biome cache: {s_biomeHits} hit(s), {s_biomeMisses} lookup(s) in the last minute ({100f * s_biomeHits / (s_biomeHits + s_biomeMisses):0}% hits)");
                    else if (s_biomeNextReport == 0f)
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[Territory] biome cache attached (1 m / 0.25 s; ForceBiome territories present)");
                    s_biomeHits = 0; s_biomeMisses = 0; s_biomeNextReport = t + 60f;
                }
                if (terr == null) terr = Current;
                if (terr == null) return;

                // 0.0.770: the _GlobalSnowAmount / _SnowAmount globals set here were read by no vanilla 1.0 shader (dead); dynamic
                // snow now comes from Core's SnowField (this mod feeds it Winter / snow-ForceBiome territories, TerritorySnowFeed).
                if (terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0)
                    __result = ForcedSector((Heightmap.Biome)terr.OverridenBiome, terr.OverridenAltBiome);   // alt
            }
            catch
            {
                // Silently ignore errors - don't crash the game over biome lookups
            }
        }

     [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GetBiome), typeof(float), typeof(float), typeof(float), typeof(bool))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run AFTER BetterContinents
        private static void WorldGenerator_GetBiome_Postfix(WorldGenerator __instance, float wx, float wy, ref Heightmap.Biome __result)
        {
            if (BypassForceBiome || FatOwnsBiome) return;   // The height blend asks for the original biome; FAT answers through ForcedSectorAt
            // CRITICAL: Guard against null instance - can happen during early world loading
            // BetterContinents heavily patches WorldGenerator, so we must be defensive
            if (__instance == null) return;

            // CRITICAL: Skip during world generation when ZNet isn't ready
            // This is when BetterContinents is processing heightmaps/biomes and we must not interfere
            if (ZNet.instance == null) return;

            // Fast exit - this is called VERY frequently during terrain generation
            if (!_hasAnyForceBiomeTerritories) return;

            // Use cached ForceBiome territories with AABB filtering - supports ALL biome types
            var cache = _cachedForceBiomeTerritories;
            if (cache == null || cache.Length == 0) return;

            // Fast global bounds check - skip if point is entirely outside all territory bounds
            if (wx < _forceBiomeBoundsMinX || wx > _forceBiomeBoundsMaxX ||
                wy < _forceBiomeBoundsMinZ || wy > _forceBiomeBoundsMaxZ)
                return;

            var pos = new Vector2(wx, wy);
            for (int i = 0; i < cache.Length; i++)
            {
                ref var data = ref cache[i];

                // IMPORTANT: Skip SnowMask-only territories here!
                // WorldGenerator.GetBiome affects terrain HEIGHT generation.
                // We want SnowMask to be visual-only, so we skip it here but include it
                // in the Heightmap.Regenerate postfix for visual texture blending.
                if (data.IsSnowMaskOnly)
                    continue;

                // Per-territory AABB check
                if (wx < data.MinX || wx > data.MaxX || wy < data.MinZ || wy > data.MaxZ)
                    continue;

                try
                {
                    if (data.Territory != null && data.Territory.IsInside2D(pos))
                    {
                        __result = data.ForcedBiome;
                        return;
                    }
                }
                catch { continue; }
            }
        }

        /// <summary>
        /// Patch Heightmap.Regenerate to force corner biomes for territories that need visual biome changes.
        /// OPTIMIZED: Uses cached territory data with spatial filtering.
   /// Now supports ALL biome types, not just snow.
  /// For SnowMask-only territories, we set corner biomes for visual snow but it won't affect
      /// terrain heights since the biome was already set BEFORE height calculation in WorldGenerator.
        /// </summary>
      [HarmonyPatch(typeof(Heightmap), nameof(Heightmap.Regenerate))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)] // Run AFTER BetterContinents
 private static void Heightmap_Regenerate_Postfix(Heightmap __instance)
        {
         // Ultra-fast exits first - no method calls
    if (__instance == null) return;
            s_regenCalls++;   // counter
            if (_isDedicatedServerChecked && _isDedicatedServer) return;

            // CRITICAL: Skip during world generation when ZNet isn't ready
            // This is when BetterContinents is processing heightmaps and we must not interfere
            if (ZNet.instance == null) return;

    // Check if we even have any cached territories before doing anything else
     if (_cachedForceBiomeTerritories == null || _cachedForceBiomeTerritories.Length == 0)
            {
     // Only validate cache if version changed
       if (_territoryCacheVersion == _currentTerritoriesVersion)
    return;
            }

      // Now do the full initialization checks
     if (IsDedicatedServer()) return;
            EnsureTerritoryCacheValid();

     // Re-check after cache validation
       if (_cachedForceBiomeTerritories == null || _cachedForceBiomeTerritories.Length == 0)
      return;

            // Fast spatial rejection BEFORE reflection calls
            var hmPos = __instance.transform.position;
       float scale = __instance.m_scale;
       int width = __instance.m_width;
 float halfSize = width * scale * 0.5f;

        float hmMinX = hmPos.x - halfSize;
  float hmMaxX = hmPos.x + halfSize;
            float hmMinZ = hmPos.z - halfSize;
       float hmMaxZ = hmPos.z + halfSize;

            // Fast exit: heightmap is entirely outside the global bounds of all ForceBiome territories
            if (hmMaxX < _forceBiomeBoundsMinX || hmMinX > _forceBiomeBoundsMaxX ||
      hmMaxZ < _forceBiomeBoundsMinZ || hmMinZ > _forceBiomeBoundsMaxZ)
             return;

            // Time everything past the bounds reject, on every return path
            s_regenInside++;
            long rgT0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
         // Only NOW do we need reflection - this heightmap actually overlaps territory bounds
            EnsureReflectionCached();
            if (_cornerBiomesField == null) return;

    try
 {
      // Get the corner biomes array via reflection
    var cornerBiomes = _cornerBiomesField.GetValue(__instance) as Heightmap.Biome[];
         if (cornerBiomes == null || cornerBiomes.Length < 4) return;

     // Pre-compute corner positions
  float c0x = hmPos.x - halfSize, c0z = hmPos.z - halfSize;
    float c1x = hmPos.x + halfSize, c1z = hmPos.z - halfSize;
          float c2x = hmPos.x - halfSize, c2z = hmPos.z + halfSize;
       float c3x = hmPos.x + halfSize, c3z = hmPos.z + halfSize;

  bool needsRebuild = false;

     // Check each corner and update to the forced biome if inside a ForceBiome territory
    // NOTE: SnowMask territories are NO LONGER included here - they only affect shader globals,
    // not the terrain texture blending. This preserves the original terrain appearance.
     Heightmap.Biome biome0 = GetForcedBiomeAtPoint(c0x, c0z);
     if (biome0 != Heightmap.Biome.None && cornerBiomes[0] != biome0)
       {
   cornerBiomes[0] = biome0;
  needsRebuild = true;
     }

      Heightmap.Biome biome1 = GetForcedBiomeAtPoint(c1x, c1z);
if (biome1 != Heightmap.Biome.None && cornerBiomes[1] != biome1)
       {
   cornerBiomes[1] = biome1;
          needsRebuild = true;
   }

       Heightmap.Biome biome2 = GetForcedBiomeAtPoint(c2x, c2z);
           if (biome2 != Heightmap.Biome.None && cornerBiomes[2] != biome2)
         {
            cornerBiomes[2] = biome2;
          needsRebuild = true;
  }

         Heightmap.Biome biome3 = GetForcedBiomeAtPoint(c3x, c3z);
     if (biome3 != Heightmap.Biome.None && cornerBiomes[3] != biome3)
      {
              cornerBiomes[3] = biome3;
    needsRebuild = true;
      }

       if (needsRebuild)
{
         _cornerBiomesField.SetValue(__instance, cornerBiomes);
      if (_rebuildRenderMeshMethod != null)
          {
             try { _rebuildRenderMeshMethod.Invoke(__instance, null); s_regenRebuilds++; }
            catch { }
         }
   }
            }
  catch { }
            }
            finally
            {
                s_regenTicks += System.Diagnostics.Stopwatch.GetTimestamp() - rgT0;
                RegenReport();
            }
  }

        // Once a minute, and only in a minute where the postfix did work inside ForceBiome bounds, one Info line (perf
        // laps run at Info). Evidence for the Regenerate hitches; no behaviour change.
        private static int s_regenCalls, s_regenInside, s_regenRebuilds;
        private static long s_regenTicks;
        private static float s_regenNextReport;
        private static void RegenReport()
        {
            float now = Time.realtimeSinceStartup;
            if (s_regenNextReport <= 0f) { s_regenNextReport = now + 60f; return; }
            if (now < s_regenNextReport) return;
            double ms = s_regenTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            global::FiresRPGmaker.FiresLogger.LogDebug($"[Territory] Regenerate postfix last minute: {s_regenCalls} call(s), {s_regenInside} inside ForceBiome bounds, {s_regenRebuilds} extra RebuildRenderMesh, {ms:0.0} ms in the postfix ({(s_regenInside > 0 ? ms / s_regenInside : 0):0.00} ms per inside call)");
            s_regenCalls = s_regenInside = s_regenRebuilds = 0; s_regenTicks = 0; s_regenNextReport = now + 60f;
        }

  /// <summary>
        /// Gets the forced biome at a world point, or Biome.None if not inside any ForceBiome territory.
        /// OPTIMIZED: Uses cached array with AABB pre-filtering.
        /// NOTE: SnowMask-only territories are no longer in this cache - they don't force biomes.
        /// </summary>
        private static Heightmap.Biome GetForcedBiomeAtPoint(float worldX, float worldZ)
        {
            var cache = _cachedForceBiomeTerritories;
            if (cache == null || cache.Length == 0)
                return Heightmap.Biome.None;

            // Fast exit: point is outside global bounds
            if (worldX < _forceBiomeBoundsMinX || worldX > _forceBiomeBoundsMaxX ||
                worldZ < _forceBiomeBoundsMinZ || worldZ > _forceBiomeBoundsMaxZ)
                return Heightmap.Biome.None;

            var pos = new Vector2(worldX, worldZ);

            // Check each cached ForceBiome territory (SnowMask-only are not in this list)
            for (int i = 0; i < cache.Length; i++)
            {
                ref var data = ref cache[i];

                // CRITICAL: Null check for territory - can be null if territory was removed
                if (data.Territory == null)
                    continue;

                // Fast AABB rejection
                if (worldX < data.MinX || worldX > data.MaxX ||
                    worldZ < data.MinZ || worldZ > data.MaxZ)
                    continue;

                // Detailed check only if AABB passed
                try
                {
                    if (data.Territory.IsInside2D(pos))
                        return data.ForcedBiome;
                }
                catch { continue; }
            }

            return Heightmap.Biome.None;
        }

        /// <summary>
        /// Checks if a world point is inside a SnowMask-only territory.
        /// Returns true if point should have visual snow applied via shaders.
        /// </summary>
        private static bool IsInSnowMaskTerritory(float worldX, float worldZ)
        {
            if (!_hasAnySnowMaskOnlyTerritories) return false;

            // Fast exit: point is outside global snow mask bounds
            if (worldX < _snowMaskBoundsMinX || worldX > _snowMaskBoundsMaxX ||
                worldZ < _snowMaskBoundsMinZ || worldZ > _snowMaskBoundsMaxZ)
                return false;

            var territories = GetCurrentTerritories();
            if (territories == null) return false;

            var pos = new Vector2(worldX, worldZ);

            foreach (var terr in territories)
            {
                if (terr == null) continue;
                if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.SnowMask)) continue;
                if (terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0) continue; // This is ForceBiome, not SnowMask-only

                try
                {
                    if (terr.IsInside2D(pos))
                        return true;
                }
                catch { continue; }
            }

            return false;
        }

        // Cached SnowMask territory data for fast lookups in RebuildRenderMesh
        private static SnowMaskTerritoryData[] _cachedSnowMaskTerritories = null;


        private struct SnowMaskTerritoryData
        {
            public Territory Territory;
            public float MinX, MaxX, MinZ, MaxZ;
        }

        /// <summary>
        /// Patch Heightmap.RebuildRenderMesh to apply per-vertex snow colors for SnowMask territories.
        /// This is the KG Marketplace approach - it sets vertex colors directly on the render mesh,
        /// giving precise per-vertex control over snow appearance without modifying biomes.
        ///
        /// Key benefits:
        /// - Snow follows exact territory boundaries (not chunk corners)
        /// - Original terrain textures are preserved outside snow areas
        /// - No biome changes = no terrain height/generation changes
        /// - Works independently of ForceBiome system
        /// </summary>
        [HarmonyPatch(typeof(Heightmap), "RebuildRenderMesh")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Heightmap_RebuildRenderMesh_Postfix(Heightmap __instance)
        {
            // Fast exits
            if (__instance == null) return;
            if (_isDedicatedServerChecked && _isDedicatedServer) return;
            if (ZNet.instance == null) return;
            if (!_hasAnySnowMaskOnlyTerritories) return;

            // Ensure reflection is cached
            EnsureReflectionCached();

            // Skip distant LOD heightmaps (use reflection since m_isDistantLod is private)
            if (_isDistantLodField != null)
            {
                try
                {
                    bool isDistantLod = (bool)_isDistantLodField.GetValue(__instance);
                    if (isDistantLod) return;
                }
                catch { return; }
            }

            // Check if render mesh exists (use reflection since m_renderMesh is private)
            Mesh renderMesh = null;
            if (_renderMeshField != null)
            {
                try
                {
                    renderMesh = _renderMeshField.GetValue(__instance) as Mesh;
                    if (renderMesh == null) return;
                }
                catch { return; }
            }
            else return;

            // Get the static temp colors list (use reflection since s_tempColors is private static)
            List<Color32> tempColors = null;
            if (_tempColorsField != null)
            {
                try
                {
                    tempColors = _tempColorsField.GetValue(null) as List<Color32>;
                    if (tempColors == null) return;
                }
                catch { return; }
            }
            else return;

            // Build/update cached snow mask territories if needed
            EnsureSnowMaskCacheValid();

            var snowCache = _cachedSnowMaskTerritories;
            if (snowCache == null || snowCache.Length == 0) return;

            // Get heightmap bounds
            var hmPos = __instance.transform.position;
            float scale = __instance.m_scale;
            int width = __instance.m_width;
            float halfSize = width * scale * 0.5f;

            // Calculate world position of heightmap origin (bottom-left corner)
            Vector3 origin = hmPos - new Vector3(halfSize, 0f, halfSize);

            // Fast bounds check against all snow mask territories
            float hmMinX = hmPos.x - halfSize;
            float hmMaxX = hmPos.x + halfSize;
            float hmMinZ = hmPos.z - halfSize;
            float hmMaxZ = hmPos.z + halfSize;

            if (hmMaxX < _snowMaskBoundsMinX || hmMinX > _snowMaskBoundsMaxX ||
                hmMaxZ < _snowMaskBoundsMinZ || hmMinZ > _snowMaskBoundsMaxZ)
                return;

            // Check if ANY snow mask territory overlaps this heightmap
            bool hasOverlap = false;
            for (int i = 0; i < snowCache.Length; i++)
            {
                ref var data = ref snowCache[i];
                if (hmMaxX >= data.MinX && hmMinX <= data.MaxX &&
                    hmMaxZ >= data.MinZ && hmMinZ <= data.MaxZ)
                {
                    hasOverlap = true;
                    break;
                }
            }
            if (!hasOverlap) return;

            try
            {
                // Get vertex count - should be (width+1) * (width+1)
                int num = width + 1;
                bool anySnowApplied = false;

                // Clear and reuse the static temp colors list (same as vanilla)
                tempColors.Clear();

                // Iterate through each vertex position
                for (int z = 0; z < num; z++)
                {
                    for (int x = 0; x < num; x++)
                    {
                        // Calculate world position for this vertex
                        float worldX = origin.x + x * scale;
                        float worldZ = origin.z + z * scale;

                        // Check if this vertex is inside any snow mask territory
                        bool inSnowMask = false;
                        for (int i = 0; i < snowCache.Length; i++)
                        {
                            ref var data = ref snowCache[i];

                            // Fast AABB check first
                            if (worldX < data.MinX || worldX > data.MaxX ||
                                worldZ < data.MinZ || worldZ > data.MaxZ)
                                continue;

                            // Detailed territory check
                            if (data.Territory != null)
                            {
                                try
                                {
                                    if (data.Territory.IsInside2D(new Vector2(worldX, worldZ)))
                                    {
                                        inSnowMask = true;
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }

                        if (inSnowMask)
                        {
                            // Apply Mountain biome color (triggers snow texture in shader)
                            // Mountain = (0, 255, 0, 0) - green channel
                            tempColors.Add(Heightmap.GetBiomeColor(Heightmap.Biome.Mountain));
                            anySnowApplied = true;
                        }
                        else
                        {
                            // Get the actual biome color for this position from WorldGenerator
                            Heightmap.Biome biome = WorldGenerator.instance.GetBiome(worldX, worldZ);
                            tempColors.Add(Heightmap.GetBiomeColor(biome));
                        }
                    }
                }

                // Only update the mesh if we actually applied snow
                if (anySnowApplied)
                {
                    renderMesh.SetColors(tempColors);
                }
            }
            catch (Exception ex)
            {
                // Log but don't crash
                Debug.LogWarning($"[FiresRPGmaker] Error applying snow mask to heightmap: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures the snow mask territory cache is up to date.
        /// </summary>
        private static void EnsureSnowMaskCacheValid()
        {
            // Reuse the same version tracking as ForceBiome cache
            // Both are invalidated together via InvalidateSnowTerritoryCache()

            if (!_hasAnySnowMaskOnlyTerritories)
            {
                _cachedSnowMaskTerritories = null;
                return;
            }

            var territories = GetCurrentTerritories();
            if (territories == null || territories.Count == 0)
            {
                _cachedSnowMaskTerritories = null;
                return;
            }

            var snowList = new List<SnowMaskTerritoryData>();

            foreach (var terr in territories)
            {
                if (terr == null) continue;
                if (!terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.SnowMask)) continue;
                // Skip if this is a ForceBiome territory - those are handled by the corner biome system
                if (terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0) continue;

                var bounds = GetTerritoryBounds(terr);
                snowList.Add(new SnowMaskTerritoryData
                {
                    Territory = terr,
                    MinX = bounds.min.x,
                    MaxX = bounds.max.x,
                    MinZ = bounds.min.y,
                    MaxZ = bounds.max.y
                });
            }

            _cachedSnowMaskTerritories = snowList.ToArray();
        }
    }
}













