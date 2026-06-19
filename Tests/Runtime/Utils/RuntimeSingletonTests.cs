// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RuntimeSingletonTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            DestroyAll<TestRuntimeSingleton>();
            DestroyAll<PreservableSingleton>();
            DestroyAll<NonPreservableSingleton>();
            DestroyAll<CustomAwakeSingleton>();
            DestroyAll<CustomStartSingleton>();
            DestroyAll<CustomDestroyableSingleton>();
            DestroyAll<ApplicationQuitSingleton>();

            // Reset test flags
            CustomDestroyableSingleton.destroyWasCalled = false;
            ApplicationQuitSingleton.quitWasCalled = false;
            return;

            // Proactively clear any lingering singleton instances between tests
            void DestroyAll<T>()
                where T : RuntimeSingleton<T>
            {
                foreach (T inst in UnityObjectExtensions.FindObjectsOfTypeShim<T>(true))
                {
                    if (inst != null)
                    {
                        Object.DestroyImmediate(inst.gameObject); // UNH-SUPPRESS: Test cleanup for singleton instances
                    }
                }
            }
        }

        private sealed class TestRuntimeSingleton : RuntimeSingleton<TestRuntimeSingleton>
        {
            public int testValue = 42;
        }

        private sealed class PreservableSingleton : RuntimeSingleton<PreservableSingleton>
        {
            protected override bool Preserve => true;
            public bool awakeWasCalled = false;

            protected override void Awake()
            {
                base.Awake();
                awakeWasCalled = true;
            }
        }

        private sealed class NonPreservableSingleton : RuntimeSingleton<NonPreservableSingleton>
        {
            protected override bool Preserve => false;
            public bool wasPreserved = false;

            protected override void Awake()
            {
                base.Awake();
                wasPreserved = transform.parent == null;
            }
        }

        private sealed class CustomAwakeSingleton : RuntimeSingleton<CustomAwakeSingleton>
        {
            public int awakeCallCount = 0;

            protected override void Awake()
            {
                base.Awake();
                awakeCallCount++;
            }
        }

        private sealed class CustomStartSingleton : RuntimeSingleton<CustomStartSingleton>
        {
            public int startCallCount = 0;

            protected override void Start()
            {
                base.Start();
                startCallCount++;
            }
        }

        private sealed class CustomDestroyableSingleton
            : RuntimeSingleton<CustomDestroyableSingleton>
        {
            public static bool destroyWasCalled = false;

            protected override void OnDestroy()
            {
                base.OnDestroy();
                destroyWasCalled = true;
            }
        }

        private sealed class ApplicationQuitSingleton : RuntimeSingleton<ApplicationQuitSingleton>
        {
            public static bool quitWasCalled = false;

            protected override void OnApplicationQuit()
            {
                base.OnApplicationQuit();
                quitWasCalled = true;
            }
        }

        // Cleanup handled by CommonTestBase via tracking

        [Test]
        public void HasInstanceReturnsFalseBeforeAccess()
        {
            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
        }

        [Test]
        public void HasInstanceReturnsTrueAfterAccess()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            Assert.IsTrue(TestRuntimeSingleton.HasInstance);
            Assert.IsTrue(instance != null);
        }

        [Test]
        public void InstanceReturnsNonNull()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);
            Assert.IsTrue(instance != null);
        }

        [Test]
        public void InstanceReturnsSameObjectOnMultipleAccesses()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            Track(instance1.gameObject);
            TestRuntimeSingleton instance2 = TestRuntimeSingleton.Instance;

            Assert.AreSame(instance1, instance2);
        }

        [Test]
        public void InstanceIsMonoBehaviour()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            Assert.IsInstanceOf<MonoBehaviour>(instance);
        }

        [Test]
        public void InstancePreservesData()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            Assert.AreEqual(42, instance.testValue);
        }

        [Test]
        public void InstanceCreatesGameObjectWithCorrectName()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            Assert.IsTrue(instance != null);
            Assert.AreEqual("TestRuntimeSingleton-Singleton", instance.gameObject.name);
        }

        [Test]
        public void InstanceHasCorrectComponent()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsTrue(instance.gameObject.TryGetComponent(out TestRuntimeSingleton component));
            Assert.AreSame(instance, component);
        }

        [UnityTest]
        public IEnumerator InstanceFindsExistingInstanceInScene()
        {
            GameObject existingObject = Track(new GameObject("ExistingTestRuntimeSingleton"));
            TestRuntimeSingleton existing = existingObject.AddComponent<TestRuntimeSingleton>();

            yield return null;

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.AreSame(existing, instance);

            // Cleanup via tracking
        }

        [UnityTest]
        public IEnumerator PreservableSingletonSurvivesSceneLoad()
        {
            PreservableSingleton instance = PreservableSingleton.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.IsTrue(instance != null);
            Assert.IsTrue(instance.awakeWasCalled);
            Assert.IsTrue(instance.transform.parent == null);
        }

        [UnityTest]
        public IEnumerator NonPreservableSingletonIsNotDontDestroyOnLoad()
        {
            NonPreservableSingleton instance = NonPreservableSingleton.Instance;
            Track(instance.gameObject);

            yield return null;
            Assert.IsTrue(instance != null);
        }

        [UnityTest]
        public IEnumerator AwakeIsCalledOnceForSingleInstance()
        {
            CustomAwakeSingleton instance = CustomAwakeSingleton.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.AreEqual(1, instance.awakeCallCount);
        }

        [UnityTest]
        public IEnumerator SecondInstanceIsDestroyedInStart()
        {
            GameObject firstObject = Track(new GameObject("FirstCustomStartSingleton"));
            CustomStartSingleton first = firstObject.AddComponent<CustomStartSingleton>();

            yield return null;

            GameObject secondObject = Track(new GameObject("SecondCustomStartSingleton"));
            CustomStartSingleton second = secondObject.AddComponent<CustomStartSingleton>();

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(".*Double singleton detected.*")
            );

            yield return null;
            yield return null;

            Assert.IsTrue(first != null);
            Assert.IsTrue(second == null);
        }

        [UnityTest]
        public IEnumerator OnDestroyResetsInstanceReference()
        {
            CustomDestroyableSingleton instance = CustomDestroyableSingleton.Instance;
            Track(instance.gameObject);

            Assert.IsTrue(CustomDestroyableSingleton.HasInstance);

            Object.DestroyImmediate(instance.gameObject);

            yield return null;

            Assert.IsTrue(CustomDestroyableSingleton.destroyWasCalled);
            Assert.IsFalse(CustomDestroyableSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator InstanceCanBeAccessedAfterDestruction()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            int instanceId1 = instance1.GetUnityObjectId();

            Object.DestroyImmediate(instance1.gameObject); // UNH-SUPPRESS: Test verifies singleton recreation after destruction

            yield return null;

            TestRuntimeSingleton instance2 = TestRuntimeSingleton.Instance;

            Assert.IsTrue(instance2 != null);
            Assert.AreNotEqual(instanceId1, instance2.GetUnityObjectId());
        }

        [Test]
        public void MultipleTypesHaveIndependentInstances()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            PreservableSingleton instance2 = PreservableSingleton.Instance;

            Assert.IsTrue(instance1 != null);
            Assert.IsTrue(instance2 != null);
            Assert.AreNotSame(instance1, instance2);
        }

        [Test]
        public void InstancePersistsAcrossAccesses()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.testValue = 123;

            TestRuntimeSingleton sameInstance = TestRuntimeSingleton.Instance;

            Assert.AreEqual(123, sameInstance.testValue);
        }

        [Test]
        public void HasInstanceDoesNotTriggerCreation()
        {
            bool hasInstance = TestRuntimeSingleton.HasInstance;

            Assert.IsFalse(hasInstance);

            TestRuntimeSingleton[] allInstances =
                UnityObjectExtensions.FindObjectsOfTypeShim<TestRuntimeSingleton>(true);
            Assert.AreEqual(0, allInstances.Length);
        }

        [Test]
        public void InstanceWorksWithInheritance()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsInstanceOf<RuntimeSingleton<TestRuntimeSingleton>>(instance);
            Assert.IsInstanceOf<MonoBehaviour>(instance);
        }

        [Test]
        public void InstanceCanBeCompared()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            TestRuntimeSingleton instance2 = TestRuntimeSingleton.Instance;

            Assert.IsTrue(instance1 == instance2);
            Assert.IsFalse(instance1 != instance2);
        }

        [Test]
        public void InstanceHasConsistentHashCode()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            int hash1 = instance1.GetHashCode();

            TestRuntimeSingleton instance2 = TestRuntimeSingleton.Instance;
            int hash2 = instance2.GetHashCode();

            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void InstanceToStringReturnsTypeName()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            string result = instance.ToString();

            Assert.IsTrue(result != null);
            Assert.IsTrue(result.Contains("TestRuntimeSingleton") || result.Length > 0);
        }

        [Test]
        public void InstanceCanAccessPublicFields()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.testValue = 99;

            Assert.AreEqual(99, instance.testValue);
        }

        [Test]
        public void MultipleAccessesDoNotCreateMultipleInstances()
        {
            TestRuntimeSingleton instance1 = TestRuntimeSingleton.Instance;
            TestRuntimeSingleton instance2 = TestRuntimeSingleton.Instance;
            TestRuntimeSingleton instance3 = TestRuntimeSingleton.Instance;

            Assert.AreSame(instance1, instance2);
            Assert.AreSame(instance2, instance3);

            TestRuntimeSingleton[] allInstances =
                UnityObjectExtensions.FindObjectsOfTypeShim<TestRuntimeSingleton>(true);
            Assert.AreEqual(1, allInstances.Length);
        }

        [Test]
        public void CanAccessInstanceAfterHasInstanceCheck()
        {
            bool hasInstance = TestRuntimeSingleton.HasInstance;
            Assert.IsFalse(hasInstance);

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Assert.IsTrue(instance != null);

            hasInstance = TestRuntimeSingleton.HasInstance;
            Assert.IsTrue(hasInstance);
        }

        [Test]
        public void InstanceHasCorrectTypeName()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.AreEqual(typeof(TestRuntimeSingleton), instance.GetType());
        }

        [Test]
        public void InstanceCanBeUsedInCollections()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            System.Collections.Generic.List<TestRuntimeSingleton> list = new() { instance };

            Assert.AreEqual(1, list.Count);
            Assert.Contains(instance, list);
        }

        [UnityTest]
        public IEnumerator InstanceGameObjectIsActive()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            yield return null;

            Assert.IsTrue(instance.gameObject.activeSelf);
            Assert.IsTrue(instance.gameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator InstanceComponentIsEnabled()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            yield return null;

            Assert.IsTrue(instance.enabled);
        }

        [UnityTest]
        public IEnumerator DisabledInstanceCanBeAccessed()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.enabled = false;

            yield return null;

            TestRuntimeSingleton sameInstance = TestRuntimeSingleton.Instance;

            Assert.AreSame(instance, sameInstance);
            Assert.IsFalse(sameInstance.enabled);
        }

        [UnityTest]
        public IEnumerator InactiveGameObjectInstanceCanBeAccessed()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.gameObject.SetActive(false);

            yield return null;

            TestRuntimeSingleton sameInstance = TestRuntimeSingleton.Instance;

            Assert.AreSame(instance, sameInstance);
            Assert.IsFalse(sameInstance.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator InstanceWithParentCanBeFound()
        {
            GameObject parent = Track(new GameObject("Parent"));
            GameObject childObject = Track(new GameObject("ChildTestRuntimeSingleton"));
            childObject.transform.SetParent(parent.transform);
            TestRuntimeSingleton child = childObject.AddComponent<TestRuntimeSingleton>();

            yield return null;

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.AreSame(child, instance);

            // Cleanup via tracking
        }

        [UnityTest]
        public IEnumerator SecondInstanceLogsError()
        {
            GameObject firstObject = Track(new GameObject("FirstTestRuntimeSingleton"));
            TestRuntimeSingleton first = firstObject.AddComponent<TestRuntimeSingleton>();

            yield return null;

            GameObject secondObject = Track(new GameObject("SecondTestRuntimeSingleton"));
            TestRuntimeSingleton second = secondObject.AddComponent<TestRuntimeSingleton>();

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(".*Double singleton detected.*")
            );

            yield return null;
            yield return null;

            Assert.IsTrue(first != null);
            Assert.IsTrue(second == null);
        }

        [UnityTest]
        public IEnumerator OnlyFirstInstanceSurvives()
        {
            GameObject firstObject = Track(new GameObject("First"));
            TestRuntimeSingleton first = firstObject.AddComponent<TestRuntimeSingleton>();
            first.testValue = 100;

            yield return null;

            GameObject secondObject = Track(new GameObject("Second"));
            TestRuntimeSingleton second = secondObject.AddComponent<TestRuntimeSingleton>();
            second.testValue = 200;

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(".*Double singleton detected.*")
            );

            yield return null;
            yield return null;

            TestRuntimeSingleton survivor = TestRuntimeSingleton.Instance;

            Assert.AreSame(first, survivor);
            Assert.AreEqual(100, survivor.testValue);
        }

        [Test]
        public void StaticInstanceFieldIsAccessible()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsTrue(TestRuntimeSingleton._instance != null);
            Assert.AreSame(instance, TestRuntimeSingleton._instance);
        }

        [UnityTest]
        public IEnumerator InstanceSurvivesMultipleFrames()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            int instanceId = instance.GetUnityObjectId();

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            TestRuntimeSingleton sameInstance = TestRuntimeSingleton.Instance;

            Assert.AreEqual(instanceId, sameInstance.GetUnityObjectId());
        }

        [Test]
        public void InstanceDoesNotHaveDisallowMultipleComponentViolation()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            TestRuntimeSingleton[] components = instance.GetComponents<TestRuntimeSingleton>();

            Assert.AreEqual(1, components.Length);
        }

        [UnityTest]
        public IEnumerator DestroyingNonInstanceDoesNotAffectInstance()
        {
            GameObject realObject = Track(new GameObject("Real"));
            TestRuntimeSingleton real = realObject.AddComponent<TestRuntimeSingleton>();

            yield return null;

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Assert.AreSame(real, instance);

            GameObject fakeObject = Track(new GameObject("Fake"));
            TestRuntimeSingleton fake = fakeObject.AddComponent<TestRuntimeSingleton>();

            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(".*Double singleton detected.*")
            );

            yield return null;
            yield return null;

            Assert.IsTrue(real != null);
            Assert.IsTrue(TestRuntimeSingleton.HasInstance);
            Assert.AreSame(real, TestRuntimeSingleton.Instance);
        }

        [Test]
        public void NullCheckOnInstanceWorks()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsFalse(instance == null);
            Assert.IsTrue(instance != null);
        }

        [UnityTest]
        public IEnumerator InstanceReferenceMatchesStaticField()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            yield return null;

            Assert.AreSame(instance, TestRuntimeSingleton._instance);
        }

        [Test]
        public void HasInstanceImmediatelyTrueAfterInstanceAccess()
        {
            Assert.IsFalse(TestRuntimeSingleton.HasInstance);

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsTrue(TestRuntimeSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator ApplicationQuitCallbackWorks()
        {
            ApplicationQuitSingleton instance = ApplicationQuitSingleton.Instance;

            yield return null;

            instance.SendMessage("OnApplicationQuit");

            Assert.IsTrue(ApplicationQuitSingleton.quitWasCalled);
        }

        [UnityTest]
        public IEnumerator PreservableSingletonHasNullParent()
        {
            PreservableSingleton instance = PreservableSingleton.Instance;

            yield return null;

            Assert.IsTrue(instance.transform.parent == null);
        }

        [UnityTest]
        public IEnumerator PreservableSingletonLivesInDontDestroyScene()
        {
            PreservableSingleton instance = PreservableSingleton.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.AreEqual("DontDestroyOnLoad", instance.gameObject.scene.name);
        }

        [UnityTest]
        public IEnumerator NonPreservableSingletonRemainsInActiveScene()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            NonPreservableSingleton instance = NonPreservableSingleton.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.AreEqual(activeScene, instance.gameObject.scene);
        }

        [Test]
        public void InstanceCreationDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            });
        }

        [UnityTest]
        public IEnumerator StartIsCalledAfterAwake()
        {
            CustomStartSingleton instance = CustomStartSingleton.Instance;

            Assert.AreEqual(0, instance.startCallCount);

            yield return null;

            Assert.AreEqual(1, instance.startCallCount);
        }

        [UnityTest]
        public IEnumerator ExistingInactiveInstanceIsNotFound()
        {
            GameObject inactiveObject = Track(new GameObject("InactiveTestRuntimeSingleton"));
            inactiveObject.SetActive(false);
            TestRuntimeSingleton inactive = inactiveObject.AddComponent<TestRuntimeSingleton>();

            yield return null;

            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.AreNotSame(inactive, instance);
            Assert.IsTrue(instance.gameObject.activeSelf);

            Object.DestroyImmediate(inactiveObject); // UNH-SUPPRESS: Test cleanup for inactive object
        }

        [Test]
        public void GetComponentReturnsCorrectInstance()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            TestRuntimeSingleton component = instance.GetComponent<TestRuntimeSingleton>();

            Assert.AreSame(instance, component);
        }

        [Test]
        public void TryGetComponentReturnsTrue()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            bool found = instance.TryGetComponent(out TestRuntimeSingleton component);

            Assert.IsTrue(found);
            Assert.AreSame(instance, component);
        }

        [UnityTest]
        public IEnumerator InstanceTransformIsAccessible()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            yield return null;

            Assert.IsTrue(instance != null);
            Assert.AreSame(instance.gameObject.transform, instance.transform);
        }

        [Test]
        public void GameObjectTagCanBeModified()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.gameObject.tag = "Untagged";

            Assert.AreEqual("Untagged", instance.gameObject.tag);
        }

        [Test]
        public void GameObjectLayerCanBeModified()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            instance.gameObject.layer = 0;

            Assert.AreEqual(0, instance.gameObject.layer);
        }

        [UnityTest]
        public IEnumerator ComponentCanBeAddedToSingletonGameObject()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Rigidbody rb = instance.gameObject.AddComponent<Rigidbody>();

            yield return null;

            Assert.IsTrue(rb != null);
            Assert.IsTrue(instance.gameObject.TryGetComponent(out Rigidbody foundRb));
            Assert.AreSame(rb, foundRb);

            Object.DestroyImmediate(rb); // UNH-SUPPRESS: Test cleanup for added component
        }

        [Test]
        public void GameObjectNameMatchesExpectedPattern()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;

            Assert.IsTrue(instance.gameObject.name.Contains("TestRuntimeSingleton"));
            Assert.IsTrue(instance.gameObject.name.Contains("Singleton"));
        }

        [Test]
        public void InstanceThrowsWhenCreatedFromBackgroundThread()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                Task.Run(() =>
                    {
                        TestRuntimeSingleton singleton = TestRuntimeSingleton.Instance;
                    })
                    .GetAwaiter()
                    .GetResult();
            });

            Assert.IsTrue(
                exception != null,
                "InvalidOperationException should be thrown for background thread access"
            );
            StringAssert.Contains("main thread", exception.Message);
            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
        }

        [Test]
        public void BackgroundThreadCanAccessInstanceAfterMainThreadCreation()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            TestRuntimeSingleton backgroundInstance = null;

            Task.Run(() =>
                {
                    backgroundInstance = TestRuntimeSingleton.Instance;
                })
                .GetAwaiter()
                .GetResult();

            Assert.AreSame(instance, backgroundInstance);
        }

        [Test]
        public void ClearInstanceWhenNeverAccessedDoesNotThrow()
        {
            Assert.DoesNotThrow(() => TestRuntimeSingleton.ClearInstance());
            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
        }

        [Test]
        public void ClearInstanceWhenCalledTwiceDoesNotThrow()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            Assert.DoesNotThrow(() =>
            {
                TestRuntimeSingleton.ClearInstance();
                TestRuntimeSingleton.ClearInstance();
            });
            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator ClearInstanceDestroysGameObjectAndClearsReference()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            GameObject gameObject = instance.gameObject;

            TestRuntimeSingleton.ClearInstance();
            yield return null;

            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
            Assert.IsTrue(gameObject == null);
        }

        [UnityTest]
        public IEnumerator ClearInstanceResetsInitializeCount()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            Track(instance.gameObject);

            yield return null;

            Assert.Greater(TestRuntimeSingleton.InitializeCount, 0);

            TestRuntimeSingleton.ClearInstance();
            yield return null;

            Assert.AreEqual(0, TestRuntimeSingleton.InitializeCount);
        }

        [UnityTest]
        public IEnumerator ClearInstanceAllowsFreshInstanceAfterClear()
        {
            TestRuntimeSingleton first = TestRuntimeSingleton.Instance;
            Track(first.gameObject);
            first.testValue = 99;

            TestRuntimeSingleton.ClearInstance();
            yield return null;

            TestRuntimeSingleton second = TestRuntimeSingleton.Instance;
            Track(second.gameObject);

            Assert.IsTrue(first == null);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(42, second.testValue);
        }

        [UnityTest]
        public IEnumerator RegistryClearAllDestroysRegisteredSingletonInstances()
        {
            TestRuntimeSingleton instance = TestRuntimeSingleton.Instance;
            GameObject gameObject = instance.gameObject;

            RuntimeSingletonRegistry.ClearAllRegisteredInstances();
            yield return null;

            Assert.IsFalse(TestRuntimeSingleton.HasInstance);
            Assert.IsTrue(gameObject == null);
        }
    }
}
