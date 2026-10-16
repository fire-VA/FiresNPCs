using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FiresRPGmaker.Modules.Buffer
{
    /// <summary>Console commands to validate the buffer engine before the UI exists:
    ///   firesnpcs_buffer_dump                              — reload + list parsed profiles and buffs
    ///   firesnpcs_buffer_list_se [filter]                  — list every vanilla/modded status effect + badge
    ///   firesnpcs_buff_apply &lt;UniqueName|@SEName&gt; [secs] [strength%]  — apply to yourself (local test)
    /// The @ form applies ANY status effect in ObjectDB directly, demonstrating the every-SE capability.</summary>
    [HarmonyPatch(typeof(Terminal), "Awake")]
    internal static class BufferConsoleCommands
    {
        private static bool _registered;

        [HarmonyPostfix]
        private static void Register()
        {
            if (_registered) return;
            _registered = true;

            new Terminal.ConsoleCommand("firesnpcs_buffer_dump",
                "Reload buffer configs and list parsed profiles + buffs.", DumpCmd);

            new Terminal.ConsoleCommand("firesnpcs_buffer_list_se",
                "List every status effect (vanilla + modded) with its strength badge. Optional name filter.", ListSeCmd);

            new Terminal.ConsoleCommand("firesnpcs_buff_apply",
                "Apply a buff to yourself: firesnpcs_buff_apply <UniqueName|@StatusEffectName> [seconds] [strength%]", ApplyCmd);

            new Terminal.ConsoleCommand("firesnpcs_buff_buy",
                "Server-validated buy: firesnpcs_buff_buy <UniqueName> [profileKey] [seconds] [strength%] — routes through the grant RPC (profile + caps enforced server-side, cost consumed).", BuyCmd);

            new Terminal.ConsoleCommand("firesnpcs_buff_shop",
                "Open the buff shop UI for a profile: firesnpcs_buff_shop [profileKey]", ShopCmd);

            new Terminal.ConsoleCommand("firesnpcs_buff_admin",
                "Open the admin buff-profile builder (browse every status effect, assemble a profile).", AdminCmd);
        }

        /// <summary>Admin gate for the debug/config commands. The shop + server-validated buy stay
        /// player-usable (they enforce cost/caps server-side); everything else is admin-only.</summary>
        private static bool RequireAdmin(Terminal.ConsoleEventArgs args)
        {
            bool isAdmin = FiresRPGmaker.Instance?.configSync?.IsAdmin ?? false;
            if (!isAdmin) args.Context?.AddString("[buffer] admin only");
            return isAdmin;
        }

        private static void ShopCmd(Terminal.ConsoleEventArgs args)
        {
            string profile = args.Length > 1 ? args[1] : "default";
            global::FiresRPGmaker.UI.Screens.PlayerPanels.BufferPlayerPanel.Show(profile, "Buffs");
            args.Context?.AddString($"[buffer] opening shop for profile '{profile}'");
        }

        private static void AdminCmd(Terminal.ConsoleEventArgs args)
        {
            if (!RequireAdmin(args)) return;
            global::FiresRPGmaker.UI.Screens.PlayerPanels.BufferAdminPanel.Show();
            args.Context?.AddString("[buffer] opening profile builder");
        }

        private static void BuyCmd(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 2)
            {
                args.Context?.AddString("usage: firesnpcs_buff_buy <UniqueName> [profileKey] [seconds] [strength%]");
                return;
            }
            string uniqueName = args[1];
            string profileKey = args.Length > 2 ? args[2] : "";
            // Without a profile it is the admin's un-gated apply (the server checks too, 0.0.532).
            if (string.IsNullOrEmpty(profileKey) && !RequireAdmin(args)) return;
            int secs = args.Length > 3 && int.TryParse(args[3], out var sc) ? sc : 0;
            float strength = args.Length > 4 && float.TryParse(args[4], out var st) ? Mathf.Clamp01(st / 100f) : 1f;
            BufferNetwork.RequestApply(profileKey, uniqueName, secs, strength);
            args.Context?.AddString($"[buffer] requested '{uniqueName}' (profile='{profileKey}') from server");
        }

        private static void DumpCmd(Terminal.ConsoleEventArgs args)
        {
            if (!RequireAdmin(args)) return;
            BufferManager.Reload();
            var p = BufferManager.Profiles;
            var b = BufferManager.Buffs;
            args.Context?.AddString($"[buffer] {b.Count} buff(s), {p.Count} profile(s)");
            foreach (var kv in p)
                args.Context?.AddString($"  profile [{kv.Key}] = {kv.Value}");
            foreach (var buff in b)
            {
                string backend = buff.Backend == BuffBackend.TargetSE ? $"SE:{buff.TargetSE}" : "synthetic";
                args.Context?.AddString($"  buff [{buff.UniqueName}] '{buff.Name}' {buff.Duration}s {backend} " +
                                        $"cost={buff.NeededPrefab}x{buff.NeededPrefabCount} group='{buff.BuffGroup}' cap={buff.StrengthCap:0.##}");
            }
        }

        private static void ListSeCmd(Terminal.ConsoleEventArgs args)
        {
            if (!RequireAdmin(args)) return;
            string filter = args.Length > 1 ? args[1].ToLowerInvariant() : null;
            var all = StatusEffectCatalog.All();
            int shown = 0;
            foreach (var se in all.OrderBy(s => s.name))
            {
                if (filter != null && !se.name.ToLowerInvariant().Contains(filter) &&
                    !(se.m_name ?? "").ToLowerInvariant().Contains(filter)) continue;
                var badge = StatusEffectCatalog.Badge(se);
                args.Context?.AddString($"  {se.name}  ({se.m_name})  [{badge}]  ttl={se.m_ttl:0}");
                shown++;
            }
            args.Context?.AddString($"[buffer] {shown}/{all.Count} status effect(s)" + (filter != null ? $" matching '{filter}'" : ""));
        }

        private static void ApplyCmd(Terminal.ConsoleEventArgs args)
        {
            if (!RequireAdmin(args)) return;   // free self-buffing bypasses the shop's server-validated costs
            if (args.Length < 2)
            {
                args.Context?.AddString("usage: firesnpcs_buff_apply <UniqueName|@StatusEffectName> [seconds] [strength%]");
                return;
            }

            string name = args[1];
            int secs = args.Length > 2 && int.TryParse(args[2], out var sc) ? sc : 0;
            float strength = args.Length > 3 && float.TryParse(args[3], out var st) ? Mathf.Clamp01(st / 100f) : 1f;

            FiresBuffData b;
            if (name.StartsWith("@"))
            {
                string se = name.Substring(1);
                b = new FiresBuffData
                {
                    UniqueName = se, Name = se,
                    Backend = BuffBackend.TargetSE, TargetSE = se,
                    Duration = secs > 0 ? secs : 60,
                };
            }
            else
            {
                b = BufferManager.Resolve(name);
                if (b == null) { args.Context?.AddString($"[buffer] no buff '{name}' (try firesnpcs_buffer_dump)"); return; }
            }

            if (BufferApplyEngine.ApplyToLocalPlayer(b, secs, strength, out var msg))
                args.Context?.AddString("[buffer] " + msg);
            else
                args.Context?.AddString("[buffer] failed: " + msg);
        }
    }
}
