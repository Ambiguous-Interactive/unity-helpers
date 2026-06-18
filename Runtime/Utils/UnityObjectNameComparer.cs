// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public sealed class UnityObjectNameComparer<T> : IComparer<T>
        where T : UnityEngine.Object
    {
        private static readonly Regex TrailingNumberRegex = new(
            @"^(.*?)(\d+)$",
            RegexOptions.Compiled
        );
        public static readonly UnityObjectNameComparer<T> Instance = new();

        private UnityObjectNameComparer() { }

        public int Compare(T x, T y)
        {
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
            Match matchA = TrailingNumberRegex.Match(nameA);
            Match matchB = TrailingNumberRegex.Match(nameB);

            // If both have trailing numbers, compare prefix then numeric
            if (matchA.Success && matchB.Success)
            {
                string prefixA = matchA.Groups[1].Value;
                string prefixB = matchB.Groups[1].Value;

                int prefixCompare = string.Compare(
                    prefixA,
                    prefixB,
                    StringComparison.OrdinalIgnoreCase
                );
                if (prefixCompare != 0)
                {
                    return prefixCompare;
                }

                // same prefix → compare parsed integers
                int numA = int.Parse(matchA.Groups[2].Value);
                int numB = int.Parse(matchB.Groups[2].Value);
                return numA.CompareTo(numB);
            }

            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }
    }
}
