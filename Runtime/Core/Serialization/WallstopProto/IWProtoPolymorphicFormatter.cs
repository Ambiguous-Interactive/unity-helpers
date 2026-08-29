// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Reports which runtime types a formatter's subtype dispatch chain can write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by <see cref="WProtoFacade"/> when a value's runtime type is not the declared one, which
    /// is the ordinary case for a polymorphic member: this package documents declaring a generator as
    /// <c>AbstractRandom</c>, so the declared type is the abstract base and the runtime type is one of
    /// seventeen subtypes. Without an answer the facade could only serve an exact type match, and
    /// every realistic call fell through to protobuf-net -- the path that cannot run under IL2CPP.
    /// </para>
    /// <para>
    /// The alternative was a registry keyed by <see cref="Type"/> mapping each subtype to its base.
    /// This is exact instead of merely parallel: the generated implementation is the same chain of
    /// type tests <c>Write</c> dispatches on, so the two cannot drift, and a subtype the chain refuses
    /// is one this reports as unwritable rather than one that throws halfway through a write.
    /// </para>
    /// <para>
    /// Deliberately <b>separate</b> from <see cref="IWProtoFormatter{T}"/> and non-generic. A
    /// hand-written formatter describes one type and needs nothing here; not implementing this
    /// interface means "exactly my declared type", which is what such a formatter does. Adding a
    /// member to <see cref="IWProtoFormatter{T}"/> instead would break every one already written.
    /// </para>
    /// </remarks>
    public interface IWProtoPolymorphicFormatter
    {
        /// <summary>
        /// Reports whether this formatter writes a value whose runtime type is
        /// <paramref name="runtimeType"/>.
        /// </summary>
        /// <param name="runtimeType">The value's runtime type.</param>
        /// <returns>
        /// <c>true</c> when the dispatch chain has a branch for it -- the declared type itself, or a
        /// subtype declared at every level between them, with <c>[WProtoInclude]</c> on each base or
        /// <c>[WProtoSubtype]</c> on each subtype.
        /// </returns>
        /// <remarks>
        /// <c>false</c> for a subtype no include names. That value has no encoding here: writing it
        /// under its nearest declared ancestor's tag would read back as that ancestor, losing a level
        /// of type identity in saved data, so the generated code refuses it by name and the facade
        /// hands it to protobuf-net instead.
        /// </remarks>
        bool CanWrite(Type runtimeType);
    }
}
