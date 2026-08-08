// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Reads and writes one message type with no runtime reflection.
    /// </summary>
    /// <typeparam name="T">The message type this formatter handles.</typeparam>
    /// <remarks>
    /// <para>
    /// A formatter is normally generated, but the interface is public and hand-implementable
    /// because consumer code needs both: a game's own <c>[WProtoContract]</c> types get formatters
    /// generated into the game's assembly, and a type the generator cannot see -- one from a
    /// third-party assembly, say -- can be supported by writing one of these by hand and
    /// registering it. Nothing here is reserved to this package.
    /// </para>
    /// <para>
    /// Implementations must not throw. A malformed payload is reported by returning
    /// <see langword="false"/>, which is what lets a caller distinguish corrupt save data from a
    /// crash. <see cref="Measure"/> must return exactly the byte count <see cref="Write"/> produces
    /// for the same value, because a parent message emits that number as a length prefix before the
    /// payload exists; a formatter whose measurement disagrees with its output corrupts every
    /// message that contains it.
    /// </para>
    /// <para>
    /// <b>Hook ordering.</b> A formatter that handles a type carrying lifecycle hooks must invoke
    /// them in exactly this order, and exactly once each:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <see cref="WProtoBeforeSerializationAttribute"/> -- first statement of <see cref="Measure"/>,
    /// and <b>not</b> repeated in <see cref="Write"/>. A type that projects its live state into its
    /// serialized members must have done so before it is measured, or the length prefix will not
    /// match the payload. This makes <see cref="Measure"/> stateful rather than pure, and makes
    /// "measure exactly once, immediately before each write" part of the contract -- which the
    /// length-prefix protocol already requires, since a prefix cannot be emitted without it. A hook
    /// that rents pooled scratch would leak if it also ran from <see cref="Write"/>.
    /// </description></item>
    /// <item><description>
    /// <see cref="WProtoAfterSerializationAttribute"/> -- last statement of <see cref="Write"/>, so
    /// whatever the before-serialization hook staged is released once the bytes are out.
    /// </description></item>
    /// <item><description>
    /// <see cref="WProtoBeforeDeserializationAttribute"/> -- after the instance is created and
    /// before any member is assigned.
    /// </description></item>
    /// <item><description>
    /// <see cref="WProtoAfterDeserializationAttribute"/> -- after the last member is assigned, and
    /// only on a successful read. A failed read returns without running it, because a hook that
    /// rebuilds derived state from half-populated members produces a plausible-looking wrong object
    /// instead of a reported failure.
    /// </description></item>
    /// </list>
    /// </remarks>
    public interface IWProtoFormatter<T>
    {
        /// <summary>
        /// Returns the exact number of bytes <see cref="Write"/> will produce for <paramref name="value"/>.
        /// </summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>The encoded byte count, excluding any tag or length prefix the parent adds.</returns>
        int Measure(in T value);

        /// <summary>
        /// Writes <paramref name="value"/>'s fields, without a tag or length prefix.
        /// </summary>
        /// <param name="writer">The destination writer.</param>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the whole value was written.</returns>
        bool Write(ref WProtoWriter writer, in T value);

        /// <summary>
        /// Reads a value from <paramref name="reader"/> until its input is exhausted.
        /// </summary>
        /// <param name="reader">A reader scoped to this message's payload.</param>
        /// <param name="value">Receives the decoded value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a complete value was read.</returns>
        bool TryRead(ref WProtoReader reader, out T value);
    }
}
