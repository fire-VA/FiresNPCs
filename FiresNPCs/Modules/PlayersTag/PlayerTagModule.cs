using System.Collections.Generic;
using HarmonyLib;

namespace FiresRPGmaker.Modules.PlayersTag
{
    /// <summary>Registers the player-title console commands. Membership + nameplate live in
    /// PlayerTagManager / PlayerTagNameplatePatches.</summary>
    public static class PlayerTagModule
    {
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        private static class RegisterCommands
        {
            private static bool _registered;

            [HarmonyPostfix]
            private static void Register()
            {
                if (_registered) return;
                _registered = true;

                new Terminal.ConsoleCommand("mytag", "<title|none> - set your own name title", args =>
                {
                    string tag = args.Length > 1 ? string.Join(" ", System.Linq.Enumerable.Skip(args.Args, 1)) : "none";
                    PlayerTagManager.SetLocal(tag);
                });

                new Terminal.ConsoleCommand("settag", "<player|me> <title|none> - admin: set a player's title", args =>
                {
                    if (args.Length < 2) { args.Context.AddString("usage: settag <player|me> <title|none>"); return; }
                    string who = args[1];
                    string tag = args.Length > 2 ? string.Join(" ", System.Linq.Enumerable.Skip(args.Args, 2)) : "none";
                    PlayerTagManager.AdminSet(who, tag);
                    args.Context.AddString($"[tag] requested '{tag}' for {who}");
                }, optionsFetcher: OnlinePlayerNames);
            }
        }

        private static List<string> OnlinePlayerNames()
        {
            var names = new List<string> { "me" };
            try { if (ZNet.instance != null) foreach (var p in ZNet.instance.GetPeers()) if (!string.IsNullOrEmpty(p.m_playerName)) names.Add(p.m_playerName); }
            catch { }
            return names;
        }
    }
}
