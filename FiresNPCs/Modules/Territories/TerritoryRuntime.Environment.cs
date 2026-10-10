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
    private static bool _snowForced;
   private static float? _origSnowAmount;
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
      return TerritorySyncedState.ClientTerritories;
        }

        /// <summary>
     /// Light-weight cache validation - builds cached ForceBiome territory data for ALL biome types
        /// </summary>
        private static void EnsureTerritoryCacheValid()
   {
  if (_territoryCacheVersion == _currentTerritoriesVersion)
    return;

        _territoryCacheVersion = _currentTerritoriesVersion;
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

            _cachedForceBiomeTerritories = biomeList.ToArray();
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

      if (terr.Shape == TerritoryShape.Rectangle)
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

                    // Fire 10-08: paint untouched ground too, through vanilla's own GetAndCreateTerrainCompiler (what the
                    // hoe and the cultivator use), but only once the zone is fully loaded. ZNetScene.IsAreaReady is the
                    // check vanilla's teleport waits on: the zone is loaded and every object in it has spawned, so the
                    // area's existing terrain compiler is already found and never duplicated. Before, only ground that
                    // had been edited already got painted (an existing TerrainComp at the territory centre).
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
    if (_snowForced)
        {
 Shader.SetGlobalFloat("_GlobalSnowAmount", _origSnowAmount ?? 0f);
  Shader.SetGlobalFloat("_SnowAmount", _origSnowAmount ?? 0f);
      _snowForced = false;
      }
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

          // Snow mask
  bool needsSnowShader = Current.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.SnowMask);
         if (!needsSnowShader && Current.Flags.HasFlag(TerritoryFlags.ForceBiome) && Current.OverridenBiome != 0)
needsSnowShader = BiomeRequiresSnowShader((Heightmap.Biome)Current.OverridenBiome);

     if (needsSnowShader)
            {
 if (!_origSnowAmount.HasValue)
      try { _origSnowAmount = Shader.GetGlobalFloat("_GlobalSnowAmount"); } catch { _origSnowAmount = 0f; }
      
      // Set snow amount to max
      Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
        Shader.SetGlobalFloat("_SnowAmount", 1f);
        
        // The piece snow shader uses _AddSnow property which is based on world height
        // We need to set it globally to force snow on all pieces regardless of Y position
        // _AddSnow is typically calculated from world Y position vs a threshold
        Shader.SetGlobalFloat("_AddSnow", 1f);
        
       _snowForced = true;
 }
        else if (_snowForced)
      {
      Shader.SetGlobalFloat("_GlobalSnowAmount", _origSnowAmount ?? 0f);
      Shader.SetGlobalFloat("_SnowAmount", _origSnowAmount ?? 0f);
      Shader.SetGlobalFloat("_AddSnow", 0f);
          
  _snowForced = false;
      }

       // Periodic effects
            var interval = Current.PeriodicHealInterval > 0f ? Current.PeriodicHealInterval : 1f;
  if (Time.time >= _nextPeriodicTime)
            {
    if (Current.Flags.HasFlag(TerritoryFlags.PeriodicDamage) && !IsPermitted(Current, player))
   {
                 var hit = new HitData();
hit.m_damage.m_damage = Current.PeriodicDamageValue > 0 ? Current.PeriodicDamageValue : 5f; // 0.0.533: plain damage; as fire it became a slow burn (round 13: 0.2 hp in 4 s)
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
            float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            float depth = waterLevel - ground;
         bool shallowWater = player.InWater() && depth <= 4f && depth >= -1f;
   bool nearSeaIce = ground >= 28f && ground <= 32f && !player.IsSwimming();
      bool groundIce = player.IsOnGround() && !player.InWater() && !player.IsSwimming() && ground < 32f && Mathf.PerlinNoise(pos.x * 0.05f, pos.z * 0.05f) > 0.35f;

bool onIceRock = false;
  if (player.IsOnGround())
   {
   if (Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
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
            if (__instance == null) return;
            
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

        // Valheim 1.0 changed EnvMan.GetBiome to return BiomeSector - a CLASS wrapping the
        // Heightmap.Biome enum plus alt-biome data - instead of the enum itself. GetBiome hands back
        // either a shared BiomeSector.Empty* static or WorldGenerator's cached sector for that
        // position, so writing __result.Biome would mutate state the whole session shares. Return a
        // separate instance instead, cached per biome because this runs every frame.
        private static readonly System.Collections.Generic.Dictionary<Heightmap.Biome, BiomeSector> _forcedSectors
            = new System.Collections.Generic.Dictionary<Heightmap.Biome, BiomeSector>();

        private static BiomeSector ForcedSector(Heightmap.Biome biome)
        {
            if (!_forcedSectors.TryGetValue(biome, out var sector))
            {
                sector = new BiomeSector(null, biome);
                _forcedSectors[biome] = sector;
            }
            return sector;
        }

        [HarmonyPatch(typeof(EnvMan), "GetBiome")]
        [HarmonyPostfix]
        private static void EnvMan_GetBiome_Postfix(EnvMan __instance, ref BiomeSector __result)
        {
            // CRITICAL: Guard against null instance - can happen during early initialization
            if (__instance == null) return;
            
            // Fast exit if no territories are configured to force biomes
            if (!_hasAnyForceBiomeTerritories) return;
            
            try
            {
                var cam = Utils.GetMainCamera();
                if (cam == null) return;
                
                var terr = GetTerritory(cam.transform.position);
                if (terr == null) terr = Current;
                if (terr == null) return;

                // NOTE: SnowMask only sets shader globals for visual snow on objects
                // It does NOT change the biome result - weather/environment stays as original biome
                if (terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.SnowMask))
                {
                    // Only set snow shader globals - don't change biome
                    Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
                    Shader.SetGlobalFloat("_SnowAmount", 1f);
                    // Don't return - let ForceBiome handling run if applicable
                }

                if (terr.Flags.HasFlag(TerritoryFlags.ForceBiome) && terr.OverridenBiome != 0)
                {
                    __result = ForcedSector((Heightmap.Biome)terr.OverridenBiome);
                    if (BiomeRequiresSnowShader((Heightmap.Biome)terr.OverridenBiome))
                    {
                        Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
                        Shader.SetGlobalFloat("_SnowAmount", 1f);
                    }
                }
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
             try { _rebuildRenderMeshMethod.Invoke(__instance, null); }
            catch { }
         }
   }
            }
  catch { }
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













