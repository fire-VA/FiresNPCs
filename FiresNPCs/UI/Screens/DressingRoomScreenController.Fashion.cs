using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using FiresCore.Npc.Animation;

namespace FiresRPGmaker.UI.Screens
{
    // Partial implementation to hold fashion dropdown population and helpers.
    public partial class DressingRoomScreenController
    {

        // Each animation dropdown lists the player-rig animations that suit it, from Core's PlayerAnimationCatalog:
        // one-shots for greet, bye and interact; one-shots plus held poses for the periodic idle. Options show the
        // label, the NPC stores the catalog key, and a saved value the list does not know stays as an extra option
        // so an Apply never erases it.
        private readonly Dictionary<TMP_Dropdown, List<string>> _animationValues = new Dictionary<TMP_Dropdown, List<string>>();

        public void PopulateDropdowns(NpcController npc)
        {
            if (npc == null) return;

            var animator = npc.GetComponentInChildren<Animator>(true);
            PopulateAnimationDropdown(_periodicAnimationDropdown, PlayerAnimUse.Idle, animator);
            PopulateAnimationDropdown(_interactAnimationDropdown, PlayerAnimUse.Interact, animator);
            PopulateAnimationDropdown(_greetAnimationDropdown, PlayerAnimUse.Greet, animator);
            PopulateAnimationDropdown(_byeAnimationDropdown, PlayerAnimUse.Bye, animator);

            PopulateSoundDropdown(_periodicSoundDropdown, npc);

            PopulateHairDropdown(_hairItemDropdown);
            PopulateBeardDropdown(_beardItemDropdown);
        }

        /// <summary>A model override without the player rig (a creature animator) gets only "(none)"; an animator that
        /// cannot be read yet gets the full list for that use.</summary>
        private void PopulateAnimationDropdown(TMP_Dropdown dropdown, PlayerAnimUse use, Animator animator)
        {
            if (dropdown == null) return;
            var values = new List<string> { NoneOptionText };
            var labels = new List<string> { NoneOptionText };
            bool readable = animator != null && animator.runtimeAnimatorController != null;
            foreach (var anim in PlayerAnimationCatalog.For(use))
            {
                if (readable && !PlayerAnimationCatalog.Has(animator, anim)) continue;
                values.Add(anim.Key);
                labels.Add(anim.Label);
            }
            _animationValues[dropdown] = values;
            dropdown.ClearOptions();
            dropdown.AddOptions(labels);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        private void SelectAnimationOption(TMP_Dropdown dropdown, string saved)
        {
            if (dropdown == null || string.IsNullOrEmpty(saved) || saved == NoneOptionText) return;
            if (!_animationValues.TryGetValue(dropdown, out var values)) return;
            var anim = PlayerAnimationCatalog.Find(saved);
            int index = anim != null ? values.IndexOf(anim.Key) : -1;
            if (index < 0)
            {
                values.Add(saved);
                dropdown.AddOptions(new List<string> { saved });
                index = values.Count - 1;
            }
            dropdown.value = index;
            dropdown.RefreshShownValue();
        }

        private string SelectedAnimationValue(TMP_Dropdown dropdown)
        {
            if (dropdown == null) return null;
            if (!_animationValues.TryGetValue(dropdown, out var values) || dropdown.value >= values.Count)
                return dropdown.options[dropdown.value].text;
            return values[dropdown.value];
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

            // 0.0.546 ([visual]'s snd2 "no clip"): PlaySound already falls back to any loaded game clip by name
            // (ResolveClipCached), but the dropdown only listed the NPC's own and the custom clips, so it was empty on a rig
            // with no custom files. Add the loaded game clips, sorted, after the custom ones.
            var gameClips = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>())
                if (c != null && !string.IsNullOrEmpty(c.name) && !options.Contains(c.name)) gameClips.Add(c.name);
            options.AddRange(gameClips);
            global::FiresRPGmaker.FiresRPGmaker.Log?.LogDebug($"[DressingRoom] sound dropdown: {options.Count} option(s) ({FiresCore.Audio.FiresCustomAudioStore.Clips.Count} custom, {gameClips.Count} game clips)");

            dropdown.AddOptions(options);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        /// <summary>Fills a customization dropdown from the live barber pool, prefixed with the empty option.</summary>
        private void PopulateCustomizationDropdown(TMP_Dropdown dropdown, List<string> styles)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();

            var options = new List<string>(styles.Count + 1) { NoneOptionText };
            foreach (var name in styles)
            {
                if (!string.IsNullOrEmpty(name))
                    options.Add(name);
            }

            dropdown.AddOptions(options);
            dropdown.value = 0;
            dropdown.RefreshShownValue();
        }

        private void PopulateHairDropdown(TMP_Dropdown dropdown)
            => PopulateCustomizationDropdown(dropdown, GetAvailableHairItems());

        private void PopulateBeardDropdown(TMP_Dropdown dropdown)
            => PopulateCustomizationDropdown(dropdown, GetAvailableBeardItems());

        /// <summary>The vanilla barber's own pool, owned by Core so the loadout roller, the static-NPC randomiser
        /// and this dressing room all offer the same styles, modded ones included.</summary>
        public List<string> GetAvailableHairItems()
            => FiresCore.Npc.CompanionRandomLoadout.GetAvailableHairStyles();

        public List<string> GetAvailableBeardItems()
            => FiresCore.Npc.CompanionRandomLoadout.GetAvailableBeardStyles();
    }
}
