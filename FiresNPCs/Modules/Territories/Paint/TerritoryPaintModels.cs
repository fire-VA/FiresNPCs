using UnityEngine;

namespace FiresRPGmaker.Modules.Territories.Paint
{
    /// <summary>What the next painted stroke becomes. Flood-fill = draw an outline, interior auto-fills with
    /// 15m circles; Road = draw a line, a strip of circles; Circle/Square = click-drag a single shape.</summary>
    public enum TerritoryPaintType { FloodFill, Road, Circle, Square }

    /// <summary>The admin's active brush — set once in the docked palette, reused for every stroke.</summary>
    public class TerritoryBrush
    {
        public string Name = "territory";
        public TerritoryPaintType Type = TerritoryPaintType.FloodFill;
        public int R = 60, G = 180, B = 220;   // brush color
        public int Alpha = 80;                 // territory fill alpha (KG default)
        public int Radius = 30;                // circle/square radius (world m); road half-width
        public string Flags = "None";          // pre-built KG flag line, e.g. "NoBuild|PvpOnly|MoveSpeedMultiplier=1.5"
    }

    /// <summary>Large-map cursor↔world helpers, zoom/pan-correct. Mirrors the private Minimap.ScreenToWorldPoint
    /// via Minimap's public fields (m_mapImageLarge.uvRect + m_textureSize + m_pixelSize) — copied verbatim from
    /// GuildMapFeatures so a screen pixel resolves to the right world point at any zoom/pan.</summary>
    public static class MapPaintMath
    {
        public static bool IsLargeMapOpen()
            => Minimap.instance != null && Minimap.instance.m_mode == Minimap.MapMode.Large;

        public static bool CursorOverMap()
        {
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null) return false;
            var rt = mm.m_mapImageLarge.transform as RectTransform;
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, ZInput.pointerPosition, null);
        }

        /// <summary>Cursor → flat world XZ on the open large map. Returns false if the cursor isn't over the map.</summary>
        public static bool CursorToWorld(out Vector3 world)
        {
            world = Vector3.zero;
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null) return false;
            var rt = mm.m_mapImageLarge.transform as RectTransform;
            if (rt == null) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, ZInput.pointerPosition, null, out var localPt)) return false;
            Vector2 norm = Rect.PointToNormalized(rt.rect, localPt);
            Rect uv = mm.m_mapImageLarge.uvRect;
            float mx = uv.xMin + norm.x * uv.width;
            float my = uv.yMin + norm.y * uv.height;
            int half = mm.m_textureSize / 2;
            float wx = (mx * mm.m_textureSize - half) * mm.m_pixelSize;
            float wz = (my * mm.m_textureSize - half) * mm.m_pixelSize;
            world = new Vector3(wx, 0f, wz);
            return true;
        }

        /// <summary>World XZ → an anchoredPosition inside the large-map image rect (anchor bottom-left), for
        /// placing UI preview markers. Inverse of <see cref="CursorToWorld"/>; respects current zoom/pan.</summary>
        public static bool WorldToMapLocal(Vector3 world, out Vector2 anchored, out RectTransform mapRect)
        {
            anchored = Vector2.zero; mapRect = null;
            var mm = Minimap.instance;
            if (mm == null || mm.m_mapImageLarge == null) return false;
            mapRect = mm.m_mapImageLarge.transform as RectTransform;
            if (mapRect == null) return false;
            int half = mm.m_textureSize / 2;
            float mxn = (world.x / mm.m_pixelSize + half) / mm.m_textureSize;
            float myn = (world.z / mm.m_pixelSize + half) / mm.m_textureSize;
            Rect uv = mm.m_mapImageLarge.uvRect;
            float nx = (mxn - uv.xMin) / uv.width;
            float ny = (myn - uv.yMin) / uv.height;
            var r = mapRect.rect;
            anchored = new Vector2(nx * r.width, ny * r.height);
            return true;
        }
    }
}
