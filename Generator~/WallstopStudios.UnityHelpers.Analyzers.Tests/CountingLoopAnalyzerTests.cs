// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using NUnit.Framework;

    /// <summary>
    /// Pins which counting loops can become <c>foreach</c> and, more importantly, which cannot.
    /// </summary>
    /// <remarks>
    /// The negative cases are the rule. <c>foreach</c> over <c>IReadOnlyList&lt;T&gt;</c> boxes an
    /// enumerator, so a counting loop there is the correct shape and reporting it would push people
    /// toward the allocation this family exists to prevent.
    /// </remarks>
    [TestFixture]
    public sealed class CountingLoopAnalyzerTests
    {
        private const string DiagnosticId = "WUH013";

        private const string StructDeclaration =
            "public struct Point { public int X; public void Bump() { X = X + 1; } }";

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, ReportDiagnostic.Warn);
        }

        private static ImmutableArray<Diagnostic> Analyze(string body, ReportDiagnostic reportedAs)
        {
            string source = "namespace Consumer { public static class Subject { " + body + " } }";

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
                new[]
                {
                    CSharpSyntaxTree.ParseText(
                        source,
                        new CSharpParseOptions(LanguageVersion.CSharp9)
                    ),
                },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary
                ).WithSpecificDiagnosticOptions(
                    ImmutableDictionary<string, ReportDiagnostic>.Empty.Add(
                        DiagnosticId,
                        reportedAs
                    )
                )
            );

            ImmutableArray<Diagnostic> compileErrors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
            Assert.IsEmpty(
                compileErrors.Select(diagnostic => diagnostic.ToString()).ToArray(),
                "The fixture must compile"
            );

            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new CountingLoopAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("string[] rows", "rows.Length")]
        [TestCase("System.Collections.Generic.List<string> rows", "rows.Count")]
        public void ACountingWalkOfAnAllocationFreeSequenceIsReported(
            string declaration,
            string bound
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                "public static void Walk("
                    + declaration
                    + ") { for (int index = 0; index < "
                    + bound
                    + "; ++index) { System.Console.WriteLine(rows[index]); } }"
            );

            Assert.AreEqual(1, reported.Length);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, reported[0].Severity);
        }

        [TestCase(
            "struct",
            "public",
            "",
            "public bool MoveNext() { return false; } public string Current { get { return null; } }",
            "",
            1
        )]
        [TestCase(
            "class",
            "public",
            "",
            "public bool MoveNext() { return false; } public string Current { get { return null; } }",
            "",
            0
        )]
        [TestCase(
            "struct",
            "private",
            "",
            "public bool MoveNext() { return false; } public string Current { get { return null; } }",
            "",
            0
        )]
        [TestCase(
            "struct",
            "public",
            "int unused",
            "public bool MoveNext() { return false; } public string Current { get { return null; } }",
            "",
            0
        )]
        [TestCase(
            "struct",
            "public",
            "",
            "public int MoveNext() { return 0; } public string Current { get { return null; } }",
            "",
            0
        )]
        [TestCase(
            "struct",
            "public",
            "",
            "public bool MoveNext() { return false; } private string Current { get { return null; } }",
            "",
            0
        )]
        [TestCase(
            "struct",
            "public",
            "",
            "public bool MoveNext() { return false; } public string Current { get { return null; } }",
            "rows.Change();",
            0
        )]
        public void ConcreteEnumerationUsesTheCompilerPattern(
            string enumeratorKind,
            string accessibility,
            string parameters,
            string enumeratorMembers,
            string body,
            int expected
        )
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                "public sealed class Rows { public int Count { get { return 0; } } "
                    + "public string this[int index] { get { return null; } } public void Change() { } "
                    + accessibility
                    + " Enumerator GetEnumerator("
                    + parameters
                    + ") { return new Enumerator(); } "
                    + "public "
                    + enumeratorKind
                    + " Enumerator { "
                    + enumeratorMembers
                    + " } } "
                    + "public static void Walk(Rows rows) { for (int index = 0; index < rows.Count; ++index) { "
                    + body
                    + " System.Console.WriteLine(rows[index]); } }"
            );

            Assert.AreEqual(expected, reported.Length);
            if (0 < expected)
            {
                Assert.AreEqual(DiagnosticId, reported[0].Id);
            }
        }

        [TestCase("int count = rows.Length;", "", "", 1)]
        [TestCase("int count = rows.Length;", "", "count--;", 0)]
        [TestCase("int count = rows.Length;", "", "Change(ref count);", 0)]
        [TestCase("int count = rows.Length;", "", "rows = new string[0];", 0)]
        [TestCase("int count = rows.Length;", "rows = new string[0];", "", 0)]
        [TestCase("int count = rows.Length - 1;", "", "", 0)]
        [TestCase("int count = 2;", "", "", 0)]
        [TestCase("int count = rows.Length, other = 1;", "", "", 0)]
        public void CachedBoundsRequireAnUnchangedFullSequenceSnapshot(
            string declaration,
            string between,
            string mutation,
            int expected
        )
        {
            Assert.AreEqual(
                expected,
                Analyze(
                    "private static void Change(ref int value) { value--; } "
                        + "public static void Walk(string[] rows) { "
                        + declaration
                        + between
                        + " for (int index = 0; index < count; ++index) { "
                        + mutation
                        + " System.Console.WriteLine(rows[index]); } }"
                ).Length
            );
        }

        [Test]
        public void DictionaryIntegerKeysAreNotSequentialElements()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.Dictionary<int, string> rows) { "
                        + "for (int index = 0; index < rows.Count; ++index) { System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        [TestCase("", 1)]
        [TestCase(
            "public new System.Collections.Generic.IEnumerator<string> GetEnumerator() { return null; }",
            0
        )]
        public void InheritedEnumerationRespectsHiddenMembers(string derivedMember, int expected)
        {
            Assert.AreEqual(
                expected,
                Analyze(
                    "public class Rows { public int Count { get { return 0; } } "
                        + "public string this[int index] { get { return null; } } "
                        + "public Enumerator GetEnumerator() { return new Enumerator(); } "
                        + "public struct Enumerator { public bool MoveNext() { return false; } public string Current { get { return null; } } } } "
                        + "public sealed class DerivedRows : Rows { "
                        + derivedMember
                        + " } "
                        + "public static void Walk(DerivedRows rows) { for (int index = 0; index < rows.Count; ++index) { System.Console.WriteLine(rows[index]); } }"
                ).Length
            );
        }

        [TestCase("System.Collections.Generic.List<string>", 1)]
        [TestCase("System.Collections.Generic.IReadOnlyList<string>", 0)]
        public void CachedCountRetainsTheConcreteTypeRequirement(string type, int expected)
        {
            Assert.AreEqual(
                expected,
                Analyze(
                    "public static void Walk("
                        + type
                        + " rows) { int count = rows.Count; "
                        + "for (int index = 0; index < count; ++index) { System.Console.WriteLine(rows[index]); } }"
                ).Length
            );
        }

        [TestCase("var alias = rows; alias.Clear();")]
        [TestCase(
            "System.Collections.Generic.List<string> alias = null; alias = rows; alias.Clear();"
        )]
        [TestCase("var aliases = new[] { rows }; aliases[0].Clear();")]
        [TestCase("var alias = (rows, 1); alias.Item1.Clear();")]
        [TestCase(
            "var alias = rows ?? new System.Collections.Generic.List<string>(); alias.Clear();"
        )]
        public void SequenceAliasesInsideTheBodyCanChangeEnumeration(string mutation)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<string> rows) { int count = rows.Count; "
                        + "for (int index = 0; index < count; ++index) { "
                        + mutation
                        + " System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        [Test]
        public void MultidimensionalArraysDoNotWalkOneIndexedDimension()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[,] rows) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(rows[index, 0]); } }"
                )
            );
        }

        [Test]
        public void MultipleLoopInitializersAreNotASingleForwardWalk()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { for (int index = 0, count = rows.Length; index < rows.Length; ++index) { System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        /// <summary>
        /// Two instances walked in step are two sequences, not one. Comparing the field symbol
        /// alone made <c>this.rows[i]</c> and <c>other.rows[i]</c> look identical, so every
        /// hand-written equality method over a serialized list was reported -- and <c>foreach</c>
        /// there loses the parallel index and changes what the method computes.
        /// </summary>
        [Test]
        public void AWalkOfTwoInstancesInStepIsTheCorrectShape()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Holder { public System.Collections.Generic.List<string> rows; "
                        + "public bool Same(Holder other) { if (rows.Count != other.rows.Count) { return false; } "
                        + "for (int index = 0; index < rows.Count; ++index) { if (rows[index] != other.rows[index]) { return false; } } "
                        + "return true; } }"
                ),
                "a loop that indexes a second instance's same-named field is not a single-sequence walk"
            );
        }

        /// <summary>
        /// The red half of the receiver check: the same shape, walking only its own field, is still
        /// reported. Without this the fix above could be silently over-broad.
        /// </summary>
        [Test]
        public void AWalkOfOneInstancesOwnFieldIsStillReported()
        {
            ImmutableArray<Diagnostic> reported = Analyze(
                "public sealed class Holder { public System.Collections.Generic.List<string> rows; "
                    + "public void Walk() { for (int index = 0; index < rows.Count; ++index) "
                    + "{ System.Console.WriteLine(rows[index]); } } }"
            );

            Assert.AreEqual(1, reported.Length);
            Assert.AreEqual(DiagnosticId, reported[0].Id);
        }

        [TestCase("System.Collections.Generic.IReadOnlyList<string> rows")]
        [TestCase("System.Collections.Generic.IList<string> rows")]
        public void ACountingWalkOfAnInterfaceIsTheCorrectShape(string declaration)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk("
                        + declaration
                        + ") { for (int index = 0; index < rows.Count; ++index) { System.Console.WriteLine(rows[index]); } }"
                ),
                "foreach over an interface boxes its enumerator, so the counting loop is right."
            );
        }

        [Test]
        public void ALoopThatUsesTheIndexItselfIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(index + \": \" + rows[index]); } }"
                )
            );
        }

        [Test]
        public void ALoopThatIndexesASecondSequenceIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows, string[] others) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(rows[index] + others[index]); } }"
                )
            );
        }

        [TestCase("for (int index = 1; index < rows.Length; ++index)")]
        [TestCase("for (int index = rows.Length - 1; 0 <= index; --index)")]
        [TestCase("for (int index = 0; index < rows.Length; index += 2)")]
        public void AWalkThatIsNotTheOrdinaryForwardOneIsNotReported(string header)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { "
                        + header
                        + " { System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        [Test]
        public void AForeachIsNeverReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { foreach (string row in rows) { System.Console.WriteLine(row); } }"
                )
            );
        }

        [TestCase("rows[index] = \"replaced\";")]
        [TestCase("rows[index] += \"suffix\";")]
        [TestCase("Replace(ref rows[index]);")]
        public void ALoopThatWritesThroughTheIndexIsNotReported(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Replace(ref string slot) { } public static void Walk(string[] rows) { for (int index = 0; index < rows.Length; ++index) { "
                        + body
                        + " } }"
                ),
                "foreach cannot assign back into the sequence, so advising it would drop the write."
            );
        }

        [TestCase("points[index].X = 1;")]
        [TestCase("points[index].X += 1;")]
        [TestCase("points[index].X++;")]
        [TestCase("points[index].Bump();")]
        public void AStoreThroughAStructElementsMemberIsNotReported(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    StructDeclaration
                        + " public static void Walk(Point[] points) { for (int index = 0; index < points.Length; ++index) { "
                        + body
                        + " } }"
                ),
                "The member is part of the slot, and foreach hands out a copy."
            );
        }

        [Test]
        public void ReadingAStructElementsMemberIsStillReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    StructDeclaration
                        + " public static void Walk(Point[] points) { for (int index = 0; index < points.Length; ++index) { System.Console.WriteLine(points[index].X); } }"
                ).Length,
                "A read of a struct member is exactly what foreach is for."
            );
        }

        [Test]
        public void AStoreThroughAClassElementsMemberIsStillReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public sealed class Node { public int X; } public static void Walk(Node[] nodes) { for (int index = 0; index < nodes.Length; ++index) { nodes[index].X = 1; } }"
                ).Length,
                "A class element is written through its reference, which foreach does perfectly well."
            );
        }

        [TestCase("rows[0] = rows[index];")]
        [TestCase("int writeIndex = 0; rows[writeIndex++] = rows[index];")]
        [TestCase(
            "System.Console.WriteLine(rows[index]); rows = new System.Collections.Generic.List<string>();"
        )]
        [TestCase("System.Console.WriteLine(rows[index]); rows.Add(\"new\");")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.AddRange(new string[0]);")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.Clear();")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.Insert(0, \"new\");")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.InsertRange(0, new string[0]);")]
        [TestCase("rows.Remove(rows[index]);")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.RemoveAt(0);")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.RemoveAll(value => value == null);")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.RemoveRange(0, 1);")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.Reverse();")]
        [TestCase("System.Console.WriteLine(rows[index]); rows.Sort();")]
        public void ALoopThatMutatesItsListCannotUseAnEnumerator(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<string> rows) { for (int index = 0; index < rows.Count; ++index) { "
                        + body
                        + " } }"
                )
            );
        }

        [TestCase("Mutate(rows);")]
        [TestCase("rows.MutateExtension();")]
        [TestCase("rows.ForEach(value => HiddenMutation());")]
        public void ASequenceExposedToUnknownCodeIsNotReported(string call)
        {
            Assert.IsEmpty(
                Analyze(
                    "private static void Mutate(System.Collections.Generic.List<string> values) { values.Clear(); } "
                        + "private static void MutateExtension(this System.Collections.Generic.List<string> values) { values.Clear(); } "
                        + "private static void HiddenMutation() { } "
                        + "public static void Walk(System.Collections.Generic.List<string> rows) { for (int index = 0; index < rows.Count; ++index) { "
                        + call
                        + " System.Console.WriteLine(rows[index]); } }"
                )
            );
        }

        [TestCase("holder = replacement;")]
        [TestCase("Replace(ref holder, replacement);")]
        [TestCase("var alias = holder; alias.rows = replacement.rows;")]
        [TestCase("Expose(holder, replacement);")]
        public void ReplacingTheSequenceReceiverIsNotReported(string mutation)
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Holder { public string[] rows; } "
                        + "private static void Replace(ref Holder value, Holder replacement) { value = replacement; } "
                        + "private static void Expose(Holder value, Holder replacement) { value.rows = replacement.rows; } "
                        + "public static void Walk(Holder holder, Holder replacement) { for (int index = 0; index < holder.rows.Length; ++index) { "
                        + mutation
                        + " System.Console.WriteLine(holder.rows[index]); } }"
                )
            );
        }

        [TestCase("other")]
        [TestCase("owner")]
        public void NestedFieldReceiversAreNotAssumedToBeTheSameSequence(string bodyReceiver)
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Holder { public string[] items; } "
                        + "public sealed class Owner { public Holder holder; } "
                        + "public static void Walk(Owner owner, Owner other) { for (int index = 0; index < owner.holder.items.Length; ++index) { System.Console.WriteLine("
                        + bodyReceiver
                        + ".holder.items[index]); } }"
                )
            );
        }

        [TestCase(
            "int other = 0; System.Console.WriteLine(rows[index]); (rows[0], other) = (2, 3);"
        )]
        [TestCase("int other = 0; (rows[index], other) = (2, 3);")]
        public void TupleTargetWritesAreNotReported(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; ++index) { "
                        + body
                        + " } }"
                )
            );
        }

        [Test]
        public void RefElementAliasesAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] values) { for (int index = 0; index < values.Length; ++index) { ref int slot = ref values[index]; slot++; } }"
                )
            );
        }

        [Test]
        public void ALoopMayReadItsListWhileMutatingADifferentList()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<string> rows, System.Collections.Generic.List<string> output) { for (int index = 0; index < rows.Count; ++index) { if (rows.Contains(rows[index])) output.Add(rows[index]); } }"
                ).Length
            );
        }

        [Test]
        public void AListWrittenThroughItsIndexerIsNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<string> rows) { for (int index = 0; index < rows.Count; ++index) { rows[index] = \"replaced\"; } }"
                )
            );
        }

        [Test]
        public void TheRuleIsOffUntilAConsumerAsksForIt()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(string[] rows) { for (int index = 0; index < rows.Length; ++index) { System.Console.WriteLine(rows[index]); } }",
                    ReportDiagnostic.Default
                ),
                "Consumer opt-in remains independent of package gate enforcement."
            );
        }
    }
}
