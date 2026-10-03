// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class LayerHelperTests : CommonTestBase
    {
        [TearDown]
        public void ClearCaches()
        {
            Helpers.CLearLayerNames();
        }

        [Test]
        public void GetAllLayerNamesCachesResultsUntilCleared()
        {
            Helpers.ResetLayerCache();
            string[] first = Helpers.GetAllLayerNames();
            Assert.IsTrue(first != null);
            Assert.AreSame(first, Helpers.GetAllLayerNames());
            Helpers.ResetLayerCache();
            Assert.IsFalse(Helpers.LayerCacheInitialized);
            CollectionAssert.AreEqual(first, Helpers.GetAllLayerNames());
        }

        [Test]
        public void GetAllLayerNamesBufferMatchesArray()
        {
            string[] layers = Helpers.GetAllLayerNames();
            List<string> buffer = new() { "placeholder" };
            Helpers.GetAllLayerNames(buffer);
            CollectionAssert.AreEqual(layers, buffer);
        }

        [Test]
        public void GetAllLayerNamesReturnsLiveRuntimeLayers()
        {
            Helpers.ResetLayerCache();
            string[] layers = Helpers.GetAllLayerNames();
            Assert.IsTrue(layers != null);
            Assert.IsNotEmpty(layers);
            Assert.Contains("Default", layers);
        }

        [Test]
        public void ResetLayerCacheDiscardsStaleNames()
        {
            string[] expected = Helpers.GetAllLayerNames();
            Helpers.CachedLayerNames = new[] { "StaleLayer" };
            Helpers.LayerCacheInitialized = true;
            CollectionAssert.AreEqual(new[] { "StaleLayer" }, Helpers.GetAllLayerNames());
            Helpers.ResetLayerCache();
            CollectionAssert.AreEqual(expected, Helpers.GetAllLayerNames());
        }

#if UNITY_EDITOR
        [Test]
        public void ProjectChangeResetsLayerCache()
        {
            string[] expected = Helpers.GetAllLayerNames();
            Helpers.CachedLayerNames = new[] { "StaleLayer" };
            Helpers.LayerCacheInitialized = true;
            Helpers.HandleProjectChangedForHelpers();
            Assert.IsFalse(Helpers.LayerCacheInitialized);
            CollectionAssert.AreEqual(expected, Helpers.GetAllLayerNames());
        }
#endif
    }
}
