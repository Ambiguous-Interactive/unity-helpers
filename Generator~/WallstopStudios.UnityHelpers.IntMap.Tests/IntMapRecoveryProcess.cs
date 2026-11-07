// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.IntMap.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    internal static class IntMapRecoveryProcess
    {
        private static void Main(string[] arguments)
        {
            bool insertZero =
                arguments.Length == 1
                && string.Equals(arguments[0], "zero", StringComparison.Ordinal);
            bool controlFailed = false;
            try
            {
                byte[] control = new byte[64 * 1024 * 1024];
                GC.KeepAlive(control);
            }
            catch (OutOfMemoryException)
            {
                controlFailed = true;
            }
            Require(controlFailed, "The heap limit must refuse the positive allocation control.");

            if (
                arguments.Length == 1
                && string.Equals(arguments[0], "indices", StringComparison.Ordinal)
            )
            {
                VerifyRecovery<byte>(262144, false, static key => (byte)(key % 251));
            }
            else
            {
                VerifyRecovery<RecoveryValue>(
                    32768,
                    insertZero,
                    static key => new RecoveryValue { data = key + 17 }
                );
            }
            Console.WriteLine("PASS: forced allocation failure preserves the map and all views.");
        }

        private static void VerifyRecovery<T>(
            int capacityHint,
            bool insertZero,
            Func<int, T> createValue
        )
            where T : struct, IEquatable<T>
        {
            IntMap<T> map = new IntMap<T>(capacityHint);
            int capacity = map.Capacity;
            int count = capacity / 4;
            int firstKey = insertZero ? 1 : 0;
            for (int index = 0; index < count; ++index)
            {
                int key = firstKey + index;
                T value = createValue(key);
                Require(map.TrySet(key, value), "Initial insertion must succeed.");
            }
            IntMap<T>.Enumerator enumerator = map.GetEnumerator();
            IntMap<T>.KeyEnumerator keyEnumerator = map.Keys.GetEnumerator();
            IntMap<T>.ValueEnumerator valueEnumerator = map.Values.GetEnumerator();
            int failedKey = insertZero ? 0 : count;
            bool growthFailed = false;
            try
            {
                T value = createValue(failedKey);
                map.TrySet(failedKey, value);
            }
            catch (OutOfMemoryException exception)
            {
                Console.WriteLine(exception.StackTrace);
                growthFailed = true;
            }
            Require(growthFailed, "Growth must actually throw OutOfMemoryException.");
            Require(map.Capacity == capacity, "Failed growth must preserve capacity.");
            Require(map.Count == count, "Failed growth must preserve count.");
            Require(!map.ContainsKey(failedKey), "Failed insertion must remain absent.");
            for (int index = 0; index < count; ++index)
            {
                int key = firstKey + index;
                Require(map.TryGet(key, out T value), "Every key must remain reachable.");
                T expected = createValue(key);
                Require(value.Equals(expected), "Every value must remain unchanged.");
            }
            bool[] seen = new bool[count];
            int pairs = 0;
            foreach (System.Collections.Generic.KeyValuePair<int, T> pair in map)
            {
                Require(
                    firstKey <= pair.Key && pair.Key < firstKey + count,
                    "Every enumerated key must be in the original range."
                );
                int index = pair.Key - firstKey;
                Require(!seen[index], "Pair enumeration must visit each original entry once.");
                seen[index] = true;
                T expected = createValue(pair.Key);
                Require(pair.Value.Equals(expected), "Pair enumeration must preserve values.");
                Require(
                    keyEnumerator.MoveNext() && keyEnumerator.Current == pair.Key,
                    "The existing key view must preserve every key."
                );
                Require(
                    valueEnumerator.MoveNext() && valueEnumerator.Current.Equals(expected),
                    "The existing value view must preserve every value."
                );
                ++pairs;
            }
            Require(pairs == count, "All views must preserve count.");
            Require(
                !keyEnumerator.MoveNext() && !valueEnumerator.MoveNext(),
                "Views must finish after the original contents."
            );
            Require(enumerator.MoveNext(), "The existing pair enumerator must remain valid.");
            T replacement = createValue(99);
            Require(map.TrySet(17, replacement), "Replacement must still succeed.");
            Require(
                map.TryGet(17, out T replaced) && replaced.Equals(replacement),
                "Replacement must remain readable."
            );
            T expectedRemoved = createValue(18);
            Require(
                map.Remove(18, out T removed) && removed.Equals(expectedRemoved),
                "Removal must still succeed."
            );
            T inserted = createValue(123);
            Require(map.TrySet(failedKey, inserted), "Insertion after removal must succeed.");
            Require(
                map.TryGet(failedKey, out T added) && added.Equals(inserted),
                "The new value must remain readable."
            );
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        [StructLayout(LayoutKind.Sequential, Size = 64)]
        private struct RecoveryValue : IEquatable<RecoveryValue>
        {
            public long data;

            public bool Equals(RecoveryValue other)
            {
                return data == other.data;
            }
        }
    }
}
