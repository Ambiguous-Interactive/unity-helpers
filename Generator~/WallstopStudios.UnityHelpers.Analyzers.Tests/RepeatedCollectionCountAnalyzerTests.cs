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

    /// <summary>Pins repeated Count observations and boundaries that require fresh reads.</summary>
    [TestFixture]
    public sealed class RepeatedCollectionCountAnalyzerTests
    {
        private const string DiagnosticId = "WUH021";

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            string declarations = "",
            ReportDiagnostic reportedAs = ReportDiagnostic.Warn,
            bool allowCompilationErrors = false
        )
        {
            System.Reflection.Assembly binderAssembly =
                typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly;
            Assert.IsTrue(binderAssembly != null);
            string source =
                "using System; using System.Collections.Generic; using System.Collections.Concurrent; "
                + "namespace Consumer { public static class Subject { "
                + body
                + " } } "
                + declarations;
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
            string[] compileErrors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.ToString())
                .ToArray();
            if (allowCompilationErrors)
            {
                Assert.IsNotEmpty(
                    compileErrors,
                    "The malformed fixture must exercise invalid code"
                );
            }
            else
            {
                Assert.IsEmpty(compileErrors, "The fixture must compile");
            }
            return compilation
                .WithAnalyzers(
                    ImmutableArray.Create<DiagnosticAnalyzer>(new RepeatedCollectionCountAnalyzer())
                )
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("List<int>")]
        [TestCase("Queue<int>")]
        [TestCase("HashSet<int>")]
        [TestCase("ConcurrentQueue<int>")]
        [TestCase("ICollection<int>")]
        [TestCase("IReadOnlyCollection<int>")]
        [TestCase("IList<int>")]
        [TestCase("IReadOnlyList<int>")]
        [TestCase("System.Collections.ICollection")]
        public void AGuardAndBranchReadReportTheSecondObservation(string type)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "public static int Read("
                    + type
                    + " values) { if (values.Count > 0) { return values.Count; } return 0; }"
            );
            Assert.That(diagnostics.Length, Is.EqualTo(1));
            Assert.That(diagnostics[0].Id, Is.EqualTo(DiagnosticId));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        }

        [Test]
        public void TheOriginalCacheNullGuardAndPoolRentAreReported()
        {
            Assert.That(
                Analyze(
                    "private static ConcurrentQueue<int> pending; public static void Read() { if (pending != null && 0 < pending.Count) { int[] notifications = System.Buffers.ArrayPool<int>.Shared.Rent(pending.Count); } }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("return values.Count + values.Count;")]
        [TestCase("int first = values.Count; return values.Count;")]
        [TestCase("if (values != null && values.Count > 0) { return values.Count; } return 0;")]
        [TestCase("if (values.Count > 0 && values.Count < 10) { return 1; } return 0;")]
        [TestCase("return values.Count > 0 ? values.Count : 0;")]
        [TestCase("return Consume(values.Count, values.Count);")]
        [TestCase("return ((ICollection<int>)values).Count + ((ICollection<int>)values).Count;")]
        public void UninterruptedRepeatedObservationsAreReported(string statements)
        {
            Assert.That(
                Analyze(
                    "private static int Consume(int first, int second) { return first + second; } public static int Read(List<int> values) { "
                        + statements
                        + " }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void AConstrainedTypeParameterIsReported()
        {
            Assert.That(
                Analyze(
                    "public static int Read<T>(T values) where T : ICollection<int> { return values.Count + values.Count; }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("int first = values.Count; values.Add(1); return values.Count;")]
        [TestCase(
            "int first = values.Count; IList<int> alias = values; alias.Clear(); return values.Count;"
        )]
        [TestCase("int first = values.Count; Unknown(); return values.Count;")]
        [TestCase("int first = values.Count; int element = values[0]; return values.Count;")]
        [TestCase("int first = values.Count; values = new List<int>(); return values.Count;")]
        [TestCase("if (flag) { return values.Count; } else { return values.Count; }")]
        [TestCase("if (flag || values.Count > 0) { return values.Count; } return 0;")]
        [TestCase(
            "int total = 0; for (int i = 0; i < values.Count; i++) { total += values.Count; } return total;"
        )]
        [TestCase("int first = values.Count; lock (values) { return values.Count; }")]
        [TestCase("int first = values.Count; try { return values.Count; } finally { Unknown(); }")]
        [TestCase("int first = values.Count; if (flag) { Unknown(); } return values.Count;")]
        [TestCase("int first = values.Count; int other = otherValues.Count; return values.Count;")]
        public void MutationDispatchAndControlFlowBoundariesRequireFreshReads(string statements)
        {
            Assert.IsEmpty(
                Analyze(
                    "private static void Unknown() { } public static int Read(IList<int> values, ICollection<int> otherValues, bool flag) { "
                        + statements
                        + " }"
                )
            );
        }

        [Test]
        public void AwaitAndYieldRequireFreshReads()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static async System.Threading.Tasks.Task<int> Read(ICollection<int> values) { int first = values.Count; await System.Threading.Tasks.Task.Delay(1); return values.Count; } public static IEnumerable<int> Iterate(ICollection<int> values) { yield return values.Count; yield return values.Count; }"
                )
            );
        }

        [Test]
        public void UnrelatedCountNamesAndDifferentReceiversAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Custom values) { return values.Count + values.Count; } public static int Different(ICollection<int> first, ICollection<int> second) { return first.Count + second.Count; }",
                    "public sealed class Custom { public int Count { get; set; } }"
                )
            );
        }

        [Test]
        public void CallbackReceiverExpressionsAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "private static ICollection<int> Get() { return null; } public static int Read() { return Get().Count + Get().Count; }"
                )
            );
        }

        [TestCase("return nameof(values.Count) + nameof(values.Count);")]
        [TestCase("int first = values.Count; return nameof(values.Count);")]
        public void NameOfDoesNotReadCount(string statements)
        {
            Assert.IsEmpty(
                Analyze("public static string Read(ICollection<int> values) { " + statements + " }")
            );
        }

        [Test]
        public void NameOfDoesNotInterruptRealObservations()
        {
            Assert.That(
                Analyze(
                    "public static int Read(ICollection<int> values) { int first = values.Count; string name = nameof(values.Count); return values.Count; }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void AClassConstrainedTypeParameterIsReported()
        {
            Assert.That(
                Analyze(
                    "public static int Read<T>(T values) where T : List<int> { return values.Count + values.Count; }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("&&", "&")]
        [TestCase("||", "|")]
        public void OverloadedShortCircuitOperatorsMayMutateBeforeTheRightOperand(
            string conditional,
            string binary
        )
        {
            Assert.IsEmpty(
                Analyze(
                    "public static Gate Read(ICollection<int> values, Gate gate) { int first = values.Count; return gate "
                        + conditional
                        + " new Gate(values.Count); }",
                    "public sealed class Gate { public static ICollection<int> Values; public Gate(int ignored) { } public static bool operator true(Gate value) { Values.Clear(); return true; } public static bool operator false(Gate value) { Values.Clear(); return false; } public static Gate operator "
                        + binary
                        + "(Gate left, Gate right) { return left; } }"
                )
            );
        }

        [Test]
        public void AConstructorRunsBeforeItsObjectInitializer()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values) { int first = values.Count; Holder holder = new Holder(values) { Size = values.Count }; return holder.Size; }",
                    "public sealed class Holder { public int Size; public Holder(ICollection<int> values) { values.Clear(); } }"
                )
            );
        }

        [TestCase("Holder holder = new Holder(argument); return values.Count;")]
        [TestCase("Holder holder = new Holder(argument) { Size = values.Count }; return 0;")]
        public void ADynamicConstructorIsAnExecutionBoundary(string statements)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values, dynamic argument) { int first = values.Count; "
                        + statements
                        + " }",
                    "public sealed class Holder { public static ICollection<int> Values; public int Size; public Holder(object ignored) { Values.Clear(); } }"
                )
            );
        }

        [Test]
        public void ASortedSetViewCountMayInvokeAUserComparer()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values, SortedSet<int> view) { int first = values.Count; int other = view.Count; return values.Count; }"
                )
            );
        }

        [TestCase("Func<int> read = () => values.Count + values.Count;")]
        [TestCase("int Read() { return values.Count + values.Count; }")]
        public void NestedFunctionBodiesHaveIndependentObservationState(string declaration)
        {
            Assert.That(
                Analyze(
                    "public static void Outer(ICollection<int> values) { " + declaration + " }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("Func<int> read = () => values.Count;")]
        [TestCase("int Read() { return values.Count; }")]
        public void AOuterObservationDoesNotReachANestedFunction(string declaration)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Outer(ICollection<int> values) { int first = values.Count; "
                        + declaration
                        + " }"
                )
            );
        }

        [Test]
        public void DistinctExplicitCountImplementationsAreNotOneObservation()
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "public static int Read(Dual values) { return ((ICollection<int>)values).Count + ((IReadOnlyCollection<int>)values).Count; }",
                @"public sealed class Dual : System.Collections.Generic.List<int>, System.Collections.Generic.ICollection<int>, System.Collections.Generic.IReadOnlyCollection<int> {
                    int System.Collections.Generic.ICollection<int>.Count => 1;
                    int System.Collections.Generic.IReadOnlyCollection<int>.Count => 2;
                }"
            );
            Assert.IsEmpty(diagnostics);
        }

        [Test]
        public void AnUnknownInterfaceCastDoesNotProveTheSameGetter()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(ICollection<int> values) { return values.Count + ((IReadOnlyCollection<int>)values).Count; }"
                )
            );
        }

        [Test]
        public void AnUnsealedReceiverMayReimplementItsInterfaceCount()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values) { return values.Count + ((ICollection<int>)values).Count; }",
                    "public sealed class Dual : System.Collections.Generic.List<int>, System.Collections.Generic.ICollection<int> { int System.Collections.Generic.ICollection<int>.Count => 2; }"
                )
            );
        }

        [Test]
        public void ASealedReceiverCanProveItsInterfaceImplementation()
        {
            Assert.That(
                Analyze(
                    "public static int Read(Exact values) { return values.Count + ((ICollection<int>)values).Count; }",
                    "public sealed class Exact : System.Collections.Generic.List<int> { }"
                ).Length,
                Is.EqualTo(1)
            );
        }

        [TestCase("dynamic ignored = gate + 1;")]
        [TestCase("dynamic ignored = -gate;")]
        [TestCase("int ignored = (int)gate;")]
        public void DynamicOperatorsMayExecuteUserCallbacks(string statement)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values, dynamic gate) { int first = values.Count; "
                        + statement
                        + " return values.Count; }"
                )
            );
        }

        [TestCase("&&", "&")]
        [TestCase("||", "|")]
        public void DynamicShortCircuitOperatorsMayRunBeforeTheRightOperand(
            string conditional,
            string binary
        )
        {
            Assert.IsEmpty(
                Analyze(
                    "public static object Read(List<int> values, dynamic gate) { int first = values.Count; return gate "
                        + conditional
                        + " new Gate(values.Count); }",
                    "public sealed class Gate { public Gate(int size) { } public static bool operator true(Gate value) => true; public static bool operator false(Gate value) => false; public static Gate operator "
                        + binary
                        + "(Gate first, Gate second) => first; }"
                )
            );
        }

        [Test]
        public void ACoalesceConversionMayExecuteUserCode()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(ICollection<int> values, Left? left, Right fallback) { int first = values.Count; Right converted = left ?? fallback; return values.Count; }",
                    "public struct Left { public static implicit operator Right(Left value) => default; } public struct Right { }"
                )
            );
        }

        [Test]
        public void ARecordCloneRunsBeforeItsInitializer()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values, Holder holder) { int first = values.Count; Holder copy = holder with { Size = values.Count }; return values.Count; }",
                    "public record Holder { public int Size { get; init; } protected Holder(Holder other) { } }"
                )
            );
        }

        [Test]
        public void DynamicNegationInAConditionMayExecuteUserCode()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values, dynamic gate) { int first = values.Count; if (!gate) { return values.Count; } return 0; }"
                )
            );
        }

        [Test]
        public void AStaticFieldAccessMayRunATypeInitializer()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(List<int> values) { int first = values.Count; int ignored = Holder.Value; return values.Count; }",
                    "public static class Holder { public static int Value; static Holder() { Value = 1; } }"
                )
            );
        }

        [Test]
        public void ASourceTypeCannotClaimTheFrameworkCallbackFreeGetter()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Queue<int> values, List<int> shadow) { int first = values.Count; int ignored = shadow.Count; return values.Count; }",
                    @"namespace System.Collections.Generic { public sealed class List<T> : IReadOnlyCollection<T> {
                    public int Count => 1;
                    public IEnumerator<T> GetEnumerator() => null;
                    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => null;
                } }"
                )
            );
        }

        [Test]
        public void ASourceArrayPoolSharedGetterMayExecuteUserCode()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(Queue<int> values) { int first = values.Count; object ignored = System.Buffers.ArrayPool<int>.Shared; return values.Count; }",
                    "namespace System.Buffers { public static class ArrayPool<T> { public static object Shared => new object(); } }"
                )
            );
        }

        [Test]
        public void ASourceInterfaceWithAFrameworkNameIsNotTheFrameworkContract()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read(System.Collections.ICollection values) { return values.Count + values.Count; }",
                    "namespace System.Collections { public interface ICollection { int Count { get; } } }"
                )
            );
        }

        [Test]
        public void MalformedCircularConstraintsDoNotCrashAnalysis()
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "public static int Read<T, U>(T values) where T : U where U : T, List<int> { return values.Count + values.Count; }",
                allowCompilationErrors: true
            );
            Assert.IsFalse(
                diagnostics.Any(diagnostic =>
                    string.Equals(diagnostic.Id, "AD0001", StringComparison.Ordinal)
                )
            );
        }

        [Test]
        public void ATypeParameterConstructorRunsBeforeItsInitializer()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Read<T>(ICollection<int> values) where T : IHolder, new() { int first = values.Count; T holder = new T { Size = values.Count }; return 0; }",
                    "public interface IHolder { int Size { get; set; } }"
                )
            );
        }

        [Test]
        public void TheRuleIsOptInSuppressibleAndNeverAboveWarning()
        {
            DiagnosticDescriptor descriptor =
                new RepeatedCollectionCountAnalyzer().SupportedDiagnostics.Single();
            Assert.IsFalse(descriptor.IsEnabledByDefault);
            Assert.That(descriptor.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Warning));
            const string body =
                "public static int Read(ICollection<int> values) { return values.Count + values.Count; }";
            Assert.IsEmpty(Analyze(body, reportedAs: ReportDiagnostic.Default));
            Assert.IsEmpty(Analyze(body, reportedAs: ReportDiagnostic.Suppress));
            string message = Analyze(body)[0].GetMessage();
            StringAssert.Contains("one snapshot is intended", message);
            StringAssert.Contains("custom code", message);
            StringAssert.Contains("suppression", message);
        }
    }
}
