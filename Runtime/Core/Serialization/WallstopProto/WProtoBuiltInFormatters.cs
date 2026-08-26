// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System.Threading;
    using DataStructure.Adapters;
    using Random;

    /// <summary>
    /// Registers the formatters this package ships for its own contracts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inside Unity nothing has to call this: <c>WProtoBootstrap</c> runs it at
    /// <c>SubsystemRegistration</c> and on editor domain load. It is public for the cases that sit
    /// outside Unity's startup -- a plain <c>dotnet</c> test harness, or a tool that loads the
    /// package's assembly directly -- and because calling it more than once is harmless.
    /// </para>
    /// <para>
    /// Every registration here is a direct call naming a closed generic the compiler emits, so
    /// there is no assembly scan, no <c>MakeGenericType</c>, and nothing an AOT compiler cannot see.
    /// The hazard this serializer exists to avoid is <i>discovery</i> -- reflecting over loaded
    /// assemblies to find implementations of <see cref="IWProtoFormatter{T}"/> and closing generics
    /// at runtime -- not registration itself. When the source generator lands it will emit the
    /// equivalent of this method per assembly, including into a consumer's own assembly for a
    /// consumer's own contracts, so nobody maintains this list by hand.
    /// </para>
    /// <para>
    /// A consumer that deliberately replaces one of these formatters registers theirs from any
    /// phase after <c>SubsystemRegistration</c>, since last registration wins.
    /// </para>
    /// </remarks>
    public static class WProtoBuiltInFormatters
    {
        private static readonly object RegistrationGate = new object();

        private static bool _registered;

        /// <summary>
        /// Registers every built-in formatter, once.
        /// </summary>
        /// <remarks>
        /// The flag is published <b>after</b> the registrations and read through
        /// <see cref="Volatile"/>. Setting it first left a window where a second thread saw
        /// "already registered" and serialized against a provider that had none of these eight yet,
        /// which surfaces as "no formatter is registered for FastVector2Int" from a type this
        /// package ships a formatter for.
        /// </remarks>
        public static void RegisterAll()
        {
            if (Volatile.Read(ref _registered))
            {
                return;
            }

            lock (RegistrationGate)
            {
                if (_registered)
                {
                    return;
                }

                WProtoFormatterProvider.Register(FastVector2Int.WProtoFormatter.Instance);
                WProtoFormatterProvider.Register(FastVector3Int.WProtoFormatter.Instance);
                WProtoFormatterProvider.Register(WGuid.WProtoFormatter.Instance);
                WProtoFormatterProvider.Register(RandomState.WProtoFormatter.Instance);
                WProtoBcl.RegisterAll();

                Volatile.Write(ref _registered, true);
            }
        }
    }
}
