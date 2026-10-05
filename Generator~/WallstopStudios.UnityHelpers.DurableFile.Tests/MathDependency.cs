// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    /// <summary>Supplies gate-index arithmetic without Unity math dependencies.</summary>
    internal static class MathDependency
    {
        internal static int PositiveMod(this int value, int max)
        {
            if (0 <= value && value < max)
            {
                return value;
            }

            int remainder = value % max;
            if (remainder != 0 && remainder < 0 != max < 0)
            {
                remainder += max;
            }

            return remainder;
        }
    }
}
