// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;

    /// <summary>
    /// Defines sorting algorithms available for list operations.
    /// </summary>
    public enum SortAlgorithm
    {
        /// <summary>Invalid sorting algorithm placeholder.</summary>
        [Obsolete("Please use a valid SortAlgorithm")]
        None = 0,

        /// <summary>Ghost sort algorithm - adaptive sorting with caching optimizations. Not stable.</summary>
        Ghost = 1,

        /// <summary>Insertion sort algorithm - efficient for small or nearly-sorted lists. Stable.</summary>
        Insertion = 2,

        /// <summary>Meteor sort algorithm - adaptive gap-based sorting variant. Not stable.</summary>
        Meteor = 3,

        /// <summary>Pattern-defeating quicksort - adaptive quicksort with pattern detection. Not stable.</summary>
        PatternDefeatingQuickSort = 4,

        /// <summary>Grail sort algorithm - mergesort leveraging pooled buffers. Stable.</summary>
        Grail = 5,

        /// <summary>Power sort algorithm - adaptive mergesort that exploits natural runs. Stable.</summary>
        Power = 6,

        /// <summary>Tim sort algorithm - hybrid run-detecting mergesort popularized by Python/Java. Stable.</summary>
        Tim = 7,

        /// <summary>Jesse sort algorithm - dual-patience sort hybrid inspired by Jesse Michel’s research. Not stable.</summary>
        Jesse = 8,

        /// <summary>Green sort algorithm - symmetric merge strategy inspired by greeNsort sustainability work. Stable.</summary>
        Green = 9,

        /// <summary>Ska sort algorithm - multi-pivot quicksort adapted from Malte Skarupke’s research. Not stable.</summary>
        Ska = 10,

        /// <summary>Ipn sort algorithm - in-place, adaptive quicksort variant from Voultapher’s research. Not stable.</summary>
        Ipn = 11,

        /// <summary>Smooth sort algorithm - weak-heap/smoothsort hybrid optimized for presorted data. Not stable.</summary>
        Smooth = 12,

        /// <summary>Block merge sort (WikiSort-style) - low-buffer mergesort. Stable.</summary>
        Block = 13,

        /// <summary>IPS4o samplesort - cache-efficient multi-way samplesort. Not stable.</summary>
        Ips4o = 14,

        /// <summary>Power sort plus - enhanced run-priority mergesort inspired by Wild & Nebel. Stable.</summary>
        PowerPlus = 15,

        /// <summary>Glide sort - galloping merges inspired by Rust glidesort. Stable.</summary>
        Glide = 16,

        /// <summary>Flux sort - pattern-defeating dual-pivot quicksort from sort-research. Not stable.</summary>
        Flux = 17,

        /// <summary>Yam sort - bisection mergesort that adapts to sequential data. Stable.</summary>
        Yam = 18,
    }
}
