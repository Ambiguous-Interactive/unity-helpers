// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// A formatter that can be registered for a type it is not always able to serve.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A generated contract formatter is registered only for a type someone annotated, so it can
    /// always answer. A <b>root marshal</b> cannot: it is registered for every closed construction
    /// of a collection found anywhere in source, whatever the element type is, and an element
    /// WallstopProto has no formatter for cannot be encoded at all — a type protobuf-net reaches
    /// through a surrogate, or an enum, both of which are resolved when a contract is generated and
    /// not when a generic wrapper is closed.
    /// </para>
    /// <para>
    /// Without this the facade would claim such a request and fail inside <c>Measure</c>, where the
    /// code it replaced fell through to protobuf-net and round-tripped. So the question is asked
    /// <b>before</b> anything is written and before any hook runs, and a <c>false</c> means "not
    /// mine" rather than "broken" — exactly what the per-type opt-in promises.
    /// </para>
    /// <para>
    /// A formatter that does not implement this is always asked to serve, which is the right default
    /// for every hand-written and generated formatter in this package.
    /// </para>
    /// </remarks>
    public interface IWProtoConditionalFormatter
    {
        /// <summary>
        /// Reports whether this formatter can encode and decode the type it is registered for.
        /// </summary>
        /// <returns><c>true</c> when the request is this formatter's to answer.</returns>
        /// <remarks>
        /// Must not depend on the value being serialized. An empty collection would otherwise be
        /// served while a populated one is refused, and the empty payload would be written by a
        /// serializer that cannot read it back once an element exists.
        /// </remarks>
        bool CanServe();
    }
}
