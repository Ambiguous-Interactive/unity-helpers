// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    internal sealed class WProtoByteBudget
    {
        private long _remaining;

        internal WProtoByteBudget(long remaining)
        {
            _remaining = remaining < 0 ? 0 : remaining;
        }

        internal bool TryCharge(long count)
        {
            if (count < 0 || _remaining < count)
            {
                return false;
            }

            _remaining -= count;
            return true;
        }
    }
}
