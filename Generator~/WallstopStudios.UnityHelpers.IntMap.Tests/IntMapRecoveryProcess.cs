// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.IntMap.Tests
{
    using System;
    using System.Runtime.InteropServices;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    internal static class IntMapRecoveryProcess
    {
        internal const string LongMapKind = "longmap";
        internal const string ZeroKeyKind = "zero";
        internal const string NonzeroKeyKind = "nonzero";
        internal const string IndicesKeyKind = "indices";
        internal const string PassMarker = "PASS: forced allocation failure";

        private static void Main(string[] arguments)
        {
            Require(arguments.Length == 1, "Exactly one recovery scenario is required.");
            string keyKind = arguments[0];
            Require(
                string.Equals(keyKind, LongMapKind, StringComparison.Ordinal)
                    || string.Equals(keyKind, ZeroKeyKind, StringComparison.Ordinal)
                    || string.Equals(keyKind, NonzeroKeyKind, StringComparison.Ordinal)
                    || string.Equals(keyKind, IndicesKeyKind, StringComparison.Ordinal),
                "The recovery scenario must be recognized."
            );
            bool insertZero = string.Equals(keyKind, ZeroKeyKind, StringComparison.Ordinal);
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

            if (string.Equals(keyKind, LongMapKind, StringComparison.Ordinal))
            {
                VerifyLongMapRecovery();
            }
            else if (string.Equals(keyKind, IndicesKeyKind, StringComparison.Ordinal))
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
            Console.WriteLine(PassMarker + " preserves the map and all views.");
        }

        private static void VerifyLongMapRecovery()
        {
            const int Count = 262144;
            LongMap<RecoveryValue> map = new LongMap<RecoveryValue>(Count);
            for (int index = 0; index < Count; ++index)
            {
                Require(
                    map.TrySet((ulong)index, new RecoveryValue { data = index + 17 }),
                    "Initial long-key insertion must succeed."
                );
            }
            int capacity = map.Capacity;
            LongMap<RecoveryValue>.Enumerator pairs = map.GetEnumerator();
            LongMap<RecoveryValue>.KeyEnumerator keys = map.Keys.GetEnumerator();
            LongMap<RecoveryValue>.ValueEnumerator values = map.Values.GetEnumerator();
            bool refused = false;
            try
            {
                map.TrySet(ulong.MaxValue, new RecoveryValue { data = 123 });
            }
            catch (OutOfMemoryException)
            {
                refused = true;
            }
            Require(refused, "The limited heap must refuse long-map growth.");
            Require(
                map.Count == Count && map.Capacity == capacity,
                "Failed long-map growth must retain count and capacity."
            );
            Require(!map.ContainsKey(ulong.MaxValue), "The refused key must remain absent.");
            int observed = 0;
            while (pairs.MoveNext())
            {
                System.Collections.Generic.KeyValuePair<ulong, RecoveryValue> pair = pairs.Current;
                Require(
                    pair.Key == (ulong)observed && pair.Value.data == observed + 17,
                    "Packed long-map entries must remain intact."
                );
                Require(
                    map.TryGet(pair.Key, out RecoveryValue value) && value.Equals(pair.Value),
                    "All long-map lookups must remain valid."
                );
                Require(
                    keys.MoveNext() && keys.Current == pair.Key,
                    "Existing long-map key views must remain valid."
                );
                Require(
                    values.MoveNext() && values.Current.Equals(pair.Value),
                    "Existing long-map value views must remain valid."
                );
                ++observed;
            }
            Require(
                observed == Count && !keys.MoveNext() && !values.MoveNext(),
                "Long-map views must retain every original entry."
            );
            Require(
                map.TrySet(17, new RecoveryValue { data = 99 }),
                "Replacement after failed growth must succeed."
            );
            Require(
                map.Remove(18, out RecoveryValue removed) && removed.data == 35,
                "Removal after failed growth must succeed."
            );
            Require(
                map.TryAdd(ulong.MaxValue, new RecoveryValue { data = 123 }),
                "Insertion into freed capacity must succeed."
            );
            Require(
                map.TryGet(ulong.MaxValue, out RecoveryValue inserted) && inserted.data == 123,
                "The replacement key must be readable."
            );
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
