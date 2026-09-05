// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class PersistentDirectorySettingsTests : CommonTestBase
    {
        [Test]
        public void GetPathsSortsByCountThenLastUsed()
        {
            PersistentDirectorySettings settings = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );

            string tool = "TestTool";
            string ctx = "Context";

            settings.RecordPath(tool, ctx, "Assets/A");
            settings.RecordPath(tool, ctx, "Assets/A");
            settings.RecordPath(tool, ctx, "Assets/A");

            settings.RecordPath(tool, ctx, "Assets/C");
            settings.RecordPath(tool, ctx, "Assets/C");

            settings.RecordPath(tool, ctx, "Assets/B");

            DirectoryUsageData[] paths = settings.GetPaths(tool, ctx);
            Assert.IsTrue(paths != null);
            Assert.GreaterOrEqual(paths.Length, 3);
            Assert.AreEqual("Assets/A", paths[0].path);
            Assert.AreEqual("Assets/C", paths[1].path);
            Assert.AreEqual("Assets/B", paths[2].path);
        }

        [Test]
        public void GetPathsTopOnlyRespectsLimit()
        {
            PersistentDirectorySettings settings = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );

            string tool = "TopOnlyTool";
            string ctx = "Context";

            settings.RecordPath(tool, ctx, "Assets/One");
            settings.RecordPath(tool, ctx, "Assets/One");
            settings.RecordPath(tool, ctx, "Assets/Two");
            settings.RecordPath(tool, ctx, "Assets/Three");

            DirectoryUsageData[] top2 = settings.GetPaths(tool, ctx, topOnly: true, topN: 2);
            Assert.IsTrue(top2 != null);
            Assert.AreEqual(2, top2.Length);
            Assert.AreEqual("Assets/One", top2[0].path);
        }
    }
#endif
}
