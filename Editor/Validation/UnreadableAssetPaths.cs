// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>The files a scan could not open, and how a report says so.</summary>
    /// <remarks>
    /// A file the scan could not read is a hole in the measurement, not a defect in the asset, so
    /// it is never a finding: mixing the two would make a finding mean two things and a caller
    /// could no longer read "no findings" as "nothing is wrong". One locked file in a project of
    /// four thousand is the realistic shape, and the subject counts cannot catch it because the
    /// scan still read almost everything.
    /// </remarks>
    /// <remarks>
    /// A permission error, a lock, a delete between enumeration and read, and an asset Unity wrote
    /// as binary each leave the same hole. The last is permanent -- <c>LightingData.asset</c> is
    /// binary whatever the serialization mode says, measured on two of two under <c>ForceText</c>
    /// -- which is why the set prints without raising the severity on its own.
    /// </remarks>
    internal static class UnreadableAssetPaths
    {
        /// <summary>Sorts <paramref name="unreadable"/> and drops the repeats, in place.</summary>
        /// <param name="unreadable">The asset paths a scan could not read.</param>
        /// <remarks>A report a reader diffs against the previous run has to be stable.</remarks>
        internal static void SortAndDeduplicate(List<string> unreadable)
        {
            if (unreadable == null || unreadable.Count <= 1)
            {
                return;
            }

            unreadable.Sort(StringComparer.Ordinal);

            int kept = 1;
            for (int index = 1; index < unreadable.Count; ++index)
            {
                if (
                    string.Equals(unreadable[index], unreadable[kept - 1], StringComparison.Ordinal)
                )
                {
                    continue;
                }

                unreadable[kept] = unreadable[index];
                ++kept;
            }

            unreadable.RemoveRange(kept, unreadable.Count - kept);
        }

        /// <summary>Appends the unreadable set to <paramref name="message"/>, when there is one.</summary>
        /// <param name="message">The report being built.</param>
        /// <param name="unreadable">The asset paths a scan could not read.</param>
        internal static void Append(StringBuilder message, IReadOnlyList<string> unreadable)
        {
            if (message == null || unreadable == null || unreadable.Count <= 0)
            {
                return;
            }

            message
                .AppendLine()
                .Append("  ")
                .Append(unreadable.Count)
                .Append(" file(s) could not be read, so this run did not see all of them:");

            for (int index = 0; index < unreadable.Count; ++index)
            {
                message.AppendLine().Append("    ").Append(unreadable[index]);
            }
        }
    }
#endif
}
