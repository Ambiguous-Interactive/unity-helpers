// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous.Rules
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Tells one rule's several findings on one asset apart, without naming a line number.
    /// </summary>
    /// <remarks>
    /// A discriminator is part of a finding's identity, so a suppression is recorded against it. A
    /// line number would change every time anything above the finding is edited, presenting an
    /// already-accepted finding as a new one; an occurrence count only changes when that asset's
    /// population of that exact problem changes, which is when a reviewer should look again.
    /// </remarks>
    internal static class ValidationDiscriminators
    {
        /// <summary>Returns <paramref name="key"/>, numbered when it has already been seen.</summary>
        /// <param name="counts">How many times each key has been used for the current asset.</param>
        /// <param name="key">What the finding is about: a field, a problem, a curve.</param>
        /// <returns>The key, or the key with a one-based occurrence suffix.</returns>
        internal static string Occurrence(Dictionary<string, int> counts, string key)
        {
            if (counts == null)
            {
                return key;
            }

            int seen = counts.TryGetValue(key, out int existing) ? existing : 0;
            counts[key] = seen + 1;
            return seen == 0 ? key : key + "#" + (seen + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
#endif
}
