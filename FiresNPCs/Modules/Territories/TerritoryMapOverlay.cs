using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;
using UnityEngine;
using FiresRPGmaker.Managers;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    [HarmonyPatch]
    internal static class TerritoryMapOverlay
    {
        // The overlay keeps its own copy of the untouched minimap so each pass can rebuild from it, plus a scratch
        // copy it paints into. Compact mode stores those in the textures' own formats: the map as Color32 (the map
        // texture is RGB24, so this is exact) and the height as raw RHalf bits, converted only for the handful of
        // pixels a territory clamps. On a 2048 map that is ~50 MB held instead of ~270 MB as Color[], which is four
        // arrays of 16 bytes a pixel. Legacy mode keeps the original Color[] pair so the two can be compared in
        // game; only one set is ever allocated, and switching re-captures.
        private static Color[] _baseMap;
        private static Color[] _baseHeight;
        private static Color[] _scratchMap;
        private static Color[] _scratchHeight;
        private static Color32[] _baseMap32;
        private static Color32[] _scratchMap32;
        private static ushort[] _baseHeightRaw;
        private static ushort[] _scratchHeightRaw;
        private static bool _capturedCompact;
        // True while the height texture holds pixels this overlay clamped: the next pass has to upload the restored
        // scratch even if it clamps nothing itself, or the old clamp stays on screen.
        private static bool _heightDirtyOnTexture;
        private static readonly List<GameObject> _visualizers = new();
        private static Material _vizMat;
        private static bool _visualizerDisabled;
        private static bool _overlayDirty;
        private static bool _baseTexturesCaptured;

        private static bool OverlayEnabled => ConfigManager.Instance?.configTerritoryOverlay?.Value ?? true;
        private static bool DeepNorthOverlayEnabled => ConfigManager.Instance?.configDeepNorthMapOverlay?.Value ?? true;

        private static Texture2D GetTex(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) as Texture2D;
        private static float GetFloat(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) is float f ? f : 1f;
        private static int GetInt(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) is int i ? i : 2048;

        private static bool VisualizersEnabled => ConfigManager.Instance?.configTerritoryVisualizers?.Value ?? false;
        private static bool CompactBuffers => ConfigManager.Instance?.configCompactMapOverlayBuffers?.Value ?? true;

        // Map pixel as the paint code wants it (float Color), whichever buffer is live.
        private static Color ReadScratchMap(int idx) => _scratchMap32 != null ? (Color)_scratchMap32[idx] : _scratchMap[idx];

        private static void WriteScratchMap(int idx, Color value)
        {
            if (_scratchMap32 != null) _scratchMap32[idx] = value;
            else _scratchMap[idx] = value;
        }

        /// <summary>Clamps one height pixel into the range the water line needs, in whichever buffer is live.
        /// Raw mode converts the single pixel rather than the whole 4-million-pixel array.</summary>
        private static void ClampScratchHeight(int idx, float min, float max)
        {
            if (_scratchHeightRaw != null)
            {
                float metres = Mathf.HalfToFloat(_scratchHeightRaw[idx]);
                float clamped = Mathf.Clamp(metres, min, max);
                if (clamped != metres) _scratchHeightRaw[idx] = Mathf.FloatToHalf(clamped);
                return;
            }
            var hc = _scratchHeight[idx];
            hc.r = Mathf.Clamp(hc.r, min, max);
            _scratchHeight[idx] = hc;
        }

        public static void OnTerritoriesChanged()
        {
 var sw = Stopwatch.StartNew();
    var territories = GetTerritories();
     int count = territories?.Count() ?? 0;
  Debug.Log($"[FiresRPGmaker] TerritoryMapOverlay.OnTerritoriesChanged called - {count} territories, baseTexturesCaptured={_baseTexturesCaptured}, overlayEnabled={OverlayEnabled}");
         
     _overlayDirty = true;
          
        // If we haven't captured base textures yet but minimap exists, try now
            if (!_baseTexturesCaptured && Minimap.instance != null)
            {
                if (TryCaptureBaseTextures())
      {
   Debug.Log("[FiresRPGmaker] Base textures captured on territory change");
      }
   }
            
 MaybeApplyOverlay();
            RebuildVisualizers();
            sw.Stop();
            Debug.Log($"[FiresRPGmaker] TerritoryMapOverlay.OnTerritoriesChanged completed in {sw.ElapsedMilliseconds}ms");
  }

     public static void ForceRedraw()
        {
  var sw = Stopwatch.StartNew();
         Debug.Log("[FiresRPGmaker] TerritoryMapOverlay.ForceRedraw called");
      _overlayDirty = true;
            MaybeApplyOverlay();
        RebuildVisualizers();
            sw.Stop();
            Debug.Log($"[FiresRPGmaker] TerritoryMapOverlay.ForceRedraw completed in {sw.ElapsedMilliseconds}ms");
        }

        private static void MaybeApplyOverlay()
        {
    if (!OverlayEnabled)
     {
       _overlayDirty = false;
            return;
  }
            // Per-server paint gate: a pure client must not paint until the server
            // has confirmed this session's territories, so a foreign/stale cache can
            // never flash when one r2modman profile is reused across servers. The
            // server/host is authoritative (uses TerritoryLedger) and is never gated.
            if (ZNet.instance != null && !ZNet.instance.IsServer()
                && !TerritorySyncedState.HasReceivedThisSession)
            {
                Debug.Log("[FiresRPGmaker] MaybeApplyOverlay deferred - awaiting server territory confirmation");
                return;
            }
   var mm = Minimap.instance;
       if (mm == null)
            {
   Debug.Log("[FiresRPGmaker] MaybeApplyOverlay skipped - Minimap.instance is null");
   return;
     }
            var modeField = AccessTools.Field(typeof(Minimap), "m_mode");
      var mode = modeField != null ? (Minimap.MapMode)modeField.GetValue(mm) : Minimap.MapMode.Small;
            if (mode == Minimap.MapMode.None)
     {
      // Map is closed, but still apply overlay so it's ready when opened
       }
   ApplyOverlay();
        }

  private static IEnumerator DelayedTextureCapture()
      {
          yield return new WaitForSeconds(2f);
            if (!OverlayEnabled) yield break;
            Debug.Log("[FiresRPGmaker] Attempting delayed texture capture...");
        if (TryCaptureBaseTextures())
        {
  _overlayDirty = true;
        MaybeApplyOverlay();
          RebuildVisualizers();
        }
            else
         {
          Debug.LogWarning("[FiresRPGmaker] Failed to capture base minimap textures after delay");
         }
        }

        private static void ApplyOverlay()
        {
            var sw = Stopwatch.StartNew();
            if (!OverlayEnabled)
            {
                _overlayDirty = false;
                return;
            }
            var mm = Minimap.instance;
            if (mm == null)
            {
                Debug.Log("[FiresRPGmaker] ApplyOverlay skipped - Minimap.instance is null");
                return;
            }
            // Flipping CompactMapOverlayBuffers takes effect on the next pass: the base copy is re-taken in the
            // other form and the old buffers are dropped, so an A/B comparison needs no relog.
            if (_baseTexturesCaptured && _capturedCompact != CompactBuffers)
            {
                Debug.Log($"[FiresRPGmaker] Map overlay buffers switching to {(CompactBuffers ? "compact" : "legacy")} - re-capturing base textures");
                ReleaseBuffers();
                _baseTexturesCaptured = false;
            }
            if (BasePixelCount() == 0)
            {
                Debug.Log("[FiresRPGmaker] ApplyOverlay skipped - no base map captured, attempting to capture...");
                if (TryCaptureBaseTextures())
                {
                    Debug.Log("[FiresRPGmaker] Successfully captured base textures in ApplyOverlay");
                }
                else
                {
                    return;
                }
            }
            if (!_overlayDirty)
            {
                return;
            }

            var territories = GetTerritories();
            int territoryCount = territories?.Count() ?? 0;

            var mapTex = GetTex(mm, "m_mapTexture");
            var heightTex = GetTex(mm, "m_heightTexture");
            var fogTex = GetTex(mm, "m_fogTexture");
            if (mapTex == null)
            {
                Debug.LogWarning("[FiresRPGmaker] ApplyOverlay skipped - m_mapTexture is null");
                return;
            }

            int len = BasePixelCount();

            // Scratch buffers are reused and refilled from the base copy, so a pass never allocates them again.
            if (_baseMap32 != null)
            {
                if (_scratchMap32 == null || _scratchMap32.Length != len) _scratchMap32 = new Color32[len];
                Array.Copy(_baseMap32, _scratchMap32, len);
            }
            else
            {
                if (_scratchMap == null || _scratchMap.Length != len) _scratchMap = new Color[len];
                Array.Copy(_baseMap, _scratchMap, len);
            }

            bool hasHeight = heightTex != null && (_baseHeightRaw != null || _baseHeight != null);
            if (hasHeight && _baseHeightRaw != null)
            {
                if (_scratchHeightRaw == null || _scratchHeightRaw.Length != _baseHeightRaw.Length)
                    _scratchHeightRaw = new ushort[_baseHeightRaw.Length];
                Array.Copy(_baseHeightRaw, _scratchHeightRaw, _baseHeightRaw.Length);
            }
            else if (hasHeight)
            {
                if (_scratchHeight == null || _scratchHeight.Length != _baseHeight.Length)
                    _scratchHeight = new Color[_baseHeight.Length];
                Array.Copy(_baseHeight, _scratchHeight, _baseHeight.Length);
            }
            bool heightTouched = false;

            // Live fog, read and written through the texture's own buffer. GetPixels() built a 64 MB Color[] on
            // EVERY call (2048x2048 at 16 B/px) to change a handful of pixels - the biggest allocation in a
            // profiled run. Vanilla creates the fog as R8G8_UNorm, falling back to RGBA32, so the stride is
            // derived from the texture rather than assumed, and only byte 0 (the fog amount) is touched.
            NativeArray<byte> fogData = default;
            int fogStride = 0;
            bool fogTouched = false;
            if (fogTex != null)
            {
                try
                {
                    int fogPixels = fogTex.width * fogTex.height;
                    fogData = fogTex.GetPixelData<byte>(0);
                    if (fogPixels > 0) fogStride = fogData.Length / fogPixels;
                }
                catch (Exception ex)
                {
                    fogStride = 0;
                    Debug.LogWarning($"[FiresRPGmaker] fog pixels unavailable, territory reveal skipped this pass: {ex.Message}");
                }
            }

            float pixelSize = GetFloat(mm, "m_pixelSize");
            int texSize = GetInt(mm, "m_textureSize");
            int num = texSize / 2;
            float num2 = pixelSize / 2f;

            // Deep North ocean band overlay (visual cue similar to Ashlands)
            if (DeepNorthOverlayEnabled)
            {
                for (int y = 0; y < texSize; y++)
                {
                    for (int x = 0; x < texSize; x++)
                    {
                        float wx = (x - num) * pixelSize;
                        float wz = (y - num) * pixelSize;
                        if (WorldGenerator.IsDeepnorth(wx, wz))
                        {
                            int idx = y * texSize + x;
                            Color c = ReadScratchMap(idx);
                            // Tint with icy blue
                            Color band = new Color(0.55f, 0.7f, 0.9f, 0.2f);
                            c = Color.Lerp(c, band, 0.6f);
                            WriteScratchMap(idx, c);
                            if (hasHeight)
                            {
                                ClampScratchHeight(idx, 29f, 89f);
                                heightTouched = true;
                            }
                        }
                    }
                }
            }

        int drawnCount = 0;
        foreach (var terr in territories.Where(t => t.DrawOnMap()).OrderBy(t => t.Priority))
        {
            drawnCount++;
            int startY, endY;
            switch (terr.Shape)
            {
                case TerritoryShape.Rectangle:
                    startY = Mathf.RoundToInt((terr.Y[0] + num2) / pixelSize + num);
                    endY = Mathf.RoundToInt((terr.Y[0] + terr.Y[1] + num2) / pixelSize + num);
                    break;
                default:
                    startY = Mathf.RoundToInt((terr.Y[0] - terr.Radius + num2) / pixelSize + num);
                    endY = Mathf.RoundToInt((terr.Y[0] + terr.Radius + num2) / pixelSize + num);
                    break;
            }
            startY = Mathf.Clamp(startY, 0, texSize);
            endY = Mathf.Clamp(endY, 0, texSize);

            bool hasReveal = terr.AdditionalFlags.HasFlag(AdditionalTerritoryFlags.RevealOnMap);

            for (int y = startY; y < endY; y++)
            {
                int startX, endX;
                switch (terr.Shape)
                {
                    case TerritoryShape.Rectangle:
                        startX = Mathf.RoundToInt((terr.X[0] + num2) / pixelSize + num);
                        endX = Mathf.RoundToInt((terr.X[0] + terr.X[1] + num2) / pixelSize + num);
                        break;
                    case TerritoryShape.Square:
                        startX = Mathf.RoundToInt((terr.X[0] - terr.Radius + num2) / pixelSize + num);
                        endX = Mathf.RoundToInt((terr.X[0] + terr.Radius + num2) / pixelSize + num);
                        break;
                    case TerritoryShape.Circle:
                    default:
                        float halfWidth = Mathf.Cos(Mathf.Asin(((y - num) * pixelSize - num2 - terr.Y[0]) / terr.Radius)) * terr.Radius;
                        startX = Mathf.RoundToInt((terr.X[0] - halfWidth + num2) / pixelSize + num);
                        endX = Mathf.RoundToInt((terr.X[0] + halfWidth + num2) / pixelSize + num);
                        break;
                }
                startX = Mathf.Clamp(startX, 0, texSize);
                endX = Mathf.Clamp(endX, 0, texSize);

                for (int x = startX; x < endX; x++)
                {
                    int idx = y * texSize + x;
                    Color c;
                    if (terr.UsingGradient())
                    {
                        var worldPos = new Vector2((x - num) * pixelSize, (y - num) * pixelSize);
                        c = terr.GradientType switch
                        {
                            GradientType.FromCenter => terr.GetGradientFromCenter(worldPos),
                            GradientType.ToCenter => terr.GetGradientFromCenter(worldPos, true),
                            GradientType.LeftRight => terr.GetGradientX(worldPos.x),
                            GradientType.RightLeft => terr.GetGradientX(worldPos.x, true),
                            GradientType.BottomTop => terr.GetGradientY(worldPos.y),
                            GradientType.TopBottom => terr.GetGradientY(worldPos.y, true),
                            GradientType.BottomLeftTopRight => terr.GetGradientXY(worldPos),
                            GradientType.TopRightBottomLeft => terr.GetGradientXY(worldPos, true),
                            GradientType.BottomRightTopLeft => terr.GetGradientXY(worldPos),
                            GradientType.TopLeftBottomRight => terr.GetGradientXY(worldPos, true),
                            _ => terr.GetColor()
                        };
                    }
                    else
                    {
                        c = terr.GetColor();
                    }
                    if (c.a <= 0f) c.a = 0.31f;
                    WriteScratchMap(idx, c);

                    if (terr.ShowExternalWater && hasHeight)
                    {
                        ClampScratchHeight(idx, 29f, 89f);
                        heightTouched = true;
                    }
                    if (hasReveal && fogStride > 0)
                    {
                        fogData[idx * fogStride] = 0;
                        fogTouched = true;
                    }
                }
            }
        }

            if (_scratchMap32 != null) mapTex.SetPixels32(_scratchMap32);
            else mapTex.SetPixels(_scratchMap);
            mapTex.Apply(false);

            // Upload the height when this pass clamped something, and once more after a pass that did not, to put
            // back what the last one changed.
            if (hasHeight && (heightTouched || _heightDirtyOnTexture))
            {
                if (_scratchHeightRaw != null) heightTex.GetPixelData<ushort>(0).CopyFrom(_scratchHeightRaw);
                else heightTex.SetPixels(_scratchHeight);
                heightTex.Apply(false);
                _heightDirtyOnTexture = heightTouched;
            }
            // Only upload when a territory actually revealed something; the pixels are already in the texture's buffer.
            if (fogTouched) fogTex.Apply(false);
            _overlayDirty = false;
            sw.Stop();
            Debug.Log($"[FiresRPGmaker] Territory overlay applied to minimap - drew {drawnCount} territories in {sw.ElapsedMilliseconds}ms " +
                      $"({(_capturedCompact ? "compact" : "legacy")} buffers, {HeldMegabytes():F0} MB held)");
        }

        private static int BasePixelCount() => _baseMap32?.Length ?? _baseMap?.Length ?? 0;

        /// <summary>What the overlay's four buffers hold, for comparing the two modes in a run.</summary>
        private static float HeldMegabytes()
        {
            long bytes = 0L;
            if (_baseMap != null) bytes += (long)_baseMap.Length * 16;
            if (_scratchMap != null) bytes += (long)_scratchMap.Length * 16;
            if (_baseHeight != null) bytes += (long)_baseHeight.Length * 16;
            if (_scratchHeight != null) bytes += (long)_scratchHeight.Length * 16;
            if (_baseMap32 != null) bytes += (long)_baseMap32.Length * 4;
            if (_scratchMap32 != null) bytes += (long)_scratchMap32.Length * 4;
            if (_baseHeightRaw != null) bytes += (long)_baseHeightRaw.Length * 2;
            if (_scratchHeightRaw != null) bytes += (long)_scratchHeightRaw.Length * 2;
            return bytes / (1024f * 1024f);
        }

        private static void ReleaseBuffers()
        {
            _baseMap = _scratchMap = _baseHeight = _scratchHeight = null;
            _baseMap32 = _scratchMap32 = null;
            _baseHeightRaw = _scratchHeightRaw = null;
            _heightDirtyOnTexture = false;
        }

        private static IEnumerable<Territory> GetTerritories()
        {
  if (ZNet.instance != null && ZNet.instance.IsServer())
{
           return TerritoryLedger.Territories;
            }
        return TerritorySyncedState.ClientTerritories;
        }

        private static bool TryCaptureBaseTextures()
        {
   if (!OverlayEnabled) return false;
   var mm = Minimap.instance;
     if (mm == null)
  {
       Debug.Log("[FiresRPGmaker] TryCaptureBaseTextures failed - Minimap.instance is null");
          return false;
            }
         var map = GetTex(mm, "m_mapTexture");
            var ht = GetTex(mm, "m_heightTexture");
       var fog = GetTex(mm, "m_fogTexture");
   if (map == null)
            {
  Debug.Log("[FiresRPGmaker] TryCaptureBaseTextures failed - m_mapTexture is null");
          return false;
          }
 try
        {
            bool compact = CompactBuffers;
            ReleaseBuffers();
            // fog snapshot removed - live fog is read each ApplyOverlay call
            if (compact)
            {
                _baseMap32 = map.GetPixels32();
                // RHalf is one ushort per pixel, so the raw buffer copies straight out and back with no conversion.
                if (ht != null && ht.format == TextureFormat.RHalf) _baseHeightRaw = ht.GetPixelData<ushort>(0).ToArray();
                else if (ht != null) _baseHeight = ht.GetPixels();
            }
            else
            {
                _baseMap = map.GetPixels();
                _baseHeight = ht != null ? ht.GetPixels() : null;
            }
            _capturedCompact = compact;
            _baseTexturesCaptured = true;
            Debug.Log($"[FiresRPGmaker] Base textures captured successfully - map size: {BasePixelCount()}, " +
                      $"buffers {(compact ? "compact" : "legacy")} holding {HeldMegabytes():F0} MB");
       return true;
  }
          catch (Exception ex)
            {
             Debug.LogWarning($"[FiresRPGmaker] TryCaptureBaseTextures exception: {ex.Message}");
  return false;
            }
        }

      private static void EnsureMaterial()
        {
            if (_visualizers.Count > 0 && _vizMat != null) return;
      if (_visualizerDisabled) return;
          var shaderNames = new[]
            {
        "Particles/Standard Unlit",
           "Legacy Shaders/Particles/Alpha Blended",
          "Legacy Shaders/Transparent/Diffuse",
            "Standard",
                "Unlit/Color",
      "Hidden/Internal-Colored"
   };
      Shader shader = null;
            foreach (var name in shaderNames)
          {
     shader = Shader.Find(name);
                if (shader != null) break;
            }
          if (shader == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
      {
      _visualizerDisabled = true;
  Debug.LogWarning("[FiresRPGmaker] Territory visualizers disabled - no suitable shader found and/or graphics device is Null.");
   return;
      }
          Debug.Log($"[FiresRPGmaker] Territory visualizers using shader: {shader.name}");
            _vizMat = new Material(shader);
      _vizMat.SetFloat("_Mode", 3);
       _vizMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _vizMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
    _vizMat.SetInt("_ZWrite", 0);
 _vizMat.DisableKeyword("_ALPHATEST_ON");
     _vizMat.EnableKeyword("_ALPHABLEND_ON");
  _vizMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
      _vizMat.renderQueue = 3000;
      }

        private static void RebuildVisualizers()
 {
            foreach (var v in _visualizers) { if (v) UnityEngine.Object.Destroy(v); }
            _visualizers.Clear();
      if (!VisualizersEnabled)
{
 return; // world visualizers disabled by config
       }
            EnsureMaterial();
      if (_visualizerDisabled || _vizMat == null) return; // shader not available
        foreach (var terr in GetTerritories())
 {
         if (terr.Colors.Count == 0) continue;
        var go = GameObject.CreatePrimitive(terr.Shape == TerritoryShape.Circle ? PrimitiveType.Cylinder : PrimitiveType.Cube);
  UnityEngine.Object.Destroy(go.GetComponent<Collider>());
                go.name = "TerritoryViz_" + terr.Name;
var pos = terr.Shape switch
          {
          TerritoryShape.Rectangle => terr.Pos3D() + new Vector3(terr.X[1] * 0.5f, 0, terr.Y[1] * 0.5f),
   _ => terr.Pos3D()
            };
                go.transform.position = pos;
             float rawHeight = terr.HeightBounds.Item2 - terr.HeightBounds.Item1;
       float height = Mathf.Clamp(rawHeight, 1f, 200f); // clamp to avoid sky-high columns
            go.transform.localScale = terr.Shape switch
     {
          TerritoryShape.Rectangle => new Vector3(Mathf.Max(1, terr.X.Length > 1 ? terr.X[1] : 1), height, Mathf.Max(1, terr.Y.Length > 1 ? terr.Y[1] : 1)),
        _ => new Vector3(Mathf.Max(1, terr.Radius * 2f), height, Mathf.Max(1, terr.Radius * 2f)),
      };
            var rend = go.GetComponent<Renderer>();
   rend.sharedMaterial = _vizMat;
    var c = terr.GetColor(); if (c.a <= 0) c.a = (byte)(0.4f * 255f); rend.material.color = c;
  _visualizers.Add(go);
            }
      }

        [HarmonyPatch(typeof(Minimap), "SetMapMode")] // trigger when map opens/closes
     [HarmonyPostfix]
     private static void Minimap_SetMapMode_Postfix(Minimap __instance)
        {
          if (_overlayDirty)
          {
        MaybeApplyOverlay();
    }
     }

        [HarmonyPatch(typeof(Minimap), "Start")]
        [HarmonyPostfix]
        private static void Minimap_Start_Postfix(Minimap __instance)
        {
            Debug.Log("[FiresRPGmaker] Minimap_Start_Postfix triggered");
            if (!TryCaptureBaseTextures())
   {
 if (OverlayEnabled)
 Minimap.instance?.StartCoroutine(DelayedTextureCapture());
 }
            else
    {
                _overlayDirty = true;
       MaybeApplyOverlay();
            }
        }

    [HarmonyPatch(typeof(Minimap), "LoadMapData")]
        [HarmonyPostfix]
 private static void Minimap_LoadMapData_Postfix(Minimap __instance)
        {
            Debug.Log("[FiresRPGmaker] Minimap_LoadMapData_Postfix triggered");
      if (!TryCaptureBaseTextures())
          {
      if (OverlayEnabled)
         Minimap.instance?.StartCoroutine(DelayedTextureCapture());
            }
            else
            {
         _overlayDirty = true;
    MaybeApplyOverlay();
            }
 }

 [HarmonyPatch(typeof(Minimap), "GenerateWorldMap")]
[HarmonyPostfix]
        private static void Minimap_GenerateWorldMap_Postfix(Minimap __instance)
        {
            Debug.Log("[FiresRPGmaker] Minimap_GenerateWorldMap_Postfix triggered");
       if (!TryCaptureBaseTextures())
  {
      if (OverlayEnabled)
         Minimap.instance?.StartCoroutine(DelayedTextureCapture());
      }
            else
      {
         _overlayDirty = true;
     MaybeApplyOverlay();
}
  }
    }
}
