// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// A member that occupies its field number at most once: a primitive, a string, a
    /// <c>byte[]</c>, an enum, a nested contract, or a <see cref="System.Nullable{T}"/> of any of
    /// those.
    /// </summary>
    internal sealed class ScalarMember : Member
    {
        private readonly Shape _shape;
        private readonly string _presence;
        private readonly string _value;
        private readonly string _assign;
        private readonly string _declared;
        private readonly bool _nullable;

        private ScalarMember(
            string name,
            int tag,
            Shape shape,
            string presence,
            string value,
            string assign,
            string declared,
            bool nullable
        )
            : base(name, tag)
        {
            _shape = shape;
            _presence = presence;
            _value = value;
            _assign = assign;
            _declared = declared;
            _nullable = nullable;
        }

        private string Local => ReadLocal;

        private string SeenFlag => "seen" + Tag;

        /// <summary>The accumulator that gathers every occurrence of a sub-message field.</summary>
        private string Occurrences => "occurrences" + Tag;

        /// <summary>The destination the decoded value lands on once the read loop is done.</summary>
        private string Destination => ConstructAtEnd ? Local : "read." + Name;

        /// <summary>What the read local holds before the payload has said anything about it.</summary>
        /// <remarks>
        /// The seed, where the contract built an instance to take one from. A member the payload
        /// never mentions is then handed to the generated constructor unchanged, which is what makes
        /// an absent field leave the author's constructor value alone -- the same thing an assignable
        /// contract gets for free by reading into the instance itself.
        /// </remarks>
        private string LocalStart => SeedsFromInstance ? SeedSource : "default(" + _declared + ")";

        /// <summary>The value a merged sub-message decodes into, as the type the read produces.</summary>
        /// <remarks>
        /// protobuf reads a sub-message field as <c>MergeFrom</c>, so the FIRST occurrence keeps
        /// whatever the constructor left on the member. Three shapes have nothing to keep and say so
        /// rather than reaching for a value that is not there: a contract built at the end of the
        /// read has no instance to read from, <c>SkipConstructor</c> is standing in for an
        /// uninitialized allocation the oracle does not seed either, and a surrogate member is not
        /// stored as the type the read decodes.
        /// </remarks>
        private string Seed
        {
            get
            {
                string none = "default(" + _shape.ReadLocalType + ")";
                if (Unseeded || _shape.SeedExpression == null)
                {
                    return none;
                }

                string current = SeedSource + (_nullable ? ".GetValueOrDefault()" : string.Empty);
                string seed = Shape.Fill(_shape.SeedExpression, current);
                if (!SkipConstructor)
                {
                    return seed;
                }

                // Under SkipConstructor the member is a seed only when the instance came from the
                // caller; see Member.SeedGuard. With no guard the instance is always this
                // formatter's own, so there is nothing to preserve.
                return SeedGuard == null
                    ? none
                    : "(" + SeedGuard + " ? " + seed + " : " + none + ")";
            }
        }

        /// <summary>
        /// Builds the member when <paramref name="type"/> has a single-value shape, and returns
        /// <c>null</c> otherwise.
        /// </summary>
        /// <param name="name">The member's name.</param>
        /// <param name="tag">The wire field number.</param>
        /// <param name="type">The member's declared type.</param>
        /// <param name="isRequired">Whether <c>IsRequired</c> was set.</param>
        /// <param name="surrogates">The assembly's surrogate registrations.</param>
        /// <param name="nested">The contract's wrapper-message registry.</param>
        /// <remarks>
        /// The registry reaches this last of the three member kinds, and only a <b>rectangular</b>
        /// array ever takes it up. Every collection that earns a wrapper answers the repeated or map
        /// question before this one, so a type arriving here has already been refused by both and the
        /// registry would refuse it too -- except for <c>T[a,b]</c>, whose message is not the run it
        /// declined to be.
        /// </remarks>
        internal static ScalarMember TryCreate(
            string name,
            int tag,
            ITypeSymbol type,
            bool isRequired,
            SurrogateMap surrogates,
            NestedCollections nested
        )
        {
            ITypeSymbol underlying = type;
            bool nullable = false;

            if (
                type is INamedTypeSymbol named
                && named.IsGenericType
                && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            )
            {
                nullable = true;
                underlying = named.TypeArguments[0];
            }

            string access = "value." + name;
            string value = nullable ? access + ".Value" : access;
            string qualifiedUnderlying = underlying.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            Shape shape = Shape.For(underlying, qualifiedUnderlying, surrogates, nested, name);
            if (shape == null)
            {
                return null;
            }

            string presence;
            if (isRequired)
            {
                // IsRequired forces a VALUE onto the wire even when it equals its default; it does
                // not invent one. Measured against protobuf-net 3.2.56: a required int at 0 and a
                // required struct sub-message at default are both written, while a required null
                // string, byte[] or message reference is still absent. Treating "required" as
                // "always present" writes an empty string where protobuf-net wrote nothing -- and,
                // for a message, hands Measure a null to dereference.
                presence =
                    nullable ? access + ".HasValue"
                    : shape.IsReference ? Shape.Fill(shape.PresenceTest, value)
                    : "true";
            }
            else if (nullable)
            {
                presence = access + ".HasValue";
            }
            else
            {
                presence = Shape.Fill(shape.PresenceTest, value);
            }

            string assign = nullable
                ? "(" + qualifiedUnderlying + "?)(" + shape.AssignExpression + ")"
                : shape.AssignExpression;

            return new ScalarMember(
                name,
                tag,
                shape,
                presence,
                value,
                assign,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                nullable
            );
        }

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            writer.Line("if (" + _presence + ")" + Writer.Open);
            writer.Indent();
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Shape.Fill(_shape.SizeExpression, _value)
                    + ";"
            );
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            writer.Line("if (" + _presence + ")" + Writer.Open);
            writer.Indent();
            writer.Line("if (!(" + _shape.WriteCall(_value, Tag) + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitReadLocals(Writer writer)
        {
            if (_shape.IsMessage)
            {
                // Always, not only when deferred: a sub-message is decoded after the loop whatever
                // the contract looks like, because the second occurrence of the field is part of the
                // same value as the first and neither is complete until the loop has ended.
                writer.Line(
                    Proto
                        + ".WProtoMessageAccumulator "
                        + Occurrences
                        + " = default("
                        + Proto
                        + ".WProtoMessageAccumulator);"
                );
                if (ConstructAtEnd)
                {
                    writer.Line(_declared + " " + Local + " = " + LocalStart + ";");
                }

                return;
            }

            if (!Deferred)
            {
                return;
            }

            writer.Line("bool " + SeenFlag + " = false;");
            writer.Line(_declared + " " + Local + " = " + LocalStart + ";");
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer, string qualifiedContract)
        {
            if (_shape.IsMessage)
            {
                EmitMessageEpilogue(writer, qualifiedContract);
                return;
            }

            if (!Deferred)
            {
                return;
            }

            if (ConstructAtEnd)
            {
                // The constructor takes this local directly; there is no instance to assign onto.
                return;
            }

            writer.Line("if (" + SeenFlag + ")" + Writer.Open);
            writer.Indent();
            writer.Line("read." + Name + " = " + Local + ";");
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitReadCases(Writer writer, string qualifiedContract)
        {
            if (_shape.IsMessage)
            {
                EmitMessageCase(writer, qualifiedContract);
                return;
            }

            string local = "decoded" + Tag;
            OpenCase(writer, _shape.WireType);
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
            if (Deferred)
            {
                // The instance this lands on is not known yet: an include tag later in the payload
                // can replace it with a subtype. Assigning now would write onto an object that is
                // about to be thrown away, and protobuf-net permits the include in either position.
                writer.Line(Local + " = " + Shape.Fill(_assign, local) + ";");
                writer.Line(SeenFlag + " = true;");
            }
            else
            {
                writer.Line("read." + Name + " = " + Shape.Fill(_assign, local) + ";");
            }

            writer.Line("break;");
            Close(writer);
        }

        /// <summary>
        /// Emits the case that gathers one occurrence of a sub-message field.
        /// </summary>
        /// <remarks>
        /// The payload is kept rather than decoded, because protobuf says a parser merges repeated
        /// occurrences of a non-repeated sub-message "as if with <c>Message::MergeFrom</c>" --
        /// measured, protobuf-net reads <c>12 02 08 01</c> followed by <c>12 02 10 02</c> as both
        /// members set, for a struct sub-message as well as a reference one. Decoding here instead
        /// is what made the second occurrence REPLACE the first, losing its members in silence.
        /// </remarks>
        private void EmitMessageCase(Writer writer, string qualifiedContract)
        {
            string chunk = "chunk" + Tag;
            OpenCase(writer, _shape.WireType);
            writer.Line(
                "if (!reader.TryReadBytes(out global::System.ReadOnlySpan<byte> "
                    + chunk
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line("if (!" + Occurrences + ".TryAdd(" + chunk + "))" + Writer.Open);
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line("break;");
            Close(writer);
        }

        /// <summary>
        /// Emits the single decode of every occurrence a sub-message field carried.
        /// </summary>
        /// <remarks>
        /// Once, deliberately, however many occurrences there were: the concatenation of two
        /// encodings IS their merge, so decoding it once produces the merged value and runs the
        /// nested type's lifecycle hooks exactly once. It also lands after the loop's malformed
        /// check and after an include has settled which instance the member belongs to, which is
        /// where a value that outlives the read has to be committed.
        /// </remarks>
        private void EmitMessageEpilogue(Writer writer, string qualifiedContract)
        {
            string local = "decoded" + Tag;
            writer.Line("if (" + Occurrences + ".HasValue)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!reader.TryReadMessage("
                    + Occurrences
                    + ".Payload, "
                    + _shape.ReadFormatter
                    + ", "
                    + Seed
                    + ", out "
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
            writer.Line(Destination + " = " + Shape.Fill(_assign, local) + ";");
            Close(writer);
            writer.Blank();
        }
    }
}
