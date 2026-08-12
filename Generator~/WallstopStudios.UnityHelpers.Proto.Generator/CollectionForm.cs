// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>How a repeated member's decoded elements reach the collection they belong to.</summary>
    internal enum CollectionSeeding
    {
        /// <summary>
        /// The accumulator <b>is</b> the member's collection, so reading fills the value the
        /// constructor produced. <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>,
        /// <c>LinkedList&lt;T&gt;</c>, <c>Queue&lt;T&gt;</c> and any consumer collection.
        /// </summary>
        InPlace = 1,

        /// <summary>
        /// The accumulator is a different type, seeded by copying the member's current elements in.
        /// An array, a <c>ReadOnlyCollection&lt;T&gt;</c> and every interface-typed member, none of
        /// which can be constructed and filled in place.
        /// </summary>
        Copy = 2,

        /// <summary>
        /// The accumulator holds only the decoded elements and the commit consults the member. Only
        /// <c>Stack&lt;T&gt;</c> needs this: its elements have to be pushed in the reverse of the
        /// order they arrive in, which cannot be done until all of them have.
        /// </summary>
        Fresh = 3,
    }

    /// <summary>
    /// The shape of one supported repeated member type: what the read loop fills, how, and what it
    /// commits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every field here was measured against protobuf-net 3.2.56 rather than chosen. The interface
    /// mappings are the ones the oracle picks (<c>List&lt;T&gt;</c> for <c>IList&lt;T&gt;</c>,
    /// <c>ICollection&lt;T&gt;</c>, <c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c> and
    /// <c>IReadOnlyCollection&lt;T&gt;</c>; <c>HashSet&lt;T&gt;</c> for <c>ISet&lt;T&gt;</c>) --
    /// which member type a round trip leaves behind is a decision, not an implementation detail, and
    /// matching the oracle is what keeps a migrating consumer's code working unchanged.
    /// </para>
    /// <para>
    /// <c>Stack&lt;T&gt;</c>'s ordering is the one that would have been guessed wrong. protobuf-net
    /// writes a stack in enumeration order -- top first -- and pushes the decoded run back in
    /// <b>reverse</b>, which is what makes the round trip faithful. Pushing in wire order instead
    /// silently inverts a stack, which is a correctness trap rather than a missing call.
    /// </para>
    /// <para>
    /// Two shapes are refused rather than mapped. A <b>consumer's own</b> collection interface and
    /// <c>IReadOnlySet&lt;T&gt;</c> both make protobuf-net write correctly and then throw
    /// <c>InvalidCastException</c> on read, because it builds a <c>List&lt;T&gt;</c> that is not one
    /// -- measured. <c>IReadOnlySet&lt;T&gt;</c> maps to <c>HashSet&lt;T&gt;</c> here, which does
    /// satisfy it; a consumer interface has no answer the generator could pick, so it stays
    /// <c>WPROTO003</c> at build time, which beats a cast failure in a shipped player.
    /// </para>
    /// </remarks>
    internal sealed class CollectionForm
    {
        private const string ListType = "global::System.Collections.Generic.List";

        private const string HashSetType = "global::System.Collections.Generic.HashSet";

        private const string ReadOnlyCollectionType =
            "global::System.Collections.ObjectModel.ReadOnlyCollection";

        private CollectionForm(
            CollectionSeeding seeding,
            string accumulatorGeneric,
            string addMethod,
            string bulkAddMethod,
            string commitGeneric,
            string countMember
        )
        {
            Seeding = seeding;
            _accumulatorGeneric = accumulatorGeneric;
            AddMethod = addMethod;
            BulkAddMethod = bulkAddMethod;
            _commitGeneric = commitGeneric;
            CountMember = countMember;
        }

        private readonly string _accumulatorGeneric;
        private readonly string _commitGeneric;

        /// <summary>Where the decoded elements land.</summary>
        internal CollectionSeeding Seeding { get; }

        /// <summary>The one-argument method that appends one element to the accumulator.</summary>
        internal string AddMethod { get; }

        /// <summary>
        /// The method that adds many elements to an aside accumulator at once, or <c>null</c> when
        /// the accumulator is the member's own collection.
        /// </summary>
        /// <remarks>
        /// Used twice: to seed a <see cref="CollectionSeeding.Copy"/> accumulator from the member's
        /// current elements, and to move a deferred read's pending list into the accumulator once
        /// the instance is final.
        /// </remarks>
        internal string BulkAddMethod { get; }

        /// <summary>
        /// The member that reports the element count, or <c>null</c> when the declared type has
        /// none. Only <c>IEnumerable&lt;T&gt;</c> has none, and the packed write path is what cares:
        /// it decides emptiness from the count rather than by sizing the run a second time.
        /// </summary>
        internal string CountMember { get; }

        /// <summary>Whether the commit pushes the decoded run back in reverse order.</summary>
        internal bool PushesInReverse => Seeding == CollectionSeeding.Fresh;

        /// <summary>Whether the accumulator is a different type from the member's own.</summary>
        internal bool AccumulatesAside => _accumulatorGeneric != null;

        /// <summary>
        /// Whether the member is walked by index rather than with <c>foreach</c>.
        /// </summary>
        /// <remarks>
        /// Only an array is, because that is what the compiler would do anyway and it keeps the
        /// bounds check visible. Everything else is walked with <c>foreach</c> over its declared
        /// type, which binds to the concrete enumerator -- enumerating a struct collection through
        /// <c>IEnumerable&lt;T&gt;</c> would box it on every serialization.
        /// </remarks>
        internal bool IsArray => _commitGeneric == ArrayCommit;

        /// <summary>
        /// The type the read loop accumulates into.
        /// </summary>
        /// <param name="element">The fully qualified element type.</param>
        /// <param name="declared">The member's fully qualified declared type.</param>
        /// <returns>The accumulator's fully qualified type.</returns>
        internal string AccumulatorType(string element, string declared)
        {
            return _accumulatorGeneric == null
                ? declared
                : _accumulatorGeneric + "<" + element + ">";
        }

        /// <summary>
        /// The expression that turns a finished accumulator into the member's value.
        /// </summary>
        /// <param name="accumulator">The accumulator local.</param>
        /// <param name="element">The fully qualified element type.</param>
        /// <returns>The commit expression.</returns>
        internal string Commit(string accumulator, string element)
        {
            if (_commitGeneric == null)
            {
                return accumulator;
            }

            return _commitGeneric == ArrayCommit
                ? accumulator + ".ToArray()"
                : "new " + _commitGeneric + "<" + element + ">(" + accumulator + ")";
        }

        private const string ArrayCommit = "<array>";

        /// <summary>The form for a single-dimension array.</summary>
        internal static CollectionForm Array()
        {
            return new CollectionForm(
                CollectionSeeding.Copy,
                ListType,
                "Add",
                "AddRange",
                ArrayCommit,
                "Length"
            );
        }

        /// <summary>The form for a collection the read loop can construct and fill directly.</summary>
        /// <param name="addMethod">The method that appends one element.</param>
        /// <returns>The form.</returns>
        internal static CollectionForm InPlace(string addMethod)
        {
            return new CollectionForm(
                CollectionSeeding.InPlace,
                null,
                addMethod,
                null,
                null,
                "Count"
            );
        }

        /// <summary>
        /// Resolves the form for a well-known collection type, or <c>null</c> when
        /// <paramref name="type"/> is not one.
        /// </summary>
        /// <param name="type">The member's declared type.</param>
        /// <param name="element">Receives the element type.</param>
        /// <returns>The form, or <c>null</c>.</returns>
        /// <remarks>
        /// Matched on the metadata name rather than on a display string, so a type parameter renamed
        /// upstream cannot silently stop matching. The generic scan in <see cref="RepeatedMember"/>
        /// runs afterwards and picks up everything else, including a consumer's own collection.
        /// </remarks>
        internal static CollectionForm TryWellKnown(INamedTypeSymbol type, out ITypeSymbol element)
        {
            element = null;
            if (!type.IsGenericType || type.TypeArguments.Length != 1)
            {
                return null;
            }

            INamedTypeSymbol definition = type.ConstructedFrom;
            string name =
                definition.ContainingNamespace == null
                    ? definition.MetadataName
                    : definition.ContainingNamespace.ToDisplayString()
                        + "."
                        + definition.MetadataName;

            CollectionForm form = FormFor(name);
            if (form == null)
            {
                return null;
            }

            element = type.TypeArguments[0];
            return form;
        }

        private static CollectionForm FormFor(string name)
        {
            switch (name)
            {
                // Implements ICollection<T> with an EXPLICIT Add, so nothing can fill it through the
                // interface; AddLast is the public way in and appends, which is what the wire order
                // means.
                case "System.Collections.Generic.LinkedList`1":
                    return InPlace("AddLast");

                // Not an ICollection<T> at all. Enqueue appends at the back, and enumeration runs
                // front to back, so a round trip preserves order.
                case "System.Collections.Generic.Queue`1":
                    return InPlace("Enqueue");

                // Also not an ICollection<T>, and the only shape whose order has to be undone: see
                // the class remarks.
                case "System.Collections.Generic.Stack`1":
                    return new CollectionForm(
                        CollectionSeeding.Fresh,
                        ListType,
                        "Add",
                        "AddRange",
                        null,
                        "Count"
                    );

                // Cannot be filled after construction, so the elements are collected and it is built
                // once. protobuf-net writes this shape and then refuses to read it back ("No
                // parameterless constructor found"), so supporting the read is strictly more than
                // the oracle does with bytes it produced itself.
                case "System.Collections.ObjectModel.ReadOnlyCollection`1":
                    return new CollectionForm(
                        CollectionSeeding.Copy,
                        ListType,
                        "Add",
                        "AddRange",
                        ReadOnlyCollectionType,
                        "Count"
                    );

                case "System.Collections.Generic.IList`1":
                case "System.Collections.Generic.ICollection`1":
                case "System.Collections.Generic.IReadOnlyList`1":
                case "System.Collections.Generic.IReadOnlyCollection`1":
                    return new CollectionForm(
                        CollectionSeeding.Copy,
                        ListType,
                        "Add",
                        "AddRange",
                        null,
                        "Count"
                    );

                // The one interface with no Count, which is why the packed write path has a second
                // spelling of "is this collection empty".
                case "System.Collections.Generic.IEnumerable`1":
                    return new CollectionForm(
                        CollectionSeeding.Copy,
                        ListType,
                        "Add",
                        "AddRange",
                        null,
                        null
                    );

                case "System.Collections.Generic.ISet`1":
                case "System.Collections.Generic.IReadOnlySet`1":
                    return new CollectionForm(
                        CollectionSeeding.Copy,
                        HashSetType,
                        "Add",
                        "UnionWith",
                        null,
                        "Count"
                    );

                default:
                    return null;
            }
        }
    }
}
