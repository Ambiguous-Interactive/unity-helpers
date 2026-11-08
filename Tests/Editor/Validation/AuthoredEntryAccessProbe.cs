// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System.Collections;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal sealed class AuthoredEntryAccessProbe : IReadOnlyList<AuthoredAssetEntry>
    {
        public int Count => _entries.Count;
        public int ReadCount { get; set; }
        public AuthoredAssetEntry this[int index]
        {
            get
            {
                ++ReadCount;
                return _entries[index];
            }
        }
        private readonly IReadOnlyList<AuthoredAssetEntry> _entries;

        public AuthoredEntryAccessProbe(IReadOnlyList<AuthoredAssetEntry> entries)
        {
            _entries = entries;
        }

        public IEnumerator<AuthoredAssetEntry> GetEnumerator()
        {
            for (int index = 0; index < Count; ++index)
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
