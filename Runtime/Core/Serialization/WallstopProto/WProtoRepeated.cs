// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Collections.Generic;

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
        /// Makes room in <paramref name="destination"/> for <paramref name="additional"/> more
        /// elements in one allocation.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="destination">The list about to be filled; <c>null</c> is ignored.</param>
        /// <param name="additional">
        /// How many elements are about to be added, from
        /// <see cref="WProtoReader.CountPackedElements"/>.
        /// </param>
        /// <remarks>
        /// <para>
        /// A list that grows from empty leaves every intermediate buffer behind: decoding 128
        /// <c>int</c>s into one allocated 1,208 bytes to produce a 592-byte graph. Sizing it once
        /// from the count already encoded in the packed run removes all of it.
        /// </para>
        /// <para>
        /// A hint rather than a requirement -- a count of zero, or a run whose count is unknown
        /// because protobuf-net wrote it unpacked, leaves the list to grow exactly as it did before.
        /// The capacity is only ever raised, so a caller's list that is already large enough is left
        /// alone rather than reallocated smaller.
        /// </para>
        /// </remarks>
        public static void Reserve<T>(List<T> destination, int additional)
        {
            if (destination == null || additional <= 0)
            {
                return;
            }

            int required = destination.Count + additional;
            if (destination.Capacity < required)
            {
                destination.Capacity = required;
            }
        }

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

        /// <summary>
        /// Builds the exception thrown when a <b>nested</b> collection holds a <c>null</c> element.
        /// </summary>
        /// <param name="contract">The contract type's name, for the message.</param>
        /// <param name="collectionType">The nested collection's type name, for the message.</param>
        /// <param name="elementType">The element type's name, for the message.</param>
        /// <returns>The exception to throw.</returns>
        /// <remarks>
        /// The same refusal as <see cref="NullElement"/>, for the same reason, and separate only
        /// because of what it can truthfully name. A nested collection is encoded by one wrapper
        /// message per inner type, and one wrapper serves every member of the contract that holds
        /// that type -- so naming a member here would name whichever one the generator happened to
        /// resolve first. The type is what all of them share.
        /// </remarks>
        public static Exception NullNestedElement(
            string contract,
            string collectionType,
            string elementType
        )
        {
            return new InvalidOperationException(
                $"A nested '{collectionType}' inside '{contract}' holds a null '{elementType}' "
                    + "element. A repeated field is a run of same-numbered fields on the wire, and "
                    + "there is no encoding for an absent value inside one -- writing it would "
                    + "either invent an empty value or silently shorten the collection. Remove the "
                    + "null element, or make the element a message with its own presence flag."
            );
        }
    }
}
