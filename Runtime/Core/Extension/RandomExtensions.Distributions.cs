// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Random;

    public static partial class RandomExtensions
    {
        /// <summary>Checks a probability without drawing when it is outside the open interval (0, 1).</summary>
        /// <remarks>NaN and null generators return false. Unlike NextBool, endpoint checks preserve the seeded stream.</remarks>
        public static bool NextBoolSparing(this IRandom random, double probability)
        {
            if (random == null || !(0d < probability))
            {
                return false;
            }
            return 1d <= probability || random.NextDouble() < probability;
        }

        /// <summary>Draws an exponential variate with the given positive finite rate.</summary>
        /// <remarks>Uses -log(1-u)/rate with one double u in [0,1). Returns NaN without drawing for an invalid rate or null generator. Results may overflow for very small rates.</remarks>
        public static double NextExponential(this IRandom random, double rate)
        {
            if (random == null || !(0d < rate) || !double.IsFinite(rate))
            {
                return double.NaN;
            }
            double uniform = random.NextDouble();
            if (!(0d <= uniform && uniform < 1d))
            {
                return double.NaN;
            }
            return -Math.Log(1d - uniform) / rate;
        }

        /// <summary>Draws a standard Gumbel variate with one double draw.</summary>
        /// <remarks>Uses -log(-log(u)), replacing u=0 with 2^-53. Returns NaN for a null generator or samples outside [0,1). Transcendental rounding is platform dependent.</remarks>
        public static double NextGumbel(this IRandom random)
        {
            if (random == null)
            {
                return double.NaN;
            }
            double uniform = random.NextDouble();
            if (!(0d <= uniform && uniform < 1d))
            {
                return double.NaN;
            }
            const double minimumUniform = 1d / 9007199254740992d;
            double openUniform = uniform == 0d ? minimumUniform : uniform;
            return -Math.Log(-Math.Log(openUniform));
        }
    }
}
