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
        // ── Polygon rows are filled by even-odd crossings, one pass over the edges per row ──
        private static readonly List<float> s_polyCross = new List<float>(16);
        private static void PolyRowCrossings(Territory t, float worldY, int num, float num2, float pixelSize)
        {
            s_polyCross.Clear();
            int n = t.X.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float yi = t.Y[i], yj = t.Y[j];
                if ((yi > worldY) == (yj > worldY)) continue;
                float wx = t.X[i] + (worldY - yi) * (t.X[j] - t.X[i]) / (yj - yi);
                s_polyCross.Add((wx + num2) / pixelSize + num);
            }
            s_polyCross.Sort();
        }
        private static bool PolyPixelInside(int x)
        {
            float px = x + 0.5f;
            int left = 0;
            for (int k = 0; k < s_polyCross.Count && s_polyCross[k] < px; k++) left++;
            return (left & 1) == 1;
        }

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
        // The Deep North band is a pure function of world coordinates: IsDeepnorth is static, reads no world
        // state, and its answer never changes in a session. It used to be evaluated for EVERY pixel on EVERY
        // pass - 4,194,304 atan2+sin+sqrt calls on a 2048 map - which is why a pass cost the same ~580 ms with
        // 0 territories as with 290 ([99be3d] measured 573/577/579/581/657). The pixel indices are computed once
        // per texture geometry, off the main thread when possible, and walked directly after that.
        private static readonly Color DeepNorthBand = new Color(0.55f, 0.7f, 0.9f, 0.2f);
        private static int[] _deepNorthIdx;
        private static int _deepNorthTexSize;
        private static float _deepNorthPixelSize;
        private static volatile int[] _deepNorthBuilt;
        private static bool _deepNorthBuilding;
        // What the texture currently holds. A pass that would paint the same pixels again uploads nothing.
        private static long _appliedSignature;
        private static bool _applyScheduled;
        // The band is static, and so is the base copy, so it belongs IN the base rather than being re-walked
        // every pass. Baked once the indices exist; a config flip re-captures, which un-bakes it.
        private static bool _baseBandBaked;
        private static bool _capturedDeepNorth;
        private static bool _forceHeightUpload;

        private static bool OverlayEnabled => ConfigManager.Instance?.configTerritoryOverlay?.Value ?? true;
        private static bool DeepNorthOverlayEnabled => ConfigManager.Instance?.configDeepNorthMapOverlay?.Value ?? true;

        private static Texture2D GetTex(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) as Texture2D;
        private static float GetFloat(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) is float f ? f : 1f;
        private static int GetInt(Minimap mm, string name) => AccessTools.Field(mm.GetType(), name)?.GetValue(mm) is int i ? i : 2048;

        private static bool VisualizersEnabled => ConfigManager.Instance?.configTerritoryVisualizers?.Value ?? false;
        private static bool CompactBuffers => ConfigManager.Instance?.configCompactMapOverlayBuffers?.Value ?? true;

        // Map pixel as the paint code wants it (float Color), whichever buffer is live.
        private static Color ReadScratchMap(int idx) => _scratchMap32 != null ? (Color)_scratchMap32[idx] : _scratchMap[idx];

        // The forced biome of an area (0 = none), and vanilla's map colour for a biome (Minimap.GetPixelColor, by reflection)
        private static int ForcedMapBiome(Territory t) => t != null && t.Flags.HasFlag(TerritoryFlags.ForceBiome) ? t.OverridenBiome : 0;
        private static MethodInfo s_pixelColor; private static bool s_pixelColorLooked;
        private static Color BiomeMapColor(int biome)
        {
            if (!s_pixelColorLooked) { s_pixelColorLooked = true; s_pixelColor = AccessTools.Method(typeof(Minimap), "GetPixelColor", new[] { typeof(Heightmap.Biome) }); }
            try { if (s_pixelColor != null) return (Color)s_pixelColor.Invoke(s_pixelColor.IsStatic ? null : Minimap.instance, new object[] { (Heightmap.Biome)biome }); } catch { }
            return Color.white;
        }

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

        // Territory sync delivers in chunks and fires this once per message, so a single sync used to repaint the
        // whole minimap several times over. Passes are coalesced to at most one per frame through Core's
        // dispatcher, and the pass itself no-ops when the pixels would be identical.
        public static void OnTerritoriesChanged()
        {
            _overlayDirty = true;

            if (!_baseTexturesCaptured && Minimap.instance != null && TryCaptureBaseTextures())
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Base textures captured on territory change");

            ScheduleApply();
        }

        /// <summary>Add-only, for another mod that recolours the map (Vedr's seasons): RPGM paints territories into m_mapTexture from its own base copy,
        /// so each mod's upload would overwrite the other. Give the new map here instead of uploading it. Copied; the Deep North band goes back on and
        /// territories repaint and upload, at most once a frame. Height is untouched. False: nothing taken (overlay off, no map, wrong size), so upload it yourself.</summary>
        public static bool SetBaseMap(Color32[] pixels)
        {
            if (pixels == null || !OverlayEnabled || Minimap.instance == null) return false;
            if (!_baseTexturesCaptured && !TryCaptureBaseTextures()) return false;
            if (pixels.Length != BasePixelCount()) { global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] SetBaseMap refused: {pixels.Length} pixels, base has {BasePixelCount()}"); return false; }
            if (_baseMap32 != null) System.Array.Copy(pixels, _baseMap32, pixels.Length);
            else for (int i = 0; i < pixels.Length; i++) _baseMap[i] = pixels[i];
            _baseBandBaked = false;
            _appliedSignature = 0L;
            _overlayDirty = true;
            ScheduleApply();
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[Territory] SetBaseMap: {pixels.Length} pixels taken");
            return true;
        }

        public static void ForceRedraw()
        {
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] TerritoryMapOverlay.ForceRedraw called");
            _overlayDirty = true;
            _appliedSignature = 0L;
            var sw = Stopwatch.StartNew();
            MaybeApplyOverlay();
            RebuildVisualizers();
            sw.Stop();
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] TerritoryMapOverlay.ForceRedraw completed in {sw.ElapsedMilliseconds}ms");
        }

        private static void ScheduleApply()
        {
            if (_applyScheduled) return;
            _applyScheduled = true;
            FiresCore.Async.MainThreadDispatcher.Enqueue(() =>
            {
                _applyScheduled = false;
                var sw = Stopwatch.StartNew();
                MaybeApplyOverlay();
                RebuildVisualizers();
                sw.Stop();
                global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] TerritoryMapOverlay.OnTerritoriesChanged completed in {sw.ElapsedMilliseconds}ms");
            });
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
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] MaybeApplyOverlay deferred - awaiting server territory confirmation");
                return;
            }
   var mm = Minimap.instance;
       if (mm == null)
            {
   global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] MaybeApplyOverlay skipped - Minimap.instance is null");
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
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Attempting delayed texture capture...");
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

        // 0.0.806 ([ghost], Fire 10-31 "spread between frames and budgeted"; "drew 505 territories in 209ms" in one frame): with
        // [Territories] Spread map overlay paint ON the same pass runs as a Core FrameBudgetScheduler coroutine that paints territory rows
        // for up to SpreadStepMs per frame, then uploads the textures once at the end (the old overlay stays until then). OFF (default) =
        // the same enumerator run to the end in this frame, i.e. exactly the old pass. A change arriving mid-pass repaints after it.
        private const double SpreadStepMs = 2.0;
        private static bool SpreadPaint => ConfigManager.Instance?.configSpreadMapOverlayPaint?.Value ?? false;
        private static bool s_spreadRunning, s_changedDuringSpread;

        private static void ApplyOverlay()
        {
            if (s_spreadRunning) { s_changedDuringSpread = true; return; }   // the running pass repaints when it ends
            if (!SpreadPaint) { var e = ApplyOverlayCo(false); while (e.MoveNext()) { } return; }
            s_spreadRunning = true; s_changedDuringSpread = false;
            FiresCore.Async.FrameBudgetScheduler.ScheduleCoroutine(FiresCore.Async.WorkPriority.Normal, ApplyOverlayCo(true), () =>
            {
                s_spreadRunning = false;
                if (s_changedDuringSpread) { s_changedDuringSpread = false; _overlayDirty = true; ScheduleApply(); }
            });
        }

        private static IEnumerator ApplyOverlayCo(bool spread)
        {
            var sw = Stopwatch.StartNew();
            var step = Stopwatch.StartNew();
            double worstStep = 0; int steps = 1;
            if (!OverlayEnabled)
            {
                _overlayDirty = false;
                yield break;
            }
            var mm = Minimap.instance;
            if (mm == null)
            {
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] ApplyOverlay skipped - Minimap.instance is null");
                yield break;
            }
            // Flipping CompactMapOverlayBuffers takes effect on the next pass: the base copy is re-taken in the
            // other form and the old buffers are dropped, so an A/B comparison needs no relog.
            if (_baseTexturesCaptured && (_capturedCompact != CompactBuffers || _capturedDeepNorth != DeepNorthOverlayEnabled))
            {
                global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Map overlay re-capturing base textures (buffers {(CompactBuffers ? "compact" : "legacy")}, deep north {(DeepNorthOverlayEnabled ? "on" : "off")})");
                ReleaseBuffers();
                _baseTexturesCaptured = false;
            }
            if (BasePixelCount() == 0)
            {
                global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] ApplyOverlay skipped - no base map captured, attempting to capture...");
                if (TryCaptureBaseTextures())
                {
                    global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Successfully captured base textures in ApplyOverlay");
                }
                else
                {
                    yield break;
                }
            }
            if (!_overlayDirty)
            {
                yield break;
            }

            var territories = GetTerritories();
            int territoryCount = territories?.Count() ?? 0;

            var mapTex = GetTex(mm, "m_mapTexture");
            var heightTex = GetTex(mm, "m_heightTexture");
            var fogTex = GetTex(mm, "m_fogTexture");
            if (mapTex == null)
            {
                Debug.LogWarning("[FiresRPGmaker] ApplyOverlay skipped - m_mapTexture is null");
                yield break;
            }

            float pixelSize = GetFloat(mm, "m_pixelSize");
            int texSize = GetInt(mm, "m_textureSize");
            int num = texSize / 2;
            float num2 = pixelSize / 2f;

            // Nothing below changes a pixel unless the territory set, the Deep North band or the texture geometry
            // changed, and a chunked sync fires this repeatedly with the SAME set. Comparing first turns those
            // repeats into no work at all instead of a 16 MB copy plus a full texture upload each.
            long signature = OverlaySignature(territories, texSize, pixelSize);
            if (signature == _appliedSignature)
            {
                _overlayDirty = false;
                global::FiresRPGmaker.FiresLogger.LogVerbose("[FiresRPGmaker] Territory overlay unchanged - skipped repaint");
                yield break;
            }

            if (DeepNorthOverlayEnabled && !_baseBandBaked)
            {
                BakeDeepNorthIntoBase(texSize, pixelSize);
                // The signature above was taken while the band was unbaked, and the bake is part of what the
                // texture will now hold. Storing the stale one would make the NEXT pass see a change that is
                // already on screen and repaint the whole map again for nothing.
                signature = OverlaySignature(territories, texSize, pixelSize);
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



            // Deep North ocean band overlay (visual cue similar to Ashlands)
            if (_forceHeightUpload)
            {
                heightTouched = true;
                _forceHeightUpload = false;
            }

        int drawnCount = 0;
        foreach (var terr in territories.Where(t => t.DrawOnMap() || ForcedMapBiome(t) != 0).OrderBy(t => t.Priority).ToList())   // Undrawn ForceBiome areas (their biome colour); 0.0.806: a snapshot, so a spread pass never enumerates a list that changed
        {
            drawnCount++;
            int startY, endY;
            switch (terr.Shape)
            {
                case TerritoryShape.Rectangle:
                    startY = Mathf.RoundToInt((terr.Y[0] + num2) / pixelSize + num);
                    endY = Mathf.RoundToInt((terr.Y[0] + terr.Y[1] + num2) / pixelSize + num);
                    break;
                case TerritoryShape.Polygon when terr.IsPolygon:
                    startY = Mathf.RoundToInt((terr.PolyMin.y + num2) / pixelSize + num);
                    endY = Mathf.RoundToInt((terr.PolyMax.y + num2) / pixelSize + num);
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
                    case TerritoryShape.Polygon when terr.IsPolygon:
                        // The row's even-odd crossings (pixel x, sorted); a pixel is drawn when an odd number lie left of it
                        startX = Mathf.RoundToInt((terr.PolyMin.x + num2) / pixelSize + num);
                        endX = Mathf.RoundToInt((terr.PolyMax.x + num2) / pixelSize + num);
                        PolyRowCrossings(terr, (y - num) * pixelSize - num2, num, num2, pixelSize);
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

                bool polyRow = terr.Shape == TerritoryShape.Polygon && terr.IsPolygon;
                for (int x = startX; x < endX; x++)
                {
                    if (polyRow && !PolyPixelInside(x)) continue;
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
                    else if (!terr.DrawOnMap())
                    {
                        // An area with no colour of its own shows the forced biome's own map colour, as vanilla paints that biome
                        c = BiomeMapColor(ForcedMapBiome(terr));
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
                // 0.0.806: spread mode yields between rows once this frame's step is spent
                if (spread && step.Elapsed.TotalMilliseconds >= SpreadStepMs)
                {
                    if (step.Elapsed.TotalMilliseconds > worstStep) worstStep = step.Elapsed.TotalMilliseconds;
                    yield return null;
                    steps++; step.Restart();
                    if (Minimap.instance != mm || mapTex == null || (fogStride > 0 && fogTex == null))
                    {
                        _scratchMap32 = null; _scratchMap = null; _scratchHeightRaw = null; _scratchHeight = null;
                        _overlayDirty = true;
                        global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug("[TerritoryOverlay] spread paint stopped: the minimap was replaced mid-pass; it repaints on the next change");
                        yield break;
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
            // The scratch copies are only the working space of this pass; the next pass rebuilds them from the base. Holding
            // them between passes kept another ~72 MB (2048² map + height) for nothing.
            _scratchMap32 = null; _scratchMap = null; _scratchHeightRaw = null; _scratchHeight = null;
            _overlayDirty = false;
            _appliedSignature = signature;
            sw.Stop();
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Territory overlay applied to minimap - drew {drawnCount} territories in {sw.ElapsedMilliseconds}ms " +
                      $"({(_capturedCompact ? "compact" : "legacy")} buffers, {HeldMegabytes():F0} MB held)");
            if (step.Elapsed.TotalMilliseconds > worstStep) worstStep = step.Elapsed.TotalMilliseconds;
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[TerritoryOverlay] paint {(spread ? "spread" : "one frame")}: {drawnCount} territories, total {sw.ElapsedMilliseconds} ms over {steps} frame(s), worst frame {worstStep:0.0} ms");
        }

        // Cheap fingerprint of everything ApplyOverlay reads. Never returns 0, so the initial _appliedSignature
        // cannot collide with a real state.
        private static long OverlaySignature(IEnumerable<Territory> territories, int texSize, float pixelSize)
        {
            unchecked
            {
                long sig = 17L;
                sig = sig * 31L + texSize;
                sig = sig * 31L + BitConverter.DoubleToInt64Bits(pixelSize);
                sig = sig * 31L + BasePixelCount();
                sig = sig * 31L + (DeepNorthOverlayEnabled ? 1L : 0L);
                sig = sig * 31L + (_deepNorthIdx != null ? _deepNorthIdx.Length : -1L);
                sig = sig * 31L + (_baseBandBaked ? 1L : 0L);

                if (territories != null)
                {
                    foreach (var terr in territories)
                    {
                        if (terr == null || !terr.DrawOnMap()) continue;
                        sig = sig * 31L + terr.UID;
                        sig = sig * 31L + (terr.Name?.GetHashCode() ?? 0);
                        sig = sig * 31L + (int)terr.Shape;
                        sig = sig * 31L + terr.Priority;
                        sig = sig * 31L + terr.Radius;
                        sig = sig * 31L + (int)terr.AdditionalFlags;
                        sig = sig * 31L + (terr.ShowExternalWater ? 1L : 0L);
                        sig = sig * 31L + (int)terr.GradientType;
                        sig = sig * 31L + terr.Colors.Count;
                        var c = terr.GetColor();
                        sig = sig * 31L + ((c.r << 24) | (c.g << 16) | (c.b << 8) | c.a);
                        if (terr.X != null) foreach (int v in terr.X) sig = sig * 31L + v;
                        if (terr.Y != null) foreach (int v in terr.Y) sig = sig * 31L + v;
                    }
                }

                return sig == 0L ? 1L : sig;
            }
        }

        /// <summary>The Deep North pixel indices for this texture geometry, building them inline only if the
        /// background build has not finished yet. Returns an empty set rather than blocking twice.</summary>
        private static int[] GetDeepNorthIndices(int texSize, float pixelSize)
        {
            var built = _deepNorthBuilt;
            if (built != null && _deepNorthTexSize == texSize && _deepNorthPixelSize == pixelSize)
            {
                _deepNorthIdx = built;
                return built;
            }

            if (_deepNorthIdx != null && _deepNorthTexSize == texSize && _deepNorthPixelSize == pixelSize)
                return _deepNorthIdx;

            var sw = Stopwatch.StartNew();
            _deepNorthIdx = BuildDeepNorthIndices(texSize, pixelSize);
            _deepNorthTexSize = texSize;
            _deepNorthPixelSize = pixelSize;
            _deepNorthBuilt = _deepNorthIdx;
            sw.Stop();
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Deep North band cached - {_deepNorthIdx.Length} pixels of {texSize * texSize} in {sw.ElapsedMilliseconds}ms (once per map geometry)");
            return _deepNorthIdx;
        }

        /// <summary>Precomputes the band off the main thread. IsDeepnorth is static and reads no world or Unity
        /// state - only Mathf - so it is safe here; vanilla calls WorldGenerator from its own heightmap thread.</summary>
        private static void BeginDeepNorthBuild(int texSize, float pixelSize)
        {
            if (_deepNorthBuilding) return;
            if (_deepNorthBuilt != null && _deepNorthTexSize == texSize && _deepNorthPixelSize == pixelSize) return;
            _deepNorthBuilding = true;
            _deepNorthTexSize = texSize;
            _deepNorthPixelSize = pixelSize;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    var built = BuildDeepNorthIndices(texSize, pixelSize);
                    _deepNorthBuilt = built;
                    sw.Stop();
                    long ms = sw.ElapsedMilliseconds;
                    FiresCore.Async.MainThreadDispatcher.Enqueue(() =>
                        global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Deep North band precomputed off-thread - {built.Length} pixels of {texSize * texSize} in {ms}ms"));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FiresRPGmaker] Deep North band precompute failed, falling back to inline: {ex.Message}");
                }
                finally
                {
                    _deepNorthBuilding = false;
                }
            });
        }

        /// <summary>Writes the band into the BASE copy once, so every later pass inherits it through the Array.Copy
        /// it already does and no pass walks the band again. The base is the overlay's own snapshot of the pristine
        /// minimap, never vanilla's texture, and a Deep North config flip re-captures it.</summary>
        private static void BakeDeepNorthIntoBase(int texSize, float pixelSize)
        {
            var band = GetDeepNorthIndices(texSize, pixelSize);
            if (band == null || band.Length == 0) return;

            var sw = Stopwatch.StartNew();
            bool height = _baseHeightRaw != null || _baseHeight != null;
            for (int i = 0; i < band.Length; i++)
            {
                int idx = band[i];
                if (_baseMap32 != null)
                {
                    if (idx >= _baseMap32.Length) continue;
                    _baseMap32[idx] = Color.Lerp(_baseMap32[idx], DeepNorthBand, 0.6f);
                }
                else if (_baseMap != null)
                {
                    if (idx >= _baseMap.Length) continue;
                    _baseMap[idx] = Color.Lerp(_baseMap[idx], DeepNorthBand, 0.6f);
                }

                if (!height) continue;
                if (_baseHeightRaw != null)
                {
                    if (idx >= _baseHeightRaw.Length) continue;
                    float metres = Mathf.HalfToFloat(_baseHeightRaw[idx]);
                    float clamped = Mathf.Clamp(metres, 29f, 89f);
                    if (clamped != metres) _baseHeightRaw[idx] = Mathf.FloatToHalf(clamped);
                }
                else if (_baseHeight != null && idx < _baseHeight.Length)
                {
                    var hc = _baseHeight[idx];
                    hc.r = Mathf.Clamp(hc.r, 29f, 89f);
                    _baseHeight[idx] = hc;
                }
            }

            _baseBandBaked = true;
            _forceHeightUpload = height;
            _appliedSignature = 0L;
            sw.Stop();
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Deep North band baked into the base map - {band.Length} pixels in {sw.ElapsedMilliseconds}ms, no per-pass band work from here");
        }

        private static int[] BuildDeepNorthIndices(int texSize, float pixelSize)
        {
            int num = texSize / 2;
            var hits = new List<int>(texSize * 8);
            for (int y = 0; y < texSize; y++)
            {
                float wz = (y - num) * pixelSize;
                int row = y * texSize;
                for (int x = 0; x < texSize; x++)
                {
                    if (WorldGenerator.IsDeepnorth((x - num) * pixelSize, wz)) hits.Add(row + x);
                }
            }
            return hits.ToArray();
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
            _appliedSignature = 0L;
            _baseBandBaked = false;
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
        return TerritoryApi.WithExternal(TerritorySyncedState.ClientTerritories); // API boxes on clients
        }

        private static bool TryCaptureBaseTextures()
        {
   if (!OverlayEnabled) return false;
   var mm = Minimap.instance;
     if (mm == null)
  {
       global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] TryCaptureBaseTextures failed - Minimap.instance is null");
          return false;
            }
         var map = GetTex(mm, "m_mapTexture");
            var ht = GetTex(mm, "m_heightTexture");
       var fog = GetTex(mm, "m_fogTexture");
   if (map == null)
            {
  global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] TryCaptureBaseTextures failed - m_mapTexture is null");
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
            _capturedDeepNorth = DeepNorthOverlayEnabled;
            _baseBandBaked = false;
            _baseTexturesCaptured = true;
            _appliedSignature = 0L;
            if (DeepNorthOverlayEnabled) BeginDeepNorthBuild(GetInt(mm, "m_textureSize"), GetFloat(mm, "m_pixelSize"));
            global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Base textures captured successfully - map size: {BasePixelCount()}, " +
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
          global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] Territory visualizers using shader: {shader.name}");
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
          TerritoryShape.Polygon when terr.IsPolygon => new Vector3(terr.Center2D().x, terr.Pos3D().y, terr.Center2D().y),   // Its bounds box
   _ => terr.Pos3D()
            };
                go.transform.position = pos;
             float rawHeight = terr.HeightBounds.Item2 - terr.HeightBounds.Item1;
       float height = Mathf.Clamp(rawHeight, 1f, 200f); // clamp to avoid sky-high columns
            go.transform.localScale = terr.Shape switch
     {
          TerritoryShape.Rectangle => new Vector3(Mathf.Max(1, terr.X.Length > 1 ? terr.X[1] : 1), height, Mathf.Max(1, terr.Y.Length > 1 ? terr.Y[1] : 1)),
          TerritoryShape.Polygon when terr.IsPolygon => new Vector3(Mathf.Max(1f, terr.PolyMax.x - terr.PolyMin.x), height, Mathf.Max(1f, terr.PolyMax.y - terr.PolyMin.y)),
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

        // The base map copy (2048² ARGB + height, ~72 MB) lived on in these statics after logout, from the main menu
        // onward. The map goes with its world now; the next world captures its own. (A dedicated server has no Minimap, so
        // it never captured any.)
        [HarmonyPatch(typeof(Minimap), "OnDestroy")]
        [HarmonyPostfix]
        private static void Minimap_OnDestroy_Postfix()
        {
            float mb = HeldMegabytes();
            ReleaseBuffers();
            _baseTexturesCaptured = false;
            _overlayDirty = false;
            if (mb > 0f) global::FiresRPGmaker.FiresLogger.LogDebug($"[FiresRPGmaker] TerritoryMapOverlay: map closed with its world; released {mb:F0} MB of map buffers");
        }

        [HarmonyPatch(typeof(Minimap), "Start")]
        [HarmonyPostfix]
        private static void Minimap_Start_Postfix(Minimap __instance)
        {
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Minimap_Start_Postfix triggered");
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
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Minimap_LoadMapData_Postfix triggered");
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
            global::FiresRPGmaker.FiresLogger.LogDebug("[FiresRPGmaker] Minimap_GenerateWorldMap_Postfix triggered");
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
