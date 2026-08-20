// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// The seam <see cref="Serializer"/> uses to serve a type through WallstopProto instead of
    /// protobuf-net.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the facade swap of the design item, and it is deliberately <b>opt-in per type</b>
    /// rather than all-or-nothing. Each method answers "is there a generated formatter for exactly
    /// this type", and returns <c>false</c> when there is not, so a contract that has been annotated
    /// travels the reflection-free path while one that has not keeps working exactly as before.
    /// Porting the remaining contracts is therefore incremental and individually verifiable, instead
    /// of one change that moves every type at once and can only be tested in aggregate.
    /// </para>
    /// <para>
    /// <see cref="Serializer"/> only calls this when <c>WALLSTOP_PROTO</c> is defined. The methods
    /// themselves compile unconditionally so they can be tested without a second compilation.
    /// </para>
    /// <para>
    /// A value whose runtime type is a <b>declared</b> subtype is served too, through its base's
    /// formatter. That is not a relaxation of the wire contract: a generated formatter dispatches on
    /// the runtime type and writes the include holding the subtype's members followed by the base's,
    /// which is byte-for-byte what protobuf-net writes for the same value. A subtype no
    /// <c>[WProtoInclude]</c> names is still refused, because writing it under its nearest declared
    /// ancestor's tag would read back as that ancestor. The formatter answers that question through
    /// <see cref="IWProtoPolymorphicFormatter"/>; one that does not implement it serves its declared
    /// type only.
    /// </para>
    /// </remarks>
    public static class WProtoFacade
    {
        /// <summary>
        /// Whether a value of <typeparamref name="T"/> can be null, resolved once per closure.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <remarks>
        /// Reference-type closures share one canonical instantiation, so <c>typeof(T)</c> inside a
        /// generic method is a per-call handle lookup rather than a constant.
        /// </remarks>
        private static class TypeShape<T>
        {
            internal static readonly bool IsReferenceType = !typeof(T).IsValueType;
        }

        /// <summary>
        /// Serializes <paramref name="value"/> into <paramref name="buffer"/>, growing it only when
        /// what is already there is too small.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="buffer">
        /// The destination, reused in place when it is large enough and replaced with a larger array
        /// when it is not. May be <c>null</c>. Left untouched when the request is not served.
        /// </param>
        /// <returns>
        /// A <see cref="WProtoWriteResult"/> carrying the byte count -- which may be less than
        /// <c>buffer.Length</c> -- and whether the buffer had to be replaced to fit it.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The allocation-free entry point, and the one a caller serializing every frame should use.
        /// <see cref="TrySerialize{T}"/> hands back an array sized exactly to the payload, which
        /// means a fresh allocation per call; this one lets a caller keep a single scratch buffer for
        /// the lifetime of the program.
        /// </para>
        /// <para>
        /// The length is the RETURN VALUE rather than the buffer's length, because a reused buffer is
        /// almost never exactly the right size. Callers must slice to it -- writing
        /// <c>buffer.Length</c> bytes to a file or socket would append whatever the previous, larger
        /// message left behind.
        /// </para>
        /// <para>
        /// The result is a struct rather than an <c>int</c> with a sentinel. <c>0</c> is a legitimate
        /// length -- an empty contract and a null root both encode to nothing, measured against
        /// protobuf-net -- so "not served" needed a representation that is not a number at all, and a
        /// magic <c>-1</c> reads as a length everywhere it is passed on.
        /// </para>
        /// </remarks>
        public static WProtoWriteResult Serialize<T>(T value, ref byte[] buffer)
        {
            if (!TryResolve(value, out IWProtoFormatter<T> formatter))
            {
                return new WProtoWriteResult(null, false);
            }

            if (TypeShape<T>.IsReferenceType && value == null)
            {
                return new WProtoWriteResult(0, false);
            }

            using WProtoSizes.SizePlanScope sizePlanScope = WProtoSizes.BeginSizePlan();
            int size = formatter.Measure(value);
            ReadOnlySpan<int> sizePlan = sizePlanScope.Freeze();
            bool resized = buffer == null || buffer.Length < size;
            if (resized)
            {
                buffer = new byte[size];
            }

            // Sliced to `size`, not handed the whole buffer: a reused buffer is longer than this
            // message, and the writer's bounds checks are what stop a formatter from running past
            // its own payload into the previous one's bytes.
            WProtoWriter writer = new WProtoWriter(new Span<byte>(buffer, 0, size), sizePlan);
            bool written = formatter.Write(ref writer, value);
            if (!written || writer.Faulted || writer.Position != size)
            {
                // Thrown, not reported as "not served" -- the serialize-side mirror of the refused
                // READ that already throws below. A registered formatter that fails is a defect in
                // this package (Measure and Write disagreeing, or a latched fault), and returning
                // the not-served answer would send the value on to protobuf-net, silently producing
                // bytes from the reflection path this whole serializer exists to avoid. The buffer
                // may also be half-written by now, so there is nothing coherent to hand back.
                throw new InvalidOperationException(
                    $"WallstopProto failed to write a '{typeof(T).FullName}' into {size} measured "
                        + "byte(s). Measure and Write disagree, or the writer latched a fault. This "
                        + "type has a generated formatter, so it is not retried with protobuf-net."
                );
            }

            return new WProtoWriteResult(size, resized);
        }

        /// <summary>
        /// Serializes <paramref name="value"/> when a formatter is registered for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="bytes">Receives the payload, or <c>null</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        /// <remarks>
        /// Allocates an array sized exactly to the payload. A caller that serializes repeatedly
        /// should prefer <see cref="Serialize{T}"/>, which reuses a buffer it is given.
        /// </remarks>
        public static bool TrySerialize<T>(T value, out byte[] bytes)
        {
            // Starting from null is what makes the delegation exact: Serialize allocates precisely
            // `size` when it has nothing to reuse, so the array handed back is never oversized and
            // the caller does not need the written count to interpret it.
            byte[] buffer = null;
            WProtoWriteResult result = Serialize(value, ref buffer);

            if (!result.Served)
            {
                bytes = null;
                return false;
            }

            // A null root and an empty contract both encode to nothing, and neither allocates.
            bytes = result.Length == 0 ? Array.Empty<byte>() : buffer;
            return true;
        }

        /// <summary>
        /// Deserializes <paramref name="data"/> when a formatter is registered for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="data">The payload.</param>
        /// <param name="value">Receives the value, or <c>default</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        /// <exception cref="InvalidOperationException">
        /// <typeparamref name="T"/> has a formatter and that formatter refused
        /// <paramref name="data"/>. This is the <b>only</b> exception this method raises for a
        /// malformed, truncated or hostile payload, and the fuzz suite asserts it: a corrupt save
        /// file has to arrive as one catchable error rather than as whatever the byte that broke
        /// happened to break. See <see cref="TryDeserializeAs{T}"/> for why it is not reported as
        /// <c>false</c>.
        /// </exception>
        public static bool TryDeserialize<T>(ReadOnlySpan<byte> data, out T value)
        {
            return TryDeserializeAs(data, typeof(T), out value);
        }

        /// <summary>
        /// Deserializes <paramref name="data"/> into <paramref name="concrete"/>, when a formatter
        /// registered for <typeparamref name="T"/> produces that type.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="data">The payload.</param>
        /// <param name="concrete">The type the caller named explicitly.</param>
        /// <param name="value">Receives the value, or <c>default</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        /// <exception cref="InvalidOperationException">
        /// A formatter answers for this type and refused the payload. The only exception this method
        /// raises for malformed input, asserted against every strategy in the fuzz suite.
        /// </exception>
        /// <remarks>
        /// The read side of the entry point that lets a caller override the declared type. The
        /// concrete type is not passed on to the formatter, because it does not need it -- the
        /// payload's include tags already name the subtype and the generated reader narrows to it.
        /// It is used to decide whether to answer at all: a type this formatter's dispatch chain does
        /// not produce is one protobuf-net has to serve, and answering anyway would hand back the
        /// wrong type from a payload that is not this contract's.
        /// </remarks>
        public static bool TryDeserializeAs<T>(ReadOnlySpan<byte> data, Type concrete, out T value)
        {
            if (!TryResolveForRead(concrete, out IWProtoFormatter<T> formatter))
            {
                value = default;
                return false;
            }

            WProtoReader reader = new WProtoReader(data);
            try
            {
                if (formatter.TryRead(ref reader, out value))
                {
                    return true;
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"WallstopProto could not read a '{typeof(T).FullName}' from {data.Length} byte(s): "
                        + "the payload is truncated, malformed, or was written by a different contract. "
                        + "This type has a generated formatter, so it is not retried with protobuf-net.",
                    exception
                );
            }

            // "No formatter for this type" and "this type's formatter rejected the payload" are
            // different answers, and only the first one means "not ours". Reporting both as false
            // sent a payload WallstopProto had already refused on to protobuf-net for a second,
            // differently-implemented decode -- which under IL2CPP is the path that cannot run at
            // all, so the real error would surface as a reflection failure somewhere else entirely.
            throw new InvalidOperationException(
                $"WallstopProto could not read a '{typeof(T).FullName}' from {data.Length} byte(s): "
                    + "the payload is truncated, malformed, or was written by a different contract. "
                    + "This type has a generated formatter, so it is not retried with protobuf-net."
            );
        }

        /// <summary>
        /// Finds the formatter that writes <typeparamref name="T"/>, contract or root marshal.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="formatter">Receives the formatter, or <c>null</c> when unserved.</param>
        /// <returns><c>true</c> when the request is WallstopProto's to answer.</returns>
        /// <remarks>
        /// A root marshal is asked no subtype question, and that is not an oversight. It replaces
        /// <c>Serializer</c>'s wrapper interception, which selects on the <b>declared</b> type alone
        /// -- a value held as <c>SerializableDictionary&lt;K, V&gt;</c> has always been written as
        /// that dictionary's wrapper whatever its runtime type is. Asking here would change which
        /// bytes an existing consumer's subclass produces, which is the one thing this port must not
        /// do.
        /// </remarks>
        private static bool TryResolve<T>(T value, out IWProtoFormatter<T> formatter)
        {
            // A declared root is asked the SAME subtype question a contract is, unlike a marshal:
            // it exists precisely because the declared type is not the runtime one, so refusing an
            // implementation outside the root's chain is the whole point of it.
            if (
                WProtoFormatterProvider.TryGet(out formatter)
                || WProtoDeclaredRootProvider.TryGetFormatter(out formatter)
            )
            {
                if (CanEncode(formatter) && CanServe(value, formatter))
                {
                    return true;
                }

                formatter = null;
                return false;
            }

            return WProtoRootMarshalProvider.TryGet(out formatter) && CanEncode(formatter);
        }

        /// <summary>
        /// Finds the formatter that reads <typeparamref name="T"/>, contract or root marshal.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="concrete">The type the caller named, or <c>null</c> for none.</param>
        /// <param name="formatter">Receives the formatter, or <c>null</c> when unserved.</param>
        /// <returns><c>true</c> when the request is WallstopProto's to answer.</returns>
        /// <remarks>
        /// The read mirror of <see cref="TryResolve{T}"/>, including the marshal's indifference to
        /// the concrete type: the wrapper interception it replaces hands back the declared type on
        /// every payload, so a caller naming a subtype gets the same value it always did.
        /// </remarks>
        private static bool TryResolveForRead<T>(Type concrete, out IWProtoFormatter<T> formatter)
        {
            if (
                WProtoFormatterProvider.TryGet(out formatter)
                || WProtoDeclaredRootProvider.TryGetFormatter(out formatter)
            )
            {
                if (CanEncode(formatter) && CanRead(formatter, concrete))
                {
                    return true;
                }

                formatter = null;
                return false;
            }

            return WProtoRootMarshalProvider.TryGet(out formatter) && CanEncode(formatter);
        }

        /// <summary>
        /// Reports whether a formatter can encode the type it is registered for at all.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="formatter">The formatter found for it.</param>
        /// <returns><c>true</c> when the request is WallstopProto's to answer.</returns>
        /// <remarks>
        /// <para>
        /// Two kinds of formatter are registered for closures nobody chose one by one: a root
        /// marshal, registered for every construction of its collection found in source, and a
        /// GENERIC CONTRACT's formatter, registered for every closure of itself. Either can be handed
        /// an element WallstopProto has no formatter for -- a type protobuf-net reaches through a
        /// surrogate, or an enum, both substituted while a contract is generated and unknown at the
        /// closure.
        /// </para>
        /// <para>
        /// It has to be declined HERE, before a hook runs and before a byte is written: the
        /// alternative is a throw from inside <c>Measure</c>, where the reflection path this replaces
        /// fell through to protobuf-net and round-tripped. An empty collection hides it, because the
        /// element loop never runs, so the first failure would land on real data.
        /// </para>
        /// </remarks>
        private static bool CanEncode<T>(IWProtoFormatter<T> formatter)
        {
            return !(formatter is IWProtoConditionalFormatter conditional)
                || conditional.CanServe();
        }

        /// <summary>
        /// Reports whether <paramref name="formatter"/> can write <paramref name="value"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="formatter">The formatter registered for <typeparamref name="T"/>.</param>
        /// <returns><c>true</c> when the request is WallstopProto's to answer.</returns>
        private static bool CanServe<T>(T value, IWProtoFormatter<T> formatter)
        {
            // A null reference has no runtime type to compare, and its encoding is the empty payload
            // either way, so it is served.
            if (!TypeShape<T>.IsReferenceType || value == null)
            {
                return true;
            }

            Type runtimeType = value.GetType();
            if (runtimeType == typeof(T))
            {
                return true;
            }

            // A subtype is asked about rather than refused. A generated formatter dispatches on the
            // runtime type and writes the whole chain -- the include holding the subtype's members,
            // then the base's -- which is byte-for-byte what protobuf-net writes for the same value,
            // so serving it drops nothing. Only the subtypes the chain actually declares qualify;
            // anything else falls through, because a value written under its nearest declared
            // ancestor's tag would read back as that ancestor.
            return formatter is IWProtoPolymorphicFormatter polymorphic
                && polymorphic.CanWrite(runtimeType);
        }

        /// <summary>
        /// Reports whether <paramref name="formatter"/> produces <paramref name="concrete"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="formatter">The formatter registered for <typeparamref name="T"/>.</param>
        /// <param name="concrete">The type the caller named, or <c>null</c> for none.</param>
        /// <returns><c>true</c> when the request is WallstopProto's to answer.</returns>
        private static bool CanRead<T>(IWProtoFormatter<T> formatter, Type concrete)
        {
            if (concrete == null || concrete == typeof(T))
            {
                return true;
            }

            // The same question the write side asks, and deliberately the same answer: a formatter
            // writes exactly the set of runtime types it reads back, because both travel one
            // dispatch chain.
            return formatter is IWProtoPolymorphicFormatter polymorphic
                && polymorphic.CanWrite(concrete);
        }
    }
}
