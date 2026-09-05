// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Editor.Tools;

    /// <summary>
    /// Pins the zero-touch subtype form: a field number the author never writes, taken from a
    /// committed manifest, producing exactly the bytes the written number produces.
    /// </summary>
    /// <remarks>
    /// Two properties carry the whole design, and both are asserted here rather than argued. The
    /// first is byte identity -- a subtype resolved from the manifest at N has to be
    /// indistinguishable on the wire from one that wrote N in its own attribute, and from what
    /// protobuf-net writes. The second is that the numbers never move: add, remove and re-add a
    /// subtype and the number it had is the number it gets back, because a number that moves is a
    /// saved payload that reads as the wrong type with nothing to warn about it.
    /// </remarks>
    [TestFixture]
    public sealed class SubtypeTagManifestTests
    {
        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void AManifestNumberedHierarchyProducesTheBytesAWrittenNumberProduces(
            string label,
            SubtypeFormRoot written,
            ManifestFormRoot fromManifest
        )
        {
            string hand = Encode<SubtypeFormRoot>(written);

            Assert.AreEqual(hand, Encode<ManifestFormRoot>(fromManifest), label);
            Assert.AreEqual(hand, OracleHex(written), label + " against the oracle");
            Assert.AreEqual(hand, OracleHex(fromManifest), label + " against the oracle");
        }

        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void AManifestNumberedHierarchyIsIdenticalUnderALengthPrefixToo(
            string label,
            SubtypeFormRoot written,
            ManifestFormRoot fromManifest
        )
        {
            string hand = Encode(new SubtypeFormHolder { Value = written, Trailer = 2 });

            Assert.AreEqual(
                hand,
                Encode(new ManifestFormHolder { Value = fromManifest, Trailer = 2 }),
                label
            );
            Assert.AreEqual(
                hand,
                OracleHex(new ManifestFormHolder { Value = fromManifest, Trailer = 2 }),
                label + " against the oracle"
            );
        }

        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void AManifestNumberedHierarchyRoundTripsAsItsConcreteType(
            string label,
            SubtypeFormRoot written,
            ManifestFormRoot fromManifest
        )
        {
            Assert.AreEqual(
                written.GetType().Name.Substring("SubtypeForm".Length),
                RoundTrip<ManifestFormRoot>(fromManifest)
                    .GetType()
                    .Name.Substring("ManifestForm".Length),
                label
            );
        }

        [Test]
        public void AManifestNumberedChainCoversItsSubtypesAndStopsAtItsEdges()
        {
            IWProtoPolymorphicFormatter root = ManifestFormRoot.WProtoFormatter.Instance;

            Assert.IsTrue(root.CanWrite(typeof(ManifestFormAlpha)));
            Assert.IsTrue(root.CanWrite(typeof(ManifestFormBeta)));
            Assert.IsTrue(root.CanWrite(typeof(ManifestFormGamma)));
            Assert.IsFalse(root.CanWrite(typeof(SubtypeFormAlpha)), "an unrelated chain");
        }

        [TestCaseSource(nameof(ResolvedTagCases))]
        public void ATagLessDeclarationTakesItsFieldNumberFromTheManifest(int tag)
        {
            string source = Fixture(
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), "
                    + tag
                    + ")]",
                "[WProtoSubtype(typeof(Base))]"
            );

            Assert.IsEmpty(Describe(Run(source, out Compilation generated)));
            StringAssert.Contains(
                "writer.TryWriteMessage(" + tag + ", global::Consumer.Sub.WProtoFormatter.Instance",
                FormatterFor("Base", generated)
            );
        }

        [Test]
        public void ATagLessDeclarationWithNoManifestEntryCannotReachAPlayer()
        {
            /*
             * Inventing tags from the current compilation would make wire identity depend on which types it
             * contains.
             */
            Diagnostic match = Run(Fixture(string.Empty, "[WProtoSubtype(typeof(Base))]"))
                .Single(diagnostic => diagnostic.Id == "WPROTO041");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            StringAssert.Contains("Consumer.Sub", match.GetMessage());
            StringAssert.Contains("Consumer.Base", match.GetMessage());
            StringAssert.Contains("Assign WallstopProto Subtype Tags", match.GetMessage());
            StringAssert.Contains("WProtoSubtypeTag", match.GetMessage());
            StringAssert.Contains("UNITY_EDITOR is not defined", match.GetMessage());
        }

        [Test]
        public void ATagLessDeclarationWithNoManifestEntryIsOnlyAWarningInTheEditor()
        {
            /*
             * Editor errors hide unnumbered types from TypeCache and prevent automatic assignment; warnings
             * keep them discoverable.
             */
            Diagnostic match = Run(
                    Fixture(string.Empty, "[WProtoSubtype(typeof(Base))]"),
                    out Compilation generated,
                    "UNITY_EDITOR"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO041");

            Assert.AreEqual(DiagnosticSeverity.Warning, match.Severity);
            StringAssert.Contains("on the next assembly reload", match.GetMessage());
            Assert.IsEmpty(
                generated
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage()),
                "a warning that still left the compilation broken would be no better than the error"
            );
        }

        [Test]
        public void TheUnassignedSeverityFollowsTheUnityEditorSymbolAndNothingElse()
        {
            string source = Fixture(string.Empty, "[WProtoSubtype(typeof(Base))]");

            Assert.AreEqual(
                DiagnosticSeverity.Error,
                Severity(Run(source, out Compilation _)),
                "no UNITY_EDITOR"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Warning,
                Severity(Run(source, out Compilation _, "UNITY_EDITOR")),
                "UNITY_EDITOR"
            );
            Assert.AreEqual(
                DiagnosticSeverity.Error,
                Severity(Run(source, out Compilation _, "UNITY_ANDROID", "DEVELOPMENT_BUILD")),
                "an unrelated symbol must not soften it"
            );
        }

        [Test]
        public void AnUnassignedSubtypeLeavesNoOrphanedIncludeBehindIt()
        {
            // Withholding a subtype must also remove references to its unavailable formatter.
            string source = Fixture(string.Empty, "[WProtoSubtype(typeof(Base))]");

            CollectionAssert.AreEqual(
                new[] { "WPROTO041" },
                Run(source, out Compilation generated)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Id)
                    .Distinct()
                    .ToArray()
            );

            Assert.IsEmpty(
                generated
                    .GetDiagnostics()
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Id + " " + diagnostic.GetMessage())
            );
        }

        [Test]
        public void AManifestEntryForAnotherBaseDoesNotSatisfyADeclaration()
        {
            // Manifest tags belong to a base/subtype pair; another base owns a different field-number space.
            Assert.IsNotEmpty(
                Run(
                        Fixture(
                            "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Other), 7)]"
                                + "\n[assembly: WProtoSubtypeTag(\"Consumer.Other\", typeof(Consumer.Base), 9)]",
                            "[WProtoSubtype(typeof(Base))]",
                            "[WProtoContract] [WProtoSubtype(typeof(Base), 9)] public partial class Other : Base { [WProtoMember(1)] public int O; }"
                        )
                    )
                    .Where(diagnostic => diagnostic.Id == "WPROTO041")
            );
        }

        [Test]
        public void AWrittenFieldNumberStillWorksAndStillWinsOverTheManifest()
        {
            string source = Fixture(
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 7)]",
                "[WProtoSubtype(typeof(Base), 5)]"
            );

            Assert.IsEmpty(Describe(Run(source, out Compilation generated)));

            string formatter = FormatterFor("Base", generated);
            StringAssert.Contains(
                "writer.TryWriteMessage(5, global::Consumer.Sub.WProtoFormatter.Instance",
                formatter
            );
            Assert.IsFalse(formatter.Contains("TryWriteMessage(7,"), formatter);
        }

        [Test]
        public void AWrittenFieldNumberThatCollidesWithAManifestNumberIsRefused()
        {
            Diagnostic match = Run(
                    Fixture(
                        "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 5)]",
                        "[WProtoSubtype(typeof(Base))]",
                        "[WProtoContract] [WProtoSubtype(typeof(Base), 5)] public partial class Other : Base { [WProtoMember(1)] public int O; }"
                    )
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO039");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            StringAssert.Contains("5", match.GetMessage());
        }

        [TestCaseSource(nameof(CorruptManifestCases))]
        public void AManifestEntryThatCannotBeHonouredIsAnError(
            string label,
            string assemblyAttributes,
            string mustSay
        )
        {
            Diagnostic match = Run(
                    Fixture(assemblyAttributes, "[WProtoSubtype(typeof(Base))]", ExtraSubtype)
                )
                .First(diagnostic => diagnostic.Id == "WPROTO042");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity, label);
            StringAssert.Contains(mustSay, match.GetMessage(), label);
        }

        [Test]
        public void ARetiredNumberIsNotHandedToANewType()
        {
            // Reusing a deleted subtype tag would reinterpret old payloads as the new subtype.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Added", "N.Base") },
                NoEntries,
                NoEntries,
                new[] { Entry("N.Removed", "N.Base", 1) }
            );

            CollectionAssert.AreEqual(new[] { "N.Added=2" }, Describe(plan.Assigned));
            CollectionAssert.AreEqual(new[] { "N.Removed=1" }, Describe(plan.Retired));
        }

        [Test]
        public void RemovingASubtypeRetiresItsNumberRatherThanFreeingIt()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Kept", "N.Base") },
                NoEntries,
                new[] { Entry("N.Kept", "N.Base", 1), Entry("N.Gone", "N.Base", 2) },
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.Kept=1" }, Describe(plan.Assigned));
            CollectionAssert.AreEqual(new[] { "N.Gone=2" }, Describe(plan.Retired));
        }

        [Test]
        public void ReAddingARetiredTypeRestoresTheNumberItHad()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Gone", "N.Base"), Declare("N.Later", "N.Base") },
                NoEntries,
                new[] { Entry("N.Later", "N.Base", 2) },
                new[] { Entry("N.Gone", "N.Base", 1) }
            );

            CollectionAssert.AreEqual(new[] { "N.Gone=1", "N.Later=2" }, Describe(plan.Assigned));
            Assert.IsEmpty(plan.Retired, "the number is in use again, so it is no longer retired");
        }

        [Test]
        public void AnUnseenDeclarationsNumberIsKeptByTheUnattendedPassAndRetiredByTheExplicitOne()
        {
            /*
             * Missing TypeCache entries may be hidden behind defines, so partial surveys cannot infer
             * deletion.
             */
            WProtoSubtypeTagPlan.Declaration[] visible = { Declare("N.Fresh", "N.Base") };
            WProtoSubtypeTagPlan.Entry[] committed = { Entry("N.Hidden", "N.Base", 1) };

            WProtoSubtypeTagPlan unattended = WProtoSubtypeTagPlan.Create(
                visible,
                NoEntries,
                committed,
                NoEntries,
                WProtoSubtypeTagDiscovery.Partial
            );

            CollectionAssert.AreEqual(
                new[] { "N.Hidden=1", "N.Fresh=2" },
                Describe(unattended.Assigned),
                "the entry this editor cannot see keeps its number"
            );
            Assert.IsEmpty(
                unattended.Retired,
                "an unattended run may never turn 'I cannot see it' into 'it is retired'"
            );
            CollectionAssert.AreEqual(
                new[] { "N.Fresh=2" },
                Describe(unattended.FreshlyAssigned),
                "and the hidden entry is not reported as newly numbered either"
            );

            WProtoSubtypeTagPlan asked = WProtoSubtypeTagPlan.Create(
                visible,
                NoEntries,
                committed,
                NoEntries,
                WProtoSubtypeTagDiscovery.Complete
            );

            CollectionAssert.AreEqual(new[] { "N.Fresh=2" }, Describe(asked.Assigned));
            CollectionAssert.AreEqual(
                new[] { "N.Hidden=1" },
                Describe(asked.Retired),
                "the explicit run still retires it, where a human reads the diff"
            );
        }

        [Test]
        public void AnUnattendedPassOverAnAssemblyItCannotFullySeeWritesNothingAtAll()
        {
            WProtoSubtypeTagPlan.Entry[] committed = { Entry("N.Hidden", "N.Base", 1) };
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new WProtoSubtypeTagPlan.Declaration[0],
                NoEntries,
                committed,
                NoEntries,
                WProtoSubtypeTagDiscovery.Partial
            );
            string rendered = plan.Render("Some.Assembly");

            Assert.IsEmpty(plan.Retired);
            Assert.IsFalse(
                WProtoSubtypeTagManifestFile.NeedsWrite(rendered, rendered, plan.IsEmpty)
            );
        }

        [TestCase(WProtoSubtypeTagDiscovery.Complete)]
        [TestCase(WProtoSubtypeTagDiscovery.Partial)]
        public void AReAddedTypeTakesBackItsRetiredNumberWhicheverDiscoveryTheRunHad(
            WProtoSubtypeTagDiscovery discovery
        )
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Gone", "N.Base"), Declare("N.Later", "N.Base") },
                NoEntries,
                new[] { Entry("N.Later", "N.Base", 2) },
                new[] { Entry("N.Gone", "N.Base", 1) },
                discovery
            );

            CollectionAssert.AreEqual(new[] { "N.Gone=1", "N.Later=2" }, Describe(plan.Assigned));
            Assert.IsEmpty(plan.Retired, "the number is in use again, so it is no longer retired");
            Assert.IsEmpty(
                plan.FreshlyAssigned,
                "and nothing was invented -- the number came back from retirement"
            );
        }

        [Test]
        public void TheUnattendedPassStillAssignsAGenuinelyNewDeclaration()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.New", "N.Base") },
                NoEntries,
                NoEntries,
                NoEntries,
                WProtoSubtypeTagDiscovery.Partial
            );

            CollectionAssert.AreEqual(new[] { "N.New=1" }, Describe(plan.Assigned));
            CollectionAssert.AreEqual(new[] { "N.New=1" }, Describe(plan.FreshlyAssigned));
        }

        [Test]
        public void AnExistingNumberIsNeverRecomputedEvenWhenASmallerOneIsFree()
        {
            // An existing tag remains a wire contract even when smaller numbers are unused.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Old", "N.Base"), Declare("N.New", "N.Base") },
                NoEntries,
                new[] { Entry("N.Old", "N.Base", 40) },
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.New=1", "N.Old=40" }, Describe(plan.Assigned));
        }

        [Test]
        public void AFreshNumberAvoidsTheBasesOwnMembersAndItsIncludes()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base"), Declare("N.Pinned", "N.Base", 3) },
                new[] { Entry("Id", "N.Base", 1), Entry("N.Included", "N.Base", 2) },
                NoEntries,
                NoEntries
            );

            CollectionAssert.AreEqual(
                new[] { "N.Pinned=3", "N.Sub=4" },
                Describe(plan.Assigned),
                "N.Sub avoids 1, 2 and 3; N.Pinned is recorded at the number it wrote itself"
            );
        }

        [Test]
        public void AFreshNumberAvoidsWhatTheBaseReservedWithWProtoReserved()
        {
            /*
             * Reserved tags are unavailable even without live claims; assigning one would fail the next
             * compilation.
             */
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base") },
                new[]
                {
                    Entry("Id", "N.Base", 1),
                    Entry("[WProtoReserved]", "N.Base", 2),
                    Entry("[WProtoReserved]", "N.Base", 3),
                },
                NoEntries,
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=4" }, Describe(plan.Assigned));
        }

        [Test]
        public void AFreshNumberSkipsTheReservedProtobufRange()
        {
            List<WProtoSubtypeTagPlan.Entry> reserved = new List<WProtoSubtypeTagPlan.Entry>();
            for (int tag = 1; tag < 19000; tag++)
            {
                reserved.Add(new WProtoSubtypeTagPlan.Entry("filler" + tag, "N.Base", tag));
            }

            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base") },
                reserved,
                NoEntries,
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=20000" }, Describe(plan.Assigned));
        }

        [Test]
        public void AssignmentIsDeterministicWhateverOrderTheTypesWereDiscoveredIn()
        {
            // TypeCache order is unstable and must not affect assigned wire tags.
            WProtoSubtypeTagPlan.Declaration[] forward =
            {
                Declare("N.Alpha", "N.Base"),
                Declare("N.Beta", "N.Base"),
                Declare("N.Gamma", "N.Base"),
            };
            // Qualify Enumerable.Reverse to avoid binding the void MemoryExtensions.Reverse overload.
            WProtoSubtypeTagPlan.Declaration[] reversed = Enumerable.Reverse(forward).ToArray();

            CollectionAssert.AreEqual(
                Describe(
                    WProtoSubtypeTagPlan.Create(forward, NoEntries, NoEntries, NoEntries).Assigned
                ),
                Describe(
                    WProtoSubtypeTagPlan.Create(reversed, NoEntries, NoEntries, NoEntries).Assigned
                )
            );
        }

        [Test]
        public void RunningAssignmentTwiceProducesTheSameFileByteForByte()
        {
            WProtoSubtypeTagPlan.Declaration[] declarations =
            {
                Declare("N.Alpha", "N.Base"),
                Declare("N.Beta", "N.Base"),
                Declare("N.Deep", "N.Alpha"),
            };
            WProtoSubtypeTagPlan.Entry[] retired = { Entry("N.Gone", "N.Base", 9) };

            WProtoSubtypeTagPlan first = WProtoSubtypeTagPlan.Create(
                declarations,
                NoEntries,
                NoEntries,
                retired
            );
            string once = first.Render("Some.Assembly");

            WProtoSubtypeTagPlan second = WProtoSubtypeTagPlan.Create(
                declarations,
                NoEntries,
                first.Assigned,
                first.Retired
            );

            Assert.AreEqual(once, second.Render("Some.Assembly"));
            Assert.AreEqual(
                once,
                WProtoSubtypeTagPlan
                    .Create(declarations, NoEntries, second.Assigned, second.Retired)
                    .Render("Some.Assembly"),
                "a third pass has to agree as well, or the file oscillates"
            );
        }

        [Test]
        public void TheRenderedManifestIsWhatTheGeneratorReadsBack()
        {
            string rendered = WProtoSubtypeTagPlan
                .Create(
                    new[] { Declare("Consumer.Sub", "Consumer.Base") },
                    new[] { Entry("A", "Consumer.Base", 1) },
                    NoEntries,
                    NoEntries
                )
                .Render("ConsumerAssembly");

            StringAssert.Contains("WProtoSubtypeTag(", rendered);
            Assert.IsEmpty(
                Describe(Run(Fixture(ManifestBody(rendered), "[WProtoSubtype(typeof(Base))]")))
            );
        }

        [Test]
        public void APromotedSubtypeKeepsItsNumberWithoutRetiringIt()
        {
            /*
             * Promoting a tag into its attribute must keep its manifest history so later deletion cannot free
             * it.
             */
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 4) },
                NoEntries,
                new[] { Entry("N.Sub", "N.Base", 4) },
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=4" }, Describe(plan.Assigned));
            Assert.IsEmpty(plan.Retired);
            Assert.IsEmpty(plan.FreshlyAssigned);
        }

        [Test]
        public void AnExplicitlyNumberedSubtypeIsRecordedSoItsNumberCanBeRetired()
        {
            // Explicit tags also need durable history because deleting the type removes its attribute.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Melee", "N.Base", 1) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            CollectionAssert.AreEqual(new[] { "N.Melee=1" }, Describe(plan.Assigned));
            Assert.IsEmpty(
                plan.FreshlyAssigned,
                "the number was written by the developer, so nothing was invented and the "
                    + "automatic pass has no reason to run"
            );
        }

        [Test]
        public void DeletingAnExplicitlyNumberedSubtypeRetiresItsNumber()
        {
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Melee", "N.Base", 1) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            WProtoSubtypeTagPlan afterDeletion = WProtoSubtypeTagPlan.Create(
                new WProtoSubtypeTagPlan.Declaration[0],
                NoEntries,
                recorded.Assigned,
                recorded.Retired
            );

            CollectionAssert.AreEqual(new[] { "N.Melee=1" }, Describe(afterDeletion.Retired));
            Assert.IsEmpty(afterDeletion.Assigned);
        }

        [Test]
        public void ANumberFreedByDeletingAnExplicitlyNumberedSubtypeIsNeverHandedOut()
        {
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Melee", "N.Base", 1) },
                NoEntries,
                NoEntries,
                NoEntries
            );
            WProtoSubtypeTagPlan afterDeletion = WProtoSubtypeTagPlan.Create(
                new WProtoSubtypeTagPlan.Declaration[0],
                NoEntries,
                recorded.Assigned,
                recorded.Retired
            );

            WProtoSubtypeTagPlan withSuccessor = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Later", "N.Base") },
                NoEntries,
                afterDeletion.Assigned,
                afterDeletion.Retired
            );

            CollectionAssert.AreEqual(new[] { "N.Later=2" }, Describe(withSuccessor.Assigned));
            CollectionAssert.AreEqual(new[] { "N.Melee=1" }, Describe(withSuccessor.Retired));
        }

        [Test]
        public void ReAddingAnExplicitlyNumberedSubtypeTakesBackTheNumberItHeld()
        {
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Melee", "N.Base", 1) },
                NoEntries,
                NoEntries,
                NoEntries
            );
            WProtoSubtypeTagPlan afterDeletion = WProtoSubtypeTagPlan.Create(
                new WProtoSubtypeTagPlan.Declaration[0],
                NoEntries,
                recorded.Assigned,
                recorded.Retired
            );

            WProtoSubtypeTagPlan restored = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Melee", "N.Base", 1) },
                NoEntries,
                afterDeletion.Assigned,
                afterDeletion.Retired
            );

            CollectionAssert.AreEqual(new[] { "N.Melee=1" }, Describe(restored.Assigned));
            Assert.IsEmpty(restored.Retired, "the number is in use again by the type that held it");
        }

        [Test]
        public void ReAddingATypeLiftsOnlyTheRetirementItReclaims()
        {
            // One pair can have several retired tags; restoring one must not free the others.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 5) },
                NoEntries,
                NoEntries,
                new[] { Entry("N.Sub", "N.Base", 5), Entry("N.Sub", "N.Base", 7) }
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=5" }, Describe(plan.Assigned));
            CollectionAssert.AreEqual(
                new[] { "N.Sub=7" },
                Describe(plan.Retired),
                "7 belonged to an earlier version of this type and is still spent"
            );
        }

        [Test]
        public void ANumberAPairRetiredTwiceOverIsNeverHandedToTheNextSubtype()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 1), Declare("N.Later", "N.Base") },
                NoEntries,
                NoEntries,
                new[] { Entry("N.Sub", "N.Base", 1), Entry("N.Sub", "N.Base", 2) }
            );

            CollectionAssert.DoesNotContain(
                plan.Assigned.Select(entry => entry.Tag).ToArray(),
                2,
                "2 is retired and may never be handed out again"
            );
            CollectionAssert.Contains(Describe(plan.Retired), "N.Sub=2");
        }

        [Test]
        public void ATaglessReAddLiftsOnlyTheRetirementItReclaims()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base") },
                NoEntries,
                NoEntries,
                new[] { Entry("N.Sub", "N.Base", 3), Entry("N.Sub", "N.Base", 8) }
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=3" }, Describe(plan.Assigned));
            CollectionAssert.AreEqual(new[] { "N.Sub=8" }, Describe(plan.Retired));
        }

        [Test]
        public void DemotingASubtypeToTheManifestKeepsTheNumberItWroteByHand()
        {
            // Removing an explicit number must recover its recorded tag instead of assigning a new one.
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 40) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            WProtoSubtypeTagPlan demoted = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base") },
                NoEntries,
                recorded.Assigned,
                recorded.Retired
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=40" }, Describe(demoted.Assigned));
            Assert.IsEmpty(demoted.Retired);
            Assert.IsEmpty(
                demoted.FreshlyAssigned,
                "40 came from the record, so nothing was invented"
            );
        }

        [Test]
        public void RenumberingAnExplicitDeclarationRetiresTheNumberItLeft()
        {
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 5) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            WProtoSubtypeTagPlan renumbered = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 6) },
                NoEntries,
                recorded.Assigned,
                recorded.Retired
            );

            CollectionAssert.AreEqual(new[] { "N.Sub=6" }, Describe(renumbered.Assigned));
            CollectionAssert.AreEqual(new[] { "N.Sub=5" }, Describe(renumbered.Retired));
        }

        [Test]
        public void AnExplicitDeclarationIsNotRetiredByAnUnattendedPassThatCannotSeeIt()
        {
            // Partial surveys cannot retire explicit tags hidden behind player-only defines.
            WProtoSubtypeTagPlan recorded = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Hidden", "N.Base", 1) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            WProtoSubtypeTagPlan unattended = WProtoSubtypeTagPlan.Create(
                new WProtoSubtypeTagPlan.Declaration[0],
                NoEntries,
                recorded.Assigned,
                recorded.Retired,
                WProtoSubtypeTagDiscovery.Partial
            );

            CollectionAssert.AreEqual(new[] { "N.Hidden=1" }, Describe(unattended.Assigned));
            Assert.IsEmpty(unattended.Retired);
        }

        [Test]
        public void TheGeneratorRefusesAnExplicitSubtypeClaimingARetiredNumber()
        {
            // Live-claim collision checks cannot detect reuse of retired tags.
            Diagnostic match = Run(
                    Fixture(
                        "[assembly: WProtoRetiredSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 7)]",
                        "[WProtoSubtype(typeof(Base), 7)]"
                    )
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO040");

            StringAssert.Contains("Consumer.Deleted", match.GetMessage());
            StringAssert.Contains("retired", match.GetMessage());
        }

        [Test]
        public void TheGeneratorRefusesAnIncludeClaimingARetiredNumber()
        {
            Diagnostic match = Run(
                    "[assembly: WProtoRetiredSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 7)]"
                        + "\n[WProtoContract] [WProtoInclude(7, typeof(Sub))] public partial class Base { [WProtoMember(1)] public int A; }"
                        + "\n[WProtoContract] public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                )
                .Single(diagnostic => diagnostic.Id == "WPROTO013");

            StringAssert.Contains("Consumer.Deleted", match.GetMessage());
            StringAssert.Contains("retired", match.GetMessage());
        }

        [Test]
        public void TheGeneratorLetsARetiredTypeReclaimItsOwnNumber()
        {
            Assert.IsEmpty(
                Describe(
                    Run(
                        Fixture(
                            "[assembly: WProtoRetiredSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 7)]",
                            "[WProtoSubtype(typeof(Base), 7)]"
                        )
                    )
                )
            );
        }

        [Test]
        public void AnOrphanedManifestEntryReadFromARealAssemblyIsRetiredRatherThanDropped()
        {
            /*
             * String-keyed records can preserve deleted types; typeof records cannot compile once their type
             * disappears.
             */
            List<WProtoSubtypeTagPlan.Entry> committed = WProtoSubtypeTagManifestFile.ReadAssigned(
                typeof(ManifestFormRoot).Assembly
            );

            Assert.IsTrue(
                committed.Any(entry =>
                    entry.SubTypeName.EndsWith("ManifestFormOrphaned", StringComparison.Ordinal)
                ),
                "the orphaned entry has to survive reading, or nothing downstream can retire it"
            );
            Assert.IsNull(
                Type.GetType(
                    "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned",
                    false
                ),
                "the fixture is only meaningful while the type really is gone"
            );

            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                LiveManifestFormDeclarations(),
                NoEntries,
                committed,
                WProtoSubtypeTagManifestFile.ReadRetired(typeof(ManifestFormRoot).Assembly)
            );

            CollectionAssert.Contains(
                Describe(plan.Retired),
                "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned=103"
            );
            CollectionAssert.DoesNotContain(
                plan.Assigned.Select(entry => entry.Tag).ToArray(),
                103,
                "103 belonged to a deleted subtype and may never be handed out again"
            );
            Assert.IsEmpty(plan.FreshlyAssigned, "nothing here is missing a number");
        }

        [Test]
        public void ANumberRetiredFromAnOrphanIsNeverHandedToTheNextSubtypeAdded()
        {
            List<WProtoSubtypeTagPlan.Declaration> declarations =
                new List<WProtoSubtypeTagPlan.Declaration>(LiveManifestFormDeclarations())
                {
                    Declare(
                        "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormLater",
                        "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormRoot"
                    ),
                };

            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                declarations,
                NoEntries,
                WProtoSubtypeTagManifestFile.ReadAssigned(typeof(ManifestFormRoot).Assembly),
                WProtoSubtypeTagManifestFile.ReadRetired(typeof(ManifestFormRoot).Assembly)
            );

            WProtoSubtypeTagPlan.Entry added = plan.Assigned.Single(entry =>
                entry.SubTypeName.EndsWith("ManifestFormLater", StringComparison.Ordinal)
            );

            Assert.AreNotEqual(103, added.Tag, "103 is retired");
            Assert.AreNotEqual(102, added.Tag, "102 was already retired by hand");
            CollectionAssert.AreEqual(
                new[] { added.SubTypeName + "=" + added.Tag },
                Describe(plan.FreshlyAssigned),
                "exactly one declaration was missing a number"
            );
        }

        [Test]
        public void ReAddingTheOrphanedTypeTakesBackTheNumberItHeld()
        {
            List<WProtoSubtypeTagPlan.Entry> committed = WProtoSubtypeTagManifestFile.ReadAssigned(
                typeof(ManifestFormRoot).Assembly
            );
            WProtoSubtypeTagPlan retiring = WProtoSubtypeTagPlan.Create(
                LiveManifestFormDeclarations(),
                NoEntries,
                committed,
                WProtoSubtypeTagManifestFile.ReadRetired(typeof(ManifestFormRoot).Assembly)
            );

            List<WProtoSubtypeTagPlan.Declaration> readded =
                new List<WProtoSubtypeTagPlan.Declaration>(LiveManifestFormDeclarations())
                {
                    Declare(
                        "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned",
                        "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormRoot"
                    ),
                };

            WProtoSubtypeTagPlan restored = WProtoSubtypeTagPlan.Create(
                readded,
                NoEntries,
                retiring.Assigned,
                retiring.Retired
            );

            CollectionAssert.Contains(
                Describe(restored.Assigned),
                "WallstopStudios.UnityHelpers.Proto.Generator.Tests.ManifestFormOrphaned=103"
            );
            Assert.IsEmpty(
                restored.FreshlyAssigned,
                "a restored number is not a fresh assignment, so it must not trigger the automatic pass"
            );
        }

        [Test]
        public void TheGeneratorAcceptsAManifestEntryNamingATypeThatNoLongerExists()
        {
            // Orphan records must compile or the repair tool cannot run after deletion.
            Assert.IsEmpty(
                Describe(
                    Run(
                        "[assembly: WProtoSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 7)]"
                            + Fixture(string.Empty, "[WProtoSubtype(typeof(Base), 5)]")
                    )
                )
            );
        }

        [Test]
        public void AnOrphanedEntrysNumberStaysClaimedInTheCompilationItself()
        {
            Diagnostic match = Run(
                    Fixture(
                        "[assembly: WProtoSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 7)]"
                            + "\n[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 7)]",
                        "[WProtoSubtype(typeof(Base))]"
                    )
                )
                .First(diagnostic => diagnostic.Id == "WPROTO042");

            StringAssert.Contains("cannot name two types", match.GetMessage());
        }

        [TestCaseSource(nameof(PredefinedAssemblyNames))]
        public void EachPredefinedAssemblysManifestCompilesIntoTheAssemblyItDescribes(string name)
        {
            /*
             * Predefined runtime and editor assemblies require separate manifest destinations to avoid
             * overwriting each other.
             */
            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(name);

            Assert.IsNotNull(directory, name);
            Assert.AreEqual(
                name,
                WProtoSubtypeTagManifestFile.PredefinedAssemblyForDirectory(directory),
                "a manifest in '" + directory + "' does not compile into '" + name + "'"
            );
        }

        [Test]
        public void NoTwoPredefinedAssembliesShareAManifestPath()
        {
            List<string> paths = new List<string>();
            foreach (string name in WProtoSubtypeTagManifestFile.PredefinedAssemblyNames())
            {
                paths.Add(
                    WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(name)
                        + "/"
                        + WProtoSubtypeTagManifestFile.FileName
                );
            }

            CollectionAssert.AllItemsAreUnique(paths);
            Assert.AreEqual(4, paths.Count);
        }

        [Test]
        public void AnAssemblyWithNoAsmdefAndNoPredefinedHomeIsRefusedRatherThanMisplaced()
        {
            Assert.IsNull(
                WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly("Some.Other.Assembly")
            );
            StringAssert.Contains(
                "Some.Other.Assembly",
                WProtoSubtypeTagManifestFile.DescribeUnplaceableAssembly("Some.Other.Assembly")
            );
            StringAssert.Contains(
                ".asmdef",
                WProtoSubtypeTagManifestFile.DescribeUnplaceableAssembly("Some.Other.Assembly")
            );
        }

        [Test]
        public void TheBuildGateRefusesOnlyForAssembliesThePlayerActuallyContains()
        {
            Dictionary<string, List<string>> unnumbered = new Dictionary<string, List<string>>(
                StringComparer.Ordinal
            )
            {
                {
                    "Game.Runtime",
                    new List<string> { "Melee on Weapon" }
                },
                {
                    "Game.Editor",
                    new List<string> { "EditorOnly on Weapon" }
                },
                {
                    "Game.Tests",
                    new List<string> { "Fixture on Weapon" }
                },
            };

            List<KeyValuePair<string, List<string>>> shipped =
                WProtoSubtypeTagManifestFile.ShippedUnnumbered(
                    unnumbered,
                    new HashSet<string>(StringComparer.Ordinal) { "Game.Runtime" }
                );

            Assert.AreEqual(1, shipped.Count, "Only the shipped assembly may fail a build.");
            Assert.AreEqual("Game.Runtime", shipped[0].Key);
        }

        [Test]
        public void TheBuildGateAllowsABuildWhoseUnnumberedDeclarationsAreAllEditorOnly()
        {
            Dictionary<string, List<string>> unnumbered = new Dictionary<string, List<string>>(
                StringComparer.Ordinal
            )
            {
                {
                    "Game.Editor",
                    new List<string> { "EditorOnly on Weapon" }
                },
            };

            Assert.IsEmpty(
                WProtoSubtypeTagManifestFile.ShippedUnnumbered(
                    unnumbered,
                    new HashSet<string>(StringComparer.Ordinal) { "Game.Runtime" }
                )
            );
        }

        [Test]
        public void TheBuildGateReportsAssembliesInAFixedOrder()
        {
            Dictionary<string, List<string>> unnumbered = new Dictionary<string, List<string>>(
                StringComparer.Ordinal
            )
            {
                {
                    "Zeta.Runtime",
                    new List<string> { "Z on Weapon" }
                },
                {
                    "Alpha.Runtime",
                    new List<string> { "A on Weapon" }
                },
            };

            List<KeyValuePair<string, List<string>>> shipped =
                WProtoSubtypeTagManifestFile.ShippedUnnumbered(
                    unnumbered,
                    new HashSet<string>(StringComparer.Ordinal) { "Zeta.Runtime", "Alpha.Runtime" }
                );

            Assert.AreEqual("Alpha.Runtime", shipped[0].Key);
            Assert.AreEqual("Zeta.Runtime", shipped[1].Key);
        }

        [Test]
        public void TheBuildGateToleratesNothingToDecide()
        {
            Assert.IsEmpty(WProtoSubtypeTagManifestFile.ShippedUnnumbered(null, null));
            Assert.IsEmpty(
                WProtoSubtypeTagManifestFile.ShippedUnnumbered(
                    new Dictionary<string, List<string>>(StringComparer.Ordinal),
                    new HashSet<string>(StringComparer.Ordinal)
                )
            );
        }

        [TestCase("Assembly-CSharp-firstpass", "Assets/Plugins")]
        [TestCase("Assembly-CSharp-Editor-firstpass", "Assets/Plugins")]
        public void AFirstpassManifestIsNotBlockedByAnAsmdefOutsideItsRoot(
            string assemblyName,
            string expectedFloor
        )
        {
            // Unity firstpass roots stop ancestor ownership lookup before an outer Assets asmdef.
            Assert.AreEqual(
                expectedFloor,
                WProtoSubtypeTagManifestFile.ClaimFloorForPredefinedAssembly(assemblyName)
            );

            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(
                assemblyName
            );

            Assert.IsNull(
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    directory,
                    AtOrAbove(directory, expectedFloor, "Assets/Game.asmdef")
                ),
                "An .asmdef at Assets does not take " + directory
            );
        }

        [TestCase("Assembly-CSharp-firstpass")]
        [TestCase("Assembly-CSharp-Editor-firstpass")]
        public void AFirstpassManifestIsStillBlockedByAnAsmdefInsideItsRoot(string assemblyName)
        {
            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(
                assemblyName
            );
            string floor = WProtoSubtypeTagManifestFile.ClaimFloorForPredefinedAssembly(
                assemblyName
            );

            Assert.AreEqual(
                "Assets/Plugins/Vendor.asmdef",
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    directory,
                    AtOrAbove(directory, floor, "Assets/Plugins/Vendor.asmdef")
                )
            );
        }

        [TestCase("Assembly-CSharp", "Assets")]
        [TestCase("Assembly-CSharp-Editor", "Assets")]
        public void TheNonFirstpassAssembliesStillWalkUpToAssets(
            string assemblyName,
            string expectedFloor
        )
        {
            Assert.AreEqual(
                expectedFloor,
                WProtoSubtypeTagManifestFile.ClaimFloorForPredefinedAssembly(assemblyName)
            );

            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(
                assemblyName
            );

            Assert.AreEqual(
                "Assets/Game.asmdef",
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    directory,
                    AtOrAbove(directory, expectedFloor, "Assets/Game.asmdef")
                ),
                "An .asmdef at Assets does take " + directory
            );
        }

        /// <summary>
        /// The asmdefs the assigner's floored ancestor walk would find, given every candidate.
        /// </summary>
        private static List<string> AtOrAbove(string directory, string floor, params string[] all)
        {
            List<string> visible = new List<string>();
            string current = directory;
            while (!string.IsNullOrEmpty(current))
            {
                foreach (string path in all)
                {
                    int separator = path.LastIndexOf('/');
                    string owner = separator < 0 ? string.Empty : path.Substring(0, separator);
                    if (string.Equals(owner, current, StringComparison.Ordinal))
                    {
                        visible.Add(path);
                    }
                }

                if (string.Equals(current, floor, StringComparison.Ordinal))
                {
                    break;
                }

                int cut = current.LastIndexOf('/');
                current = cut < 0 ? null : current.Substring(0, cut);
            }

            return visible;
        }

        [TestCaseSource(nameof(ClaimedPredefinedDirectories))]
        public void APredefinedDirectoryAnAsmdefClaimsIsRefusedAndTheAsmdefIsNamed(
            string assemblyName,
            string assemblyDefinition
        )
        {
            /*
             * Writing beneath another assembly's definition would leave the intended assembly permanently
             * unnumbered.
             */
            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(
                assemblyName
            );

            Assert.AreEqual(
                assemblyDefinition,
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    directory,
                    new[] { assemblyDefinition }
                ),
                "'" + assemblyDefinition + "' takes any file written into '" + directory + "'"
            );

            string message = WProtoSubtypeTagManifestFile.DescribeClaimedPredefinedDirectory(
                assemblyName,
                directory,
                assemblyDefinition
            );

            StringAssert.Contains(assemblyDefinition, message);
            StringAssert.Contains(assemblyName, message);
            StringAssert.Contains(directory, message);
        }

        [TestCaseSource(nameof(PredefinedAssemblyNames))]
        public void AnUnclaimedPredefinedDirectoryResolvesExactlyAsItAlwaysDid(string name)
        {
            // Only ancestors can claim a manifest; sibling and descendant definitions must not block it.
            string directory = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(name);

            Assert.IsNull(
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    directory,
                    new[]
                    {
                        "Assets/Game/Game.asmdef",
                        "Assets/Editor/Deep/Nested/Deep.Editor.asmdef",
                        "Assets/Plugins/Vendor/Vendor.asmdef",
                        "Packages/com.vendor.thing/Runtime/Vendor.asmdef",
                    }
                ),
                name
            );
        }

        [Test]
        public void TheNearestAsmdefIsTheOneNamed()
        {
            Assert.AreEqual(
                "Assets/Editor/Game.Editor.asmdef",
                WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                    "Assets/Editor",
                    new[] { "Assets/Everything.asmdef", "Assets/Editor/Game.Editor.asmdef" }
                )
            );
        }

        [Test]
        public void ASecondPassOverAnAlreadyWrittenManifestWritesNothing()
        {
            // Writes trigger reloads, so unchanged manifests must settle without rewriting.
            WProtoSubtypeTagPlan.Declaration[] declarations =
            {
                Declare("N.Alpha", "N.Base"),
                Declare("N.Beta", "N.Base"),
            };

            WProtoSubtypeTagPlan first = WProtoSubtypeTagPlan.Create(
                declarations,
                NoEntries,
                NoEntries,
                NoEntries
            );
            string written = first.Render("Some.Assembly");

            Assert.IsTrue(
                WProtoSubtypeTagManifestFile.NeedsWrite(null, written, first.IsEmpty),
                "the first pass has to write"
            );
            Assert.IsNotEmpty(first.FreshlyAssigned, "the first pass invents both numbers");

            WProtoSubtypeTagPlan second = WProtoSubtypeTagPlan.Create(
                declarations,
                NoEntries,
                first.Assigned,
                first.Retired
            );

            Assert.IsFalse(
                WProtoSubtypeTagManifestFile.NeedsWrite(
                    written,
                    second.Render("Some.Assembly"),
                    second.IsEmpty
                ),
                "a reload with nothing to do must write no file"
            );
            Assert.IsEmpty(
                second.FreshlyAssigned,
                "and must not report anything as newly numbered, which is what triggers the pass"
            );
        }

        [Test]
        public void AdoptingThePackageWritesNoManifestIntoAProjectThatInventsNoNumbers()
        {
            /*
             * Only fresh automatic assignments may create a manifest; explicit-only history requires the
             * deliberate menu action.
             */
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 4) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            Assert.IsEmpty(
                plan.FreshlyAssigned,
                "nothing was invented, so the automatic pass has no reason to write"
            );
            CollectionAssert.AreEqual(
                new[] { "N.Sub=4" },
                Describe(plan.Assigned),
                "and an explicit run records the number, which is what makes a later deletion visible"
            );
        }

        [Test]
        public void RewordingTheHeaderCommentDoesNotMakeEveryManifestStale()
        {
            // Header wording changes must not stale manifests, including those in read-only packages.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base") },
                NoEntries,
                NoEntries,
                NoEntries
            );
            string rendered = plan.Render("A");

            Assert.IsFalse(
                WProtoSubtypeTagManifestFile.NeedsWrite(
                    "// something an older version of the tool wrote\r\n\r\n"
                        + StripComments(rendered),
                    rendered,
                    plan.IsEmpty
                )
            );
        }

        [Test]
        public void WritingASubtypeTheChainDoesNotDeclareThrowsRatherThanWritingItAsItsBase()
        {
            /*
             * Until an unnumbered subtype gains a branch, serialization must refuse rather than erase its
             * runtime identity.
             */
            ManifestFormRoot value = new ManifestFormUndeclared { Id = 1, UndeclaredOnly = 2 };
            IWProtoFormatter<ManifestFormRoot> formatter =
                WProtoFormatterProvider.Get<ManifestFormRoot>();

            InvalidOperationException measured = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(value)
            );
            StringAssert.Contains("ManifestFormUndeclared", measured.Message);
            StringAssert.Contains("ManifestFormRoot", measured.Message);

            Assert.Throws<InvalidOperationException>(() =>
            {
                byte[] buffer = new byte[64];
                WProtoWriter writer = new WProtoWriter(buffer);
                formatter.Write(ref writer, value);
            });
        }

        private static IEnumerable<TestCaseData> EquivalentHierarchyValues()
        {
            yield return Pair("the root itself", new SubtypeFormRoot(), new ManifestFormRoot());
            yield return Pair(
                "the root with members",
                new SubtypeFormRoot { Id = 1, Label = "a" },
                new ManifestFormRoot { Id = 1, Label = "a" }
            );
            yield return Pair(
                "an all-default leaf subtype",
                new SubtypeFormAlpha(),
                new ManifestFormAlpha()
            );
            yield return Pair(
                "a leaf subtype with members",
                new SubtypeFormAlpha
                {
                    Id = -1,
                    Label = string.Empty,
                    AlphaOnly = int.MinValue,
                    AlphaText = "é中",
                },
                new ManifestFormAlpha
                {
                    Id = -1,
                    Label = string.Empty,
                    AlphaOnly = int.MinValue,
                    AlphaText = "é中",
                }
            );
            yield return Pair(
                "a middle subtype",
                new SubtypeFormBeta { Id = 2, BetaOnly = -0.5 },
                new ManifestFormBeta { Id = 2, BetaOnly = -0.5 }
            );
            yield return Pair(
                "an all-default deep subtype",
                new SubtypeFormGamma(),
                new ManifestFormGamma()
            );
            yield return Pair(
                "a deep subtype with members",
                new SubtypeFormGamma
                {
                    Id = 3,
                    Label = "g",
                    BetaOnly = double.MaxValue,
                    GammaOnly = true,
                },
                new ManifestFormGamma
                {
                    Id = 3,
                    Label = "g",
                    BetaOnly = double.MaxValue,
                    GammaOnly = true,
                }
            );
        }

        private static IEnumerable<int> ResolvedTagCases()
        {
            yield return 3;
            yield return 100;
            yield return 20000;
            yield return 536870911;
        }

        private static IEnumerable<TestCaseData> CorruptManifestCases()
        {
            yield return new TestCaseData(
                "two numbers for one pair",
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 6)]",
                "already has a number"
            ).SetName("{m} - two numbers for one pair");
            yield return new TestCaseData(
                "one number for two subtypes",
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoSubtypeTag(\"Consumer.Other\", typeof(Consumer.Base), 5)]",
                "cannot name two types"
            ).SetName("{m} - one number for two subtypes");
            yield return new TestCaseData(
                "a retired number handed out again",
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoRetiredSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 5)]",
                "is retired"
            ).SetName("{m} - a retired number handed out again");
            yield return new TestCaseData(
                "a number outside the protobuf range",
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 19500)]",
                "reserved 19000-19999"
            ).SetName("{m} - a number outside the protobuf range");
            yield return new TestCaseData(
                "a retired entry naming no type",
                "[assembly: WProtoSubtypeTag(\"Consumer.Sub\", typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoRetiredSubtypeTag(\"\", typeof(Consumer.Base), 6)]",
                "names no type"
            ).SetName("{m} - a retired entry naming no type");
        }

        private const string ExtraSubtype =
            "[WProtoContract] [WProtoSubtype(typeof(Base), 400)] public partial class Other : Base { [WProtoMember(1)] public int O; }";

        private static readonly WProtoSubtypeTagPlan.Entry[] NoEntries =
            new WProtoSubtypeTagPlan.Entry[0];

        /// <summary>
        /// Predefined assemblies whose only directory an <c>.asmdef</c> has taken.
        /// </summary>
        /// <returns>The assembly and the <c>.asmdef</c> that claims its directory.</returns>
        /// <remarks>
        /// Both shapes, because they fail identically and are found differently: an
        /// <c>.asmdef</c> sitting IN the directory, and one sitting above it.
        /// </remarks>
        private static IEnumerable<TestCaseData> ClaimedPredefinedDirectories()
        {
            yield return new TestCaseData(
                "Assembly-CSharp-Editor",
                "Assets/Editor/Game.Editor.asmdef"
            ).SetName("{m} - in the directory itself");
            yield return new TestCaseData(
                "Assembly-CSharp-Editor",
                "Assets/Everything.asmdef"
            ).SetName("{m} - in an ancestor");
            yield return new TestCaseData("Assembly-CSharp", "Assets/Everything.asmdef").SetName(
                "{m} - Assets itself"
            );
            yield return new TestCaseData(
                "Assembly-CSharp-Editor-firstpass",
                "Assets/Plugins/Vendor.asmdef"
            ).SetName("{m} - two directories up");
        }

        private static IEnumerable<string> PredefinedAssemblyNames()
        {
            return WProtoSubtypeTagManifestFile.PredefinedAssemblyNames();
        }

        /// <summary>
        /// The ManifestForm hierarchy exactly as this assembly declares it.
        /// </summary>
        /// <returns>One numberless declaration per live subtype.</returns>
        /// <remarks>
        /// Written out rather than reflected so the fixture states what it means: these three are
        /// alive and ManifestFormOrphaned is not, which is the difference the retirement tests turn
        /// on.
        /// </remarks>
        private static List<WProtoSubtypeTagPlan.Declaration> LiveManifestFormDeclarations()
        {
            const string Prefix = "WallstopStudios.UnityHelpers.Proto.Generator.Tests.";
            return new List<WProtoSubtypeTagPlan.Declaration>
            {
                Declare(Prefix + "ManifestFormAlpha", Prefix + "ManifestFormRoot"),
                Declare(Prefix + "ManifestFormBeta", Prefix + "ManifestFormRoot"),
                Declare(Prefix + "ManifestFormGamma", Prefix + "ManifestFormBeta"),
            };
        }

        private static DiagnosticSeverity Severity(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics.Single(diagnostic => diagnostic.Id == "WPROTO041").Severity;
        }

        private static string StripComments(string rendered)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string line in rendered.Replace("\r\n", "\n").Split('\n'))
            {
                if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    builder.Append(line);
                    builder.Append("\r\n");
                }
            }

            return builder.ToString();
        }

        private static TestCaseData Pair(
            string label,
            SubtypeFormRoot written,
            ManifestFormRoot fromManifest
        )
        {
            return new TestCaseData(label, written, fromManifest).SetName("{m} - " + label);
        }

        private static WProtoSubtypeTagPlan.Declaration Declare(string subType, string baseType)
        {
            return new WProtoSubtypeTagPlan.Declaration(subType, baseType, false, 0);
        }

        private static WProtoSubtypeTagPlan.Declaration Declare(
            string subType,
            string baseType,
            int tag
        )
        {
            return new WProtoSubtypeTagPlan.Declaration(subType, baseType, true, tag);
        }

        private static WProtoSubtypeTagPlan.Entry Entry(string subType, string baseType, int tag)
        {
            return new WProtoSubtypeTagPlan.Entry(subType, baseType, tag);
        }

        private static string[] Describe(IReadOnlyList<WProtoSubtypeTagPlan.Entry> entries)
        {
            return entries.Select(entry => entry.SubTypeName + "=" + entry.Tag).ToArray();
        }

        private static string[] Describe(ImmutableArray<Diagnostic> diagnostics)
        {
            return diagnostics.Select(entry => entry.Id + " " + entry.GetMessage()).ToArray();
        }

        /// <summary>
        /// Turns a rendered manifest back into fixture lines the harness can hoist.
        /// </summary>
        /// <param name="rendered">What the assignment tool would write.</param>
        /// <returns>The attribute lines, one per line, with comments dropped.</returns>
        private static string ManifestBody(string rendered)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string line in rendered.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(trimmed);
                if (trimmed.EndsWith(")]", StringComparison.Ordinal))
                {
                    builder.Append('\n');
                }
            }

            return builder.ToString();
        }

        private static string Fixture(
            string assemblyAttributes,
            string subtypeAttribute,
            string extra = null
        )
        {
            return assemblyAttributes
                + "\n[WProtoContract] public partial class Base { [WProtoMember(1)] public int A; }"
                + "\n[WProtoContract] "
                + subtypeAttribute
                + " public partial class Sub : Base { [WProtoMember(1)] public int B; }"
                + (extra == null ? string.Empty : "\n" + extra);
        }

        private static string FormatterFor(string contract, Compilation generated)
        {
            return generated
                .SyntaxTrees.Single(tree =>
                    tree.FilePath.EndsWith(
                        "global__Consumer_" + contract + ".WProtoFormatter.g.cs",
                        StringComparison.Ordinal
                    )
                )
                .ToString();
        }

        private static ImmutableArray<Diagnostic> Run(string body)
        {
            return Run(body, out Compilation _);
        }

        /// <summary>
        /// Drives the shipped generator over a synthetic consumer compilation.
        /// </summary>
        /// <param name="body">The fixture, with any [assembly:] lines at the top.</param>
        /// <param name="generated">The compilation including everything the generator emitted.</param>
        /// <returns>What the generator reported.</returns>
        /// <remarks>
        /// The [assembly:] lines are hoisted above the namespace, because that is the only place C#
        /// accepts them and the manifest is nothing but assembly attributes.
        /// </remarks>
        private static ImmutableArray<Diagnostic> Run(
            string body,
            out Compilation generated,
            params string[] preprocessorSymbols
        )
        {
            List<string> assemblyAttributes = new List<string>();
            List<string> rest = new List<string>();
            foreach (string line in body.Split('\n'))
            {
                (
                    line.TrimStart().StartsWith("[assembly:", StringComparison.Ordinal)
                        ? assemblyAttributes
                        : rest
                ).Add(line);
            }

            string source =
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                + string.Join("\n", assemblyAttributes)
                + "\nnamespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
                + string.Join("\n", rest)
                + " }";

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            /*
             * Driver and syntax-tree symbols must agree because the generator reads the same defines that
             * control source inclusion.
             */
            CSharpParseOptions parseOptions = new CSharpParseOptions(
                preprocessorSymbols: preprocessorSymbols ?? new string[0]
            );

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            CSharpGeneratorDriver
                .Create(new ISourceGenerator[] { new WProtoGenerator() }, null, parseOptions, null)
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out Compilation updated,
                    out ImmutableArray<Diagnostic> diagnostics
                );

            generated = updated;
            return diagnostics;
        }

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
        }

        private static T RoundTrip<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
