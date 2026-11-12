// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;

    public static partial class RandomExtensions
    {
        /// <summary>Samples a logit distribution using caller-owned scratch storage and one random draw.</summary>
        /// <remarks>Invalid input returns false with index zero and consumes no draw. Scratch must fit all logits; only its used prefix is changed. Scratch may overlap logits.</remarks>
        public static bool TryNextSoftmaxIndex(
            this IRandom random,
            ReadOnlySpan<double> logits,
            Span<double> scratch,
            out int index,
            double temperature = 1d
        )
        {
            if (random == null || logits.IsEmpty || scratch.Length < logits.Length)
            {
                index = default;
                return false;
            }
            Span<double> probabilities = scratch.Slice(0, logits.Length);
            logits.CopyTo(probabilities);
            if (!WallMath.SoftmaxInPlace(probabilities, temperature))
            {
                index = default;
                return false;
            }
            return random.TryNextWeightedIndex(probabilities, out index);
        }
    }
}
