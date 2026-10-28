// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Fast")]
    public sealed class ChildSpawnerReentryTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ChildSpawnerCallback.Callback = null;
            ChildSpawner.ClearSpawnedPrefabs();
        }

        [TearDown]
        public override void TearDown()
        {
            ChildSpawnerCallback.Callback = null;
            ChildSpawner.ClearSpawnedPrefabs();
            base.TearDown();
        }

        [TestCase(false, 0)]
        [TestCase(true, 0)]
#if UNITY_EDITOR
        [TestCase(false, 1)]
        [TestCase(true, 1)]
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [TestCase(false, 2)]
        [TestCase(true, 2)]
#endif
        public void ReentrantEnableDoesNotDuplicateChildren(bool persistent, int group)
        {
            GameObject prefab = Track(
                new GameObject("CallbackPrefab", typeof(ChildSpawnerCallback))
            );
            GameObject owner = Track(new GameObject("CallbackOwner", typeof(ChildSpawner)));
            ChildSpawner spawner = owner.GetComponent<ChildSpawner>();
            spawner._dontDestroyOnLoad = persistent;
            spawner._spawnMethod = ChildSpawnMethod.OnEnabled;
            if (group == 0)
            {
                spawner._prefabs = new[] { prefab };
            }
            else if (group == 1)
            {
                spawner._editorOnlyPrefabs = new[] { prefab };
            }
            else
            {
                spawner._developmentOnlyPrefabs = new[] { prefab };
            }
            bool reentered = false;
            ChildSpawnerCallback.Callback = () =>
            {
                if (reentered)
                {
                    return;
                }
                reentered = true;
                spawner.enabled = false;
                spawner.enabled = true;
            };
            spawner.enabled = false;
            spawner.enabled = true;

            Assert.That(reentered, Is.True);
            Assert.That(owner.transform.childCount, Is.EqualTo(1));
            string label =
                group == 0 ? string.Empty
                : group == 1 ? "EDITOR-ONLY "
                : "DEVELOPMENT-ONLY ";
            Assert.That(
                owner.transform.GetChild(0).name,
                Is.EqualTo($"CallbackPrefab ({label}00)")
            );
            ChildSpawnerCallback.Callback = null;
            Object.DestroyImmediate(owner.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Exercise an independent lifecycle event after reentry.
            spawner.enabled = false;
            spawner.enabled = true;
            Assert.That(owner.transform.childCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CallbackFailureDoesNotPreventLaterSpawning(bool persistent)
        {
            GameObject prefab = Track(
                new GameObject("ThrowingPrefab", typeof(ChildSpawnerCallback))
            );
            GameObject owner = Track(new GameObject("RecoveryOwner", typeof(ChildSpawner)));
            ChildSpawner spawner = owner.GetComponent<ChildSpawner>();
            spawner._dontDestroyOnLoad = persistent;
            spawner._spawnMethod = ChildSpawnMethod.OnEnabled;
            spawner._prefabs = new[] { prefab };
            ChildSpawnerCallback.Callback = () =>
            {
                throw new InvalidOperationException("Expected child callback failure");
            };
            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: Expected child callback failure")
            );
            spawner.enabled = false;
            spawner.enabled = true;
            Assert.That(owner.transform.childCount, Is.EqualTo(1));

            ChildSpawnerCallback.Callback = null;
            Object.DestroyImmediate(owner.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Verify recovery after a failed callback.
            spawner.enabled = false;
            spawner.enabled = true;
            Assert.That(owner.transform.childCount, Is.EqualTo(1));
        }
    }
}
