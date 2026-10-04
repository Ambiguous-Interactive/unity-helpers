// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class DetectAssetChangeProcessorTests : DetectAssetChangeTestBase
    {
        private const string HandlerAssetPath = TestRoot + "/Handler.asset";
        private const string PayloadPath = TestRoot + "/Payload.asset";
        private const string DetailedHandlerAssetPath = TestRoot + "/DetailedHandler.asset";
        private const string AlternatePayloadPath = TestRoot + "/AlternatePayload.asset";
        private const string AssignableHandlerAssetPath = TestRoot + "/AssignableHandler.asset";

        private DetectAssetChangeProcessorTestAccess.AssetWatcherSettings _settings;
        private DetectAssetChangeProcessorTestAccess.AssetWatcherSettings _fixtureSettings;
        private float _originalLoopWindowSeconds;
        private AssetChangeDetectionEnabledScope _watcherScope;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            _settings = DetectAssetChangeProcessorTestAccess.GetSettings();
            ClearTestState();
            CleanupTestFolders();
            EnsureTestFolder();
            TrackFolder(TestRoot);
            EnsureHandlerAsset<TestDetectAssetChangeHandler>(HandlerAssetPath);
            EnsureHandlerAsset<TestDetailedSignatureHandler>(DetailedHandlerAssetPath);
            EnsureHandlerAsset<TestAssignableAssetChangeHandler>(AssignableHandlerAssetPath);
            /*
                Setup asset mutation can queue late drains; flush before the first test observes inherited
                handler state.
            */
            AssetPostprocessorDeferralTestAccess.Flush();
            DetectAssetChangeProcessorTestAccess.Reset();
            DetectAssetChangeProcessor.EnabledOverride = true;

            DetectAssetChangeProcessorTestAccess.EnsureInitialized();
            _fixtureSettings = DetectAssetChangeProcessorTestAccess.GetSettings();
            DetectAssetChangeProcessorTestAccess.Reset(_settings);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Check inherited handler pollution before base setup changes its attribution.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            /*
                Delete previous payload assets before reset can discover them in tests that require prior
                nonexistence.
            */
            DeleteAssetIfExists(PayloadPath);
            DeleteAssetIfExists(AlternatePayloadPath);

            EnsureTestFolder();

            AssetPostprocessorTestHandlers.FlushAndClearAll();
            DetectAssetChangeProcessorTestAccess.Reset(_fixtureSettings);
            // Force the watcher on because CI runs this fixture in batch mode.
            _watcherScope = AssetChangeDetectionUtility.EnabledScope(true);

            _originalLoopWindowSeconds = UnityHelpersSettings
                .instance
                .DetectAssetChangeLoopWindowSeconds;
        }

        [TearDown]
        public override void TearDown()
        {
            _watcherScope?.Dispose();
            _watcherScope = null;
            DetectAssetChangeProcessorTestAccess.Reset(_settings);

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            if (
                settings != null
                && !Mathf.Approximately(
                    settings.DetectAssetChangeLoopWindowSeconds,
                    _originalLoopWindowSeconds
                )
            )
            {
                settings.DetectAssetChangeLoopWindowSeconds = _originalLoopWindowSeconds;
            }

            // Clear handler state after base teardown has finished queuing drains from tracked-asset destruction.
            base.TearDown();
            ClearTestState();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                InternalTeardown();
                CleanupTestFolders();
                // Flush fixture cleanup mutations before the next fixture begins.
                AssetPostprocessorDeferralTestAccess.Flush();
            }
            finally
            {
                base.OneTimeTearDown();
            }
        }

        [Test]
        public void InvokesHandlersWhenAssetsAreCreated()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                $"Expected 1 invocation but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
            AssetChangeContext context = TestDetectAssetChangeHandler.RecordedContexts[0];
            Assert.AreEqual(
                AssetChangeFlags.Created,
                context.Flags,
                $"Expected Created flag but got {context.Flags}"
            );
            CollectionAssert.Contains(
                context.CreatedAssetPaths,
                PayloadPath,
                $"Expected CreatedAssetPaths to contain '{PayloadPath}' but got [{string.Join(", ", context.CreatedAssetPaths)}]"
            );
        }

        [Test]
        public void CreatedPathsRemainStableAfterLaterChangeBatch()
        {
            CreatePayloadAssetAt(PayloadPath);
            CreatePayloadAssetAt(AlternatePayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(1, TestDetectAssetChangeHandler.RecordedContexts.Count);
            AssetChangeContext firstContext = TestDetectAssetChangeHandler.RecordedContexts[0];
            CollectionAssert.AreEqual(new[] { PayloadPath }, firstContext.CreatedAssetPaths);

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { AlternatePayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(2, TestDetectAssetChangeHandler.RecordedContexts.Count);
            CollectionAssert.AreEqual(new[] { PayloadPath }, firstContext.CreatedAssetPaths);
            CollectionAssert.AreEqual(
                new[] { AlternatePayloadPath },
                TestDetectAssetChangeHandler.RecordedContexts[1].CreatedAssetPaths
            );
        }

        [Test]
        public void DeletedContextPathsAreIndependentFromDetailedHandlerArray()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();
            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                new[] { PayloadPath },
                null,
                null
            );

            Assert.AreEqual(1, TestDetectAssetChangeHandler.RecordedContexts.Count);
            Assert.AreEqual(1, TestDetailedSignatureHandler.LastDeletedPaths.Length);
            AssetChangeContext context = TestDetectAssetChangeHandler.RecordedContexts[0];
            TestDetailedSignatureHandler.LastDeletedPaths[0] = AlternatePayloadPath;
            CollectionAssert.AreEqual(new[] { PayloadPath }, context.DeletedAssetPaths);
        }

        [Test]
        public void InheritedHandlerOverrideIsRegistered()
        {
            Assert.IsTrue(
                _fixtureSettings.WatchersByAssetType.TryGetValue(
                    typeof(TestDetectableAsset),
                    out DetectAssetChangeProcessor.AssetWatcher watcher
                )
            );

            foreach (
                DetectAssetChangeProcessor.MethodSubscription subscription in watcher.Subscriptions
            )
            {
                if (subscription._declaringType == typeof(InheritedHandler))
                {
                    Assert.AreEqual(
                        nameof(InheritedHandler.OnAssetChanged),
                        subscription._method.Name
                    );
                    return;
                }
            }

            Assert.Fail("Inherited handler override was not registered.");
        }

        [Test]
        public void InvokesHandlersWhenAssetsAreDeleted()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            TestDetectAssetChangeHandler.Clear();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                new[] { PayloadPath },
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                $"Expected 1 invocation for deletion but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
            AssetChangeContext context = TestDetectAssetChangeHandler.RecordedContexts[0];
            Assert.AreEqual(
                AssetChangeFlags.Deleted,
                context.Flags,
                $"Expected Deleted flag but got {context.Flags}"
            );
            CollectionAssert.Contains(
                context.DeletedAssetPaths,
                PayloadPath,
                $"Expected DeletedAssetPaths to contain '{PayloadPath}' but got [{string.Join(", ", context.DeletedAssetPaths)}]"
            );
        }

        [Test]
        public void StaticHandlersReceiveNotificationsForAssetChanges()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestStaticAssetChangeHandler.RecordedContexts.Count,
                $"Expected 1 static handler invocation for creation but got {TestStaticAssetChangeHandler.RecordedContexts.Count}"
            );
            Assert.AreEqual(
                AssetChangeFlags.Created,
                TestStaticAssetChangeHandler.RecordedContexts[0].Flags,
                $"Expected Created flag for static handler but got {TestStaticAssetChangeHandler.RecordedContexts[0].Flags}"
            );

            TestStaticAssetChangeHandler.Clear();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                new[] { PayloadPath },
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestStaticAssetChangeHandler.RecordedContexts.Count,
                $"Expected 1 static handler invocation for deletion but got {TestStaticAssetChangeHandler.RecordedContexts.Count}"
            );
            Assert.AreEqual(
                AssetChangeFlags.Deleted,
                TestStaticAssetChangeHandler.RecordedContexts[0].Flags,
                $"Expected Deleted flag for static handler but got {TestStaticAssetChangeHandler.RecordedContexts[0].Flags}"
            );
        }

        [Test]
        public void DetailedSignatureReceivesCreatedAssetsAndDeletedPaths()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestDetailedSignatureHandler.LastCreatedAssets.Length,
                $"Expected 1 created asset but got {TestDetailedSignatureHandler.LastCreatedAssets.Length}"
            );
            Assert.IsTrue(
                TestDetailedSignatureHandler.LastCreatedAssets[0] != null,
                "First created asset in LastCreatedAssets should not be null"
            );
            Assert.AreEqual(
                PayloadPath,
                AssetDatabase.GetAssetPath(TestDetailedSignatureHandler.LastCreatedAssets[0]),
                $"Expected created asset path to be '{PayloadPath}' but got '{AssetDatabase.GetAssetPath(TestDetailedSignatureHandler.LastCreatedAssets[0])}'"
            );
            Assert.AreEqual(
                0,
                TestDetailedSignatureHandler.LastDeletedPaths.Length,
                $"Expected 0 deleted paths after creation but got {TestDetailedSignatureHandler.LastDeletedPaths.Length}"
            );

            TestDetailedSignatureHandler.Clear();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                new[] { PayloadPath },
                null,
                null
            );

            Assert.AreEqual(
                0,
                TestDetailedSignatureHandler.LastCreatedAssets.Length,
                $"Expected 0 created assets after deletion but got {TestDetailedSignatureHandler.LastCreatedAssets.Length}"
            );
            CollectionAssert.AreEquivalent(
                new[] { PayloadPath },
                TestDetailedSignatureHandler.LastDeletedPaths,
                $"Expected LastDeletedPaths to contain only '{PayloadPath}' but got [{string.Join(", ", TestDetailedSignatureHandler.LastDeletedPaths)}]"
            );
        }

        [Test]
        public void SingleMethodCanWatchMultipleAssetTypes()
        {
            CreatePayloadAssetAt(PayloadPath);
            CreateAlternatePayloadAssetAt(AlternatePayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestMultiAttributeHandler.RecordedInvocations.Count,
                $"Expected 1 invocation but got {TestMultiAttributeHandler.RecordedInvocations.Count}"
            );
            Assert.AreEqual(
                typeof(TestDetectableAsset),
                TestMultiAttributeHandler.RecordedInvocations[0].AssetType
            );
            Assert.AreEqual(
                AssetChangeFlags.Created,
                TestMultiAttributeHandler.RecordedInvocations[0].Flags
            );

            TestMultiAttributeHandler.Clear();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { AlternatePayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                0,
                TestMultiAttributeHandler.RecordedInvocations.Count,
                "TestAlternateDetectableAsset should not trigger Created flag handler"
            );

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                new[] { AlternatePayloadPath },
                null,
                null
            );

            Assert.AreEqual(
                1,
                TestMultiAttributeHandler.RecordedInvocations.Count,
                $"Expected 1 invocation but got {TestMultiAttributeHandler.RecordedInvocations.Count}"
            );
            Assert.AreEqual(
                typeof(TestAlternateDetectableAsset),
                TestMultiAttributeHandler.RecordedInvocations[0].AssetType
            );
            Assert.AreEqual(
                AssetChangeFlags.Deleted,
                TestMultiAttributeHandler.RecordedInvocations[0].Flags
            );
        }

        [Test]
        public void ReentrantHandlersQueueChangesInsteadOfRecursing()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            ResetProcessorWithFixtureState();
            TestReentrantHandler.Configure(PayloadPath);

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.AreEqual(
                2,
                TestReentrantHandler.InvocationCount,
                $"Expected 2 invocations (initial + reentrant) but got {TestReentrantHandler.InvocationCount}"
            );
        }

        [Test]
        public void ChangeBatchesWithGapsLongerThanWindowAreNotSuppressed()
        {
            for (
                int i = 0;
                i <= DetectAssetChangeProcessor.MaxConsecutiveChangeSetsWithinWindow;
                ++i
            )
            {
                DetectAssetChangeProcessor.UpdateLoopWindow(1, i * 6d, 5d);
                Assert.IsFalse(DetectAssetChangeProcessor._loopProtectionActive);
                Assert.AreEqual(1, DetectAssetChangeProcessor._consecutiveChangeBatches);
            }
        }

        [Test]
        public void PublicResetLoopProtectionResumesDispatchWithoutDroppingSubscriptions()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();
            ResetProcessorWithOnlyLoopHandler();

            LogAssert.Expect(
                LogType.Error,
                new Regex("potentially infinite asset change loop", RegexOptions.Singleline)
            );

            DetectAssetChangeProcessor.UpdateLoopWindow(
                DetectAssetChangeProcessor.MaxConsecutiveChangeSetsWithinWindow,
                0d,
                30d
            );

            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings protectedSettings =
                DetectAssetChangeProcessorTestAccess.GetSettings();
            Assert.IsTrue(protectedSettings.LoopProtectionActive);
            Assert.IsTrue(
                protectedSettings.WatchersByAssetType.TryGetValue(
                    typeof(TestDetectableAsset),
                    out DetectAssetChangeProcessor.AssetWatcher snapshotWatcher
                )
            );
            int activeSubscriptionCount = snapshotWatcher.Subscriptions.Count;
            snapshotWatcher.Subscriptions.Clear();
            Assert.IsTrue(
                DetectAssetChangeProcessorTestAccess
                    .GetSettings()
                    .WatchersByAssetType.TryGetValue(
                        typeof(TestDetectableAsset),
                        out DetectAssetChangeProcessor.AssetWatcher activeWatcher
                    )
            );
            Assert.AreEqual(activeSubscriptionCount, activeWatcher.Subscriptions.Count);
            int invocationCountAfterLoopProtection = TestLoopingHandler.InvocationCount;

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            Assert.AreEqual(invocationCountAfterLoopProtection, TestLoopingHandler.InvocationCount);

            AssetChangeDetectionUtility.ResetLoopProtection();

            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings resetSettings =
                DetectAssetChangeProcessorTestAccess.GetSettings();
            Assert.IsFalse(resetSettings.LoopProtectionActive);
            Assert.AreEqual(0, resetSettings.ConsecutiveChangeBatches);
            Assert.AreEqual(0, resetSettings.PendingAssetChanges.Count);

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.Greater(TestLoopingHandler.InvocationCount, invocationCountAfterLoopProtection);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ResetToleratesNullCollectionsAndEntries()
        {
            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings settings = new()
            {
                WatchersByAssetType = null,
                PendingAssetChanges = null,
            };

            Assert.DoesNotThrow(() => DetectAssetChangeProcessorTestAccess.Reset(settings));

            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings emptySnapshot =
                DetectAssetChangeProcessorTestAccess.GetSettings();
            Assert.AreEqual(0, emptySnapshot.WatchersByAssetType.Count);
            Assert.AreEqual(0, emptySnapshot.PendingAssetChanges.Count);

            settings.WatchersByAssetType = new();
            settings.WatchersByAssetType.Add(typeof(TestDetectableAsset), null);
            settings.PendingAssetChanges = new();
            settings.PendingAssetChanges.Enqueue(null);

            Assert.DoesNotThrow(() => DetectAssetChangeProcessorTestAccess.Reset(settings));

            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings nullEntrySnapshot =
                DetectAssetChangeProcessorTestAccess.GetSettings();
            Assert.IsTrue(
                nullEntrySnapshot.WatchersByAssetType.TryGetValue(
                    typeof(TestDetectableAsset),
                    out DetectAssetChangeProcessor.AssetWatcher watcher
                )
            );
            Assert.IsTrue(watcher == null);
            Assert.AreEqual(1, nullEntrySnapshot.PendingAssetChanges.Count);
            Assert.IsTrue(
                nullEntrySnapshot.PendingAssetChanges.TryPeek(
                    out DetectAssetChangeProcessor.PendingAssetChangeSet pendingChange
                )
            );
            Assert.IsTrue(pendingChange == null);
        }

        [Test]
        public void LogsErrorWhenMethodReturnsNonVoid()
        {
            Regex expected = new(
                "TestInvalidReturnTypeHandler\\.OnInvalidReturnType.*Supported signatures",
                RegexOptions.Singleline
            );
            LogAssert.Expect(LogType.Error, expected);

            bool isValid = DetectAssetChangeProcessorTestAccess.ValidateMethodSignature(
                typeof(TestInvalidReturnTypeHandler),
                "OnInvalidReturnType"
            );

            Assert.IsFalse(isValid);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogsErrorWhenMethodHasUnsupportedSingleParameter()
        {
            Regex expected = new(
                "TestInvalidParameterHandler\\.OnInvalidSingleParameter.*Supported signatures",
                RegexOptions.Singleline
            );
            LogAssert.Expect(LogType.Error, expected);

            bool isValid = DetectAssetChangeProcessorTestAccess.ValidateMethodSignature(
                typeof(TestInvalidParameterHandler),
                "OnInvalidSingleParameter"
            );

            Assert.IsFalse(isValid);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogsErrorWhenCreatedAssetParameterIsNotArray()
        {
            Regex expected = new(
                "TestInvalidCreatedParameterHandler\\.OnInvalidCreated.*Supported signatures",
                RegexOptions.Singleline
            );
            LogAssert.Expect(LogType.Error, expected);

            bool isValid = DetectAssetChangeProcessorTestAccess.ValidateMethodSignature(
                typeof(TestInvalidCreatedParameterHandler),
                "OnInvalidCreated"
            );

            Assert.IsFalse(isValid);
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(
            typeof(TestValidNoParametersHandler),
            "OnValidNoParameters",
            true,
            TestName = "SignatureValidation.NoParameters.Valid"
        )]
        [TestCase(
            typeof(TestValidContextHandler),
            "OnValidContext",
            true,
            TestName = "SignatureValidation.ContextParameter.Valid"
        )]
        [TestCase(
            typeof(TestValidDetailedHandler),
            "OnValidDetailed",
            true,
            TestName = "SignatureValidation.DetailedSignature.Valid"
        )]
        [TestCase(
            typeof(TestInvalidReturnTypeHandler),
            "OnInvalidReturnType",
            false,
            TestName = "SignatureValidation.NonVoidReturn.Invalid"
        )]
        [TestCase(
            typeof(TestInvalidParameterHandler),
            "OnInvalidSingleParameter",
            false,
            TestName = "SignatureValidation.WrongSingleParam.Invalid"
        )]
        [TestCase(
            typeof(TestInvalidCreatedParameterHandler),
            "OnInvalidCreated",
            false,
            TestName = "SignatureValidation.NonArrayCreated.Invalid"
        )]
        public void MethodSignatureValidationDataDriven(
            Type declaringType,
            string methodName,
            bool expectedValid
        )
        {
            if (!expectedValid)
            {
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(
                        $"{declaringType.Name}\\.{methodName}.*Supported signatures",
                        RegexOptions.Singleline
                    )
                );
            }

            bool isValid = DetectAssetChangeProcessorTestAccess.ValidateMethodSignature(
                declaringType,
                methodName
            );

            Assert.AreEqual(
                expectedValid,
                isValid,
                $"Method {declaringType.Name}.{methodName} should be {(expectedValid ? "valid" : "invalid")}"
            );
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(
            true,
            false,
            false,
            false,
            AssetChangeFlags.Created,
            TestName = "ChangeFlags.CreatedOnly.FlagsCreated"
        )]
        [TestCase(
            false,
            true,
            false,
            false,
            AssetChangeFlags.Deleted,
            TestName = "ChangeFlags.DeletedOnly.FlagsDeleted"
        )]
        [TestCase(
            true,
            true,
            false,
            false,
            AssetChangeFlags.Created | AssetChangeFlags.Deleted,
            TestName = "ChangeFlags.CreatedAndDeleted.FlagsBoth"
        )]
        [TestCase(
            false,
            false,
            false,
            false,
            AssetChangeFlags.None,
            TestName = "ChangeFlags.NoChanges.FlagsNone"
        )]
        public void AssetChangeFlagsDataDriven(
            bool hasCreated,
            bool hasDeleted,
            bool hasMoved,
            bool hasMovedFrom,
            AssetChangeFlags expectedFlags
        )
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            string[] created = hasCreated ? new[] { PayloadPath } : null;
            string[] deleted = hasDeleted ? new[] { PayloadPath } : null;
            string[] moved = hasMoved ? new[] { PayloadPath } : null;
            string[] movedFrom = hasMovedFrom ? new[] { PayloadPath } : null;

            // Deletion lookup requires the asset path to have been tracked before deletion.
            if (hasDeleted && !hasCreated)
            {
                DetectAssetChangeProcessorTestAccess.ProcessChanges(
                    new[] { PayloadPath },
                    null,
                    null,
                    null
                );
                ClearTestState();
            }

            DetectAssetChangeProcessorTestAccess.ProcessChanges(created, deleted, moved, movedFrom);

            if (expectedFlags == AssetChangeFlags.None)
            {
                Assert.AreEqual(
                    0,
                    TestDetectAssetChangeHandler.RecordedContexts.Count,
                    "No changes should not trigger handlers"
                );
            }
            else
            {
                Assert.GreaterOrEqual(
                    TestDetectAssetChangeHandler.RecordedContexts.Count,
                    1,
                    $"Expected flags {expectedFlags} should result in handler invocation"
                );
            }
        }

        [Test]
        public void EmptyChangeListsDoNotTriggerHandlers()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            );

            Assert.AreEqual(
                0,
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                "Empty change lists should not trigger handlers"
            );
        }

        [Test]
        public void NullChangeListsDoNotTriggerHandlers()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(null, null, null, null);

            Assert.AreEqual(
                0,
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                "Null change lists should not trigger handlers"
            );
        }

        [Test]
        public void ProcessingNonExistentPathsDoesNotCrash()
        {
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { "Assets/__DoesNotExist__/fake.asset" },
                null,
                null,
                null
            );

            Assert.AreEqual(
                0,
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                "Non-existent paths should not trigger handlers"
            );
        }

        [Test]
        public void MixedValidAndInvalidPathsProcessesCorrectly()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath, "Assets/__DoesNotExist__/fake.asset" },
                null,
                null,
                null
            );

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for valid path '{PayloadPath}' in mixed list with invalid paths, "
                    + $"but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
        }

        [Test]
        public void InPlaceAssetRenameTriggersMovedEvent()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            string renamedPath = TestRoot + "/PayloadRenamed.asset";

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                null,
                new[] { renamedPath },
                new[] { PayloadPath }
            );

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for in-place renamed asset from '{PayloadPath}' to '{renamedPath}', "
                    + $"but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
        }

        [Test]
        public void MultipleAssetsMoveInSameBatchTriggersHandlers()
        {
            string subFolderPath = CreateTestSubFolder("BatchMoveTarget");
            CreatePayloadAssetAt(PayloadPath);
            CreateAlternatePayloadAssetAt(AlternatePayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath, AlternatePayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            string movedPath1 = subFolderPath + "/Payload.asset";
            string movedPath2 = subFolderPath + "/AlternatePayload.asset";

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                null,
                null,
                new[] { movedPath1, movedPath2 },
                new[] { PayloadPath, AlternatePayloadPath }
            );

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for batch move of 2 assets, "
                    + $"but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );

            Assert.GreaterOrEqual(
                TestLoopingHandler.InvocationCount,
                1,
                $"Expected TestLoopingHandler to be invoked at least once for batch move, "
                    + $"but got {TestLoopingHandler.InvocationCount} invocations"
            );

            if (AssetDatabase.IsValidFolder(subFolderPath))
            {
                AssetDatabase.DeleteAsset(subFolderPath);
            }
        }

        [Test]
        public void MixedHandlerTypesInSingleEventBatchProcessCorrectly()
        {
            CreatePayloadAssetAt(PayloadPath);
            CreateAlternatePayloadAssetAt(AlternatePayloadPath);
            ClearTestState();
            ResetProcessorWithFixtureState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath, AlternatePayloadPath },
                null,
                null,
                null
            );

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected TestDetectAssetChangeHandler to be invoked at least once for TestDetectableAsset in mixed batch, "
                    + $"but got {TestDetectAssetChangeHandler.RecordedContexts.Count} invocations"
            );

            // The second asset is watched only for deletion, so its creation must not invoke this handler.
            Assert.GreaterOrEqual(
                TestMultiAttributeHandler.RecordedInvocations.Count,
                1,
                $"Expected TestMultiAttributeHandler to be invoked at least once for Created TestDetectableAsset, "
                    + $"but got {TestMultiAttributeHandler.RecordedInvocations.Count} invocations"
            );
        }

        [TestCase(".unity", TestName = "SceneFile.LowerCase.DoesNotCrash")]
        [TestCase(".Unity", TestName = "SceneFile.PascalCase.DoesNotCrash")]
        [TestCase(".UNITY", TestName = "SceneFile.UpperCase.DoesNotCrash")]
        [TestCase(".scenetemplate", TestName = "SceneFile.SceneTemplate.DoesNotCrash")]
        public void SceneFileImportDoesNotCrash(string extension)
        {
            /*
                Scene sub-asset loading previously triggered Unity’s ReadObjectThreaded error; include a scene
                import in the batch.
            */
            string fakeScenePath = TestRoot + "/TestScene" + extension;

            ClearTestState();

            Assert.DoesNotThrow(
                () =>
                    DetectAssetChangeProcessorTestAccess.ProcessChanges(
                        new[] { fakeScenePath },
                        null,
                        null,
                        null
                    ),
                $"Processing a '{extension}' scene file as an imported asset should not throw or crash"
            );

            foreach (AssetChangeContext context in TestDetectAssetChangeHandler.RecordedContexts)
            {
                CollectionAssert.DoesNotContain(
                    context.CreatedAssetPaths,
                    fakeScenePath,
                    $"Scene file '{fakeScenePath}' should not appear in created asset paths for a ScriptableObject watcher"
                );
            }
        }

        [Test]
        public void SceneFileInMixedBatchDoesNotInterfereWithNormalAssets()
        {
            string fakeScenePath = TestRoot + "/TestScene.unity";

            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath, fakeScenePath },
                null,
                null,
                null
            );

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for valid asset '{PayloadPath}' in mixed batch with scene file, "
                    + $"but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );

            foreach (AssetChangeContext context in TestDetectAssetChangeHandler.RecordedContexts)
            {
                CollectionAssert.DoesNotContain(
                    context.CreatedAssetPaths,
                    fakeScenePath,
                    $"Scene file '{fakeScenePath}' should not appear in created asset paths when mixed with normal assets"
                );
            }
        }

        [TestCase(false, false, true, true, TestName = "ChangeFlags.MovedOnly.HandlesMovedAsset")]
        public void MovedAssetFlagsDataDriven(
            bool hasCreated,
            bool hasDeleted,
            bool hasMoved,
            bool hasMovedFrom
        )
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();

            DetectAssetChangeProcessorTestAccess.ProcessChanges(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            string movedPath = TestRoot + "/MovedPayload.asset";

            string[] created = hasCreated ? new[] { PayloadPath } : null;
            string[] deleted = hasDeleted ? new[] { PayloadPath } : null;
            string[] moved = hasMoved ? new[] { movedPath } : null;
            string[] movedFrom = hasMovedFrom ? new[] { PayloadPath } : null;

            DetectAssetChangeProcessorTestAccess.ProcessChanges(created, deleted, moved, movedFrom);

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for moved asset (hasCreated={hasCreated}, hasDeleted={hasDeleted}, "
                    + $"hasMoved={hasMoved}, hasMovedFrom={hasMovedFrom}), but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
        }

        private void InternalTeardown()
        {
            ClearTestState();
            DetectAssetChangeProcessorTestAccess.Reset(_settings);

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            if (
                settings != null
                && !Mathf.Approximately(
                    settings.DetectAssetChangeLoopWindowSeconds,
                    _originalLoopWindowSeconds
                )
            )
            {
                settings.DetectAssetChangeLoopWindowSeconds = _originalLoopWindowSeconds;
            }
        }

        private void ResetProcessorWithFixtureState()
        {
            DetectAssetChangeProcessorTestAccess.Reset(_fixtureSettings);
            EnsureTestFolder();
            DetectAssetChangeProcessor.EnabledOverride = true;
        }

        private void ResetProcessorWithOnlyLoopHandler()
        {
            ResetProcessorWithFixtureState();
            DetectAssetChangeProcessorTestAccess.AssetWatcherSettings settings =
                DetectAssetChangeProcessorTestAccess.GetSettings();
            Assert.IsTrue(
                settings.WatchersByAssetType.TryGetValue(
                    typeof(TestDetectableAsset),
                    out DetectAssetChangeProcessor.AssetWatcher payloadWatcher
                )
            );
            settings.WatchersByAssetType.Clear();
            settings.WatchersByAssetType.Add(typeof(TestDetectableAsset), payloadWatcher);
            for (int i = payloadWatcher.Subscriptions.Count - 1; 0 <= i; i--)
            {
                if (payloadWatcher.Subscriptions[i]._declaringType != typeof(TestLoopingHandler))
                {
                    payloadWatcher.Subscriptions.RemoveAt(i);
                }
            }
            Assert.AreEqual(1, payloadWatcher.Subscriptions.Count);
            DetectAssetChangeProcessorTestAccess.Reset(settings);
        }

        private abstract class InheritedHandlerBase
        {
            public static void Clear() { }

            [DetectAssetChanged(typeof(TestDetectableAsset))]
            public virtual void OnAssetChanged() { }
        }

        private sealed class InheritedHandler : InheritedHandlerBase
        {
            public override void OnAssetChanged() { }
        }
    }
}
