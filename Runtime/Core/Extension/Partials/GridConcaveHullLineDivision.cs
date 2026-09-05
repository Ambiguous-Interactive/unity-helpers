// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using UnityEngine;

    /// <summary>
    /// Legacy line-division concave hull entry point (retired).
    /// This algorithm has been removed; use <see cref="ConcaveHullStrategy.EdgeSplit"/> instead.
    /// </summary>
    public static partial class UnityExtensions
    {
        [Obsolete(
            "The line-division concave hull is retired due to instability. Use ConcaveHullStrategy.EdgeSplit instead."
        )]
        public static List<FastVector3Int> BuildConcaveHull(
            this IEnumerable<FastVector3Int> gridPositions,
            Grid grid,
            float scaleFactor = 1,
            float concavity = 0f
        )
        {
            throw new NotSupportedException(
                "The line-division concave hull algorithm has been removed; switch to ConcaveHullStrategy.EdgeSplit."
            );
        }
    }
}
