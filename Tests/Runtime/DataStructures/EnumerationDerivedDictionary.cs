// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    internal sealed class EnumerationDerivedDictionary
        : Dictionary<int, int>,
            IEnumerable<KeyValuePair<int, int>>
    {
        public int Acquisitions;
        public int Disposals;
        public bool FailOnAcquire;
        public bool FailOnMove;

        private IEnumerable<KeyValuePair<int, int>> EnumerateValues()
        {
            try
            {
                if (FailOnMove)
                {
                    throw new InvalidOperationException(nameof(FailOnMove));
                }
                yield return new KeyValuePair<int, int>(42, 24);
                yield return new KeyValuePair<int, int>(13, 31);
            }
            finally
            {
                ++Disposals;
            }
        }

        IEnumerator<KeyValuePair<int, int>> IEnumerable<KeyValuePair<int, int>>.GetEnumerator()
        {
            ++Acquisitions;
            if (FailOnAcquire)
            {
                throw new InvalidOperationException(nameof(FailOnAcquire));
            }
            return EnumerateValues().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<int, int>>)this).GetEnumerator();
        }
    }
}
