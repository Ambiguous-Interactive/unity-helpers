// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections;
    using System.Collections.Generic;

    internal sealed class EnumerationDerivedList : List<int>, IEnumerable<int>
    {
        public readonly EnumerationProbe Probe = new EnumerationProbe();

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return Probe.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Probe.GetEnumerator();
        }
    }
}
