using System;
using System.Collections.Generic;
using UnityEngine;
using FiresRPGmaker.Modules.Vault;

namespace FiresRPGmaker.Services
{
    /// <summary>
  /// Centralized service for querying player inventory and item information.
    /// This service can be used by quest systems, marketplace, trading, crafting UI, etc.
    /// </summary>
    public static class PlayerInventoryService
    {
        #region Item Display Names

        /// <summary>
        /// Gets the localized display name for an item prefab.
        /// Falls back to the prefab name if not found.
        /// </summary>
 /// <param name="prefabName">The internal prefab name (e.g., "SwordIron")</param>
 /// <returns>The localized display name or the prefab name as fallback</returns>
        public static string GetItemDisplayName(string prefabName)
        {
     if (string.IsNullOrEmpty(prefabName)) return prefabName;

     try
    {
       // Try to get the item from ObjectDB
      if (ObjectDB.instance != null)
        {
            var itemPrefab = ObjectDB.instance.GetItemPrefab(prefabName);
          if (itemPrefab != null)
     {
             var itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop?.m_itemData?.m_shared != null)
             {
        string localizedName = itemDrop.m_itemData.m_shared.m_name;
   if (!string.IsNullOrEmpty(localizedName))
        {
           if (localizedName.StartsWith("$"))
   {
        string localized = Localization.instance?.Localize(localizedName);
              if (!string.IsNullOrEmpty(localized) && localized != localizedName)
     return localized;
    }
         else
       {
     return localizedName;
             }
        }
             }
   }
           }

                // Also try ZNetScene as fallback
         if (ZNetScene.instance != null)
           {
   var prefab = ZNetScene.instance.GetPrefab(prefabName);
                 if (prefab != null)
  {
             var itemDrop = prefab.GetComponent<ItemDrop>();
           if (itemDrop?.m_itemData?.m_shared != null)
      {
             string localizedName = itemDrop.m_itemData.m_shared.m_name;
 if (!string.IsNullOrEmpty(localizedName))
      {
        if (localizedName.StartsWith("$"))
     {
        string localized = Localization.instance?.Localize(localizedName);
         if (!string.IsNullOrEmpty(localized) && localized != localizedName)
     return localized;
        }
         else
          {
                 return localizedName;
     }
  }
   }
                 }
    }
            }
catch { }

    // Fallback: return the prefab name as-is
         return prefabName;
      }

        #endregion

        #region Inventory Counting

    /// <summary>
        /// Gets the count of an item in the local player's inventory by prefab name.
/// </summary>
   /// <param name="prefabName">The internal prefab name</param>
        /// <returns>The total count of the item in inventory</returns>
        public static int GetPlayerItemCount(string prefabName)
 {
            if (string.IsNullOrEmpty(prefabName)) return 0;

       int found = 0;
            try
            {
                var player = Player.m_localPlayer;
      if (player == null) return 0;

             var inventory = player.GetInventory();
  if (inventory != null)
       {
         // Try CountItems first
   found = inventory.CountItems(prefabName);

 // If nothing found, also check by prefab name directly
        if (found == 0)
             {
       var allItems = inventory.GetAllItems();
              if (allItems != null)
         {
   foreach (var item in allItems)
        {
      if (item?.m_dropPrefab != null)
          {
        string itemPrefabName = item.m_dropPrefab.name?.Replace("(Clone)", "").Trim();
                if (string.Equals(itemPrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
               {
                   found += item.m_stack;
            }
  }
          }
    }
         }
     }
 }
      catch { }

   return found;
        }

    /// <summary>
        /// Gets the count of an item by searching both prefab name and display name.
        /// This is useful when you only have the display name (from formatted UI text).
     /// Also checks the player's bank items.
        /// </summary>
        /// <param name="displayNameOrPrefab">Either the prefab name or display name</param>
        /// <returns>Total count from inventory and bank</returns>
      public static int GetPlayerItemCountByDisplayName(string displayNameOrPrefab)
        {
            if (string.IsNullOrEmpty(displayNameOrPrefab)) return 0;

       int found = 0;
   try
        {
         var player = Player.m_localPlayer;
     if (player == null) return 0;

     var inventory = player.GetInventory();
    if (inventory != null)
       {
    var allItems = inventory.GetAllItems();
      if (allItems != null)
        {
       foreach (var item in allItems)
            {
  if (item == null) continue;

              // Check by prefab name
        string itemPrefabName = item.m_dropPrefab?.name?.Replace("(Clone)", "").Trim();
  if (string.Equals(itemPrefabName, displayNameOrPrefab, StringComparison.OrdinalIgnoreCase))
           {
 found += item.m_stack;
     continue;
   }

       // Check by display name (m_shared.m_name or localized)
        if (item.m_shared != null)
     {
         string sharedName = item.m_shared.m_name;
             if (string.Equals(sharedName, displayNameOrPrefab, StringComparison.OrdinalIgnoreCase))
      {
     found += item.m_stack;
          continue;
        }

 // Try localized name
     if (!string.IsNullOrEmpty(sharedName) && sharedName.StartsWith("$"))
       {
string localized = Localization.instance?.Localize(sharedName);
       if (string.Equals(localized, displayNameOrPrefab, StringComparison.OrdinalIgnoreCase))
      {
   found += item.m_stack;
            continue;
}
            }
         }
   }
      }

                    // Also check bank (bank stores by prefab name)
  found += GetBankItemCount(player.GetPlayerID(), displayNameOrPrefab);
 }
            }
        catch { }

            return found;
        }

        /// <summary>
        /// Gets the count of an item in the player's bank.
      /// Checks both by prefab name and display name.
        /// </summary>
 /// <param name="playerId">The player's ID</param>
   /// <param name="prefabOrDisplayName">Either the prefab name or display name</param>
 /// <returns>Total count in bank</returns>
      public static int GetBankItemCount(long playerId, string prefabOrDisplayName)
        {
        if (playerId <= 0 || string.IsNullOrEmpty(prefabOrDisplayName)) return 0;

   int found = 0;
  try
     {
       var vault = VaultOfKnowledge.Instance;
                var playerData = vault?.GetPlayerData(playerId);
             if (playerData?.BankItems != null)
       {
           foreach (var item in playerData.BankItems)
   {
     if (item is StoredBankItem sb)
              {
          // Check by prefab name
   if (string.Equals(sb.Prefab, prefabOrDisplayName, StringComparison.OrdinalIgnoreCase))
 {
          found += sb.Amount;
       continue;
        }

                 // Check if the prefab's display name matches
         string itemDisplayName = GetItemDisplayName(sb.Prefab);
    if (string.Equals(itemDisplayName, prefabOrDisplayName, StringComparison.OrdinalIgnoreCase))
           {
     found += sb.Amount;
     }
        }
   }
       }
 }
       catch { }

            return found;
        }

      /// <summary>
 /// Checks if the player has at least the required amount of an item.
        /// Checks both inventory and bank.
        /// </summary>
        /// <param name="prefab">The item prefab name</param>
        /// <param name="requiredAmount">The required amount</param>
        /// <param name="playerId">Optional player ID (uses local player if not provided)</param>
        /// <param name="playerData">Optional pre-loaded player data</param>
        /// <returns>True if player has enough items</returns>
        public static bool HasItem(string prefab, int requiredAmount, long playerId = 0, PlayerVaultData playerData = null)
      {
     if (string.IsNullOrEmpty(prefab)) return false;

    int found = 0;
          var player = Player.m_localPlayer;

        if (player != null)
            {
         var inventory = player.GetInventory();
    if (inventory != null)
     {
     // Try CountItems first
           found += inventory.CountItems(prefab);

        // If nothing found, also check by prefab name directly
        if (found == 0)
   {
        var allItems = inventory.GetAllItems();
    if (allItems != null)
    {
      foreach (var item in allItems)
       {
       if (item?.m_dropPrefab != null)
           {
         string itemPrefabName = item.m_dropPrefab.name?.Replace("(Clone)", "").Trim();
          if (string.Equals(itemPrefabName, prefab, StringComparison.OrdinalIgnoreCase))
          {
                 found += item.m_stack;
               }
               }
         }
    }
    }
}

         if (playerId <= 0)
                playerId = player.GetPlayerID();
            }

            if (found >= requiredAmount) return true;

            // Check bank items
    if (playerData == null && playerId > 0)
            {
     try { playerData = VaultOfKnowledge.Instance?.GetPlayerData(playerId); } catch { playerData = null; }
   }

            if (playerData?.BankItems != null)
  {
       foreach (var item in playerData.BankItems)
       {
       if (item is StoredBankItem sb &&
  string.Equals(sb.Prefab, prefab, StringComparison.OrdinalIgnoreCase))
       {
            found += sb.Amount;
         }
     }
            }

   return found >= requiredAmount;
        }

        #endregion

        #region Inventory Listing

 /// <summary>
  /// Gets all items from the local player's inventory.
        /// </summary>
        /// <returns>List of inventory item info</returns>
      public static List<InventoryItemInfo> GetAllInventoryItems()
        {
 var items = new List<InventoryItemInfo>();

            try
            {
                var player = Player.m_localPlayer;
       if (player == null) return items;

  var inventory = player.GetInventory();
              if (inventory == null) return items;

   var allItems = inventory.GetAllItems();
      if (allItems == null) return items;

     foreach (var item in allItems)
      {
     if (item == null) continue;

      string prefabName = item.m_dropPrefab?.name?.Replace("(Clone)", "").Trim() ?? "Unknown";
              string displayName = GetItemDisplayName(prefabName);
        if (string.IsNullOrEmpty(displayName) || displayName == prefabName)
  {
 // Try to get from shared data
   if (item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name))
          {
            displayName = item.m_shared.m_name;
    if (displayName.StartsWith("$"))
  {
    string localized = Localization.instance?.Localize(displayName);
    if (!string.IsNullOrEmpty(localized))
             displayName = localized;
    }
      }
          }

     items.Add(new InventoryItemInfo
         {
       Prefab = prefabName,
            DisplayName = displayName,
   Amount = item.m_stack,
      Quality = item.m_quality,
          Durability = item.m_durability
        });
       }
          }
catch { }

            return items;
        }

        /// <summary>
        /// Gets all items from the player's bank.
        /// </summary>
    /// <param name="playerId">The player's ID (uses local player if 0)</param>
/// <returns>List of bank item info</returns>
    public static List<InventoryItemInfo> GetAllBankItems(long playerId = 0)
      {
   var items = new List<InventoryItemInfo>();

            try
            {
       if (playerId <= 0)
        playerId = Player.m_localPlayer?.GetPlayerID() ?? 0;

    if (playerId <= 0) return items;

       var vault = VaultOfKnowledge.Instance;
    var playerData = vault?.GetPlayerData(playerId);
   if (playerData?.BankItems == null) return items;

   foreach (var item in playerData.BankItems)
                {
           if (item is StoredBankItem sb)
          {
    items.Add(new InventoryItemInfo
        {
   Prefab = sb.Prefab,
          DisplayName = GetItemDisplayName(sb.Prefab),
               Amount = sb.Amount,
Quality = sb.Quality,
        Durability = sb.Durability
             });
           }
}
            }
   catch { }

  return items;
 }

        #endregion
    }

    /// <summary>
    /// Data transfer object representing an inventory item.
    /// Used for passing item information between services and UI.
    /// </summary>
    public class InventoryItemInfo
    {
        /// <summary>The internal prefab name</summary>
      public string Prefab { get; set; }

        /// <summary>The localized display name</summary>
     public string DisplayName { get; set; }

        /// <summary>The stack amount</summary>
        public int Amount { get; set; }

        /// <summary>The item quality level</summary>
        public int Quality { get; set; }

        /// <summary>The current durability (-1 if not applicable)</summary>
        public float Durability { get; set; } = -1f;
    }
}
