// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;

    internal sealed class StatisticsDerivedList<T> : List<T>, IReadOnlyList<T>
    {
        T IReadOnlyList<T>.this[int index]
        {
            get
            {
                throw new InvalidOperationException(
                    "The list snapshot must bypass the interface indexer."
                );
            }
        }

        internal StatisticsDerivedList(IEnumerable<T> values)
            : base(values) { }
    }
}
