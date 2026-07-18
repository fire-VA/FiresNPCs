using UnityEngine;
using FiresRPGmaker.Managers;

namespace FiresRPGmaker.UI.QuestJournal
{
    /// <summary>
    /// Handles the Quest Journal hotkey input.
    /// This component is added to the game to listen for the configured hotkey.
    /// </summary>
    public class QuestJournalHotkeyHandler : MonoBehaviour
    {
        private static QuestJournalHotkeyHandler _instance;
        public static QuestJournalHotkeyHandler Instance => _instance;

        private void Awake()
     {
   if (_instance != null && _instance != this)
 {
Destroy(gameObject);
     return;
       }
    _instance = this;
   DontDestroyOnLoad(gameObject);
        }

     private void OnDestroy()
   {
if (_instance == this)
      _instance = null;
        }

   private void Update()
     {
     // Don't process hotkeys if certain conditions aren't met
      if (!CanProcessHotkey())
           return;

  // Check for Quest Journal hotkey
   var hotkeyConfig = ConfigManager.Instance?.configQuestJournalHotkey;
 if (hotkeyConfig == null || hotkeyConfig.Value == KeyCode.None)
             return;

   if (Input.GetKeyDown(hotkeyConfig.Value))
            {
      ToggleQuestJournal();
          }
     }

        /// <summary>
        /// Checks if we're in a state where hotkeys should be processed.
        /// </summary>
        private bool CanProcessHotkey()
        {
    // Must have a local player
  if (Player.m_localPlayer == null)
     return false;

  // Don't process if we're in a menu or chat is open
    if (Menu.IsVisible())
         return false;

          // Don't process if chat is focused
  if (Chat.instance != null && Chat.instance.HasFocus())
     return false;

       // Don't process if console is open
            if (Console.IsVisible())
          return false;

      // Don't process if any text input is focused
  if (TextInput.IsVisible())
     return false;

     // Don't process if the inventory or other standard UI is blocking
         if (InventoryGui.IsVisible())
   return false;

  // Don't process if store UI is open
         if (StoreGui.IsVisible())
   return false;

 return true;
 }

  /// <summary>
     /// Toggles the Quest Journal UI.
    /// </summary>
      private void ToggleQuestJournal()
        {
   try
   {
    global::FiresRPGmaker.UI.Books.QuestBookJournal.Toggle();
     }
  catch (System.Exception ex)
   {
  Debug.LogError($"[QuestJournalHotkeyHandler] Error toggling Quest Journal: {ex.Message}");
            }
        }

  /// <summary>
   /// Creates the hotkey handler if it doesn't exist.
   /// Called during game initialization.
        /// </summary>
        public static void EnsureCreated()
     {
  if (_instance != null)
          return;

     var go = new GameObject("QuestJournalHotkeyHandler");
           go.AddComponent<QuestJournalHotkeyHandler>();
            Debug.Log("[QuestJournalHotkeyHandler] Created hotkey handler");
        }
    }
}
