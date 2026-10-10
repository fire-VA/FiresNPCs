using System.Text;

namespace FiresRPGmaker.Modules.Dialogues
{
    /// <summary>
    /// Derives the stable per-option cooldown key used by the server cooldown store.
    /// A frozen <see cref="RawPlayerOption.CooldownKey"/> always wins; otherwise the key
    /// is a run-stable FNV-1a hash of the option's identity tuple so it does not drift
    /// the way <c>string.GetHashCode</c> would across runs.
    /// </summary>
    public static class DialogueKeyUtil
    {
        public static string OptionKey(string dialogueUID, RawPlayerOption opt)
        {
            if (opt != null && !string.IsNullOrEmpty(opt.CooldownKey))
                return opt.CooldownKey;

            string identity = BuildIdentity(dialogueUID, opt);
            return (dialogueUID ?? string.Empty) + "#" + Fnv1aHex(identity);
        }

        private static string BuildIdentity(string dialogueUID, RawPlayerOption opt)
        {
            var sb = new StringBuilder();
            sb.Append(dialogueUID ?? string.Empty);
            if (opt != null)
            {
                sb.Append(opt.Cooldown);
                sb.Append(opt.CooldownSeconds);
                if (opt.Commands != null)
                    sb.Append(string.Join(string.Empty, opt.Commands));
            }
            return sb.ToString();
        }

        private const uint FnvOffsetBasis = 2166136261;
        private const uint FnvPrime = 16777619;

        private static string Fnv1aHex(string text)
        {
            uint hash = FnvOffsetBasis;
            if (text != null)
            {
                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= FnvPrime;
                }
            }
            return hash.ToString("x8");
        }
    }
}
