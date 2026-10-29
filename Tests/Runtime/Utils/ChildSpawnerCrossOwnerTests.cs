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
    public sealed class ChildSpawnerCrossOwnerTests : CommonTestBase
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

        [TestCase(false, false, 0)]
        [TestCase(false, true, 0)]
        [TestCase(true, false, 0)]
        [TestCase(true, true, 0)]
#if UNITY_EDITOR
        [TestCase(false, false, 1)]
        [TestCase(false, true, 1)]
        [TestCase(true, false, 1)]
        [TestCase(true, true, 1)]
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [TestCase(false, false, 2)]
        [TestCase(false, true, 2)]
        [TestCase(true, false, 2)]
        [TestCase(true, true, 2)]
#endif
        public void SurvivingCallbackChildPreventsLaterDuplicates(
            bool destroyOuterFirst,
            bool throwAfterNestedSpawn,
            int group
        )
        {
            GameObject prefab = Track(new GameObject("SharedPrefab", typeof(ChildSpawnerCallback)));
            ChildSpawner outer = CreateSpawner("OuterOwner", prefab, group);
            ChildSpawner nested = CreateSpawner("NestedOwner", prefab, group);
            ChildSpawner later = CreateSpawner("LaterOwner", prefab, group);
            bool entered = false;
            ChildSpawnerCallback.Callback = () =>
            {
                if (entered)
                {
                    return;
                }
                entered = true;
                nested.enabled = true;
                if (throwAfterNestedSpawn)
                {
                    throw new InvalidOperationException("Expected cross-owner callback failure");
                }
            };
            if (throwAfterNestedSpawn)
            {
                LogAssert.Expect(
                    LogType.Exception,
                    new Regex("InvalidOperationException: Expected cross-owner callback failure")
                );
            }
            outer.enabled = true;
            ChildSpawnerCallback.Callback = null;

            Assert.That(entered, Is.True);
            Assert.That(outer.transform.childCount, Is.EqualTo(1));
            Assert.That(nested.transform.childCount, Is.EqualTo(1));
            string label =
                group == 0 ? string.Empty
                : group == 1 ? "EDITOR-ONLY "
                : "DEVELOPMENT-ONLY ";
            Assert.That(outer.transform.GetChild(0).name, Is.EqualTo($"SharedPrefab ({label}00)"));
            Assert.That(nested.transform.GetChild(0).name, Is.EqualTo($"SharedPrefab ({label}00)"));

            ChildSpawner destroyed = destroyOuterFirst ? outer : nested;
            ChildSpawner survivor = destroyOuterFirst ? nested : outer;
            Object.DestroyImmediate(destroyed.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Exercise persistent registry lookup after one callback child is destroyed.
            later.enabled = true;
            Assert.That(
                later.transform.childCount,
                Is.Zero,
                "A surviving persistent child must suppress another owner."
            );
            destroyed.enabled = false;
            destroyed.enabled = true;
            Assert.That(destroyed.transform.childCount, Is.Zero);
            Assert.That(survivor.transform.childCount, Is.EqualTo(1));

            Object.DestroyImmediate(survivor.transform.GetChild(0).gameObject); // UNH-SUPPRESS: Verify retry after every persistent instance has been destroyed.
            later.enabled = false;
            later.enabled = true;
            Assert.That(later.transform.childCount, Is.EqualTo(1));
            Assert.That(later.transform.GetChild(0).name, Is.EqualTo($"SharedPrefab ({label}00)"));
            outer.enabled = false;
            outer.enabled = true;
            nested.enabled = false;
            nested.enabled = true;
            Assert.That(outer.transform.childCount, Is.Zero);
            Assert.That(nested.transform.childCount, Is.Zero);
        }

        private ChildSpawner CreateSpawner(string ownerName, GameObject prefab, int group)
        {
            GameObject owner = Track(new GameObject(ownerName, typeof(ChildSpawner)));
            ChildSpawner spawner = owner.GetComponent<ChildSpawner>();
            spawner.enabled = false;
            spawner._dontDestroyOnLoad = true;
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
            return spawner;
        }
    }
}
