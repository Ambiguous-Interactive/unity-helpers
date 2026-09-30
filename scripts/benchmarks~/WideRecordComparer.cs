// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using WideRecord = System.ValueTuple<long, long, long, long, long>;

    public static partial class JesseWideParityHarness
    {
        private sealed class WideRecordComparer : IComparer<WideRecord>
        {
            public int Compare(WideRecord left, WideRecord right)
            {
                return left.Item1.CompareTo(right.Item1);
            }
        }
    }
}
