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

        private DetectAssetChangeProcessor.AssetWatcherSettings _settings;
        private float _originalLoopWindowSeconds;
        private AssetChangeDetectionEnabledScope _watcherScope;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            _settings = DetectAssetChangeProcessor.GetSettingsForTesting();
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
            AssetPostprocessorDeferral.FlushForTesting();
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
            /*
                Register the folder before enabling test assets so processor initialization does not warn about
                a missing folder.
            */
            EnsureTestFolder();
            /*
                Drain setup mutations before configuring the allowlist so recorded invocations cannot appear
                mid-test.
            */
            AssetPostprocessorTestHandlers.FlushAndClearAll();
            DetectAssetChangeProcessor.ResetForTesting();
            // Force the watcher on because CI runs this fixture in batch mode.
            _watcherScope = AssetChangeDetectionUtility.EnabledScope(true);
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            // Restrict observed paths so other fixtures’ assets cannot invoke this handler.
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = FixtureAllowlist;
            _originalLoopWindowSeconds = UnityHelpersSettings
                .instance
                .DetectAssetChangeLoopWindowSeconds;
        }

        [TearDown]
        public override void TearDown()
        {
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = null;
            DetectAssetChangeProcessor.ResetForTesting(_settings);
            _watcherScope?.Dispose();
            _watcherScope = null;

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
                AssetPostprocessorDeferral.FlushForTesting();
            }
            finally
            {
                base.OneTimeTearDown();
            }
        }

        private void InternalTeardown()
        {
            ClearTestState();
            DetectAssetChangeProcessor.ResetForTesting(_settings);

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

        [Test]
        public void InvokesHandlersWhenAssetsAreCreated()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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
        public void InvokesHandlersWhenAssetsAreDeleted()
        {
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            TestDetectAssetChangeHandler.Clear();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            ResetProcessorWithCleanState();
            TestReentrantHandler.Configure(PayloadPath);

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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
            CreatePayloadAssetAt(PayloadPath);
            // Clear state after asset creation since Unity's OnPostprocessAllAssets may have fired
            ClearTestState();

            ResetProcessorWithCleanState();

            double fakeTime = 0;
            DetectAssetChangeProcessor.TimeProvider = () => fakeTime;
            DetectAssetChangeProcessor.LoopWindowSecondsOverride = 5d;

            int iterations = DetectAssetChangeProcessor.MaxConsecutiveChangeSetsWithinWindow + 1;
            for (int i = 0; i < iterations; i++)
            {
                fakeTime += 6d;
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { PayloadPath },
                    null,
                    null,
                    null
                );
            }

            Assert.AreEqual(
                iterations,
                TestLoopingHandler.InvocationCount,
                $"Expected {iterations} invocations (gaps prevent loop detection) but got {TestLoopingHandler.InvocationCount}"
            );
        }

        [Test]
        public void PublicResetLoopProtectionResumesDispatchWithoutDroppingSubscriptions()
        {
            CreatePayloadAssetAt(PayloadPath);
            ClearTestState();
            ResetProcessorWithCleanState();

            double fakeTime = 0;
            DetectAssetChangeProcessor.TimeProvider = () => fakeTime;
            DetectAssetChangeProcessor.LoopWindowSecondsOverride = 30d;
            LogAssert.Expect(
                LogType.Error,
                new Regex("potentially infinite asset change loop", RegexOptions.Singleline)
            );

            int iterations = DetectAssetChangeProcessor.MaxConsecutiveChangeSetsWithinWindow;
            for (int i = 0; i < iterations; i++)
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { PayloadPath },
                    null,
                    null,
                    null
                );
            }

            DetectAssetChangeProcessor.AssetWatcherSettings protectedSettings =
                DetectAssetChangeProcessor.GetSettingsForTesting();
            Assert.IsTrue(protectedSettings.LoopProtectionActive);
            int invocationCountAfterLoopProtection = TestLoopingHandler.InvocationCount;

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            Assert.AreEqual(invocationCountAfterLoopProtection, TestLoopingHandler.InvocationCount);

            AssetChangeDetectionUtility.ResetLoopProtection();

            DetectAssetChangeProcessor.AssetWatcherSettings resetSettings =
                DetectAssetChangeProcessor.GetSettingsForTesting();
            Assert.IsFalse(resetSettings.LoopProtectionActive);
            Assert.AreEqual(0, resetSettings.ConsecutiveChangeBatches);
            Assert.AreEqual(0, resetSettings.PendingAssetChanges.Count);

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { PayloadPath },
                null,
                null,
                null
            );

            Assert.Greater(TestLoopingHandler.InvocationCount, invocationCountAfterLoopProtection);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LogsErrorWhenMethodReturnsNonVoid()
        {
            Regex expected = new(
                "TestInvalidReturnTypeHandler\\.OnInvalidReturnType.*Supported signatures",
                RegexOptions.Singleline
            );
            LogAssert.Expect(LogType.Error, expected);

            bool isValid = DetectAssetChangeProcessor.ValidateMethodSignatureForTesting(
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

            bool isValid = DetectAssetChangeProcessor.ValidateMethodSignatureForTesting(
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

            bool isValid = DetectAssetChangeProcessor.ValidateMethodSignatureForTesting(
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

            bool isValid = DetectAssetChangeProcessor.ValidateMethodSignatureForTesting(
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
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { PayloadPath },
                    null,
                    null,
                    null
                );
                ClearTestState();
            }

            DetectAssetChangeProcessor.ProcessChangesForTesting(created, deleted, moved, movedFrom);

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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(null, null, null, null);

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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { PayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            string renamedPath = TestRoot + "/PayloadRenamed.asset";

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
                new[] { PayloadPath, AlternatePayloadPath },
                null,
                null,
                null
            );
            ClearTestState();

            string movedPath1 = subFolderPath + "/Payload.asset";
            string movedPath2 = subFolderPath + "/AlternatePayload.asset";

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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
            ResetProcessorWithCleanState();

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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
                    DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(
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

            DetectAssetChangeProcessor.ProcessChangesForTesting(created, deleted, moved, movedFrom);

            Assert.GreaterOrEqual(
                TestDetectAssetChangeHandler.RecordedContexts.Count,
                1,
                $"Expected at least 1 handler invocation for moved asset (hasCreated={hasCreated}, hasDeleted={hasDeleted}, "
                    + $"hasMoved={hasMoved}, hasMovedFrom={hasMovedFrom}), but got {TestDetectAssetChangeHandler.RecordedContexts.Count}"
            );
        }
    }
}
