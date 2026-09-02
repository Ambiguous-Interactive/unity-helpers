// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Serves <typeparamref name="TDeclared"/> by handing every request to
    /// <typeparamref name="TRoot"/>'s formatter.
    /// </summary>
    /// <typeparam name="TDeclared">
    /// The type a value is held as -- an interface, or an abstract type with no contract.
    /// </typeparam>
    /// <typeparam name="TRoot">The contract whose formatter does the work.</typeparam>
    /// <remarks>
    /// <para>
    /// Registered by <see cref="WProtoDeclaredRootProvider.Register{TDeclared, TRoot}"/> for each
    /// <c>[assembly: WProtoDeclaredRoot]</c> pair. It adds no encoding of its own: the bytes are
    /// <typeparamref name="TRoot"/>'s, which is what protobuf-net produces for the same value once
    /// its own root resolution has picked the same type.
    /// </para>
    /// <para>
    /// <b>The root is resolved per call rather than captured.</b> Registration order across Unity's
    /// initialization phases is not guaranteed between assemblies, so capturing the formatter here
    /// would freeze whichever registrar happened to run first -- and silently ignore a consumer who
    /// replaced <typeparamref name="TRoot"/>'s formatter afterwards. The lookup is a static field
    /// read on a closed generic, so there is nothing to save by caching it.
    /// </para>
    /// </remarks>
    public sealed class WProtoDeclaredRootFormatter<TDeclared, TRoot>
        : IWProtoFormatter<TDeclared>,
            IWProtoPolymorphicFormatter,
            IWProtoConditionalFormatter
        where TDeclared : class
        where TRoot : TDeclared
    {
        /// <summary>
        /// Reports whether this adapter is the one currently designated for
        /// <typeparamref name="TDeclared"/>.
        /// </summary>
        /// <returns><c>true</c> when the request is this formatter's to answer.</returns>
        /// <remarks>
        /// <para>
        /// Two ways to be the wrong answer, and both have to be caught before a byte is written --
        /// on the READ side above all, where the facade's alternative is to throw for a payload that
        /// protobuf-net decodes today.
        /// </para>
        /// <para>
        /// <b>Someone else claimed this declared type.</b>
        /// <c>Serializer.RegisterProtobufRoot&lt;IRandom, TheirRandom&gt;()</c> says a consumer's own
        /// implementation owns <c>IRandom</c>, so their payload is not
        /// <typeparamref name="TRoot"/>'s chain and must not be decoded as it.
        /// </para>
        /// <para>
        /// <b>The root has no formatter.</b> A pair can name a contract whose assembly was compiled
        /// without the generator, and a declared root that is also its own root would resolve to
        /// this adapter and recurse forever. The reference comparison is exact rather than
        /// defensive: <c>TRoot : TDeclared</c> makes a longer cycle impossible, because two distinct
        /// types cannot each derive from the other.
        /// </para>
        /// <para>
        /// <b>The declared type can be instantiated.</b> The generator refuses that pair
        /// (<c>WPROTO029</c>), but this provider is public API and a hand-written call can still
        /// make it, so the answer is a refusal rather than a wrong encoding. A value whose runtime
        /// type IS the declared type never reaches <see cref="CanWrite"/> --
        /// <see cref="WProtoFacade"/> short-circuits an exact type match -- and would then be
        /// narrowed to <typeparamref name="TRoot"/>, fail, and encode to nothing. Measured: a
        /// concrete declared type wrote zero bytes for a populated value and read back as the root.
        /// </para>
        /// </remarks>
        public bool CanServe()
        {
            return NeverARuntimeType
                && Root() != null
                && WProtoDeclaredRootProvider.IsRootFor(typeof(TDeclared), typeof(TRoot));
        }

        /// <summary>
        /// Reports whether the root's dispatch chain writes <paramref name="runtimeType"/>.
        /// </summary>
        /// <param name="runtimeType">The value's runtime type.</param>
        /// <returns><c>true</c> when the chain has a branch for it.</returns>
        /// <remarks>
        /// This is what keeps an interface mapping from claiming a consumer's own implementation.
        /// Every value held as <typeparamref name="TDeclared"/> has a runtime type that is not
        /// <typeparamref name="TDeclared"/>, so the facade always asks, and a type outside the
        /// root's include chain is answered <c>false</c> and served by protobuf-net.
        /// </remarks>
        public bool CanWrite(Type runtimeType)
        {
            IWProtoFormatter<TRoot> root = Root();
            if (root == null)
            {
                return false;
            }

            if (root is IWProtoPolymorphicFormatter polymorphic)
            {
                return polymorphic.CanWrite(runtimeType);
            }

            return runtimeType == typeof(TRoot);
        }

        /// <summary>
        /// Returns the byte count the root's formatter produces for <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>The encoded byte count.</returns>
        public int Measure(in TDeclared value)
        {
            IWProtoFormatter<TRoot> root = Root();
            if (root == null || !(value is TRoot narrowed))
            {
                /*
                    Null encodes to nothing, matching every other root. Anything else that fails to
                    narrow is unreachable through the facade -- CanServe refuses a declared type that
                    can BE a runtime type, and every other value is asked of CanWrite, which is
                    derived from the same chain -- and Write agrees with this, so the two can never
                    disagree about a length.
                */
                return 0;
            }

            return root.Measure(narrowed);
        }

        /// <summary>
        /// Writes <paramref name="value"/> through the root's formatter.
        /// </summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the whole value was written.</returns>
        public bool Write(ref WProtoWriter writer, in TDeclared value)
        {
            IWProtoFormatter<TRoot> root = Root();
            if (root == null || !(value is TRoot narrowed))
            {
                return true;
            }

            return root.Write(ref writer, narrowed);
        }

        /// <summary>
        /// Reads a value through the root's formatter.
        /// </summary>
        /// <param name="reader">A reader scoped to this message's payload.</param>
        /// <param name="value">Receives the decoded value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a complete value was read.</returns>
        public bool TryRead(ref WProtoReader reader, out TDeclared value)
        {
            IWProtoFormatter<TRoot> root = Root();
            if (root == null)
            {
                value = default;
                return false;
            }

            if (!root.TryRead(ref reader, out TRoot read))
            {
                value = default;
                return false;
            }

            value = read;
            return true;
        }

        /*
            An interface reports IsAbstract too, so one test would do; both are named because the
            property being asserted is "no value's runtime type is ever TDeclared", and that is what
            makes the facade's exact-match short-circuit unreachable here.
        */
        private static readonly bool NeverARuntimeType =
            typeof(TDeclared).IsInterface || typeof(TDeclared).IsAbstract;

        private IWProtoFormatter<TRoot> Root()
        {
            if (
                !WProtoFormatterProvider.TryGet(out IWProtoFormatter<TRoot> root)
                || ReferenceEquals(root, this)
            )
            {
                return null;
            }

            return root;
        }
    }
}
