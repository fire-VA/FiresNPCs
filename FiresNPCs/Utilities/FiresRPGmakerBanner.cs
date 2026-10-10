using System;
using FiresCore.Logging;
using UnityEngine;

namespace FiresRPGmaker
{
    // FiresRPGmaker's console banners, rendered through the shared Banner engine.
    //
    //   PrintBig()  → the FIRES RPG MAKER block-letter banner (owner-authored).
    //                 Fires at the very top of Setup as the load announcement.
    //   Print()     → compact speech bubble over a status frame
    //                 ("QUESTS AWAKE"). Fires once at first WorldStart.
    internal static class FiresRPGmakerBanner
    {
        private static bool s_compactPrinted;

        public static void PrintBig()
        {
            try
            {
                EmitHeader("Fires RPG Maker Loading...");
                Banner.Print(ToSegments(s_bigLines));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Big banner failed: {ex.Message}");
            }
        }

        public static void Print()
        {
            if (s_compactPrinted) return;
            s_compactPrinted = true;
            try
            {
                EmitHeader("Fires RPG Maker Loaded.");
                Banner.Print(s_compactLines);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] Compact banner failed: {ex.Message}");
            }
        }

        // Routes the one-line header through the plugin's BepInEx log source so it
        // prints once, colored by FiresLogColorPatch, without a raw white stdout
        // duplicate. Falls back to Debug.Log before Setup wires the source.
        private static void EmitHeader(string text)
        {
            if (FiresRPGmaker.Log != null) FiresRPGmaker.Log.LogInfo(text);
            else Debug.Log($"[FiresRPGmaker] {text}");
        }

        private static Banner.Segment[][] ToSegments((string text, ConsoleColor color)[] lines)
        {
            var result = new Banner.Segment[lines.Length][];
            for (int i = 0; i < lines.Length; i++)
                result[i] = new[] { new Banner.Segment(lines[i].text, lines[i].color) };
            return result;
        }

        // Compact banner — speech bubble over a status frame. 31-char frame body
        // (3 lead + ╔ + 26 ═ + ╗); 📖 is a surrogate pair, but the console
        // ADVANCES ONE column for it (no VS16), however wide the font paints the glyph, so this row pads it as one cell.
        private static readonly Banner.Segment[][] s_compactLines =
        {
            new[] { new Banner.Segment("            ╭───────╮",        ConsoleColor.DarkCyan) },
            new[]
            {
                new Banner.Segment("            │",   ConsoleColor.DarkCyan),
                new Banner.Segment(" ▪ ▪ ▪ ",         ConsoleColor.White),
                new Banner.Segment("│",               ConsoleColor.DarkCyan),
            },
            new[] { new Banner.Segment("            ╰──╮ ╭──╯",        ConsoleColor.DarkCyan) },
            new[] { new Banner.Segment("               ╲╱",            ConsoleColor.Cyan) },
            new[] { new Banner.Segment("              (••)",           ConsoleColor.Magenta) },
            new[] { new Banner.Segment("   ╔══════════════════════════╗", ConsoleColor.DarkGray) },
            new[]
            {
                new Banner.Segment("   ║   ",              ConsoleColor.DarkGray),
                new Banner.Segment("[",                    ConsoleColor.Gray),
                new Banner.Segment("██████████████████",   ConsoleColor.Green),
                new Banner.Segment("]",                    ConsoleColor.Gray),
                new Banner.Segment("   ║",                 ConsoleColor.DarkGray),
            },
            new[]
            {
                new Banner.Segment("   ║     ",            ConsoleColor.DarkGray),
                new Banner.Segment("📖 QUESTS AWAKE",      ConsoleColor.Yellow),
                new Banner.Segment("       ║",              ConsoleColor.DarkGray),
            },
            new[] { new Banner.Segment("   ╚══════════════════════════╝", ConsoleColor.DarkGray) },
            new[] { new Banner.Segment("    FIRES RPG MAKER",          ConsoleColor.Cyan) },
            new Banner.Segment[0],
            new[] { new Banner.Segment("   Fires RPG Maker Loaded",    ConsoleColor.Magenta) },
        };

        // FIRES RPG MAKER block banner (owner-authored 2026-07-07). Color zones:
        // starfield Cyan/DarkCyan, FIRES Red, RPG Yellow, MAKER Magenta, tagline White/Gray.
        private static readonly (string text, ConsoleColor color)[] s_bigLines =
        {
            ("", ConsoleColor.Gray),
            (".        *           .        *", ConsoleColor.DarkCyan),
            ("    *        .         ✧          .", ConsoleColor.Cyan),
            ("          .        *        .        *", ConsoleColor.DarkCyan),
            ("", ConsoleColor.Gray),
            ("        ✦      ·      ✧      ·      ✦", ConsoleColor.Cyan),
            ("      ·      ✧      ·      ✦      ·", ConsoleColor.DarkCyan),
            ("        ✧      ·      ✦      ·      ✧", ConsoleColor.Cyan),
            ("", ConsoleColor.Gray),
            (" ███████╗██╗██████╗ ███████╗ ███████╗", ConsoleColor.Red),
            (" ██╔════╝██║██╔══██╗██╔════╝ ██╔════╝", ConsoleColor.Red),
            (" █████╗  ██║██████╔╝█████╗   ███████╗", ConsoleColor.Red),
            (" ██╔══╝  ██║██╔══██╗██╔══╝   ╚════██║", ConsoleColor.DarkRed),
            (" ██║     ██║██║  ██║███████╗ ███████║", ConsoleColor.DarkRed),
            (" ╚═╝     ╚═╝╚═╝  ╚═╝╚══════╝ ╚══════╝", ConsoleColor.DarkRed),
            ("", ConsoleColor.Gray),
            ("        ██████╗ ██████╗  ██████╗", ConsoleColor.Yellow),
            ("        ██╔══██╗██╔══██╗██╔════╝", ConsoleColor.Yellow),
            ("        ██████╔╝██████╔╝██║  ███╗", ConsoleColor.Yellow),
            ("        ██╔══██╗██╔═══╝ ██║   ██║", ConsoleColor.DarkYellow),
            ("        ██║  ██║██║     ╚██████╔╝", ConsoleColor.DarkYellow),
            ("        ╚═╝  ╚═╝╚═╝      ╚═════╝", ConsoleColor.DarkYellow),
            ("", ConsoleColor.Gray),
            ("              ███╗   ███╗ █████╗ ██╗  ██╗███████╗██████╗", ConsoleColor.Magenta),
            ("              ████╗ ████║██╔══██╗██║ ██╔╝██╔════╝██╔══██╗", ConsoleColor.Magenta),
            ("              ██╔████╔██║███████║█████╔╝ █████╗  ██████╔╝", ConsoleColor.Magenta),
            ("              ██║╚██╔╝██║██╔══██║██╔═██╗ ██╔══╝  ██╔══██╗", ConsoleColor.DarkMagenta),
            ("              ██║ ╚═╝ ██║██║  ██║██║  ██╗███████╗██║  ██║", ConsoleColor.DarkMagenta),
            ("              ╚═╝     ╚═╝╚═╝  ╚═╝╚═╝  ╚═╝╚══════╝╚═╝  ╚═╝", ConsoleColor.DarkMagenta),
            ("", ConsoleColor.Gray),
            ("════════════════════════════════════", ConsoleColor.DarkGray),
            ("        QUESTS • SYSTEMS • THE GOOD SHIT", ConsoleColor.Yellow),
            ("════════════════════════════════════", ConsoleColor.DarkGray),
            ("", ConsoleColor.Gray),
            ("   WELCOME TO THE FUTURE, YOU STUPID VIKINGS.", ConsoleColor.White),
            ("   FIRE’S RPG MAKER HAS ARRIVED TO KICK YOUR ASS", ConsoleColor.White),
            ("   BACK INTO VALHEIM WITH REAL QUESTS,", ConsoleColor.Gray),
            ("   REAL SYSTEMS, AND EXTREMELY QUESTIONABLE POWER.", ConsoleColor.Gray),
            ("", ConsoleColor.Gray),
            ("   AND BEST OF ALL YOU CAN DROP IN ALL YOUR MARKETPLACE CONFIGS", ConsoleColor.Green),
            ("", ConsoleColor.Gray),
            ("        .        *           .        *", ConsoleColor.DarkCyan),
            ("              ✧        .         *", ConsoleColor.Cyan),
            ("", ConsoleColor.Gray),
        };

    }
}
