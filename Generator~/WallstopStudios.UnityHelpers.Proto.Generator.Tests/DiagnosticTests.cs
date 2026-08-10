// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;

    /// <summary>
    /// Pins the build errors a consumer sees when a contract cannot be serialized.
    /// </summary>
    /// <remarks>
    /// These matter more than the happy path. A generator that silently skips a contract it cannot
    /// handle produces an <c>InvalidOperationException</c> from the first save in a shipped player,
    /// naming a type the developer has long since stopped thinking about. Each case below asserts
    /// both the identifier and that the message names the member, because an error code alone sends
    /// the reader to a search engine.
    /// </remarks>
    [TestFixture]
    public sealed class DiagnosticTests
    {
        [Test]
        public void ANonPartialContractIsAnError()
        {
            AssertDiagnostic(
                "WPROTO001",
                "Loose",
                @"[WProtoContract] public sealed class Loose { [WProtoMember(1)] public int Value; }"
            );
        }

        [Test]
        public void TwoMembersClaimingOneFieldNumberIsAnError()
        {
            AssertDiagnostic(
                "WPROTO002",
                "Second",
                @"[WProtoContract] public sealed partial class Clash
                  {
                      [WProtoMember(1)] public int First;
                      [WProtoMember(1)] public int Second;
                  }"
            );
        }

        // Every one of these is a shape a developer would reasonably expect to work, which is why it
        // has to fail the build with a message rather than silently get no formatter.
        //
        // The dictionaries are refused because a protobuf map is a repeated SUB-MESSAGE with the key
        // at field 1 and the value at field 2, not a repeated value -- accepting them here would
        // produce bytes no protobuf implementation could read back. LinkedList and
        // ReadOnlyCollection implement ICollection<T> with an explicit Add, so nothing can fill
        // them; Queue and Stack do not implement it at all. The rest are element-shape refusals: a
        // jagged array of anything but bytes, a rank-2 array, and a nullable element (protobuf-net
        // refuses a null element, so Nullable<T>[] is a collection that can only hold values it
        // cannot write).
        [TestCase("System.Collections.Generic.Dictionary<string, int>")]
        [TestCase("System.Collections.Generic.SortedDictionary<string, int>")]
        [TestCase("System.Collections.Generic.LinkedList<int>")]
        [TestCase("System.Collections.ObjectModel.ReadOnlyCollection<int>")]
        [TestCase("System.Collections.Generic.Queue<int>")]
        [TestCase("System.Collections.Generic.Stack<int>")]
        [TestCase("System.Collections.Generic.IList<int>")]
        [TestCase("System.Collections.Generic.List<System.Collections.Generic.List<int>>")]
        [TestCase("int[][]")]
        [TestCase("int[,]")]
        [TestCase("int?[]")]
        [TestCase("System.DateTime")]
        public void AnUnsupportedMemberTypeIsAnError(string declaredType)
        {
            AssertDiagnostic(
                "WPROTO003",
                "Values",
                @"[WProtoContract] public sealed partial class Unsupported
                  {
                      [WProtoMember(1)] public "
                    + declaredType
                    + @" Values;
                  }"
            );
        }

        [Test]
        public void EveryConstructibleCollectionIsAccepted()
        {
            // The counterpart to the list above, and the reason it is worth having: WPROTO003 fired
            // on List<int> until this session, so "it errors" is not by itself evidence that the
            // error is right. The requirement is ICollection<T> plus a parameterless constructor
            // plus an accessible Add -- not "is one of the types this generator has heard of".
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Supported
                  {
                      [WProtoMember(1)] public int[] Ints;
                      [WProtoMember(2)] public System.Collections.Generic.List<string> Texts;
                      [WProtoMember(3)] public byte[][] Blobs;
                      [WProtoMember(4, OverwriteList = true)] public double[] Doubles;
                      [WProtoMember(5)] public System.Collections.Generic.HashSet<int> Set;
                      [WProtoMember(6)] public System.Collections.Generic.SortedSet<string> Sorted;
                      [WProtoMember(7)] public System.Collections.ObjectModel.Collection<int> Owned;
                  }"
            );
        }

        [Test]
        public void ACollectionImplementedAsAStructIsAcceptedLikeAnyOther()
        {
            // The assumption being refused: nothing about ICollection<T> requires a class, and an
            // inline or pooled buffer is a natural struct. A generator that emits `member != null`
            // for every collection does not merely produce redundant code for one -- it produces
            // code that does not compile.
            AssertNoDiagnostics(
                @"public struct Bag : System.Collections.Generic.ICollection<int>
                  {
                      private System.Collections.Generic.List<int> _items;
                      public int Count { get { return _items == null ? 0 : _items.Count; } }
                      public bool IsReadOnly { get { return false; } }
                      public void Add(int item)
                      {
                          if (_items == null) { _items = new System.Collections.Generic.List<int>(); }
                          _items.Add(item);
                      }
                      public void Clear() { _items = null; }
                      public bool Contains(int item) { return _items != null && _items.Contains(item); }
                      public void CopyTo(int[] array, int index) { }
                      public bool Remove(int item) { return false; }
                      public System.Collections.Generic.IEnumerator<int> GetEnumerator()
                      {
                          return (_items ?? new System.Collections.Generic.List<int>()).GetEnumerator();
                      }
                      System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                      {
                          return GetEnumerator();
                      }
                  }

                  [WProtoContract] public sealed partial class Holder
                  {
                      [WProtoMember(1)] public Bag Values;
                  }"
            );
        }

        [TestCase(0)]
        [TestCase(19500)]
        [TestCase(-3)]
        public void AFieldNumberOutsideTheLegalRangeIsAnError(int tag)
        {
            AssertDiagnostic(
                "WPROTO004",
                "Value",
                @"[WProtoContract] public sealed partial class Ranged
                  {
                      [WProtoMember("
                    + tag
                    + @")] public int Value;
                  }"
            );
        }

        [Test]
        public void ALifecycleHookOnATypeWithNoContractIsAnError()
        {
            // The mistake that shipped inert for two years in Runtime/Tags/Attribute.cs (#370): an
            // attribute advertising a hook nothing was wired to call.
            AssertDiagnostic(
                "WPROTO005",
                "Rebuild",
                @"public sealed partial class Orphan
                  {
                      [WProtoAfterDeserialization] private void Rebuild() { }
                  }"
            );
        }

        [Test]
        public void AReadOnlyMemberIsAnError()
        {
            AssertDiagnostic(
                "WPROTO007",
                "Value",
                @"[WProtoContract] public sealed partial class Frozen
                  {
                      [WProtoMember(1)] public readonly int Value;
                  }"
            );
        }

        [Test]
        public void AHookOnAStructIsAnError()
        {
            // 'in T' makes the compiler copy the struct before the call, so every mutation the hook
            // makes lands on a temporary and is discarded. Silent, and impossible to debug from the
            // outside.
            AssertDiagnostic(
                "WPROTO010",
                "Copied",
                @"[WProtoContract] public partial struct Copied
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoBeforeSerialization] private void Prepare() { }
                  }"
            );
        }

        [Test]
        public void AClassWithNoParameterlessConstructorIsAnError()
        {
            AssertDiagnostic(
                "WPROTO011",
                "Demanding",
                @"[WProtoContract] public sealed partial class Demanding
                  {
                      public Demanding(int seed) { Value = seed; }
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        [Test]
        public void TwoHooksOfTheSameKindIsAnError()
        {
            AssertDiagnostic(
                "WPROTO006",
                "AfterDeserialization",
                @"[WProtoContract] public sealed partial class Twice
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoAfterDeserialization] private void One() { }
                      [WProtoAfterDeserialization] private void Two() { }
                  }"
            );
        }

        [Test]
        public void AHookThatTakesArgumentsIsAnError()
        {
            AssertDiagnostic(
                "WPROTO008",
                "Prepare",
                @"[WProtoContract] public sealed partial class Awkward
                  {
                      [WProtoMember(1)] public int Value;
                      [WProtoBeforeSerialization] private void Prepare(int unused) { }
                  }"
            );
        }

        [Test]
        public void AGenericContractIsARefusalRatherThanAWrongFormatter()
        {
            AssertDiagnostic(
                "WPROTO009",
                "Boxed",
                @"[WProtoContract] public sealed partial class Boxed<T>
                  {
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        [Test]
        public void AContractNestedInsideAGenericTypeIsARefusalToo()
        {
            // The contract itself is not generic; the type it is nested in is. The formatter is
            // emitted by reopening every enclosing type as partial, and a reopened declaration that
            // drops its type parameters does not compile -- so this has to be caught here rather
            // than surface as a compile error in a file the developer never wrote.
            AssertDiagnostic(
                "WPROTO009",
                "Inner",
                @"public static partial class Holder<T>
                  {
                      [WProtoContract] public sealed partial class Inner
                      {
                          [WProtoMember(1)] public int Value;
                      }
                  }"
            );
        }

        [Test]
        public void AContractNestedInAFixtureWithItsOwnHandWrittenFormatterIsAccepted()
        {
            // The shape of WProtoFormatterContractTests.HookedMessage, which is what actually broke
            // the Unity legs the first time the analyzer shipped: a contract nested inside a test
            // fixture, with private members, private hooks, and a hand-written formatter of its own.
            // Every enclosing type has to be partial too, and a hand-written nested formatter must
            // not be mistaken for a conflict.
            AssertNoDiagnostics(
                @"public sealed partial class Fixture
                  {
                      [WProtoContract(Name = ""player_state"")]
                      internal sealed partial class Hooked
                      {
                          [WProtoMember(1, Name = ""health"")] private int _health;
                          [WProtoMember(2, Name = ""label"")] private string _label;
                          [WProtoIgnore] private string _derived;

                          internal Hooked() { }
                          internal Hooked(int health) { _health = health; }

                          [WProtoBeforeSerialization] private void OnBeforeSerialization() { }
                          [WProtoAfterSerialization] private void OnAfterSerialization() { }
                          [WProtoBeforeDeserialization] private void OnBeforeDeserialization() { }
                          [WProtoAfterDeserialization] private void OnAfterDeserialization() { _derived = _label; }

                          internal sealed class Formatter : IWProtoFormatter<Hooked>
                          {
                              public int Measure(in Hooked value) { return 0; }
                              public bool Write(ref WProtoWriter writer, in Hooked value) { return true; }
                              public bool TryRead(ref WProtoReader reader, out Hooked value) { value = null; return true; }
                          }
                      }
                  }"
            );
        }

        [Test]
        public void AValidContractProducesNoDiagnosticsAtAll()
        {
            AssertNoDiagnostics(
                @"[WProtoContract] public sealed partial class Fine
                  {
                      [WProtoMember(1)] public int Value;
                  }"
            );
        }

        private static void AssertNoDiagnostics(string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Assert.IsEmpty(
                diagnostics.Where(d => d.Id.StartsWith("WPROTO", StringComparison.Ordinal)),
                string.Join("; ", diagnostics.Select(d => d.ToString()))
            );
        }

        private static void AssertDiagnostic(string id, string mustName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(d => d.Id == id);

            Assert.IsNotNull(
                match,
                "expected " + id + ", saw: " + string.Join("; ", diagnostics.Select(d => d.Id))
            );
            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(
                match.GetMessage().Contains(mustName),
                "the message must name '" + mustName + "': " + match.GetMessage()
            );
        }

        private static ImmutableArray<Diagnostic> Run(string body)
        {
            string source =
                "namespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
                + body
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
                    out Compilation _,
                    out ImmutableArray<Diagnostic> diagnostics
                );

            return diagnostics;
        }
    }
}
