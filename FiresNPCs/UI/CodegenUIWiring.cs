using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI.Screens;

namespace FiresRPGmaker.UI
{
    /// <summary>
    /// Wiring layer for the codegen UI. Resolves sprites, fonts, and field references
    /// on the hierarchy produced by GeneratedUI_completecompanionsui.Instantiate().
    /// </summary>
    public static class CodegenUIWiring
    {

        #region Sprite Resolver

        // Cached Valheim sprites
        private static Sprite _itemBackgroundSprite;
        private static Sprite _selectionFrameSprite;
        private static Sprite _noTeleportSprite;
        private static Sprite _npcUiBkgSprite;
        private static bool _spritesCached;

        /// <summary>
        /// Sprite resolver function passed to codegen ResolveAssets.
        /// Maps "cached:spriteName" strings to actual Sprite objects.
        /// </summary>
        private static Sprite ResolveSpriteByName(string spriteName)
        {
            CacheValheimSprites();

            if (string.IsNullOrEmpty(spriteName)) return null;

            // Strip "cached:" prefix if present
            string name = spriteName.StartsWith("cached:") ? spriteName.Substring(7) : spriteName;

            switch (name.ToLowerInvariant())
            {
                case "npcuibkg": return _npcUiBkgSprite;
                case "item_background": return _itemBackgroundSprite;
                case "selection_frame": return _selectionFrameSprite;
                case "noteleport": return _noTeleportSprite;
                default:
                    // Try VAMiscAssetManager as fallback
                    return VAMiscAssetManager.GetSprite(name);
            }
        }

        /// <summary>
        /// Public accessor for the sprite resolver, usable by other codegen consumers
        /// (e.g. DialogueUI's marketplace-style templates).
        /// </summary>
        public static Sprite ResolveSpriteByNamePublic(string spriteName)
        {
            return ResolveSpriteByName(spriteName);
        }

        private static void CacheValheimSprites()
        {
            if (_spritesCached) return;

            _npcUiBkgSprite = VAMiscAssetManager.GetSprite("npcuibkg");

            if (InventoryGui.instance != null)
            {
                var playerInventory = InventoryGui.instance.m_playerGrid;
                if (playerInventory != null && playerInventory.m_elementPrefab != null)
                {
                    var elemImages = playerInventory.m_elementPrefab.GetComponentsInChildren<Image>(true);
                    foreach (var img in elemImages)
                    {
                        if (img == null || img.sprite == null) continue;
                        string n = img.sprite.name;

                        if (n.Contains("item_background") || n.Contains("item_bg"))
                            _itemBackgroundSprite = img.sprite;
                        else if (n.Contains("selection") || n.Contains("selected"))
                            _selectionFrameSprite = img.sprite;
                        else if (n.Contains("noteleport"))
                            _noTeleportSprite = img.sprite;
                    }
                }
            }

            // Skip expensive Resources.FindObjectsOfTypeAll<Sprite>() fallback.
            // Missing sprites are cosmetic-only - the UI works without them.
            // They'll be found naturally if InventoryGui is available on subsequent opens.

            _spritesCached = true;
            if (FiresLogger.VerboseEnabled)
                FiresLogger.LogVerbose($"[CodegenUIWiring] Sprite cache: BG={_npcUiBkgSprite != null}, " +
                      $"ItemBg={_itemBackgroundSprite != null}, Selection={_selectionFrameSprite != null}, " +
                      $"NoTeleport={_noTeleportSprite != null}");
        }

        /// <summary>Clears cached sprites (call on scene change / logout).</summary>
        public static void ClearSpriteCache()
        {
            _spritesCached = false;
            _itemBackgroundSprite = null;
            _selectionFrameSprite = null;
            _noTeleportSprite = null;
            _npcUiBkgSprite = null;
        }

        #endregion

        #region Helpers

        /// <summary>Finds a child transform by path, searching recursively if direct path fails.</summary>
        public static Transform FindChild(Transform parent, string path)
        {
            if (parent == null) return null;

            var result = parent.Find(path);
            if (result != null) return result;

            string lastName = path.Contains("/") ? path.Substring(path.LastIndexOf('/') + 1) : path;
            return FindDeep(parent, lastName);
        }

        /// <summary>Recursive depth-first search by name.</summary>
        public static Transform FindDeep(Transform parent, string name)
        {
            if (parent == null) return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child;

                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public static TMP_Text FindTMP(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponent<TMP_Text>() ?? t?.GetComponentInChildren<TMP_Text>(true);
        }

        public static Button FindButton(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponent<Button>() ?? t?.GetComponentInChildren<Button>(true);
        }

        public static TMP_InputField FindInputField(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            if (t == null) return null;

            // Prefer active InputFields - the codegen creates both an inactive
            // legacy input (e.g. "NameOverrideInput") and the real transparent
            // one ("Transparent_NameOverrideInput"). GetComponentInChildren(true)
            // finds the inactive one first, which breaks all input.
            var active = t.GetComponentInChildren<TMP_InputField>(false);
            if (active != null) return active;

            // Fallback to include inactive (for fields not yet activated)
            return t.GetComponentInChildren<TMP_InputField>(true);
        }

        public static TMP_Dropdown FindDropdown(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<TMP_Dropdown>(true);
        }

        public static Toggle FindToggle(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<Toggle>(true);
        }

        public static ScrollRect FindScrollRect(Transform parent, string path)
        {
            var t = FindChild(parent, path);
            return t?.GetComponentInChildren<ScrollRect>(true);
        }

        #endregion

    }
}
