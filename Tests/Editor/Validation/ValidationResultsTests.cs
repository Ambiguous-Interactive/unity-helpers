// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the store the window reads and the incremental re-check writes.
    /// </summary>
    /// <remarks>
    /// The property that matters is that an asset's entry is REPLACED. A store that only added
    /// would report every problem the project has ever had, and a fixed asset would keep its
    /// finding forever -- which is the failure an incremental engine is most likely to ship with,
    /// because it looks correct on the run that finds the problem.
    /// </remarks>
    [TestFixture]
    public sealed class ValidationResultsTests : CommonTestBase
    {
        private const string FirstGuid = "00000000000000000000000000000001";
        private const string SecondGuid = "00000000000000000000000000000002";
        private const string ThirdGuid = "00000000000000000000000000000003";

        [SetUp]
        public void ClearStore()
        {
            ValidationResults.Clear();
        }

        [TearDown]
        public void ClearStoreAfter()
        {
            ValidationResults.Clear();
        }

        [Test]
        public void AnUntouchedStoreHasNotRun()
        {
            Assert.IsFalse(ValidationResults.HasRun);
            Assert.AreEqual(0, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.Snapshot());
        }

        /// <summary>
        /// "Checked, and clean" is not "nothing checked yet", and the store has to tell them apart.
        /// </summary>
        [Test]
        public void ACleanRunRecordsEveryAssetItConsidered()
        {
            ValidationRun run = Run(new SilentRule(), FirstGuid, SecondGuid);

            ValidationResults.RecordRun(run);

            Assert.IsTrue(ValidationResults.HasRun);
            Assert.AreEqual(2, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.Snapshot());
        }

        [Test]
        public void RecordingARunDiscardsWhatWasKnownBefore()
        {
            ValidationResults.Replace(
                SecondGuid,
                new List<ValidationFinding> { Finding(SecondGuid, "stale") }
            );

            ValidationResults.RecordRun(Run(new SilentRule(), FirstGuid));

            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.Snapshot());
        }

        [Test]
        public void ReplacingAnAssetDropsItsPreviousFindings()
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding>
                {
                    Finding(FirstGuid, "first"),
                    Finding(FirstGuid, "second"),
                }
            );

            ValidationResults.Replace(FirstGuid, new List<ValidationFinding>());

            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.Snapshot());
        }

        [Test]
        public void ForgettingAnAssetRemovesItFromTheCount()
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "first") }
            );

            Assert.IsTrue(ValidationResults.Forget(FirstGuid));
            Assert.IsFalse(ValidationResults.Forget(FirstGuid));
            Assert.AreEqual(0, ValidationResults.CheckedAssetCount);
        }

        /// <summary>
        /// A scoped re-check knows nothing about the rest of the project, so it must not replace it.
        /// </summary>
        [Test]
        public void AScopedMergeLeavesUnrelatedAssetsAlone()
        {
            ValidationResults.Replace(
                SecondGuid,
                new List<ValidationFinding> { Finding(SecondGuid, "elsewhere") }
            );

            ValidationResults.MergeScopedRun(Run(new NoisyRule(), FirstGuid));

            List<ValidationFinding> all = ValidationResults.Snapshot();
            Assert.AreEqual(2, ValidationResults.CheckedAssetCount);
            Assert.AreEqual(2, all.Count);
        }

        /// <summary>
        /// The case that decides whether the incremental path is correct: an asset that used to
        /// have a finding and now has none.
        /// </summary>
        [Test]
        public void AScopedMergeClearsAnAssetThatIsCleanNow()
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "was broken") }
            );

            ValidationResults.MergeScopedRun(Run(new SilentRule(), FirstGuid));

            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.Snapshot());
        }

        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Null,
            TestName = "Full.Null.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Incomplete,
            TestName = "Full.Incomplete.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Cancelled,
            TestName = "Full.Cancelled.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Failed,
            TestName = "Full.Failed.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Null,
            TestName = "Scoped.Null.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Incomplete,
            TestName = "Scoped.Incomplete.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Cancelled,
            TestName = "Scoped.Cancelled.RetainsSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Failed,
            TestName = "Scoped.Failed.RetainsSnapshot"
        )]
        public void ARejectedRunRetainsLastKnownResultsWithoutRaising(
            CommitOperation operation,
            RejectedRunState state
        )
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "first previous") }
            );
            ValidationResults.Replace(
                SecondGuid,
                new List<ValidationFinding> { Finding(SecondGuid, "second previous") }
            );
            List<ValidationFinding> expectedFindings = ValidationResults.Snapshot();
            string[] expectedGuids = new List<string>(
                ValidationResults.RecordedAssetGuids
            ).ToArray();
            int raised = 0;
            void Count() => raised++;

            ValidationResults.Changed += Count;
            try
            {
                Commit(operation, CreateRejectedRun(state));
            }
            finally
            {
                ValidationResults.Changed -= Count;
            }

            Assert.IsTrue(ValidationResults.HasRun);
            Assert.AreEqual(expectedGuids.Length, ValidationResults.CheckedAssetCount);
            CollectionAssert.AreEqual(expectedGuids, ValidationResults.RecordedAssetGuids);
            CollectionAssert.AreEqual(expectedFindings, ValidationResults.Snapshot());
            Assert.AreEqual(0, raised);
        }

        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Null,
            TestName = "Full.Null.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Incomplete,
            TestName = "Full.Incomplete.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Cancelled,
            TestName = "Full.Cancelled.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Full,
            RejectedRunState.Failed,
            TestName = "Full.Failed.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Null,
            TestName = "Scoped.Null.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Incomplete,
            TestName = "Scoped.Incomplete.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Cancelled,
            TestName = "Scoped.Cancelled.DoesNotCreateSnapshot"
        )]
        [TestCase(
            CommitOperation.Scoped,
            RejectedRunState.Failed,
            TestName = "Scoped.Failed.DoesNotCreateSnapshot"
        )]
        public void ARejectedRunDoesNotCreateAnInitialSnapshot(
            CommitOperation operation,
            RejectedRunState state
        )
        {
            int raised = 0;
            void Count() => raised++;

            ValidationResults.Changed += Count;
            try
            {
                Commit(operation, CreateRejectedRun(state));
            }
            finally
            {
                ValidationResults.Changed -= Count;
            }

            Assert.IsFalse(ValidationResults.HasRun);
            Assert.AreEqual(0, ValidationResults.CheckedAssetCount);
            Assert.IsEmpty(ValidationResults.RecordedAssetGuids);
            Assert.IsEmpty(ValidationResults.Snapshot());
            Assert.AreEqual(0, raised);
        }

        [Test]
        public void TryCommitReportsWhetherTheSnapshotWasAccepted()
        {
            ValidationRun incomplete = CreateIncompleteRun();
            ValidationRun complete = Run(new SilentRule(), FirstGuid);

            Assert.IsFalse(ValidationResults.TryRecordRun(incomplete));
            Assert.IsTrue(ValidationResults.TryRecordRun(complete));
            Assert.IsFalse(ValidationResults.TryMergeScopedRun(incomplete));
            Assert.IsTrue(ValidationResults.TryMergeScopedRun(complete));
        }

        [TestCase(CommitOperation.Full)]
        [TestCase(CommitOperation.Scoped)]
        public void ARunWithAnInvalidFindingCannotPartiallyReplaceTheSnapshot(
            CommitOperation operation
        )
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "previous") }
            );
            ValidationRun invalid = Run(new InvalidFindingRule(), SecondGuid);

            Commit(operation, invalid);

            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.AreEqual("previous", ValidationResults.Snapshot()[0].Message);
        }

        [TestCase(CommitOperation.Full)]
        [TestCase(CommitOperation.Scoped)]
        public void ARunCannotReportAFindingForAnUnvisitedAsset(CommitOperation operation)
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "previous") }
            );
            ValidationRun invalid = Run(new UnrelatedFindingRule(), SecondGuid);

            Commit(operation, invalid);

            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.AreEqual("previous", ValidationResults.Snapshot()[0].Message);
        }

        [Test]
        public void RecordedAssetGuidViewRemainsLiveAcrossAFullCommit()
        {
            ValidationResults.Replace(FirstGuid, null);
            IReadOnlyList<string> recorded = ValidationResults.RecordedAssetGuids;

            ValidationResults.RecordRun(Run(new SilentRule(), SecondGuid));

            CollectionAssert.AreEqual(new[] { SecondGuid }, recorded);
            Assert.AreSame(recorded, ValidationResults.RecordedAssetGuids);
        }

        [Test]
        public void SuccessfulWindowCompletionReplacesResultsAndClearsStatus()
        {
            ValidationResults.Replace(FirstGuid, null);
            ValidationRun complete = Run(new NoisyRule(), SecondGuid);
            ValidationWindow window = Track(
                UnityEngine.ScriptableObject.CreateInstance<ValidationWindow>()
            );

            window.CompleteForTesting(complete);

            Assert.AreEqual(string.Empty, window.StatusForTesting);
            CollectionAssert.AreEqual(new[] { SecondGuid }, ValidationResults.RecordedAssetGuids);
            Assert.AreEqual(1, ValidationResults.Snapshot().Count);
        }

        [Test]
        public void FailedWindowCompletionRetainsResultsLogsAndExplainsTheState()
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "previous") }
            );
            ValidationRun failed = Run(new ThrowingRule(), SecondGuid);
            ExpectError(
                UnityEngine.LogType.Warning,
                @"\[Asset Validation\] Validation failed\. Previous results retained\."
            );
            ExpectError(UnityEngine.LogType.Error, @"\[Asset Validation\].*deliberate");
            ValidationWindow window = Track(
                UnityEngine.ScriptableObject.CreateInstance<ValidationWindow>()
            );

            window.CompleteForTesting(failed);

            Assert.AreEqual(
                "Validation failed. Previous results retained.",
                window.StatusForTesting
            );
            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.AreEqual("previous", ValidationResults.Snapshot()[0].Message);
        }

        [Test]
        public void CancellingAnIncompleteWindowRunRetainsResultsWithoutFailures()
        {
            ValidationResults.Replace(FirstGuid, null);
            ValidationRun cancelled = CreateRun(
                new List<IValidationRule> { new SilentRule() },
                FirstGuid,
                SecondGuid
            );
            Assert.IsFalse(cancelled.Step(0));
            cancelled.Cancel();
            ValidationWindow window = Track(
                UnityEngine.ScriptableObject.CreateInstance<ValidationWindow>()
            );

            window.CompleteForTesting(cancelled);

            Assert.AreEqual("Cancelled. Previous results retained.", window.StatusForTesting);
            CollectionAssert.AreEqual(new[] { FirstGuid }, ValidationResults.RecordedAssetGuids);
        }

        [Test]
        public void CancellingAfterAProcessedFailureStillLogsThatFailure()
        {
            ValidationResults.Replace(FirstGuid, null);
            ValidationRun cancelled = CreateRun(
                new List<IValidationRule> { new ThrowingRule() },
                FirstGuid,
                SecondGuid
            );
            Assert.IsFalse(cancelled.Step(0));
            Assert.AreEqual(1, cancelled.Failures.Count);
            cancelled.Cancel();
            ExpectError(UnityEngine.LogType.Error, @"\[Asset Validation\].*deliberate");
            ValidationWindow window = Track(
                UnityEngine.ScriptableObject.CreateInstance<ValidationWindow>()
            );

            window.CompleteForTesting(cancelled);

            Assert.AreEqual("Cancelled. Previous results retained.", window.StatusForTesting);
            CollectionAssert.AreEqual(new[] { FirstGuid }, ValidationResults.RecordedAssetGuids);
        }

        [Test]
        public void AFailedIncrementalRunKeepsItsTargetsQueuedWithoutImmediateRetry()
        {
            bool wasEnabled = ValidationAutoRun.Enabled;
            ValidationAutoRun.Enabled = true;
            ValidationAutoRun.ClearPendingForTesting();
            ValidationRun failed = Run(
                new List<IValidationRule> { new ThrowingRule() },
                FirstGuid,
                SecondGuid
            );
            ExpectError(
                UnityEngine.LogType.Warning,
                @"Incremental validation retained previous results because 2 rule or load failure\(s\)"
            );

            try
            {
                ValidationAutoRun.CompleteRunForTesting(failed);

                Assert.AreEqual(2, ValidationAutoRun.PendingCount);
                Assert.IsFalse(ValidationResults.HasRun);
            }
            finally
            {
                ValidationAutoRun.ClearPendingForTesting();
                ValidationAutoRun.Enabled = wasEnabled;
            }
        }

        [Test]
        public void AFailedIncrementalRunDoesNotClaimRequeueWhenAutoRunIsDisabled()
        {
            bool wasEnabled = ValidationAutoRun.Enabled;
            ValidationAutoRun.Enabled = false;
            ValidationAutoRun.ClearPendingForTesting();
            ValidationRun failed = Run(new ThrowingRule(), FirstGuid);
            ExpectError(
                UnityEngine.LogType.Warning,
                "Automatic validation is disabled, so the affected assets were not requeued"
            );

            try
            {
                ValidationAutoRun.CompleteRunForTesting(failed);

                Assert.AreEqual(0, ValidationAutoRun.PendingCount);
            }
            finally
            {
                ValidationAutoRun.ClearPendingForTesting();
                ValidationAutoRun.Enabled = wasEnabled;
            }
        }

        /// <summary>
        /// Deleting several assets is one notification and one pass, not one of each per asset.
        /// </summary>
        /// <remarks>
        /// The incremental re-check prunes every deleted GUID after an import. Doing that one
        /// <c>Forget</c> at a time raised once per asset, and every subscriber rebuilt its whole
        /// view each time -- against a store whose order list each removal also scanned linearly.
        /// </remarks>
        [Test]
        public void ForgettingManyAssetsRaisesOnceAndKeepsTheOrderOfTheRest()
        {
            const string ThirdGuid = "00000000000000000000000000000003";
            ValidationResults.Replace(FirstGuid, null);
            ValidationResults.Replace(SecondGuid, null);
            ValidationResults.Replace(ThirdGuid, null);

            int raised = 0;
            void Count() => raised++;

            int forgotten;
            ValidationResults.Changed += Count;
            try
            {
                forgotten = ValidationResults.ForgetAll(
                    new List<string> { FirstGuid, ThirdGuid, "not-recorded", null }
                );
            }
            finally
            {
                ValidationResults.Changed -= Count;
            }

            Assert.AreEqual(2, forgotten);
            Assert.AreEqual(1, raised);
            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
            Assert.AreEqual(
                new[] { SecondGuid },
                new List<string>(ValidationResults.RecordedAssetGuids).ToArray()
            );
        }

        /// <summary>
        /// Forgetting nothing changes nothing, and tells nobody.
        /// </summary>
        [Test]
        public void ForgettingNothingRaisesNothing()
        {
            ValidationResults.Replace(FirstGuid, null);

            int raised = 0;
            void Count() => raised++;

            ValidationResults.Changed += Count;
            try
            {
                Assert.AreEqual(0, ValidationResults.ForgetAll(null));
                Assert.AreEqual(0, ValidationResults.ForgetAll(new List<string>()));
                Assert.AreEqual(0, ValidationResults.ForgetAll(new List<string> { SecondGuid }));
            }
            finally
            {
                ValidationResults.Changed -= Count;
            }

            Assert.AreEqual(0, raised);
            Assert.AreEqual(1, ValidationResults.CheckedAssetCount);
        }

        /// <summary>
        /// <c>CopyInto</c> answers exactly what <c>Snapshot</c> does, into a reused buffer.
        /// </summary>
        [Test]
        public void CopyIntoMatchesSnapshotAndClearsTheDestinationFirst()
        {
            ValidationResults.MergeScopedRun(Run(new NoisyRule(), FirstGuid, SecondGuid));

            List<ValidationFinding> destination = new List<ValidationFinding>
            {
                new ValidationFinding("stale", ValidationSeverity.Info, null, "x", "y", "z", "w"),
            };
            ValidationResults.CopyInto(destination);

            Assert.AreEqual(ValidationResults.Snapshot(), destination);
        }

        /// <summary>
        /// One merge is one notification, however many assets it touched.
        /// </summary>
        /// <remarks>
        /// Without the batch guard a re-check of forty imported assets raises forty times, and every
        /// subscriber rebuilds its whole view thirty-nine times for a state nobody saw.
        /// </remarks>
        [Test]
        public void AScopedMergeRaisesOnce()
        {
            int raised = 0;
            void Count() => raised++;

            ValidationResults.Changed += Count;
            try
            {
                ValidationResults.MergeScopedRun(Run(new NoisyRule(), FirstGuid, SecondGuid));
            }
            finally
            {
                ValidationResults.Changed -= Count;
            }

            Assert.AreEqual(1, raised);
        }

        /// <summary>
        /// A subscriber that throws must not take the rest of them down with it.
        /// </summary>
        /// <remarks>
        /// An exception escaping the notification would unwind through the store's own callers --
        /// an asset postprocessor drain among them -- so it is caught, reported, and the remaining
        /// subscribers still run.
        /// </remarks>
        [Test]
        public void ASubscriberThatThrowsDoesNotStopTheOthers()
        {
            int reached = 0;
            void Throw() => throw new System.InvalidOperationException("deliberate");
            void Reach() => reached++;

            ExpectError(UnityEngine.LogType.Exception, "InvalidOperationException: deliberate");
            ValidationResults.Changed += Throw;
            ValidationResults.Changed += Reach;
            try
            {
                ValidationResults.Replace(FirstGuid, null);
            }
            finally
            {
                ValidationResults.Changed -= Throw;
                ValidationResults.Changed -= Reach;
            }

            Assert.AreEqual(1, reached);
        }

        private static ValidationRun Run(IValidationRule rule, params string[] guids)
        {
            return Run(new List<IValidationRule> { rule }, guids);
        }

        private static ValidationRun Run(
            IReadOnlyList<IValidationRule> rules,
            params string[] guids
        )
        {
            ValidationRun run = CreateRun(rules, guids);
            while (!run.Step(double.MaxValue)) { }

            return run;
        }

        private static ValidationRun CreateRun(
            IReadOnlyList<IValidationRule> rules,
            params string[] guids
        )
        {
            List<ValidationTarget> targets = new List<ValidationTarget>(guids.Length);
            for (int index = 0; index < guids.Length; index++)
            {
                targets.Add(
                    new ValidationTarget(guids[index], "Assets/" + guids[index] + ".asset", null)
                );
            }

            return new ValidationRun(rules, targets, _ => null);
        }

        private static ValidationRun CreateRejectedRun(RejectedRunState state)
        {
            switch (state)
            {
                case RejectedRunState.Null:
                    return null;
                case RejectedRunState.Incomplete:
                    return CreateIncompleteRun();
                case RejectedRunState.Cancelled:
                    ValidationRun cancelled = CreateIncompleteRun();
                    cancelled.Cancel();
                    return cancelled;
                case RejectedRunState.Failed:
                    ValidationRun failed = Run(
                        new List<IValidationRule> { new NoisyRule(), new ThrowingRule() },
                        FirstGuid,
                        ThirdGuid
                    );
                    Assert.IsNotEmpty(failed.Findings);
                    Assert.IsNotEmpty(failed.Failures);
                    return failed;
                default:
                    Assert.Fail("Unexpected rejected run state: " + state);
                    return null;
            }
        }

        private static ValidationRun CreateIncompleteRun()
        {
            ValidationRun run = CreateRun(
                new List<IValidationRule> { new NoisyRule() },
                FirstGuid,
                ThirdGuid
            );
            Assert.IsFalse(run.Step(0));
            Assert.IsNotEmpty(run.Findings);
            return run;
        }

        private static void Commit(CommitOperation operation, ValidationRun run)
        {
            switch (operation)
            {
                case CommitOperation.Full:
                    ValidationResults.RecordRun(run);
                    return;
                case CommitOperation.Scoped:
                    ValidationResults.MergeScopedRun(run);
                    return;
                default:
                    Assert.Fail("Unexpected commit operation: " + operation);
                    return;
            }
        }

        private static ValidationFinding Finding(string guid, string discriminator)
        {
            return new ValidationFinding(
                "Test",
                ValidationSeverity.Warning,
                null,
                guid,
                "Assets/" + guid + ".asset",
                discriminator,
                discriminator
            );
        }

        /// <summary>Claims every asset and reports nothing, so a target is checked and clean.</summary>
        private sealed class SilentRule : IValidationRule
        {
            public string RuleId => nameof(SilentRule);

            public string DisplayName => nameof(SilentRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            ) { }
        }

        /// <summary>Reports one finding per asset.</summary>
        private sealed class NoisyRule : IValidationRule
        {
            public string RuleId => nameof(NoisyRule);

            public string DisplayName => nameof(NoisyRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                findings.Add(
                    new ValidationFinding(
                        nameof(NoisyRule),
                        ValidationSeverity.Warning,
                        null,
                        target.AssetGuid,
                        target.AssetPath,
                        "only",
                        "found something"
                    )
                );
            }
        }

        private sealed class ThrowingRule : IValidationRule
        {
            public string RuleId => nameof(ThrowingRule);

            public string DisplayName => nameof(ThrowingRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                throw new InvalidOperationException("deliberate");
            }
        }

        private sealed class InvalidFindingRule : IValidationRule
        {
            public string RuleId => nameof(InvalidFindingRule);

            public string DisplayName => nameof(InvalidFindingRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                findings.Add(default);
            }
        }

        private sealed class UnrelatedFindingRule : IValidationRule
        {
            public string RuleId => nameof(UnrelatedFindingRule);

            public string DisplayName => nameof(UnrelatedFindingRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                findings.Add(Finding(ThirdGuid, "unvisited"));
            }
        }

        public enum CommitOperation
        {
            Full = 0,
            Scoped = 1,
        }

        public enum RejectedRunState
        {
            Null = 0,
            Incomplete = 1,
            Cancelled = 2,
            Failed = 3,
        }
    }
}
