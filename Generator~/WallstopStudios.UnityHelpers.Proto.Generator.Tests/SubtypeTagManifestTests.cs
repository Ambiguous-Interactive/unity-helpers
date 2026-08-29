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
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), "
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
        public void ATagLessDeclarationWithNoManifestEntryIsAnError()
        {
            // Not a guess and not a warning. A number invented here would depend on which types
            // this compilation happened to contain, which is precisely the wire instability the
            // manifest exists to remove.
            Diagnostic match = Run(Fixture(string.Empty, "[WProtoSubtype(typeof(Base))]"))
                .Single(diagnostic => diagnostic.Id == "WPROTO041");

            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            StringAssert.Contains("Consumer.Sub", match.GetMessage());
            StringAssert.Contains("Consumer.Base", match.GetMessage());
            StringAssert.Contains("Assign WallstopProto Subtype Tags", match.GetMessage());
            StringAssert.Contains("WProtoSubtypeTag", match.GetMessage());
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
                            "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Other), 7)]"
                                + "\n[assembly: WProtoSubtypeTag(typeof(Consumer.Other), typeof(Consumer.Base), 9)]",
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
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 7)]",
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
                        "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 5)]",
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
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 6)]",
                "already has a number"
            ).SetName("{m} - two numbers for one pair");
            yield return new TestCaseData(
                "one number for two subtypes",
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoSubtypeTag(typeof(Consumer.Other), typeof(Consumer.Base), 5)]",
                "cannot name two types"
            ).SetName("{m} - one number for two subtypes");
            yield return new TestCaseData(
                "a retired number handed out again",
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoRetiredSubtypeTag(\"Consumer.Deleted\", typeof(Consumer.Base), 5)]",
                "is retired"
            ).SetName("{m} - a retired number handed out again");
            yield return new TestCaseData(
                "a number outside the protobuf range",
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 19500)]",
                "reserved 19000-19999"
            ).SetName("{m} - a number outside the protobuf range");
            yield return new TestCaseData(
                "a retired entry naming no type",
                "[assembly: WProtoSubtypeTag(typeof(Consumer.Sub), typeof(Consumer.Base), 5)]"
                    + "\n[assembly: WProtoRetiredSubtypeTag(\"\", typeof(Consumer.Base), 6)]",
                "names no type"
            ).SetName("{m} - a retired entry naming no type");
        }

        private const string ExtraSubtype =
            "[WProtoContract] [WProtoSubtype(typeof(Base), 400)] public partial class Other : Base { [WProtoMember(1)] public int O; }";

        private static readonly WProtoSubtypeTagPlan.Entry[] NoEntries =
            new WProtoSubtypeTagPlan.Entry[0];

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
        private static ImmutableArray<Diagnostic> Run(string body, out Compilation generated)
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

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
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
