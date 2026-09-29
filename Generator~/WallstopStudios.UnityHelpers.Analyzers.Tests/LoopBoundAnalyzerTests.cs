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
    /// Pins stable bounds and live collection traversal.
    /// </summary>
    [TestFixture]
    public sealed class LoopBoundAnalyzerTests
    {
        private const string DiagnosticId = "WUH019";

        private static ImmutableArray<Diagnostic> Analyze(string body)
        {
            return Analyze(body, ReportDiagnostic.Warn);
        }

        private static ImmutableArray<Diagnostic> Analyze(
            string body,
            ReportDiagnostic reportedAs,
            string additionalSource = ""
        )
        {
            string source =
                additionalSource
                + "namespace Consumer { public static class Subject { "
                + body
                + " } }";
            System.Reflection.Assembly binderAssembly =
                typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly;
            Assert.IsNotNull(binderAssembly);
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
                .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new LoopBoundAnalyzer()))
                .GetAnalyzerDiagnosticsAsync()
                .GetAwaiter()
                .GetResult();
        }

        [TestCase("int[] rows", "rows.Length")]
        [TestCase("string rows", "rows.Length")]
        [TestCase("System.Span<int> rows", "rows.Length")]
        [TestCase("System.ReadOnlySpan<int> rows", "rows.Length")]
        [TestCase("System.Collections.Generic.List<int> rows", "rows.Count")]
        public void StableBoundsAreReported(string parameter, string bound)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(
                "public static int Sum("
                    + parameter
                    + ") { int total = 0; for (int index = 0; index < "
                    + bound
                    + "; index++) { total += index; } return total; }"
            );
            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual(DiagnosticId, diagnostics[0].Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        }

        [TestCase("rows.Add(index);")]
        [TestCase("rows.RemoveAt(index);")]
        [TestCase("rows.Clear();")]
        [TestCase("rows = new System.Collections.Generic.List<int>();")]
        [TestCase("Mutate(rows);")]
        [TestCase("System.Collections.Generic.List<int> alias = rows; alias.Add(index);")]
        public void LiveCollectionBoundsAreRetained(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "private static void Mutate(System.Collections.Generic.List<int> rows) { rows.Clear(); } public static void Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; index++) { "
                        + body
                        + " } }"
                )
            );
        }

        [TestCase("rows = new int[0];")]
        [TestCase("Replace(ref rows);")]
        public void ReplacingAnArrayRetainsItsLiveLength(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "private static void Replace(ref int[] rows) { rows = new int[0]; } public static void Walk(int[] rows) { for (int index = 0; index < rows.Length; index++) { "
                        + body
                        + " } }"
                )
            );
        }

        [Test]
        public void ArrayElementWritesDoNotChangeTheirLength()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk(int[] rows) { for (int index = 0; index < rows.Length; index++) { rows[index] = index; } }"
                ).Length
            );
        }

        [TestCase("rows != null && index < rows.Length")]
        [TestCase("rows == null || index < rows.Length")]
        public void GuardedBoundsCannotBeHoistedBeforeTheirGuards(string condition)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { for (int index = 0; "
                        + condition
                        + "; index++) { System.Console.WriteLine(index); } }"
                )
            );
        }

        [Test]
        public void CallbackDrivenCollectionsRetainLiveCounts()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows, System.Action callback) { for (int index = 0; index < rows.Count; index++) { callback(); } }"
                )
            );
        }

        [Test]
        public void IteratorsRetainLiveCountsBetweenMoveNextCalls()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static System.Collections.Generic.IEnumerable<int> Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; index++) { yield return rows[index]; } }"
                )
            );
        }

        [Test]
        public void ALoopInitializerMayReplaceTheSequence()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { for (rows = new int[0]; rows.Length > 0;) { } }"
                )
            );
        }

        [TestCase("rows = replacement;")]
        [TestCase("holder = replacementHolder;")]
        public void ReceiverReplacementRetainsLiveBounds(string body)
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Holder { public int[] rows; } public static void Walk(Holder holder, Holder replacementHolder, int[] replacement) { int[] rows = holder.rows; for (int index = 0; index < "
                        + (
                            body.StartsWith("holder", StringComparison.Ordinal)
                                ? "holder.rows.Length"
                                : "rows.Length"
                        )
                        + "; index++) { "
                        + body
                        + " } }"
                )
            );
        }

        [Test]
        public void UnknownCountGettersAreNotAssumedStable()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Counter { public int Count { get { return System.Environment.TickCount; } } } public static void Walk(Counter rows) { for (int index = 0; index < rows.Count; index++) { } }"
                )
            );
        }

        [Test]
        public void MutationInLoopIncrementRetainsLiveCounts()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; rows.RemoveAt(index)) { } }"
                )
            );
        }

        [Test]
        public void UnknownCallbacksCanMutateAnAliasedCollection()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Callback { public System.Collections.Generic.List<int> rows; public void Run() { rows.Clear(); } } public static void Walk(System.Collections.Generic.List<int> rows, Callback callback) { for (int index = 0; index < rows.Count; index++) { callback.Run(); } }"
                )
            );
        }

        [Test]
        public void FieldReceiversMayChangeInUnknownCalls()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int[] rows; private static void Replace() { rows = new int[0]; } public static void Walk() { for (int index = 0; index < rows.Length; index++) { Replace(); } }"
                )
            );
        }

        [Test]
        public void AStableFieldWithoutUnknownCallsIsReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static int[] rows; public static void Walk() { for (int index = 0; index < rows.Length; index++) { rows[index] = index; } }"
                ).Length
            );
        }

        [Test]
        public void BoundsInsideArithmeticAreReported()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk(int[] rows) { for (int index = 0; index < rows.Length - 1; index++) { rows[index] = index; } }"
                ).Length
            );
        }

        [Test]
        public void AliasesEstablishedBeforeTraversalRetainLiveBounds()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { System.Collections.Generic.List<int> alias = rows; for (int index = 0; index < rows.Count; index++) { alias.Add(index); } }"
                )
            );
        }

        [Test]
        public void AwaitCanChangeTheCollectionBetweenIterations()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static async System.Threading.Tasks.Task Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; index++) { await System.Threading.Tasks.Task.Yield(); } }"
                )
            );
        }

        [Test]
        public void ConcurrentCollectionsNeedTheirLiveCounts()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Concurrent.ConcurrentQueue<int> rows) { for (int index = 0; index < rows.Count; index++) { } }"
                )
            );
        }

        [Test]
        public void RefAliasesMayReplaceAnArray()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { ref int[] alias = ref rows; for (int index = 0; index < rows.Length; index++) { alias = new int[0]; } }"
                )
            );
        }

        [TestCase("rows.GetLength(0)")]
        [TestCase("rows.GetLongLength(1)")]
        public void ArrayDimensionsAreStableBounds(string bound)
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk(int[,] rows) { for (int index = 0; index < "
                        + bound
                        + "; index++) { rows[index, 0] = index; } }"
                ).Length
            );
        }

        [Test]
        public void ChangingDimensionArgumentsCannotBeHoisted()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[,] rows) { for (int index = 0; index < rows.GetLength(index); index++) { } }"
                )
            );
        }

        [Test]
        public void LocalFunctionCallbacksMayMutateAnAliasedCollection()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { void Append() { rows.Add(1); } for (int index = 0; index < rows.Count; index++) { Append(); } }"
                )
            );
        }

        [TestCase("System.Action change = () => rows = new int[1];", "change();")]
        [TestCase("void Change() { rows = new int[1]; }", "Change();")]
        public void CapturedArrayReceiversCanBeReplaced(string callback, string call)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { "
                        + callback
                        + " for (int index = 0; index < rows.Length; index++) { "
                        + call
                        + " } }"
                )
            );
        }

        [Test]
        public void VirtualSystemCallbacksCanMutateAnAliasedCollection()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.IList<int> rows, System.Collections.Generic.IComparer<int> comparer) { for (int index = 0; index < rows.Count; index++) { comparer.Compare(index, index); } }"
                )
            );
        }

        [TestCase("UnityEngine", "Transform", "childCount")]
        [TestCase("UnityEditor", "SerializedProperty", "arraySize")]
        public void StableUnitySizeGettersAreReported(
            string typeNamespace,
            string typeName,
            string sizeName
        )
        {
            string declaration =
                "namespace "
                + typeNamespace
                + " { public sealed class "
                + typeName
                + " { public int "
                + sizeName
                + " { get; set; } } }";
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk("
                        + typeNamespace
                        + "."
                        + typeName
                        + " rows) { for (int index = 0; index < rows."
                        + sizeName
                        + "; index++) { } }",
                    ReportDiagnostic.Warn,
                    declaration
                ).Length
            );
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk("
                        + typeNamespace
                        + "."
                        + typeName
                        + " rows) { for (int index = 0; index < rows."
                        + sizeName
                        + "; index++) { rows."
                        + sizeName
                        + "--; } }",
                    ReportDiagnostic.Warn,
                    declaration
                )
            );
        }

        [Test]
        public void AssignmentEstablishedAliasesRetainLiveBounds()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { System.Collections.Generic.List<int> alias; alias = rows; for (int index = 0; index < rows.Count; index++) { alias.Add(index); } }"
                )
            );
        }

        [Test]
        public void UnknownPropertyGettersCanMutateCollections()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Mutator { public System.Collections.Generic.List<int> rows; public int Value { get { rows.Clear(); return 1; } } } public static int Walk(System.Collections.Generic.List<int> rows, Mutator mutator) { int sum = 0; for (int index = 0; index < rows.Count; index++) { sum += mutator.Value; } return sum; }"
                )
            );
        }

        [Test]
        public void CustomCollectionSizeGettersAreNotAssumedStable()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Counter : System.Collections.Generic.List<int> { public new int Count { get { return System.Environment.TickCount; } } } public static void Walk(Counter rows) { for (int index = 0; index < rows.Count; index++) { } }"
                )
            );
        }

        [Test]
        public void SystemCollectionCallsCanInvokeUserEquality()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Item { public System.Collections.Generic.List<Item> owner; public override int GetHashCode() { owner.Clear(); return 0; } } public static void Walk(System.Collections.Generic.List<Item> rows, System.Collections.Generic.HashSet<Item> set) { for (int index = 0; index < rows.Count; index++) { set.Contains(rows[index]); } }"
                )
            );
        }

        [Test]
        public void SeparateCollectionParametersCanAliasAtRuntime()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows, System.Collections.Generic.List<int> output) { for (int index = 0; index < rows.Count; index++) { output.Add(rows[index]); } }"
                )
            );
        }

        [Test]
        public void CapturedReceiverReplacementCanHideBehindAnObjectMethod()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Trigger { public System.Action callback; public void Run() { callback(); } } public static void Walk(int[] rows) { Trigger trigger = new Trigger { callback = () => rows = new int[1] }; for (int index = 0; index < rows.Length; index++) { trigger.Run(); } }"
                )
            );
        }

        [Test]
        public void OverloadedOperatorsCanMutateTheirSourceCollection()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class Item { public static System.Collections.Generic.List<Item> owner; public static Item operator +(Item left, Item right) { owner.Clear(); return left; } } public static void Walk(System.Collections.Generic.List<Item> rows) { Item total = new Item(); for (int index = 0; index < rows.Count; index++) { total = total + rows[index]; } }"
                )
            );
        }

        [Test]
        public void DictionaryIndexerGettersCanInvokeUserEquality()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Walk(System.Collections.Generic.List<int> rows, System.Collections.Generic.Dictionary<string, int> values) { int total = 0; for (int index = 0; index < rows.Count; index++) { total += values[System.String.Empty]; } return total; }"
                )
            );
        }

        [Test]
        public void InterfaceIndexerGettersAreUnknownCode()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Walk(System.Collections.Generic.IReadOnlyList<int> rows) { int total = 0; for (int index = 0; index < rows.Count; index++) { total += rows[index]; } return total; }"
                )
            );
        }

        [Test]
        public void ConcreteListIndexersCannotChangeTheirCount()
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static int Walk(System.Collections.Generic.List<int> rows) { int total = 0; for (int index = 0; index < rows.Count; index++) { total += rows[index]; } return total; }"
                ).Length
            );
        }

        [TestCase("rows[index] = index;")]
        [TestCase("rows[index]++;")]
        public void ConcreteListElementWritesDoNotChangeTheirCount(string body)
        {
            Assert.AreEqual(
                1,
                Analyze(
                    "public static void Walk(System.Collections.Generic.List<int> rows) { for (int index = 0; index < rows.Count; index++) { "
                        + body
                        + " } }"
                ).Length
            );
        }

        [Test]
        public void FieldArrayReceiversCanChangeInsideDictionaryGetters()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int[] rows; public static void Walk(System.Collections.Generic.Dictionary<string, int> values) { for (int index = 0; index < rows.Length; index++) { rows[index] = values[System.String.Empty]; } }"
                )
            );
        }

        [Test]
        public void FieldArrayReceiversCanChangeInsideOverloadedOperators()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int[] rows; public sealed class Item { public static int operator +(Item left, Item right) { rows = new int[0]; return 0; } } public static void Walk(Item item) { for (int index = 0; index < rows.Length; index++) { rows[index] = item + item; } }"
                )
            );
        }

        [TestCase("System.Collections.Generic.IReadOnlyList<int>")]
        [TestCase("System.Collections.Generic.IList<int>")]
        [TestCase("System.Collections.Generic.ICollection<int>")]
        [TestCase("System.Collections.Generic.IReadOnlyCollection<int>")]
        public void InterfaceSizeGettersAreUnknownCode(string typeName)
        {
            Assert.IsEmpty(
                Analyze(
                    "public static int Walk("
                        + typeName
                        + " rows) { int total = 0; for (int index = 0; index < rows.Count; index++) { total += index; } return total; }"
                )
            );
        }

        [Test]
        public void ACountGetterCanShortenItsCollectionDuringTraversal()
        {
            Assert.IsEmpty(
                Analyze(
                    "public sealed class View : System.Collections.Generic.IReadOnlyList<int> { public System.Collections.Generic.List<int> values; public int Count { get { if (values.Count > 0) { values.RemoveAt(values.Count - 1); } return values.Count; } } public int this[int index] { get { return values[index]; } } public System.Collections.Generic.IEnumerator<int> GetEnumerator() { return values.GetEnumerator(); } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); } } public static int Walk(System.Collections.Generic.IReadOnlyList<int> rows) { int total = 0; for (int index = 0; index < rows.Count; index++) { total += index; } return total; }"
                )
            );
        }

        [TestCase("new Mutator();")]
        [TestCase("value.Changed += handler;")]
        [TestCase("value.Changed -= handler;")]
        [TestCase("target.Replace();")]
        [TestCase("rows[index] = target.Number;")]
        [TestCase("rows[index] = target[index];")]
        [TestCase("value++;")]
        [TestCase("value += 1;")]
        [TestCase("using (value) { rows[index] = index; }")]
        [TestCase("using Mutator lease = value;")]
        [TestCase("foreach (int item in value) { rows[index] = item; }")]
        [TestCase("string text = $\"{value}\";")]
        [TestCase("string text = System.String.Empty + value;")]
        [TestCase("(int left, int right) = value;")]
        public void ImplicitDispatchCanReplaceAFieldArray(string body)
        {
            string declarations =
                "public sealed class Mutator : System.IDisposable, System.Collections.Generic.IEnumerable<int> { public Mutator() { rows = new int[0]; } public void Replace() { rows = new int[0]; } public void Dispose() { rows = new int[0]; } public event System.Action Changed { add { rows = new int[0]; } remove { rows = new int[0]; } } public int Number { get { rows = new int[0]; return 0; } } public int this[int index] { get { rows = new int[0]; return 0; } } public System.Collections.Generic.IEnumerator<int> GetEnumerator() { rows = new int[0]; return ((System.Collections.Generic.IEnumerable<int>)new int[0]).GetEnumerator(); } System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); } public void Deconstruct(out int left, out int right) { rows = new int[0]; left = 0; right = 0; } public override string ToString() { rows = new int[0]; return System.String.Empty; } public static Mutator operator ++(Mutator value) { rows = new int[0]; return value; } public static implicit operator int(Mutator value) { rows = new int[0]; return 0; } public static implicit operator Mutator(int value) { rows = new int[0]; return null; } }";
            Assert.IsEmpty(
                Analyze(
                    "public static int[] rows; "
                        + declarations
                        + " public static void Walk(Mutator value, System.Action handler) { dynamic target = value; for (int index = 0; index < rows.Length; index++) { "
                        + body
                        + " } }"
                )
            );
        }

        [Test]
        public void CachedBoundsAreNotReported()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { int count = rows.Length; for (int index = 0; index < count; index++) { rows[index] = index; } }"
                )
            );
        }

        [Test]
        public void ConsumerMustOptIn()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) { for (int index = 0; index < rows.Length; index++) { rows[index] = index; } }",
                    ReportDiagnostic.Default
                )
            );
        }

        [Test]
        public void SuppressionIsHonored()
        {
            Assert.IsEmpty(
                Analyze(
                    "public static void Walk(int[] rows) {\n#pragma warning disable WUH019\nfor (int index = 0; index < rows.Length; index++) { rows[index] = index; }\n#pragma warning restore WUH019\n}"
                )
            );
        }
    }
}
