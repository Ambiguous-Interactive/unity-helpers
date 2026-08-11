// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Resolves the formatter that serves a type when it is the <b>root</b> of a serialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately a second registry rather than more entries in
    /// <see cref="WProtoFormatterProvider"/>, and the separation is load-bearing. That provider is
    /// consulted from member positions too -- <see cref="WProtoGeneric{T}"/> asks it for every field
    /// whose type is only known once a generic contract is closed -- so a marshal registered there
    /// would leak out of the root and rewrite the encoding of any member that happened to close at
    /// one of these types. A registry the member path cannot see makes "root only" a property of the
    /// design instead of a rule someone has to remember.
    /// </para>
    /// <para>
    /// The lookup is the same static field on a closed generic that
    /// <see cref="WProtoFormatterProvider"/> uses, so it costs a field read, needs no
    /// <c>Type</c>-keyed dictionary, and is compiled ahead of time by IL2CPP like any other generic
    /// call.
    /// </para>
    /// </remarks>
    public static class WProtoRootMarshalProvider
    {
        /// <summary>
        /// Registers <paramref name="formatter"/> as the root formatter for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type being marshalled.</typeparam>
        /// <param name="formatter">The formatter, or <c>null</c> to clear the registration.</param>
        /// <remarks>
        /// <para>
        /// Last registration wins, matching <see cref="WProtoFormatterProvider.Register{T}"/>.
        /// Unlike the built-in contract formatters, though, this package's marshals are registered
        /// from a <b>generated</b> registrar at
        /// <c>RuntimeInitializeLoadType.BeforeSceneLoad</c> -- the same phase a consumer's generated
        /// registrar runs in -- and Unity does not order those across assemblies. So replacing one
        /// this package ships is not a guarantee: do it from a later phase, or from a
        /// <c>[RuntimeInitializeOnLoadMethod]</c> of your own, if the override has to hold.
        /// </para>
        /// <para>
        /// Registration is not thread-safe against concurrent resolution and is meant to run once
        /// during startup, before anything serializes.
        /// </para>
        /// </remarks>
        public static void Register<T>(IWProtoFormatter<T> formatter)
        {
            Cache<T>.Formatter = formatter;
        }

        /// <summary>
        /// Indicates whether a root marshal is registered for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The type being marshalled.</typeparam>
        /// <returns><c>true</c> when <see cref="TryGet{T}"/> would return a formatter.</returns>
        public static bool IsRegistered<T>()
        {
            return Cache<T>.Formatter != null;
        }

        /// <summary>
        /// Gets the root formatter for <typeparamref name="T"/> without throwing.
        /// </summary>
        /// <typeparam name="T">The type being marshalled.</typeparam>
        /// <param name="formatter">Receives the formatter, or <c>null</c> when none is registered.</param>
        /// <returns><c>true</c> when one was found.</returns>
        public static bool TryGet<T>(out IWProtoFormatter<T> formatter)
        {
            formatter = Cache<T>.Formatter;
            return formatter != null;
        }

        private static class Cache<T>
        {
            internal static IWProtoFormatter<T> Formatter;
        }
    }
}
