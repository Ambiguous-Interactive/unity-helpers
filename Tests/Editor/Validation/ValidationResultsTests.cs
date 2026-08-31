// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
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

        /// <summary>
        /// A cancelled run never reached its later targets, so recording them as clean would be a
        /// claim it did not make.
        /// </summary>
        [Test]
        public void ACancelledScopedMergeIsDropped()
        {
            ValidationResults.Replace(
                FirstGuid,
                new List<ValidationFinding> { Finding(FirstGuid, "was broken") }
            );
            ValidationRun run = Run(new SilentRule(), FirstGuid, SecondGuid);
            run.Cancel();

            ValidationResults.MergeScopedRun(run);

            Assert.AreEqual(1, ValidationResults.Snapshot().Count);
        }

        /// <summary>
        /// One merge is one notification, however many assets it touched.
        /// </summary>
        /// <remarks>
        /// Without the batch guard a re-check of forty imported assets raises forty times, and every
        /// subscriber rebuilds its whole view thirty-nine times for a state nobody saw.
        /// </remarks>
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
            List<ValidationTarget> targets = new List<ValidationTarget>(guids.Length);
            for (int index = 0; index < guids.Length; index++)
            {
                targets.Add(
                    new ValidationTarget(guids[index], "Assets/" + guids[index] + ".asset", null)
                );
            }

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { rule },
                targets,
                _ => null
            );
            while (!run.Step(double.MaxValue)) { }

            return run;
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
    }
}
