using HarmonyLib;
using UnityEngine;
using FiresRPGmaker.Modules.Guilds;
using FiresRPGmaker.Modules.Guilds.Core;

namespace FiresRPGmaker.Modules.Guilds.Map
{
    /// <summary>
    /// Guild map features:
    ///   1. Guild members' map pins are tinted with the guild's colour.
    ///   2. "/gping" command (or Shift+map ping) sends a guild-only map ping.
    ///   3. Hovering a player icon on the map shows their guild tag.
    ///
    /// Implementation via Harmony patches on Minimap.
    /// </summary>
    public static class GuildMapFeatures
    {
        // Tints the player icon colour for guild members.
        [HarmonyPatch(typeof(Minimap), "UpdatePlayerPins")]
        private static class Minimap_UpdatePlayerPins_Patch
        {
            private static void Postfix(Minimap __instance)
            {
                if (__instance == null) return;

                var local = Player.m_localPlayer;
                if (local == null) return;

                var ownGuild = GuildAPI.GetPlayerGuild(local.GetPlayerID());
                if (ownGuild == null) return;

                // Walk all player pins and colour guildmates.
                try
                {
                    foreach (var pin in __instance.m_pins)
                    {
                        if (pin == null || pin.m_type != Minimap.PinType.Player) continue;

                        // Resolve the player id from the pin name (vanilla stores player name).
                        long pinPlayerId = ResolvePlayerIdByName(pin.m_name);
                        if (pinPlayerId == 0 || !ownGuild.IsMember(pinPlayerId)) continue;

                        if (pin.m_iconElement != null)
                            pin.m_iconElement.color = ownGuild.GuildColor;
                    }
                }
                catch { }
            }
        }

        // Intercept Shift+middle-click to send a guild-only map ping via chat.
        [HarmonyPatch(typeof(Minimap), "OnMapMiddleClick")]
        private static class Minimap_OnMapMiddleClick_Patch
        {
            private static bool Prefix(Minimap __instance)
            {
                if (!Input.GetKey(KeyCode.LeftShift)) return true;

                var local = Player.m_localPlayer;
                if (local == null) return true;

                var guild = GuildAPI.GetOwnGuild();
                if (guild == null) return true;

                // Replicate the private Minimap.ScreenToWorldPoint + MapPointToWorld using public fields.
                var img = __instance.m_mapImageLarge;
                var rt  = img.transform as RectTransform;
                if (rt == null) return true;

                Vector2 localPt;
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        rt, ZInput.mousePosition, null, out localPt))
                    return false;

                Vector2 norm = Rect.PointToNormalized(rt.rect, localPt);
                Rect uv     = img.uvRect;
                float mx    = uv.xMin + norm.x * uv.width;
                float my    = uv.yMin + norm.y * uv.height;
                int   half  = __instance.m_textureSize / 2;
                float wx    = (mx * __instance.m_textureSize - half) * __instance.m_pixelSize;
                float wz    = (my * __instance.m_textureSize - half) * __instance.m_pixelSize;
                var worldPoint = new Vector3(wx, 0f, wz);

                string colorHex = ColorUtility.ToHtmlStringRGB(guild.GuildColor);
                Chat.instance?.AddString(
                    $"<color=#{colorHex}>[{guild.Name} Ping]</color> " +
                    $"{local.GetPlayerName()} at ({worldPoint.x:F0}, {worldPoint.z:F0})");

                GuildAPI.SendChatMessage($"[MAP PING] {worldPoint.x:F0},{worldPoint.z:F0}");
                return false;
            }
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static long ResolvePlayerIdByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            var list = ZNet.instance?.GetPlayerList();
            if (list == null) return 0;
            foreach (var entry in list)
                if (entry.m_name == name) return entry.m_characterID.UserID;
            return 0;
        }
    }
}
