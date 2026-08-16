// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Tags;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Drives every branch of the editor's <see cref="SingletonCreationAttribute"/> diagnostic.
    /// </summary>
    /// <remarks>
    /// The attributes are constructed here rather than applied to purpose-built wrong types, because
    /// <c>TypeCache</c> would find those on every editor load and the diagnostic under test would
    /// then report this fixture's own fixtures forever. Real types supply the only thing the rule
    /// reads off a <see cref="Type"/> -- whether it derives from <see cref="RuntimeSingleton{T}"/>.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SingletonCreationDiagnosticsTests
    {
        [Test]
        public void ASoundAnnotationOnARuntimeSingletonIsQuiet()
        {
            Assert.IsTrue(
                AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                    typeof(RuntimeMismatchSingleton),
                    new SingletonCreationAttribute(SingletonCreationPolicy.NeverCreate),
                    null
                ) == null
            );
        }

        [Test]
        public void AnUnannotatedTypeIsQuiet()
        {
            Assert.IsTrue(
                AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                    typeof(RuntimeMismatchSingleton),
                    null,
                    new AutoLoadSingletonAttribute(RuntimeInitializeLoadType.BeforeSplashScreen)
                ) == null
            );
        }

        /// <summary>
        /// The realistic version of the mistake is the attribute on a <c>ScriptableObjectSingleton</c>,
        /// which never creates an asset at runtime and so has nothing for the policy to govern.
        /// </summary>
        [Test]
        [TestCase(typeof(AttributeMetadataCache))]
        [TestCase(typeof(SingletonCreationDiagnosticsTests))]
        public void TheAttributeOnSomethingThatIsNotARuntimeSingletonIsReportedAsInert(Type type)
        {
            string problem = AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                type,
                new SingletonCreationAttribute(SingletonCreationPolicy.NeverCreate),
                null
            );

            Assert.IsTrue(problem != null);
            StringAssert.Contains(type.FullName, problem);
            StringAssert.Contains("has no effect", problem);
        }

        /// <summary>
        /// Auto-loading a singleton that refuses to be created can only find nothing at any phase
        /// that runs before a scene exists.
        /// </summary>
        [Test]
        [TestCase(RuntimeInitializeLoadType.SubsystemRegistration)]
        [TestCase(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        [TestCase(RuntimeInitializeLoadType.BeforeSplashScreen)]
        [TestCase(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public void NeverCreatePairedWithAPreSceneAutoLoadIsReported(
            RuntimeInitializeLoadType phase
        )
        {
            string problem = AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                typeof(RuntimeMismatchSingleton),
                new SingletonCreationAttribute(SingletonCreationPolicy.NeverCreate),
                new AutoLoadSingletonAttribute(phase)
            );

            Assert.IsTrue(problem != null);
            StringAssert.Contains(phase.ToString(), problem);
            StringAssert.Contains(nameof(SingletonCreationPolicy.NeverCreate), problem);
        }

        /// <summary>
        /// The control the refusals need: <c>AfterSceneLoad</c> runs once an authored instance
        /// exists, so the pair binds the static cache to it and is a choice rather than a mistake.
        /// </summary>
        [Test]
        public void NeverCreatePairedWithAnAfterSceneLoadAutoLoadIsQuiet()
        {
            Assert.IsTrue(
                AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                    typeof(RuntimeMismatchSingleton),
                    new SingletonCreationAttribute(SingletonCreationPolicy.NeverCreate),
                    new AutoLoadSingletonAttribute(RuntimeInitializeLoadType.AfterSceneLoad)
                ) == null
            );
        }

        /// <summary>
        /// A creating singleton is what auto-load is for, at every phase.
        /// </summary>
        [Test]
        [TestCase(RuntimeInitializeLoadType.BeforeSplashScreen)]
        [TestCase(RuntimeInitializeLoadType.AfterSceneLoad)]
        public void CreateOnDemandPairedWithAnyAutoLoadIsQuiet(RuntimeInitializeLoadType phase)
        {
            Assert.IsTrue(
                AttributeMetadataCacheGenerator.DescribeSingletonCreationProblem(
                    typeof(RuntimeMismatchSingleton),
                    new SingletonCreationAttribute(SingletonCreationPolicy.CreateOnDemand),
                    new AutoLoadSingletonAttribute(phase)
                ) == null
            );
        }
    }
#endif
}
