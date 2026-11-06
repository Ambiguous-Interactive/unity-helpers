// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class UnityObjectMapTests : CommonTestBase
    {
        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        [TestCase(16, 5)]
        [TestCase(16, 16)]
        public void EnumerationSkipsDestroyedKeysWithoutPruning(int count, int dead)
        {
            using UnityObjectMap<GameObject, int> map = new();
            HashSet<GameObject> expected = new(ReferenceIdentityComparer<GameObject>.Instance);
            for (int index = 0; index < count; ++index)
            {
                GameObject key = Track(new GameObject(nameof(key)));
                Assert.That(map.TrySet(key, index), Is.True);
                if (index < dead)
                {
                    Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Destroyed identity is under test; tracked for teardown.
                }
                else
                {
                    expected.Add(key);
                }
            }
            int observed = 0;
            foreach (KeyValuePair<GameObject, int> pair in map)
            {
                Assert.That(pair.Key != null, Is.True);
                Assert.That(expected.Remove(pair.Key), Is.True);
                Assert.That(pair.Value, Is.InRange(dead, count - 1));
                ++observed;
            }
            Assert.That(observed, Is.EqualTo(count - dead));
            Assert.That(expected, Is.Empty);
            Assert.That(map.Count, Is.EqualTo(count));
        }

        [TestCase("replace")]
        [TestCase("insert")]
        [TestCase("remove")]
        [TestCase("clear")]
        [TestCase("prune")]
        [TestCase("dispose")]
        public void EveryMapMutationInvalidatesEnumeration(string operation)
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, 1);
            UnityObjectMap<GameObject, int>.Enumerator enumerator = map.GetEnumerator();
            Assert.That(enumerator.MoveNext(), Is.True);
            switch (operation)
            {
                case "replace":
                    map.TrySet(key, 2);
                    break;
                case "insert":
                    GameObject added = Track(new GameObject(nameof(added)));
                    map.TrySet(added, 2);
                    break;
                case "remove":
                    map.Remove(key);
                    break;
                case "clear":
                    map.Clear();
                    break;
                case "prune":
                    Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Pruning invalidation is under test; tracked for teardown.
                    map.PruneDestroyed();
                    break;
                case "dispose":
                    map.Dispose();
                    break;
                default:
                    Assert.Fail(operation);
                    break;
            }
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
            Assert.Throws<InvalidOperationException>(() => enumerator.Reset());
        }

        [Test]
        public void DestroyingAKeyDuringEnumerationSkipsItWithoutInvalidation()
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, 1);
            UnityObjectMap<GameObject, int>.Enumerator enumerator = map.GetEnumerator();
            Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Destroyed-key filtering is under test; tracked for teardown.
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(enumerator.Current.Key == null, Is.True);
            enumerator.Reset();
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(map.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReadsAndRefusedMutationsKeepTheEnumeratorValid()
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, 3);
            UnityObjectMap<GameObject, int>.Enumerator enumerator = map.GetEnumerator();
            Assert.That(map.TrySet(null, 2), Is.False);
            Assert.That(map.Remove(null), Is.False);
            Assert.That(map.PruneDestroyed(), Is.Zero);
            Assert.That(map.GetOrAdd(key, static _ => 5), Is.EqualTo(3));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current.Value, Is.EqualTo(3));
        }

        [Test]
        public void DefaultCopiedResetAndInterfaceEnumeratorsAreSupported()
        {
            UnityObjectMap<GameObject, int>.Enumerator empty = default;
            Assert.That(empty.MoveNext(), Is.False);
            empty.Reset();
            empty.Dispose();
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, 4);
            UnityObjectMap<GameObject, int>.Enumerator first = map.GetEnumerator();
            UnityObjectMap<GameObject, int>.Enumerator copy = first;
            Assert.That(first.Equals(copy), Is.True);
            Assert.That(first.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
            Assert.That(first.MoveNext(), Is.True);
            Assert.That(first.Equals(copy), Is.False);
            Assert.That(copy.MoveNext(), Is.True);
            Assert.That(first.Equals(copy), Is.True);
            Assert.That(first.MoveNext(), Is.False);
            Assert.That(first.Current, Is.EqualTo(default(KeyValuePair<GameObject, int>)));
            first.Reset();
            Assert.That(first.MoveNext(), Is.True);
            using IEnumerator<KeyValuePair<GameObject, int>> boxed = (
                (IEnumerable<KeyValuePair<GameObject, int>>)map
            ).GetEnumerator();
            Assert.That(boxed.MoveNext(), Is.True);
            Assert.That(boxed.Current.Value, Is.EqualTo(4));
            System.Collections.IEnumerator untyped = (
                (System.Collections.IEnumerable)map
            ).GetEnumerator();
            Assert.That(untyped.MoveNext(), Is.True);
            Assert.That(untyped.Current, Is.EqualTo(boxed.Current));
        }

        [Test]
        public void GetOrAddCallsFactoryOnceOnMissAndNeverForLiveHits()
        {
            using UnityObjectMap<GameObject, string> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            int calls = 0;
            string Factory(GameObject actual)
            {
                Assert.That(ReferenceEquals(actual, key), Is.True);
                ++calls;
                return "created";
            }
            Assert.That(map.GetOrAdd(key, Factory), Is.EqualTo("created"));
            Assert.That(map.GetOrAdd(key, Factory), Is.EqualTo("created"));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(
                map.GetOrAdd(key, static (_, state) => state, "unused"),
                Is.EqualTo("created")
            );
            map.Clear();
            Assert.That(
                map.GetOrAdd(key, static (_, state) => state, "state"),
                Is.EqualTo("state")
            );
        }

        [Test]
        public void DestroyedGetOrAddReplacesStaleValueOncePerCall()
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, -1);
            Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Destroyed-key factory behavior is under test; tracked for teardown.
            int calls = 0;
            Assert.That(map.GetOrAdd(key, _ => ++calls), Is.EqualTo(1));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(map.TryGetValue(key, out int stored), Is.True);
            Assert.That(stored, Is.EqualTo(1));
            Assert.That(map.GetOrAdd(key, (_, state) => ++calls + state, 10), Is.EqualTo(12));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map.PruneDestroyed(), Is.EqualTo(1));
        }

        [Test]
        public void InvalidGetOrAddInputsNeverInvokeFactory()
        {
            using UnityObjectMap<GameObject, string> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            int calls = 0;
            Assert.That(map.GetOrAdd(null, _ => (++calls).ToString()), Is.Null);
            Assert.That(map.GetOrAdd(key, null), Is.Null);
            Assert.That(map.GetOrAdd<int>(key, null, 1), Is.Null);
            Assert.That(map.GetOrAdd(key, static (_, state) => state, (string)null), Is.Null);
            Assert.That(map.ContainsKey(key), Is.True);
            Assert.That(map.GetOrAdd(key, _ => (++calls).ToString()), Is.Null);
            map.Dispose();
            Assert.That(map.GetOrAdd(key, _ => (++calls).ToString()), Is.Null);
            Assert.That(calls, Is.Zero);
        }

        [Test]
        public void FactoryFailureDoesNotPublishAndReentrantInsertWins()
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            Assert.Throws<InvalidOperationException>(() =>
                map.GetOrAdd(key, _ => throw new InvalidOperationException())
            );
            Assert.That(map.Count, Is.Zero);
            int result = map.GetOrAdd(
                key,
                item =>
                {
                    map.TrySet(item, 7);
                    return 8;
                }
            );
            Assert.That(result, Is.EqualTo(7));
            Assert.That(map.TryGetValue(key, out int stored), Is.True);
            Assert.That(stored, Is.EqualTo(7));
            map.Clear();
            Assert.That(
                map.GetOrAdd(
                    key,
                    _ =>
                    {
                        map.Dispose();
                        return 9;
                    }
                ),
                Is.Zero
            );
            Assert.That(map.Count, Is.Zero);
        }

        [Test]
        public void TypedEnumerationAndStatefulHitsAllocateZeroBytes()
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            using UnityObjectMap<GameObject, int> map = new();
            GameObject live = Track(new GameObject(nameof(live)));
            GameObject dead = Track(new GameObject(nameof(dead)));
            map.TrySet(live, 3);
            map.TrySet(dead, 20);
            Object.DestroyImmediate(dead); // UNH-SUPPRESS UNH001: Allocation witness includes a destroyed key; tracked for teardown.
            int sum = 0;
            Func<GameObject, int, int> factory = static (_, state) => state;
            foreach (KeyValuePair<GameObject, int> pair in map)
            {
                sum += pair.Value;
            }
            sum += map.GetOrAdd(live, factory, 4);
            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[1024]);
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            if (controlBytes == 0)
            {
                Assert.Ignore(
                    "The allocation counter cannot observe the retained positive control."
                );
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 128; ++iteration)
            {
                foreach (KeyValuePair<GameObject, int> pair in map)
                {
                    sum += pair.Value;
                }
                sum += map.GetOrAdd(live, factory, 4);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(bytes, Is.Zero);
            Assert.That(sum, Is.EqualTo(129 * 6));
        }

        [Test]
        public void TypedEnumerationAndStatefulHitsHaveNoProfilerAllocationEvents()
        {
            object[] retained = new object[1];
            object existing = new();
            Action allocate = () => retained[0] = new object();
            Action reuse = () => retained[0] = existing;
            const int iterations = 128;
            const int capacity = 512;
            long positive = ProfilerAllocationCounter.Measure(allocate, iterations, capacity);
            if (positive == 0)
            {
                Assert.Ignore("The GC.Alloc recorder cannot observe fresh retained objects.");
            }
            Assert.That(positive, Is.EqualTo(iterations));
            Assert.That(ProfilerAllocationCounter.Measure(reuse, iterations, capacity), Is.Zero);
            using UnityObjectMap<GameObject, int> map = new();
            GameObject live = Track(new GameObject(nameof(live)));
            GameObject dead = Track(new GameObject(nameof(dead)));
            map.TrySet(live, 3);
            map.TrySet(dead, 20);
            Object.DestroyImmediate(dead); // UNH-SUPPRESS UNH001: Profiler witness includes a destroyed key; tracked for teardown.
            int sum = 0;
            Func<GameObject, int, int> factory = static (_, state) => state;
            Action enumerate = () =>
            {
                foreach (KeyValuePair<GameObject, int> pair in map)
                {
                    sum += pair.Value;
                }
                sum += map.GetOrAdd(live, factory, 4);
            };
            long events = ProfilerAllocationCounter.Measure(enumerate, iterations, capacity);
            Assert.That(events, Is.Zero);
            Assert.That(sum, Is.EqualTo((iterations + 64) * 6));
        }

        [Test]
        public void ComparerUsesManagedIdentityForDestroyedKeys()
        {
            GameObject first = Track(new GameObject(nameof(first)));
            GameObject second = Track(new GameObject(nameof(second)));
            ReferenceIdentityComparer<GameObject> comparer =
                ReferenceIdentityComparer<GameObject>.Instance;
            int hash = comparer.GetHashCode(first);
            Dictionary<GameObject, int> entries = new(comparer) { [first] = 1, [second] = 2 };
            Object.DestroyImmediate(first); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
            Object.DestroyImmediate(second); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
            Assert.That(first == null, Is.True);
            Assert.That(second == null, Is.True);
            Assert.That(comparer.Equals(first, first), Is.True);
            Assert.That(comparer.Equals(first, second), Is.False);
            Assert.That(comparer.Equals(first, null), Is.False);
            Assert.That(comparer.GetHashCode(first), Is.EqualTo(hash));
            Assert.That(entries.TryGetValue(first, out int found), Is.True);
            Assert.That(found, Is.EqualTo(1));
            Assert.That(entries.Remove(second), Is.True);
            Assert.That(entries.Count, Is.EqualTo(1));
        }

        [Test]
        public void ComparerHandlesNullAndIgnoresValueEquality()
        {
            ReferenceIdentityComparer<string> comparer = ReferenceIdentityComparer<string>.Instance;
            string first = new(new[] { 'a', 'b' });
            string second = new(new[] { 'a', 'b' });
            Assert.That(first, Is.EqualTo(second));
            Assert.That(comparer.Equals(first, second), Is.False);
            Assert.That(comparer.Equals(null, null), Is.True);
            Assert.That(comparer.Equals(first, null), Is.False);
            Assert.That(comparer.Equals(null, first), Is.False);
            Assert.That(comparer.GetHashCode(null), Is.Zero);
            Assert.That(comparer, Is.SameAs(ReferenceIdentityComparer<string>.Instance));
        }

        [Test]
        public void MapHandlesNullAndDisposedInputs()
        {
            using UnityObjectMap<GameObject, string> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            Assert.That(map.TrySet(null, "null"), Is.False);
            Assert.That(map.TryGetValue(null, out string missing), Is.False);
            Assert.That(missing, Is.Null);
            Assert.That(map.ContainsKey(null), Is.False);
            Assert.That(map.Remove(null), Is.False);
            Assert.That(map.PruneDestroyed(), Is.Zero);
            Assert.That(map.TrySet(key, "first"), Is.True);
            Assert.That(map.TrySet(key, null), Is.True);
            Assert.That(map.Count, Is.EqualTo(1));
            Assert.That(map.TryGetValue(key, out string value), Is.True);
            Assert.That(value, Is.Null);
            map.Dispose();
            map.Dispose();
            map.AutoPruneOnSceneUnload = true;
            Assert.That(map.AutoPruneOnSceneUnload, Is.False);
            Assert.That(map.Count, Is.Zero);
            Assert.That(map.TrySet(key, "disposed"), Is.False);
            Assert.That(map.ContainsKey(key), Is.False);
            Assert.That(map.Remove(key), Is.False);
            Assert.That(map.TryGetValue(key, out _), Is.False);
            Assert.That(map.PruneDestroyed(), Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(5, 0)]
        [TestCase(5, 3)]
        [TestCase(5, 5)]
        [TestCase(10000, 5000)]
        public void PruningRemovesExactlyDestroyedKeys(int count, int destroyedCount)
        {
            using UnityObjectMap<GameObject, int> map = new();
            List<GameObject> keys = new(count);
            for (int i = 0; i < count; ++i)
            {
                GameObject key = Track(new GameObject("IdentityKey"));
                keys.Add(key);
                Assert.That(map.TrySet(key, i), Is.True);
            }
            for (int i = 0; i < destroyedCount; ++i)
            {
                Object.DestroyImmediate(keys[i]); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
                Assert.That(map.TryGetValue(keys[i], out int value), Is.True);
                Assert.That(value, Is.EqualTo(i));
            }
            Assert.That(map.PruneDestroyed(), Is.EqualTo(destroyedCount));
            Assert.That(map.Count, Is.EqualTo(count - destroyedCount));
            Assert.That(map.PruneDestroyed(), Is.Zero);
            for (int i = 0; i < count; ++i)
            {
                Assert.That(map.ContainsKey(keys[i]), Is.EqualTo(destroyedCount <= i));
            }
            map.Clear();
            Assert.That(map.Count, Is.Zero);
        }

        [Test]
        public void DestroyedKeysCanBeRemovedBeforePruning()
        {
            using UnityObjectMap<GameObject, int> map = new();
            GameObject key = Track(new GameObject(nameof(key)));
            map.TrySet(key, 7);
            Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
            Assert.That(map.Remove(key), Is.True);
            Assert.That(map.Remove(key), Is.False);
            Assert.That(map.PruneDestroyed(), Is.Zero);
        }

        [Test]
        public void UnloadRegistrationIsIdempotentAndResetClearsSessionState()
        {
            using UnityObjectMap<GameObject, int> automatic = new(true);
            using UnityObjectMap<GameObject, int> manual = new();
            GameObject key = Track(new GameObject(nameof(key)));
            automatic.TrySet(key, 1);
            manual.TrySet(key, 2);
            automatic.AutoPruneOnSceneUnload = true;
            automatic.AutoPruneOnSceneUnload = false;
            automatic.AutoPruneOnSceneUnload = true;
            Object.DestroyImmediate(key); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
            UnityObjectMapLifecycle.OnSceneUnloaded(default);
            UnityObjectMapLifecycle.OnSceneUnloaded(default);
            Assert.That(automatic.Count, Is.Zero);
            Assert.That(manual.Count, Is.EqualTo(1));
            GameObject live = Track(new GameObject(nameof(live)));
            automatic.TrySet(live, 3);
            UnityObjectMapLifecycle.ResetForPlaySession();
            UnityObjectMapLifecycle.ResetForPlaySession();
            Assert.That(automatic.Count, Is.Zero);
            Assert.That(automatic.AutoPruneOnSceneUnload, Is.False);
            automatic.TrySet(live, 4);
            Object.DestroyImmediate(live); // UNH-SUPPRESS UNH001: Destroyed keys are under test; tracked for teardown.
            UnityObjectMapLifecycle.OnSceneUnloaded(default);
            Assert.That(automatic.Count, Is.EqualTo(1));
            automatic.AutoPruneOnSceneUnload = true;
            UnityObjectMapLifecycle.OnSceneUnloaded(default);
            Assert.That(automatic.Count, Is.Zero);
        }
    }
}
