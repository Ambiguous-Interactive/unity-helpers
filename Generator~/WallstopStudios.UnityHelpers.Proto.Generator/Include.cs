// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// One <c>[WProtoInclude(tag, subType)]</c>: a subtype this contract can hold, and the field
    /// number that identifies it on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The encoding was measured against protobuf-net 3.2.56, and it is <b>not</b> what ascending
    /// field order would suggest. The include is written <b>before</b> the contract's own members,
    /// whatever its tag number -- confirmed with an include at tag 3 emitted ahead of members at tags
    /// 1 and 5. Sorting includes in with the members produces bytes protobuf-net cannot read.
    /// </para>
    /// <para>
    /// Each level repeats the pattern recursively and writes only what it declares: its own subtype
    /// include, then its own members, never the base's. The base formatter is entered first and its
    /// members land last.
    /// </para>
    /// <para>
    /// An include names a <b>direct</b> subtype. protobuf-net refuses a grandchild declared on the
    /// grandparent with "Unexpected sub-type" (measured), so direct subtypes of one contract are
    /// mutually exclusive and the dispatch chain's order cannot change which branch matches.
    /// </para>
    /// <para>
    /// An all-default subtype <b>still writes its include</b> (<c>A2 06 00</c> — a tag and a zero
    /// length). Omitting it because the payload is empty would silently downgrade the value to its
    /// base type on read, which is a type change disguised as a size optimization.
    /// </para>
    /// </remarks>
    internal sealed class Include
    {
        private const string Proto =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        internal Include(int tag, INamedTypeSymbol subType)
            : this(tag, subType, false) { }

        internal Include(int tag, INamedTypeSymbol subType, bool tagFromManifest)
        {
            Tag = tag;
            SubType = subType;
            TagFromManifest = tagFromManifest;
            Qualified = subType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        internal int Tag { get; }

        /// <summary>
        /// Whether <see cref="Tag"/> came from the assembly's subtype tag manifest rather than from
        /// the declaration itself.
        /// </summary>
        /// <remarks>
        /// Nothing about the emitted bytes depends on this -- a manifest number and a written number
        /// are the same number. It exists so a diagnostic can quote the declaration the developer
        /// actually wrote, rather than a number they never typed.
        /// </remarks>
        internal bool TagFromManifest { get; }

        internal INamedTypeSymbol SubType { get; }

        internal string Qualified { get; }

        /// <summary>The local the type test binds the narrowed value to.</summary>
        internal string Local => "include" + Tag;

        /// <summary>
        /// The subtype's own-members formatter, named directly rather than looked up.
        /// </summary>
        /// <remarks>
        /// It must NOT go through <c>WProtoFormatterProvider</c>. What is registered for a subtype is
        /// the formatter that writes the whole hierarchy -- include and base members -- because that
        /// is what protobuf-net writes when the subtype is the declared type. Resolving the include's
        /// payload through the provider would find that one and recurse forever. Naming the nested
        /// type is also one static call instead of a registry read.
        /// </remarks>
        internal string Formatter => Qualified + ".WProtoFormatter.Instance";
    }
}
