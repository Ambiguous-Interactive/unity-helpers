// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class LayerHelperTests : CommonTestBase
    {
        [TearDown]
        public void ClearCaches()
        {
            InvokeClearLayerNames();
            Helpers.ResetLayerNameProvider();
        }

        [Test]
        public void GetAllLayerNamesCachesResultsUntilCleared()
        {
            int invocation = 0;
            Helpers.LayerNameProvider = () =>
            {
                ++invocation;
                return new[] { "LayerA", "LayerB" };
            };

            Helpers.ResetLayerCache();
            string[] first = Helpers.GetAllLayerNames();
            Assert.AreEqual(1, invocation, "Provider should be invoked on first call.");

            Helpers.LayerNameProvider = () =>
            {
                ++invocation;
                return new[] { "LayerChanged" };
            };

            string[] second = Helpers.GetAllLayerNames();
            CollectionAssert.AreEqual(new[] { "LayerA", "LayerB" }, second);

            Helpers.ResetLayerCache();
            string[] third = Helpers.GetAllLayerNames();
            Assert.AreEqual(2, invocation, "Reset should force provider refresh.");
            CollectionAssert.AreEqual(new[] { "LayerChanged" }, third);
        }

        [Test]
        public void GetAllLayerNamesBufferMatchesArray()
        {
            string[] layers = Helpers.GetAllLayerNames();
            Assume.That(layers, Is.Not.Null.And.Not.Empty);

            System.Collections.Generic.List<string> buffer = new() { "placeholder" };
            Helpers.GetAllLayerNames(buffer);

            CollectionAssert.AreEquivalent(layers, buffer);
        }

        [Test]
        public void FallingBackToRuntimeLayerProviderStillReturnsLayers()
        {
            Helpers.LayerNameProvider = () => Array.Empty<string>();
            ClearCaches();

            string[] layers = Helpers.GetAllLayerNames();
            Assert.IsTrue(layers != null);
            Assert.IsNotEmpty(layers);
        }

        [Test]
        public void ResetLayerCacheForcesProviderRefresh()
        {
            int invocation = 0;
            Helpers.LayerNameProvider = () =>
            {
                ++invocation;
                return invocation == 1 ? new[] { "LayerZero" } : new[] { "LayerOne", "LayerTwo" };
            };

            Helpers.ResetLayerCache();
            _ = Helpers.GetAllLayerNames();
            Helpers.ResetLayerCache();
            string[] refreshed = Helpers.GetAllLayerNames();

            CollectionAssert.AreEqual(new[] { "LayerOne", "LayerTwo" }, refreshed);
        }

#if UNITY_EDITOR
        [Test]
        public void ProjectChangeResetsLayerCache()
        {
            Helpers.LayerNameProvider = () => new[] { "LayerInitial" };
            Helpers.ResetLayerCache();
            _ = Helpers.GetAllLayerNames();

            Helpers.LayerNameProvider = () => new[] { "LayerUpdated" };
            Helpers.HandleProjectChangedForHelpers();

            string[] updated = Helpers.GetAllLayerNames();
            CollectionAssert.AreEqual(new[] { "LayerUpdated" }, updated);
        }
#endif

        private static void InvokeClearLayerNames()
        {
            Helpers.CLearLayerNames();
        }
    }
}
