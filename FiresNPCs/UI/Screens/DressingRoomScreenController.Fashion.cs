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
        // Player customization prefab names — the SAME pool vanilla character-creation
        // shows. Mirrored by KnownHairStyles / KnownBeardStyles in
        // FiresCore.Npc.CompanionRandomLoadout; keep the two in sync.
        //
        // Validated 2026-06-13 against the Unity project at
        // <c>NewProject2026/Fires2026Project/Assets/GameElements/Items/customizations/{hairs,beards}/</c>:
        // 37 hair prefabs (Hair1..Hair37) + HairNone; 26 beard prefabs (Beard1..Beard26) + BeardNone.
        // Old list was Hair3..Hair34 — restored Hair1/2 + added Mistlands-era Hair35/36/37.
        private static readonly string[] DefaultHairItems = new[] {
            "HairNone",
            "Hair1","Hair2","Hair3","Hair4","Hair5","Hair6","Hair7","Hair8","Hair9","Hair10",
            "Hair11","Hair12","Hair13","Hair14","Hair15","Hair16","Hair17","Hair18","Hair19","Hair20",
            "Hair21","Hair22","Hair23","Hair24","Hair25","Hair26","Hair27","Hair28","Hair29","Hair30",
            "Hair31","Hair32","Hair33","Hair34","Hair35","Hair36","Hair37"
        };

        private static readonly string[] DefaultBeardItems = new[] {
            "BeardNone",
            "Beard1","Beard2","Beard3","Beard4","Beard5","Beard6","Beard7","Beard8",
            "Beard9","Beard10","Beard11","Beard12","Beard13","Beard14","Beard15","Beard16",
            "Beard17","Beard18","Beard19","Beard20","Beard21","Beard22","Beard23","Beard24",
            "Beard25","Beard26"
        };

        // Animation curation for the NPC dressing room.
        //
        // <para><b>Why curate at all.</b> Companion / static-NPC prefabs share the vanilla
        // Player.controller — 100+ clips spanning combat, death, projectile, swimming,
        // riding, climbing, weapon-handling, etc. None of those read as a stationed-NPC
        // "behavior". The dropdowns (periodic / interact / greet / bye) need to show only
        // the friendly idles + emotes the player normally triggers via the social menu.</para>
        //
        // <para><b>Filter shape.</b> Two stages:</para>
        // <list type="number">
        //   <item><description><b>Substring deny</b> — any of <see cref="DeniedAnimationSubstrings"/>
        //     appearing anywhere in the lowercased clip name disqualifies it. Catches
        //     concatenated names like "BombThrow" / "SpearThrow" that token splitting alone
        //     would miss.</description></item>
        //   <item><description><b>Token allow</b> — the clip's tokens (split on space / underscore /
        //     dash / dot / '@' / parens) must include at least ONE entry from
        //     <see cref="AllowedAnimationTokens"/>. Default-deny: unknown clips are filtered.
        //     Tokenizing prevents the old substring-allow bug where "Land" denied
        //     "LayOfTheLand".</description></item>
        // </list>
        //
        // <para>The lists were built from the Unity project's player-model animation folders
        // (NewProject2026/Fires2026Project/Assets/Characters/Player/model/{Emotes,Sit,axe,
        // battleaxe,bow,...}_anim/) and the live FBX clip dump.</para>
        private static readonly string[] DeniedAnimationSubstrings = {
            // Combat — attacks, weapon swings, combos
            "attack", "swing", "slash", "stab", "thrust", "punch", "kick", "combo",
            // Death / damage / stagger
            "death", "die", "dead", "dying", "stagger", "hit", "hurt", "pain", "knockback", "stun",
            // Movement — NPCs don't walk/run/jog/jump on a periodic timer
            "walk", "walking", "jog", "run", "running", "sprint",
            "jump", "leap", "fall", "falling", "landing",
            "sneak", "crouch", "crawl", "roll", "dodge", "dive",
            "treading", "swim", "swimming", "drown",
            // Climbing
            "climb", "climbing", "ladder", "vault",
            // Riding / mounts
            "ride", "riding", "mount", "dismount", "saddle", "lox", "asksvin",
            // Weapons / equipment in use
            "bow", "crossbow", "spear", "spearthrow", "bombthrow", "throw", "shoot", "reload", "aim",
            "sword", "knife", "knives", "atgeir", "mace", "scythe", "pickaxe",
            "hammer_swing", "hoe_swing", "axe_swing",
            "block", "parry",
            // Magic / projectiles
            "magic", "cast", "spell", "channel", "weave", "projectile", "bomb",
            // Monster animations (in case anyone wires up a creature animator)
            "roar", "growl", "summon", "spawn", "bite", "claw", "charge", "pounce",
            "ram", "scream", "stomp", "breath", "burrow", "slither",
            // Object / rig animations (not character behavior)
            "incinerator", "armature", "open_", "closing", "closed", "mast", "poof",
            "intro", "outro", "transform", "morph", "t-pose", "t_pose",
        };

        private static readonly HashSet<string> AllowedAnimationTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Idles — foundation of "doing nothing in particular"
            "idle", "stand", "standing", "warrior", "bouncing", "torch",
            // Resting / sitting / sleeping
            "sit", "sitting",
            "sleep", "sleeping", "lay", "laying",
            "rest", "resting",
            // Eating / drinking — food-station and cooking NPCs
            "eat", "eating", "drink", "drinking",
            // Greeting / social emotes
            "wave", "waving", "cheer", "clap", "pray", "praying", "stretch",
            "salute", "shrug", "nod", "thumbsup", "point", "talk", "talking",
            "greet", "hello", "goodbye", "bye",
            // Dance / pose
            "dance", "dancing", "pose", "model", "showcase",
            // Friendly work poses (the pose, not the violent swing)
            "smith", "cook", "smelt", "forge", "operate", "pickup", "crafting",
            // Filler reactions
            "look", "looking", "think", "thinking", "ponder",
            // Mood
            "sad", "happy", "angry", "scared",
            // Equip / ready poses (weapon at the hip, harmless)
            "equip", "ready",
            // Emotes / taunts (Player_taunts in vanilla is a friendly emote set, not combat)
            "emote", "emotes", "taunt", "taunts",
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

        // Cached token splitters — reused per clip-name suitability check to avoid
        // allocating a fresh char[] every call (the dropdown rebuild scans 100+ clips).
        private static readonly char[] AnimationTokenSeparators = { ' ', '_', '-', '.', '@', '(', ')' };

        /// <summary>
        /// Two-stage filter for player-NPC animation suitability. See the
        /// <see cref="DeniedAnimationSubstrings"/> and <see cref="AllowedAnimationTokens"/>
        /// docs for the rationale.
        /// </summary>
        private static bool IsAnimationSuitable(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            // Substring deny — catches CamelCase concatenations (e.g. "BombThrow") that
            // tokenization alone would miss.
            string lower = name.ToLowerInvariant();
            for (int i = 0; i < DeniedAnimationSubstrings.Length; i++)
            {
                if (lower.Contains(DeniedAnimationSubstrings[i])) return false;
            }

            // Token allow — must include at least one curated player-friendly token.
            // Default-deny: an unknown clip with no matching token stays hidden.
            var tokens = name.Split(AnimationTokenSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (AllowedAnimationTokens.Contains(tokens[i])) return true;
            }
            return false;
        }

        // The legit player emotes — the only animation names that actually fire on a player-derived
        // Animator (each is an "emote_<name>" parameter; "sit" is the only looping one). A model-overridden
        // monster NPC (Draugr/Lox/...) has none of these, so its dropdowns correctly come up empty.
        private static readonly string[] PlayerEmotes =
        {
            "wave","sit","challenge","cheer","nonono","thumbsup","point","blowkiss",
            "bow","cower","cry","despair","flex","comehere","headbang","kneel",
            "laugh","roar","shrug","dance","relax","toast","rest","vibe","loveyou"
        };

        /// <summary>
        /// Builds (and caches) the valid emote list for the NPC: the curated player emotes intersected
        /// with the emote_* params actually present on this NPC's Animator (ground-truth that the emote
        /// will play). The full list is offered only when the Animator can't be read at all; an Animator
        /// with no emote params (a monster model override) yields just "(none)". Invalidated on model swap.
        /// </summary>
        public static void InvalidateAnimationCache() { _cachedAnimOptions = null; }

        private static List<string> GetCachedAnimationOptions(NpcController npc)
        {
            int npcId = npc.GetInstanceID();
            if (_cachedAnimOptions != null && _cachedAnimNpcId == npcId)
                return _cachedAnimOptions;

            var options = new List<string> { "(none)" };
            try
            {
                var animator = npc.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    // Animator unreadable at populate time — offer the full curated set rather than nothing.
                    foreach (var name in PlayerEmotes) options.Add(name);
                }
                else
                {
                    var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in animator.parameters)
                        if (p.name != null && p.name.StartsWith("emote_") && p.name != "emote_stop")
                            present.Add(p.name.Substring("emote_".Length));
                    // Animator read fine: offer only the emotes it can actually fire (empty if a monster model).
                    foreach (var name in PlayerEmotes)
                        if (present.Contains(name)) options.Add(name);
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

            // Custom mp3 sounds loaded from BepInEx/config/FiresNPCs_Sounds (KG-style).
            foreach (var key in FiresCore.Audio.FiresCustomAudioStore.Clips.Keys)
                if (!options.Contains(key)) options.Add(key);

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
