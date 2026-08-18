// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Decodes a message into a value the caller already holds, instead of into a fresh one.
    /// </summary>
    /// <typeparam name="T">The message type this formatter handles.</typeparam>
    /// <remarks>
    /// <para>
    /// protobuf defines reading a sub-message field as <c>Message::MergeFrom</c>, and protobuf-net
    /// implements that literally: the <b>first</b> occurrence of a sub-message field is merged into
    /// whatever the enclosing contract's constructor already left on the member. Measured against
    /// 3.2.56, a member seeded to <c>{A = 9}</c> plus a payload setting only <c>B</c> reads back as
    /// <c>{A = 9, B = 2}</c>. Decoding into a fresh instance instead drops the seed, which is silent
    /// data loss for any contract whose author gave a member a starting value.
    /// </para>
    /// <para>
    /// This is separate from <see cref="IWProtoFormatter{T}"/> rather than part of it, because that
    /// interface is public and hand-implementable and the design promises it will not move under a
    /// consumer. A formatter that does not implement this one still reads correctly; its members
    /// simply replace their seed, which is what every formatter did before.
    /// </para>
    /// <para>
    /// A generated formatter implements this whenever it has an instance to merge into. A contract
    /// built by a constructor at the end of the read -- one with <c>readonly</c> members -- an
    /// abstract or <c>[WProtoInclude]</c> contract whose instance the payload chooses, and one
    /// declaring <c>SkipConstructor</c> all do not: the first two have no instance to seed from, and
    /// the third is standing in for an uninitialized allocation that protobuf-net gives no seed
    /// either.
    /// </para>
    /// </remarks>
    public interface IWProtoMergeFormatter<T>
    {
        /// <summary>
        /// Reads a value from <paramref name="reader"/>, merging it into <paramref name="seed"/>.
        /// </summary>
        /// <param name="reader">A reader scoped to this message's payload.</param>
        /// <param name="seed">
        /// The value the member already holds, or <c>default</c> when it holds none. A member the
        /// payload does not mention keeps the seed's value; one it does mention takes the payload's.
        /// </param>
        /// <param name="value">Receives the decoded value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a complete value was read.</returns>
        /// <remarks>
        /// Every guarantee of <see cref="IWProtoFormatter{T}.TryRead"/> holds here unchanged,
        /// lifecycle hooks included: this is one read of one value, so each hook runs once.
        /// </remarks>
        bool TryReadInto(ref WProtoReader reader, in T seed, out T value);
    }
}
