using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using FiresRPGmaker.Modules.Quest;
using FiresRPGmaker.Modules.Vault;
using FiresCore.Npc;
using FiresCore.Lifecycle;
using FiresRPGmaker.Managers;
using FiresRPGmaker.UI;
using BepInEx;
using System.IO;
using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;
using System.Collections.Generic;

namespace FiresRPGmaker
{
    public static class DebugTools
    {
    // Track the last time we pushed configs to avoid re-sending unchanged files
        private static DateTime _lastPushTime = DateTime.MinValue;
        private static readonly string _lastPushTimePath = Path.Combine(Paths.ConfigPath, "FiresRPGmaker", ".lastpush");
        private static readonly string _lastPushManifestPath = Path.Combine(Paths.ConfigPath, "FiresRPGmaker", ".lastpush.manifest");
        private static Dictionary<string, string> _lastPushHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

   /// <summary>
        /// Registers debug commands into the vanilla F5 console.
 /// Call this once after VaultOfKnowledge and QuestManager are initialized.
        /// </summary>
        public static void RegisterConsoleCommands()
        {
// Load last push time from disk if available
            try
            {
            if (File.Exists(_lastPushTimePath))
          {
     var text = File.ReadAllText(_lastPushTimePath).Trim();
   if (long.TryParse(text, out long ticks))
    _lastPushTime = new DateTime(ticks, DateTimeKind.Utc);
 }
            if (File.Exists(_lastPushManifestPath))
            {
                _lastPushHashes.Clear();
                foreach (var line in File.ReadAllLines(_lastPushManifestPath))
                {
                    int sep = line.IndexOf('|');
                    if (sep > 0)
                        _lastPushHashes[line.Substring(0, sep)] = line.Substring(sep + 1);
                }
            }
            else
            {
                // No manifest yet - bootstrap from existing config files so the first push
                // doesn't re-send everything the server already synced to us on login.
                try
                {
                    BootstrapManifestFromExistingFiles();
                }
                catch { }
            }
        }
   catch { }

          var terminal = UnityEngine.Object.FindFirstObjectByType<Terminal>();
     if (terminal == null)
            {
   FiresLogger.LogWarning("Terminal instance not ready - skipping console commands");
           return;
      }

   // ============================================================
            // NPC UI DEBUG COMMANDS
     // ============================================================

     // Command: testnpcui - Opens the NPC panel with a mock NPC for testing
      new Terminal.ConsoleCommand("testnpcui", "Opens the NPC UI panel for testing (no NPC required)", (Terminal.ConsoleEventArgs args) =>
         {
                try
      {
    if (Player.m_localPlayer == null)
          {
      args.Context.AddString("[FiresRPGmaker] No local player - cannot test UI");
         return;
   }

   var mockNpc = CreateMockNpcController();
            if (mockNpc == null)
          {
     args.Context.AddString("[FiresRPGmaker] Failed to create mock NPC controller");
              return;
        }

        args.Context.AddString("[FiresRPGmaker] NPC UI panel retired — quest/admin UI now lives in the NPC book (NpcBookController)");
      }
       catch (Exception ex)
                {
          Debug.LogError($"[FiresRPGmaker] testnpcui exception: {ex}");
           args.Context.AddString($"[FiresRPGmaker] testnpcui error: {ex.Message}");
    }
       });

            // 0.0.543 (the lead, for [visual]'s rpg_economy_test): the supported way to give a placed NPC a role from the
            // console, instead of writing its ZDO keys. Same path as the NPC book's Apply: set the controller's fields, then
            // SavedNpcManager.SaveFromNpc (server record + every client refreshed). Targets the nearest NPC within 10 m.
            new Terminal.ConsoleCommand("firesnpcs_setrole",
                "firesnpcs_setrole <role> [profile=<trader profile>] [name=<display name>] - give the nearest NPC (10 m) a role: " + string.Join("|", Enum.GetNames(typeof(NpcType))) + ". Admin.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    try
                    {
                        if (args.Length < 2 || !Enum.TryParse(args[1], true, out NpcType role))
                        { args.Context.AddString("[FiresRPGmaker] usage: firesnpcs_setrole <" + string.Join("|", Enum.GetNames(typeof(NpcType))) + "> [profile=<name>] [name=<display name>]"); return; }
                        var player = Player.m_localPlayer;
                        if (player == null) { args.Context.AddString("[FiresRPGmaker] firesnpcs_setrole: no local player"); return; }
                        NpcController best = null; float bestD = 10f;
                        foreach (var c in UnityEngine.Object.FindObjectsByType<NpcController>(FindObjectsSortMode.None))
                        {
                            if (c == null) continue;
                            float d = Vector3.Distance(c.transform.position, player.transform.position);
                            if (d < bestD) { bestD = d; best = c; }
                        }
                        if (best == null) { args.Context.AddString("[FiresRPGmaker] firesnpcs_setrole: no NPC within 10 m"); return; }
                        string rest = args.Length > 2 ? string.Join(" ", args.Args.Skip(2)) : "";
                        string profile = null, name = null;
                        int pi = rest.IndexOf("profile=", StringComparison.OrdinalIgnoreCase), ni = rest.IndexOf("name=", StringComparison.OrdinalIgnoreCase);
                        if (pi >= 0) { int end = ni > pi ? ni : rest.Length; profile = rest.Substring(pi + 8, end - pi - 8).Trim(); }
                        if (ni >= 0) { int end = pi > ni ? pi : rest.Length; name = rest.Substring(ni + 5, end - ni - 5).Trim(); }
                        best.npcType = role;
                        // 0.0.548: profile= goes to the ROLE's own profile field (it only ever set the trader's, so a Banker kept '').
                        if (!string.IsNullOrEmpty(profile))
                            switch (role)
                            {
                                case NpcType.Trader: best.traderProfile = profile; break;
                                case NpcType.Banker: best.bankerProfile = profile; break;
                                case NpcType.Gambler: best.gamblerProfile = profile; break;
                                case NpcType.Marketplace: best.marketplaceProfile = profile; break;
                                case NpcType.Achievement: best.achievementProfile = profile; break;
                                case NpcType.Leaderboard: best.leaderboardProfile = profile; break;
                                case NpcType.Buffer: best.bufferProfile = profile; break;
                                case NpcType.Mail: best.mailProfile = profile; break;
                                case NpcType.Transmog: best.transmogProfile = profile; break;
                                case NpcType.QuestNpc: best.questProfile = profile; break;
                                case NpcType.InfoNpc: best.infoProfile = profile; break;
                                case NpcType.DialogueNpc: best.dialogueProfile = profile; break;
                                default: best.traderProfile = profile; break;
                            }
                        if (!string.IsNullOrEmpty(name)) best.displayNameOverride = name;
                        SavedNpcManager.SaveFromNpc(best);
                        string line = $"[FiresRPGmaker] firesnpcs_setrole: NPC at {best.transform.position:F1} ({bestD:F1} m) -> role {role}"
                            + (profile != null ? $", {role} profile '{profile}'" : "") + (name != null ? $", name '{name}'" : "") + " (saved like the NPC book's Apply)";
                        args.Context.AddString(line);
                        Debug.Log(line);
                    }
                    catch (Exception ex) { args.Context.AddString($"[FiresRPGmaker] firesnpcs_setrole error: {ex.Message}"); }
                }, isCheat: true);

            // 0.0.547 (Fire: NPC-driven ships, step 1): bind the nearest ship within 20 m to a saved patrol route. The ship is
            // then sailed by vanilla physics with its controls worked by NpcShipDriver (owner side). "none" unbinds.
            new Terminal.ConsoleCommand("firesnpcs_shipdrive",
                "firesnpcs_shipdrive <patrol route|none> - the nearest ship (20 m) sails that patrol route point to point (real sailing). Admin.",
                (Terminal.ConsoleEventArgs args) =>
                {
                    try
                    {
                        var player = Player.m_localPlayer;
                        if (player == null || args.Length < 2) { args.Context.AddString("[FiresRPGmaker] usage: firesnpcs_shipdrive <route|none>"); return; }
                        string route = string.Equals(args[1], "none", StringComparison.OrdinalIgnoreCase) ? "" : string.Join(" ", args.Args.Skip(1));
                        if (route.Length > 0 && FiresCore.Npc.Patrol.PatrolRouteManager.GetRoute(route) == null) { args.Context.AddString($"[FiresRPGmaker] no patrol route '{route}'"); return; }
                        Ship best = null; float bd = 20f;
                        foreach (var s in UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None))
                        { float d = Vector3.Distance(s.transform.position, player.transform.position); if (d < bd) { bd = d; best = s; } }
                        if (best == null) { args.Context.AddString("[FiresRPGmaker] no ship within 20 m"); return; }
                        global::FiresRPGmaker.Modules.Vehicles.NpcShipDriver.Bind(best, route);
                        string line = $"[FiresRPGmaker] firesnpcs_shipdrive: {best.name} at {best.transform.position:F0} -> route '{(route.Length > 0 ? route : "none")}'";
                        args.Context.AddString(line); Debug.Log(line);
                    }
                    catch (Exception ex) { args.Context.AddString($"[FiresRPGmaker] firesnpcs_shipdrive error: {ex.Message}"); }
                }, isCheat: true);

         // Command: closenpcui - Closes the NPC panel if open
      new Terminal.ConsoleCommand("closenpcui", "Closes the NPC UI panel if open", (Terminal.ConsoleEventArgs args) =>
            {
try
     {
            if (GUIManager.IsCustomPanelOpen())
     {
     GUIManager.CloseAllPanels();
       args.Context.AddString("[FiresRPGmaker] NPC UI panel closed");
         }
     else
         {
       args.Context.AddString("[FiresRPGmaker] No NPC UI panel is open");
       }
 }
       catch (Exception ex)
       {
    args.Context.AddString($"[FiresRPGmaker] closenpcui error: {ex.Message}");
     }
            });

       // Command: reloadui - Reloads UI config (dialogues, quest profiles)
   new Terminal.ConsoleCommand("reloadui", "Reloads UI configuration (dialogues, quest profiles)", (Terminal.ConsoleEventArgs args) =>
 {
        try
         {
        UIInitializer.Reload();
          args.Context.AddString("[FiresRPGmaker] UI configuration reloaded");
         }
     catch (Exception ex)
                {
         args.Context.AddString($"[FiresRPGmaker] reloadui error: {ex.Message}");
                }
    });

        // Command: listdialogues - Lists all loaded dialogue profiles
   new Terminal.ConsoleCommand("listdialogues", "Lists all loaded dialogue profiles", (Terminal.ConsoleEventArgs args) =>
    {
        try
      {
         var profiles = DialogueManager.GetAllProfileNames();
  if (profiles == null || profiles.Count == 0)
     {
        args.Context.AddString("[FiresRPGmaker] No dialogue profiles loaded");
        return;
    }

   args.Context.AddString($"[FiresRPGmaker] Dialogue profiles ({profiles.Count}):");
        foreach (var name in profiles)
  {
    var profile = DialogueManager.GetProfile(name);
           int lineCount = profile?.Lines?.Count ?? 0;
        args.Context.AddString($"  - {name} ({lineCount} lines)");
       }
    }
         catch (Exception ex)
         {
  args.Context.AddString($"[FiresRPGmaker] listdialogues error: {ex.Message}");
         }
        });

      // Command: listquestprofiles - Lists all loaded quest profiles
            new Terminal.ConsoleCommand("listquestprofiles", "Lists all loaded quest profiles for NPCs", (Terminal.ConsoleEventArgs args) =>
        {
       try
            {
                    var profiles = QuestProfileManager.GetAllProfileNames();
        if (profiles == null || profiles.Count == 0)
      {
              args.Context.AddString("[FiresRPGmaker] No quest profiles loaded");
       return;
      }

   args.Context.AddString($"[FiresRPGmaker] Quest profiles ({profiles.Count}):");
             foreach (var name in profiles)
      {
                  var profile = QuestProfileManager.GetProfile(name);
    int questCount = profile?.QuestIds?.Count ?? 0;
             args.Context.AddString($"  - {name} ({questCount} quests)");
   }
    }
         catch (Exception ex)
{
      args.Context.AddString($"[FiresRPGmaker] listquestprofiles error: {ex.Message}");
    }
 });

            // Command: listassets - Debug command to list all assets in the bundle
      new Terminal.ConsoleCommand("listassets", "Lists all assets in the mod's asset bundle (for debugging)", (Terminal.ConsoleEventArgs args) =>
    {
     try
     {
                 VAMiscAssetManager.DebugListAllAssets();
   args.Context.AddString("[FiresRPGmaker] Asset list printed to log (check BepInEx/LogOutput.log)");
        }
            catch (Exception ex)
   {
          args.Context.AddString($"[FiresRPGmaker] listassets error: {ex.Message}");
 }
            });

      // Command: uistatus - Shows current UI system status
   new Terminal.ConsoleCommand("uistatus", "Shows current UI system status", (Terminal.ConsoleEventArgs args) =>
          {
     try
         {
  args.Context.AddString("=== UI System Status ===");
       args.Context.AddString("Panel Instance: retired (NPC book UI)");
 args.Context.AddString($"Panel Visible: {GUIManager.IsCustomPanelOpen()}");
        args.Context.AddString($"Input Blocked: {GUIManager.IsInputBlocked}");
        args.Context.AddString($"Using Asset Bundle: {UIInitializer.IsUsingAssetBundle}");
        args.Context.AddString($"Dialogue Profiles: {DialogueManager.GetAllProfileNames()?.Count ?? 0}");
   args.Context.AddString($"Quest Profiles: {QuestProfileManager.GetAllProfileNames()?.Count ?? 0}");
     args.Context.AddString("========================");
           }
     catch (Exception ex)
     {
 args.Context.AddString($"[FiresRPGmaker] uistatus error: {ex.Message}");
     }
    });

          // ============================================================
// END NPC UI DEBUG COMMANDS
            // ============================================================

 // Command: startsamplequest
         new Terminal.ConsoleCommand("startsamplequest", "Starts the sample KillBoarSample quest for debugging", (Terminal.ConsoleEventArgs args) =>
   {
       try
      {
if (Player.m_localPlayer == null)
         {
            args.Context.AddString("[FiresRPGmaker] No local player - cannot start quest");
             return;
        }

      if (VaultOfKnowledge.Instance == null)
 {
             args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized on this client; the server is authoritative. Try again after vault sync.");
   return;
   }

        if (QuestManager.Instance == null)
         {
                   args.Context.AddString("[FiresRPGmaker] QuestManager not initialized; cannot start sample quest");
  return;
        }

         string questId = "KillBoarSample";
             long playerId = Player.m_localPlayer.GetPlayerID();
          var vaultData = VaultOfKnowledge.Instance.GetPlayerData(playerId);
           if (vaultData == null)
       {
   args.Context.AddString("[FiresRPGmaker] Could not obtain vault data for local player; aborting");
 return;
     }

  var def = QuestManager.Instance.GetQuestDefinition(questId);
           if (def == null)
           {
         args.Context.AddString($"[FiresRPGmaker] Quest not found: {questId}");
                   return;
      }

     if (vaultData.ActiveQuests.Any(q => q.QuestId == questId))
           {
   args.Context.AddString($"[FiresRPGmaker] Quest already active: {questId}");
         return;
  }

        if (vaultData.CompletedQuests != null && vaultData.CompletedQuests.TryGetValue(questId, out var completedEntry))
       {
var localDef = QuestManager.Instance?.GetQuestDefinition(questId);
       if (localDef != null && localDef.CooldownDays > 0)
   {
               if (ChroniclesOfTime.IsQuestOnCooldown(completedEntry, localDef.CooldownDays, out DateTime availableAt))
 {
    string cooldownMsg = ChroniclesOfTime.BuildCooldownMessage(questId, localDef.Name, availableAt);
     args.Context.AddString($"[FiresRPGmaker] {cooldownMsg}");
    return;
 }
        else
   {
             args.Context.AddString($"[FiresRPGmaker] Quest cooldown expired; allowing restart: {questId}");
           }
             }
        else if (localDef == null || localDef.CooldownDays == 0)
{
      args.Context.AddString($"[FiresRPGmaker] Quest was previously completed but has no cooldown; allowing restart: {questId}");
            }
                    }

      try
    {
        if (ZRoutedRpc.instance != null)
         {
          if (!ZNet.instance.IsServer())
     {
         var pkg = new ZPackage();
      pkg.Write(playerId);
                  pkg.Write(questId);
     pkg.Write("active");
              pkg.Write(true);

              try
             {
   var snapshot = BuildInventorySnapshot(Player.m_localPlayer);
          if (snapshot != null && snapshot.Count > 0)
{
           pkg.Write(true);
         pkg.Write(snapshot.Count);
          foreach (var sb in snapshot)
    {
               try
{
          pkg.Write(sb.Type ?? "item");
   pkg.Write(sb.Prefab ?? string.Empty);
            pkg.Write(sb.Amount);
        pkg.Write(sb.Quality);
           pkg.Write(sb.Durability.ToString(CultureInfo.InvariantCulture));
          }
  catch { }
                 }
        }
  else
        {
                 pkg.Write(false);
      }
    }
 catch { try { pkg.Write(false); } catch { } }

 ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_Vault_UpdateQuestFlag", pkg);
         Debug.Log($"[FiresRPGmaker] Client sent UpdateQuestFlag RPC to server for player {playerId}, quest {questId}");
               args.Context.AddString($"[FiresRPGmaker] Requested server to start quest '{questId}'");
     }
  else
     {
     try
          {
      if (VaultOfKnowledge.Instance != null)
{
   VaultOfKnowledge.Instance.ApplyQuestFlagLocally(playerId, questId, "active", true);
           args.Context.AddString($"[FiresRPGmaker] Server started quest '{questId}'");
     }
         else
{
                 args.Context.AddString($"[FiresRPGmaker] VaultOfKnowledge not initialized on server");
         }
 }
     catch (Exception ex)
            {
   Debug.LogWarning($"[FiresRPGmaker] Failed to apply quest flag locally: {ex.Message}");
     }
         }
              }
     }
catch (Exception ex)
        {
       Debug.LogWarning($"[FiresRPGmaker] Failed to request quest start from server: {ex.Message}");
                 }

      string msg = $"Requested sample quest '{questId}' ({def.Name ?? "Unnamed"})";
     args.Context.AddString($"[FiresRPGmaker] {msg}");
       if (MessageHud.instance != null)
       MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, msg);
    }
       catch (Exception ex)
         {
        Debug.LogError($"[FiresRPGmaker] startsamplequest handler exception: {ex}");
 try { args.Context.AddString($"[FiresRPGmaker] startsamplequest exception: {ex.Message}"); } catch { }
        }
         });

            // Command: queststatus
         new Terminal.ConsoleCommand("queststatus", "Shows current quest status for local player", (Terminal.ConsoleEventArgs args) =>
            {
         if (Player.m_localPlayer == null)
    {
          args.Context.AddString("[FiresRPGmaker] No local player");
     return;
           }

     long playerId = Player.m_localPlayer.GetPlayerID();
          var data = VaultOfKnowledge.Instance.GetPlayerData(playerId);

        args.Context.AddString("=== Quest Status ===");
                args.Context.AddString($"Active quests ({data.ActiveQuests.Count}):");
      foreach (var q in data.ActiveQuests)
              {
        var def = QuestManager.Instance.GetQuestDefinition(q.QuestId);
        string name = def?.Name ?? q.QuestId;
               args.Context.AddString($" - {q.QuestId} ({name}): Progress {q.Progress}");
        }

  int completedCount = data.CompletedQuests?.Count ?? 0;
     args.Context.AddString($"Completed quests ({completedCount}):");
        if (data.CompletedQuests != null)
           {
        foreach (var kv in data.CompletedQuests)
      {
                   var q = kv.Key;
            var entry = kv.Value;
       var def = QuestManager.Instance.GetQuestDefinition(q);
             string name = def?.Name ?? q;

             string cooldownStatus = "";
   if (def != null && def.CooldownDays > 0)
              {
       if (ChroniclesOfTime.IsQuestOnCooldown(entry, def.CooldownDays, out DateTime availableAt))
             {
         cooldownStatus = $" [Cooldown: available {ChroniclesOfTime.GetAvailabilityMessage(availableAt)}]";
         }
       else
    {
      cooldownStatus = " [Ready to restart]";
        }
              }
  else
                    {
            cooldownStatus = " [No cooldown]";
               }

      args.Context.AddString($" - {q} x{entry.TimesCompleted} ({name}) Last: {entry.LastCompletedAt.ToLocalTime():g}{cooldownStatus}");
                 }
           }
            args.Context.AddString("====================");
            });

    // Command: savevault
    new Terminal.ConsoleCommand("savevault", "Forces a save of Vault data (debug)", (Terminal.ConsoleEventArgs args) =>
            {
  if (VaultOfKnowledge.Instance == null)
          {
  args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
 return;
          }

  VaultOfKnowledge.Instance.SaveAllDirty();
  args.Context.AddString("[FiresRPGmaker] Vault data force-saved");
    });

            // Command: startquest <QuestName>
            new Terminal.ConsoleCommand("startquest", "Starts a quest by display name. Usage: startquest=Quest Name", (Terminal.ConsoleEventArgs args) =>
     {
        try
     {
   if (Player.m_localPlayer == null)
        {
     args.Context.AddString("[FiresRPGmaker] No local player - cannot start quest");
   return;
       }

        if (QuestManager.Instance == null)
      {
 args.Context.AddString("[FiresRPGmaker] QuestManager not initialized");
            return;
      }

        string questName = (args.Args != null && args.Args.Length > 0) ? string.Join(" ", args.Args).Trim() : string.Empty;
        try { questName = global::FiresRPGmaker.Modules.Vault.VaultOfKnowledge.NormalizeQuestId(questName); } catch { }
    if (string.IsNullOrEmpty(questName))
    {
        args.Context.AddString("[FiresRPGmaker] Usage: startquest=Quest Name");
         return;
   }

        string questId = null;
      if (QuestManager.Instance != null)
           {
   questId = QuestManager.Instance.FindQuestIdByName(questName);
            if (questId == null)
      {
              var matches = QuestManager.Instance.SearchQuestNames(questName);
                 if (matches.Count == 0)
     {
                  // no local match
            }
 else if (matches.Count > 1)
            {
  args.Context.AddString($"[FiresRPGmaker] Multiple quests match '{questName}': {string.Join(", ", matches)}");
    return;
      }
    else
           {
         questId = QuestManager.Instance.FindQuestIdByName(matches[0]);
    }
     }
            }

       if (string.IsNullOrEmpty(questId))
   {
                if (ZRoutedRpc.instance != null && ZNet.instance != null && !ZNet.instance.IsServer())
        {
     args.Context.AddString($"[FiresRPGmaker] Quest not found locally; requesting server to start '{questName}' (using as quest id)");
         questId = questName;
         }
  else
{
             args.Context.AddString($"[FiresRPGmaker] No quest found matching '{questName}'");
          return;
                }
 }

   long playerId = Player.m_localPlayer.GetPlayerID();
var vaultData = VaultOfKnowledge.Instance.GetPlayerData(playerId);
  if (vaultData == null)
             {
 args.Context.AddString("[FiresRPGmaker] Could not obtain vault data for local player; aborting");
    return;
            }

     var def = QuestManager.Instance.GetQuestDefinition(questId);
      string defName = def?.Name ?? "(unresolved)";
          if (def == null)
                 {
       if (ZRoutedRpc.instance != null && ZNet.instance != null && !ZNet.instance.IsServer())
   {
          args.Context.AddString($"[FiresRPGmaker] Quest '{questId}' not found locally; requesting server to start (server will resolve by id)");
  }
                  else
       {
   args.Context.AddString($"[FiresRPGmaker] Quest not found by id: {questId}");
              return;
       }
    }

  if (vaultData.ActiveQuests.Any(q => q.QuestId == questId))
        {
       args.Context.AddString($"[FiresRPGmaker] Quest already active: {questId}");
 return;
        }

            if (vaultData.CompletedQuests != null && vaultData.CompletedQuests.TryGetValue(questId, out var completedEntry))
    {
        var localDef = QuestManager.Instance?.GetQuestDefinition(questId);
         if (localDef != null && localDef.CooldownDays > 0)
        {
  if (ChroniclesOfTime.IsQuestOnCooldown(completedEntry, localDef.CooldownDays, out DateTime availableAt))
         {
      string cooldownMsg = ChroniclesOfTime.BuildCooldownMessage(questId, localDef.Name, availableAt);
              args.Context.AddString($"[FiresRPGmaker] {cooldownMsg}");
    return;
     }
     else
    {
       args.Context.AddString($"[FiresRPGmaker] Quest cooldown expired; allowing restart: {questId}");
     }
      }
         else if (localDef == null || localDef.CooldownDays == 0)
    {
              args.Context.AddString($"[FiresRPGmaker] Quest was previously completed but has no cooldown; allowing restart: {questId}");
   }
       }

             try
             {
                 if (ZRoutedRpc.instance != null)
                 {
                     if (!ZNet.instance.IsServer())
                     {
                         var pkg = new ZPackage();
                         pkg.Write(playerId);
                         pkg.Write(questId);
                         pkg.Write("active");
                         pkg.Write(true);

                         try
                         {
                             var snapshot = BuildInventorySnapshot(Player.m_localPlayer);
                             if (snapshot != null && snapshot.Count > 0)
                             {
                                 pkg.Write(true);
                                 pkg.Write(snapshot.Count);
                                 foreach (var sb in snapshot)
                                 {
                                     try
                                     {
                                         pkg.Write(sb.Type ?? "item");
                                         pkg.Write(sb.Prefab ?? string.Empty);
                                         pkg.Write(sb.Amount);
                                         pkg.Write(sb.Quality);
                                         pkg.Write(sb.Durability.ToString(CultureInfo.InvariantCulture));
                                     }
                                     catch { }
                                 }
                             }
                             else
                             {
                                 pkg.Write(false);
                             }
                         }
                         catch { try { pkg.Write(false); } catch { } }

                         ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_Vault_UpdateQuestFlag", pkg);
                         args.Context.AddString($"[FiresRPGmaker] Requested server to start quest '{questId}'");
                     }
                     else
                     {
                         if (VaultOfKnowledge.Instance != null)
                         {
                             VaultOfKnowledge.Instance.ApplyQuestFlagLocally(playerId, questId, "active", true);
                             args.Context.AddString($"[FiresRPGmaker] Server started quest '{questId}'");
                         }
                     }
                 }
             }
             catch (Exception ex)
             {
                 Debug.LogWarning($"[FiresRPGmaker] Failed to request quest start from server: {ex.Message}");
             }

                args.Context.AddString($"[FiresRPGmaker] Start request processed for quest '{questId}' ({defName})");
     }
catch (Exception ex)
             {
             Debug.LogError($"[FiresRPGmaker] startquest handler exception: {ex}");
        try { args.Context.AddString($"[FiresRPGmaker] startquest exception: {ex.Message}"); } catch { }
         }
            });

        // Command: listquests [filter]
 new Terminal.ConsoleCommand("listquests", "Lists loaded quests (optional filter). Usage: listquests [term]", (Terminal.ConsoleEventArgs args) =>
  {
         try
           {
if (QuestManager.Instance == null)
    {
        QuestManager.Init();
 args.Context.AddString("[FiresRPGmaker] QuestManager initialized on demand");
             }

         string filter = (args.Args != null && args.Args.Length > 1) ? string.Join(" ", args.Args.Skip(1)).Trim() : string.Empty;   // 0.0.544: Args[0] is the command word ("Quests (0)" bug)
        var all = QuestManager.Instance.GetAllQuests();
    var list = string.IsNullOrEmpty(filter) ? all : all.Where(t => (t.Name ?? t.QuestId).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || t.QuestId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

      args.Context.AddString($"[FiresRPGmaker] Quests ({list.Count}):");
     foreach (var q in list)
    {
           args.Context.AddString($" - {q.QuestId} -> {q.Name}");
   }
    }
     catch (Exception ex)
              {
                    Debug.LogWarning($"[FiresRPGmaker] listquests failed: {ex.Message}");
           }
     });

            // Ensure client HUD exists for quest timers
    try { global::FiresRPGmaker.Modules.Vault.QuestTimerHUD.EnsureCreated(); } catch { }
            // Ensure the right-side quest tracker HUD exists (collapsible; hides at zero quests / Ctrl+F3)
    try { global::FiresRPGmaker.UI.QuestTrackerHud.EnsureCreated(); } catch { }

     // Command: vaultclean <playerName>
         new Terminal.ConsoleCommand("vaultclean", "Clears quests and bank items for the named player's vault file (admin only). Usage: vaultclean <playerName>", (Terminal.ConsoleEventArgs args) =>
          {
     try
      {
           if (VaultOfKnowledge.Instance == null)
         {
     args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
           return;
         }

        // Permission check: allow if server or local player is admin
        if (ZNet.instance != null && !ZNet.instance.IsServer())
      {
     if (Player.m_localPlayer == null)
   {
       args.Context.AddString("[FiresRPGmaker] No local player - cannot execute");
     return;
   }
         var localVault = VaultOfKnowledge.Instance.GetPlayerData(Player.m_localPlayer.GetPlayerID());
       if (localVault == null || !localVault.IsAdmin)
   {
     args.Context.AddString("[FiresRPGmaker] Permission denied - admin required");
      return;
         }
      }

    string targetName = (args.Args != null && args.Args.Length > 0) ? string.Join(" ", args.Args).Trim() : string.Empty;
        if (string.IsNullOrEmpty(targetName))
      {
    args.Context.AddString("[FiresRPGmaker] Usage: vaultclean <playerName>");
      return;
    }

     var playersDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Players);
      if (!Directory.Exists(playersDir))
     {
     args.Context.AddString("[FiresRPGmaker] Players directory does not exist");
      return;
    }

          string Sanitize(string s)
     {
        var invalid = Path.GetInvalidFileNameChars();
   var filtered = new string(s.Where(c => !invalid.Contains(c)).ToArray());
                return filtered.Replace(' ', '_');
        }

      string targetSan = Sanitize(targetName);

var matches = Directory.GetFiles(playersDir, "*.json").Where(f =>
              {
        var fname = Path.GetFileNameWithoutExtension(f);
       var prefix = fname.Split(new[] { '_' }, 2)[0];
 return string.Equals(prefix, targetName, StringComparison.OrdinalIgnoreCase) || string.Equals(prefix, targetSan, StringComparison.OrdinalIgnoreCase);
       }).ToList();

           if (matches.Count == 0)
        {
         args.Context.AddString($"[FiresRPGmaker] No player files found for name '{targetName}'");
          return;
         }

 int cleaned = 0;
   foreach (var file in matches)
  {
    try
         {
           string json = File.ReadAllText(file);
           var data = JsonConvert.DeserializeObject<PlayerVaultData>(json);
        if (data == null) continue;

    data.ActiveQuests = new List<ActiveQuestData>();
      data.CompletedQuests = new Dictionary<string, CompletedQuestEntry>(StringComparer.OrdinalIgnoreCase);
      data.FailedQuests = new List<string>();
       data.BankItems = new List<object>();
             data.IsDirty = true;

      var player = Player.GetPlayer(data.PlayerId);
                if (player != null)
       {
    try
         {
               VaultOfKnowledge.Instance.SerializeToCustomData(player, data);
  VaultOfKnowledge.Instance.SavePlayerDataFromZDO(data.PlayerId, player);
      args.Context.AddString($"[FiresRPGmaker] Cleared vault for online player {data.PlayerName} (ID {data.PlayerId})");
    }
     catch (Exception ex)
                {
         args.Context.AddString($"[FiresRPGmaker] Failed to save ZDO for {data.PlayerName}: {ex.Message}");
 }
            }
         else
         {
     try
        {
             VaultOfKnowledge.Instance.SavePlayerData(data.PlayerId, data);
              args.Context.AddString($"[FiresRPGmaker] Cleared vault file {Path.GetFileName(file)} for {data.PlayerName} (ID {data.PlayerId})");
             }
 catch (Exception ex)
    {
  args.Context.AddString($"[FiresRPGmaker] Failed to save file for {data.PlayerName}: {ex.Message}");
           }
               }

       cleaned++;
   }
           catch (Exception ex)
{
          args.Context.AddString($"[FiresRPGmaker] Error processing {Path.GetFileName(file)}: {ex.Message}");
}
       }

              args.Context.AddString($"[FiresRPGmaker] Vaultclean processed {cleaned} file(s) for name '{targetName}'");
         }
            catch (Exception ex)
                {
     Debug.LogWarning($"[FiresRPGmaker] vaultclean handler exception: {ex.Message}");
         try { args.Context.AddString($"[FiresRPGmaker] vaultclean exception: {ex.Message}"); } catch { }
          }
    });

        // Command: firesnpcs_dialogue_cooldown_reset [optionKeyOrUID]
        // Clears dialogue-option cooldowns from the server store. With no arg, clears everything;
        // with an arg, clears just that option key. Server-side only; admin-gated on a client.
        new Terminal.ConsoleCommand("firesnpcs_dialogue_cooldown_reset",
            "Clears dialogue option cooldowns on the server (admin only). Usage: firesnpcs_dialogue_cooldown_reset [optionKey]",
            (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer())
                {
                    args.Context.AddString("[FiresRPGmaker] Dialogue cooldowns are stored on the server - run this on the server/host.");
                    return;
                }

                // Permission check on a listen-server host (the local player must be admin).
                if (!ZNet.instance.IsDedicated() && Player.m_localPlayer != null && VaultOfKnowledge.Instance != null)
                {
                    var localVault = VaultOfKnowledge.Instance.GetPlayerData(Player.m_localPlayer.GetPlayerID());
                    if (localVault == null || !localVault.IsAdmin)
                    {
                        args.Context.AddString("[FiresRPGmaker] Permission denied - admin required");
                        return;
                    }
                }

                string arg = (args.Args != null && args.Args.Length > 1) ? string.Join(" ", args.Args.Skip(1)).Trim() : string.Empty;
                if (string.IsNullOrEmpty(arg))
                {
                    global::FiresRPGmaker.Modules.Dialogues.DialogueCooldownManager.ClearAll();
                    args.Context.AddString("[FiresRPGmaker] Cleared ALL dialogue option cooldowns.");
                }
                else
                {
                    global::FiresRPGmaker.Modules.Dialogues.DialogueCooldownManager.ClearKey(arg);
                    args.Context.AddString($"[FiresRPGmaker] Cleared dialogue cooldowns for key '{arg}'.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FiresRPGmaker] dialogue_cooldown_reset handler exception: {ex.Message}");
                try { args.Context.AddString($"[FiresRPGmaker] reset exception: {ex.Message}"); } catch { }
            }
        });

        // Command: pushcfgs - send local Quests, QuestProfiles, and Territories cfgs to server (admin only)
     Terminal.ConsoleEvent pushCfgHandler = (Terminal.ConsoleEventArgs args) =>
          {
     try
          {
         bool forceAll = args.Args != null && args.Args.Any(a =>
                 string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(a, "-f", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(a, "force", StringComparison.OrdinalIgnoreCase));

       bool isServer = (ZNet.instance != null && ZNet.instance.IsServer());
   if (!isServer)
     {
       if (Player.m_localPlayer == null)
     {
        args.Context.AddString("[FiresRPGmaker] No local player - cannot execute pushcfgs");
 return;
   }
      var localVault = VaultOfKnowledge.Instance?.GetPlayerData(Player.m_localPlayer.GetPlayerID());
    if (localVault == null || !localVault.IsAdmin)
  {
            args.Context.AddString("[FiresRPGmaker] Permission denied - admin required to push cfg files to server");
   return;
        }
    }

                    // Fires family: also push FAP's preset/EWP yaml (and any
                    // other registered mod's server data) up to the server, so
                    // one pushcfgs ships everything. Client-side only — the
                    // bridge no-ops on the server/host.
                    if (!isServer)
                    {
                        try { FiresCore.Bridge.ServerPushBridge.InvokeAll(s => args.Context.AddString(s)); }
                        catch (System.Exception ex) { args.Context.AddString($"[FiresRPGmaker] FAP push bridge failed: {ex.Message}"); }
                    }

 var baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker");
     var questsDir = Path.Combine(baseDir, "Quests");
        var profilesDir = Path.Combine(baseDir, "QuestProfiles");
       var territoriesDir = Path.Combine(baseDir, "Territories");
      var serverInfosDir = Path.Combine(baseDir, "ServerInfos");
      var dialoguesDir = Path.Combine(baseDir, "Dialogues");
      var tradersDir = Path.Combine(baseDir, "Traders");

        DateTime cutoff = forceAll ? DateTime.MinValue : _lastPushTime;
  DateTime pushStartTime = DateTime.UtcNow;

                var filesToSend = new List<(string folder, string name, string content, DateTime modified)>();
          int skippedUnchanged = 0;
   bool territoriesChanged = false;
  bool serverInfosChanged = false;
        var newHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

     string ComputeHash(string content)
     {
         using (var md5 = System.Security.Cryptography.MD5.Create())
         {
             var bytes = System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty);
             var hash = md5.ComputeHash(bytes);
             var sb = new System.Text.StringBuilder(hash.Length * 2);
             foreach (var b in hash) sb.Append(b.ToString("x2"));
             return sb.ToString();
         }
     }

     // Cache of file content read during IsModified - avoids double-read
     var contentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

     bool IsModified(string filePath, string folder, string name)
     {
         try
         {
             string content = File.ReadAllText(filePath);
             string key = folder + "/" + name;
             contentCache[key] = content;
             string hash = ComputeHash(content);
             newHashes[key] = hash;
             if (forceAll) return true;
             if (_lastPushHashes.TryGetValue(key, out var oldHash) &&
                 string.Equals(hash, oldHash, StringComparison.OrdinalIgnoreCase))
                 return false;
             return true;
         }
         catch { return true; }
     }

     void CollectFolder(string dir, string folder, string pattern = "*.cfg", bool recursive = false)
     {
         if (!Directory.Exists(dir)) return;
         var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
         string baseFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
         foreach (var f in Directory.GetFiles(dir, pattern, searchOpt))
         {
             try
             {
                 string rel;
                 if (recursive)
                 {
                     var full = Path.GetFullPath(f);
                     rel = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                         ? full.Substring(baseFull.Length + 1) : Path.GetFileName(f);
                 }
                 else
                 {
                     rel = Path.GetFileName(f);
                 }

                 if (IsModified(f, folder, rel))
                 {
                     string key = folder + "/" + rel;
                     string content = contentCache.TryGetValue(key, out var cached) ? cached : File.ReadAllText(f);
                     filesToSend.Add((folder, rel, content, File.GetLastWriteTimeUtc(f)));
                 }
                 else
                 {
                     skippedUnchanged++;
                 }
             }
             catch { }
         }
     }

     CollectFolder(questsDir, "Quests");
     CollectFolder(profilesDir, "QuestProfiles");
     CollectFolder(territoriesDir, "Territories", "*.cfg", true);
     CollectFolder(serverInfosDir, "ServerInfos");
     CollectFolder(dialoguesDir, "Dialogues", "*.cfg", true);
     CollectFolder(tradersDir, "Traders");

               // Push RuntimeSprites PNGs first (before cfgs to avoid timeout from cfg flood)
               int spriteCount = global::FiresRPGmaker.UI.QuestJournal.RuntimeSpriteSync.PushSpritesToServer(
                   cutoff, forceAll, msg => args.Context.AddString(msg));

               int questCount = filesToSend.Count(f => f.folder == "Quests");
               int profileCount = filesToSend.Count(f => f.folder == "QuestProfiles");
               int territoryCount = filesToSend.Count(f => f.folder == "Territories");
               int serverInfoCount = filesToSend.Count(f => f.folder == "ServerInfos");
               int dialogueCount = filesToSend.Count(f => f.folder == "Dialogues");
               int traderCount = filesToSend.Count(f => f.folder == "Traders");

               if (filesToSend.Count == 0 && spriteCount == 0)
               {
                   if (skippedUnchanged > 0)
                       args.Context.AddString($"[FiresRPGmaker] No modified files to push ({skippedUnchanged} unchanged). Use 'pushcfgs --force' to push all.");
                   else
                       args.Context.AddString("[FiresRPGmaker] No config files or sprites found to push");
                   SavePushManifest(newHashes);
                   return;
               }

               int sent = 0;

   if (!isServer)
 {
         if (ZRoutedRpc.instance == null)
     {
   args.Context.AddString("[FiresRPGmaker] No network RPC instance available");
          return;
     }

     // Use a coroutine to send RPCs with small delays to avoid flooding the Steam connection
     SaveLastPushTime(pushStartTime);
     SavePushManifest(newHashes);

         var summary = new List<string>();
    if (questCount > 0) summary.Add($"{questCount} quest(s)");
             if (profileCount > 0) summary.Add($"{profileCount} profile(s)");
          if (territoryCount > 0) summary.Add($"{territoryCount} territory(ies)");
          if (serverInfoCount > 0) summary.Add($"{serverInfoCount} server info(s)");
          if (dialogueCount > 0) summary.Add($"{dialogueCount} dialogue(s)");
          if (traderCount > 0) summary.Add($"{traderCount} trader profile(s)");
               if (spriteCount > 0) summary.Add($"{spriteCount} sprite(s)");
         string skippedNote = skippedUnchanged > 0 ? $" ({skippedUnchanged} unchanged skipped)" : "";
        args.Context.AddString($"[FiresRPGmaker] Queuing {string.Join(", ", summary)} to server{skippedNote}");

     if (FiresRPGmaker.Instance != null)
     {
         FiresRPGmaker.Instance.StartCoroutine(SendFilesWithDelay(filesToSend, args.Context));
     }
     else
     {
         // Fallback: send all at once (shouldn't happen)
         foreach (var entry in filesToSend)
         {
             try
             {
                 var pkg = new ZPackage();
                 pkg.Write(entry.folder);
                 pkg.Write(entry.name);
                 pkg.Write(entry.content ?? string.Empty);
                 var rpcName = string.Equals(entry.folder, "Territories", StringComparison.OrdinalIgnoreCase)
                     ? "FiresRPGmaker_Territory_FileUpdate"
                     : "FiresRPGmaker_SyncFiles";
                 ZRoutedRpc.instance.InvokeRoutedRPC(0L, rpcName, pkg);
             }
             catch { }
         }
         args.Context.AddString("[FiresRPGmaker] Push complete");
     }
            }
         else
               {
    try
     {
      Directory.CreateDirectory(questsDir);
            Directory.CreateDirectory(profilesDir);
     Directory.CreateDirectory(serverInfosDir);
       foreach (var entry in filesToSend)
          {
  try
     {
 var targetDir = Path.Combine(baseDir, entry.folder);
      Directory.CreateDirectory(targetDir);
           var fullPath = Path.Combine(targetDir, entry.name);
                var parent = Path.GetDirectoryName(fullPath);
   if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        if (string.IsNullOrEmpty(entry.content))
        {
      if (File.Exists(fullPath)) File.Delete(fullPath);
       }
    else
     {
           File.WriteAllText(fullPath, entry.content);
     }
             var pkg = new ZPackage();
      pkg.Write(entry.folder);
 pkg.Write(entry.name);
   pkg.Write(entry.content ?? string.Empty);
    var rpcName = string.Equals(entry.folder, "Territories", StringComparison.OrdinalIgnoreCase)
 ? "FiresRPGmaker_Territory_FileUpdate"
: "FiresRPGmaker_SyncFiles";
            VaultOfKnowledge.SafeInvokeRoutedRPC(0L, rpcName, pkg);
           sent++;
 if (string.Equals(entry.folder, "Territories", StringComparison.OrdinalIgnoreCase)) territoriesChanged = true;
        if (string.Equals(entry.folder, "ServerInfos", StringComparison.OrdinalIgnoreCase)) serverInfosChanged = true;
 }
     catch { }
     }

         if (questCount > 0 || profileCount > 0)
        {
      try { QuestManager.Instance?.LoadQuestDatabase(); } catch { }
        try { QuestManager.Instance?.LoadQuestProfiles(); } catch { }
        }
 if (territoriesChanged)
           {
          try { global::FiresRPGmaker.Modules.Territories.TerritoryLedger.Reload(); } catch { }
      try { global::FiresRPGmaker.Modules.Territories.TerritorySyncedState.BroadcastTerritories(); } catch { }
       }
          if (serverInfosChanged)
               {
       try { global::FiresRPGmaker.UI.Screens.InfoNpcScreenController.ReloadCache(); } catch { }
   }
   if (dialogueCount > 0)
   {
       try { global::FiresRPGmaker.Modules.Dialogues.DialogueCodex.Reload(); } catch { }
   }
   if (traderCount > 0)
   {
       try { global::FiresRPGmaker.Managers.TraderProfileManager.Reset(); global::FiresRPGmaker.Managers.TraderProfileManager.Initialize(); } catch { }
   }

          SaveLastPushTime(pushStartTime);
          SavePushManifest(newHashes);

         var summary = new List<string>();
                if (questCount > 0) summary.Add($"{questCount} quest(s)");
             if (profileCount > 0) summary.Add($"{profileCount} profile(s)");
            if (territoryCount > 0) summary.Add($"{territoryCount} territory(ies)");
           if (serverInfoCount > 0) summary.Add($"{serverInfoCount} server info(s)");
             if (dialogueCount > 0) summary.Add($"{dialogueCount} dialogue(s)");
             if (traderCount > 0) summary.Add($"{traderCount} trader profile(s)");
                  if (spriteCount > 0) summary.Add($"{spriteCount} sprite(s)");
            string skippedNote = skippedUnchanged > 0 ? $" ({skippedUnchanged} unchanged skipped)" : "";
       args.Context.AddString($"[FiresRPGmaker] Applied {string.Join(", ", summary)}{skippedNote}");
  }
           catch (Exception ex)
              {
     args.Context.AddString($"[FiresRPGmaker] Server error: {ex.Message}");
    }
        }
   }
         catch (Exception ex)
         {
         Debug.LogError($"[FiresRPGmaker] pushcfgs handler exception: {ex}");
     try { args.Context.AddString($"[FiresRPGmaker] pushcfgs exception: {ex.Message}"); } catch { }
            }
 };
            new Terminal.ConsoleCommand("pushcfgs", "Push modified cfg files to server. Use --force to push all. (admin only)", pushCfgHandler);
         new Terminal.ConsoleCommand("pushquests", "(alias) pushcfgs", pushCfgHandler);

          // ============================================================
    // COMPANION DEBUG COMMANDS
 // ============================================================

   // Command: spawncompanion - Spawns a companion NPC in front of the player
         new Terminal.ConsoleCommand("spawncompanion", "Spawns a companion NPC in front of the player. Usage: spawncompanion [prefabName]", (Terminal.ConsoleEventArgs args) =>
 {
  try
       {
     if (Player.m_localPlayer == null)
{
  args.Context.AddString("[FiresRPGmaker] No local player - cannot spawn companion");
      return;
        }

             // Default prefab name
    string prefabName = "CompanionNpc";
         
         // args.Args[0] is the command name "spawncompanion", actual arguments start at index 1
          if (args.Args != null && args.Args.Length > 1)
     {
     // Skip the command name (index 0) and join the rest
            prefabName = string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim();
              if (string.IsNullOrEmpty(prefabName))
         {
         prefabName = "CompanionNpc";
        }
            }

            Debug.Log($"[FiresRPGmaker] spawncompanion: Using prefab name '{prefabName}'");

         Vector3 playerPos = Player.m_localPlayer.transform.position;
  Vector3 playerForward = Player.m_localPlayer.transform.forward;
     Vector3 spawnPos = playerPos + playerForward * 3f + Vector3.up * 0.5f;

       var companion = FiresCore.Npc.CompanionPrefabManager.SpawnCompanion(
      prefabName, spawnPos, Quaternion.identity);

   if (companion != null)
     {
          args.Context.AddString($"[FiresRPGmaker] Spawned companion '{prefabName}' at {spawnPos}");
          MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
     $"Companion spawned! Give it 100 Coins to recruit.");
           }
     else
    {
      args.Context.AddString($"[FiresRPGmaker] Failed to spawn companion '{prefabName}'");

    var available = FiresCore.Npc.CompanionPrefabManager.GetLoadedCompanionNames();
      if (available.Count > 0)
       {
            args.Context.AddString($"[FiresRPGmaker] Available companions: {string.Join(", ", available)}");
    }
          else
 {
        args.Context.AddString("[FiresRPGmaker] No companion prefabs loaded");
       }
  }
         }
       catch (Exception ex)
      {
          Debug.LogError($"[FiresRPGmaker] spawncompanion exception: {ex}");
       args.Context.AddString($"[FiresRPGmaker] spawncompanion error: {ex.Message}");
           }
            });

    // Command: listcompanions - Lists all loaded companion prefabs
      new Terminal.ConsoleCommand("listcompanions", "Lists all loaded companion PREFABS (not your saved companions - use 'companioninfo' for that)", (Terminal.ConsoleEventArgs args) =>
            {
     try
  {
          var companions = FiresCore.Npc.CompanionPrefabManager.GetLoadedCompanionNames();
        if (companions.Count == 0)
  {
            args.Context.AddString("[FiresRPGmaker] No companion prefabs loaded");
             }
         else
       {
      args.Context.AddString($"[FiresRPGmaker] Loaded companion PREFABS ({companions.Count}):");
      args.Context.AddString("[FiresRPGmaker] (These are templates, not your saved companions)");
   foreach (var name in companions)
       {
            args.Context.AddString($"  - {name}");
          }
          args.Context.AddString("[FiresRPGmaker] Use 'companioninfo' to see YOUR saved companions");
          args.Context.AddString("[FiresRPGmaker] Use 'listlostcompanions' to see missing companions");
      }
       }
    catch (Exception ex)
  {
args.Context.AddString($"[FiresRPGmaker] listcompanions error: {ex.Message}");
             }
            });

         // Command: restorecompanions - Forces restoration of player's companions from vault
    new Terminal.ConsoleCommand("restorecompanions", "Forces restoration of your FOLLOWING companions from vault (teleports existing, spawns missing)", (Terminal.ConsoleEventArgs args) =>
            {
     try
    {
     if (Player.m_localPlayer == null)
      {
          args.Context.AddString("[FiresRPGmaker] No local player - cannot restore companions");
         return;
   }
            
            args.Context.AddString("[FiresRPGmaker] NOTE: This only restores companions with IsFollowing=true");
            args.Context.AddString("[FiresRPGmaker] Use 'recovercompanion <name>' to recover specific companions by name");

              CompanionPatches.ForceRestoreCompanions();
         args.Context.AddString("[FiresRPGmaker] Companion restoration triggered - check log for results");
    }
        catch (Exception ex)
    {
  args.Context.AddString($"[FiresRPGmaker] restorecompanions error: {ex.Message}");
    }
    });

       // Command: companioninfo - Shows info about player's companions in vault
       new Terminal.ConsoleCommand("companioninfo", "Shows info about your companions stored in vault", (Terminal.ConsoleEventArgs args) =>
      {
  try
         {
     if (Player.m_localPlayer == null)
 {
  args.Context.AddString("[FiresRPGmaker] No local player");
 return;
     }

   if (VaultOfKnowledge.Instance == null)
       {
      args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
     return;
    }

long playerId = Player.m_localPlayer.GetPlayerID();
   var companions = VaultOfKnowledge.Instance.GetCompanions(playerId);
      
      args.Context.AddString($"=== Companion Info (Player {playerId}) ===");
        args.Context.AddString($"Companions in vault: {companions.Count}");

           foreach (var c in companions)
   {
 args.Context.AddString($"  - {c.CompanionName} (ID: {c.CompanionId})");
      args.Context.AddString($"    Prefab: {c.PrefabName}, Following: {c.IsFollowing}");
    }
        
       // Also show companions currently in world
int worldCount = 0;
   foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
  {
              if (controller != null && controller.ownerPlayerId == playerId)
        {
        worldCount++;
      var status = controller.isDefeated ? "DEFEATED" : (controller.IsFollowing ? "Following" : "Staying");
     args.Context.AddString($"  [WORLD] {controller.GetDisplayName()} (ID: {controller.companionId}) - {status}");
      }
   }
      
    args.Context.AddString($"Companions in world: {worldCount}");
    args.Context.AddString("=====================================");
       }
    catch (Exception ex)
     {
       args.Context.AddString($"[FiresRPGmaker] companioninfo error: {ex.Message}");
            }
  });

        // Command: recovercompanion - Spawns a missing companion by name from vault data
        new Terminal.ConsoleCommand("recovercompanion", "Recovers a lost companion by name from vault data. Usage: recovercompanion <name>", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player - cannot recover companion");
                    return;
                }

                if (VaultOfKnowledge.Instance == null)
                {
                    args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
                    return;
                }

                // Get companion name from args
                string searchName = (args.Args != null && args.Args.Length > 1) 
                    ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim() 
                    : string.Empty;

                if (string.IsNullOrEmpty(searchName))
                {
                    args.Context.AddString("[FiresRPGmaker] Usage: recovercompanion <companion name>");
                    args.Context.AddString("[FiresRPGmaker] Use 'companioninfo' to see your saved companions");
                    return;
                }

                long playerId = Player.m_localPlayer.GetPlayerID();
                args.Context.AddString($"[FiresRPGmaker] Searching vault for player ID: {playerId}");
                
                // Get companions from vault
                var companions = VaultOfKnowledge.Instance.GetCompanions(playerId);
                
                // Debug: Also check the raw player data
                var playerData = VaultOfKnowledge.Instance.GetPlayerData(playerId);
                if (playerData != null)
                {
                    int rawCount = playerData.Companions?.Count ?? 0;
                    args.Context.AddString($"[FiresRPGmaker] Raw vault data: {rawCount} companion entries");
                }

                if (companions == null || companions.Count == 0)
                {
                    args.Context.AddString("[FiresRPGmaker] No companions found in vault for this player");
                    args.Context.AddString("[FiresRPGmaker] DEBUG: Check if vault data is synced from server");
                    
                    // Try to load directly from player's customData
                    if (Player.m_localPlayer.m_customData.TryGetValue("FiresRPGmaker_Vault", out string vaultJson))
                    {
                        args.Context.AddString($"[FiresRPGmaker] Vault JSON in customData: {(string.IsNullOrEmpty(vaultJson) ? "EMPTY" : $"{vaultJson.Length} chars")}");
                        if (!string.IsNullOrEmpty(vaultJson) && vaultJson.Contains("Companions"))
                        {
                            args.Context.AddString("[FiresRPGmaker] Companions key found in JSON - parsing issue?");
                        }
                    }
                    else
                    {
                        args.Context.AddString("[FiresRPGmaker] No FiresRPGmaker_Vault in customData - vault not synced?");
                    }
                    return;
                }
                
                args.Context.AddString($"[FiresRPGmaker] Found {companions.Count} companions in vault");

                // Find matching companion (case-insensitive partial match)
                var matches = companions.Where(c => 
                    (c.CompanionName != null && c.CompanionName.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.DisplayNameOverride != null && c.DisplayNameOverride.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.CompanionId != null && c.CompanionId.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                if (matches.Count == 0)
                {
                    args.Context.AddString($"[FiresRPGmaker] No companion found matching '{searchName}'");
                    args.Context.AddString("[FiresRPGmaker] Available companions:");
                    foreach (var c in companions)
                    {
                        args.Context.AddString($"  - {c.CompanionName} (ID: {c.CompanionId})");
                    }
                    return;
                }

                if (matches.Count > 1)
                {
                    args.Context.AddString($"[FiresRPGmaker] Multiple companions match '{searchName}':");
                    foreach (var c in matches)
                    {
                        args.Context.AddString($"  - {c.CompanionName} (ID: {c.CompanionId})");
                    }
                    args.Context.AddString("[FiresRPGmaker] Please use a more specific name or the companion ID");
                    return;
                }

                var companionData = matches[0];

                // Check if companion already exists in world
                foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (controller != null && controller.companionId == companionData.CompanionId)
                    {
                        args.Context.AddString($"[FiresRPGmaker] Companion '{companionData.CompanionName}' already exists in world!");
                        args.Context.AddString("[FiresRPGmaker] Use 'restorecompanions' to teleport existing companions to you");
                        return;
                    }
                }

                // Spawn the companion
                string prefabName = companionData.PrefabName ?? "CompanionNpc";
                Vector3 playerPos = Player.m_localPlayer.transform.position;
                Vector3 playerForward = Player.m_localPlayer.transform.forward;
                Vector3 spawnPos = playerPos + playerForward * 3f + Vector3.up * 0.5f;

                // Try to find ground height
                if (ZoneSystem.instance != null)
                {
                    float groundHeight;
                    if (ZoneSystem.instance.GetGroundHeight(spawnPos, out groundHeight))
                    {
                        spawnPos.y = groundHeight + 0.5f;
                    }
                }

                var companionGO = CompanionPrefabManager.SpawnCompanion(prefabName, spawnPos, Quaternion.identity);

                if (companionGO == null)
                {
                    args.Context.AddString($"[FiresRPGmaker] Failed to spawn companion prefab '{prefabName}'");
                    return;
                }

                var newController = companionGO.GetComponent<CompanionController>();
                if (newController != null)
                {
                    // Restore all the saved data
                    newController.companionId = companionData.CompanionId;
                    newController.companionName = companionData.CompanionName;
                    newController.displayNameOverride = companionData.DisplayNameOverride;
                    newController.isTamed = true;
                    newController.ownerPlayerId = playerId;
                    newController.isDefeated = false;

                    // Set tamed state on Character
                    var character = companionGO.GetComponent<Character>();
                    if (character != null)
                    {
                        character.SetTamed(true);
                    }

                    // Set on ZDO
                    var nview = companionGO.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid())
                    {
                        var zdo = nview.GetZDO();
                        if (zdo != null)
                        {
                            zdo.Set(ZDOVars.s_tamed, true);
                            zdo.Set("companion_tamed", true);
                            zdo.Set("companion_id", companionData.CompanionId);
                            zdo.Set("companion_owner", playerId);
                            zdo.Set("companion_name", companionData.CompanionName);
                            zdo.Set(ZDOVars.s_tamedName, companionData.CompanionName);
                        }
                    }

                    // Restore equipment from vault
                    var inventory = newController.GetInventory();
                    if (inventory != null && companionData.EquipmentPrefabs != null)
                    {
                        foreach (var kvp in companionData.EquipmentPrefabs)
                        {
                            try
                            {
                                var slot = (CompanionInventory.EquipmentSlot)Enum.Parse(
                                    typeof(CompanionInventory.EquipmentSlot), kvp.Key);
                                var quality = companionData.EquipmentQualities != null && 
                                    companionData.EquipmentQualities.ContainsKey(kvp.Key)
                                    ? companionData.EquipmentQualities[kvp.Key] : 1;
                                inventory.RestoreEquipmentFromVault(slot, kvp.Value, quality);
                            }
                            catch { }
                        }
                        inventory.RecalculateEquipmentBonusesPublic();
                    }

                    // Restore storage inventory
                    if (!string.IsNullOrEmpty(companionData.StorageInventoryData) && inventory != null)
                    {
                        try
                        {
                            var storageInv = inventory.GetStorageInventory();
                            if (storageInv != null)
                            {
                                var pkg = new ZPackage(companionData.StorageInventoryData);
                                storageInv.Load(pkg);
                            }
                        }
                        catch { }
                    }

                    // Set to follow
                    newController.CommandFollow(Player.m_localPlayer);
                    newController.SaveToZDO();

                    // Clear any pending respawn state
                    if (companionData.IsPendingRespawn)
                    {
                        companionData.IsPendingRespawn = false;
                        companionData.RespawnTimeRemaining = 0;
                        companionData.DeathTimestamp = 0;
                        VaultOfKnowledge.Instance.SaveCompanion(playerId, companionData);
                    }

                    args.Context.AddString($"[FiresRPGmaker] Successfully recovered companion '{companionData.CompanionName}'!");
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        $"{companionData.CompanionName} has been recovered!");
                }
                else
                {
                    args.Context.AddString("[FiresRPGmaker] Failed to configure recovered companion");
                    CompanionNetworkHelper.Destroy(companionGO);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] recovercompanion exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] recovercompanion error: {ex.Message}");
            }
        });

        // Command: listlostcompanions - Lists companions in vault that aren't in the world
        new Terminal.ConsoleCommand("listlostcompanions", "Lists companions saved in vault that are not currently in the world", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player");
                    return;
                }

                if (VaultOfKnowledge.Instance == null)
                {
                    args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
                    return;
                }

                long playerId = Player.m_localPlayer.GetPlayerID();
                var companions = VaultOfKnowledge.Instance.GetCompanions(playerId);

                if (companions == null || companions.Count == 0)
                {
                    args.Context.AddString("[FiresRPGmaker] No companions found in vault");
                    return;
                }

                // Get all companions currently in world
                var worldCompanionIds = new HashSet<string>();
                foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (controller != null && !string.IsNullOrEmpty(controller.companionId))
                    {
                        worldCompanionIds.Add(controller.companionId);
                    }
                }

                // Find missing companions
                var lostCompanions = companions.Where(c => !worldCompanionIds.Contains(c.CompanionId)).ToList();

                if (lostCompanions.Count == 0)
                {
                    args.Context.AddString("[FiresRPGmaker] All your companions are present in the world!");
                    return;
                }

                args.Context.AddString($"=== Lost Companions ({lostCompanions.Count}) ===");
                foreach (var c in lostCompanions)
                {
                    string status = c.IsPendingRespawn ? " [PENDING RESPAWN]" : "";
                    string following = c.IsFollowing ? " (was following)" : " (was staying)";
                    args.Context.AddString($"  - {c.CompanionName}{status}{following}");
                    args.Context.AddString($"    ID: {c.CompanionId}");
                    
                    int equipCount = c.EquipmentPrefabs?.Count ?? 0;
                    if (equipCount > 0)
                    {
                        args.Context.AddString($"    Equipment: {equipCount} items");
                    }
                }
                args.Context.AddString("=====================================");
                args.Context.AddString("[FiresRPGmaker] Use 'recovercompanion <name>' to recover a lost companion");
            }
            catch (Exception ex)
            {
                args.Context.AddString($"[FiresRPGmaker] listlostcompanions error: {ex.Message}");
            }
        });

    // ============================================================
 // END COMPANION DEBUG COMMANDS
            // ============================================================

        // Command: deletecompanion - Permanently removes a companion from world and vault
        new Terminal.ConsoleCommand("deletecompanion", "Permanently deletes a companion by name. Usage: deletecompanion <name>", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player - cannot delete companion");
                    return;
                }

                if (VaultOfKnowledge.Instance == null)
                {
                    args.Context.AddString("[FiresRPGmaker] VaultOfKnowledge not initialized");
                    return;
                }

                // Get companion name from args
                string searchName = (args.Args != null && args.Args.Length > 1) 
                    ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim() 
                    : string.Empty;

                if (string.IsNullOrEmpty(searchName))
                {
                    args.Context.AddString("[FiresRPGmaker] Usage: deletecompanion <companion name>");
                    args.Context.AddString("[FiresRPGmaker] Use 'companioninfo' to see your saved companions");
                    args.Context.AddString("[FiresRPGmaker] WARNING: This permanently deletes the companion!");
                    return;
                }

                long playerId = Player.m_localPlayer.GetPlayerID();
                var companions = VaultOfKnowledge.Instance.GetCompanions(playerId);

                if (companions == null || companions.Count == 0)
                {
                    args.Context.AddString("[FiresRPGmaker] No companions found in vault for this player");
                    return;
                }

                // Find matching companion (case-insensitive partial match)
                var matches = companions.Where(c => 
                    (c.CompanionName != null && c.CompanionName.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.DisplayNameOverride != null && c.DisplayNameOverride.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (c.CompanionId != null && c.CompanionId.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0)
                ).ToList();

                if (matches.Count == 0)
                {
                    args.Context.AddString($"[FiresRPGmaker] No companion found matching '{searchName}'");
                    args.Context.AddString("[FiresRPGmaker] Available companions:");
                    foreach (var c in companions)
                    {
                        args.Context.AddString($"  - {c.CompanionName} (ID: {c.CompanionId})");
                    }
                    return;
                }

                if (matches.Count > 1)
                {
                    args.Context.AddString($"[FiresRPGmaker] Multiple companions match '{searchName}':");
                    foreach (var c in matches)
                    {
                        args.Context.AddString($"  - {c.CompanionName} (ID: {c.CompanionId})");
                    }
                    args.Context.AddString("[FiresRPGmaker] Please use a more specific name or the companion ID");
                    return;
                }

                var companionData = matches[0];
                string companionName = companionData.CompanionName;
                string companionId = companionData.CompanionId;

                // First, destroy the companion if it exists in the world
                bool foundInWorld = false;
                foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (controller != null && controller.companionId == companionId)
                    {
                        foundInWorld = true;
                        CompanionNetworkHelper.Destroy(controller.gameObject);
                        args.Context.AddString($"[FiresRPGmaker] Destroyed companion '{companionName}' from world");
                        break;
                    }
                }

                // Remove from vault
                try
                {
                    VaultOfKnowledge.Instance.RemoveCompanion(playerId, companionId);
                    args.Context.AddString($"[FiresRPGmaker] Removed companion '{companionName}' from vault");
                }
                catch (Exception removeEx)
                {
                    args.Context.AddString($"[FiresRPGmaker] Failed to remove companion from vault: {removeEx.Message}");
                }

                // Cancel any pending respawns
                if (CompanionRespawnManager.Instance != null)
                {
                    CompanionRespawnManager.Instance.CancelRespawn(companionId);
                }

                string location = foundInWorld ? "world and vault" : "vault only";
                args.Context.AddString($"[FiresRPGmaker] Companion '{companionName}' has been permanently deleted from {location}");
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"{companionName} has been permanently dismissed.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] deletecompanion exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] deletecompanion error: {ex.Message}");
            }
        });

        // Command: findcompanion - Shows companion location on the map
        new Terminal.ConsoleCommand("findcompanion", "Shows a companion's location on the map. Usage: findcompanion <name>", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player - cannot find companion");
                    return;
                }

                // Get companion name from args
                string searchName = (args.Args != null && args.Args.Length > 1) 
                    ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim() 
                    : string.Empty;

                long playerId = Player.m_localPlayer.GetPlayerID();

                // If no name provided, show all owned companions in world
                if (string.IsNullOrEmpty(searchName))
                {
                    args.Context.AddString("[FiresRPGmaker] Companion Locations:");
                    int found = 0;
                    foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
                    {
                        if (controller != null && controller.ownerPlayerId == playerId)
                        {
                            found++;
                            Vector3 pos = controller.transform.position;
                            float dist = Vector3.Distance(Player.m_localPlayer.transform.position, pos);
                            string status = controller.IsFollowing ? "Following" : "Staying";
                            args.Context.AddString($"  - {controller.GetDisplayName()} ({status}) - {dist:F0}m away at ({pos.x:F0}, {pos.z:F0})");
                            
                            // Add temporary map pin
                            AddTemporaryMapPin(pos, controller.GetDisplayName());
                        }
                    }
                    
                    if (found == 0)
                    {
                        args.Context.AddString("[FiresRPGmaker] No companions found in world");
                        args.Context.AddString("[FiresRPGmaker] Use 'listlostcompanions' to see missing companions");
                    }
                    else
                    {
                        args.Context.AddString($"[FiresRPGmaker] Showing {found} companion(s) on map for 60 seconds");
                    }
                    return;
                }

                // Search for specific companion
                CompanionController target = null;
                foreach (var controller in UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None))
                {
                    if (controller == null) continue;
                    
                    string name = controller.GetDisplayName() ?? "";
                    string id = controller.companionId ?? "";
                    
                    if (name.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        id.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        target = controller;
                        break;
                    }
                }

                if (target == null)
                {
                    args.Context.AddString($"[FiresRPGmaker] No companion found in world matching '{searchName}'");
                    
                    // Check vault for lost companions
                    if (VaultOfKnowledge.Instance != null)
                    {
                        var companions = VaultOfKnowledge.Instance.GetCompanions(playerId);
                        var vaultMatch = companions?.FirstOrDefault(c => 
                            (c.CompanionName != null && c.CompanionName.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0));
                        
                        if (vaultMatch != null)
                        {
                            args.Context.AddString($"[FiresRPGmaker] Found '{vaultMatch.CompanionName}' in vault but not in world");
                            args.Context.AddString("[FiresRPGmaker] Use 'recovercompanion' to recover lost companions");
                        }
                    }
                    return;
                }

                Vector3 companionPos = target.transform.position;
                float distance = Vector3.Distance(Player.m_localPlayer.transform.position, companionPos);
                string statusText = target.IsFollowing ? "Following" : "Staying";

                args.Context.AddString($"[FiresRPGmaker] Found {target.GetDisplayName()}:");
                args.Context.AddString($"  Status: {statusText}");
                args.Context.AddString($"  Distance: {distance:F0}m");
                args.Context.AddString($"  Position: ({companionPos.x:F0}, {companionPos.y:F0}, {companionPos.z:F0})");

                // Add temporary map pin
                AddTemporaryMapPin(companionPos, target.GetDisplayName());
                args.Context.AddString($"[FiresRPGmaker] Added temporary map pin for 60 seconds");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] findcompanion exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] findcompanion error: {ex.Message}");
            }
        });

        // Command: purgeallcompanions - Nuclear option to remove ALL companions from world and vault
        new Terminal.ConsoleCommand("purgeallcompanions", "NUCLEAR: Permanently deletes ALL your companions from world AND vault. Type 'purgeallcompanions CONFIRM' to execute.", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player - cannot purge companions");
                    return;
                }

                // Require confirmation
                bool confirmed = args.Args != null && args.Args.Length > 1 && 
                    string.Equals(args.Args[1], "CONFIRM", StringComparison.OrdinalIgnoreCase);
                
                if (!confirmed)
                {
                    args.Context.AddString("[FiresRPGmaker] WARNING: This will permanently delete ALL your companions!");
                    args.Context.AddString("[FiresRPGmaker] This includes:");
                    args.Context.AddString("  - All companions in the world");
                    args.Context.AddString("  - All companion data in your vault");
                    args.Context.AddString("  - All companion data in your character's customData");
                    args.Context.AddString("[FiresRPGmaker] Type 'purgeallcompanions CONFIRM' to execute");
                    return;
                }

                long playerId = Player.m_localPlayer.GetPlayerID();
                int destroyedWorld = 0;
                int removedVault = 0;

                args.Context.AddString("[FiresRPGmaker] === PURGING ALL COMPANIONS ===");

                // Step 1: Cancel ALL pending respawns first
                try
                {
                    if (CompanionRespawnManager.Instance != null)
                    {
                        var companions = VaultOfKnowledge.Instance?.GetCompanions(playerId);
                        if (companions != null)
                        {
                            foreach (var c in companions)
                            {
                                if (!string.IsNullOrEmpty(c.CompanionId))
                                {
                                    CompanionRespawnManager.Instance.CancelRespawn(c.CompanionId);
                                }
                            }
                        }
                        args.Context.AddString("[FiresRPGmaker] Cancelled all pending respawns");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"[FiresRPGmaker] Warning: Failed to cancel respawns: {ex.Message}");
                }

                // Step 2: Destroy ALL companions in the world that belong to this player
                try
                {
                    var allControllers = UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None);
                    var toDestroy = new List<GameObject>();
                    
                    foreach (var controller in allControllers)
                    {
                        if (controller != null && controller.ownerPlayerId == playerId)
                        {
                            toDestroy.Add(controller.gameObject);
                            args.Context.AddString($"[FiresRPGmaker] Queued for destruction: {controller.GetDisplayName()} (ID: {controller.companionId})");
                        }
                    }

                    foreach (var go in toDestroy)
                    {
                        try
                        {
                            CompanionNetworkHelper.Destroy(go);
                            destroyedWorld++;
                            args.Context.AddString($"[FiresRPGmaker] Destroyed companion object");
                        }
                        catch (Exception destroyEx)
                        {
                            args.Context.AddString($"[FiresRPGmaker] Destroy failed: {destroyEx.Message}");
                        }
                    }
                    args.Context.AddString($"[FiresRPGmaker] Destroyed {destroyedWorld} companion(s) from world");
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"[FiresRPGmaker] Warning: World destruction error: {ex.Message}");
                }

                // Step 3: Remove ALL companions from vault
                try
                {
                    var companions = VaultOfKnowledge.Instance?.GetCompanions(playerId);
                    if (companions != null && companions.Count > 0)
                    {
                        var idsToRemove = companions.Select(c => c.CompanionId).ToList();
                        foreach (var id in idsToRemove)
                        {
                            try
                            {
                                VaultOfKnowledge.Instance.RemoveCompanion(playerId, id);
                                removedVault++;
                            }
                            catch { }
                        }
                        args.Context.AddString($"[FiresRPGmaker] Removed {removedVault} companion(s) from vault");
                    }
                    else
                    {
                        args.Context.AddString("[FiresRPGmaker] No companions in vault to remove");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"[FiresRPGmaker] Warning: Vault removal error: {ex.Message}");
                }

                // Step 4: FORCE clear companions from player's customData
                try
                {
                    if (Player.m_localPlayer.m_customData.TryGetValue("FiresRPGmaker_Vault", out string vaultJson))
                    {
                        if (!string.IsNullOrEmpty(vaultJson))
                        {
                            var vaultData = JsonConvert.DeserializeObject<PlayerVaultData>(vaultJson);
                            if (vaultData != null)
                            {
                                int cleared = vaultData.Companions?.Count ?? 0;
                                vaultData.Companions = new List<object>();
                                vaultData.IsDirty = true;
                                
                                var capturedVault = vaultData;
                                int capturedCleared = cleared;
                                PlayerSpawnGate.RunWhenLocalReady(5f, lp =>
                                {
                                    try
                                    {
                                        lp.m_customData["FiresRPGmaker_Vault"] = JsonConvert.SerializeObject(capturedVault);
                                        lp.m_customData["FiresRPGmaker_Dirty"] = "1";
                                    }
                                    catch { }
                                });

                                args.Context.AddString($"[FiresRPGmaker] Cleared {cleared} companion(s) from player customData");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"[FiresRPGmaker] Warning: CustomData clear error: {ex.Message}");
                }

                // Step 5: Force save
                try
                {
                    VaultOfKnowledge.Instance?.SaveAllDirty();
                    args.Context.AddString("[FiresRPGmaker] Forced vault save");
                }
                catch { }

                args.Context.AddString("[FiresRPGmaker] === PURGE COMPLETE ===");
                args.Context.AddString($"[FiresRPGmaker] Destroyed {destroyedWorld} from world, removed {removedVault} from vault");
                args.Context.AddString("[FiresRPGmaker] IMPORTANT: Save your game and relog for changes to fully take effect");
                args.Context.AddString("[FiresRPGmaker] The world save still contains the ZDO data until the world is saved");
                
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"Purged all companions: {destroyedWorld} from world, {removedVault} from vault");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] purgeallcompanions exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] purgeallcompanions error: {ex.Message}");
            }
        });

        // Command: destroyallcompanionnpcs - Destroys ALL CompanionNpc objects in the loaded world (admin only)
        new Terminal.ConsoleCommand("destroyallcompanionnpcs", "ADMIN: Destroys ALL CompanionNpc objects in loaded chunks. Type 'destroyallcompanionnpcs CONFIRM' to execute.", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("[FiresRPGmaker] No local player");
                    return;
                }

                // Require confirmation
                bool confirmed = args.Args != null && args.Args.Length > 1 && 
                    string.Equals(args.Args[1], "CONFIRM", StringComparison.OrdinalIgnoreCase);
                
                if (!confirmed)
                {
                    args.Context.AddString("[FiresRPGmaker] WARNING: This will destroy ALL CompanionNpc objects in loaded chunks!");
                    args.Context.AddString("[FiresRPGmaker] This affects ALL players' companions, not just yours!");
                    args.Context.AddString("[FiresRPGmaker] Type 'destroyallcompanionnpcs CONFIRM' to execute");
                    return;
                }

                int destroyed = 0;

                // Find and destroy ALL CompanionController objects
                var allControllers = UnityEngine.Object.FindObjectsByType<CompanionController>(UnityEngine.FindObjectsSortMode.None);
                args.Context.AddString($"[FiresRPGmaker] Found {allControllers.Length} CompanionController(s) in world");

                foreach (var controller in allControllers)
                {
                    if (controller == null) continue;
                    
                    try
                    {
                        string name = controller.GetDisplayName() ?? "Unknown";
                        string id = controller.companionId ?? "NoID";
                        long owner = controller.ownerPlayerId;
                        
                        args.Context.AddString($"[FiresRPGmaker] Destroying: {name} (ID: {id}, Owner: {owner})");

                        CompanionNetworkHelper.Destroy(controller.gameObject);
                        destroyed++;
                    }
                    catch (Exception ex)
                    {
                        args.Context.AddString($"[FiresRPGmaker] Failed to destroy: {ex.Message}");
                    }
                }

                args.Context.AddString($"[FiresRPGmaker] Destroyed {destroyed} CompanionNpc object(s)");
                args.Context.AddString("[FiresRPGmaker] IMPORTANT: Save your game for the world ZDO data to be updated!");
                
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    $"Destroyed {destroyed} CompanionNpc objects");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] destroyallcompanionnpcs exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] destroyallcompanionnpcs error: {ex.Message}");
            }
        });

        // ============================================================
        // TERRAIN/HEIGHTMAP DEBUG COMMANDS
        // ============================================================

        // Command: dumpskybox - Dumps skybox and environment zone shader/material info
        new Terminal.ConsoleCommand("dumpskybox", "Dumps all skybox, EnvZone, and environment shader/material info to a text file", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpSkyboxAndEnvironmentInfo(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] dumpskybox exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] dumpskybox error: {ex.Message}");
            }
        });

        // Command: listshaders - Lists all loaded shaders in the game
        new Terminal.ConsoleCommand("listshaders", "Lists all loaded shaders that can be used for custom materials. Optional filter: listshaders <filter>", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                ListAllShaders(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listshaders exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listshaders error: {ex.Message}");
            }
        });

        // Command: dumpenvironments - Dumps all environment details to a text file
        new Terminal.ConsoleCommand("dumpenvironments", "Dumps all environment definitions with complete details (colors, fog, wind, etc.) to a text file", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpAllEnvironments(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] dumpenvironments exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] dumpenvironments error: {ex.Message}");
            }
        });

        // Command: dumpglobalshaders - Dumps all global shader properties currently set
        new Terminal.ConsoleCommand("dumpglobalshaders", "Dumps all global shader properties (snow, fog, lighting, etc.) to a file", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpGlobalShaderProperties(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] dumpglobalshaders exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] dumpglobalshaders error: {ex.Message}");
            }
        });

        // ============================================================
        // END TERRAIN/HEIGHTMAP DEBUG COMMANDS
        // ============================================================

        // ============================================================
        // RESOURCE PREFAB DEBUG COMMANDS
        // ============================================================

        // Command: listminerocks - Lists all prefabs with MineRock or MineRock5 component
        new Terminal.ConsoleCommand("listminerocks", "Lists all prefabs with MineRock or MineRock5 component (ore deposits, rocks)", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpMineRockPrefabs(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listminerocks exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listminerocks error: {ex.Message}");
            }
        });

        // Command: listpickables - Lists all prefabs with Pickable component
        new Terminal.ConsoleCommand("listpickables", "Lists all prefabs with Pickable component (berries, mushrooms, etc.)", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpPickablePrefabs(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listpickables exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listpickables error: {ex.Message}");
            }
        });

        // Command: listtrees - Lists all prefabs with TreeBase component
        new Terminal.ConsoleCommand("listtrees", "Lists all prefabs with TreeBase component (trees, stumps)", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpTreeBasePrefabs(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listtrees exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listtrees error: {ex.Message}");
            }
        });

        // Command: listdestructibles - Lists all prefabs with IDestructible (TreeLog, etc.)
        new Terminal.ConsoleCommand("listdestructibles", "Lists all prefabs with TreeLog component (fallen logs)", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpTreeLogPrefabs(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listdestructibles exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listdestructibles error: {ex.Message}");
            }
        });

        // ============================================================
        // END RESOURCE PREFAB DEBUG COMMANDS
        // ============================================================

        // Command: listsprites - Lists all available sprites in the game for UI use
        new Terminal.ConsoleCommand("listsprites", "Lists all available sprites in the game. Usage: listsprites [filter]", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpAvailableSprites(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listsprites exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listsprites error: {ex.Message}");
            }
        });

        // Command: listicons - Lists all icon sprites specifically (items, skills, etc)
        new Terminal.ConsoleCommand("listicons", "Lists icon sprites (items, skills, abilities). Usage: listicons [filter]", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpIconSprites(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] listicons exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] listicons error: {ex.Message}");
            }
        });

        // Register dump_chat_ui command (dumps chat window hierarchy to file)
        new Terminal.ConsoleCommand("dump_chat_ui", "Dumps the Chat UI hierarchy to a text file for debugging layout", (Terminal.ConsoleEventArgs args) =>
        {
            try
            {
                DumpChatUIHierarchy(args);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FiresRPGmaker] dump_chat_ui exception: {ex}");
                args.Context.AddString($"[FiresRPGmaker] dump_chat_ui error: {ex.Message}");
            }
        });

        // Register Recipe Dumper commands (craftable item recipe dump)
        try
        {
            DebugRecipeDumper.RegisterCommands();
            FiresLogger.LogVerbose("DebugRecipeDumper commands registered");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FiresRPGmaker] Failed to register DebugRecipeDumper commands: {ex.Message}");
        }

        // Register Prefab Dumper commands (prefab localization dump)
        try
        {
            DebugPrefabDumper.RegisterCommands();
            FiresLogger.LogVerbose("DebugPrefabDumper commands registered");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FiresRPGmaker] Failed to register DebugPrefabDumper commands: {ex.Message}");
        }

            FiresLogger.LogVerbose("Console commands registered: startsamplequest, queststatus, savevault, vaultclean, listquests, startquest, pushcfgs, spawncompanion, listcompanions, dumpglobalshaders");
        }

        private static string FormatColor(Color c)
        {
            return $"RGBA({c.r:F3}, {c.g:F3}, {c.b:F3}, {c.a:F3})";
        }

        /// <summary>
        /// Dumps all skybox, EnvZone, and environment-related shader and material info to a text file.
        /// </summary>
        private static void DumpSkyboxAndEnvironmentInfo(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Scanning skybox and environment info...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM SKYBOX & ENVIRONMENT REFERENCE");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Section 1: Current RenderSettings
            sb.AppendLine("=== RENDER SETTINGS (Current) ===");
            sb.AppendLine();
            sb.AppendLine($"Ambient Mode: {RenderSettings.ambientMode}");
            sb.AppendLine($"Ambient Light: {FormatColor(RenderSettings.ambientLight)}");
            sb.AppendLine($"Ambient Intensity: {RenderSettings.ambientIntensity:F3}");
            sb.AppendLine($"Ambient Sky Color: {FormatColor(RenderSettings.ambientSkyColor)}");
            sb.AppendLine($"Ambient Equator Color: {FormatColor(RenderSettings.ambientEquatorColor)}");
            sb.AppendLine($"Ambient Ground Color: {FormatColor(RenderSettings.ambientGroundColor)}");
            sb.AppendLine($"Fog: {RenderSettings.fog}");
            sb.AppendLine($"Fog Color: {FormatColor(RenderSettings.fogColor)}");
            sb.AppendLine($"Fog Mode: {RenderSettings.fogMode}");
            sb.AppendLine($"Fog Density: {RenderSettings.fogDensity:F4}");
            sb.AppendLine($"Fog Start Distance: {RenderSettings.fogStartDistance:F1}");
            sb.AppendLine($"Fog End Distance: {RenderSettings.fogEndDistance:F1}");
            sb.AppendLine();

            // Section 2: Current Skybox Material
            sb.AppendLine("=== CURRENT SKYBOX MATERIAL ===");
            sb.AppendLine();
            var skybox = RenderSettings.skybox;
            if (skybox != null)
            {
                DumpMaterialInfo(sb, skybox, "Skybox");
            }
            else
            {
                sb.AppendLine("Skybox Material: NULL");
            }
            sb.AppendLine();

            // Section 3: EnvMan Info
            sb.AppendLine("=== ENVMAN STATE ===");
            sb.AppendLine();
            if (EnvMan.instance != null)
            {
                sb.AppendLine($"Instance: EXISTS");
                
                // Get forced environment via reflection since it may be private
                try
                {
                    var forceEnvField = typeof(EnvMan).GetField("m_forceEnv", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (forceEnvField != null)
                    {
                        var forceEnv = forceEnvField.GetValue(EnvMan.instance) as string;
                        sb.AppendLine($"Forced Environment: \"{forceEnv ?? ""}\"");
                    }
                    
                    var debugEnvField = typeof(EnvMan).GetField("m_debugEnv", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    if (debugEnvField != null)
                    {
                        var debugEnv = debugEnvField.GetValue(EnvMan.instance) as string;
                        sb.AppendLine($"Debug Environment: \"{debugEnv ?? ""}\"");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"(Could not read force/debug env: {ex.Message})");
                }
                
                var currentEnv = EnvMan.instance.GetCurrentEnvironment();
                if (currentEnv != null)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Current Environment: {currentEnv.m_name}");
                    sb.AppendLine($"  Is Wet: {currentEnv.m_isWet}");
                    sb.AppendLine($"  Is Freezing: {currentEnv.m_isFreezing}");
                    sb.AppendLine($"  Is Cold: {currentEnv.m_isCold}");
                    sb.AppendLine($"  Always Dark: {currentEnv.m_alwaysDark}");
                    sb.AppendLine($"  Ambient Night: {FormatColor(currentEnv.m_ambColorNight)}");
                    sb.AppendLine($"  Ambient Day: {FormatColor(currentEnv.m_ambColorDay)}");
                    sb.AppendLine($"  Fog Color Day: {FormatColor(currentEnv.m_fogColorDay)}");
                    sb.AppendLine($"  Fog Density Day: {currentEnv.m_fogDensityDay:F4}");
                    sb.AppendLine($"  Light Intensity Day: {currentEnv.m_lightIntensityDay:F3}");
                    sb.AppendLine($"  Wind Min/Max: {currentEnv.m_windMin:F2}/{currentEnv.m_windMax:F2}");
                }
                
                // List all environments
                sb.AppendLine();
                sb.AppendLine("=== ALL ENVIRONMENT NAMES ===");
                sb.AppendLine();
                if (EnvMan.instance.m_environments != null)
                {
                    foreach (var env in EnvMan.instance.m_environments)
                    {
                        string flags = "";
                        if (env.m_alwaysDark) flags += "[DARK]";
                        if (env.m_isWet) flags += "[WET]";
                        if (env.m_isFreezing) flags += "[FREEZE]";
                        if (env.m_isCold) flags += "[COLD]";
                        sb.AppendLine($"  \"{env.m_name}\" {flags}");
                    }
                }
            }
            else
            {
                sb.AppendLine("EnvMan: NOT AVAILABLE");
            }
            sb.AppendLine();

            // Section 4: Find all EnvZone objects in scene
            sb.AppendLine("=== ENVZONE OBJECTS IN SCENE ===");
            sb.AppendLine();
            var envZones = UnityEngine.Object.FindObjectsByType<EnvZone>(UnityEngine.FindObjectsSortMode.None);
            sb.AppendLine($"Total EnvZones: {envZones.Length}");
            foreach (var zone in envZones)
            {
                sb.AppendLine($"  - {zone.gameObject.name}");
                sb.AppendLine($"    Environment: \"{zone.m_environment}\"");
                sb.AppendLine($"    Force: {zone.m_force}");
                sb.AppendLine($"    Position: {zone.transform.position}");
                sb.AppendLine($"    Scale: {zone.transform.lossyScale}");
                
                // Check for collider
                var collider = zone.GetComponent<Collider>();
                if (collider != null)
                {
                    sb.AppendLine($"    Collider: {collider.GetType().Name}, IsTrigger: {collider.isTrigger}");
                    if (collider is BoxCollider box)
                    {
                        sb.AppendLine($"    Box Size: {box.size}");
                        sb.AppendLine($"    Box Center: {box.center}");
                    }
                }
                
                // Check for renderer/material
                var renderer = zone.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                {
                    var mat = renderer.sharedMaterial;
                    sb.AppendLine($"    Material: {mat.name}");
                    sb.AppendLine($"    Shader: {mat.shader?.name ?? "NULL"}");
                }
                
                if (zone.m_exteriorMesh != null)
                {
                    sb.AppendLine($"    Exterior Mesh: {zone.m_exteriorMesh.gameObject.name}");
                    if (zone.m_exteriorMesh.sharedMaterial != null)
                    {
                        sb.AppendLine($"    Exterior Material: {zone.m_exteriorMesh.sharedMaterial.name}");
                        sb.AppendLine($"    Exterior Shader: {zone.m_exteriorMesh.sharedMaterial.shader?.name ?? "NULL"}");
                    }
                }
                sb.AppendLine();
            }

            // Section 5: Common shaders for transparent/unlit rendering
            sb.AppendLine("=== RECOMMENDED SHADERS FOR ENVBOX ===");
            sb.AppendLine();
            sb.AppendLine("For semi-transparent box visualization, try these shaders:");
            sb.AppendLine();
            
            string[] shadersToCheck = {
                "Standard",
                "Unlit/Color",
                "Unlit/Transparent",
                "Sprites/Default",
                "UI/Default",
                "Legacy Shaders/Transparent/Diffuse",
                "Legacy Shaders/Transparent/VertexLit",
                "Particles/Standard Unlit",
                "Custom/Blob",
                "Custom/Distortion",
                "Custom/GuiScroll",
                "Custom/LitGui",
                "Custom/LitParticles",
                "Custom/ParticleDecal",
                "Custom/Piece",
                "Custom/Standard TwoSided",
                "Custom/Vegetation",
                "Custom/Water",
                "Custom/WaterBottom",
                "Custom/WaterMask",
                "Custom/Trilinear"
            };
            
            foreach (var shaderName in shadersToCheck)
            {
                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    sb.AppendLine($"  [FOUND] {shaderName}");
                }
                else
                {
                    sb.AppendLine($"  [NOT FOUND] {shaderName}");
                }
            }
            sb.AppendLine();

            // Section 6: Code snippet for creating proper transparent material
            sb.AppendLine("=== CODE SNIPPET FOR TRANSPARENT MATERIAL ===");
            sb.AppendLine();
            sb.AppendLine(@"
// Method 1: Using Standard shader with transparency
private static Material CreateTransparentMaterial(Color color)
{
    // Try Standard shader first
    var shader = Shader.Find(""Standard"");
    if (shader == null)
    {
        // Fallback to Unlit/Color
        shader = Shader.Find(""Unlit/Color"");
    }
    
    var mat = new Material(shader);
    
    // For Standard shader, set up transparency
    if (shader.name == ""Standard"")
    {
        mat.SetFloat(""_Mode"", 3); // Transparent mode
        mat.SetInt(""_SrcBlend"", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt(""_DstBlend"", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt(""_ZWrite"", 0);
        mat.DisableKeyword(""_ALPHATEST_ON"");
        mat.EnableKeyword(""_ALPHABLEND_ON"");
        mat.DisableKeyword(""_ALPHAPREMULTIPLY_ON"");
        mat.renderQueue = 3000;
    }
    
    mat.color = color;
    return mat;
}

// Method 2: Using Legacy transparent shader (more compatible)
private static Material CreateLegacyTransparentMaterial(Color color)
{
    var shader = Shader.Find(""Legacy Shaders/Transparent/Diffuse"");
    if (shader == null)
    {
        shader = Shader.Find(""Sprites/Default""); // Fallback
    }
    
    var mat = new Material(shader);
    mat.color = color;
    mat.renderQueue = 3000; // Transparent queue
    return mat;
}
");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "SkyboxEnvironmentReference.txt");
            
            File.WriteAllText(outputPath, sb.ToString());
            
            args.Context.AddString($"[FiresRPGmaker] Skybox/Environment reference saved to: {outputPath}");
            Debug.Log($"[FiresRPGmaker] Skybox/Environment reference saved to: {outputPath}");
        }

        /// <summary>
        /// Dumps detailed info about a material.
        /// </summary>
        private static void DumpMaterialInfo(System.Text.StringBuilder sb, Material mat, string label)
        {
            sb.AppendLine($"{label} Material: {mat.name}");
            sb.AppendLine($"  Shader: {mat.shader?.name ?? "NULL"}");
            sb.AppendLine($"  Render Queue: {mat.renderQueue}");
            sb.AppendLine($"  Pass Count: {mat.passCount}");
            
            if (mat.shader != null && mat.shader.isSupported)
            {
                int propCount = mat.shader.GetPropertyCount();
                sb.AppendLine($"  Property Count: {propCount}");
                
                for (int i = 0; i < propCount && i < 30; i++) // Limit to first 30
                {
                    try
                    {
                        string propName = mat.shader.GetPropertyName(i);
                        var propType = mat.shader.GetPropertyType(i);
                        
                        string value = "";
                        switch (propType)
                        {
                            case ShaderPropertyType.Color:
                                if (mat.HasProperty(propName))
                                    value = FormatColor(mat.GetColor(propName));
                                break;
                            case ShaderPropertyType.Float:
                            case ShaderPropertyType.Range:
                                if (mat.HasProperty(propName))
                                    value = mat.GetFloat(propName).ToString("F4");
                                break;
                            case ShaderPropertyType.Vector:
                                if (mat.HasProperty(propName))
                                {
                                    var v = mat.GetVector(propName);
                                    value = $"({v.x:F2}, {v.y:F2}, {v.z:F2}, {v.w:F2})";
                                }
                                break;
                            case ShaderPropertyType.Texture:
                                if (mat.HasProperty(propName))
                                {
                                    var tex = mat.GetTexture(propName);
                                    value = tex != null ? $"{tex.name} ({tex.width}x{tex.height})" : "NULL";
                                }
                                break;
                        }
                        
                        sb.AppendLine($"    {propName} ({propType}): {value}");
                    }
                    catch { }
                }
            }
            
            // Keywords
            var keywords = mat.shaderKeywords;
            if (keywords != null && keywords.Length > 0)
            {
                sb.AppendLine($"  Keywords: {string.Join(", ", keywords)}");
            }
        }

        /// <summary>
        /// Lists all shaders that are currently loaded/available and dumps to a file.
        /// </summary>
        private static void ListAllShaders(Terminal.ConsoleEventArgs args)
        {
            string filter = (args.Args != null && args.Args.Length > 1) 
                ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim().ToLower()
                : "";

            args.Context.AddString("[FiresRPGmaker] Scanning for loaded shaders...");

            var foundShaders = new HashSet<string>();
            
            // Method 1: Check all materials in scene
            var allMaterials = Resources.FindObjectsOfTypeAll<Material>();
            foreach (var mat in allMaterials)
            {
                if (mat != null && mat.shader != null)
                {
                    foundShaders.Add(mat.shader.name);
                }
            }
            
            // Method 2: Check common shader names
            string[] commonShaders = {
                "Standard", "Standard (Specular setup)",
                "Unlit/Color", "Unlit/Texture", "Unlit/Transparent", "Unlit/Transparent Cutout",
                "Sprites/Default", "Sprites/Diffuse",
                "UI/Default", "UI/Unlit/Transparent",
                "Particles/Standard Surface", "Particles/Standard Unlit",
                "Legacy Shaders/Diffuse", "Legacy Shaders/Transparent/Diffuse",
                "Legacy Shaders/Transparent/VertexLit", "Legacy Shaders/Self-Illumin/Diffuse",
                "Skybox/Procedural", "Skybox/6 Sided", "Skybox/Cubemap",
                "Nature/Tree Creator Bark", "Nature/Tree Creator Leaves",
                "Custom/Blob", "Custom/Distortion", "Custom/GuiScroll",
                "Custom/Heightmap", "Custom/LitGui", "Custom/LitParticles",
                "Custom/ParticleDecal", "Custom/Piece", "Custom/Standard TwoSided",
                "Custom/Vegetation", "Custom/Water", "Custom/WaterBottom",
                "Custom/WaterMask", "Custom/Trilinear", "Custom/staticrock",
                "Custom/creature", "Custom/yggdrasil", "Custom/SkyboxProcedural",
                "Custom/InteriorSide"
            };
            
            foreach (var name in commonShaders)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                {
                    foundShaders.Add(name);
                }
            }
            
            // Filter and sort
            var results = foundShaders
                .Where(s => string.IsNullOrEmpty(filter) || s.ToLower().Contains(filter))
                .OrderBy(s => s)
                .ToList();
            
            // Build output for file
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM SHADER REFERENCE");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();
            sb.AppendLine($"Total Shaders Found: {results.Count}");
            if (!string.IsNullOrEmpty(filter))
            {
                sb.AppendLine($"Filter: {filter}");
            }
            sb.AppendLine();
            sb.AppendLine("=== ALL AVAILABLE SHADERS ===");
            sb.AppendLine();
            
            foreach (var shaderName in results)
            {
                var shader = Shader.Find(shaderName);
                string status = shader != null && shader.isSupported ? "[SUPPORTED]" : "[NOT SUPPORTED]";
                sb.AppendLine($"  {shaderName} {status}");
            }
            sb.AppendLine();
            
            // Categorize shaders
            sb.AppendLine("=== SHADERS BY CATEGORY ===");
            sb.AppendLine();
            
            sb.AppendLine("  [Standard/Built-in]");
            foreach (var s in results.Where(s => s.StartsWith("Standard") || s.StartsWith("Unlit/") || s.StartsWith("Legacy")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            sb.AppendLine("  [UI/Sprites]");
            foreach (var s in results.Where(s => s.StartsWith("UI/") || s.StartsWith("Sprites/")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            sb.AppendLine("  [Custom/Valheim]");
            foreach (var s in results.Where(s => s.StartsWith("Custom/")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            sb.AppendLine("  [Skybox]");
            foreach (var s in results.Where(s => s.Contains("Skybox") || s.Contains("skybox")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            sb.AppendLine("  [Particles/Effects]");
            foreach (var s in results.Where(s => s.StartsWith("Particles/") || s.Contains("Particle")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            sb.AppendLine("  [Nature/Environment]");
            foreach (var s in results.Where(s => s.StartsWith("Nature/") || s.Contains("Vegetation") || s.Contains("Water")))
            {
                sb.AppendLine($"    - {s}");
            }
            sb.AppendLine();
            
            // Recommended shaders for specific uses
            sb.AppendLine("=== RECOMMENDED SHADERS FOR MOD USE ===");
            sb.AppendLine();
            sb.AppendLine("  For transparent overlays (like EnvBox visualization):");
            sb.AppendLine("    - Sprites/Default (best compatibility)");
            sb.AppendLine("    - UI/Default (alternative)");
            sb.AppendLine();
            sb.AppendLine("  For terrain materials:");
            sb.AppendLine("    - Custom/Heightmap (vanilla terrain shader)");
            sb.AppendLine();
            sb.AppendLine("  For building pieces:");
            sb.AppendLine("    - Custom/Piece");
            sb.AppendLine("    - Custom/Standard TwoSided");
            sb.AppendLine();
            sb.AppendLine("  For environment/skybox:");
            sb.AppendLine("    - Custom/SkyboxProcedural");
            sb.AppendLine("    - Custom/InteriorSide (dungeon interior)");
            sb.AppendLine();
            
            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "ShaderReference.txt");
            
            File.WriteAllText(outputPath, sb.ToString());
            
            // Also output to console
            args.Context.AddString($"[FiresRPGmaker] Found {results.Count} shaders" + 
                (string.IsNullOrEmpty(filter) ? "" : $" matching '{filter}'"));
            args.Context.AddString($"[FiresRPGmaker] Full list saved to: {outputPath}");
            
            // Show first 20 in console
            int shown = 0;
            foreach (var shaderName in results.Take(20))
            {
                var shader = Shader.Find(shaderName);
                string status = shader != null && shader.isSupported ? "" : " [NOT SUPPORTED]";
                args.Context.AddString($"  {shaderName}{status}");
                shown++;
            }
            
            if (results.Count > 20)
            {
                args.Context.AddString($"  ... and {results.Count - 20} more (see file for full list)");
            }
            
            args.Context.AddString("[FiresRPGmaker] Use: listshaders <filter> to search");
        }

        /// <summary>
        /// Dumps all environment definitions with complete details to a text file.
        /// </summary>
        private static void DumpAllEnvironments(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Dumping all environment definitions...");

            if (EnvMan.instance == null)
            {
                args.Context.AddString("[FiresRPGmaker] ERROR: EnvMan not available - are you in a world?");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM COMPLETE ENVIRONMENT REFERENCE");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Section 1: Summary of all environments
            sb.AppendLine("=== ENVIRONMENT SUMMARY ===");
            sb.AppendLine();
            sb.AppendLine("Environment Name                      | Flags");
            sb.AppendLine("--------------------------------------|---------------------------");
            
            var environments = EnvMan.instance.m_environments;
            if (environments == null || environments.Count == 0)
            {
                sb.AppendLine("  (no environments found)");
            }
            else
            {
                foreach (var env in environments)
                {
                    string flags = "";
                    if (env.m_alwaysDark) flags += "[DARK]";
                    if (env.m_isWet) flags += "[WET]";
                    if (env.m_isFreezing) flags += "[FREEZE]";
                    if (env.m_isFreezingAtNight) flags += "[FREEZE@NIGHT]";
                    if (env.m_isCold) flags += "[COLD]";
                    if (env.m_isColdAtNight) flags += "[COLD@NIGHT]";
                    
                    string name = env.m_name.PadRight(38);
                    sb.AppendLine($"{name}| {flags}");
                }
            }
            sb.AppendLine();

            // Section 2: Detailed environment data
            sb.AppendLine("=== DETAILED ENVIRONMENT DATA ===");
            sb.AppendLine();

            foreach (var env in environments)
            {
                sb.AppendLine($"--- {env.m_name} ---");
                sb.AppendLine();
                
                // Basic flags
                sb.AppendLine("  [Flags]");
                sb.AppendLine($"    Is Wet: {env.m_isWet}");
                sb.AppendLine($"    Is Freezing: {env.m_isFreezing}");
                sb.AppendLine($"    Is Freezing At Night: {env.m_isFreezingAtNight}");
                sb.AppendLine($"    Is Cold: {env.m_isCold}");
                sb.AppendLine($"    Is Cold At Night: {env.m_isColdAtNight}");
                sb.AppendLine($"    Always Dark: {env.m_alwaysDark}");
                sb.AppendLine();

                // Ambient colors
                sb.AppendLine("  [Ambient Colors]");
                sb.AppendLine($"    Night: {FormatColor(env.m_ambColorNight)}");
                sb.AppendLine($"    Day:   {FormatColor(env.m_ambColorDay)}");
                sb.AppendLine();

                // Fog colors
                sb.AppendLine("  [Fog Colors]");
                sb.AppendLine($"    Night:   {FormatColor(env.m_fogColorNight)}");
                sb.AppendLine($"    Morning: {FormatColor(env.m_fogColorMorning)}");
                sb.AppendLine($"    Day:     {FormatColor(env.m_fogColorDay)}");
                sb.AppendLine($"    Evening: {FormatColor(env.m_fogColorEvening)}");
                sb.AppendLine();

                // Fog density
                sb.AppendLine("  [Fog Density]");
                sb.AppendLine($"    Night:   {env.m_fogDensityNight:F4}");
                sb.AppendLine($"    Morning: {env.m_fogDensityMorning:F4}");
                sb.AppendLine($"    Day:     {env.m_fogDensityDay:F4}");
                sb.AppendLine($"    Evening: {env.m_fogDensityEvening:F4}");
                sb.AppendLine();

                // Sun colors
                sb.AppendLine("  [Sun Colors]");
                sb.AppendLine($"    Night:   {FormatColor(env.m_sunColorNight)}");
                sb.AppendLine($"    Morning: {FormatColor(env.m_sunColorMorning)}");
                sb.AppendLine($"    Day:     {FormatColor(env.m_sunColorDay)}");
                sb.AppendLine($"    Evening: {FormatColor(env.m_sunColorEvening)}");
                sb.AppendLine();

                // Light intensity
                sb.AppendLine("  [Light Intensity]");
                sb.AppendLine($"    Day:   {env.m_lightIntensityDay:F3}");
                sb.AppendLine($"    Night: {env.m_lightIntensityNight:F3}");
                sb.AppendLine($"    Sun Angle: {env.m_sunAngle:F1}");
                sb.AppendLine();

                // Wind
                sb.AppendLine("  [Wind]");
                sb.AppendLine($"    Min: {env.m_windMin:F2}");
                sb.AppendLine($"    Max: {env.m_windMax:F2}");
                sb.AppendLine();

                // Effects
                sb.AppendLine("  [Effects]");
                sb.AppendLine($"    Environment Object: {(env.m_envObject != null ? env.m_envObject.name : "None")}");
                sb.AppendLine($"    Particle Systems: {(env.m_psystems != null ? env.m_psystems.Length.ToString() : "0")}");
                sb.AppendLine($"    Particles Outside Only: {env.m_psystemsOutsideOnly}");
                sb.AppendLine($"    Rain Cloud Alpha: {env.m_rainCloudAlpha:F2}");
                sb.AppendLine();

                // Audio
                sb.AppendLine("  [Audio]");
                sb.AppendLine($"    Ambient Loop: {(env.m_ambientLoop != null ? env.m_ambientLoop.name : "None")}");
                sb.AppendLine($"    Ambient Volume: {env.m_ambientVol:F2}");
                sb.AppendLine();
                sb.AppendLine();
            }

            // Section 3: Code snippets for EnvironmentBox
            sb.AppendLine("=== CODE REFERENCE: Environment Names Array ===");
            sb.AppendLine();
            sb.AppendLine("// Copy this into EnvironmentBoxController.EnvironmentNames:");
            sb.AppendLine("public static readonly string[] EnvironmentNames = {");
            sb.AppendLine("    \"\",  // No override (use world environment)");
            foreach (var env in environments)
            {
                sb.AppendLine($"    \"{env.m_name}\",");
            }
            sb.AppendLine("};");
            sb.AppendLine();

            // Section 4: Wind intensity reference
            sb.AppendLine("=== WIND INTENSITY REFERENCE ===");
            sb.AppendLine();
            sb.AppendLine("Wind values from environments for reference:");
            sb.AppendLine();
            
            var windGroups = environments
                .GroupBy(e => $"{e.m_windMin:F2}-{e.m_windMax:F2}")
                .OrderBy(g => float.Parse(g.Key.Split('-')[0]));
            
            foreach (var group in windGroups)
            {
                sb.AppendLine($"  Wind {group.Key}:");
                foreach (var env in group)
                {
                    sb.AppendLine($"    - {env.m_name}");
                }
                sb.AppendLine();
            }

            // Section 5: Categorized environments
            sb.AppendLine("=== ENVIRONMENTS BY CATEGORY ===");
            sb.AppendLine();

            sb.AppendLine("  [Clear Weather]");
            foreach (var env in environments.Where(e => !e.m_isWet && !e.m_alwaysDark && e.m_fogDensityDay < 0.02f))
            {
                sb.AppendLine($"    - {env.m_name}");
            }
            sb.AppendLine();

            sb.AppendLine("  [Rainy/Wet]");
            foreach (var env in environments.Where(e => e.m_isWet))
            {
                sb.AppendLine($"    - {env.m_name}");
            }
            sb.AppendLine();

            sb.AppendLine("  [Dark/Indoor]");
            foreach (var env in environments.Where(e => e.m_alwaysDark))
            {
                sb.AppendLine($"    - {env.m_name}");
            }
            sb.AppendLine();

            sb.AppendLine("  [Cold/Mountain]");
            foreach (var env in environments.Where(e => e.m_isFreezing || e.m_isCold))
            {
                sb.AppendLine($"    - {env.m_name}");
            }
            sb.AppendLine();

            sb.AppendLine("  [Misty/Foggy]");
            foreach (var env in environments.Where(e => e.m_fogDensityDay >= 0.02f && !e.m_alwaysDark && !e.m_isWet))
            {
                sb.AppendLine($"    - {env.m_name}");
            }
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "EnvironmentReference.txt");
            
            File.WriteAllText(outputPath, sb.ToString());
            
            args.Context.AddString($"[FiresRPGmaker] Environment reference saved to: {outputPath}");
            args.Context.AddString($"[FiresRPGmaker] Found {environments.Count} environments");
            Debug.Log($"[FiresRPGmaker] Environment reference saved to: {outputPath}");
        }

        /// <summary>
        /// Saves the last push timestamp to disk
        /// </summary>
        private static void SaveLastPushTime(DateTime time)
        {
            try
            {
                _lastPushTime = time;
                var dir = Path.GetDirectoryName(_lastPushTimePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_lastPushTimePath, time.Ticks.ToString());
            }
            catch { }
        }

        private static void SavePushManifest(Dictionary<string, string> hashes)
        {
            try
            {
                _lastPushHashes = new Dictionary<string, string>(hashes, StringComparer.OrdinalIgnoreCase);
                var dir = Path.GetDirectoryName(_lastPushManifestPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var lines = new List<string>();
                foreach (var kvp in hashes)
                    lines.Add(kvp.Key + "|" + kvp.Value);
                File.WriteAllLines(_lastPushManifestPath, lines.ToArray());
            }
            catch { }
        }

        /// <summary>
        /// Builds a manifest from all existing config files on disk.
        /// Called once when no .lastpush.manifest exists, so that the first pushcfgs
        /// doesn't re-send everything the server already synced to us.
        /// </summary>
        private static void BootstrapManifestFromExistingFiles()
        {
            var baseDir = Path.Combine(Paths.ConfigPath, "FiresRPGmaker");
            var folders = new[]
            {
                ("Quests", "*.cfg", false),
                ("QuestProfiles", "*.cfg", false),
                ("Territories", "*.cfg", true),
                ("ServerInfos", "*.cfg", false),
                ("Dialogues", "*.cfg", true),
                ("Traders", "*.cfg", false),
            };

            _lastPushHashes.Clear();
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                foreach (var (folder, pattern, recursive) in folders)
                {
                    var dir = Path.Combine(baseDir, folder);
                    if (!Directory.Exists(dir)) continue;
                    var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    string baseFull = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                    foreach (var f in Directory.GetFiles(dir, pattern, searchOpt))
                    {
                        try
                        {
                            string rel;
                            if (recursive)
                            {
                                var full = Path.GetFullPath(f);
                                rel = full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase)
                                    ? full.Substring(baseFull.Length + 1) : Path.GetFileName(f);
                            }
                            else
                            {
                                rel = Path.GetFileName(f);
                            }

                            var content = File.ReadAllText(f);
                            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                            var hash = md5.ComputeHash(bytes);
                            var sb = new System.Text.StringBuilder(hash.Length * 2);
                            foreach (var b in hash) sb.Append(b.ToString("x2"));
                            _lastPushHashes[folder + "/" + rel] = sb.ToString();
                        }
                        catch { }
                    }
                }
            }

            // Persist so we don't bootstrap again next session
            SavePushManifest(_lastPushHashes);
            Debug.Log($"[FiresRPGmaker] Bootstrapped push manifest from {_lastPushHashes.Count} existing config files");
        }

        /// <summary>
        /// Sends config files to the server with real time delays between RPCs to avoid
        /// flooding the Steam connection buffer and causing a disconnect.
        /// Sends 1 file, then waits 200ms before the next to give the network time to flush.
        /// </summary>
        private static System.Collections.IEnumerator SendFilesWithDelay(
            List<(string folder, string name, string content, DateTime modified)> files,
            Terminal terminal)
        {
            int sent = 0;
            int failed = 0;

            for (int i = 0; i < files.Count; i++)
            {
                var entry = files[i];
                try
                {
                    if (ZRoutedRpc.instance == null) break;
                    var pkg = new ZPackage();
                    pkg.Write(entry.folder);
                    pkg.Write(entry.name);
                    pkg.Write(entry.content ?? string.Empty);
                    var rpcName = string.Equals(entry.folder, "Territories", StringComparison.OrdinalIgnoreCase)
                        ? "FiresRPGmaker_Territory_FileUpdate"
                        : "FiresRPGmaker_SyncFiles";
                    ZRoutedRpc.instance.InvokeRoutedRPC(0L, rpcName, pkg);
                    sent++;
                }
                catch
                {
                    failed++;
                }

                // Wait 200ms between each file to let the server process + network flush
                yield return new WaitForSeconds(0.2f);
            }

            // Final status
            string msg = $"[FiresRPGmaker] Push complete: {sent} file(s) sent";
            if (failed > 0) msg += $", {failed} failed";
            try { terminal?.AddString(msg); } catch { }
            Debug.Log(msg);
        }

        /// <summary>
        /// Dumps all biome visual information for snow/lava overlay implementation.
        /// </summary>
        private static void DumpBiomeVisualsReference(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Dumping biome visual reference...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM BIOME VISUAL REFERENCE");
            sb.AppendLine("For Snow/Lava/Terrain Overlay Implementation");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Section 1: Biome Vertex Colors
            sb.AppendLine("=== BIOME VERTEX COLORS (from Heightmap.GetBiomeColor) ===");
            sb.AppendLine();
            sb.AppendLine("These colors are set on terrain mesh vertices to control texture blending:");
            sb.AppendLine();
            sb.AppendLine("| Biome        | Color32 (RGBA)       | Hex        | Channel Used     |");
            sb.AppendLine("|--------------|----------------------|------------|------------------|");
            
            foreach (Heightmap.Biome biome in Enum.GetValues(typeof(Heightmap.Biome)))
            {
                if (biome == Heightmap.Biome.None) continue;
                
                var color = Heightmap.GetBiomeColor(biome);
                string hex = $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}";
                string channel = GetDominantChannel(color);
                sb.AppendLine($"| {biome,-12} | ({color.r,3}, {color.g,3}, {color.b,3}, {color.a,3}) | {hex,-10} | {channel,-16} |");
            }
            sb.AppendLine();

            // Section 2: Code for Setting Biome Colors
            sb.AppendLine("=== CODE: Setting Biome Colors on Terrain ===");
            sb.AppendLine();
            sb.AppendLine(@"
// To force snow (Mountain/DeepNorth) on terrain vertices:
Color32 snowColor = Heightmap.GetBiomeColor(Heightmap.Biome.Mountain);
// snowColor = (0, 255, 0, 0) - Green channel triggers snow texture

// To force Ashlands lava/char texture:
Color32 ashlandsColor = Heightmap.GetBiomeColor(Heightmap.Biome.AshLands);
// ashlandsColor = (255, 0, 0, 255) - Red + Alpha channels

// Apply to mesh vertices:
List<Color32> tempColors = new List<Color32>();
for (int i = 0; i < vertexCount; i++)
{
    if (IsInsideOverrideZone(vertexPosition))
        tempColors.Add(snowColor); // or ashlandsColor
    else
        tempColors.Add(originalColor);
}
renderMesh.SetColors(tempColors);
");
            sb.AppendLine();

            // Section 3: Global Shader Properties for Snow
            sb.AppendLine("=== GLOBAL SHADER PROPERTIES FOR SNOW ===");
            sb.AppendLine();
            sb.AppendLine("Current values:");
            
            string[] snowProps = { "_GlobalSnowAmount", "_SnowAmount", "_AddSnow", "_SnowHeight", "_SnowBlending" };
            foreach (var prop in snowProps)
            {
                try
                {
                    float val = Shader.GetGlobalFloat(prop);
                    sb.AppendLine($"  {prop}: {val:F4}");
                }
                catch
                {
                    sb.AppendLine($"  {prop}: [NOT SET]");
                }
            }
            sb.AppendLine();
            
            sb.AppendLine("To force snow on building pieces:");
            sb.AppendLine(@"
Shader.SetGlobalFloat(""_GlobalSnowAmount"", 1f);
Shader.SetGlobalFloat(""_SnowAmount"", 1f);
Shader.SetGlobalFloat(""_AddSnow"", 1f);  // CRITICAL: Bypasses altitude check for pieces
");
            sb.AppendLine();

            // Section 4: Heightmap Corner Biomes
            sb.AppendLine("=== HEIGHTMAP CORNER BIOMES (via Reflection) ===");
            sb.AppendLine();
            
            Heightmap nearbyHeightmap = null;
            if (Player.m_localPlayer != null)
            {
                nearbyHeightmap = Heightmap.FindHeightmap(Player.m_localPlayer.transform.position);
            }
            
            if (nearbyHeightmap != null)
            {
                sb.AppendLine($"Heightmap at player position: {nearbyHeightmap.transform.position}");
                
                try
                {
                    var cornerBiomesField = typeof(Heightmap).GetField("m_cornerBiomes", 
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (cornerBiomesField != null)
                    {
                        var cornerBiomes = cornerBiomesField.GetValue(nearbyHeightmap) as Heightmap.Biome[];
                        if (cornerBiomes != null && cornerBiomes.Length >= 4)
                        {
                            sb.AppendLine($"  Corner 0 (SW): {cornerBiomes[0]}");
                            sb.AppendLine($"  Corner 1 (SE): {cornerBiomes[1]}");
                            sb.AppendLine($"  Corner 2 (NW): {cornerBiomes[2]}");
                            sb.AppendLine($"  Corner 3 (NE): {cornerBiomes[3]}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"  ERROR reading corner biomes: {ex.Message}");
                }
            }
            else
            {
                sb.AppendLine("  [No heightmap found at player position]");
            }
            sb.AppendLine();

            // Section 5: Terrain Material Properties
            sb.AppendLine("=== TERRAIN MATERIAL TEXTURE SLOTS ===");
            sb.AppendLine();
            
            Material heightmapMaterial = null;
            if (nearbyHeightmap != null)
            {
                var renderer = nearbyHeightmap.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    heightmapMaterial = renderer.sharedMaterial;
                }
            }
            
            if (heightmapMaterial != null)
            {
                sb.AppendLine($"Shader: {heightmapMaterial.shader?.name ?? "NULL"}");
                sb.AppendLine();
                
                // List all texture properties
                string[] texProps = { "_MainTex", "_ClearedMaskTex", "_DiffuseTex0", "_DiffuseTex1", "_DiffuseTex2", 
                    "_DiffuseTex3", "_NormalTex", "_BumpMap", "_DetailTex", "_BlendTex" };
                
                foreach (var prop in texProps)
                {
                    if (heightmapMaterial.HasProperty(prop))
                    {
                        var tex = heightmapMaterial.GetTexture(prop);
                        if (tex != null)
                        {
                            sb.AppendLine($"  {prop}: {tex.name} ({tex.width}x{tex.height})");
                        }
                        else
                        {
                            sb.AppendLine($"  {prop}: NULL");
                        }
                    }
                }
            }
            else
            {
                sb.AppendLine("  [No terrain material found]");
            }
            sb.AppendLine();

            // Section 6: Paint Mask Colors
            sb.AppendLine("=== TERRAIN PAINT MASK COLORS ===");
            sb.AppendLine();
            sb.AppendLine("Used for cultivated/paved/dirt terrain states:");
            sb.AppendLine();
            sb.AppendLine($"  Dirt (Red channel):       {FormatColor(Heightmap.m_paintMaskDirt)}");
            sb.AppendLine($"  Cultivated (Green):       {FormatColor(Heightmap.m_paintMaskCultivated)}");
            sb.AppendLine($"  Paved (Blue):             {FormatColor(Heightmap.m_paintMaskPaved)}");
            sb.AppendLine($"  Nothing/Grass (Black):    {FormatColor(Heightmap.m_paintMaskNothing)}");
            sb.AppendLine();
            sb.AppendLine("The paint mask is stored in _ClearedMaskTex and sampled by the shader.");
            sb.AppendLine("Each pixel's RGBA determines the terrain paint type at that location.");
            sb.AppendLine();

            // Section 7: Key Reflection Fields
            sb.AppendLine("=== KEY REFLECTION FIELDS FOR HEIGHTMAP ===");
            sb.AppendLine();
            sb.AppendLine(@"
// Cache these fields at startup:
const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

_cornerBiomesField = typeof(Heightmap).GetField(""m_cornerBiomes"", instanceFlags);
_renderMeshField = typeof(Heightmap).GetField(""m_renderMesh"", instanceFlags);
_tempColorsField = typeof(Heightmap).GetField(""s_tempColors"", staticFlags);
_isDistantLodField = typeof(Heightmap).GetField(""m_isDistantLod"", instanceFlags);
_rebuildRenderMeshMethod = typeof(Heightmap).GetMethod(""RebuildRenderMesh"", instanceFlags);
");
            sb.AppendLine();

            // Section 8: Implementation Strategy
            sb.AppendLine("=== IMPLEMENTATION STRATEGY FOR SNOW/LAVA OVERLAYS ===");
            sb.AppendLine();
            sb.AppendLine(@"
OPTION A: Vertex Color Override (Recommended for terrain)
---------------------------------------------------------
1. Patch Heightmap.RebuildRenderMesh (PREFIX) to set corner biomes
2. Patch Heightmap.RebuildRenderMesh (POSTFIX) to set per-vertex colors
3. Set global shader props for building pieces

OPTION B: Direct Paint Mask Application (Alternative)
----------------------------------------------------
1. Get the _ClearedMaskTex from heightmap material
2. Paint biome-specific colors onto it directly
3. Re-apply the texture to trigger shader update
Note: This is what vanilla TerrainComp.PaintCleared does

OPTION C: Raycast Paint Application (What you're considering)
------------------------------------------------------------
1. Raycast from env box to ground
2. Find heightmap at hit point
3. Get TerrainComp for that heightmap
4. Call PaintCleared with appropriate paint type
Pros: Uses vanilla painting system
Cons: Limited to existing paint types (dirt/cultivate/pave)
      Does NOT give snow/lava visuals - those need vertex colors

RECOMMENDATION:
---------------
For snow overlay: Use vertex colors (Heightmap.GetBiomeColor(Mountain))
For lava overlay: Use vertex colors (Heightmap.GetBiomeColor(AshLands))
For terrain paint: Use TerrainComp.PaintCleared (existing system)

The vertex color approach gives the ACTUAL biome textures (snow, char/ash)
while paint mask only gives dirt/cultivated/paved appearances.
");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "BiomeVisualsReference.txt");
            
            File.WriteAllText(outputPath, sb.ToString());
            
            args.Context.AddString($"[FiresRPGmaker] Biome visuals reference saved to: {outputPath}");
            Debug.Log($"[FiresRPGmaker] Biome visuals reference saved to: {outputPath}");
        }

        /// <summary>
        /// Gets the dominant color channel(s) for a biome color.
        /// </summary>
        private static string GetDominantChannel(Color32 c)
        {
            var channels = new List<string>();
            if (c.r > 127) channels.Add("R");
            if (c.g > 127) channels.Add("G");
            if (c.b > 127) channels.Add("B");
            if (c.a > 127) channels.Add("A");
            return channels.Count > 0 ? string.Join("+", channels) : "None";
        }

        /// <summary>
        /// Exports terrain textures to PNG files for reference.
        /// </summary>
        private static void DumpTerrainTexturesToFiles(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Exporting terrain textures...");

            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug, "TerrainTextures");
            Directory.CreateDirectory(outputDir);

            Heightmap nearbyHeightmap = null;
            if (Player.m_localPlayer != null)
            {
                nearbyHeightmap = Heightmap.FindHeightmap(Player.m_localPlayer.transform.position);
            }

            if (nearbyHeightmap == null)
            {
                var heightmaps = UnityEngine.Object.FindObjectsByType<Heightmap>(UnityEngine.FindObjectsSortMode.None);
                if (heightmaps.Length > 0) nearbyHeightmap = heightmaps[0];
            }

            if (nearbyHeightmap == null)
            {
                args.Context.AddString("[FiresRPGmaker] ERROR: No heightmap found");
                return;
            }

            var renderer = nearbyHeightmap.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterial == null)
            {
                args.Context.AddString("[FiresRPGmaker] ERROR: No terrain material found");
                return;
            }

            Material mat = renderer.sharedMaterial;
            int exported = 0;

            // Try to export common texture properties
            string[] texProps = { "_MainTex", "_ClearedMaskTex", "_DiffuseTex0", "_DiffuseTex1", "_DiffuseTex2", 
                "_DiffuseTex3", "_NormalTex", "_BumpMap", "_DetailTex", "_BlendTex", 
                "_Tex0", "_Tex1", "_Tex2", "_Tex3" };

            foreach (var prop in texProps)
            {
                if (!mat.HasProperty(prop)) continue;
                
                var tex = mat.GetTexture(prop);
                if (tex == null) continue;

                try
                {
                    // Try to read texture data
                    Texture2D tex2D = null;
                    
                    if (tex is Texture2D t2d)
                    {
                        // Need to make it readable
                        tex2D = MakeTextureReadable(t2d);
                    }
                    else if (tex is RenderTexture rt)
                    {
                        tex2D = RenderTextureToTexture2D(rt);
                    }

                    if (tex2D != null)
                    {
                        byte[] pngData = tex2D.EncodeToPNG();
                        string filename = $"{prop.TrimStart('_')}_{tex.name}.png";
                        string path = Path.Combine(outputDir, filename);
                        File.WriteAllBytes(path, pngData);
                        
                        args.Context.AddString($"  Exported: {filename} ({tex2D.width}x{tex2D.height})");
                        exported++;
                        
                        // Clean up temporary texture
                        if (tex2D != tex)
                        {
                            UnityEngine.Object.Destroy(tex2D);
                        }
                    }
                    else
                    {
                        args.Context.AddString($"  Skipped: {prop} ({tex.name}) - not readable");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"  Failed: {prop} - {ex.Message}");
                }
            }

            // Also create a reference document
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Terrain Textures exported from: {mat.name}");
            sb.AppendLine($"Shader: {mat.shader?.name ?? "NULL"}");
            sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("Exported files:");
            
            foreach (var file in Directory.GetFiles(outputDir, "*.png"))
            {
                sb.AppendLine($"  - {Path.GetFileName(file)}");
            }
            
            File.WriteAllText(Path.Combine(outputDir, "_README.txt"), sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Exported {exported} textures to: {outputDir}");
        }

        /// <summary>
        /// Makes a texture readable by creating a copy.
        /// </summary>
        private static Texture2D MakeTextureReadable(Texture2D source)
        {
            if (source.isReadable) return source;

            // Create a temporary RenderTexture
            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();
            
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            
            return readable;
        }

        /// <summary>
        /// Converts a RenderTexture to Texture2D.
        /// </summary>
        private static Texture2D RenderTextureToTexture2D(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            
            RenderTexture.active = previous;
            return tex;
        }

        /// <summary>
        /// Dumps all global shader properties to a file.
        /// </summary>
        private static void DumpGlobalShaderProperties(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Dumping global shader properties...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM GLOBAL SHADER PROPERTIES");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Float properties
            sb.AppendLine("=== FLOAT PROPERTIES ===");
            sb.AppendLine();
            string[] floatProps = {
                "_GlobalSnowAmount", "_SnowAmount", "_AddSnow", "_SnowHeight", "_SnowBlending",
                "_GlobalTime", "_GlobalWind", "_WaterLevel", "_WaterTime",
                "_FogDensity", "_FogStart", "_FogEnd",
                "_LavaAmount", "_AshAmount", "_CharAmount", "_AshlandsGlow",
                "_MistAmount", "_MistDensity"
            };
            foreach (var prop in floatProps)
            {
                try
                {
                    int propId = Shader.PropertyToID(prop);
                    float val = Shader.GetGlobalFloat(propId);
                    sb.AppendLine($"  {prop}: {val:F4}");
                }
                catch
                {
                    sb.AppendLine($"  {prop}: [ERROR]");
                }
            }
            sb.AppendLine();

            // Vector properties  
            sb.AppendLine("=== VECTOR PROPERTIES ===");
            sb.AppendLine();
            string[] vectorProps = {
                "_GlobalWindDir", "_SunDir", "_WorldSpaceCameraPos", "_ZBufferParams"
            };
            foreach (var prop in vectorProps)
            {
                try
                {
                    int propId = Shader.PropertyToID(prop);
                    var vec = Shader.GetGlobalVector(propId);
                    sb.AppendLine($"  {prop}: ({vec.x:F3}, {vec.y:F3}, {vec.z:F3}, {vec.w:F3})");
                }
                catch
                {
                    sb.AppendLine($"  {prop}: [ERROR]");
                }
            }
            sb.AppendLine();

            // Color properties
            sb.AppendLine("=== COLOR PROPERTIES ===");
            sb.AppendLine();
            string[] colorProps = {
                "_FogColor", "_SunColor", "_AmbientColor", "_ReflectionColor", "_MistColor"
            };
            foreach (var prop in colorProps)
            {
                try
                {
                    int propId = Shader.PropertyToID(prop);
                    var col = Shader.GetGlobalColor(propId);
                    sb.AppendLine($"  {prop}: RGBA({col.r:F3}, {col.g:F3}, {col.b:F3}, {col.a:F3})");
                }
                catch
                {
                    sb.AppendLine($"  {prop}: [ERROR]");
                }
            }
            sb.AppendLine();

            // Section 2: Current EnvMan state
            sb.AppendLine("=== ENVMAN CURRENT STATE ===");
            sb.AppendLine();
            if (EnvMan.instance != null)
            {
                var currentEnv = EnvMan.instance.GetCurrentEnvironment();
                if (currentEnv != null)
                {
                    sb.AppendLine($"Current Environment: {currentEnv.m_name}");
                    sb.AppendLine($"Is Wet: {currentEnv.m_isWet}");
                    sb.AppendLine($"Is Freezing: {currentEnv.m_isFreezing}");
                    sb.AppendLine($"Wind: {currentEnv.m_windMin:F2} - {currentEnv.m_windMax:F2}");
                    sb.AppendLine($"Fog Density Day: {currentEnv.m_fogDensityDay:F4}");
                    sb.AppendLine($"Fog Color Day: {FormatColor(currentEnv.m_fogColorDay)}");
                    sb.AppendLine($"Ambient Color Day: {FormatColor(currentEnv.m_ambColorDay)}");
                    sb.AppendLine($"Sun Color Day: {FormatColor(currentEnv.m_sunColorDay)}");
                }
                
                // Get current biome
                if (Player.m_localPlayer != null)
                {
                    var biome = WorldGenerator.instance?.GetBiome(Player.m_localPlayer.transform.position) ?? Heightmap.Biome.Meadows;
                    sb.AppendLine($"Player Biome: {biome}");
                    sb.AppendLine($"Player Y: {Player.m_localPlayer.transform.position.y:F1}");
                }
            }
            sb.AppendLine();

            // Section 3: RenderSettings
            sb.AppendLine("=== RENDER SETTINGS (Current) ===");
            sb.AppendLine();
            sb.AppendLine($"  Fog Enabled: {RenderSettings.fog}");
            sb.AppendLine($"  Fog Color: {FormatColor(RenderSettings.fogColor)}");
            sb.AppendLine($"  Fog Density: {RenderSettings.fogDensity:F4}");
            sb.AppendLine($"  Fog Mode: {RenderSettings.fogMode}");
            sb.AppendLine($"  Ambient Mode: {RenderSettings.ambientMode}");
            sb.AppendLine($"  Ambient Light: {FormatColor(RenderSettings.ambientLight)}");
            sb.AppendLine($"  Ambient Intensity: {RenderSettings.ambientIntensity:F3}");
            sb.AppendLine();

            // Section 4: Recommended values
            sb.AppendLine("=== IMPLEMENTATION NOTES ===");
            sb.AppendLine();
            sb.AppendLine(@"
TERRAIN BIOME VISUALS:
- Controlled by VERTEX COLORS on the terrain mesh
- Does NOT affect terrain height (that's WorldGenerator.GetBiome)
- Use Heightmap.GetBiomeColor(biome) to get the right color
- Mountain = Green channel (0, 255, 0, 0) = Snow texture
- AshLands = Red+Alpha (255, 0, 0, 255) = Char/Lava texture

BUILDING PIECE SNOW:
- Controlled by global shader: _AddSnow
- Set to 1.0 to force snow on pieces regardless of altitude
- The piece shaders read this and apply snow tinting

IMPLEMENTATION FOR ENV BOX:
1. In RebuildRenderMesh POSTFIX, set vertex colors for terrain
2. Set _AddSnow = 1.0 for building piece snow
3. Corner biomes only affect texture SELECTION, not height
4. Height is ONLY affected by WorldGenerator.GetBiome (which we don't patch)
");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "GlobalShaderProperties.txt");
            
            File.WriteAllText(outputPath, sb.ToString());
            
            args.Context.AddString($"[FiresRPGmaker] Global shader properties saved to: {outputPath}");
        }

        /// <summary>
        /// Tests forcing a biome overlay at the player's position.
        /// </summary>
        private static void TestBiomeOverlay(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("[FiresRPGmaker] No local player");
                return;
            }

            string biomeName = (args.Args != null && args.Args.Length > 1) 
                ? args.Args[1].Trim() 
                : "";

            if (string.IsNullOrEmpty(biomeName))
            {
                args.Context.AddString("[FiresRPGmaker] Usage: testbiomeoverlay <biome>");
                args.Context.AddString("[FiresRPGmaker] Biomes: Mountain, DeepNorth, AshLands, Swamp, Mistlands, Plains, BlackForest, Meadows, clear");
                return;
            }

            if (biomeName.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                // Clear all forced overlays
                Shader.SetGlobalFloat("_GlobalSnowAmount", 0f);
                Shader.SetGlobalFloat("_SnowAmount", 0f);
                Shader.SetGlobalFloat("_AddSnow", 0f);
                args.Context.AddString("[FiresRPGmaker] Cleared shader overrides");
                
                // Force heightmap refresh
                var hm = Heightmap.FindHeightmap(Player.m_localPlayer.transform.position);
                if (hm != null)
                {
                    hm.Regenerate();
                    args.Context.AddString("[FiresRPGmaker] Heightmap regenerated");
                }
                return;
            }

            // Parse biome
            Heightmap.Biome targetBiome;
            if (!Enum.TryParse(biomeName, true, out targetBiome))
            {
                args.Context.AddString($"[FiresRPGmaker] Unknown biome: {biomeName}");
                return;
            }

            args.Context.AddString($"[FiresRPGmaker] Testing biome overlay: {targetBiome}");

            // Get biome color
            var biomeColor = Heightmap.GetBiomeColor(targetBiome);
            args.Context.AddString($"  Biome Color: ({biomeColor.r}, {biomeColor.g}, {biomeColor.b}, {biomeColor.a})");

            // Set shader globals for snow biomes
            if (targetBiome == Heightmap.Biome.Mountain || targetBiome == Heightmap.Biome.DeepNorth)
            {
                Shader.SetGlobalFloat("_GlobalSnowAmount", 1f);
                Shader.SetGlobalFloat("_SnowAmount", 1f);
                Shader.SetGlobalFloat("_AddSnow", 1f);
                args.Context.AddString("  Set snow shader globals to 1.0");
            }

            // Find and modify nearby heightmap
            var heightmap = Heightmap.FindHeightmap(Player.m_localPlayer.transform.position);
            if (heightmap == null)
            {
                args.Context.AddString("[FiresRPGmaker] No heightmap at player position");
                return;
            }

            args.Context.AddString($"  Found heightmap at: {heightmap.transform.position}");

            // Try to modify corner biomes
            try
            {
                var cornerBiomesField = typeof(Heightmap).GetField("m_cornerBiomes", 
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                
                if (cornerBiomesField != null)
                {
                    var cornerBiomes = cornerBiomesField.GetValue(heightmap) as Heightmap.Biome[];
                    if (cornerBiomes != null && cornerBiomes.Length >= 4)
                    {
                        args.Context.AddString($"  Original corners: [{cornerBiomes[0]}, {cornerBiomes[1]}, {cornerBiomes[2]}, {cornerBiomes[3]}]");
                        
                        // Set all corners to target biome
                        for (int i = 0; i < 4; i++)
                        {
                            cornerBiomes[i] = targetBiome;
                        }
                        cornerBiomesField.SetValue(heightmap, cornerBiomes);
                        args.Context.AddString($"  Set all corners to: {targetBiome}");
                        
                        // Force rebuild
                        var rebuildMethod = typeof(Heightmap).GetMethod("RebuildRenderMesh", 
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (rebuildMethod != null)
                        {
                            rebuildMethod.Invoke(heightmap, null);
                            args.Context.AddString("  Called RebuildRenderMesh");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                args.Context.AddString($"  ERROR modifying heightmap: {ex.Message}");
            }

            args.Context.AddString("[FiresRPGmaker] Test complete - check terrain visuals");
            args.Context.AddString("[FiresRPGmaker] Use 'testbiomeoverlay clear' to reset");
        }

        /// <summary>
        /// Dumps all prefabs with MineRock or MineRock5 components to a file.
        /// </summary>
        private static void DumpMineRockPrefabs(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Scanning for MineRock/MineRock5 prefabs...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM MINEROCK PREFAB REFERENCE");
            sb.AppendLine("For Companion Resource Gathering Implementation");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var mineRockPrefabs = new List<(string name, int tier, string type, float health)>();
            var mineRock5Prefabs = new List<(string name, int tier, string type, float health)>();

            // Scan ZNetScene for all prefabs
            if (ZNetScene.instance != null)
            {
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;

                    var mineRock = prefab.GetComponent<MineRock>();
                    if (mineRock != null)
                    {
                        mineRockPrefabs.Add((prefab.name, mineRock.m_minToolTier, "MineRock", mineRock.m_health));
                    }

                    var mineRock5 = prefab.GetComponent<MineRock5>();
                    if (mineRock5 != null)
                    {
                        mineRock5Prefabs.Add((prefab.name, mineRock5.m_minToolTier, "MineRock5", mineRock5.m_health));
                    }
                }
            }

            // Section 1: MineRock prefabs
            sb.AppendLine("=== MINEROCK PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                          | Tool Tier | Health   |");
            sb.AppendLine("|--------------------------------------|-----------|----------|");
            foreach (var (name, tier, type, health) in mineRockPrefabs.OrderBy(p => p.tier).ThenBy(p => p.name))
            {
                sb.AppendLine($"| {name,-36} | {tier,9} | {health,8:F0} |");
            }
            sb.AppendLine();

            // Section 2: MineRock5 prefabs
            sb.AppendLine("=== MINEROCK5 PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                          | Tool Tier | Health   |");
            sb.AppendLine("|--------------------------------------|-----------|----------|");
            foreach (var (name, tier, type, health) in mineRock5Prefabs.OrderBy(p => p.tier).ThenBy(p => p.name))
            {
                sb.AppendLine($"| {name,-36} | {tier,9} | {health,8:F0} |");
            }
            sb.AppendLine();

            // Section 3: Categorized by tier
            sb.AppendLine("=== CATEGORIZED BY TOOL TIER ===");
            sb.AppendLine();
            var allRocks = mineRockPrefabs.Concat(mineRock5Prefabs).ToList();
            foreach (var tierGroup in allRocks.GroupBy(p => p.tier).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [Tier {tierGroup.Key}] (requires pickaxe tier {tierGroup.Key}+)");
                foreach (var (name, tier, type, health) in tierGroup.OrderBy(p => p.name))
                {
                    sb.AppendLine($"    - {name} ({type}, HP:{health:F0})");
                }
                sb.AppendLine();
            }

            // Section 4: Code snippet for ore detection
            sb.AppendLine("=== CODE: ORE DETECTION NAMES ===");
            sb.AppendLine();
            sb.AppendLine("// Use these names for ore detection in FindNearbyOreDeposit:");
            sb.AppendLine("private static readonly HashSet<string> OreDepositNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("{");
            foreach (var (name, tier, type, health) in allRocks.OrderBy(p => p.name))
            {
                sb.AppendLine($"    \"{name}\",");
            }
            sb.AppendLine("};");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "MineRockPrefabs.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Found {mineRockPrefabs.Count} MineRock + {mineRock5Prefabs.Count} MineRock5 prefabs");
            args.Context.AddString($"[FiresRPGmaker] Reference saved to: {outputPath}");

            // Show first few in console
            int shown = 0;
            foreach (var (name, tier, type, health) in allRocks.OrderBy(p => p.name).Take(15))
            {
                args.Context.AddString($"  {name} (Tier {tier}, {type})");
                shown++;
            }
            if (allRocks.Count > 15)
            {
                args.Context.AddString($"  ... and {allRocks.Count - 15} more (see file for full list)");
            }
        }

        /// <summary>
        /// Dumps all prefabs with Pickable component to a file.
        /// </summary>
        private static void DumpPickablePrefabs(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Scanning for Pickable prefabs...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM PICKABLE PREFAB REFERENCE");
            sb.AppendLine("For Companion Resource Gathering Implementation");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var pickablePrefabs = new List<(string name, string itemDrop, int amount, float respawnTime)>();

            // Scan ZNetScene for all prefabs
            if (ZNetScene.instance != null)
            {
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;

                    var pickable = prefab.GetComponent<Pickable>();
                    if (pickable != null)
                    {
                        string itemName = pickable.m_itemPrefab?.name ?? "NULL";
                        pickablePrefabs.Add((prefab.name, itemName, pickable.m_amount, pickable.m_respawnTimeMinutes));
                    }
                }
            }

            // Section 1: All pickable prefabs
            sb.AppendLine("=== PICKABLE PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                          | Item Drop                | Amount | Respawn (min) |");
            sb.AppendLine("|--------------------------------------|--------------------------|--------|---------------|");
            foreach (var (name, itemDrop, amount, respawnTime) in pickablePrefabs.OrderBy(p => p.name))
            {
                sb.AppendLine($"| {name,-36} | {itemDrop,-24} | {amount,6} | {respawnTime,13:F0} |");
            }
            sb.AppendLine();

            // Section 2: Categorized by item type
            sb.AppendLine("=== CATEGORIZED BY ITEM DROP ===");
            sb.AppendLine();
            foreach (var itemGroup in pickablePrefabs.GroupBy(p => p.itemDrop).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [{itemGroup.Key}]");
                foreach (var (name, itemDrop, amount, respawnTime) in itemGroup.OrderBy(p => p.name))
                {
                    sb.AppendLine($"    - {name} (x{amount}, respawn: {respawnTime}min)");
                }
                sb.AppendLine();
            }

            // Section 3: Code snippet
            sb.AppendLine("=== CODE: PICKABLE PREFAB NAMES ===");
            sb.AppendLine();
            sb.AppendLine("// Use these names for pickable detection:");
            sb.AppendLine("private static readonly HashSet<string> PickablePrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("{");
            foreach (var (name, _, _, _) in pickablePrefabs.OrderBy(p => p.name))
            {
                sb.AppendLine($"    \"{name}\",");
            }
            sb.AppendLine("};");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "PickablePrefabs.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Found {pickablePrefabs.Count} Pickable prefabs");
            args.Context.AddString($"[FiresRPGmaker] Reference saved to: {outputPath}");

            // Show first few in console
            int shown = 0;
            foreach (var (name, itemDrop, amount, _) in pickablePrefabs.OrderBy(p => p.name).Take(15))
            {
                args.Context.AddString($"  {name} -> {itemDrop} x{amount}");
                shown++;
            }
            if (pickablePrefabs.Count > 15)
            {
                args.Context.AddString($"  ... and {pickablePrefabs.Count - 15} more (see file for full list)");
            }
        }

        /// <summary>
        /// Dumps all prefabs with TreeBase component to a file.
        /// </summary>
        private static void DumpTreeBasePrefabs(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Scanning for TreeBase prefabs...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM TREEBASE PREFAB REFERENCE");
            sb.AppendLine("For Companion Resource Gathering Implementation");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var treeBasePrefabs = new List<(string name, int tier, float health, string logPrefab)>();

            // Scan ZNetScene for all prefabs
            if (ZNetScene.instance != null)
            {
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;

                    var treeBase = prefab.GetComponent<TreeBase>();
                    if (treeBase != null)
                    {
                        string logName = treeBase.m_logPrefab?.name ?? "NULL";
                        treeBasePrefabs.Add((prefab.name, treeBase.m_minToolTier, treeBase.m_health, logName));
                    }
                }
            }

            // Section 1: All tree prefabs
            sb.AppendLine("=== TREEBASE PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                          | Tool Tier | Health   | Log Prefab               |");
            sb.AppendLine("|--------------------------------------|-----------|----------|--------------------------|");
            foreach (var (name, tier, health, logPrefab) in treeBasePrefabs.OrderBy(p => p.tier).ThenBy(p => p.name))
            {
                sb.AppendLine($"| {name,-36} | {tier,9} | {health,8:F0} | {logPrefab,-24} |");
            }
            sb.AppendLine();

            // Section 2: Categorized by tier
            sb.AppendLine("=== CATEGORIZED BY TOOL TIER ===");
            sb.AppendLine();
            foreach (var tierGroup in treeBasePrefabs.GroupBy(p => p.tier).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [Tier {tierGroup.Key}] (requires axe tier {tierGroup.Key}+)");
                foreach (var (name, tier, health, logPrefab) in tierGroup.OrderBy(p => p.name))
                {
                    sb.AppendLine($"    - {name} (HP:{health:F0}, log: {logPrefab})");
                }
                sb.AppendLine();
            }

            // Section 3: Code snippet
            sb.AppendLine("=== CODE: TREE PREFAB NAMES ===");
            sb.AppendLine();
            sb.AppendLine("// Use these names for tree detection:");
            sb.AppendLine("private static readonly HashSet<string> TreePrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("{");
            foreach (var (name, _, _, _) in treeBasePrefabs.OrderBy(p => p.name))
            {
                sb.AppendLine($"    \"{name}\",");
            }
            sb.AppendLine("};");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "TreeBasePrefabs.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Found {treeBasePrefabs.Count} TreeBase prefabs");
            args.Context.AddString($"[FiresRPGmaker] Reference saved to: {outputPath}");

            // Show first few in console
            int shown = 0;
            foreach (var (name, tier, health, _) in treeBasePrefabs.OrderBy(p => p.name).Take(15))
            {
                args.Context.AddString($"  {name} (Tier {tier}, HP:{health:F0})");
                shown++;
            }
            if (treeBasePrefabs.Count > 15)
            {
                args.Context.AddString($"  ... and {treeBasePrefabs.Count - 15} more (see file for full list)");
            }
        }

        /// <summary>
        /// Dumps all prefabs with TreeLog component to a file.
        /// </summary>
        private static void DumpTreeLogPrefabs(Terminal.ConsoleEventArgs args)
        {
            args.Context.AddString("[FiresRPGmaker] Scanning for TreeLog prefabs...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM TREELOG PREFAB REFERENCE");
            sb.AppendLine("For Companion Resource Gathering Implementation");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var treeLogPrefabs = new List<(string name, int tier, float health)>();

            // Scan ZNetScene for all prefabs
            if (ZNetScene.instance != null)
            {
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (prefab == null) continue;

                    var treeLog = prefab.GetComponent<TreeLog>();
                    if (treeLog != null)
                    {
                        treeLogPrefabs.Add((prefab.name, treeLog.m_minToolTier, treeLog.m_health));
                    }
                }
            }

            // Section 1: All log prefabs
            sb.AppendLine("=== TREELOG PREFABS ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                          | Tool Tier | Health   |");
            sb.AppendLine("|--------------------------------------|-----------|----------|");
            foreach (var (name, tier, health) in treeLogPrefabs.OrderBy(p => p.tier).ThenBy(p => p.name))
            {
                sb.AppendLine($"| {name,-36} | {tier,9} | {health,8:F0} |");
            }
            sb.AppendLine();

            // Section 2: Categorized by tier
            sb.AppendLine("=== CATEGORIZED BY TOOL TIER ===");
            sb.AppendLine();
            foreach (var tierGroup in treeLogPrefabs.GroupBy(p => p.tier).OrderBy(g => g.Key))
            {
                sb.AppendLine($"  [Tier {tierGroup.Key}] (requires axe tier {tierGroup.Key}+)");
                foreach (var (name, tier, health) in tierGroup.OrderBy(p => p.name))
                {
                    sb.AppendLine($"    - {name} (HP:{health:F0})");
                }
                sb.AppendLine();
            }

            // Section 3: Code snippet
            sb.AppendLine("=== CODE: LOG PREFAB NAMES ===");
            sb.AppendLine();
            sb.AppendLine("// Use these names for log detection:");
            sb.AppendLine("private static readonly HashSet<string> TreeLogPrefabNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)");
            sb.AppendLine("{");
            foreach (var (name, _, _) in treeLogPrefabs.OrderBy(p => p.name))
            {
                sb.AppendLine($"    \"{name}\",");
            }
            sb.AppendLine("};");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "TreeLogPrefabs.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Found {treeLogPrefabs.Count} TreeLog prefabs");
            args.Context.AddString($"[FiresRPGmaker] Reference saved to: {outputPath}");

            // Show first few in console
            int shown = 0;
            foreach (var (name, tier, health) in treeLogPrefabs.OrderBy(p => p.name).Take(15))
            {
                args.Context.AddString($"  {name} (Tier {tier}, HP:{health:F0})");
                shown++;
            }
            if (treeLogPrefabs.Count > 15)
            {
                args.Context.AddString($"  ... and {treeLogPrefabs.Count - 15} more (see file for full list)");
            }
        }

        /// <summary>
        /// Build a lightweight inventory snapshot from local player's inventory for server-side validation
        /// </summary>
        private static List<StoredBankItem> BuildInventorySnapshot(Player player)
        {
            var list = new List<StoredBankItem>();
    if (player == null) return list;

 try
            {
      var inv = player.GetInventory();
     if (inv == null) return list;

     // Try using Inventory.Save to get a serialized snapshot
    try
                {
         var pkg = new ZPackage();
        inv.Save(pkg);

    // Create a reader from the package
byte[] data = pkg.GetArray();
                 var reader = new ZPackage(data);

        int version = reader.ReadInt();
     int count = reader.ReadInt();

          for (int i = 0; i < count; i++)
              {
      try
       {
        string name = reader.ReadString();
        int stack = reader.ReadInt();
              float durability = reader.ReadSingle();
     reader.ReadVector2i(); // grid pos
      reader.ReadBool(); // equipped
    int quality = reader.ReadInt();
 reader.ReadInt(); // variant
                 reader.ReadLong(); // crafterID
    reader.ReadString(); // crafterName
        int customCount = reader.ReadInt();
        for (int c = 0; c < customCount; c++)
              {
   reader.ReadString();
        reader.ReadString();
      }
       try { reader.ReadInt(); } catch { } // worldLevel
             try { reader.ReadBool(); } catch { } // pickedUp

     if (string.IsNullOrEmpty(name)) continue;
       name = name.Replace("(Clone)", string.Empty).Trim();
          list.Add(new StoredBankItem("item", name, stack, quality, durability));
         }
  catch { break; }
        }

      if (list.Count > 0)
     {
        Debug.Log($"[FiresRPGmaker] Built snapshot from Inventory.Save: {list.Count} items");
     return list;
            }
     }
    catch (Exception ex)
      {
      Debug.Log($"[FiresRPGmaker] Inventory.Save path failed: {ex.Message}");
   }

           // Fallback: use GetAllItems
                try
    {
       var items = inv.GetAllItems();
     foreach (var item in items)
     {
       if (item == null) continue;
                 string prefab = item.m_dropPrefab?.name ?? item.m_shared?.m_name ?? "";
   if (string.IsNullOrEmpty(prefab)) continue;
   prefab = prefab.Replace("(Clone)", string.Empty).Trim();
   list.Add(new StoredBankItem("item", prefab, item.m_stack, item.m_quality, item.m_durability));
         }
          }
     catch (Exception ex)
 {
              Debug.Log($"[FiresRPGmaker] GetAllItems fallback failed: {ex.Message}");
 }

             Debug.Log($"[FiresRPGmaker] Inventory snapshot: {list.Count} entries");
            }
            catch (Exception ex)
   {
        Debug.Log($"[FiresRPGmaker] BuildInventorySnapshot failed: {ex.Message}");
            }

    return list;
        }

        #region Mock NPC for UI Testing

        // Temporary map pins for find companion
        private static List<Minimap.PinData> _temporaryPins = new List<Minimap.PinData>();

        /// <summary>
        /// Adds a temporary map pin that auto-removes after 60 seconds.
        /// </summary>
        private static void AddTemporaryMapPin(Vector3 position, string label)
        {
            try
            {
                if (Minimap.instance == null) return;

                var pin = Minimap.instance.AddPin(position, Minimap.PinType.Icon3, label, true, false);
                if (pin != null)
                {
                    _temporaryPins.Add(pin);
                    
                    // Start coroutine to remove after 60 seconds
                    if (Player.m_localPlayer != null)
                    {
                        Player.m_localPlayer.StartCoroutine(RemovePinAfterDelay(pin, 60f));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DebugTools] Failed to add map pin: {ex.Message}");
            }
        }

        private static System.Collections.IEnumerator RemovePinAfterDelay(Minimap.PinData pin, float delay)
        {
            yield return new WaitForSeconds(delay);
            
            try
            {
                if (Minimap.instance != null && pin != null)
                {
                    Minimap.instance.RemovePin(pin);
                    _temporaryPins.Remove(pin);
                }
            }
            catch { }
        }

 private static GameObject _mockNpcObject;

        /// <summary>
   /// Creates a temporary mock NPC controller for testing the UI without an actual NPC in the world.
        /// </summary>
        private static NpcController CreateMockNpcController()
        {
    try
      {
            if (_mockNpcObject != null)
       {
    var existingController = _mockNpcObject.GetComponent<NpcController>();
                if (existingController != null)
       return existingController;
   }

        _mockNpcObject = new GameObject("MockNPC_DebugTest");
    UnityEngine.Object.DontDestroyOnLoad(_mockNpcObject);
     _mockNpcObject.SetActive(false);

      var controller = _mockNpcObject.AddComponent<NpcController>();
controller.npcProfileName = "Test Merchant";
         controller.displayNameOverride = "Debug Test NPC";

                var dialogueProfiles = DialogueManager.GetAllProfileNames();
        if (dialogueProfiles != null && dialogueProfiles.Count > 0)
     {
      controller.dialogueProfile = dialogueProfiles[0];
      Debug.Log($"[DebugTools] Mock NPC using dialogue profile: {controller.dialogueProfile}");
     }
                else
             {
     controller.dialogueProfile = "test_dialogue";
       Debug.Log("[DebugTools] No dialogue profiles found, using placeholder");
       }

         var questProfiles = QuestProfileManager.GetAllProfileNames();
                if (questProfiles != null && questProfiles.Count > 0)
 {
    controller.questProfile = questProfiles[0];
   Debug.Log($"[DebugTools] Mock NPC using quest profile: {controller.questProfile}");
    }
                else
 {
            controller.questProfile = "test_quests";
       Debug.Log("[DebugTools] No quest profiles found, using placeholder");
}

      Debug.Log("[DebugTools] Created mock NPC controller for UI testing");
                return controller;
        }
            catch (Exception ex)
            {
        Debug.LogError($"[DebugTools] Failed to create mock NPC: {ex}");
   return null;
            }
        }

        /// <summary>
        /// Cleans up the mock NPC object.
      /// </summary>
public static void CleanupMockNpc()
    {
       if (_mockNpcObject != null)
            {
    UnityEngine.Object.Destroy(_mockNpcObject);
     _mockNpcObject = null;
 Debug.Log("[DebugTools] Mock NPC cleaned up");
            }
        }

        #endregion

        #region Sprite Enumeration

        /// <summary>
        /// Dumps all available sprites in the game to a file.
        /// </summary>
        private static void DumpAvailableSprites(Terminal.ConsoleEventArgs args)
        {
            string filter = (args.Args != null && args.Args.Length > 1) 
                ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim().ToLower()
                : "";

            args.Context.AddString("[FiresRPGmaker] Scanning for all sprites...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM SPRITE REFERENCE");
            sb.AppendLine("For UI Implementation (Radial Menu Icons, etc.)");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(filter))
                sb.AppendLine($"Filter: {filter}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var allSprites = new Dictionary<string, List<(string name, int width, int height, string source)>>();

            // Method 1: Find all Sprite objects loaded in memory
            var loadedSprites = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var sprite in loadedSprites)
            {
                if (sprite == null) continue;
                string name = sprite.name;
                if (string.IsNullOrEmpty(name)) continue;
                if (!string.IsNullOrEmpty(filter) && !name.ToLower().Contains(filter)) continue;

                string category = CategorizeSprite(name);
                if (!allSprites.ContainsKey(category))
                    allSprites[category] = new List<(string, int, int, string)>();

                string source = sprite.texture?.name ?? "Unknown";
                allSprites[category].Add((name, (int)sprite.rect.width, (int)sprite.rect.height, source));
            }

            // Section 1: Summary
            sb.AppendLine("=== SPRITE SUMMARY ===");
            sb.AppendLine();
            int totalCount = 0;
            foreach (var kvp in allSprites.OrderBy(k => k.Key))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value.Count} sprites");
                totalCount += kvp.Value.Count;
            }
            sb.AppendLine($"  TOTAL: {totalCount} sprites");
            sb.AppendLine();

            // Section 2: Detailed list by category
            foreach (var kvp in allSprites.OrderBy(k => k.Key))
            {
                sb.AppendLine($"=== {kvp.Key.ToUpper()} SPRITES ({kvp.Value.Count}) ===");
                sb.AppendLine();
                
                foreach (var (name, width, height, source) in kvp.Value.OrderBy(s => s.name))
                {
                    sb.AppendLine($"  {name} ({width}x{height}) [Texture: {source}]");
                }
                sb.AppendLine();
            }

            // Section 3: Code snippet for loading sprites
            sb.AppendLine("=== CODE: LOADING SPRITES ===");
            sb.AppendLine();
            sb.AppendLine(@"
// Method 1: Find sprite by name from loaded resources
public static Sprite FindSpriteByName(string spriteName)
{
    // Search all loaded sprites
    var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
    foreach (var sprite in sprites)
    {
        if (sprite != null && sprite.name == spriteName)
            return sprite;
    }
    return null;
}

// Method 2: Get item icon from ObjectDB
public static Sprite GetItemIcon(string prefabName)
{
    var prefab = ObjectDB.instance?.GetItemPrefab(prefabName);
    if (prefab != null)
    {
        var itemDrop = prefab.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.GetIcon();
    }
    return null;
}

// Method 3: Get skill icon
public static Sprite GetSkillIcon(Skills.SkillType skillType)
{
    var skillDef = Player.m_localPlayer?.GetSkills()?.GetSkillDef(skillType);
    return skillDef?.m_icon;
}

// Method 4: From Hud elements
public static Sprite GetHudSprite(string elementName)
{
    if (Hud.instance == null) return null;
    var images = Hud.instance.GetComponentsInChildren<UnityEngine.UI.Image>(true);
    foreach (var img in images)
    {
        if (img.name == elementName && img.sprite != null)
            return img.sprite;
    }
    return null;
}
");
            sb.AppendLine();

            // Section 4: Recommended icons for radial menu
            sb.AppendLine("=== RECOMMENDED ICONS FOR RADIAL MENU ===");
            sb.AppendLine();
            sb.AppendLine("For companion actions, look for these sprite names:");
            sb.AppendLine();
            sb.AppendLine("  [Follow/Stay]");
            sb.AppendLine("    - Look for: foot, walk, follow, stay, arrow");
            sb.AppendLine("  [Inventory]");
            sb.AppendLine("    - Look for: bag, backpack, inventory, chest");
            sb.AppendLine("  [Stats]");
            sb.AppendLine("    - Look for: stats, chart, heart, health");
            sb.AppendLine("  [Gather Resources]");
            sb.AppendLine("    - Look for: axe, pickaxe, wood, stone");
            sb.AppendLine("  [Tend Fires]");
            sb.AppendLine("    - Look for: fire, flame, campfire, torch");
            sb.AppendLine("  [Operate Smelters]");
            sb.AppendLine("    - Look for: forge, smelter, anvil, hammer");
            sb.AppendLine("  [Deposit Items]");
            sb.AppendLine("    - Look for: chest, deposit, store, box");
            sb.AppendLine("  [Wander]");
            sb.AppendLine("    - Look for: compass, explore, wander, patrol");
            sb.AppendLine("  [Set Home]");
            sb.AppendLine("    - Look for: home, house, pin, marker");
            sb.AppendLine("  [Dismiss]");
            sb.AppendLine("    - Look for: x, close, dismiss, leave");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "SpriteReference.txt");

            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Found {totalCount} sprites" + 
                (string.IsNullOrEmpty(filter) ? "" : $" matching '{filter}'"));
            args.Context.AddString($"[FiresRPGmaker] Full list saved to: {outputPath}");

            // Show categories in console
            foreach (var kvp in allSprites.OrderBy(k => k.Key).Take(10))
            {
                args.Context.AddString($"  {kvp.Key}: {kvp.Value.Count} sprites");
            }
            if (allSprites.Count > 10)
            {
                args.Context.AddString($"  ... and {allSprites.Count - 10} more categories");
            }

            args.Context.AddString("[FiresRPGmaker] Use: listsprites <filter> to search");
            args.Context.AddString("[FiresRPGmaker] Use: listicons for item/skill icons specifically");
        }

        /// <summary>
        /// Dumps all item and skill icons to a file - more focused for UI work.
        /// </summary>
        private static void DumpIconSprites(Terminal.ConsoleEventArgs args)
        {
            string filter = (args.Args != null && args.Args.Length > 1) 
                ? string.Join(" ", args.Args, 1, args.Args.Length - 1).Trim().ToLower()
                : "";

            args.Context.AddString("[FiresRPGmaker] Scanning for icon sprites...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("VALHEIM ICON SPRITE REFERENCE");
            sb.AppendLine("Item Icons, Skill Icons, and UI Icons");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(filter))
                sb.AppendLine($"Filter: {filter}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            var itemIcons = new List<(string prefab, string displayName, string spriteName, int width, int height)>();
            var skillIcons = new List<(Skills.SkillType skill, string spriteName, int width, int height)>();
            var uiIcons = new List<(string name, string source, int width, int height)>();

            // Method 1: Get all item icons from ObjectDB
            if (ObjectDB.instance != null)
            {
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (prefab == null) continue;
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop == null) continue;

                    var icon = itemDrop.m_itemData?.GetIcon();
                    if (icon == null) continue;

                    string prefabName = prefab.name;
                    string displayName = itemDrop.m_itemData?.m_shared?.m_name ?? prefabName;
                    
                    if (!string.IsNullOrEmpty(filter) && 
                        !prefabName.ToLower().Contains(filter) && 
                        !displayName.ToLower().Contains(filter) &&
                        !icon.name.ToLower().Contains(filter))
                        continue;

                    itemIcons.Add((prefabName, displayName, icon.name, (int)icon.rect.width, (int)icon.rect.height));
                }
            }

            // Method 2: Get all skill icons
            if (Player.m_localPlayer != null)
            {
                var skills = Player.m_localPlayer.GetSkills();
                if (skills != null && skills.m_skills != null)
                {
                    foreach (var skillDef in skills.m_skills)
                    {
                        if (skillDef == null || skillDef.m_icon == null) continue;
                        
                        var icon = skillDef.m_icon;
                        var skillType = skillDef.m_skill;
                        
                        if (!string.IsNullOrEmpty(filter) && 
                            !skillType.ToString().ToLower().Contains(filter) &&
                            !icon.name.ToLower().Contains(filter))
                            continue;

                        skillIcons.Add((skillType, icon.name, (int)icon.rect.width, (int)icon.rect.height));
                    }
                }
            }

            // Method 3: Scan UI elements for icons
            var allImages = UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Image>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
            var seenIcons = new HashSet<string>();
            foreach (var img in allImages)
            {
                if (img == null || img.sprite == null) continue;
                string spriteName = img.sprite.name;
                if (string.IsNullOrEmpty(spriteName) || seenIcons.Contains(spriteName)) continue;
                
                // Look for icon-like sprites (typically square, small)
                float width = img.sprite.rect.width;
                float height = img.sprite.rect.height;
                
                // Skip very large sprites (backgrounds, etc)
                if (width > 256 || height > 256) continue;
                
                if (!string.IsNullOrEmpty(filter) && !spriteName.ToLower().Contains(filter))
                    continue;

                string source = img.gameObject.name;
                seenIcons.Add(spriteName);
                uiIcons.Add((spriteName, source, (int)width, (int)height));
            }

            // Section 1: Item Icons
            sb.AppendLine($"=== ITEM ICONS ({itemIcons.Count}) ===");
            sb.AppendLine();
            sb.AppendLine("| Prefab Name                    | Display Name                   | Sprite Name                    | Size     |");
            sb.AppendLine("|--------------------------------|--------------------------------|--------------------------------|----------|");
            foreach (var (prefab, displayName, spriteName, w, h) in itemIcons.OrderBy(i => i.prefab))
            {
                string pn = prefab.Length > 30 ? prefab.Substring(0, 27) + "..." : prefab;
                string dn = displayName.Length > 30 ? displayName.Substring(0, 27) + "..." : displayName;
                string sn = spriteName.Length > 30 ? spriteName.Substring(0, 27) + "..." : spriteName;
                sb.AppendLine($"| {pn,-30} | {dn,-30} | {sn,-30} | {w}x{h,-5} |");
            }
            sb.AppendLine();

            // Section 2: Skill Icons
            sb.AppendLine($"=== SKILL ICONS ({skillIcons.Count}) ===");
            sb.AppendLine();
            sb.AppendLine("| Skill Type                     | Sprite Name                    | Size     |");
            sb.AppendLine("|--------------------------------|--------------------------------|----------|");
            foreach (var (skill, spriteName, w, h) in skillIcons.OrderBy(i => i.skill.ToString()))
            {
                string sn = spriteName.Length > 30 ? spriteName.Substring(0, 27) + "..." : spriteName;
                sb.AppendLine($"| {skill,-30} | {sn,-30} | {w}x{h,-5} |");
            }
            sb.AppendLine();

            // Section 3: UI Icons
            sb.AppendLine($"=== UI ICONS ({uiIcons.Count}) ===");
            sb.AppendLine();
            sb.AppendLine("| Sprite Name                    | Source UI Element              | Size     |");
            sb.AppendLine("|--------------------------------|--------------------------------|----------|");
            foreach (var (name, source, w, h) in uiIcons.OrderBy(i => i.name))
            {
                string nm = name.Length > 30 ? name.Substring(0, 27) + "..." : name;
                string sr = source.Length > 30 ? source.Substring(0, 27) + "..." : source;
                sb.AppendLine($"| {nm,-30} | {sr,-30} | {w}x{h,-5} |");
            }
            sb.AppendLine();

            // Section 4: Recommended icons for radial menu actions
            sb.AppendLine("=== RECOMMENDED ICONS FOR RADIAL MENU ===");
            sb.AppendLine();
            
            // Search for specific useful icons
            var recommendations = new Dictionary<string, List<string>>
            {
                { "Follow/Stay (movement)", new List<string>() },
                { "Inventory/Storage", new List<string>() },
                { "Stats/Health", new List<string>() },
                { "Gathering (axe/pickaxe)", new List<string>() },
                { "Fire/Torch", new List<string>() },
                { "Crafting/Forge", new List<string>() },
                { "Chest/Container", new List<string>() },
                { "Home/House", new List<string>() },
                { "Dismiss/Close", new List<string>() },
                { "Combat/Sword", new List<string>() },
                { "Shield/Defense", new List<string>() },
                { "Bow/Ranged", new List<string>() },
                { "Magic/Staff", new List<string>() },
                { "Healing/Support", new List<string>() },
            };

            // Check item icons for matches
            foreach (var (prefab, displayName, spriteName, _, _) in itemIcons)
            {
                string p = prefab.ToLower();
                string d = displayName.ToLower();
                
                if (p.Contains("axe") && !p.Contains("pickaxe"))
                    recommendations["Gathering (axe/pickaxe)"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("pickaxe"))
                    recommendations["Gathering (axe/pickaxe)"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("torch") || p.Contains("fire"))
                    recommendations["Fire/Torch"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("hammer") || p.Contains("forge") || p.Contains("anvil"))
                    recommendations["Crafting/Forge"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("chest") || p.Contains("box") || p.Contains("container"))
                    recommendations["Chest/Container"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("sword") || p.Contains("atgeir") || p.Contains("mace"))
                    recommendations["Combat/Sword"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("shield"))
                    recommendations["Shield/Defense"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("bow") || p.Contains("crossbow"))
                    recommendations["Bow/Ranged"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("staff"))
                    recommendations["Magic/Staff"].Add($"{prefab} -> {spriteName}");
                if (p.Contains("mead") || p.Contains("potion") || d.Contains("heal"))
                    recommendations["Healing/Support"].Add($"{prefab} -> {spriteName}");
            }

            // Check skill icons
            foreach (var (skill, spriteName, _, _) in skillIcons)
            {
                string s = skill.ToString().ToLower();
                
                if (s.Contains("woodcutting"))
                    recommendations["Gathering (axe/pickaxe)"].Add($"Skill:{skill} -> {spriteName}");
                if (s.Contains("pickaxe") || s.Contains("mining"))
                    recommendations["Gathering (axe/pickaxe)"].Add($"Skill:{skill} -> {spriteName}");
                if (s.Contains("sword") || s.Contains("polearm") || s.Contains("club") || s.Contains("knife"))
                    recommendations["Combat/Sword"].Add($"Skill:{skill} -> {spriteName}");
                if (s.Contains("blocking"))
                    recommendations["Shield/Defense"].Add($"Skill:{skill} -> {spriteName}");
                if (s.Contains("bow") || s.Contains("crossbow"))
                    recommendations["Bow/Ranged"].Add($"Skill:{skill} -> {spriteName}");
                if (s.Contains("bloodmagic") || s.Contains("elementalmagic"))
                    recommendations["Magic/Staff"].Add($"Skill:{skill} -> {spriteName}");
            }

            foreach (var kvp in recommendations)
            {
                sb.AppendLine($"  [{kvp.Key}]");
                if (kvp.Value.Count == 0)
                {
                    sb.AppendLine("    (no matches found - use procedural icon)");
                }
                else
                {
                    foreach (var match in kvp.Value.Take(5))
                    {
                        sb.AppendLine($"    - {match}");
                    }
                    if (kvp.Value.Count > 5)
                    {
                        sb.AppendLine($"    ... and {kvp.Value.Count - 5} more");
                    }
                }
                sb.AppendLine();
            }

            // Section 5: Code for loading specific icons
            sb.AppendLine("=== CODE: LOADING ICONS FOR RADIAL MENU ===");
            sb.AppendLine();
            sb.AppendLine(@"
// Cache item icons at startup
private static readonly Dictionary<string, Sprite> _itemIconCache = new Dictionary<string, Sprite>();

public static void CacheCommonIcons()
{
    // Item icons
    CacheItemIcon(""AxeBronze"");     // Gathering
    CacheItemIcon(""PickaxeBronze""); // Mining
    CacheItemIcon(""Torch"");         // Fire tending
    CacheItemIcon(""Hammer"");        // Crafting
    CacheItemIcon(""Chest"");         // Storage
    CacheItemIcon(""SwordBronze"");   // Combat
    CacheItemIcon(""ShieldWood"");    // Defense
    CacheItemIcon(""Bow"");           // Ranged
}

private static void CacheItemIcon(string prefabName)
{
    if (ObjectDB.instance == null) return;
    var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
    if (prefab != null)
    {
        var itemDrop = prefab.GetComponent<ItemDrop>();
        var icon = itemDrop?.m_itemData?.GetIcon();
        if (icon != null)
        {
            _itemIconCache[prefabName] = icon;
        }
    }
}

public static Sprite GetCachedIcon(string prefabName)
{
    return _itemIconCache.TryGetValue(prefabName, out var sprite) ? sprite : null;
}

// Alternative: Direct sprite search
public static Sprite FindSprite(string spriteName)
{
    var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
    return sprites.FirstOrDefault(s => s.name == spriteName);
}
");
            sb.AppendLine();

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "IconReference.txt");

            File.WriteAllText(outputPath, sb.ToString());

            int totalCount = itemIcons.Count + skillIcons.Count + uiIcons.Count;
            args.Context.AddString($"[FiresRPGmaker] Found {totalCount} icons:");
            args.Context.AddString($"  - {itemIcons.Count} item icons");
            args.Context.AddString($"  - {skillIcons.Count} skill icons");
            args.Context.AddString($"  - {uiIcons.Count} UI icons");
            args.Context.AddString($"[FiresRPGmaker] Full list saved to: {outputPath}");

            // Show some recommended items in console
            args.Context.AddString("[FiresRPGmaker] Sample item icons found:");
            var sampleItems = itemIcons
                .Where(i => i.prefab.Contains("Axe") || i.prefab.Contains("Pickaxe") || 
                           i.prefab.Contains("Torch") || i.prefab.Contains("Hammer") ||
                           i.prefab.Contains("Chest") || i.prefab.Contains("Sword"))
                .Take(10);
            foreach (var (prefab, _, spriteName, _, _) in sampleItems)
            {
                args.Context.AddString($"  {prefab} -> {spriteName}");
            }
        }

        /// <summary>
        /// Categorizes a sprite based on its name.
        /// </summary>
        private static string CategorizeSprite(string name)
        {
            string lower = name.ToLower();
            
            if (lower.Contains("icon") || lower.Contains("item_"))
                return "Icons/Items";
            if (lower.Contains("skill"))
                return "Icons/Skills";
            if (lower.Contains("background") || lower.Contains("panel") || lower.Contains("frame"))
                return "UI/Backgrounds";
            if (lower.Contains("button") || lower.Contains("btn"))
                return "UI/Buttons";
            if (lower.Contains("slot") || lower.Contains("inventory"))
                return "UI/Inventory";
            if (lower.Contains("minimap") || lower.Contains("map") || lower.Contains("pin"))
                return "UI/Map";
            if (lower.Contains("health") || lower.Contains("stamina") || lower.Contains("eitr") || lower.Contains("bar"))
                return "UI/StatusBars";
            if (lower.Contains("crosshair") || lower.Contains("cursor") || lower.Contains("pointer"))
                return "UI/Cursors";
            if (lower.Contains("arrow") || lower.Contains("chevron"))
                return "UI/Navigation";
            if (lower.Contains("particle") || lower.Contains("fx") || lower.Contains("effect"))
                return "Effects";
            if (lower.Contains("creature") || lower.Contains("monster") || lower.Contains("enemy"))
                return "Creatures";
            
            return "Other";
        }

        #endregion

        #region Chat UI Hierarchy Dump

        /// <summary>
        /// Walks the Chat UI hierarchy and writes a detailed dump to a text file.
        /// Includes RectTransform anchors, offsets, pivots, sizes, components, and
        /// active state for every object down to the specified depth.
        /// </summary>
        private static void DumpChatUIHierarchy(Terminal.ConsoleEventArgs args)
        {
            if (Chat.instance == null)
            {
                args.Context.AddString("[FiresRPGmaker] Chat.instance is null - open the game first.");
                return;
            }

            var chatWindow = Chat.instance.m_chatWindow;
            if (chatWindow == null)
            {
                args.Context.AddString("[FiresRPGmaker] Chat.m_chatWindow is null.");
                return;
            }

            int maxDepth = 8;
            if (args.Length >= 2)
            {
                int d;
                if (args.TryParameterInt(1, out d))
                    maxDepth = Mathf.Clamp(d, 1, 20);
            }

            args.Context.AddString("[FiresRPGmaker] Dumping Chat UI hierarchy...");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=============================================================");
            sb.AppendLine("CHAT UI HIERARCHY DUMP");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Max depth: {maxDepth}");
            sb.AppendLine("=============================================================");
            sb.AppendLine();

            // Dump from chat window's parent so we can see siblings too
            Transform root = chatWindow.parent != null ? chatWindow.parent : (Transform)chatWindow;
            sb.AppendLine($"Root: {GetFullPath(root)}");
            sb.AppendLine();

            WalkHierarchy(sb, root, 0, maxDepth);

            // Also dump the input field details specifically
            sb.AppendLine();
            sb.AppendLine("=============================================================");
            sb.AppendLine("INPUT FIELD DETAILS");
            sb.AppendLine("=============================================================");
            var input = Chat.instance.m_input;
            if (input != null)
            {
                var irt = input.GetComponent<RectTransform>();
                sb.AppendLine($"  GameObject: {input.gameObject.name}");
                sb.AppendLine($"  Full path:  {GetFullPath(input.transform)}");
                sb.AppendLine($"  Active:     {input.gameObject.activeSelf}  (hierarchy: {input.gameObject.activeInHierarchy})");
                if (irt != null)
                {
                    sb.AppendLine($"  AnchorMin:  {irt.anchorMin}");
                    sb.AppendLine($"  AnchorMax:  {irt.anchorMax}");
                    sb.AppendLine($"  Pivot:      {irt.pivot}");
                    sb.AppendLine($"  OffsetMin:  {irt.offsetMin}");
                    sb.AppendLine($"  OffsetMax:  {irt.offsetMax}");
                    sb.AppendLine($"  SizeDelta:  {irt.sizeDelta}");
                    sb.AppendLine($"  AnchoredPos:{irt.anchoredPosition}");
                    sb.AppendLine($"  Rect:       {irt.rect}");
                    Vector3[] corners = new Vector3[4];
                    irt.GetWorldCorners(corners);
                    sb.AppendLine($"  WorldCorners: BL={corners[0]} TL={corners[1]} TR={corners[2]} BR={corners[3]}");
                }
                sb.AppendLine($"  Components: {GetComponentList(input.transform)}");
                sb.AppendLine($"  Sibling index: {input.transform.GetSiblingIndex()} of {input.transform.parent?.childCount ?? 0}");
                sb.AppendLine($"  Parent: {(input.transform.parent != null ? input.transform.parent.name : "(none)")}");
            }
            else
            {
                sb.AppendLine("  (null)");
            }

            // Write to file
            string outputDir = Path.Combine(FiresCore.Storage.FiresConfigPaths.Debug);
            Directory.CreateDirectory(outputDir);
            string outputPath = Path.Combine(outputDir, "ChatUIHierarchy.txt");
            File.WriteAllText(outputPath, sb.ToString());

            args.Context.AddString($"[FiresRPGmaker] Chat UI hierarchy saved to: {outputPath}");
            Debug.Log($"[FiresRPGmaker] Chat UI hierarchy saved to: {outputPath}");
        }

        private static void WalkHierarchy(System.Text.StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            if (t == null || depth > maxDepth) return;

            string indent = new string(' ', depth * 2);
            var rt = t as RectTransform ?? t.GetComponent<RectTransform>();

            sb.Append($"{indent}{t.name}");
            sb.Append($"  active={t.gameObject.activeSelf}");

            if (rt != null)
            {
                sb.Append($"  anchor({rt.anchorMin}-{rt.anchorMax})");
                sb.Append($"  offset({rt.offsetMin}-{rt.offsetMax})");
                sb.Append($"  pivot({rt.pivot})");
                sb.Append($"  size({rt.sizeDelta})");
                sb.Append($"  pos({rt.anchoredPosition})");
                sb.Append($"  rect({rt.rect.width:F0}x{rt.rect.height:F0})");
            }

            sb.Append($"  [{GetComponentList(t)}]");
            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                WalkHierarchy(sb, t.GetChild(i), depth + 1, maxDepth);
        }

        private static string GetComponentList(Transform t)
        {
            var comps = t.GetComponents<Component>();
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (i > 0) names.Append(", ");
                names.Append(comps[i] != null ? comps[i].GetType().Name : "null");
            }
            return names.ToString();
        }

        private static string GetFullPath(Transform t)
        {
            var parts = new List<string>();
            var current = t;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        #endregion
    }
}
