// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    /// <summary>
    /// Relative speed bucket for RNG performance comparisons.
    /// </summary>
    public enum RandomSpeedBucket
    {
        Unknown = 0,
        VerySlow,
        Slow,
        Moderate,
        Fast,
        VeryFast,
        Fastest,
    }

    public static class RandomSpeedBucketExtensions
    {
        public static string ToLabel(this RandomSpeedBucket bucket)
        {
            return bucket switch
            {
                RandomSpeedBucket.Fastest => "Fastest",
                RandomSpeedBucket.VeryFast => "Very Fast",
                RandomSpeedBucket.Fast => "Fast",
                RandomSpeedBucket.Moderate => "Moderate",
                RandomSpeedBucket.Slow => "Slow",
                RandomSpeedBucket.VerySlow => "Very Slow",
                _ => "Unknown",
            };
        }

        public static RandomSpeedBucket FromRatio(double ratio)
        {
            if (double.IsNaN(ratio) || ratio <= 0)
            {
                return RandomSpeedBucket.Unknown;
            }

            if (0.95d <= ratio)
            {
                return RandomSpeedBucket.Fastest;
            }

            if (0.75d <= ratio)
            {
                return RandomSpeedBucket.VeryFast;
            }

            if (0.55d <= ratio)
            {
                return RandomSpeedBucket.Fast;
            }

            if (0.35d <= ratio)
            {
                return RandomSpeedBucket.Moderate;
            }

            if (0.2d <= ratio)
            {
                return RandomSpeedBucket.Slow;
            }

            return RandomSpeedBucket.VerySlow;
        }
    }
}
