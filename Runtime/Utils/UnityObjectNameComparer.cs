// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Orders Unity Objects by name, treating a trailing run of digits as a number so that
    /// "Item2" sorts before "Item10".
    /// </summary>
    /// <typeparam name="T">The Object type being ordered.</typeparam>
    /// <remarks>
    /// Null and destroyed Objects order before live ones and never throw. A numeric suffix of any
    /// length is compared without being parsed, so a name ending in a timestamp orders correctly
    /// instead of overflowing.
    /// </remarks>
    public sealed class UnityObjectNameComparer<T> : IComparer<T>
        where T : UnityEngine.Object
    {
        /// <summary>The shared, stateless comparer instance.</summary>
        public static readonly UnityObjectNameComparer<T> Instance = new();

        private UnityObjectNameComparer() { }

        /// <summary>
        /// Compares two Objects by name, then by asset path, then by instance id.
        /// </summary>
        /// <param name="x">The left Object.</param>
        /// <param name="y">The right Object.</param>
        /// <returns>A negative value when <paramref name="x"/> orders first, positive when it orders last, zero when neither does.</returns>
        public int Compare(T x, T y)
        {
            // The Unity Object constraint ensures destroyed instances are rejected before reading their names.
            if (x == y)
            {
                return 0;
            }

            if (y == null)
            {
                return 1;
            }

            if (x == null)
            {
                return -1;
            }

            int comparison = CompareNatural(x.name, y.name);
            if (comparison != 0)
            {
                return comparison;
            }

#if UNITY_EDITOR
            comparison = string.Compare(
                AssetDatabase.GetAssetOrScenePath(x),
                AssetDatabase.GetAssetOrScenePath(y),
                StringComparison.OrdinalIgnoreCase
            );
#endif
            if (comparison == 0)
            {
                return x.GetUnityObjectId().CompareTo(y.GetUnityObjectId());
            }

            return comparison;
        }

        private static int CompareNatural(string nameA, string nameB)
        {
            int digitsA = TrailingDigitRunStart(nameA);
            int digitsB = TrailingDigitRunStart(nameB);

            if (digitsA < 0 || digitsB < 0)
            {
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            }

            int prefixComparison = nameA
                .AsSpan(0, digitsA)
                .CompareTo(nameB.AsSpan(0, digitsB), StringComparison.OrdinalIgnoreCase);
            if (prefixComparison != 0)
            {
                return prefixComparison;
            }

            return CompareDigitRuns(nameA, digitsA, nameB, digitsB);
        }

        // Only ASCII digits participate in numeric ordering; other Unicode digits remain text.
        private static int TrailingDigitRunStart(string name)
        {
            int index = name.Length;
            while (0 < index && IsAsciiDigit(name[index - 1]))
            {
                --index;
            }

            return index == name.Length ? -1 : index;
        }

        private static int CompareDigitRuns(string nameA, int startA, string nameB, int startB)
        {
            int significantA = SkipLeadingZeros(nameA, startA);
            int significantB = SkipLeadingZeros(nameB, startB);

            int lengthA = nameA.Length - significantA;
            int lengthB = nameB.Length - significantB;
            if (lengthA != lengthB)
            {
                return lengthA < lengthB ? -1 : 1;
            }

            return string.CompareOrdinal(nameA, significantA, nameB, significantB, lengthA);
        }

        private static int SkipLeadingZeros(string name, int start)
        {
            int index = start;
            int lastIndex = name.Length - 1;
            while (index < lastIndex && name[index] == '0')
            {
                ++index;
            }

            return index;
        }

        private static bool IsAsciiDigit(char character)
        {
            return character is >= '0' and <= '9';
        }
    }
}
