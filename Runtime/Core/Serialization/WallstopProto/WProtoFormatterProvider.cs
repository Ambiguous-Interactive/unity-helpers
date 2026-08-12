// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Resolves the <see cref="IWProtoFormatter{T}"/> for a message type without reflection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lookup is a static field on a closed generic type, so <see cref="Get{T}"/> compiles to a
    /// field read and IL2CPP emits it ahead of time like any other generic call. There is no
    /// dictionary keyed by <see cref="Type"/>, no <c>MakeGenericType</c>, and nothing that has to
    /// exist at runtime for a type the AOT compiler did not already see -- which is the whole reason
    /// this serializer exists.
    /// </para>
    /// <para>
    /// Registration is open to consumers. A game registers formatters for its own contracts, and can
    /// hand-write one for a type no generator can see -- one from a third-party assembly, for
    /// instance -- by calling <see cref="Register{T}"/> from a
    /// <c>[RuntimeInitializeOnLoadMethod]</c> or a module initializer.
    /// </para>
    /// </remarks>
    public static class WProtoFormatterProvider
    {
        /// <summary>
        /// Registers <paramref name="formatter"/> as the formatter for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="formatter">The formatter, or <c>null</c> to clear the registration.</param>
        /// <remarks>
        /// <para>
        /// Last registration wins, so a consumer can deliberately replace a formatter this package
        /// registers for one of its own types. That ordering is a guarantee, not an accident: this
        /// package registers its built-ins from
        /// <c>RuntimeInitializeLoadType.SubsystemRegistration</c>, the first phase Unity runs, so a
        /// consumer registering from any later phase -- including the default,
        /// <c>BeforeSceneLoad</c> -- always overrides it.
        /// </para>
        /// <para>
        /// Registration is not thread-safe against concurrent resolution and is meant to run once
        /// during startup, before anything serializes.
        /// </para>
        /// </remarks>
        public static void Register<T>(IWProtoFormatter<T> formatter)
        {
            Cache<T>.Formatter = formatter;
            WProtoGeneric<T>.Reset();
        }

        /// <summary>
        /// Indicates whether a formatter is registered for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <returns><c>true</c> when <see cref="Get{T}"/> would return a formatter.</returns>
        public static bool IsRegistered<T>()
        {
            return Cache<T>.Formatter != null;
        }

        /// <summary>
        /// Gets the formatter for <typeparamref name="T"/> without throwing.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <param name="formatter">Receives the formatter, or <c>null</c> when none is registered.</param>
        /// <returns><c>true</c> when a formatter was found.</returns>
        public static bool TryGet<T>(out IWProtoFormatter<T> formatter)
        {
            formatter = Cache<T>.Formatter;
            return formatter != null;
        }

        /// <summary>
        /// Gets the formatter for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The message type.</typeparam>
        /// <returns>The registered formatter.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no formatter is registered, with a message naming the type and how to fix it.
        /// </exception>
        /// <remarks>
        /// This is the one place the serializer throws, and it is deliberate: the alternative for a
        /// missing closed generic under IL2CPP is an <c>ExecutionEngineException</c> from somewhere
        /// inside the runtime, which says nothing about which type is unannotated. Callers that
        /// would rather branch than catch have <see cref="TryGet{T}"/>.
        /// </remarks>
        public static IWProtoFormatter<T> Get<T>()
        {
            IWProtoFormatter<T> formatter = Cache<T>.Formatter;
            if (formatter != null)
            {
                return formatter;
            }

            throw new InvalidOperationException(
                $"No WallstopProto formatter is registered for '{typeof(T).FullName}'. "
                    + "Annotate the type with [WProtoContract] and its members with [WProtoMember] so a "
                    + "formatter is generated into the assembly that declares it, or register a "
                    + $"hand-written one with WProtoFormatterProvider.Register<{typeof(T).Name}>(...). "
                    + "Types this package ships formatters for register themselves as Unity starts; "
                    + "outside a Unity runtime, call WProtoBuiltInFormatters.RegisterAll() first."
            );
        }

        /// <summary>
        /// Builds the exception thrown when a value's runtime type is a subtype the contract does
        /// not declare.
        /// </summary>
        /// <param name="contract">The declared contract type.</param>
        /// <param name="actual">The value's runtime type.</param>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        /// <para>
        /// Returned rather than thrown so the call site reads
        /// <c>throw WProtoFormatterProvider.UnexpectedSubtype(…)</c> and the compiler treats the
        /// following code as unreachable.
        /// </para>
        /// <para>
        /// Refusing is the compatible behaviour, not a stricter one: protobuf-net raises
        /// <c>InvalidOperationException: Unexpected sub-type</c> on the same value, measured against
        /// 3.2.56 — an include must name a <b>direct</b> subtype, and a grandchild declared on the
        /// grandparent is rejected outright. The alternative is worse than an exception: the value
        /// would be written under its nearest declared ancestor's tag and read back as that ancestor,
        /// silently losing a level of type identity in saved data.
        /// </para>
        /// </remarks>
        public static Exception UnexpectedSubtype(Type contract, Type actual)
        {
            return new InvalidOperationException(
                $"WallstopProto cannot write a '{actual?.FullName}' as a '{contract?.FullName}': it "
                    + "is a subtype the contract does not declare. Add "
                    + $"[WProtoInclude(tag, typeof({actual?.Name}))] to its immediate base type — an "
                    + "include names a direct subtype, so a deeper type is declared on the type it "
                    + "actually derives from, not on the root."
            );
        }

        private static class Cache<T>
        {
            internal static IWProtoFormatter<T> Formatter;
        }
    }
}
