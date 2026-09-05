// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SingletonAutoLoaderTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            // Reset static caches so each test emits the warning its LogAssert expectation consumes.
            SingletonAutoLoader.ClearCachesForTests();
            AutoRuntimeSingleton.ClearForTests();
            AutoScriptableSingleton.ClearForTests();
            RuntimeMismatchSingleton.ClearForTests();
            ScriptableMismatchSingleton.ClearForTests();
        }

        private static readonly RuntimeInitializeLoadType[] RuntimeLoadTypes =
        {
            RuntimeInitializeLoadType.AfterAssembliesLoaded,
            RuntimeInitializeLoadType.BeforeSplashScreen,
            RuntimeInitializeLoadType.BeforeSceneLoad,
            RuntimeInitializeLoadType.AfterSceneLoad,
            RuntimeInitializeLoadType.SubsystemRegistration,
        };

        [UnityTest]
        public IEnumerator AutoLoaderInitializesRuntimeSingletons()
        {
            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateRuntimeEntry<AutoRuntimeSingleton>()
            );

            yield return null;

            Assert.IsTrue(AutoRuntimeSingleton.HasInstance);
            Assert.GreaterOrEqual(AutoRuntimeSingleton.AwakenCount, 1);
            Track(AutoRuntimeSingleton.Instance.gameObject);
        }

        [UnityTest]
        public IEnumerator AutoLoaderInitializesScriptableSingletons()
        {
            AutoScriptableSingleton instance =
                ScriptableObject.CreateInstance<AutoScriptableSingleton>();
            instance.name = nameof(AutoScriptableSingleton);
            instance.hideFlags = HideFlags.DontSave;
            Track(instance);

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateScriptableEntry<AutoScriptableSingleton>()
            );

            yield return null;

            Assert.IsTrue(AutoScriptableSingleton.HasInstance);
            Assert.GreaterOrEqual(AutoScriptableSingleton.CreatedCount, 1);
        }

        [UnityTest]
        public IEnumerator AutoLoaderSkipsWhenNotPlaying()
        {
            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: false,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateRuntimeEntry<AutoRuntimeSingleton>()
            );

            yield return null;

            Assert.IsFalse(AutoRuntimeSingleton.HasInstance);
            Assert.AreEqual(0, AutoRuntimeSingleton.AwakenCount);
        }

        [UnityTest]
        public IEnumerator AutoLoaderSkipsScriptableSingletonsWhenNotPlaying()
        {
            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: false,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateScriptableEntry<AutoScriptableSingleton>()
            );

            yield return null;

            Assert.IsFalse(AutoScriptableSingleton.HasInstance);
            Assert.AreEqual(0, AutoScriptableSingleton.CreatedCount);
        }

        [UnityTest]
        public IEnumerator AutoLoaderLogsWarningWhenTypeCannotBeResolved()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Unable to resolve type", RegexOptions.IgnoreCase)
            );

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                new AttributeMetadataCache.AutoLoadSingletonEntry(
                    "Missing.Type, UnknownAssembly",
                    SingletonAutoLoadKind.Runtime,
                    RuntimeInitializeLoadType.BeforeSplashScreen
                )
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator AutoLoaderWarnsWhenRuntimeEntryTargetsScriptableSingleton()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex("does not derive from RuntimeSingleton", RegexOptions.IgnoreCase)
            );

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateRuntimeEntry<ScriptableMismatchSingleton>()
            );

            yield return null;

            Assert.AreEqual(0, ScriptableMismatchSingleton.CreatedCount);
            Assert.IsFalse(ScriptableMismatchSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator AutoLoaderWarnsWhenScriptableEntryTargetsRuntimeSingleton()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex("does not derive from ScriptableObjectSingleton", RegexOptions.IgnoreCase)
            );

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateScriptableEntry<RuntimeMismatchSingleton>()
            );

            yield return null;

            Assert.AreEqual(0, RuntimeMismatchSingleton.AwakeCount);
            Assert.IsFalse(RuntimeMismatchSingleton.HasInstance);
        }

        [UnityTest]
        public IEnumerator AutoLoaderSkipsEntriesWithDifferentLoadType()
        {
            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.AfterSceneLoad,
                new AttributeMetadataCache.AutoLoadSingletonEntry(
                    typeof(AutoRuntimeSingleton).AssemblyQualifiedName,
                    SingletonAutoLoadKind.Runtime,
                    RuntimeInitializeLoadType.BeforeSplashScreen
                )
            );

            yield return null;

            Assert.IsFalse(AutoRuntimeSingleton.HasInstance);
            Assert.AreEqual(0, AutoRuntimeSingleton.AwakenCount);
        }

        [UnityTest]
        public IEnumerator AutoLoaderIgnoresDuplicateEntries()
        {
            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                CreateRuntimeEntry<AutoRuntimeSingleton>(),
                CreateRuntimeEntry<AutoRuntimeSingleton>()
            );

            yield return null;
            Assert.AreEqual(1, AutoRuntimeSingleton.AwakenCount);
        }

        [UnityTest]
        public IEnumerator MissingTypeWarningEmittedOnceForDuplicates()
        {
            LogAssert.Expect(
                LogType.Warning,
                new Regex("Unable to resolve type", RegexOptions.IgnoreCase)
            );

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                RuntimeInitializeLoadType.BeforeSplashScreen,
                new AttributeMetadataCache.AutoLoadSingletonEntry(
                    "Unknown.Type, MissingAssembly",
                    SingletonAutoLoadKind.Runtime,
                    RuntimeInitializeLoadType.BeforeSplashScreen
                ),
                new AttributeMetadataCache.AutoLoadSingletonEntry(
                    "Unknown.Type, MissingAssembly",
                    SingletonAutoLoadKind.Runtime,
                    RuntimeInitializeLoadType.BeforeSplashScreen
                )
            );

            yield return null;
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AutoLoaderExecutesOnlyMatchingLoadType(
            [ValueSource(nameof(RuntimeLoadTypes))] RuntimeInitializeLoadType loadType
        )
        {
            AutoRuntimeSingleton.ClearForTests();
            RuntimeMismatchSingleton.ClearForTests();

            RuntimeInitializeLoadType otherLoadType =
                loadType == RuntimeInitializeLoadType.BeforeSplashScreen
                    ? RuntimeInitializeLoadType.AfterSceneLoad
                    : RuntimeInitializeLoadType.BeforeSplashScreen;

            SingletonAutoLoader.ExecuteEntriesForTests(
                simulatePlayMode: true,
                loadType,
                CreateRuntimeEntry<AutoRuntimeSingleton>(loadType),
                CreateRuntimeEntry<RuntimeMismatchSingleton>(otherLoadType)
            );

            yield return null;

            Assert.AreEqual(1, AutoRuntimeSingleton.AwakenCount);
            Assert.AreEqual(0, RuntimeMismatchSingleton.AwakeCount);
        }

        private static AttributeMetadataCache.AutoLoadSingletonEntry CreateRuntimeEntry<T>(
            RuntimeInitializeLoadType loadType = RuntimeInitializeLoadType.BeforeSplashScreen
        )
        {
            return new AttributeMetadataCache.AutoLoadSingletonEntry(
                typeof(T).AssemblyQualifiedName,
                SingletonAutoLoadKind.Runtime,
                loadType
            );
        }

        private static AttributeMetadataCache.AutoLoadSingletonEntry CreateScriptableEntry<T>(
            RuntimeInitializeLoadType loadType = RuntimeInitializeLoadType.BeforeSplashScreen
        )
        {
            return new AttributeMetadataCache.AutoLoadSingletonEntry(
                typeof(T).AssemblyQualifiedName,
                SingletonAutoLoadKind.ScriptableObject,
                loadType
            );
        }
    }
}
