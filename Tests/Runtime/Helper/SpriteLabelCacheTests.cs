// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpriteLabelCacheTests : CommonTestBase
    {
        [TearDown]
        public void ResetCache()
        {
            Helpers.ResetSpriteLabelCache();
        }

        [Test]
        public void SetSpriteLabelCacheSortsValues()
        {
            Helpers.SetSpriteLabelCache(new[] { "Charlie", "Alpha", "Bravo" });
            CollectionAssert.AreEqual(
                new[] { "Alpha", "Bravo", "Charlie" },
                Helpers.AllSpriteLabels
            );
        }

        [Test]
        public void SetSpriteLabelCacheCanClearEntries()
        {
            Helpers.SetSpriteLabelCache(new[] { "Label" });
            Helpers.SetSpriteLabelCache(null);
            Assert.IsEmpty(Helpers.AllSpriteLabels);
        }

        [Test]
        public void SetSpriteLabelCacheReusesArrayWhenCountMatches()
        {
            Helpers.SetSpriteLabelCache(new[] { "A", "B" }, alreadySorted: true);
            string[] firstReference = Helpers.AllSpriteLabels;

            Helpers.SetSpriteLabelCache(new[] { "C", "D" }, alreadySorted: true);
            Assert.AreSame(firstReference, Helpers.AllSpriteLabels);
            CollectionAssert.AreEqual(new[] { "C", "D" }, Helpers.AllSpriteLabels);
        }

        [Test]
        public void GetAllSpriteLabelNamesCopiesCacheIntoBuffer()
        {
            if (Helpers.IsRunningInContinuousIntegration || Helpers.IsRunningInBatchMode)
            {
                // Helpers.GetAllSpriteLabelNames(List) intentionally clears the buffer and
                // returns under CI/batchmode (and is editor-only besides), so the copy/sort
                // behavior cannot be exercised here. Covered in interactive editor runs.
                Assert.Inconclusive(
                    "GetAllSpriteLabelNames(List) no-ops under CI/batchmode by design."
                );
            }
            Helpers.SetSpriteLabelCache(new[] { "Gamma", "Beta", "Alpha" });
            List<string> buffer = new() { "placeholder" };
            Helpers.GetAllSpriteLabelNames(buffer);
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta", "Gamma" }, buffer);
        }

        [Test]
        public void ProjectChangeClearsSpriteLabelCache()
        {
            Helpers.SetSpriteLabelCache(new[] { "Label" }, alreadySorted: true);
            Assert.IsNotEmpty(Helpers.AllSpriteLabels);

#if UNITY_EDITOR
            Helpers.HandleProjectChangedForHelpers();
#else
            Helpers.ResetSpriteLabelCache();
#endif

            Assert.IsEmpty(Helpers.AllSpriteLabels);
        }
    }
}
