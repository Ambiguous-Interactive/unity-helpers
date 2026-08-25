// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Coarse statistical quality ratings for RNG implementations.
    /// </summary>
    public enum RandomQuality
    {
        Unknown = 0,
        Excellent,
        VeryGood,
        Good,
        Fair,
        Poor,
        Experimental,
    }

    /// <summary>
    /// Describes statistical quality metadata that can be attached to <see cref="IRandom"/> implementations.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    [Preserve]
    public sealed class RandomGeneratorMetadataAttribute : Attribute
    {
        public RandomGeneratorMetadataAttribute(
            RandomQuality quality,
            string notes,
            string reference = "",
            string referenceUrl = "",
            string period = ""
        )
        {
            Quality = quality;
            Notes = notes ?? string.Empty;
            Reference = reference ?? string.Empty;
            ReferenceUrl = referenceUrl ?? string.Empty;
            Period = period ?? string.Empty;
        }

        public RandomQuality Quality { get; }

        public string Notes { get; }

        public string Reference { get; }

        public string ReferenceUrl { get; }

        /// <summary>
        /// The generator's output period, in the units a caller draws in.
        /// </summary>
        /// <remarks>
        /// Every value is a claim, and it says whose: a published period is quoted as its
        /// specification states it, and where none is published the value reports the measured live
        /// state width instead and labels it as measured. A period of 2^128 cannot be observed, so
        /// a bound that says what was actually seen is worth more than a number nobody checked.
        /// </remarks>
        public string Period { get; }

        public int QualitySortValue => (int)Quality;
    }

    /// <summary>
    /// Static helpers to retrieve metadata from RNG implementations.
    /// </summary>
    public static class RandomGeneratorMetadataRegistry
    {
        public static RandomGeneratorMetadata Snapshot(IRandom random)
        {
            if (random == null)
            {
                return RandomGeneratorMetadata.Empty;
            }

            return Snapshot(random.GetType());
        }

        public static RandomGeneratorMetadata Snapshot(Type randomType)
        {
            if (randomType == null)
            {
                return RandomGeneratorMetadata.Empty;
            }

            if (
                !ReflectionHelpers.TryGetAttributeSafe<RandomGeneratorMetadataAttribute>(
                    randomType,
                    out RandomGeneratorMetadataAttribute attribute,
                    inherit: false
                )
            )
            {
                return new RandomGeneratorMetadata(
                    randomType,
                    RandomQuality.Unknown,
                    "Not annotated.",
                    string.Empty,
                    string.Empty,
                    string.Empty
                );
            }

            return new RandomGeneratorMetadata(
                randomType,
                attribute.Quality,
                attribute.Notes,
                attribute.Reference,
                attribute.ReferenceUrl,
                attribute.Period
            );
        }
    }

    public readonly struct RandomGeneratorMetadata
    {
        public static readonly RandomGeneratorMetadata Empty = new(
            typeof(object),
            RandomQuality.Unknown,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        );

        public RandomGeneratorMetadata(
            Type type,
            RandomQuality quality,
            string notes,
            string reference,
            string referenceUrl,
            string period = ""
        )
        {
            Type = type;
            Quality = quality;
            Notes = notes ?? string.Empty;
            Reference = reference ?? string.Empty;
            ReferenceUrl = referenceUrl ?? string.Empty;
            Period = period ?? string.Empty;
        }

        public Type Type { get; }

        public RandomQuality Quality { get; }

        public string Notes { get; }

        public string Reference { get; }

        public string ReferenceUrl { get; }

        /// <summary>
        /// The generator's output period, or a measured state-width bound when none is published.
        /// </summary>
        public string Period { get; }

        /// <summary>
        /// <see cref="Period"/> for display, falling back to a label rather than an empty cell.
        /// </summary>
        public string PeriodLabel => string.IsNullOrWhiteSpace(Period) ? "Undeclared" : Period;

        public string QualityLabel
        {
            get
            {
                return Quality switch
                {
                    RandomQuality.Excellent => "Excellent",
                    RandomQuality.VeryGood => "Very Good",
                    RandomQuality.Good => "Good",
                    RandomQuality.Fair => "Fair",
                    RandomQuality.Poor => "Poor",
                    RandomQuality.Experimental => "Experimental",
                    _ => "Unknown",
                };
            }
        }

        public int QualitySortValue => (int)Quality;

        public string ReferenceDisplay
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ReferenceUrl))
                {
                    return string.IsNullOrWhiteSpace(Reference) ? string.Empty : Reference;
                }

                string label = string.IsNullOrWhiteSpace(Reference) ? ReferenceUrl : Reference;
                return $"[{label}]({ReferenceUrl})";
            }
        }
    }
}
