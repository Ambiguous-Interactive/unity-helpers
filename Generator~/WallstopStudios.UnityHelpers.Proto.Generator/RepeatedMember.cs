// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// A member whose field number carries a run of values: an array, or any collection that can be
    /// constructed and added to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four encoding rules were measured against protobuf-net 3.2.56 and all four differ from the
    /// scalar case. A repeated field is <b>unpacked</b> by default -- one field key per element,
    /// which is protobuf-net's default and the opposite of proto3's. <b>Every element is
    /// written</b>, including one equal to its type's default, so <c>{0}</c> encodes as
    /// <c>08 00</c> rather than as nothing. <b>Null and empty are the same bytes</b>, so an empty
    /// collection reads back as whatever the constructor left behind, which for a member with no
    /// initializer is <c>null</c>. And a <b>null element has no encoding at all</b>; protobuf-net
    /// raises on one and so does the generated code, via <c>WProtoRepeated.NullElement</c>.
    /// </para>
    /// <para>
    /// Reading appends to whatever the constructor produced unless <c>OverwriteList</c> is set, in
    /// which case the first element seen replaces it. Both were measured: a member initialized to
    /// <c>{7, 8}</c> that receives <c>{1}</c> holds <c>{7, 8, 1}</c> by default and <c>{1}</c> under
    /// <c>OverwriteList</c> -- and holds <c>{7, 8}</c> either way when the field is absent, because
    /// "absent" and "empty" cannot be told apart.
    /// </para>
    /// <para>
    /// <b>A collection is not assumed to be a reference type.</b> Nothing about
    /// <see cref="System.Collections.Generic.ICollection{T}"/> requires a class, and a consumer is
    /// free to implement it on a struct -- a common shape for an inline or pooled buffer. The
    /// emitted code therefore branches on <see cref="ITypeSymbol.IsValueType"/> rather than emitting
    /// a null guard everywhere: a struct collection is always present, is never null-checked, and is
    /// assigned back to its member after reading because everything in between operated on a copy.
    /// Presence is tracked by an explicit flag rather than by the accumulator being null, since a
    /// struct accumulator has no null to be.
    /// </para>
    /// </remarks>
    internal sealed class RepeatedMember : Member
    {
        private const string ListType = "global::System.Collections.Generic.List";

        private const string CollectionInterface = "System.Collections.Generic.ICollection<T>";

        private readonly Shape _shape;
        private readonly CollectionForm _form;
        private readonly string _contractName;
        private readonly string _elementQualified;
        private readonly string _elementDisplay;
        private readonly string _collectionQualified;
        private readonly string _collectionDisplay;
        private readonly bool _collectionIsValueType;
        private readonly bool _overwrite;
        private readonly bool _elementIsReference;
        private readonly bool _elementIsGeneric;

        private RepeatedMember(
            string contractName,
            string name,
            int tag,
            Shape shape,
            CollectionForm form,
            string elementQualified,
            string elementDisplay,
            string collectionQualified,
            string collectionDisplay,
            bool collectionIsValueType,
            bool overwrite,
            bool elementIsGeneric
        )
            : base(name, tag)
        {
            _contractName = contractName;
            _shape = shape;
            _form = form;
            _elementQualified = elementQualified;
            _elementDisplay = elementDisplay;
            _collectionQualified = collectionQualified;
            _collectionDisplay = collectionDisplay;
            _collectionIsValueType = collectionIsValueType;
            _overwrite = overwrite;
            _elementIsGeneric = elementIsGeneric;
            _elementIsReference = !elementIsGeneric && shape.IsReference;
        }

        /// <summary>
        /// Builds the member when <paramref name="type"/> is a supported repeated shape, and returns
        /// <c>null</c> otherwise so the caller can try the scalar shapes.
        /// </summary>
        internal static RepeatedMember TryCreate(
            string contractName,
            string name,
            int tag,
            ITypeSymbol type,
            bool overwriteList,
            SurrogateMap surrogates,
            NestedCollections nested
        )
        {
            // byte[] first: it is the one array protobuf-net treats as a single length-delimited
            // value rather than as a repeated field, so it must never reach this path.
            if (Shape.IsByteArray(type))
            {
                return null;
            }

            ITypeSymbol element;
            CollectionForm form;
            if (type is IArrayTypeSymbol array && array.Rank == 1)
            {
                element = array.ElementType;
                form = CollectionForm.Array();
            }
            else if (!TryResolveForm(type, out element, out form))
            {
                return null;
            }

            string elementQualified = element.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            // An element typed as a type parameter defers its whole encoding to the closure, exactly
            // as a member of that type would.
            bool elementIsGeneric = element is ITypeParameterSymbol;
            Shape shape = elementIsGeneric
                ? null
                : Shape.For(element, elementQualified, surrogates, nested, name);
            if (shape == null && !elementIsGeneric)
            {
                return null;
            }

            return new RepeatedMember(
                contractName,
                name,
                tag,
                shape,
                form,
                elementQualified,
                TypeNaming.Display(element),
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypeNaming.Display(type),
                type.IsValueType,
                overwriteList,
                elementIsGeneric
            );
        }

        /// <summary>
        /// Builds the element run of a rectangular array, or <c>null</c> when its element type has no
        /// encoding.
        /// </summary>
        /// <param name="contractName">The declaring contract's name, for diagnostics at runtime.</param>
        /// <param name="name">The declaring member's name, for diagnostics at runtime.</param>
        /// <param name="tag">The field number the run occupies inside the wrapper message.</param>
        /// <param name="array">The rectangular array type.</param>
        /// <param name="surrogates">The assembly's surrogate registrations.</param>
        /// <param name="nested">The contract's wrapper-message registry.</param>
        /// <returns>The run, or <c>null</c>.</returns>
        /// <remarks>
        /// Separate from <see cref="TryCreate"/> because the two answer opposite questions about the
        /// same type. A rectangular array reaching <c>TryCreate</c> must be refused -- it has no
        /// repeated encoding of its own, and taking it there would write the elements with no way to
        /// recover their shape -- while this is the wrapper deliberately asking for the run that goes
        /// <b>inside</b> the message carrying that shape. Everything else about the run is the
        /// ordinary emitter: packing, null-element refusal, reservation from a packed count, and an
        /// element that is itself a collection, a contract or a surrogated type.
        /// </remarks>
        internal static RepeatedMember CreateRectangular(
            string contractName,
            string name,
            int tag,
            IArrayTypeSymbol array,
            SurrogateMap surrogates,
            NestedCollections nested
        )
        {
            ITypeSymbol element = array.ElementType;
            string elementQualified = element.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            bool elementIsGeneric = element is ITypeParameterSymbol;
            Shape shape = elementIsGeneric
                ? null
                : Shape.For(element, elementQualified, surrogates, nested, name);
            if (shape == null && !elementIsGeneric)
            {
                return null;
            }

            return new RepeatedMember(
                contractName,
                name,
                tag,
                shape,
                CollectionForm.Rectangular(),
                elementQualified,
                TypeNaming.Display(element),
                array.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                TypeNaming.Display(array),
                false,
                // The wrapper message IS the array, so there is no constructor value behind it to
                // append onto: the run replaces rather than accumulates, exactly as the nested
                // collection wrapper's own member does.
                true,
                elementIsGeneric
            );
        }

        /// <summary>
        /// Resolves how a non-array collection type is filled, or reports that it is not one.
        /// </summary>
        /// <param name="type">The member's declared type.</param>
        /// <param name="element">Receives the element type.</param>
        /// <param name="form">Receives the fill strategy.</param>
        /// <returns><c>true</c> when the type is a supported collection.</returns>
        /// <remarks>
        /// The well-known types are asked about first, because several of them would answer the
        /// generic question wrongly: <c>ReadOnlyCollection&lt;T&gt;</c> implements
        /// <c>ICollection&lt;T&gt;</c> and cannot be filled, and <c>LinkedList&lt;T&gt;</c>
        /// implements it with an explicit <c>Add</c> nothing can reach.
        /// </remarks>
        private static bool TryResolveForm(
            ITypeSymbol type,
            out ITypeSymbol element,
            out CollectionForm form
        )
        {
            element = null;
            form = null;
            if (!(type is INamedTypeSymbol named))
            {
                return false;
            }

            form = CollectionForm.TryWellKnown(named, out element);
            if (form != null)
            {
                return true;
            }

            if (!TryElementOfConstructibleCollection(named, out element))
            {
                return false;
            }

            form = CollectionForm.InPlace("Add");
            return true;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> is a collection this generator can both fill and
        /// create, and hands back its element type.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Three requirements, each of which a failing type is better off being told about than
        /// silently serialized wrong. It implements <c>ICollection&lt;T&gt;</c> exactly once, which is
        /// what makes the element type unambiguous. It has an accessible parameterless constructor,
        /// because reading has to be able to produce one. And it has an accessible instance
        /// <c>Add</c> taking the element, because reading has to be able to fill it.
        /// </para>
        /// <para>
        /// The <c>Add</c> requirement is deliberately about the <b>accessible</b> method rather than
        /// the interface slot. <c>LinkedList&lt;T&gt;</c>, <c>ReadOnlyCollection&lt;T&gt;</c> and
        /// <c>Dictionary&lt;K,V&gt;</c> all implement <c>ICollection&lt;T&gt;</c> with an explicit
        /// <c>Add</c> -- and the last of those has a different wire shape entirely (a map is a
        /// repeated sub-message, not a repeated value), so accepting it here would produce bytes no
        /// protobuf implementation could read back.
        /// </para>
        /// <para>
        /// Nothing here requires a class. A struct that implements <c>ICollection&lt;T&gt;</c> is
        /// accepted on the same terms, and the emitted code is what accounts for the difference.
        /// </para>
        /// </remarks>
        private static bool TryElementOfConstructibleCollection(
            INamedTypeSymbol named,
            out ITypeSymbol element
        )
        {
            element = null;
            if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            if (named.IsAbstract)
            {
                return false;
            }

            foreach (INamedTypeSymbol candidate in named.AllInterfaces)
            {
                if (
                    candidate.IsGenericType
                    && candidate.ConstructedFrom.ToDisplayString() == CollectionInterface
                )
                {
                    if (element != null)
                    {
                        // Two closed ICollection<T> implementations means no unambiguous element
                        // type, and picking one of them would be a coin toss over a wire contract.
                        element = null;
                        return false;
                    }

                    element = candidate.TypeArguments[0];
                }
            }

            if (element == null)
            {
                return false;
            }

            if (!HasAccessibleParameterlessConstructor(named))
            {
                element = null;
                return false;
            }

            if (!HasAccessibleAdd(named, element))
            {
                element = null;
                return false;
            }

            return true;
        }

        private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
        {
            if (type.IsValueType)
            {
                return true;
            }

            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (
                    constructor.Parameters.Length == 0
                    && constructor.DeclaredAccessibility == Accessibility.Public
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAccessibleAdd(INamedTypeSymbol type, ITypeSymbol element)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                foreach (ISymbol member in current.GetMembers("Add"))
                {
                    if (
                        member is IMethodSymbol method
                        && !method.IsStatic
                        && method.DeclaredAccessibility == Accessibility.Public
                        && method.Parameters.Length == 1
                        && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, element)
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The closed-type dispatch used when the element type is a type parameter.</summary>
        private string Generic => Proto + ".WProtoGeneric<" + _elementQualified + ">";

        private string IndexLocal => "index" + Tag;

        private string ElementLocal => "element" + Tag;

        private string Accumulator => "repeated" + Tag;

        /// <summary>The list a deferred read collects into before it knows what to commit onto.</summary>
        private string Pending => "pending" + Tag;

        /// <summary>
        /// Whether the read collects elements aside and commits them once the instance is final.
        /// </summary>
        /// <remarks>
        /// Read in three places that must agree -- the locals, the seed and the epilogue -- so it is
        /// named once rather than tested three times. Splitting it is how a half-reverted version
        /// would still compile and read from a <c>null</c> instance.
        /// </remarks>
        private bool DeferSeeding => Deferred;

        /// <summary>Where a decoded element goes: the pending list when deferred, else the member's own accumulator.</summary>
        private string Target => DeferSeeding ? Pending : Accumulator;

        private string PendingType => ListType + "<" + _elementQualified + ">";

        private string SeenFlag => "seen" + Tag;

        /// <summary>The type the read loop accumulates into before committing it.</summary>
        private string AccumulatorType =>
            _form.AccumulatorType(_elementQualified, _collectionQualified);

        /// <summary>
        /// The statement that sizes what this read fills for a whole packed run at once, or
        /// <c>null</c> when the form has no way to be sized.
        /// </summary>
        /// <param name="packed">The reader scoped to the run.</param>
        /// <param name="wireType">The expression naming one element's wire type.</param>
        /// <remarks>
        /// Only the packed spelling gets this. An unpacked run is a sequence of separate fields that
        /// may be interleaved with other members, so its length is not knowable until it ends --
        /// which is exactly the case that has to keep growing, and does.
        /// </remarks>
        private string ReserveFor(string packed, string wireType)
        {
            string count = packed + ".CountPackedElements(" + wireType + ")";
            return DeferSeeding
                ? CollectionForm.ReservePendingStatement(Pending, count)
                : _form.ReserveStatement(Accumulator, count);
        }

        /// <summary>The statement that appends one decoded element to <paramref name="target"/>.</summary>
        private string AddTo(string target, string value)
        {
            return target + "." + _form.AddMethod + "(" + value + ");";
        }

        /// <summary>
        /// The statement that appends one decoded element to whatever this read is collecting into.
        /// </summary>
        /// <remarks>
        /// A deferred read always collects into a plain list, whose method is <c>Add</c> whatever the
        /// member's own is -- using the member's method there would emit <c>pending.Enqueue(...)</c>
        /// on a <c>List&lt;T&gt;</c>.
        /// </remarks>
        private string AddToTarget(string value)
        {
            return DeferSeeding ? Pending + ".Add(" + value + ");" : AddTo(Accumulator, value);
        }

        /// <summary>
        /// Whether this member is written as a single packed run rather than one field per element.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Packed is proto3's default and roughly HALVES a repeated scalar: unpacked pays a field key
        /// per element, packed pays one key and one length for the whole run. Measured on
        /// <c>int[]</c>: 200 bytes unpacked against 102 packed at 100 elements, 2872 against 1875 at
        /// 1000.
        /// </para>
        /// <para>
        /// This is a deliberate divergence from protobuf-net's OUTPUT at CompatibilityLevel 200,
        /// which writes unpacked, and it is safe because wire compatibility is about what the other
        /// side can READ. Measured against protobuf-net 3.2.56: a packed run decodes into a field it
        /// declares unpacked, exactly, for every packable element type -- int, long, ulong, bool,
        /// enum, float, double, short -- and into both arrays and List&lt;T&gt;. The reader here
        /// already accepted both forms, so old data keeps working in the other direction too.
        /// </para>
        /// <para>
        /// A generic element cannot take this path: whether <c>T</c> packs is a property of the
        /// closure, so its run is decided at runtime by <c>WProtoGeneric&lt;T&gt;</c> instead.
        /// </para>
        /// </remarks>
        private bool WritesPacked => !_elementIsGeneric && _shape.Packable;

        /// <summary>Whether the member is walked by index rather than with <c>foreach</c>.</summary>
        private bool IsArray => _form.WalksByIndex;

        /// <summary>The element count, however this collection spells it.</summary>
        private string CountAccess => Access + "." + _form.CountMember;

        /// <summary>
        /// Whether the packed write can decide emptiness from a count rather than by discovering it.
        /// </summary>
        private bool HasCount => _form.CountMember != null;

        private string WroteFlag => "wrotePacked" + Tag;

        private string PayloadSize => "packedSize" + Tag;

        private string PackedToken => "packedToken" + Tag;

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            if (WritesPacked)
            {
                EmitPackedMeasure(writer);
                return;
            }

            if (_elementIsGeneric)
            {
                EmitGenericMeasure(writer);
                return;
            }

            int open = OpenLoop(writer);
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Shape.Fill(_shape.SizeExpression, ElementLocal)
                    + ";"
            );
            CloseAll(writer, open);
            writer.Blank();
        }

        /// <summary>
        /// Sizes a generic run, choosing packed or unpacked once the closure is known.
        /// </summary>
        /// <remarks>
        /// Both forms are emitted and the branch is taken at runtime, because whether <c>T</c> packs
        /// is a property of the closure: <c>Deque&lt;int&gt;</c> packs and <c>Deque&lt;string&gt;</c>
        /// cannot. The generated code costs one static bool read per member to decide.
        /// </remarks>
        private void EmitGenericMeasure(Writer writer)
        {
            writer.Line("if (" + Generic + ".Packable)" + Writer.Open);
            writer.Indent();
            writer.Line("int " + PayloadSize + " = 0;");
            int packedOpen = OpenLoop(writer);
            writer.Line(PayloadSize + " += " + Generic + ".MeasureValue(" + ElementLocal + ");");
            CloseAll(writer, packedOpen);
            writer.Blank();
            writer.Line("if (0 < " + PayloadSize + ")" + Writer.Open);
            writer.Indent();
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Proto
                    + ".WProtoSizes.LengthDelimitedSize("
                    + PayloadSize
                    + ");"
            );
            Close(writer);
            Close(writer);
            writer.Line("else" + Writer.Open);
            writer.Indent();
            int looseOpen = OpenLoop(writer);
            writer.Line(
                "size += " + Generic + ".MeasureElement(" + Tag + ", " + ElementLocal + ");"
            );
            CloseAll(writer, looseOpen);
            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Sizes the run: one key and one length prefix for the whole member, and the bare values.
        /// </summary>
        private void EmitPackedMeasure(Writer writer)
        {
            writer.Line("int " + PayloadSize + " = 0;");
            int open = OpenLoop(writer);
            writer.Line(
                PayloadSize + " += " + Shape.Fill(_shape.SizeExpression, ElementLocal) + ";"
            );
            CloseAll(writer, open);
            writer.Blank();

            // `> 0` is exactly "the collection is not empty" for a packable shape, because every
            // packable element occupies at least one byte -- a varint zero is one byte, a fixed32 is
            // four. An empty collection therefore writes nothing at all, which is what protobuf-net
            // does and what keeps absent and empty indistinguishable on the wire, as before.
            writer.Line("if (0 < " + PayloadSize + ")" + Writer.Open);
            writer.Indent();
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Proto
                    + ".WProtoSizes.LengthDelimitedSize("
                    + PayloadSize
                    + ");"
            );
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            if (WritesPacked)
            {
                EmitPackedWrite(writer);
                return;
            }

            if (_elementIsGeneric)
            {
                EmitGenericWrite(writer);
                return;
            }

            int open = OpenLoop(writer);
            writer.Line(
                "if (!("
                    + (
                        _elementIsGeneric
                            ? Generic
                                + ".WriteElement(ref writer, "
                                + Tag
                                + ", "
                                + ElementLocal
                                + ")"
                            : _shape.WriteCall(ElementLocal, Tag)
                    )
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            CloseAll(writer, open);
            writer.Blank();
        }

        /// <summary>
        /// Emits the presence guard and the loop header shared by measuring and writing, and returns
        /// how many blocks the caller has to close.
        /// </summary>
        /// <remarks>
        /// An array is walked by index because that is what the compiler would do anyway and it
        /// keeps the bounds check visible; every other collection is walked with <c>foreach</c> over
        /// its declared type, which binds to the concrete enumerator. That matters for a struct
        /// collection: enumerating it through <c>IEnumerable&lt;T&gt;</c> would box it on every
        /// serialization, which is exactly the cost a struct collection exists to avoid.
        /// </remarks>
        /// <param name="guarded">
        /// Whether to emit the collection's null guard. The packed path passes <c>false</c> because
        /// it has already tested both null and emptiness in one condition, and a second identical
        /// guard inside it would be dead.
        /// </param>
        private int OpenLoop(Writer writer, bool guarded = true)
        {
            int open = 0;

            // A struct collection is always present. Emitting `!= null` for one is a compile error,
            // not merely redundant -- which is the tell that treating every collection as a
            // reference is a real assumption rather than a harmless simplification.
            if (guarded && !_collectionIsValueType)
            {
                writer.Line("if (" + Access + " != null)" + Writer.Open);
                writer.Indent();
                open++;
            }

            if (IsArray)
            {
                writer.Line(
                    "for (int "
                        + IndexLocal
                        + " = 0; "
                        + IndexLocal
                        + " < "
                        + Access
                        + ".Length; "
                        + IndexLocal
                        + "++)"
                        + Writer.Open
                );
                writer.Indent();
                open++;
                writer.Line(
                    _elementQualified
                        + " "
                        + ElementLocal
                        + " = "
                        + Access
                        + "["
                        + IndexLocal
                        + "];"
                );
            }
            else
            {
                writer.Line(
                    "foreach ("
                        + _elementQualified
                        + " "
                        + ElementLocal
                        + " in "
                        + Access
                        + ")"
                        + Writer.Open
                );
                writer.Indent();
                open++;
            }

            if (_elementIsReference)
            {
                writer.Line("if (" + ElementLocal + " == null)" + Writer.Open);
                writer.Indent();

                // A wrapper message is shared by every member of the contract holding its type, so
                // naming one of them would name whichever the generator resolved first. The
                // collection type is the thing all of them actually have in common.
                writer.Line(
                    "throw "
                        + Proto
                        + (
                            WrapsWholeValue
                                ? ".WProtoRepeated.NullNestedElement(\""
                                    + _contractName
                                    + "\", \""
                                    + _collectionDisplay
                                : ".WProtoRepeated.NullElement(\"" + _contractName + "\", \"" + Name
                        )
                        + "\", \""
                        + _elementDisplay
                        + "\");"
                );
                Close(writer);
            }

            writer.Blank();
            return open;
        }

        private static void EmitReserve(Writer writer, string statement)
        {
            if (statement == null)
            {
                return;
            }

            writer.Line(statement);
            writer.Blank();
        }

        private static void CloseAll(Writer writer, int count)
        {
            for (int closed = 0; closed < count; closed++)
            {
                Close(writer);
            }
        }

        /// <inheritdoc />
        internal override void EmitReadLocals(Writer writer)
        {
            // Presence is a flag rather than "the accumulator is not null", because a struct
            // collection has no null state to test and `default(T)` is a legitimate value for it.
            writer.Line("bool " + SeenFlag + " = false;");

            if (DeferSeeding)
            {
                // A polymorphic contract cannot seed from the member yet. `read` may still be null
                // -- an abstract base has no instance until an include tag arrives -- and even when
                // it is not, an include later in the payload replaces it, so seeding now would
                // append onto a constructor value that is about to be discarded. Elements are
                // collected on their own and combined in the epilogue, once the instance is final.
                writer.Line(PendingType + " " + Pending + " = null;");
                if (ConstructAtEnd)
                {
                    writer.Line(
                        DeclaredType + " " + ReadLocal + " = default(" + DeclaredType + ");"
                    );
                }

                return;
            }

            writer.Line(
                AccumulatorType + " " + Accumulator + " = default(" + AccumulatorType + ");"
            );
        }

        /// <inheritdoc />
        internal override void EmitReadCases(Writer writer, string qualifiedContract)
        {
            string local = "decoded" + Tag;

            if (_elementIsGeneric)
            {
                writer.Line(
                    "case " + Tag + " when " + Generic + ".Accepts(wireType):" + Writer.Open
                );
                writer.Indent();
                EmitSeed(writer);
                writer.Line(
                    "if (!"
                        + Generic
                        + ".TryReadValue(ref reader, out "
                        + _elementQualified
                        + " "
                        + local
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                EmitReadFailure(writer, qualifiedContract);
                Close(writer);
                writer.Blank();
                writer.Line(AddToTarget(local));
                writer.Line("break;");
                Close(writer);

                // The packed spelling of the same field. A generic element gets this for the same
                // reason a known one does -- a packed run is what every other protobuf implementation
                // emits, and skipping it hands back a silently short collection -- but the decision
                // has to be deferred to the closure: `Packable` is false when T is itself
                // length-delimited, where this case would otherwise steal a single string element.
                // The guard makes the two cases mutually exclusive, so order between them is not load
                // bearing.
                string genericPacked = "packed" + Tag;
                writer.Line(
                    "case "
                        + Tag
                        + " when "
                        + Generic
                        + ".Packable && wireType == "
                        + Proto
                        + ".WProtoWireType.LengthDelimited:"
                        + Writer.Open
                );
                writer.Indent();
                EmitSeed(writer);
                writer.Line(
                    "if (!reader.TryReadPackedRun(out "
                        + Proto
                        + ".WProtoReader "
                        + genericPacked
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                EmitReadFailure(writer, qualifiedContract);
                Close(writer);
                writer.Blank();
                EmitReserve(writer, ReserveFor(genericPacked, Generic + ".WireType"));
                writer.Line("while (!" + genericPacked + ".End)" + Writer.Open);
                writer.Indent();
                writer.Line(
                    "if (!"
                        + Generic
                        + ".TryReadValue(ref "
                        + genericPacked
                        + ", out "
                        + _elementQualified
                        + " "
                        + local
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                EmitReadFailure(writer, qualifiedContract);
                Close(writer);
                writer.Blank();
                writer.Line(AddToTarget(local));
                Close(writer);
                writer.Blank();
                writer.Line("break;");
                Close(writer);
                return;
            }

            OpenCase(writer, _shape.WireType);
            EmitSeed(writer);
            writer.Line(
                "if (!reader."
                    + _shape.ReadMethod
                    + "("
                    + _shape.ReadArguments
                    + "out "
                    + _shape.ReadLocalType
                    + " "
                    + local
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line(AddToTarget(Shape.Fill(_shape.AssignExpression, local)));
            writer.Line("break;");
            Close(writer);

            if (!_shape.Packable)
            {
                return;
            }

            // protobuf-net decodes a packed payload into an unpacked member and back again, and
            // accepts the two interleaved within one message (measured). A reader that only knew the
            // form it writes would silently skip the field as unrecognized and hand back a shorter
            // collection, which is the worst of the available failures.
            string packed = "packed" + Tag;
            OpenCase(writer, Proto + ".WProtoWireType.LengthDelimited");
            EmitSeed(writer);
            writer.Line(
                "if (!reader.TryReadPackedRun(out "
                    + Proto
                    + ".WProtoReader "
                    + packed
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            EmitReserve(writer, ReserveFor(packed, _shape.WireType));
            writer.Line("while (!" + packed + ".End)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!"
                    + packed
                    + "."
                    + _shape.ReadMethod
                    + "("
                    + _shape.ReadArguments
                    + "out "
                    + _shape.ReadLocalType
                    + " "
                    + local
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line(AddToTarget(Shape.Fill(_shape.AssignExpression, local)));
            Close(writer);
            writer.Blank();
            writer.Line("break;");
            Close(writer);
        }

        /// <summary>
        /// Writes a generic run, choosing packed or unpacked once the closure is known.
        /// </summary>
        private void EmitGenericWrite(Writer writer)
        {
            EmitPackedRun(
                writer,
                Generic + ".Packable",
                Generic + ".WriteValue(ref writer, " + ElementLocal + ")"
            );

            // A second `if` rather than an `else`, because the packed branch closes its own blocks.
            // The two conditions are mutually exclusive, and `Packable` is a static readonly bool.
            writer.Line("if (!" + Generic + ".Packable)" + Writer.Open);
            writer.Indent();
            int looseOpen = OpenLoop(writer);
            writer.Line(
                "if (!"
                    + Generic
                    + ".WriteElement(ref writer, "
                    + Tag
                    + ", "
                    + ElementLocal
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            CloseAll(writer, looseOpen);
            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Writes the run: one key, one back-filled length, then the bare values.
        /// </summary>
        /// <remarks>
        /// The length is back-filled rather than measured again. Measure already walked this
        /// collection once; walking it a second time here would double the cost of every repeated
        /// field, and for a large collection that is the dominant cost of serializing at all.
        /// </remarks>
        private void EmitPackedWrite(Writer writer)
        {
            EmitPackedRun(writer, null, _shape.RawWriteCall(ElementLocal));
        }

        /// <summary>
        /// Emits one packed run: the key, a back-filled length, and the bare values.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <param name="condition">An extra condition to require, or <c>null</c>.</param>
        /// <param name="writeCall">The boolean call that writes one element.</param>
        /// <remarks>
        /// <para>
        /// Emptiness is decided from the element count rather than from a computed payload size,
        /// because the whole point of back-filling the length is not to compute that size twice.
        /// </para>
        /// <para>
        /// <c>IEnumerable&lt;T&gt;</c> has no count, and it is the only supported type that does not,
        /// so the run is opened by the first element instead. Enumerating once to test for emptiness
        /// and again to write would be the alternative, and a consumer's <c>IEnumerable&lt;T&gt;</c>
        /// may be a query rather than a container -- <c>Measure</c> already walks it once, and a
        /// third pass is worse than one flag.
        /// </para>
        /// </remarks>
        private void EmitPackedRun(Writer writer, string condition, string writeCall)
        {
            string guard = PackedGuard(condition);
            int open = 0;
            if (guard != null)
            {
                writer.Line("if (" + guard + ")" + Writer.Open);
                writer.Indent();
                open++;
            }

            // `false` on every TryBeginLengthDelimited below: a packed run holds bare scalars and
            // cannot recurse, so it spends no nesting level -- matching TryReadPackedRun, which
            // hands its nested reader the same depth. Charging it would make a deep-but-legal
            // message decodable and not encodable.
            if (HasCount)
            {
                writer.Line(
                    "if (!writer.TryBeginLengthDelimited("
                        + Tag
                        + ", false, out "
                        + Proto
                        + ".WProtoLengthToken "
                        + PackedToken
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("return false;");
                Close(writer);
                writer.Blank();
            }
            else
            {
                writer.Line("bool " + WroteFlag + " = false;");
                writer.Line(
                    Proto
                        + ".WProtoLengthToken "
                        + PackedToken
                        + " = default("
                        + Proto
                        + ".WProtoLengthToken);"
                );
                writer.Blank();
            }

            string begin =
                "writer.TryBeginLengthDelimited(" + Tag + ", false, out " + PackedToken + ")";

            int loop = OpenLoop(writer, guarded: false);

            if (!HasCount)
            {
                writer.Line("if (!" + WroteFlag + ")" + Writer.Open);
                writer.Indent();
                writer.Line(WroteFlag + " = true;");
                writer.Line("if (!" + begin + ")" + Writer.Open);
                writer.Indent();
                writer.Line("return false;");
                Close(writer);
                Close(writer);
                writer.Blank();
            }

            writer.Line("if (!" + writeCall + ")" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            CloseAll(writer, loop);
            writer.Blank();

            writer.Line(
                "if ("
                    + (HasCount ? string.Empty : WroteFlag + " && ")
                    + "!writer.TryCloseLengthDelimited("
                    + PackedToken
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);

            CloseAll(writer, open);
            writer.Blank();
        }

        /// <summary>
        /// The condition a packed run is written under, or <c>null</c> when there is nothing to test.
        /// </summary>
        private string PackedGuard(string condition)
        {
            string guard = condition ?? string.Empty;

            if (!_collectionIsValueType)
            {
                guard = Join(guard, Access + " != null");
            }

            if (HasCount)
            {
                guard = Join(guard, "0 < " + CountAccess);
            }

            return guard.Length == 0 ? null : guard;
        }

        private static string Join(string left, string right)
        {
            return left.Length == 0 ? right : left + " && " + right;
        }

        /// <summary>
        /// Emits the one-time creation of the accumulator, which is also what makes a
        /// present-but-empty packed run produce an empty collection rather than leave the
        /// constructor's value in place.
        /// </summary>
        private void EmitSeed(Writer writer)
        {
            writer.Line("if (!" + SeenFlag + ")" + Writer.Open);
            writer.Indent();
            writer.Line(SeenFlag + " = true;");

            if (DeferSeeding)
            {
                writer.Line(Pending + " = new " + PendingType + "();");
                Close(writer);
                writer.Blank();
                return;
            }

            string fresh = "new " + AccumulatorType + "()";

            // `Fresh` seeding starts empty whatever the member holds, because its commit is what
            // consults the member -- a stack's elements cannot be pushed until all of them arrived.
            if (_overwrite || SkipConstructor || _form.Seeding == CollectionSeeding.Fresh)
            {
                writer.Line(Accumulator + " = " + fresh + ";");
            }
            else if (_form.Seeding == CollectionSeeding.Copy)
            {
                // The member cannot be filled in place -- it is an array, a read-only collection, or
                // an interface with no constructor -- so its current elements are copied into an
                // accumulator the decoded ones are appended to. Every declared type that lands here
                // is a reference, so the guard is always legal.
                writer.Line(Accumulator + " = " + fresh + ";");
                writer.Line("if (read." + Name + " != null)" + Writer.Open);
                writer.Indent();
                writer.Line(Accumulator + "." + _form.BulkAddMethod + "(read." + Name + ");");
                Close(writer);
            }
            else if (_collectionIsValueType)
            {
                // A copy, necessarily -- which is why the epilogue assigns it back. Reading into the
                // member in place is not available for a struct: every mutation would land on
                // whatever temporary the expression produced.
                writer.Line(Accumulator + " = read." + Name + ";");
            }
            else
            {
                // Appending into the constructor's own instance is what protobuf-net does, and it is
                // also the only way a member the constructor handed a reference out to keeps seeing
                // the decoded elements.
                writer.Line(Accumulator + " = read." + Name + " ?? " + fresh + ";");
            }

            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitPresentSeed(Writer writer)
        {
            EmitSeed(writer);
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer, string qualifiedContract)
        {
            // Guarded, because an absent field must leave the constructor's value alone. "Absent"
            // and "empty" are the same bytes, so this is the only place the difference survives.
            // The assignment is not redundant for a class either: the member may have started null.
            writer.Line("if (" + SeenFlag + ")" + Writer.Open);
            writer.Indent();

            if (DeferSeeding)
            {
                // Runs after the include has settled `read` and after the abstract-contract null
                // check, so this is the first point at which the constructor value to append onto is
                // knowable. protobuf-net, handed an include AFTER the elements, appends onto the
                // base instance and then merges into the subtype's own collection, duplicating the
                // constructor's entries -- measured, {7,8,1} against {7,8,7,8,1} for the same
                // elements in the other order. It always writes the include first, so no payload it
                // produces reaches that path; this yields the same answer either way instead.
                writer.Line(AccumulatorType + " " + Accumulator + " = " + DeferredSeed() + ";");
                if (_form.AccumulatesAside)
                {
                    writer.Line(Accumulator + "." + _form.BulkAddMethod + "(" + Pending + ");");
                }
                else
                {
                    writer.Line(
                        "foreach ("
                            + _elementQualified
                            + " "
                            + ElementLocal
                            + " in "
                            + Pending
                            + ")"
                            + Writer.Open
                    );
                    writer.Indent();
                    writer.Line(AddTo(Accumulator, ElementLocal));
                    Close(writer);
                }
            }

            EmitCommit(writer);
            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Emits the assignment of the finished accumulator onto the member.
        /// </summary>
        /// <remarks>
        /// Every form but one is an expression. <c>Stack&lt;T&gt;</c> is the exception: protobuf-net
        /// writes a stack top-first and pushes the run back in <b>reverse</b>, which is what makes a
        /// round trip faithful and cannot be expressed until the whole run has arrived. Pushing in
        /// wire order instead inverts the stack silently, which is worse than refusing it.
        /// </remarks>
        private void EmitCommit(Writer writer)
        {
            string destination = ConstructAtEnd ? ReadLocal : "read." + Name;

            if (!_form.PushesInReverse)
            {
                writer.Line(
                    destination
                        + " = "
                        + _form.CommitExpression(Accumulator, _elementQualified)
                        + ";"
                );
                return;
            }

            string target = "target" + Tag;
            string fresh = "new " + _collectionQualified + "()";
            writer.Line(
                _collectionQualified
                    + " "
                    + target
                    + " = "
                    + (
                        _overwrite || ConstructAtEnd || SkipConstructor
                            ? fresh
                            : "read." + Name + " ?? " + fresh
                    )
                    + ";"
            );
            writer.Line(
                "for (int "
                    + IndexLocal
                    + " = "
                    + Accumulator
                    + ".Count - 1; 0 <= "
                    + IndexLocal
                    + "; "
                    + IndexLocal
                    + "--)"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(target + ".Push(" + Accumulator + "[" + IndexLocal + "]);");
            Close(writer);
            writer.Line(destination + " = " + target + ";");
        }

        /// <summary>
        /// The accumulator a deferred read commits onto, seeded from the final instance.
        /// </summary>
        private string DeferredSeed()
        {
            string fresh = "new " + AccumulatorType + "()";

            // A contract built by a constructor has no instance to append onto: the formatter never
            // runs the author's constructor, so there is no seeded collection to preserve. `Fresh`
            // seeding starts empty for the same reason it does in EmitSeed -- its commit is what
            // reads the member.
            if (
                _overwrite
                || ConstructAtEnd
                || SkipConstructor
                || _form.Seeding == CollectionSeeding.Fresh
            )
            {
                return fresh;
            }

            if (_form.Seeding == CollectionSeeding.Copy)
            {
                // The pending elements are appended after this, so the member's own only has to be
                // copied in first. Every accumulator type used here takes an IEnumerable<T>.
                return "read."
                    + Name
                    + " == null ? "
                    + fresh
                    + " : new "
                    + AccumulatorType
                    + "(read."
                    + Name
                    + ")";
            }

            return _collectionIsValueType ? "read." + Name : "read." + Name + " ?? " + fresh;
        }
    }
}
