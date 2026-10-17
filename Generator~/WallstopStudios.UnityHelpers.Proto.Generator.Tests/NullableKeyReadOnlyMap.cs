// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>Supplies nullable keys through a caller-defined map interface.</summary>
    public sealed class NullableKeyReadOnlyMap : IReadOnlyDictionary<int?, int?>
    {
        public int Count => 1;

        public IEnumerable<int?> Keys => new[] { _entry.Key };

        public IEnumerable<int?> Values => new[] { _entry.Value };

        public int? this[int? key] => TryGetValue(key, out int? value) ? value : null;

        private readonly KeyValuePair<int?, int?> _entry;

        public NullableKeyReadOnlyMap(int? key)
        {
            _entry = new KeyValuePair<int?, int?>(key, 7);
        }

        public bool ContainsKey(int? key)
        {
            return _entry.Key == key;
        }

        public bool TryGetValue(int? key, out int? value)
        {
            bool found = ContainsKey(key);
            value = found ? _entry.Value : null;
            return found;
        }

        public IEnumerator<KeyValuePair<int?, int?>> GetEnumerator()
        {
            return ((IEnumerable<KeyValuePair<int?, int?>>)new[] { _entry }).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
