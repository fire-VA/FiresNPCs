using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using TMPro;

namespace FiresRPGmaker.UI.Screens
{
    // Partial implementation to hold fashion dropdown population and helpers.
    public partial class DressingRoomScreenController
    {
        // Default hair items (captured from log scan) - used to populate hair dropdown
        private static readonly string[] DefaultHairItems = new[] {
            "HairNone",
            "Hair3","Hair4","Hair5","Hair6","Hair7","Hair8","Hair9","Hair10",
            "Hair11","Hair12","Hair13","Hair14","Hair15","Hair16","Hair17","Hair18","Hair19","Hair20",
            "Hair21","Hair22","Hair23","Hair24","Hair25","Hair26","Hair27","Hair28","Hair29","Hair30",
            "Hair31","Hair32","Hair33","Hair34"
        };

        // Default beard items (captured from log scan) - used to populate beard dropdown
        private static readonly string[] DefaultBeardItems = new[] {
            "BeardNone","Beard1","Beard2","Beard3","Beard4","Beard5","Beard6","Beard7","Beard8",
         "Beard9","Beard10","Beard11","Beard12","Beard13","Beard14","Beard15","Beard16",
            "Beard17","Beard18","Beard19","Beard20","Beard21","Beard22","Beard23","Beard24",
       "Beard25","Beard26"
        };

        // Deny list for animations � pre-lowercased for fast Contains() checks
        private static readonly string[] DeniedAnimationsLower = {
            // Combat/Attack animations
            "attack", "attack1", "attack2", "attack3", "swing", "slash", "stab", "thrust",
            // Death animations
            "death", "die", "dead", "dying",
            // Monster-specific animations
            "roar", "growl", "summon", "spawn", "bite", "claw", "charge", "pounce",
            "ram", "scream", "taunt", "stomp", "breath", "burrow", "slither",
            // Projectile/Magic animations
            "throw", "bomb", "explode", "cast", "fire", "shoot", "projectile",
            // Damage/Stagger animations
            "stagger", "hit", "hurt", "pain", "knockback",
            // Environmental animations
            "drown", "fall", "swim", "fly", "takeoff", "land",
            // Object animations (not character animations)
            "incinerator", "armature", "open_", "closing", "closed", "hold the mast",
            // Misc unwanted
            "poof"
        };

        // Cached filtered animation options per NPC instance ID � avoids rebuilding 4� per Show()
        private static int _cachedAnimNpcId;
        private static List<string> _cachedAnimOptions;

        // Public helper to populate dropdowns for a given NPC (keeps main class smaller)
        public void PopulateDropdowns(NpcController npc)
        {
            if (npc == null) return;

            // Build the filtered animation option list once per NPC, then reuse for all 4 dropdowns
            var animOptions = GetCachedAnimationOptions(npc);
            ApplyAnimationOptions(_periodicAnimationDropdown, animOptions);
            ApplyAnimationOptions(_interactAnimationDropdown, animOptions);
            ApplyAnimationOptions(_greetAnimationDropdown, animOptions);
            ApplyAnimationOptions(_byeAnimationDropdown, animOptions);

            PopulateSoundDropdown(_periodicSoundDropdown, npc);

            PopulateHairDropdown(_hairItemDropdown);
            PopulateBeardDropdown(_beardItemDropdown);
        }

        /// <summary>
        /// Checks if an animation should be denied based on the deny list.
        /// Returns true if the animation is suitable (NOT denied).
        /// </summary>
        private static bool IsAnimationSuitable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant().Trim();
            for (int i = 0; i < DeniedAnimationsLower.Length; i++)
            {
                if (lower.Contains(DeniedAnimationsLower[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Builds (and caches) the filtered animation option list for the given NPC.
        /// Called once per Show(), then reused for all 4 animation dropdowns.
        /// </summary>
        private static List<string> GetCachedAnimationOptions(NpcController npc)
        {
            int npcId = npc.GetInstanceID();
            if (_cachedAnimOptions != null && _cachedAnimNpcId == npcId)
                return _cachedAnimOptions;

            var options = new List<string> { "(none)" };
            var seen = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                var animator = npc.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    var controller = animator.runtimeAnimatorController;
                    if (controller?.animationClips != null)
                    {
                        foreach (var clip in controller.animationClips)
                        {
                            var clipName = clip?.name;
                            if (string.IsNullOrEmpty(clipName)) continue;
                            if (!seen.Add(clipName)) continue;
                            if (!IsAnimationSuitable(clipName)) continue;
                            options.Add(clipName);
                        }
                    }

                    foreach (var p in animator.parameters)
                    {
                        if (!string.IsNullOrEmpty(p.name) && seen.Add(p.name))
                            options.Add(p.name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DressingRoom] PopulateAnimationDropdown error: {ex.Message}");
            }

            _cachedAnimNpcId = npcId;
            _cachedAnimOptions = options;
            return options;
        }

        /// <summary>
        /// Applies a pre-built animation option list to a single dropdown (no re-scanning).
        /// </summary>
        private static void ApplyAnimationOptions(TMP_Dropdown dropdown, List<string> options)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        private void PopulateSoundDropdown(TMP_Dropdown dropdown, NpcController npc)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            var options = new List<string> { "(none)" };

            try
            {
                var sources = npc?.GetComponentsInChildren<AudioSource>(true);
                if (sources != null)
                {
                    foreach (var s in sources)
                    {
                        var clip = s.clip;
                        if (clip != null && !options.Contains(clip.name))
                            options.Add(clip.name);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DressingRoom] PopulateSoundDropdown error: {ex.Message}");
            }

            dropdown.AddOptions(options);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        /// <summary>
        /// Populates the hair dropdown with the hardcoded list of hair items.
        /// </summary>
      private void PopulateHairDropdown(TMP_Dropdown dropdown)
      {
          if (dropdown == null) return;
          dropdown.ClearOptions();
          var options = new List<string>(DefaultHairItems.Length + 1) { "(none)" };
          foreach (var name in DefaultHairItems)
          {
              if (!string.IsNullOrEmpty(name))
                  options.Add(name);
          }
          dropdown.AddOptions(options);
          dropdown.value = 0;
          dropdown.RefreshShownValue();
      }

        /// <summary>
        /// Populates the beard dropdown with the hardcoded list of beard items.
        /// </summary>
      private void PopulateBeardDropdown(TMP_Dropdown dropdown)
      {
          if (dropdown == null) return;
          dropdown.ClearOptions();
          var options = new List<string>(DefaultBeardItems.Length + 1) { "(none)" };
          foreach (var name in DefaultBeardItems)
          {
              if (!string.IsNullOrEmpty(name))
                  options.Add(name);
          }
          dropdown.AddOptions(options);
          dropdown.value = 0;
          dropdown.RefreshShownValue();
      }

    /// <summary>
        /// Returns the list of available hair items.
        /// </summary>
      public List<string> GetAvailableHairItems()
 {
      return new List<string>(DefaultHairItems);
      }

    /// <summary>
        /// Returns the list of available beard items.
        /// </summary>
        public List<string> GetAvailableBeardItems()
        {
            return new List<string>(DefaultBeardItems);
        }
    }
}
