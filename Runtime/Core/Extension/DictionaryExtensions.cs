// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Extension methods for dictionary types providing additional functionality for retrieving, adding, and manipulating dictionary entries.
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Gets an existing value from the dictionary or adds a new value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to query or modify.</param>
        /// <param name="key">The key to look up or add.</param>
        /// <param name="valueProducer">A function that produces a new value if the key is not found.</param>
        /// <returns>The existing value if the key exists, otherwise the newly added value.</returns>
        /// <remarks>
        /// Optimized for ConcurrentDictionary using thread-safe operations.
        /// For non-concurrent dictionaries, uses TryGetValue followed by direct assignment.
        /// Null handling: Throws if dictionary or valueProducer is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary or valueProducer is null.</exception>
        public static V GetOrAdd<K, V>(
            this IDictionary<K, V> dictionary,
            K key,
            Func<V> valueProducer
        )
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.GetOrAdd(
                    key,
                    static (_, existing) => existing(),
                    valueProducer
                );
            }

            if (dictionary.TryGetValue(key, out V result))
            {
                return result;
            }

            return dictionary[key] = valueProducer();
        }

        /// <summary>
        /// Gets an existing value from the dictionary or adds a new value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to query or modify.</param>
        /// <param name="key">The key to look up or add.</param>
        /// <param name="valueProducer">A function that takes the key and produces a new value if the key is not found.</param>
        /// <returns>The existing value if the key exists, otherwise the newly added value.</returns>
        /// <remarks>
        /// This overload allows the value producer to use the key when creating a new value.
        /// Optimized for ConcurrentDictionary using thread-safe operations.
        /// Null handling: Throws if dictionary or valueProducer is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary or valueProducer is null.</exception>
        public static V GetOrAdd<K, V>(
            this IDictionary<K, V> dictionary,
            K key,
            Func<K, V> valueProducer
        )
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.GetOrAdd(key, valueProducer);
            }

            if (dictionary.TryGetValue(key, out V result))
            {
                return result;
            }

            return dictionary[key] = valueProducer(key);
        }

        /// <summary>
        /// Gets an existing value from a read-only dictionary or returns a default value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The read-only dictionary to query.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="valueProducer">A function that produces a default value if the key is not found.</param>
        /// <returns>The existing value if the key exists, otherwise the value produced by valueProducer.</returns>
        /// <remarks>
        /// Does not modify the dictionary.
        /// Null handling: Throws if dictionary or valueProducer is null.
        /// Thread-safe: Yes, as it only reads from the dictionary.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary or valueProducer is null.</exception>
        public static V GetOrElse<K, V>(
            this IReadOnlyDictionary<K, V> dictionary,
            K key,
            Func<V> valueProducer
        )
        {
            if (dictionary.TryGetValue(key, out V value))
            {
                return value;
            }

            return valueProducer.Invoke();
        }

        /// <summary>
        /// Gets an existing value from a read-only dictionary or returns a default value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The read-only dictionary to query.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="valueProducer">A function that takes the key and produces a default value if the key is not found.</param>
        /// <returns>The existing value if the key exists, otherwise the value produced by valueProducer.</returns>
        /// <remarks>
        /// This overload allows the value producer to use the key when creating a default value.
        /// Does not modify the dictionary.
        /// Null handling: Throws if dictionary or valueProducer is null.
        /// Thread-safe: Yes, as it only reads from the dictionary.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary or valueProducer is null.</exception>
        public static V GetOrElse<K, V>(
            this IReadOnlyDictionary<K, V> dictionary,
            K key,
            Func<K, V> valueProducer
        )
        {
            if (dictionary.TryGetValue(key, out V value))
            {
                return value;
            }

            return valueProducer.Invoke(key);
        }

        /// <summary>
        /// Gets an existing value from the dictionary or adds a new instance if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary. Must have a parameterless constructor.</typeparam>
        /// <param name="dictionary">The dictionary to query or modify.</param>
        /// <param name="key">The key to look up or add.</param>
        /// <returns>The existing value if the key exists, otherwise a newly created instance of V.</returns>
        /// <remarks>
        /// Requires that V has a parameterless constructor.
        /// Optimized for ConcurrentDictionary using thread-safe operations.
        /// Null handling: Throws if dictionary is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries. Adds object allocation overhead for new instances.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary is null.</exception>
        public static V GetOrAdd<K, V>(this IDictionary<K, V> dictionary, K key)
            where V : new()
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.AddOrUpdate(
                    key,
                    static _ => new V(),
                    static (_, existing) => existing
                );
            }

            if (dictionary.TryGetValue(key, out V result))
            {
                return result;
            }

            return dictionary[key] = new V();
        }

        /// <summary>
        /// Gets an existing value from a read-only dictionary or returns the specified default value if the key doesn't exist.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The read-only dictionary to query.</param>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The default value to return if the key is not found.</param>
        /// <returns>The existing value if the key exists, otherwise the specified default value.</returns>
        /// <remarks>
        /// Does not modify the dictionary.
        /// Null handling: Returns the default value if dictionary is null or key doesn't exist.
        /// Thread-safe: Yes, as it only reads from the dictionary.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        public static V GetOrElse<K, V>(this IReadOnlyDictionary<K, V> dictionary, K key, V value)
        {
            return dictionary.GetValueOrDefault(key, value);
        }

        /// <summary>
        /// Adds a new key-value pair or updates an existing value in the dictionary.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to modify.</param>
        /// <param name="key">The key to add or update.</param>
        /// <param name="creator">A function that creates a value if the key doesn't exist.</param>
        /// <param name="updater">A function that updates the value if the key exists.</param>
        /// <returns>The final value that was added or updated.</returns>
        /// <remarks>
        /// Optimized for ConcurrentDictionary using thread-safe AddOrUpdate.
        /// For non-concurrent dictionaries, uses TryGetValue followed by direct assignment.
        /// Null handling: Throws if dictionary, creator, or updater is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary, creator, or updater is null.</exception>
        public static V AddOrUpdate<K, V>(
            this IDictionary<K, V> dictionary,
            K key,
            Func<K, V> creator,
            Func<K, V, V> updater
        )
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.AddOrUpdate(key, creator, updater);
            }

            V latest = dictionary.TryGetValue(key, out V value)
                ? updater(key, value)
                : creator(key);
            dictionary[key] = latest;
            return latest;
        }

        /// <summary>
        /// Tries to add a new key-value pair to the dictionary, or returns the existing value if the key already exists.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to modify.</param>
        /// <param name="key">The key to add.</param>
        /// <param name="creator">A function that creates a value if the key doesn't exist.</param>
        /// <returns>The existing value if the key exists, otherwise the newly created value.</returns>
        /// <remarks>
        /// Unlike GetOrAdd, this always returns the value that was in the dictionary after the operation.
        /// Optimized for ConcurrentDictionary using thread-safe AddOrUpdate.
        /// Null handling: Throws if dictionary or creator is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary or creator is null.</exception>
        public static V TryAdd<K, V>(this IDictionary<K, V> dictionary, K key, Func<K, V> creator)
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.AddOrUpdate(
                    key,
                    creator,
                    static (_, existingValue) => existingValue
                );
            }

            if (dictionary.TryGetValue(key, out V existing))
            {
                return existing;
            }

            V value = creator(key);
            dictionary[key] = value;
            return value;
        }

        /// <summary>
        /// Merges two read-only dictionaries into a new dictionary, with values from the right-hand side overwriting values from the left-hand side for duplicate keys.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionaries.</typeparam>
        /// <typeparam name="V">The type of values in the dictionaries.</typeparam>
        /// <param name="lhs">The left-hand side dictionary (lower priority).</param>
        /// <param name="rhs">The right-hand side dictionary (higher priority, overwrites lhs).</param>
        /// <param name="creator">Optional function to create the result dictionary. If null, a new Dictionary is created.</param>
        /// <returns>A new dictionary containing all entries from both dictionaries, with rhs values taking precedence.</returns>
        /// <remarks>
        /// Values from rhs overwrite values from lhs for any duplicate keys.
        /// Does not modify the input dictionaries.
        /// Null handling: Handles null or empty dictionaries gracefully.
        /// Thread-safe: No. The returned dictionary is not thread-safe unless created with a concurrent type.
        /// Performance: O(n+m) where n and m are the sizes of the input dictionaries.
        /// Allocations: Creates a new dictionary. Use the creator parameter for custom capacity or implementation.
        /// </remarks>
        public static Dictionary<K, V> Merge<K, V>(
            this IReadOnlyDictionary<K, V> lhs,
            IReadOnlyDictionary<K, V> rhs,
            Func<Dictionary<K, V>> creator = null
        )
        {
            Dictionary<K, V> result = creator?.Invoke() ?? new Dictionary<K, V>();
            if (0 < lhs.Count)
            {
                foreach (KeyValuePair<K, V> kvp in lhs)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            if (0 < rhs.Count)
            {
                foreach (KeyValuePair<K, V> kvp in rhs)
                {
                    result[kvp.Key] = kvp.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Attempts to remove a key-value pair from the dictionary and returns the removed value.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to modify.</param>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">The value that was removed, or default if the key wasn't found.</param>
        /// <returns>True if the key was found and removed, false otherwise.</returns>
        /// <remarks>
        /// Optimized for ConcurrentDictionary using thread-safe TryRemove.
        /// Null handling: Returns false if dictionary is null.
        /// Thread-safe: Yes, if using ConcurrentDictionary. No, for other dictionary types.
        /// Performance: O(1) average case for hash-based dictionaries.
        /// </remarks>
        public static bool TryRemove<K, V>(this IDictionary<K, V> dictionary, K key, out V value)
        {
            if (dictionary is ConcurrentDictionary<K, V> concurrentDictionary)
            {
                return concurrentDictionary.TryRemove(key, out value);
            }

            return dictionary.Remove(key, out value);
        }

        /// <summary>
        /// Computes the difference between two dictionaries, returning entries from rhs that differ from or don't exist in lhs.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionaries.</typeparam>
        /// <typeparam name="V">The type of values in the dictionaries.</typeparam>
        /// <param name="lhs">The basis dictionary for comparison.</param>
        /// <param name="rhs">The changed dictionary to compare against.</param>
        /// <param name="creator">Optional function to create the result dictionary. If null, a new Dictionary is created with capacity of rhs.Count.</param>
        /// <returns>A dictionary containing all entries from rhs that either don't exist in lhs or have different values.</returns>
        /// <remarks>
        /// Uses Equals to compare values.
        /// Does not modify the input dictionaries.
        /// Null handling: Handles null or empty dictionaries gracefully.
        /// Thread-safe: No.
        /// Performance: O(m) where m is the size of rhs.
        /// Allocations: Creates a new dictionary. Use the creator parameter for custom capacity.
        /// </remarks>
        public static Dictionary<K, V> Difference<K, V>(
            this IReadOnlyDictionary<K, V> lhs,
            IReadOnlyDictionary<K, V> rhs,
            Func<Dictionary<K, V>> creator = null
        )
        {
            Dictionary<K, V> result = creator?.Invoke() ?? new Dictionary<K, V>(rhs.Count);
            foreach (KeyValuePair<K, V> kvp in rhs)
            {
                K key = kvp.Key;
                if (lhs.TryGetValue(key, out V existing) && Equals(existing, kvp.Value))
                {
                    continue;
                }

                result[key] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// Creates a reversed dictionary where values become keys and keys become values.
        /// </summary>
        /// <typeparam name="K">The type of keys in the input dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the input dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to reverse.</param>
        /// <param name="creator">Optional function to create the result dictionary. If null, a new Dictionary is created with capacity of dictionary.Count.</param>
        /// <returns>A new dictionary where values from the input become keys and keys become values.</returns>
        /// <remarks>
        /// If multiple keys map to the same value in the input, only the last encountered key will be retained.
        /// Does not modify the input dictionary.
        /// Null handling: Handles null or empty dictionaries gracefully.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the size of the input dictionary.
        /// Allocations: Creates a new dictionary. Duplicate values will cause overwriting.
        /// </remarks>
        public static Dictionary<V, K> Reverse<K, V>(
            this IReadOnlyDictionary<K, V> dictionary,
            Func<Dictionary<V, K>> creator = null
        )
        {
            Dictionary<V, K> output = creator?.Invoke() ?? new Dictionary<V, K>(dictionary.Count);
            foreach (KeyValuePair<K, V> entry in dictionary)
            {
                output[entry.Value] = entry.Key;
            }

            return output;
        }

        /// <summary>
        /// Converts a read-only dictionary to a mutable Dictionary.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The read-only dictionary to convert.</param>
        /// <returns>A new mutable Dictionary containing all entries from the input.</returns>
        /// <remarks>
        /// Creates a new Dictionary instance; modifications won't affect the original.
        /// Null handling: Throws ArgumentNullException if dictionary is null.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the size of the dictionary.
        /// Allocations: Creates a new dictionary with the same size as the input.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary is null.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(this IReadOnlyDictionary<K, V> dictionary)
        {
            return new Dictionary<K, V>(dictionary);
        }

        /// <summary>
        /// Converts a read-only dictionary to a mutable Dictionary with a custom equality comparer.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The read-only dictionary to convert.</param>
        /// <param name="comparer">The equality comparer to use for keys.</param>
        /// <returns>A new mutable Dictionary containing all entries from the input, using the specified comparer.</returns>
        /// <remarks>
        /// Creates a new Dictionary instance; modifications won't affect the original.
        /// Null handling: Throws ArgumentNullException if dictionary is null.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the size of the dictionary.
        /// Allocations: Creates a new dictionary with the same size as the input.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if dictionary is null.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(
            this IReadOnlyDictionary<K, V> dictionary,
            IEqualityComparer<K> comparer
        )
        {
            return new Dictionary<K, V>(dictionary, comparer);
        }

        /// <summary>
        /// Converts an enumerable of key-value pairs to a Dictionary.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="prettyMuchADictionary">An enumerable of key-value pairs.</param>
        /// <returns>A new Dictionary containing all entries from the input enumerable.</returns>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if prettyMuchADictionary is null or contains null keys.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the number of key-value pairs.
        /// Allocations: Creates a new dictionary.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if prettyMuchADictionary is null or contains null keys.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(
            this IEnumerable<KeyValuePair<K, V>> prettyMuchADictionary
        )
        {
            Dictionary<K, V> result = new();
            foreach (KeyValuePair<K, V> kvp in prettyMuchADictionary)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        /// <summary>
        /// Converts an enumerable of key-value pairs to a Dictionary with a custom equality comparer.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="prettyMuchADictionary">An enumerable of key-value pairs.</param>
        /// <param name="comparer">The equality comparer to use for keys.</param>
        /// <returns>A new Dictionary containing all entries from the input enumerable, using the specified comparer.</returns>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if prettyMuchADictionary is null or contains null keys.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the number of key-value pairs.
        /// Allocations: Creates a new dictionary.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if prettyMuchADictionary is null or contains null keys.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(
            this IEnumerable<KeyValuePair<K, V>> prettyMuchADictionary,
            IEqualityComparer<K> comparer
        )
        {
            Dictionary<K, V> result = new(comparer);
            foreach (KeyValuePair<K, V> kvp in prettyMuchADictionary)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        /// <summary>
        /// Converts an enumerable of tuples to a Dictionary.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="prettyMuchADictionary">An enumerable of (key, value) tuples.</param>
        /// <returns>A new Dictionary containing all entries from the input enumerable.</returns>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if prettyMuchADictionary is null or contains null keys.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the number of tuples.
        /// Allocations: Creates a new dictionary.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if prettyMuchADictionary is null or contains null keys.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(
            this IEnumerable<(K, V)> prettyMuchADictionary
        )
        {
            Dictionary<K, V> result = new();
            foreach ((K key, V value) in prettyMuchADictionary)
            {
                result[key] = value;
            }
            return result;
        }

        /// <summary>
        /// Converts an enumerable of tuples to a Dictionary with a custom equality comparer.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="V">The type of values in the dictionary.</typeparam>
        /// <param name="prettyMuchADictionary">An enumerable of (key, value) tuples.</param>
        /// <param name="comparer">The equality comparer to use for keys.</param>
        /// <returns>A new Dictionary containing all entries from the input enumerable, using the specified comparer.</returns>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if prettyMuchADictionary is null or contains null keys.
        /// Thread-safe: No.
        /// Performance: O(n) where n is the number of tuples.
        /// Allocations: Creates a new dictionary.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown if prettyMuchADictionary is null or contains null keys.</exception>
        public static Dictionary<K, V> ToDictionary<K, V>(
            this IEnumerable<(K, V)> prettyMuchADictionary,
            IEqualityComparer<K> comparer
        )
        {
            Dictionary<K, V> result = new(comparer);
            foreach ((K key, V value) in prettyMuchADictionary)
            {
                result[key] = value;
            }
            return result;
        }

        /// <summary>
        /// Compares two read-only dictionaries for content equality using IEquatable comparison for values.
        /// </summary>
        /// <typeparam name="K">The type of keys in the dictionaries.</typeparam>
        /// <typeparam name="V">The type of values in the dictionaries, must implement IEquatable.</typeparam>
        /// <param name="dictionary">The first dictionary to compare.</param>
        /// <param name="other">The second dictionary to compare.</param>
        /// <returns>True if both dictionaries have the same keys with equal values, false otherwise.</returns>
        /// <remarks>
        /// Returns true if both dictionaries are the same reference or both are null.
        /// Returns false if only one is null or if they have different counts.
        /// Returns true for empty dictionaries with matching counts.
        /// Null handling: Handles null dictionaries gracefully, returning true only if both are null.
        /// Thread-safe: Yes, as it only reads from the dictionaries.
        /// Performance: O(n) where n is the size of the dictionaries.
        /// </remarks>
        public static bool ContentEquals<K, V>(
            this IReadOnlyDictionary<K, V> dictionary,
            IReadOnlyDictionary<K, V> other
        )
            where V : IEquatable<V>
        {
            if (ReferenceEquals(dictionary, other))
            {
                return true;
            }

            if (ReferenceEquals(dictionary, null) || ReferenceEquals(other, null))
            {
                return false;
            }

            if (dictionary.Count != other.Count)
            {
                return false;
            }

            if (dictionary.Count == 0)
            {
                return true;
            }

            foreach (KeyValuePair<K, V> entry in dictionary)
            {
                if (!other.TryGetValue(entry.Key, out V value) || !entry.Value.Equals(value))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
