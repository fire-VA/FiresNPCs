using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace FiresRPGmaker.UI.Screens
{
    /// <summary>
    /// Controller for the Info NPC configuration screen (admin edit mode).
    /// For player-facing display, use InfoNpcPlayerPanel instead.
    /// Uses Marketplace-compatible config format for compatibility.
    /// </summary>
  public class InfoNpcScreenController : MonoBehaviour
    {
        public static InfoNpcScreenController Instance { get; private set; }

        [Header("UI Elements")]
        public GameObject screenRoot;
        public TMP_InputField headerTextInput;
    public TMP_InputField pngPathInput;
        public TMP_InputField infoTextArea;
        public TMP_Text characterCountText;
      public TMP_Text instructionsText;

        // Reference to current NPC being edited
        private NpcController _currentNpc;
        private bool _hasUnsavedChanges;
        private Button _applyButton;

        // Config folder path - mirrors Marketplace structure for compatibility
        private static string ServerInfosConfigFolder => Path.Combine(FiresCore.Storage.FiresConfigPaths.ServerInfos);

        // Cache of loaded profiles
      private static Dictionary<string, InfoProfile> _profileCache = new Dictionary<string, InfoProfile>(StringComparer.OrdinalIgnoreCase);
        private static bool _cacheLoaded = false;

        /// <summary>
        /// Data structure for info profiles with header, image, and text.
        /// </summary>
        public class InfoProfile
        {
    public string HeaderText { get; set; } = "";
  public string PngPath { get; set; } = "";
            public string InfoText { get; set; } = "";
   }

        #region Lifecycle

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        #endregion

   #region Factory

   /// <summary>
   /// Wires this controller to pre-built elements in the codegen hierarchy.
   /// Called by CodegenUIWiring.WireInfoScreen().
   /// </summary>
   public void WireFromCodegen(GameObject screenGo, Button applyButton)
   {
       screenRoot = screenGo;
       instructionsText = CodegenUIWiring.FindTMP(screenGo.transform, "Instructions");
       headerTextInput = CodegenUIWiring.FindInputField(screenGo.transform, "Row_HeaderText");
       pngPathInput = CodegenUIWiring.FindInputField(screenGo.transform, "Row_Image(PNG)");
       infoTextArea = CodegenUIWiring.FindInputField(screenGo.transform, "TextAreaContainer");
       characterCountText = CodegenUIWiring.FindTMP(screenGo.transform, "CharacterCount");

       // Wire apply button
       _applyButton = applyButton;
       if (_applyButton != null)
       {
           _applyButton.onClick.RemoveAllListeners();
           _applyButton.onClick.AddListener(ApplyChanges);

           // Add border highlight if not already present
           var border = _applyButton.transform.Find("_TransparentBorder");
           if (border != null)
           {
               var borderImage = border.GetComponent<Image>();
               if (borderImage != null && border.GetComponent<ButtonBorderHighlight>() == null)
               {
                   var highlightHandler = border.gameObject.AddComponent<ButtonBorderHighlight>();
                   highlightHandler.Initialize(borderImage, _applyButton);
               }
           }

           _applyButton.gameObject.SetActive(false);
       }

       // Wire up events
       if (infoTextArea != null)
           infoTextArea.onValueChanged.AddListener(OnTextChanged);
       if (headerTextInput != null)
           headerTextInput.onValueChanged.AddListener(_ => _hasUnsavedChanges = true);
       if (pngPathInput != null)
           pngPathInput.onValueChanged.AddListener(_ => _hasUnsavedChanges = true);

       screenGo.SetActive(false);

       Debug.Log($"[InfoNpcScreenController] WireFromCodegen - Instructions={instructionsText != null}, " +
                 $"HeaderInput={headerTextInput != null}, PngInput={pngPathInput != null}, " +
                 $"InfoArea={infoTextArea != null}, CharCount={characterCountText != null}, " +
                 $"ApplyBtn={_applyButton != null}");
   }

        #endregion

        #region Show/Hide

        public void Show(NpcController npc)
   {
            _currentNpc = npc;
            _hasUnsavedChanges = false;

            var profile = LoadInfoProfile(npc);

         headerTextInput.text = profile?.HeaderText ?? "";
        pngPathInput.text = profile?.PngPath ?? "";
    infoTextArea.text = profile?.InfoText ?? "";

         UpdateCharacterCount();
            screenRoot.SetActive(true);

    if (_applyButton != null)
_applyButton.gameObject.SetActive(true);
      }

        public void Hide()
        {
            screenRoot.SetActive(false);

            if (_applyButton != null)
        _applyButton.gameObject.SetActive(false);

 _currentNpc = null;
        }

   public bool IsVisible => screenRoot != null && screenRoot.activeSelf;
        public bool HasUnsavedChanges => _hasUnsavedChanges;
  public string GetCurrentText() => infoTextArea?.text ?? "";

        #endregion

    #region Events

   private void OnTextChanged(string newText)
        {
_hasUnsavedChanges = true;
      UpdateCharacterCount();
 }

        private void UpdateCharacterCount()
        {
            if (characterCountText != null && infoTextArea != null)
            {
   int count = infoTextArea.text?.Length ?? 0;
      characterCountText.text = $"Characters: {count} / 5000";
     
          if (count > 4500)
        characterCountText.color = UIFontConfig.Colors.Red;
        else if (count > 4000)
    characterCountText.color = UIFontConfig.Colors.Yellow;
       else
           characterCountText.color = UIFontConfig.Colors.MutedGold;
       }
        }

        #endregion

        #region Apply Changes

      public void ApplyChanges()
        {
    if (_currentNpc == null)
        {
          Debug.LogWarning("[InfoNpcScreenController] Cannot apply - no NPC selected");
        return;
           }

        string headerText = headerTextInput.text?.Trim() ?? "";
             string pngPath = pngPathInput.text?.Trim() ?? "";
  string infoText = infoTextArea.text?.Trim() ?? "";

     string profileName = GetProfileNameForNpc(_currentNpc);

        // Check if this profile already exists and belongs to a different NPC.
        // If the NPC's infoProfile is already set to this name, it's updating its own profile - allow it.
        bool isNewProfileForThisNpc = string.IsNullOrEmpty(_currentNpc.infoProfile) || 
            !_currentNpc.infoProfile.Equals(profileName, StringComparison.OrdinalIgnoreCase);

        if (isNewProfileForThisNpc)
        {
            var existingProfile = GetProfileForName(profileName);
            if (existingProfile != null)
            {
                // Profile exists and this NPC hasn't claimed it yet - prompt for overwrite
                OverwriteConfirmDialog.Show(profileName, () =>
                {
                    PerformSave(profileName, headerText, pngPath, infoText);
                });
                return;
            }
        }

        PerformSave(profileName, headerText, pngPath, infoText);
        }

        private void PerformSave(string profileName, string headerText, string pngPath, string infoText)
        {
 var profile = new InfoProfile
    {
       HeaderText = headerText,
             PngPath = pngPath,
        InfoText = infoText
   };

             SaveInfoProfile(profileName, profile);

             _currentNpc.infoProfile = profileName;
        _currentNpc.npcType = NpcType.InfoNpc;
       _currentNpc.SaveToZDO();

     _profileCache[profileName] = profile;
       _hasUnsavedChanges = false;

             MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, $"Info NPC saved with profile: {profileName}");
   SyncToServerIfNeeded();
        }

   private void SyncToServerIfNeeded() => PushServerInfosToServer();

        /// <summary>Pushes the local ServerInfos.cfg to the server via the shared file-sync RPC. No-op on the
        /// server/host (it owns the file). Used by BOTH the codegen editor AND the book Info save — without
        /// this the dedicated server never receives an admin's Info edit and nothing persists server-side.</summary>
        public static void PushServerInfosToServer()
        {
            try
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) return;
                if (ZRoutedRpc.instance == null) return;

                string filePath = Path.Combine(ServerInfosConfigFolder, "ServerInfos.cfg");
                if (!File.Exists(filePath)) return;

                string fileContent = File.ReadAllText(filePath);
                var pkg = new ZPackage();
                pkg.Write("ServerInfos");
                pkg.Write("ServerInfos.cfg");
                pkg.Write(fileContent);
                ZRoutedRpc.instance.InvokeRoutedRPC(0L, "FiresRPGmaker_SyncFiles", pkg);
                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Config synced to server");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InfoNpcScreenController] Failed to sync: {ex.Message}");
            }
        }

        #endregion

        #region Profile Management

        private string GetProfileNameForNpc(NpcController npc) => DefaultProfileNameFor(npc);

        /// <summary>Resolves the Info profile name for an NPC. Prefers an explicit infoProfile, then the NPC's
        /// (non-default) display name, else a UNIQUE per-placement id so multiple unnamed Info NPCs don't all
        /// collide on the shared "StaticNpc" name — which made a second new Info NPC overwrite the first.
        /// The unique id is taken from the stable placement anchor, so it survives relogs/patrol movement.</summary>
        public static string DefaultProfileNameFor(NpcController npc)
        {
            if (npc == null) return "default";
            if (!string.IsNullOrEmpty(npc.infoProfile)) return npc.infoProfile;
            string display = npc.GetDisplayName();
            if (!string.IsNullOrEmpty(display) && !global::FiresRPGmaker.Managers.SavedNpcManager.IsDefaultNpcName(display))
                return MakeSafeProfileName(display);
            Vector3 p = npc.transform.position;
            try
            {
                var z = npc.GetComponent<ZNetView>()?.GetZDO();
                if (z != null) p = z.GetVec3("npc_placed_anchor", z.GetVec3("npc_stationed_pos", p));
            }
            catch { }
            return $"info_{Mathf.RoundToInt(p.x)}_{Mathf.RoundToInt(p.y)}_{Mathf.RoundToInt(p.z)}";
        }

        private static string MakeSafeProfileName(string name)
        {
   var sb = new StringBuilder();
   foreach (char c in name)
   {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
 else if (c == ' ') sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "default";
        }

        private InfoProfile LoadInfoProfile(NpcController npc)
        {
            string profileName = GetProfileNameForNpc(npc);
       return GetProfileForName(profileName);
     }

        public static void SaveInfoProfile(string profileName, InfoProfile profile)
        {
          try
            {
       Directory.CreateDirectory(ServerInfosConfigFolder);
      string filePath = Path.Combine(ServerInfosConfigFolder, "ServerInfos.cfg");
      var profiles = LoadAllProfilesForEdit(filePath);
            profiles[profileName] = profile;
       WriteAllProfilesToFile(filePath, profiles);
            }
            catch (Exception ex)
       {
           Debug.LogError($"[InfoNpcScreenController] Failed to save: {ex.Message}");
     MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "Failed to save!");
            }
        }

        /// <summary>Base set an admin's Info edit merges INTO. On a pure client the real profiles
        /// live only in the RAM sync store — the client disk file is absent/sparse — so reading disk
        /// here would give an empty set and the follow-up push would carry ONE profile and blind-
        /// overwrite (wipe) every other profile on the server. Read the received store on a client;
        /// the server/host keeps reading disk (its source of truth).</summary>
        private static Dictionary<string, InfoProfile> LoadAllProfilesForEdit(string filePath)
        {
            if (!global::FiresRPGmaker.Managers.FiresConfigFiles.UseStore)
                return LoadAllProfilesFromFile(filePath);

            var merged = new Dictionary<string, InfoProfile>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("ServerInfos"))
                foreach (var p in LoadAllProfilesFromLines(kv.Value))
                    merged[p.Key] = p.Value;
            return merged;
        }

     #endregion

        #region Static Profile Cache

        public static void ReloadCache()
        {
  _profileCache.Clear();
     _cacheLoaded = false;
            EnsureCacheLoaded();
        }

    private static void EnsureCacheLoaded()
        {
            if (_cacheLoaded) return;
   _cacheLoaded = true;

        try
       {
          // Pure clients parse the server-synced in-memory store (no config files on client disk);
          // server/host reads its folder as before.
          foreach (var kv in global::FiresRPGmaker.Managers.FiresConfigFiles.EnumerateLines("ServerInfos"))
          {
           var profiles = LoadAllProfilesFromLines(kv.Value);
    foreach (var kvp in profiles)
         {
     _profileCache[kvp.Key] = kvp.Value;
                }
          }
}
catch (Exception ex)
       {
        Debug.LogWarning($"[InfoNpcScreenController] Failed to load cache: {ex.Message}");
   }
 }

      public static InfoProfile GetProfileForName(string profileName)
        {
 if (string.IsNullOrEmpty(profileName)) profileName = "default";
            EnsureCacheLoaded();

            if (_profileCache.TryGetValue(profileName, out InfoProfile profile))
   return profile;

       if (!profileName.Equals("default", StringComparison.OrdinalIgnoreCase))
     if (_profileCache.TryGetValue("default", out InfoProfile defaultProfile))
         return defaultProfile;

        return null;
  }

        public static string GetInfoTextForProfile(string profileName)
   {
            var profile = GetProfileForName(profileName);
            return profile?.InfoText;
        }

        public static List<string> GetAllProfileNames()
        {
          EnsureCacheLoaded();
  return new List<string>(_profileCache.Keys);
        }

#endregion

    #region File I/O

     private static Dictionary<string, InfoProfile> LoadAllProfilesFromFile(string filePath)
        {
            if (!File.Exists(filePath)) return new Dictionary<string, InfoProfile>(StringComparer.OrdinalIgnoreCase);
            string[] fileLines;
            try { fileLines = File.ReadAllLines(filePath); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InfoNpcScreenController] Failed to load profiles: {ex.Message}");
                return new Dictionary<string, InfoProfile>(StringComparer.OrdinalIgnoreCase);
            }
            return LoadAllProfilesFromLines(fileLines);
        }

        private static Dictionary<string, InfoProfile> LoadAllProfilesFromLines(string[] lines)
        {
            var profiles = new Dictionary<string, InfoProfile>(StringComparer.OrdinalIgnoreCase);

     try
      {
  string currentProfile = null;
         InfoProfile currentData = null;
          var contentBuilder = new StringBuilder();
       string currentSection = "info";

          foreach (string line in lines)
     {
           // Profile header: [ProfileName]
         if (line.StartsWith("[") && line.EndsWith("]") &&
             !line.StartsWith("[header]", StringComparison.OrdinalIgnoreCase) &&
       !line.StartsWith("[image]", StringComparison.OrdinalIgnoreCase) &&
      !line.StartsWith("[info]", StringComparison.OrdinalIgnoreCase))
 {
      // Save previous profile
         if (currentProfile != null && currentData != null)
  {
             if (currentSection == "info")
    currentData.InfoText = contentBuilder.ToString().Trim();
    profiles[currentProfile] = currentData;
     }

            currentProfile = line.Substring(1, line.Length - 2);
         currentData = new InfoProfile();
contentBuilder.Clear();
   currentSection = "info";
     }
       else if (line.Equals("[header]", StringComparison.OrdinalIgnoreCase))
     {
           if (currentSection == "info" && currentData != null)
          currentData.InfoText = contentBuilder.ToString().Trim();
       contentBuilder.Clear();
      currentSection = "header";
       }
     else if (line.Equals("[image]", StringComparison.OrdinalIgnoreCase))
     {
  if (currentSection == "header" && currentData != null)
     currentData.HeaderText = contentBuilder.ToString().Trim();
    else if (currentSection == "info" && currentData != null)
              currentData.InfoText = contentBuilder.ToString().Trim();
               contentBuilder.Clear();
    currentSection = "image";
    }
       else if (line.Equals("[info]", StringComparison.OrdinalIgnoreCase))
        {
               if (currentSection == "header" && currentData != null)
        currentData.HeaderText = contentBuilder.ToString().Trim();
  else if (currentSection == "image" && currentData != null)
   currentData.PngPath = contentBuilder.ToString().Trim();
  contentBuilder.Clear();
     currentSection = "info";
        }
     else if (currentProfile != null)
         {
          if (contentBuilder.Length > 0) contentBuilder.AppendLine();
         contentBuilder.Append(line);
         }
             }

          // Save last profile
    if (currentProfile != null && currentData != null)
     {
        switch (currentSection)
         {
    case "header":
    currentData.HeaderText = contentBuilder.ToString().Trim();
   break;
       case "image":
      currentData.PngPath = contentBuilder.ToString().Trim();
     break;
       case "info":
     currentData.InfoText = contentBuilder.ToString().Trim();
       break;
            }
           profiles[currentProfile] = currentData;
        }
          }
   catch (Exception ex)
     {
 Debug.LogWarning($"[InfoNpcScreenController] Failed to load profiles: {ex.Message}");
            }

         return profiles;
        }

     private static void WriteAllProfilesToFile(string filePath, Dictionary<string, InfoProfile> profiles)
        {
   var sb = new StringBuilder();
            foreach (var kvp in profiles)
            {
sb.AppendLine($"[{kvp.Key}]");

     if (!string.IsNullOrEmpty(kvp.Value.HeaderText))
  {
         sb.AppendLine("[header]");
          sb.AppendLine(kvp.Value.HeaderText);
  }

   if (!string.IsNullOrEmpty(kvp.Value.PngPath))
    {
      sb.AppendLine("[image]");
            sb.AppendLine(kvp.Value.PngPath);
 }

      sb.AppendLine("[info]");
         sb.AppendLine(kvp.Value.InfoText);
         sb.AppendLine();
        }
        File.WriteAllText(filePath, sb.ToString());
 }

        #endregion
    }
}
