// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the bounded validation engine: what it loads, what it refuses to let a rule break, and
    /// that it always makes progress.
    /// </summary>
    /// <remarks>
    /// The run is driven by an injected loader rather than the asset database, so every assertion
    /// here is about the engine rather than about whatever assets the test project happens to hold.
    /// </remarks>
    [TestFixture]
    public sealed class ValidationRunTests : CommonTestBase
    {
        private const string FirstGuid = "00000000000000000000000000000001";
        private const string SecondGuid = "00000000000000000000000000000002";

        [Test]
        public void AnEmptyRunIsCompleteBeforeItStarts()
        {
            ValidationRun run = new ValidationRun(null, null, Never);

            Assert.IsTrue(run.IsComplete);
            Assert.AreEqual(0, run.TotalCount);
            Assert.IsTrue(run.Step(1000.0));
            Assert.IsEmpty(run.Findings);
            Assert.IsEmpty(run.Failures);
        }

        [Test]
        public void NullRulesAndUnusableTargetsAreDroppedRatherThanThrown()
        {
            CountingRule counted = new CountingRule(true);
            List<ValidationTarget> targets = new List<ValidationTarget>
            {
                default,
                new ValidationTarget(null, "Assets/NoGuid.asset", null),
                new ValidationTarget(FirstGuid, null, null),
                new ValidationTarget(FirstGuid, "Assets/Real.asset", typeof(ScriptableObject)),
            };

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { null, counted, null },
                targets,
                Never
            );

            Assert.AreEqual(1, run.TotalCount);
            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, counted.AppliesToCalls);
        }

        [Test]
        public void AnAssetNoRuleClaimsIsNeverLoaded()
        {
            CountingRule declining = new CountingRule(false);
            int loads = 0;

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { declining },
                TwoTargets(),
                target =>
                {
                    loads++;
                    return null;
                }
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(2, declining.AppliesToCalls);
            Assert.AreEqual(0, declining.ValidateCalls);
            Assert.AreEqual(0, loads, "A declined asset must not be deserialized.");
        }

        [Test]
        public void AClaimedAssetIsLoadedOncePerAssetNotOncePerRule()
        {
            CountingRule first = new CountingRule(true);
            CountingRule second = new CountingRule(true);
            int loads = 0;

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { first, second },
                TwoTargets(),
                target =>
                {
                    loads++;
                    return null;
                }
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(2, first.ValidateCalls);
            Assert.AreEqual(2, second.ValidateCalls);
            Assert.AreEqual(2, loads);
        }

        [Test]
        public void TheLoadedAssetReachesTheRuleAndTheFinding()
        {
            ScriptableObject asset = Track(ScriptableObject.CreateInstance<ScriptableObject>());
            ReportingRule reporting = new ReportingRule(ValidationSeverity.Error, 1);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { reporting },
                OneTarget(),
                target => asset
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.Findings.Count);
            Assert.AreSame(asset, reporting.LastAsset);
            Assert.IsTrue(run.Findings[0].TryGetTarget(out Object bound));
            Assert.AreSame(asset, bound);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        public void EveryFindingARuleReportsIsCollected(int perAsset)
        {
            ReportingRule reporting = new ReportingRule(ValidationSeverity.Warning, perAsset);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { reporting },
                TwoTargets(),
                Never
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(perAsset * 2, run.Findings.Count);
            Assert.IsEmpty(run.Failures);
        }

        [Test]
        public void ARuleThatThrowsWhileValidatingIsRecordedAndTheOthersStillRun()
        {
            ReportingRule healthy = new ReportingRule(ValidationSeverity.Info, 1);
            ThrowingRule broken = new ThrowingRule(throwFromAppliesTo: false);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { broken, healthy },
                TwoTargets(),
                Never
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(2, run.Failures.Count);
            Assert.AreEqual(broken.RuleId, run.Failures[0].RuleId);
            Assert.AreEqual(2, run.Findings.Count, "A broken rule must not hide a healthy one.");
        }

        [Test]
        public void PartialFindingsFromAThrowingRuleAreDiscarded()
        {
            ThrowingRule broken = new ThrowingRule(throwFromAppliesTo: false)
            {
                FindingsBeforeThrowing = 3,
            };

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { broken },
                OneTarget(),
                Never
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.Failures.Count);
            Assert.IsEmpty(
                run.Findings,
                "Half an answer presented as a whole one is worse than the failure."
            );
        }

        [Test]
        public void ARuleThatThrowsWhileClaimingIsRecordedAndSkipped()
        {
            ThrowingRule broken = new ThrowingRule(throwFromAppliesTo: true);
            int loads = 0;

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { broken },
                OneTarget(),
                target =>
                {
                    loads++;
                    return null;
                }
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.Failures.Count);
            Assert.AreEqual(0, broken.ValidateCalls);
            Assert.AreEqual(0, loads);
        }

        [Test]
        public void ALoaderThatThrowsIsBlamedOnTheLoadAndTheRuleStillRuns()
        {
            ReportingRule reporting = new ReportingRule(ValidationSeverity.Warning, 1);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { reporting },
                OneTarget(),
                target => throw new InvalidOperationException("import is broken")
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.Failures.Count);
            Assert.IsTrue(run.Failures[0].IsLoadFailure, "A load failure is not a rule's fault.");
            Assert.IsTrue(run.Failures[0].ToString().Contains("Loading the asset"));
            Assert.AreEqual(1, reporting.ValidateCalls);
            Assert.IsTrue(reporting.LastAsset == null);
        }

        [Test]
        public void ARuleWithNoUsableIdIsStillDistinguishableFromALoadFailure()
        {
            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { new NamelessThrowingRule() },
                OneTarget(),
                Never
            );

            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.Failures.Count);
            Assert.IsFalse(run.Failures[0].IsLoadFailure);
            Assert.AreEqual(typeof(NamelessThrowingRule).FullName, run.Failures[0].RuleId);
        }

        [TestCase(-1.0)]
        [TestCase(0.0)]
        public void ANonPositiveBudgetStillAdvancesExactlyOneAsset(double budget)
        {
            CountingRule counted = new CountingRule(true);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { counted },
                TwoTargets(),
                Never
            );

            Assert.IsFalse(run.Step(budget));
            Assert.AreEqual(1, run.ProcessedCount);
            Assert.IsTrue(run.Step(budget));
            Assert.AreEqual(2, run.ProcessedCount);
        }

        [Test]
        public void CancellingKeepsWhatWasAlreadyFoundAndStopsTheRest()
        {
            ReportingRule reporting = new ReportingRule(ValidationSeverity.Error, 1);

            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { reporting },
                TwoTargets(),
                Never
            );

            Assert.IsFalse(run.Step(0.0));
            run.Cancel();

            Assert.IsTrue(run.IsComplete);
            Assert.IsTrue(run.IsCancelled);
            Assert.AreEqual(1, run.ProcessedCount);
            Assert.AreEqual(1, run.Findings.Count);
            Assert.IsTrue(run.Step(1000.0));
            Assert.AreEqual(1, run.ProcessedCount, "A cancelled run must not resume.");
        }

        [Test]
        public void AFindingIsIdentifiedByRuleAssetAndDiscriminatorOnly()
        {
            ValidationFinding moved = new ValidationFinding(
                "rule",
                ValidationSeverity.Warning,
                null,
                FirstGuid,
                "Assets/Before.asset",
                "field",
                "one wording"
            );
            ValidationFinding sameAssetElsewhere = new ValidationFinding(
                "rule",
                ValidationSeverity.Error,
                null,
                FirstGuid,
                "Assets/Moved/After.asset",
                "field",
                "another wording"
            );
            ValidationFinding otherMember = new ValidationFinding(
                "rule",
                ValidationSeverity.Warning,
                null,
                FirstGuid,
                "Assets/Before.asset",
                "otherField",
                "one wording"
            );

            Assert.AreEqual(moved.Id, sameAssetElsewhere.Id);
            Assert.AreNotEqual(moved.Id, otherMember.Id);
        }

        [Test]
        public void AnAbsentTargetIsReportedAsAbsent()
        {
            ValidationFinding finding = new ValidationFinding(
                "rule",
                ValidationSeverity.Info,
                null,
                FirstGuid,
                "Assets/Real.asset",
                null,
                "message"
            );

            Assert.IsFalse(finding.TryGetTarget(out Object target));
            Assert.IsTrue(target == null);
        }

        [Test]
        public void ADestroyedTargetHandsBackNothingRatherThanADeadReference()
        {
            ScriptableObject asset = ScriptableObject.CreateInstance<ScriptableObject>(); // UNH-SUPPRESS: UNH002 - destroyed by this test on purpose
            ValidationFinding finding = new ValidationFinding(
                "rule",
                ValidationSeverity.Error,
                asset,
                FirstGuid,
                "Assets/Real.asset",
                null,
                "message"
            );
            Assert.IsTrue(finding.TryGetTarget(out Object alive));
            Assert.AreSame(asset, alive);

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: UNH001 - destruction is what this test measures

            Assert.IsFalse(finding.TryGetTarget(out Object destroyed));
            Assert.IsTrue(
                ReferenceEquals(destroyed, null),
                "A dead reference handed back would throw for a caller that ignores the bool."
            );
        }

        [Test]
        public void TwoFindingsAboutOneDestroyedAssetStayDistinctFromFindingsAboutNothing()
        {
            ScriptableObject asset = ScriptableObject.CreateInstance<ScriptableObject>(); // UNH-SUPPRESS: UNH002 - destroyed by this test on purpose
            ValidationFinding bound = Finding(asset);
            ValidationFinding unbound = Finding(null);
            Assert.AreNotEqual(bound, unbound);

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: UNH001 - destruction is what this test measures

            Assert.AreNotEqual(
                bound,
                unbound,
                "Object's == is a liveness check, so equality must compare by reference."
            );
            Assert.AreEqual(bound, Finding(asset), "The same dead asset is the same finding.");
            Assert.AreEqual(bound.GetHashCode(), Finding(asset).GetHashCode());
        }

        [Test]
        public void SeverityOrdersFromLeastToMostSevere()
        {
            Assert.IsTrue(ValidationSeverity.Info < ValidationSeverity.Warning);
            Assert.IsTrue(ValidationSeverity.Warning < ValidationSeverity.Error);
        }

        [Test]
        public void TargetsAreTheSameWhenTheGuidIsTheSame()
        {
            ValidationTarget original = new ValidationTarget(
                FirstGuid,
                "Assets/Before.asset",
                typeof(ScriptableObject)
            );
            ValidationTarget moved = new ValidationTarget(FirstGuid, "Assets/After.asset", null);
            ValidationTarget other = new ValidationTarget(SecondGuid, "Assets/Before.asset", null);

            Assert.AreEqual(original, moved);
            Assert.AreEqual(original.GetHashCode(), moved.GetHashCode());
            Assert.AreNotEqual(original, other);
        }

        [Test]
        public void EnumeratingFindsTheAssetsInAFolderAndNotTheFolder()
        {
            string folder = "Assets/" + nameof(ValidationRunTests) + "Enumerate";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", nameof(ValidationRunTests) + "Enumerate");
            }
            TrackFolder(folder);

            // A concrete asset type ensures missing script binding cannot satisfy the type assertion.
            string assetPath = folder + "/Probe.asset";
            AssetDatabase.CreateAsset(
                ScriptableObject.CreateInstance<DroppedSerializedFieldAsset>(), // UNH-SUPPRESS: UNH002 - Asset managed by test cleanup
                assetPath
            );
            TrackAssetPath(assetPath);
            AssetDatabase.SaveAssets();

            List<ValidationTarget> targets = ValidationTargets.Enumerate(folder);

            Assert.AreEqual(1, targets.Count, "The folder itself must not be enumerated.");
            Assert.AreEqual(assetPath, targets[0].AssetPath);
            Assert.AreEqual(AssetDatabase.AssetPathToGUID(assetPath), targets[0].AssetGuid);
            Assert.AreEqual(typeof(DroppedSerializedFieldAsset), targets[0].MainAssetType);
            Assert.IsTrue(targets[0].IsValid());
        }

        [Test]
        public void EnumeratingAFolderThatDoesNotExistIsEmptyRatherThanThrown()
        {
            Assert.IsEmpty(ValidationTargets.Enumerate("Assets/NoSuchFolderForValidationTests"));
        }

        private static ValidationFinding Finding(Object target)
        {
            return new ValidationFinding(
                "rule",
                ValidationSeverity.Error,
                target,
                FirstGuid,
                "Assets/Real.asset",
                null,
                "message"
            );
        }

        private static Object Never(ValidationTarget target)
        {
            return null;
        }

        private static List<ValidationTarget> OneTarget()
        {
            return new List<ValidationTarget>
            {
                new ValidationTarget(FirstGuid, "Assets/First.asset", typeof(ScriptableObject)),
            };
        }

        private static List<ValidationTarget> TwoTargets()
        {
            List<ValidationTarget> targets = OneTarget();
            targets.Add(
                new ValidationTarget(SecondGuid, "Assets/Second.asset", typeof(ScriptableObject))
            );
            return targets;
        }

        /// <summary>Counts what the engine asked it, and answers the same way every time.</summary>
        private sealed class CountingRule : IValidationRule
        {
            private readonly bool _claims;

            internal CountingRule(bool claims)
            {
                _claims = claims;
            }

            internal int AppliesToCalls { get; private set; }

            internal int ValidateCalls { get; private set; }

            public string RuleId => nameof(CountingRule);

            public string DisplayName => nameof(CountingRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                AppliesToCalls++;
                return _claims;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                ValidateCalls++;
            }
        }

        /// <summary>Reports a fixed number of findings per asset and remembers what it was given.</summary>
        private sealed class ReportingRule : IValidationRule
        {
            private readonly ValidationSeverity _severity;
            private readonly int _perAsset;

            internal ReportingRule(ValidationSeverity severity, int perAsset)
            {
                _severity = severity;
                _perAsset = perAsset;
            }

            internal int ValidateCalls { get; private set; }

            internal Object LastAsset { get; private set; }

            public string RuleId => nameof(ReportingRule);

            public string DisplayName => nameof(ReportingRule);

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
                ValidateCalls++;
                LastAsset = asset;
                for (int index = 0; index < _perAsset; index++)
                {
                    findings.Add(
                        new ValidationFinding(
                            RuleId,
                            _severity,
                            asset,
                            target.AssetGuid,
                            target.AssetPath,
                            index.ToString(),
                            "found something"
                        )
                    );
                }
            }
        }

        /// <summary>Declares no usable identifier and throws, so attribution has to fall back.</summary>
        private sealed class NamelessThrowingRule : IValidationRule
        {
            public string RuleId => null;

            public string DisplayName => nameof(NamelessThrowingRule);

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
                throw new InvalidOperationException("nameless and broken");
            }
        }

        /// <summary>Throws from whichever half the test names, optionally after reporting.</summary>
        private sealed class ThrowingRule : IValidationRule
        {
            private readonly bool _throwFromAppliesTo;

            internal ThrowingRule(bool throwFromAppliesTo)
            {
                _throwFromAppliesTo = throwFromAppliesTo;
            }

            internal int FindingsBeforeThrowing { get; set; }

            internal int ValidateCalls { get; private set; }

            public string RuleId => nameof(ThrowingRule);

            public string DisplayName => nameof(ThrowingRule);

            public bool AppliesTo(in ValidationTarget target)
            {
                if (_throwFromAppliesTo)
                {
                    throw new InvalidOperationException("claiming is broken");
                }

                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                ValidateCalls++;
                for (int index = 0; index < FindingsBeforeThrowing; index++)
                {
                    findings.Add(
                        new ValidationFinding(
                            RuleId,
                            ValidationSeverity.Error,
                            asset,
                            target.AssetGuid,
                            target.AssetPath,
                            index.ToString(),
                            "partial"
                        )
                    );
                }

                throw new InvalidOperationException("validating is broken");
            }
        }
    }
}
