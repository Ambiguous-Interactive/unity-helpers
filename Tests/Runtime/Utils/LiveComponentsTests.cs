// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using AllocationIs = UnityEngine.TestTools.Constraints.Is;
#if !SINGLE_THREADED && !UNITY_WEBGL
    using System.Threading;
#endif

    [TestFixture]
    [Category("Fast")]
    public sealed class LiveComponentsTests : CommonTestBase
    {
        [SetUp]
        public void ResetRegistrations()
        {
            Assert.IsTrue(UnityMainThreadGuard.IsInitialized);
            LiveComponentRegistry.OnSubsystemRegistration();
        }

        [Test]
        public void EmptyRegistryClearsQueryOutputsAndCallerList()
        {
            Assert.AreEqual(0, LiveComponents<Transform>.Count);
            Assert.IsFalse(LiveComponents<Transform>.TryGetFirst(out Transform first));
            Assert.IsTrue(first == null);
            Assert.IsFalse(LiveComponents<Transform>.TryGetIn(default, out Transform inScene));
            Assert.IsTrue(inScene == null);
            Transform unrelated = Track(new GameObject("Unrelated")).transform;
            List<Transform> destination = new() { unrelated };
            LiveComponents<Transform>.CopyTo(destination);
            Assert.IsEmpty(destination);
            Assert.DoesNotThrow(() => LiveComponents<Transform>.CopyTo(null));
            Assert.IsFalse(LiveComponents<Transform>.Register(null));
            Assert.IsFalse(LiveComponents<Transform>.Unregister(null));
            Assert.IsFalse(LiveComponents<Transform>.Unregister(unrelated));
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(32)]
        public void RegistrationsAreUniqueOrderedAndIndependentOfOtherTypes(int count)
        {
            List<Transform> expected = new(count);
            for (int index = 0; index < count; ++index)
            {
                Transform item = Track(new GameObject("Registered")).transform;
                Assert.IsTrue(LiveComponents<Transform>.Register(item));
                Assert.IsFalse(LiveComponents<Transform>.Register(item));
                expected.Add(item);
            }
            Assert.AreEqual(count, LiveComponents<Transform>.Count);
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsTrue(LiveComponents<Transform>.TryGetFirst(out Transform first));
            Assert.AreSame(expected[0], first);
            List<Transform> destination = new();
            LiveComponents<Transform>.CopyTo(destination);
            CollectionAssert.AreEqual(expected, destination);
            Transform removed = expected[0];
            Assert.IsTrue(LiveComponents<Transform>.Unregister(removed));
            Assert.IsFalse(LiveComponents<Transform>.Unregister(removed));
            expected.RemoveAt(0);
            Assert.IsTrue(LiveComponents<Transform>.Register(removed));
            expected.Add(removed);
            LiveComponents<Transform>.CopyTo(destination);
            CollectionAssert.AreEqual(expected, destination);
        }

        [Test]
        public void LifecycleRegistrationTracksEnableDisableAndDestroy()
        {
            GameObject owner = Track(new GameObject("Lifecycle"));
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            component.enabled = false;
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            component.enabled = true;
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            owner.SetActive(false);
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            owner.SetActive(true);
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            UnityEngine.Object.DestroyImmediate(owner); // UNH-SUPPRESS: Exercise OnDisable and OnDestroy registration cleanup.
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out _));
        }

        [Test]
        public void NeverEnabledObjectsAreNotDiscoveredByRegistryQueries()
        {
            GameObject owner = Track(new GameObject("InitiallyInactive"));
            owner.SetActive(false);
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out _));
            owner.SetActive(true);
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent found
                )
            );
            Assert.AreSame(component, found);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualRegistrationsAreFilteredByCurrentActiveState(bool behaviour)
        {
            GameObject owner = Track(new GameObject("Manual"));
            if (behaviour)
            {
                AudioListener listener = owner.AddComponent<AudioListener>();
                Assert.IsTrue(LiveComponents<AudioListener>.Register(listener));
                listener.enabled = false;
                Assert.AreEqual(0, LiveComponents<AudioListener>.Count);
                Assert.IsFalse(LiveComponents<AudioListener>.TryGetFirst(out _));
                listener.enabled = true;
                Assert.AreEqual(1, LiveComponents<AudioListener>.Count);
            }
            else
            {
                Assert.IsTrue(LiveComponents<Transform>.Register(owner.transform));
                owner.SetActive(false);
                Assert.AreEqual(0, LiveComponents<Transform>.Count);
                Assert.IsFalse(LiveComponents<Transform>.TryGetFirst(out _));
                owner.SetActive(true);
                Assert.AreEqual(1, LiveComponents<Transform>.Count);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void DestroyedEntriesArePrunedWithoutChangingSurvivorOrder(int surface)
        {
            Transform first = Track(new GameObject("First")).transform;
            Transform removed = Track(new GameObject("Removed")).transform;
            Transform last = Track(new GameObject("Last")).transform;
            LiveComponents<Transform>.Register(first);
            LiveComponents<Transform>.Register(removed);
            LiveComponents<Transform>.Register(last);
            UnityEngine.Object.DestroyImmediate(removed.gameObject); // UNH-SUPPRESS: Preserve a destroyed managed reference in the registry.
            List<Transform> destination = new();
            switch (surface)
            {
                case 0:
                    Assert.AreEqual(2, LiveComponents<Transform>.Count);
                    break;
                case 1:
                    Assert.IsTrue(LiveComponents<Transform>.TryGetFirst(out Transform found));
                    Assert.AreSame(first, found);
                    break;
                case 2:
                    Assert.IsTrue(
                        LiveComponents<Transform>.TryGetIn(
                            first.gameObject.scene,
                            out Transform inScene
                        )
                    );
                    Assert.AreSame(first, inScene);
                    break;
                default:
                    LiveComponents<Transform>.CopyTo(destination);
                    CollectionAssert.AreEqual(new[] { first, last }, destination);
                    break;
            }
            Assert.IsFalse(LiveComponents<Transform>.Register(removed));
            LiveComponents<Transform>.CopyTo(destination);
            CollectionAssert.AreEqual(new[] { first, last }, destination);
        }

        [Test]
        public void DestroyedManagedReferencesCanBeExplicitlyUnregistered()
        {
            Transform item = Track(new GameObject("Destroyed")).transform;
            LiveComponents<Transform>.Register(item);
            UnityEngine.Object.DestroyImmediate(item.gameObject); // UNH-SUPPRESS: Verify managed-identity removal after native destruction.
            Assert.IsTrue(LiveComponents<Transform>.Unregister(item));
            Assert.AreEqual(0, LiveComponents<Transform>.Count);
        }

        [Test]
        public void SceneQueriesFollowTheCurrentSceneAfterAnObjectMoves()
        {
            Scene firstScene = CreateTempScene("LiveRegistryFirst", setActive: false);
            Scene secondScene = CreateTempScene("LiveRegistrySecond", setActive: false);
            GameObject first = Track(new GameObject("FirstSceneEntry"));
            GameObject second = Track(new GameObject("SecondSceneEntry"));
            SceneManager.MoveGameObjectToScene(first, firstScene);
            SceneManager.MoveGameObjectToScene(second, secondScene);
            LiveComponents<Transform>.Register(first.transform);
            LiveComponents<Transform>.Register(second.transform);
            Assert.IsTrue(LiveComponents<Transform>.TryGetIn(firstScene, out Transform inFirst));
            Assert.AreSame(first.transform, inFirst);
            Assert.IsTrue(LiveComponents<Transform>.TryGetIn(secondScene, out Transform inSecond));
            Assert.AreSame(second.transform, inSecond);
            SceneManager.MoveGameObjectToScene(first, secondScene);
            Assert.IsFalse(LiveComponents<Transform>.TryGetIn(firstScene, out Transform missing));
            Assert.IsTrue(missing == null);
            Assert.IsTrue(LiveComponents<Transform>.TryGetIn(secondScene, out Transform moved));
            Assert.AreSame(first.transform, moved);
        }

        [Test]
        public void SubsystemRegistrationClearsEveryClosedRegistryAndAllowsReregistration()
        {
            GameObject owner = Track(new GameObject("PreviousSession"));
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            LiveComponents<Transform>.Register(owner.transform);
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.AreEqual(1, LiveComponents<Transform>.Count);
            LiveComponentRegistry.OnSubsystemRegistration();
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.AreEqual(0, LiveComponents<Transform>.Count);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out _));
            Assert.IsTrue(LiveComponents<Transform>.Register(owner.transform));
            component.enabled = false;
            component.enabled = true;
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent found
                )
            );
            Assert.AreSame(component, found);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(16)]
        public void WarmQueriesAndCopiesDoNotAllocate(int count)
        {
            AllocationProbe.IgnoreWhenUnmeasurable();
            List<Transform> destination = new(count);
            for (int index = 0; index < count; ++index)
            {
                LiveComponents<Transform>.Register(
                    Track(new GameObject("AllocationEntry")).transform
                );
            }
            Scene scene = SceneManager.GetActiveScene();
            TestDelegate query = () =>
            {
                LiveComponents<Transform>.CopyTo(destination);
                _ = LiveComponents<Transform>.TryGetFirst(out _);
                _ = LiveComponents<Transform>.TryGetIn(scene, out _);
                _ = LiveComponents<Transform>.Count;
            };
            query();
            Assert.That(query, AllocationIs.Not.AllocatingGCMemory());
            Assert.AreEqual(count, destination.Count);
        }

#if !SINGLE_THREADED && !UNITY_WEBGL
        [Test]
        public void WorkerAccessDoesNotQueryUnityOrChangeRegistrationsAndBuffers()
        {
            Transform item = Track(new GameObject("MainThreadEntry")).transform;
            LiveComponents<Transform>.Register(item);
            List<Transform> destination = new() { item };
            Exception failure = null;
            bool registered = true;
            bool removed = true;
            bool found = true;
            int count = -1;
            Thread worker = new(() =>
            {
                try
                {
                    registered = LiveComponents<Transform>.Register(item);
                    removed = LiveComponents<Transform>.Unregister(item);
                    found = LiveComponents<Transform>.TryGetFirst(out _);
                    count = LiveComponents<Transform>.Count;
                    LiveComponents<Transform>.CopyTo(destination);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(failure == null, failure?.ToString());
            Assert.IsFalse(registered);
            Assert.IsFalse(removed);
            Assert.IsFalse(found);
            Assert.AreEqual(0, count);
            Assert.AreEqual(1, LiveComponents<Transform>.Count);
            CollectionAssert.AreEqual(new[] { item }, destination);
        }
#endif
    }
}
