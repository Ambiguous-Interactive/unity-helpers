// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
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
            CreateTempScene("LiveRegistryFixture");
            LiveComponentRegistry.OnSubsystemRegistration();
        }

        public override void TearDown()
        {
            try
            {
                base.TearDown();
            }
            finally
            {
#if UNITY_EDITOR
                CleanupTrackedFoldersAndAssets();
#endif
            }
        }

        [Test]
        public void EmptyQueriesClearOutputsAndCallerList()
        {
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsFalse(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent first
                )
            );
            Assert.IsTrue(first == null);
            Assert.IsFalse(
                LiveComponents<LiveRegistryTestComponent>.TryGetIn(
                    default,
                    out LiveRegistryTestComponent inScene
                )
            );
            Assert.IsTrue(inScene == null);
            List<LiveRegistryTestComponent> destination = new() { null };
            LiveComponents<LiveRegistryTestComponent>.CopyTo(destination);
            Assert.IsEmpty(destination);
            Assert.DoesNotThrow(() => LiveComponents<LiveRegistryTestComponent>.CopyTo(null));
        }

        [Test]
        public void NonComponentClassesAreEmptyWithoutCallingUnityDiscovery()
        {
            List<string> destination = new() { "existing" };
            Assert.AreEqual(0, LiveComponents<string>.Count);
            Assert.IsFalse(LiveComponents<string>.TryGetFirst(out string found));
            Assert.IsTrue(found == null);
            Assert.IsFalse(LiveComponents<string>.TryGetIn(SceneManager.GetActiveScene(), out _));
            LiveComponents<string>.CopyTo(destination);
            Assert.IsEmpty(destination);
        }

        [TestCase(1)]
        [TestCase(4)]
        [TestCase(32)]
        public void QueriesDiscoverOnlyMatchingTypesWithoutObjectSetup(int count)
        {
            List<LiveRegistryTestComponent> expected = new(count);
            for (int index = 0; index < count; ++index)
            {
                expected.Add(
                    Track(new GameObject("Discovered")).AddComponent<LiveRegistryTestComponent>()
                );
            }
            Assert.AreEqual(count, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.AreEqual(0, LiveComponents<LiveRegistryDerivedComponent>.Count);
            List<LiveRegistryTestComponent> actual = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(actual);
            CollectionAssert.AreEquivalent(expected, actual);
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent first
                )
            );
            Assert.AreSame(actual[0], first);
        }

        [Test]
        public void BaseClassAndInterfaceQueriesIncludeDerivedComponents()
        {
            LiveRegistryTestComponent first = Track(new GameObject("Base"))
                .AddComponent<LiveRegistryTestComponent>();
            LiveRegistryDerivedComponent second = Track(new GameObject("Derived"))
                .AddComponent<LiveRegistryDerivedComponent>();
            List<LiveRegistryTestComponent> classes = new();
            List<ILiveRegistryTestComponent> interfaces = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(classes);
            LiveComponents<ILiveRegistryTestComponent>.CopyTo(interfaces);
            CollectionAssert.AreEquivalent(
                new LiveRegistryTestComponent[] { first, second },
                classes
            );
            CollectionAssert.AreEquivalent(
                new ILiveRegistryTestComponent[] { first, second },
                interfaces
            );
            Assert.AreEqual(1, LiveComponents<LiveRegistryDerivedComponent>.Count);
            Assert.IsTrue(LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(out _));
            Assert.IsTrue(
                LiveComponents<ILiveRegistryTestComponent>.TryGetIn(second.gameObject.scene, out _)
            );
            second.enabled = false;
            Assert.AreEqual(1, LiveComponents<ILiveRegistryTestComponent>.Count);
            UnityEngine.Object.DestroyImmediate(first.gameObject); // UNH-SUPPRESS: Verify interface references obey Unity destruction.
            Assert.IsFalse(LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(out _));
            second.enabled = true;
            Assert.IsTrue(
                LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(
                    out ILiveRegistryTestComponent remaining
                )
            );
            Assert.AreSame(second, remaining);
        }

        [Test]
        public void ThirdPartyComponentsAreDiscoveredWithoutAdapters()
        {
            AudioListener listener = Track(new GameObject("ThirdParty"))
                .AddComponent<AudioListener>();
            Assert.IsTrue(LiveComponents<AudioListener>.TryGetFirst(out AudioListener found));
            Assert.AreSame(listener, found);
            listener.enabled = false;
            Assert.AreEqual(0, LiveComponents<AudioListener>.Count);
            listener.enabled = true;
            Assert.AreEqual(1, LiveComponents<AudioListener>.Count);
        }

        [Test]
        public void NonBehaviourQueriesFilterInactiveObjectsAndExcludeTheInternalAnchor()
        {
            GameObject owner = Track(new GameObject("PlainComponent"));
            List<Transform> actual = new();
            LiveComponents<Transform>.CopyTo(actual);
            Assert.IsTrue(actual.Contains(owner.transform));
            foreach (Transform component in actual)
            {
                Assert.IsFalse(LiveComponentRegistry.IsAnchor(component.gameObject));
            }
            owner.SetActive(false);
            LiveComponents<Transform>.CopyTo(actual);
            Assert.IsFalse(actual.Contains(owner.transform));
            owner.SetActive(true);
            LiveComponents<Transform>.CopyTo(actual);
            Assert.IsTrue(actual.Contains(owner.transform));
            Assert.AreEqual(actual.Count, LiveComponents<Transform>.Count);
        }

        [Test]
        public void LaterSameFrameAdditionsAreVisibleAfterPositiveAndNegativeQueries()
        {
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out _));
            GameObject owner = Track(new GameObject("LateComponents"));
            LiveRegistryTestComponent first = owner.AddComponent<LiveRegistryTestComponent>();
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent found
                )
            );
            Assert.AreSame(first, found);
            LiveRegistryTestComponent second = owner.AddComponent<LiveRegistryTestComponent>();
            Assert.AreEqual(2, LiveComponents<LiveRegistryTestComponent>.Count);
            List<LiveRegistryTestComponent> actual = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(actual);
            CollectionAssert.AreEqual(new[] { first, second }, actual);
            UnityEngine.Object.DestroyImmediate(first); // UNH-SUPPRESS: Verify a cache miss discovers the surviving component immediately.
            Assert.IsTrue(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out found));
            Assert.AreSame(second, found);
        }

        [Test]
        public void InstantiatedDescendantsAreDiscoveredInTheSameFrame()
        {
            GameObject parent = Track(new GameObject("Hierarchy"));
            GameObject child = Track(new GameObject("Child"));
            child.transform.SetParent(parent.transform);
            LiveRegistryTestComponent original = child.AddComponent<LiveRegistryTestComponent>();
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            GameObject clone = Track(UnityEngine.Object.Instantiate(parent));
            List<LiveRegistryTestComponent> actual = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(actual);
            CollectionAssert.AreEquivalent(
                new[] { original, clone.GetComponentInChildren<LiveRegistryTestComponent>() },
                actual
            );
        }

        [Test]
        public void AwakeCanQueryItsOwnTypeAfterAnEarlierMiss()
        {
            Assert.IsFalse(LiveComponents<LiveRegistryAwakeProbe>.TryGetFirst(out _));
            LiveRegistryAwakeProbe first = Track(new GameObject("EarlyAwake"))
                .AddComponent<LiveRegistryAwakeProbe>();
            Assert.IsTrue(first.foundSelf);
            Assert.AreEqual(1, first.observedCount);
            LiveRegistryAwakeProbe second = Track(new GameObject("LaterAwake"))
                .AddComponent<LiveRegistryAwakeProbe>();
            Assert.IsTrue(second.foundSelf);
            Assert.AreEqual(2, second.observedCount);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator SceneLoadAwakeDiscoversAllPeersBeforeTheirEnableCallbacks()
        {
            const string path =
                "Packages/com.wallstop-studios.unity-helpers/Tests/Runtime/Scenes/LiveRegistryAwake.unity";
            Assert.AreEqual(0, LiveComponents<LiveRegistryAwakeProbe>.Count);
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                path,
                new LoadSceneParameters(LoadSceneMode.Additive)
            );
            List<LiveRegistryAwakeProbe> actual = new();
            LiveComponents<LiveRegistryAwakeProbe>.CopyTo(actual);
            Assert.AreEqual(2, actual.Count);
            _trackedScenes.Add(actual[0].gameObject.scene);
            foreach (LiveRegistryAwakeProbe probe in actual)
            {
                Assert.IsTrue(probe.foundSelf);
                Assert.AreEqual(2, probe.observedCount);
            }
            yield return SceneManager.UnloadSceneAsync(actual[0].gameObject.scene);
        }
#endif

        [Test]
        public void QueriesTrackEnableDisableAndDestroyWithoutChangingCallbacks()
        {
            GameObject owner = Track(new GameObject("Lifecycle"));
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.AreEqual(1, component.enableCalls);
            component.enabled = false;
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            component.enabled = true;
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            owner.SetActive(false);
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            owner.SetActive(true);
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            UnityEngine.Object.DestroyImmediate(owner); // UNH-SUPPRESS: Exercise native disable and destroy callbacks.
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out _));
            Assert.AreEqual(3, component.enableCalls);
            Assert.AreEqual(3, component.disableCalls);
            Assert.AreEqual(1, component.destroyCalls);
        }

        [Test]
        public void InitiallyInactiveComponentsBecomeVisibleWithoutRefresh()
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

        [Test]
        public void FirstDiscoveryOrderSurvivesHierarchyChangesAndDestruction()
        {
            LiveRegistryTestComponent first = Track(new GameObject("First"))
                .AddComponent<LiveRegistryTestComponent>();
            LiveRegistryTestComponent removed = Track(new GameObject("Removed"))
                .AddComponent<LiveRegistryTestComponent>();
            LiveRegistryTestComponent last = Track(new GameObject("Last"))
                .AddComponent<LiveRegistryTestComponent>();
            List<LiveRegistryTestComponent> expected = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(expected);
            last.transform.SetSiblingIndex(0);
            UnityEngine.Object.DestroyImmediate(removed.gameObject); // UNH-SUPPRESS: Verify surviving discovery order after native destruction.
            expected.Remove(removed);
            List<LiveRegistryTestComponent> actual = new();
            LiveComponents<LiveRegistryTestComponent>.CopyTo(actual);
            CollectionAssert.AreEqual(expected, actual);
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent found
                )
            );
            Assert.AreSame(expected[0], found);
            Assert.IsTrue(actual.Contains(first));
        }

        [Test]
        public void SceneQueriesDiscoverAdditionsAndFollowMovedObjects()
        {
            Scene firstScene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
                    "Assets/LiveRegistryScene.unity"
                );
                TrackAssetPath(path);
                Assert.IsTrue(
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(firstScene, path)
                );
            }
#endif
            Scene secondScene = CreateTempScene(
                "LiveRegistrySecond",
                setActive: false,
                additive: true
            );
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetIn(secondScene, out _));
            GameObject owner = Track(new GameObject("Moved"));
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            SceneManager.MoveGameObjectToScene(owner, firstScene);
            Assert.IsTrue(LiveComponents<LiveRegistryTestComponent>.TryGetIn(firstScene, out _));
            SceneManager.MoveGameObjectToScene(owner, secondScene);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetIn(firstScene, out _));
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetIn(
                    secondScene,
                    out LiveRegistryTestComponent moved
                )
            );
            Assert.AreSame(component, moved);
        }

        [UnityTest]
        public IEnumerator UnloadedScenesLoseTheirComponents()
        {
            Scene scene = SceneManager.CreateScene("LiveRegistryUnload");
            GameObject owner = new("UnloadedEntry");
            owner.AddComponent<LiveRegistryTestComponent>();
            SceneManager.MoveGameObjectToScene(owner, scene);
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            yield return SceneManager.UnloadSceneAsync(scene);
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsFalse(LiveComponents<LiveRegistryTestComponent>.TryGetIn(scene, out _));
        }

        [UnityTest]
        public IEnumerator PersistentObjectsAddedAfterWarmupRemainVisibleAcrossSceneUnload()
        {
            Assert.AreEqual(0, LiveComponents<LiveRegistryTestComponent>.Count);
            GameObject owner = Track(new GameObject("PersistentEntry"));
            LiveRegistryTestComponent component = owner.AddComponent<LiveRegistryTestComponent>();
            UnityEngine.Object.DontDestroyOnLoad(owner);
            Scene persistent = owner.scene;
            Assert.IsTrue(
                LiveComponents<LiveRegistryTestComponent>.TryGetFirst(
                    out LiveRegistryTestComponent found
                )
            );
            Assert.AreSame(component, found);
            Assert.IsTrue(LiveComponents<LiveRegistryTestComponent>.TryGetIn(persistent, out _));
            Scene temporary = SceneManager.CreateScene("LiveRegistryTemporary");
            yield return SceneManager.UnloadSceneAsync(temporary);
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            LiveComponentRegistry.OnSubsystemRegistration();
            Assert.IsTrue(LiveComponents<LiveRegistryTestComponent>.TryGetFirst(out found));
            Assert.AreSame(component, found);
        }

        [Test]
        public void SubsystemResetRediscoversExistingObjectsAcrossAllQueriedTypes()
        {
            LiveRegistryDerivedComponent component = Track(new GameObject("SurvivingSession"))
                .AddComponent<LiveRegistryDerivedComponent>();
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.AreEqual(1, LiveComponents<ILiveRegistryTestComponent>.Count);
            LiveComponentRegistry.OnSubsystemRegistration();
            Assert.AreEqual(1, LiveComponents<LiveRegistryTestComponent>.Count);
            Assert.IsTrue(
                LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(
                    out ILiveRegistryTestComponent found
                )
            );
            Assert.AreSame(component, found);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(16)]
        public void WarmQueriesAndCompleteCopiesDoNotAllocate(int count)
        {
            AllocationProbe.IgnoreWhenUnmeasurable();
            List<ILiveRegistryTestComponent> destination = new(count);
            for (int index = 0; index < count; ++index)
            {
                Track(new GameObject("AllocationEntry")).AddComponent<LiveRegistryTestComponent>();
            }
            Scene scene = SceneManager.GetActiveScene();
            TestDelegate query = () =>
            {
                LiveComponents<ILiveRegistryTestComponent>.CopyTo(destination);
                _ = LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(out _);
                _ = LiveComponents<ILiveRegistryTestComponent>.TryGetIn(scene, out _);
                _ = LiveComponents<ILiveRegistryTestComponent>.Count;
            };
            query();
            Assert.That(query, AllocationIs.Not.AllocatingGCMemory());
            Assert.AreEqual(count, destination.Count);
        }

#if !SINGLE_THREADED && !UNITY_WEBGL
        [Test]
        public void WorkerAccessDoesNotQueryUnityOrChangeBuffers()
        {
            LiveRegistryTestComponent item = Track(new GameObject("MainThreadEntry"))
                .AddComponent<LiveRegistryTestComponent>();
            List<ILiveRegistryTestComponent> destination = new() { item };
            Exception failure = null;
            bool found = true;
            bool inScene = true;
            int count = -1;
            Scene scene = item.gameObject.scene;
            Thread worker = new(() =>
            {
                try
                {
                    found = LiveComponents<ILiveRegistryTestComponent>.TryGetFirst(out _);
                    inScene = LiveComponents<ILiveRegistryTestComponent>.TryGetIn(scene, out _);
                    count = LiveComponents<ILiveRegistryTestComponent>.Count;
                    LiveComponents<ILiveRegistryTestComponent>.CopyTo(destination);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            worker.Start();
            Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(failure == null, failure?.ToString());
            Assert.IsFalse(found);
            Assert.IsFalse(inScene);
            Assert.AreEqual(0, count);
            Assert.AreEqual(1, LiveComponents<ILiveRegistryTestComponent>.Count);
            CollectionAssert.AreEqual(new ILiveRegistryTestComponent[] { item }, destination);
        }
#endif
    }
}
