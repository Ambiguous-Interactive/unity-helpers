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

        /// <summary>Ghost sort algorithm - adaptive sorting with caching optimizations.</summary>
        Ghost = 1,

        /// <summary>Insertion sort algorithm - efficient for small or nearly-sorted lists.</summary>
        Insertion = 2,

        /// <summary>Meteor sort algorithm - adaptive gap-based sorting variant.</summary>
        Meteor = 3,

        /// <summary>Pattern-defeating quicksort - adaptive quicksort with pattern detection.</summary>
        PatternDefeatingQuickSort = 4,

        /// <summary>Grail sort algorithm - stable mergesort leveraging pooled buffers.</summary>
        Grail = 5,

        /// <summary>Power sort algorithm - adaptive mergesort that exploits natural runs.</summary>
        Power = 6,

        /// <summary>Tim sort algorithm - hybrid stable run-detecting mergesort popularized by Python/Java.</summary>
        Tim = 7,

        /// <summary>Jesse sort algorithm - dual-patience sort hybrid inspired by Jesse Michel’s research.</summary>
        Jesse = 8,

        /// <summary>Green sort algorithm - symmetric merge strategy inspired by greeNsort sustainability work.</summary>
        Green = 9,

        /// <summary>Ska sort algorithm - multi-pivot quicksort adapted from Malte Skarupke’s research.</summary>
        Ska = 10,

        /// <summary>Ipn sort algorithm - in-place, adaptive quicksort variant from Voultapher’s research.</summary>
        Ipn = 11,

        /// <summary>Smooth sort algorithm - weak-heap/smoothsort hybrid optimized for presorted data.</summary>
        Smooth = 12,

        /// <summary>Block merge sort (WikiSort-style) - stable low-buffer mergesort.</summary>
        Block = 13,

        /// <summary>IPS4o samplesort - cache-efficient multi-way samplesort.</summary>
        Ips4o = 14,

        /// <summary>Power sort plus - enhanced run-priority mergesort inspired by Wild & Nebel.</summary>
        PowerPlus = 15,

        /// <summary>Glide sort - stable galloping merges inspired by Rust glidesort.</summary>
        Glide = 16,

        /// <summary>Flux sort - pattern-defeating dual-pivot quicksort from sort-research.</summary>
        Flux = 17,

        /// <summary>Yam sort - stable bisection mergesort that adapts to sequential data.</summary>
        Yam = 18,
    }
}
