// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>Exercises candidate collection failures and reentry during real enumeration.</summary>
    internal sealed class SingletonCandidateList : IReadOnlyList<Type>
    {
        public int Count
        {
            get
            {
                ++CountReadCount;
                _onCountRead();
                return 0;
            }
        }

        public Type this[int index] => throw new ArgumentOutOfRangeException(nameof(index));

        internal int CountReadCount { get; private set; }

        private readonly Action _onCountRead;

        internal SingletonCandidateList(Action onCountRead)
        {
            _onCountRead = onCountRead;
        }

        public IEnumerator<Type> GetEnumerator()
        {
            int count = Count;
            for (int index = 0; index < count; ++index)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
