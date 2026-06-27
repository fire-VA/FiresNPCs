using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Managers;
using Debug = UnityEngine.Debug;

namespace FiresRPGmaker.Modules.Territories
{
    [HarmonyPatch]
    internal static class TerritoryMapOverlay
    {
        private static Color[] _baseMap;
        private static Color[] _baseHeight;
        // Reusable scratch buffers � avoids ~192 MB of GC pressure per overlay call
        private static Color[] _scratchMap;
        private static Color[] _scratchHeight;
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
            if (_baseMap == null)
            {
                Debug.Log("[FiresRPGmaker] ApplyOverlay skipped - _baseMap is null, attempting to capture...");
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

            int len = _baseMap.Length;

            // Reuse scratch buffers to avoid ~128-192 MB of GC pressure per call
            if (_scratchMap == null || _scratchMap.Length != len)
                _scratchMap = new Color[len];
            Array.Copy(_baseMap, _scratchMap, len);

            Color[] heightColors = null;
            bool hasHeight = _baseHeight != null && heightTex != null;
            if (hasHeight)
            {
                if (_scratchHeight == null || _scratchHeight.Length != _baseHeight.Length)
                    _scratchHeight = new Color[_baseHeight.Length];
                Array.Copy(_baseHeight, _scratchHeight, _baseHeight.Length);
                heightColors = _scratchHeight;
            }

            // Read live fog directly into a local � no base snapshot needed
            Color[] fogColors = null;
            if (fogTex != null)
                fogColors = fogTex.GetPixels();

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
                            var c = _scratchMap[idx];
                            // Tint with icy blue
                            Color band = new Color(0.55f, 0.7f, 0.9f, 0.2f);
                            c = Color.Lerp(c, band, 0.6f);
                            _scratchMap[idx] = c;
                            if (heightColors != null)
                            {
                                var hc = heightColors[idx];
                                hc.r = Mathf.Clamp(hc.r, 29f, 89f);
                                heightColors[idx] = hc;
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
                    _scratchMap[idx] = c;

                    if (terr.ShowExternalWater && heightColors != null)
                    {
                        var hc = heightColors[idx];
                        hc.r = Mathf.Clamp(hc.r, 29f, 89f);
                        heightColors[idx] = hc;
                    }
                    if (hasReveal && fogColors != null)
                    {
                        var f = fogColors[idx];
                        f.r = 0f;
                        fogColors[idx] = f;
                    }
                }
            }
        }

            mapTex.SetPixels(_scratchMap);
            mapTex.Apply(false);
            if (heightColors != null && heightTex != null)
            {
                heightTex.SetPixels(heightColors);
                heightTex.Apply(false);
            }
            if (fogColors != null && fogTex != null)
            {
                fogTex.SetPixels(fogColors);
                fogTex.Apply(false);
            }
            _overlayDirty = false;
            sw.Stop();
            Debug.Log($"[FiresRPGmaker] Territory overlay applied to minimap - drew {drawnCount} territories in {sw.ElapsedMilliseconds}ms");
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
    _baseMap = map.GetPixels();
    _baseHeight = ht != null ? ht.GetPixels() : null;
    // fog snapshot removed � live fog is read each ApplyOverlay call
    _baseTexturesCaptured = true;
    Debug.Log($"[FiresRPGmaker] Base textures captured successfully - map size: {_baseMap.Length}");
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
