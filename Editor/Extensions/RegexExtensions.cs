// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Extensions
{
#if UNITY_EDITOR
    using System.Text.RegularExpressions;

    /// <summary>
    /// Named-group lookups that report a miss instead of hiding it.
    /// </summary>
    /// <remarks>
    /// <c>GroupCollection</c>'s string indexer never throws. A name the pattern does not declare --
    /// a typo, or a group the pattern above was renamed out of -- comes back as a non-participating
    /// <see cref="Group"/> whose <c>Value</c> is empty, so the bug reads as an ordinary miss
    /// forever. <c>GroupCollection.TryGetValue</c> would say so, but it arrived with .NET Core 3.0
    /// and is absent from the netstandard2.x surface Unity compiles against, so the group NUMBER is
    /// resolved first through <see cref="Regex.GroupNumberFromName(string)"/>, which answers a
    /// negative number for a name the pattern never declared.
    /// </remarks>
    internal static class RegexExtensions
    {
        /// <summary>
        /// Reads a named group out of a match.
        /// </summary>
        /// <param name="regex">The pattern that produced <paramref name="match"/>.</param>
        /// <param name="match">The match to read the group from.</param>
        /// <param name="groupName">Name of the group, as the pattern declares it.</param>
        /// <param name="group">The captured group, or null when the pattern declares no such group.</param>
        /// <returns>
        /// True when the pattern declares <paramref name="groupName"/> and that group participated
        /// in this match, false otherwise.
        /// </returns>
        internal static bool TryGetGroup(
            this Regex regex,
            Match match,
            string groupName,
            out Group group
        )
        {
            if (regex == null || match == null || string.IsNullOrEmpty(groupName))
            {
                group = null;
                return false;
            }

            int groupNumber = regex.GroupNumberFromName(groupName);
            if (groupNumber < 0)
            {
                group = null;
                return false;
            }

            group = match.Groups[groupNumber];
            return group.Success;
        }

        /// <summary>
        /// Reads a named group's text, answering the empty string when the group is optional and
        /// did not participate -- or when the pattern declares no group by that name.
        /// </summary>
        /// <param name="regex">The pattern that produced <paramref name="match"/>.</param>
        /// <param name="match">The match to read the group from.</param>
        /// <param name="groupName">Name of the group, as the pattern declares it.</param>
        /// <returns>The captured text, or the empty string.</returns>
        internal static string GroupValueOrEmpty(this Regex regex, Match match, string groupName)
        {
            return regex.TryGetGroup(match, groupName, out Group group)
                ? group.Value
                : string.Empty;
        }
    }
#endif
}
