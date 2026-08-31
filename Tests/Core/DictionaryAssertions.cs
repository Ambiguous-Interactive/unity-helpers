// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Collections.Generic;
    using NUnit.Framework;

    /// <summary>
    /// Reads a key a fixture expects to be present, the way an assertion should: a missing key
    /// fails naming the key and what the collection does hold, where a plain indexer raises
    /// <see cref="KeyNotFoundException"/> naming nothing and stops the fixture at the read rather
    /// than at the assertion that would have explained it.
    /// </summary>
    /// <remarks>
    /// This is what <c>WUH010</c> asks a test to reach for. A fixture whose <b>subject</b> is the
    /// indexer -- a dictionary type's own test suite -- keeps the indexer and states that with a
    /// file-level suppression instead
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/653">#653</see>).
    /// </remarks>
    public static class DictionaryAssertions
    {
        /// <summary>
        /// Returns the value stored under <paramref name="key"/>, failing the test when the key is
        /// absent.
        /// </summary>
        /// <typeparam name="TKey">The collection's key type.</typeparam>
        /// <typeparam name="TValue">The collection's value type.</typeparam>
        /// <param name="source">The keyed collection to read.</param>
        /// <param name="key">The key the test expects to be present.</param>
        /// <returns>The value stored under <paramref name="key"/>.</returns>
        /// <remarks>
        /// The parameter is the pair sequence rather than one of the dictionary interfaces because
        /// <see cref="IDictionary{TKey, TValue}"/> and
        /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> do not derive from one another, and
        /// <see cref="Dictionary{TKey, TValue}"/> implements both -- an overload per interface is
        /// an ambiguous call at every concrete call site.
        /// </remarks>
        public static TValue ValueFor<TKey, TValue>(
            this IEnumerable<KeyValuePair<TKey, TValue>> source,
            TKey key
        )
        {
            Assert.IsTrue(source != null, "The keyed collection under test is null.");

            if (
                source is IReadOnlyDictionary<TKey, TValue> readOnlyDictionary
                && readOnlyDictionary.TryGetValue(key, out TValue readOnlyValue)
            )
            {
                return readOnlyValue;
            }

            if (
                source is IDictionary<TKey, TValue> dictionary
                && dictionary.TryGetValue(key, out TValue dictionaryValue)
            )
            {
                return dictionaryValue;
            }

            EqualityComparer<TKey> comparer = EqualityComparer<TKey>.Default;
            int count = 0;
            foreach (KeyValuePair<TKey, TValue> entry in source)
            {
                ++count;
                if (comparer.Equals(entry.Key, key))
                {
                    return entry.Value;
                }
            }

            Assert.Fail(
                "Expected a value for key '{0}', but the collection holds {1} entries without it.",
                key,
                count
            );
            return default;
        }
    }
}
