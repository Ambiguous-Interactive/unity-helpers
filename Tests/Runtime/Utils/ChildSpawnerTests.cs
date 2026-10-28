// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ChildSpawnerTests : CommonTestBase
    {
        private static void InvokeStartOnce(ChildSpawner childSpawner)
        {
            childSpawner.SendMessage(nameof(ChildSpawnMethod.Start));
            childSpawner._spawnMethod = ChildSpawnMethod.Awake;
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ChildSpawner.ClearSpawnedPrefabs();
        }

        [TearDown]
        public override void TearDown()
        {
            ChildSpawner.ClearSpawnedPrefabs();
            base.TearDown();
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void RecreatesDestroyedChildrenOnTheSameSpawner(
            bool persistentSpawner,
            bool persistentPrefab
        )
        {
            GameObject prefab = Track(new GameObject("RespawnPrefab"));
            if (persistentPrefab)
            {
                Object.DontDestroyOnLoad(prefab);
            }
            ChildSpawner spawner = CreateSpawner(prefab, persistentSpawner);
            InvokeStartOnce(spawner);
            Assert.That(spawner.transform.childCount, Is.EqualTo(1));
            GameObject original = spawner.transform.GetChild(0).gameObject;
            Object.DestroyImmediate(original); // UNH-SUPPRESS: Verify spawning after destruction.

            spawner._spawnMethod = ChildSpawnMethod.Start;
            InvokeStartOnce(spawner);

            Assert.That(spawner.transform.childCount, Is.EqualTo(1));
            Assert.That(
                ReferenceEquals(original, spawner.transform.GetChild(0).gameObject),
                Is.False
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RecreatesDestroyedPersistentChildrenOnAnotherSpawner(bool persistentPrefab)
        {
            GameObject prefab = Track(new GameObject("GlobalPrefab"));
            if (persistentPrefab)
            {
                Object.DontDestroyOnLoad(prefab);
            }
            ChildSpawner first = CreateSpawner(prefab, !persistentPrefab);
            InvokeStartOnce(first);
            GameObject original = first.transform.GetChild(0).gameObject;
            ChildSpawner second = CreateSpawner(prefab, !persistentPrefab);
            InvokeStartOnce(second);
            Assert.That(
                second.transform.childCount,
                Is.Zero,
                "A live persistent instance must be reused."
            );

            Object.DestroyImmediate(original); // UNH-SUPPRESS: Verify global spawn tracking after destruction.
            second._spawnMethod = ChildSpawnMethod.Start;
            InvokeStartOnce(second);

            Assert.That(second.transform.childCount, Is.EqualTo(1));
            ChildSpawner third = CreateSpawner(prefab, !persistentPrefab);
            InvokeStartOnce(third);
            Assert.That(third.transform.childCount, Is.Zero);
        }

        [Test]
        public void PersistentChildRemainsTrackedAfterItsSpawnerIsDestroyed()
        {
            GameObject prefab = Track(new GameObject("DetachedPrefab"));
            ChildSpawner first = CreateSpawner(prefab, true);
            InvokeStartOnce(first);
            GameObject original = Track(first.transform.GetChild(0).gameObject);
            original.transform.SetParent(null);
            Object.DontDestroyOnLoad(original);
            Object.DestroyImmediate(first.gameObject); // UNH-SUPPRESS: Verify tracking follows the child rather than its spawner.

            ChildSpawner second = CreateSpawner(prefab, true);
            InvokeStartOnce(second);
            Assert.That(second.transform.childCount, Is.Zero);
            Object.DestroyImmediate(original); // UNH-SUPPRESS: Verify the detached child's destruction allows a replacement.
            second._spawnMethod = ChildSpawnMethod.Start;
            InvokeStartOnce(second);
            Assert.That(second.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void DestroyingOnePersistentChildPreservesOtherPrefabInstances()
        {
            GameObject firstPrefab = Track(new GameObject("FirstPrefab"));
            GameObject secondPrefab = Track(new GameObject("SecondPrefab"));
            ChildSpawner first = CreateSpawner(firstPrefab, true);
            first._prefabs = new[] { firstPrefab, secondPrefab };
            InvokeStartOnce(first);
            GameObject survivor = first.transform.GetChild(1).gameObject;
            Object.DestroyImmediate(first.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Verify per-prefab replacement.
            ChildSpawner second = CreateSpawner(firstPrefab, true);
            second._prefabs = new[] { firstPrefab, secondPrefab };
            InvokeStartOnce(second);
            Assert.That(second.transform.childCount, Is.EqualTo(1));
            Assert.That(survivor != null, Is.True);
            Assert.That(first.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void RemainingDuplicateChildPreventsRespawnUntilAllInstancesAreDestroyed()
        {
            GameObject prefab = Track(new GameObject("DuplicatePrefab"));
            ChildSpawner spawner = CreateSpawner(prefab, false);
            spawner._prefabs = new[] { prefab, prefab };
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate child prefab")
            );
            InvokeStartOnce(spawner);
            Assert.That(spawner.transform.childCount, Is.EqualTo(2));
            Object.DestroyImmediate(spawner.transform.GetChild(1).gameObject); // UNH-SUPPRESS: Keep one duplicate alive.
            spawner._prefabs = new[] { prefab };
            spawner._spawnMethod = ChildSpawnMethod.Start;
            InvokeStartOnce(spawner);
            Assert.That(spawner.transform.childCount, Is.EqualTo(1));
            Object.DestroyImmediate(spawner.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Destroy the final duplicate.
            spawner._spawnMethod = ChildSpawnMethod.Start;
            InvokeStartOnce(spawner);
            Assert.That(spawner.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void NonPersistentChildrenAreIndependentAcrossSpawners()
        {
            GameObject prefab = Track(new GameObject("LocalPrefab"));
            ChildSpawner first = CreateSpawner(prefab, false);
            ChildSpawner second = CreateSpawner(prefab, false);
            InvokeStartOnce(first);
            InvokeStartOnce(second);
            Assert.That(first.transform.childCount, Is.EqualTo(1));
            Assert.That(second.transform.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator SpawnsChildrenOnAwake()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Awake;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;
            Assert.AreEqual(0, spawner.transform.childCount);
            yield return null;

            childSpawner.SendMessage("Awake");
            Assert.AreEqual(1, spawner.transform.childCount);
            Assert.IsTrue(
                spawner
                    .transform.GetChild(0)
                    .name.Contains("TestPrefab", StringComparison.OrdinalIgnoreCase)
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(0)
                    .name.Contains("00)", StringComparison.OrdinalIgnoreCase)
            );
        }

        [UnityTest]
        public IEnumerator SpawnsChildrenOnStart()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator SpawnsChildrenOnEnabled()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.OnEnabled;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            childSpawner.enabled = false;
            childSpawner.enabled = true;
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator SpawnsMultiplePrefabs()
        {
            GameObject prefab1 = Track(new GameObject("Prefab1"));
            GameObject prefab2 = Track(new GameObject("Prefab2"));
            GameObject prefab3 = Track(new GameObject("Prefab3"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab1, prefab2, prefab3 };
            childSpawner._dontDestroyOnLoad = false;
            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(3, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator RemovesCloneFromName()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
            Transform child = spawner.transform.GetChild(0);
            Assert.IsFalse(child.name.Contains("Clone", StringComparison.OrdinalIgnoreCase));
        }

        [UnityTest]
        public IEnumerator NumbersChildrenSequentially()
        {
            GameObject prefab1 = Track(new GameObject("TestPrefab1"));
            GameObject prefab2 = Track(new GameObject("TestPrefab2"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab1, prefab1, prefab2 };
            childSpawner._dontDestroyOnLoad = false;

            Assert.AreEqual(0, spawner.transform.childCount);
            /*
                The package logger is compiled out in non-development players; ExpectWallstopLog skips that
                expectation.
            */
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    "Duplicate child prefab detected: TestPrefab"
                )
            );
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(3, spawner.transform.childCount);
            Assert.IsTrue(
                spawner
                    .transform.GetChild(0)
                    .name.Contains("00)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(0).name
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(1)
                    .name.Contains("01)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(1).name
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(2)
                    .name.Contains("02)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(2).name
            );
        }

        [UnityTest]
        public IEnumerator DoesNotSpawnOnWrongMethod()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            childSpawner.SendMessage("Awake");
            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator HandlesEmptyPrefabArray()
        {
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = Array.Empty<GameObject>();
            childSpawner._dontDestroyOnLoad = false;

            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(0, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator HandlesNullPrefabInArray()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab, null };
            childSpawner._dontDestroyOnLoad = false;

            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("Unexpectedly null prefab")
            );
            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator SupportsCombinedSpawnMethods()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Awake | ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            childSpawner.SendMessage("Awake");
            Assert.AreEqual(1, spawner.transform.childCount);

            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator EditorOnlyPrefabsSpawnInEditor()
        {
            if (!Application.isEditor)
            {
                Assert.Pass("Test only runs in editor");
                yield break;
            }

            GameObject prefab = Track(new GameObject("EditorPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._editorOnlyPrefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
            Assert.IsTrue(spawner.transform.GetChild(0).name.Contains("EDITOR-ONLY"));
        }

        [UnityTest]
        public IEnumerator DevelopmentOnlyPrefabsSpawnInDevelopment()
        {
            if (!Application.isEditor && !Debug.isDebugBuild)
            {
                Assert.Pass("Test only runs in editor or debug build");
                yield break;
            }

            GameObject prefab = Track(new GameObject("DevPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._developmentOnlyPrefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(1, spawner.transform.childCount);
            Assert.IsTrue(spawner.transform.GetChild(0).name.Contains("DEVELOPMENT-ONLY"));
        }

        [UnityTest]
        public IEnumerator DetectsDuplicatePrefabs()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab, prefab };
            childSpawner._dontDestroyOnLoad = false;
            ExpectWallstopLog(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Duplicate.*")
            );
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(2, spawner.transform.childCount);
        }

        [UnityTest]
        public IEnumerator HandlesMultipleSpawnMethodsCombined()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            Object.DontDestroyOnLoad(prefab);
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod =
                ChildSpawnMethod.Awake | ChildSpawnMethod.OnEnabled | ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            childSpawner.SendMessage("Awake");
            yield return null;

            int childCountAfterAwake = spawner.transform.childCount;
            Assert.AreEqual(1, childCountAfterAwake);
        }

        [UnityTest]
        public IEnumerator SpawnsPrefabsInCorrectOrder()
        {
            GameObject prefab1 = Track(new GameObject("Prefab_A"));
            GameObject prefab2 = Track(new GameObject("Prefab_B"));
            GameObject prefab3 = Track(new GameObject("Prefab_C"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab1, prefab2, prefab3 };
            childSpawner._dontDestroyOnLoad = false;

            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.IsTrue(
                spawner
                    .transform.GetChild(0)
                    .name.Contains("Prefab_A", StringComparison.OrdinalIgnoreCase)
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(1)
                    .name.Contains("Prefab_B", StringComparison.OrdinalIgnoreCase)
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(2)
                    .name.Contains("Prefab_C", StringComparison.OrdinalIgnoreCase)
            );
        }

        [UnityTest]
        public IEnumerator ChildrenAreSpawnedUnderSpawner()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            InvokeStartOnce(childSpawner);
            yield return null;

            Transform child = spawner.transform.GetChild(0);
            Assert.AreEqual(spawner.transform, child.parent);
        }

        [UnityTest]
        public IEnumerator MixedPrefabTypesCombineNumbering()
        {
            if (!Application.isEditor)
            {
                Assert.Pass("Test only runs in editor");
                yield break;
            }

            GameObject prefab1 = Track(new GameObject("Regular"));
            GameObject prefab2 = Track(new GameObject("Editor"));
            GameObject prefab3 = Track(new GameObject("Dev"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab1 };
            childSpawner._editorOnlyPrefabs = new[] { prefab2 };
            childSpawner._developmentOnlyPrefabs = new[] { prefab3 };
            childSpawner._dontDestroyOnLoad = false;
            Assert.AreEqual(0, spawner.transform.childCount);
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.AreEqual(3, spawner.transform.childCount);
            Assert.IsTrue(
                spawner
                    .transform.GetChild(0)
                    .name.Contains("00)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(0).name
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(1)
                    .name.Contains("01)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(1).name
            );
            Assert.IsTrue(
                spawner
                    .transform.GetChild(2)
                    .name.Contains("02)", StringComparison.OrdinalIgnoreCase),
                spawner.transform.GetChild(2).name
            );
        }

        [UnityTest]
        public IEnumerator DontDestroyOnLoadWhenEnabled()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = true;
            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.IsTrue(spawner.IsDontDestroyOnLoad());
        }

        [UnityTest]
        public IEnumerator DoesNotDontDestroyOnLoadWhenDisabled()
        {
            GameObject prefab = Track(new GameObject("TestPrefab"));
            GameObject spawner = Track(new GameObject("Spawner", typeof(ChildSpawner)));
            ChildSpawner childSpawner = spawner.GetComponent<ChildSpawner>();

            childSpawner._spawnMethod = ChildSpawnMethod.Start;
            childSpawner._prefabs = new[] { prefab };
            childSpawner._dontDestroyOnLoad = false;

            InvokeStartOnce(childSpawner);
            yield return null;

            Assert.IsFalse(spawner.IsDontDestroyOnLoad());
        }

        [UnityTest]
        public IEnumerator AllSpawnMethodsWorkIndependently()
        {
            GameObject prefabAwake = Track(new GameObject("AwakePrefab"));
            GameObject prefabOnEnabled = Track(new GameObject("OnEnabledPrefab"));
            GameObject prefabStart = Track(new GameObject("StartPrefab"));

            GameObject spawnerAwake = Track(new GameObject("SpawnerAwake", typeof(ChildSpawner)));
            ChildSpawner childSpawnerAwake = spawnerAwake.GetComponent<ChildSpawner>();
            childSpawnerAwake._spawnMethod = ChildSpawnMethod.Awake;
            childSpawnerAwake._prefabs = new[] { prefabAwake };
            childSpawnerAwake._dontDestroyOnLoad = false;

            GameObject spawnerOnEnabled = Track(
                new GameObject("SpawnerOnEnabled", typeof(ChildSpawner))
            );
            ChildSpawner childSpawnerOnEnabled = spawnerOnEnabled.GetComponent<ChildSpawner>();
            childSpawnerOnEnabled._spawnMethod = ChildSpawnMethod.OnEnabled;
            childSpawnerOnEnabled._prefabs = new[] { prefabOnEnabled };
            childSpawnerOnEnabled._dontDestroyOnLoad = false;

            GameObject spawnerStart = Track(new GameObject("SpawnerStart", typeof(ChildSpawner)));
            ChildSpawner childSpawnerStart = spawnerStart.GetComponent<ChildSpawner>();
            childSpawnerStart._spawnMethod = ChildSpawnMethod.Start;
            childSpawnerStart._prefabs = new[] { prefabStart };
            childSpawnerStart._dontDestroyOnLoad = false;

            childSpawnerAwake.SendMessage("Awake");
            Assert.AreEqual(1, spawnerAwake.transform.childCount);
            Assert.AreEqual(0, spawnerOnEnabled.transform.childCount);
            Assert.AreEqual(0, spawnerStart.transform.childCount);

            childSpawnerOnEnabled.enabled = false;
            childSpawnerOnEnabled.enabled = true;
            Assert.AreEqual(1, spawnerAwake.transform.childCount);
            Assert.AreEqual(1, spawnerOnEnabled.transform.childCount);
            Assert.AreEqual(0, spawnerStart.transform.childCount);

            InvokeStartOnce(childSpawnerStart);
            Assert.AreEqual(1, spawnerAwake.transform.childCount);
            Assert.AreEqual(1, spawnerOnEnabled.transform.childCount);
            Assert.AreEqual(1, spawnerStart.transform.childCount);

            yield return null;
        }

        private ChildSpawner CreateSpawner(GameObject prefab, bool persistent)
        {
            GameObject owner = Track(new GameObject("RespawnOwner", typeof(ChildSpawner)));
            ChildSpawner spawner = owner.GetComponent<ChildSpawner>();
            spawner._prefabs = new[] { prefab };
            spawner._spawnMethod = ChildSpawnMethod.Start;
            spawner._dontDestroyOnLoad = persistent;
            return spawner;
        }
    }
}
