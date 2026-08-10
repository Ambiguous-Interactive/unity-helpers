// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// The pieces of repeated-field handling that would otherwise be duplicated into every generated
    /// formatter.
    /// </summary>
    /// <remarks>
    /// A repeated field is not a collection on the wire; it is a run of ordinary fields that happen
    /// to share a number. That has two consequences generated code has to encode. There is no
    /// encoding for an absent element in the middle of the run, which is why
    /// <see cref="NullElement"/> exists, and there is no encoding that distinguishes an empty
    /// collection from a missing one, which is why an empty collection reads back as whatever the
    /// constructor left behind.
    /// </remarks>
    public static class WProtoRepeated
    {
        /// <summary>
        /// Builds the exception thrown when a repeated member holds a <c>null</c> element.
        /// </summary>
        /// <param name="contract">The contract type's name, for the message.</param>
        /// <param name="member">The member's name, for the message.</param>
        /// <param name="elementType">The element type's name, for the message.</param>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        /// <para>
        /// Returned rather than thrown so the call site reads <c>throw WProtoRepeated.NullElement(…)</c>
        /// and the compiler treats the following code as unreachable.
        /// </para>
        /// <para>
        /// Refusing is the compatible behaviour, not a stricter one: protobuf-net raises
        /// <see cref="NullReferenceException"/> on the same input ("An element of type … was null;
        /// this might be as contents in a list/array"), measured against 3.2.56. The alternatives are
        /// both silent data changes -- writing a null <c>string</c> element as an empty one, or
        /// dropping the element and shortening the collection.
        /// </para>
        /// </remarks>
        public static Exception NullElement(string contract, string member, string elementType)
        {
            return new InvalidOperationException(
                $"'{contract}.{member}' holds a null '{elementType}' element. A repeated field is a "
                    + "run of same-numbered fields on the wire, and there is no encoding for an absent "
                    + "value inside one -- writing it would either invent an empty value or silently "
                    + "shorten the collection. Remove the null element, or make the member a message "
                    + "with its own presence flag."
            );
        }
    }
}
