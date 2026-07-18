using System.Collections.Generic;
using UnityEngine;

namespace FiresRPGmaker.Modules.Factions
{
    /// <summary>
    /// Resolves a faction's <c>Icon</c> string to a sprite. Asset-free: the name is looked up as an ObjectDB
    /// item prefab and its inventory icon is reused, so admins point at any existing item (e.g. a banner or a
    /// sword) as the faction crest without shipping new art. Cached; null when the name doesn't resolve
    /// (callers then render text only). Shared by the editor preview and the nameplate patch.
    /// </summary>
    public static class FactionIcons
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        public static Sprite Resolve(string iconName)
        {
            if (string.IsNullOrWhiteSpace(iconName) || ObjectDB.instance == null) return null;
            string key = iconName.Trim();
            if (_cache.TryGetValue(key, out var cached)) return cached;

            Sprite sprite = null;
            try
            {
                var prefab = ObjectDB.instance.GetItemPrefab(key);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop != null && drop.m_itemData?.m_shared?.m_icons != null && drop.m_itemData.m_shared.m_icons.Length > 0)
                    sprite = drop.m_itemData.GetIcon();
            }
            catch { }

            _cache[key] = sprite;
            return sprite;
        }

        public static void ClearCache() => _cache.Clear();

        // Vanilla / loaded UI sprites by exact name (e.g. "trash_icon") — Jotunn-free, from the full loaded-sprite
        // set. Cached per name (even misses) so the FindObjectsOfTypeAll scan runs at most once per name.
        private static Dictionary<string, Sprite> _uiCache;

        public static Sprite Ui(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_uiCache == null) _uiCache = new Dictionary<string, Sprite>();
            if (_uiCache.TryGetValue(name, out var cached)) return cached;

            Sprite found = null;
            try
            {
                foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (s != null && s.name == name) { found = s; break; }
            }
            catch { }
            _uiCache[name] = found;
            return found;
        }
    }
}
