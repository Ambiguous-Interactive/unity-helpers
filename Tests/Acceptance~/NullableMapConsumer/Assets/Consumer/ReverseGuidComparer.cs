// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using System;
    using System.Collections.Generic;

    public sealed class ReverseGuidComparer : IComparer<Guid>
    {
        public int Compare(Guid first, Guid second)
        {
            return second.CompareTo(first);
        }
    }
}
