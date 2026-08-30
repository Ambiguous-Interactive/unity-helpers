// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.Scripting;

    /// <summary>
    /// Field numbers, or member names, that a removed <c>[WProtoMember]</c> used to hold and that
    /// nothing on this contract may take again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field number is a durable wire contract, and the declaration that spends one is deleted
    /// along with the member it sits on. <c>WPROTO002</c> refuses two members claiming one number at
    /// the same TIME and so has no memory: a number freed by a deletion is indistinguishable from
    /// one never used, and handing it to a later member reads every payload written by an older
    /// build as the wrong type. This is the record that makes the deletion visible, and it is the
    /// same mechanism <c>proto3</c> spells <c>reserved</c>.
    /// </para>
    /// <code>
    /// [WProtoContract]
    /// [WProtoReserved(3)]           // Health, removed in 4.0
    /// [WProtoReserved(7, 9)]        // several at once
    /// [WProtoReserved("Health")]    // and the name it went by
    /// public partial class Player { }
    /// </code>
    /// <para>
    /// Names are reserved as well as numbers, for the reason protobuf reserves both: a re-added
    /// <c>Health</c> at a different number still breaks anything that matches by name -- a JSON
    /// projection, a generated <c>.proto</c> consumer, a schema registry -- while carrying data that
    /// means something else.
    /// </para>
    /// <para>
    /// The record is an attribute rather than a generated manifest because a member number is always
    /// written by hand. Nothing assigns one, so the record belongs beside the contract where the
    /// next author is already reading, and a reservation that contradicts a live member is refused
    /// rather than silently outranking it.
    /// </para>
    /// </remarks>
    [Preserve]
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        AllowMultiple = true,
        Inherited = false
    )]
    public sealed class WProtoReservedAttribute : Attribute
    {
        private static readonly int[] NoNumbers = Array.Empty<int>();
        private static readonly string[] NoNames = Array.Empty<string>();

        /// <summary>
        /// Reserves one or more field numbers.
        /// </summary>
        /// <param name="fieldNumber">A field number no member may take again.</param>
        /// <param name="alsoReserved">Any further numbers to reserve in the same declaration.</param>
        /// <remarks>
        /// The first number is separate from the rest so that <c>[WProtoReserved()]</c> cannot
        /// compile. An empty reservation reads as a considered decision and records nothing, which
        /// is the state this attribute exists to prevent.
        /// </remarks>
        public WProtoReservedAttribute(int fieldNumber, params int[] alsoReserved)
        {
            int[] numbers = new int[1 + (alsoReserved == null ? 0 : alsoReserved.Length)];
            numbers[0] = fieldNumber;
            for (int index = 1; index < numbers.Length; index++)
            {
                numbers[index] = alsoReserved[index - 1];
            }

            FieldNumbers = numbers;
            MemberNames = NoNames;
        }

        /// <summary>
        /// Reserves one or more member names.
        /// </summary>
        /// <param name="memberName">A member name no member may take again.</param>
        /// <param name="alsoReserved">Any further names to reserve in the same declaration.</param>
        public WProtoReservedAttribute(string memberName, params string[] alsoReserved)
        {
            string[] names = new string[1 + (alsoReserved == null ? 0 : alsoReserved.Length)];
            names[0] = memberName;
            for (int index = 1; index < names.Length; index++)
            {
                names[index] = alsoReserved[index - 1];
            }

            MemberNames = names;
            FieldNumbers = NoNumbers;
        }

        /// <summary>The field numbers this declaration holds; empty when it reserves names.</summary>
        public IReadOnlyList<int> FieldNumbers { get; }

        /// <summary>The member names this declaration holds; empty when it reserves numbers.</summary>
        public IReadOnlyList<string> MemberNames { get; }
    }
}
