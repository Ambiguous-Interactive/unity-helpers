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
        public void ReplacingTheSequenceReceiverIsNotReported(string mutation)
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Holder { public string[] rows; } "
                        + "private static void Replace(ref Holder value, Holder replacement) { value = replacement; } "
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
    }
}
