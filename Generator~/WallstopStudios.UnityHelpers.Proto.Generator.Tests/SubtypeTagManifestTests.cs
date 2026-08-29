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
            // The claim the whole feature rests on. [WProtoSubtype(typeof(Base), 100)] and
            // [WProtoSubtype(typeof(Base))] with a manifest entry of 100 are the same declaration
            // said two ways, so a payload cannot tell which the author typed.
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
            // A holder predicts the chain's length before writing it, so a difference in what the
            // two forms measure shows up here as a different prefix rather than as a shifted body.
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
            // Not a guess. A number invented here would depend on which types this compilation
            // happened to contain, which is precisely the wire instability the manifest exists to
            // remove -- and an unnumbered subtype has no wire form at all, so a player carrying one
            // throws on its first save.
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
            // The deadlock this severity exists to break, measured in editor 6000.4.6f1 before it
            // was: an error fails the assembly, the new type is then in no assembly at all, and the
            // assignment tool -- which discovers declarations through TypeCache -- cannot see the
            // very type it has to number. The only escape was to write the number by hand, which is
            // what the numberless form removed. As a warning the assembly compiles, the type
            // exists, and the automatic pass numbers it.
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
            // Both directions from one fixture, so the assertion is about the symbol rather than
            // about two tests that happen to disagree. UNITY_EDITOR is what Unity defines for every
            // assembly it compiles in the editor and for none it compiles into a player -- measured
            // in 6000.4.6f1 through CompilationPipeline.GetAssemblies(...).defines.
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
            // The refusal has to be the whole story. A base whose dispatch chain still named the
            // withheld subtype's formatter would put a CS error inside generated code the developer
            // cannot open, beside the WPROTO041 that actually says what to do.
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
            // The manifest is keyed by the PAIR. An entry naming a different base is a number in a
            // different field-number space, and honouring it would put a subtype under a number its
            // own base never reserved.
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
            // Everything already published writes its own number, and an author who wants to pin
            // one has to keep being able to. The attribute is the override, not the manifest.
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
            // One field-number space per base, whichever end each declaration was written from and
            // whichever of them stated its own number.
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
            // The case a freed number would break: Removed held 1, was deleted, and Added arrives
            // afterwards. Every payload written before the deletion still says 1, so Added has to
            // take 2 -- and a manifest that gave it 1 would read those payloads back as an Added.
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
            // Add, remove, re-add: the sequence the ask names, and the reason retirement records
            // the type's name rather than only the number.
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
            // The hazard the automatic pass has to survive: N.Hidden is real, and this editor
            // cannot see it -- it sits behind #if !UNITY_EDITOR or a platform define, so TypeCache
            // reports nothing and the survey is indistinguishable from a deletion. N.Fresh is what
            // makes that matter: an unrelated declaration needing a number is what commits the
            // whole plan, so the retirement must not be IN the plan. One fixture, two modes, and
            // the mode is the only difference between them.
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
            // The other half of keeping it: the file already says what the conservative plan says,
            // so the pass settles instead of rewriting a manifest it cannot fully account for.
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
            // Remove-then-re-add is the case the whole design exists for, so the conservative mode
            // is not allowed to cost it: a retired entry whose tag-less declaration reappears comes
            // back as an assignment in both modes.
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
            // Conservative is not the same as inert. The pass exists to number a new subtype, and
            // a mode that stopped doing that would be a silent no-op rather than a guard.
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
            // The renumbering guard. Nothing here is using 1, 2 or 3, and the tool still leaves the
            // subtype on 40: a number already written is the contract for every payload since.
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
            // A subtype's include shares the base's field-number space with the base's members, so
            // a number picked without consulting them would be WPROTO040 rather than an assignment.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base"), Declare("N.Pinned", "N.Base", 3) },
                new[] { Entry("Id", "N.Base", 1), Entry("N.Included", "N.Base", 2) },
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
            // TypeCache's order is not a property of the source, so an assignment that depended on
            // it would give two machines two different wires from one commit.
            WProtoSubtypeTagPlan.Declaration[] forward =
            {
                Declare("N.Alpha", "N.Base"),
                Declare("N.Beta", "N.Base"),
                Declare("N.Gamma", "N.Base"),
            };
            // Enumerable.Reverse by its full name, not forward.Reverse(): on an array receiver
            // that also binds MemoryExtensions.Reverse(Span<T>), which returns void. Which one
            // wins depends on the restored package graph, so it compiled here and failed CI.
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
            // The two halves have to agree on spelling as well as on numbers: the tool writes
            // `typeof(...)` and the generator reads a `typeof(...)`, and nothing else checks that.
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
            // Moving a number out of the manifest and into the attribute changes nothing on the
            // wire, so retiring it would forbid the very declaration now holding it.
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 4) },
                NoEntries,
                new[] { Entry("N.Sub", "N.Base", 4) },
                NoEntries
            );

            Assert.IsEmpty(plan.Assigned);
            Assert.IsEmpty(plan.Retired);
        }

        [Test]
        public void AnOrphanedManifestEntryReadFromARealAssemblyIsRetiredRatherThanDropped()
        {
            // Finding 1, driven through the code path that reads a manifest whose subtype no longer
            // exists. This assembly really carries
            //   [assembly: WProtoSubtypeTag("...ManifestFormOrphaned", typeof(ManifestFormRoot), 103)]
            // and ManifestFormOrphaned really does not exist. The old typeof-keyed shape could not
            // even express this fixture: a typeof for a deleted type does not compile, and the only
            // cheap repair -- deleting the line -- silently freed 103.
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
            // The consequence the retirement exists for, end to end from the committed manifest:
            // add a subtype after the deletion and it must not be given the dead type's number.
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
            // Remove-then-re-add, again from the real committed manifest. The entry was retired on
            // the run that noticed the deletion; declaring the type again has to restore 103 rather
            // than assign it something new, or every payload saved before the deletion is lost.
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
            // The half of Finding 1 that only a real compile can show. This assembly compiles with
            // an entry naming nothing, so the state a deletion leaves behind is not itself a build
            // error -- if it were, the tool could never be run to repair it.
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
            // Holding the number is the point, so the generator has to refuse a live subtype that
            // claims it -- not merely leave the orphan alone.
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
            // Finding 2. Every assembly without an .asmdef used to be written to Assets/, so
            // Assembly-CSharp and Assembly-CSharp-Editor shared one file that compiles into the
            // runtime assembly only: the editor half never saw its own entries, and whichever ran
            // last overwrote the other's numbers.
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
            // A path chosen "close enough" is a file that compiles somewhere else and does nothing,
            // which is worse than a failure because it looks like success.
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
            // Both directions, because they fail in opposite ways. Too narrow ships a player whose
            // first save throws; too broad refuses every build over a declaration in an editor-only
            // or test assembly the player never contains. The gate itself cannot be constructed
            // outside a Unity build, so this is its decision, tested where it can be.
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
            // The direction that, if wrong, breaks every player build rather than letting one
            // through: nothing shipped is unnumbered, so the gate must find nothing to report.
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
            // Unity compiles Assets/Plugins (and the Standard Assets roots) into the firstpass
            // assemblies in an earlier phase, so an .asmdef at Assets does NOT take them. Walking
            // past the firstpass root reported that outer .asmdef as the claimant and refused a
            // manifest whose scripts still compile into the firstpass assembly -- refusing to write
            // a file that was needed, which is the opposite mistake to the one the walk exists for.
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
            // The control for the case above: narrowing the walk must not stop it seeing an .asmdef
            // that genuinely does take the directory.
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
            // A predefined assembly's path is only its path while NO .asmdef sits at or above it.
            // Unity binds a script to its nearest ancestor .asmdef, so a manifest written under one
            // compiles into that assembly instead: the entries are never read, WPROTO041 keeps
            // firing, and every later pass reads the file as already current, which is what makes
            // the mistake unrecoverable rather than merely wrong.
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
            // The control. Only an ancestor can take the file, so an .asmdef in a sibling or in a
            // subdirectory changes nothing -- a check that refused those would break every project
            // that has an .asmdef anywhere.
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
            // Unity binds to the nearest ancestor, so the message has to name the file the
            // developer would actually have to move.
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
            // The automatic pass runs after every assembly reload and a write triggers another
            // reload, so a run that always wrote would never settle. Nothing is written once the
            // file already says what assignment produces.
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
        public void AnAssemblyWhoseSubtypesAllWriteTheirOwnNumbersGetsNoManifestAtAll()
        {
            WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                new[] { Declare("N.Sub", "N.Base", 4) },
                NoEntries,
                NoEntries,
                NoEntries
            );

            Assert.IsTrue(plan.IsEmpty);
            Assert.IsFalse(
                WProtoSubtypeTagManifestFile.NeedsWrite(null, plan.Render("A"), plan.IsEmpty),
                "adopting the package must not put a file into a project that never uses the form"
            );
        }

        [Test]
        public void RewordingTheHeaderCommentDoesNotMakeEveryManifestStale()
        {
            // The header is documentation. A package upgrade that reworded it would otherwise mark
            // every manifest in the project stale, including the ones inside read-only packages
            // where the rewrite cannot be performed at all.
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
            // The warning window: between adding a numberless subtype and the manifest gaining its
            // entry, the base's chain has no branch for it. Refusing is the only safe answer -- a
            // silent fall-through would write the value as its base, and no later fix could tell
            // those payloads from ones that really were the base. protobuf-net raises "Unexpected
            // sub-type" on the same value.
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
            // One byte, two bytes, and the top of the space, so nothing about the emitted tag
            // depends on how many varint bytes it costs.
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

            // The symbols are the point of several assertions rather than harness detail: the
            // generator reads UNITY_EDITOR out of exactly this set, which is the same set #if is
            // evaluated against, and both the driver and the tree have to carry it.
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
