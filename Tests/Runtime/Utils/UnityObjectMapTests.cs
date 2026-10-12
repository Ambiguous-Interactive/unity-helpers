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
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class UnityObjectMapTests : CommonTestBase
    {
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
