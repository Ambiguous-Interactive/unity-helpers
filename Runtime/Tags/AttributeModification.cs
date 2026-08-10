// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Text.Json.Serialization;
    using Core.Extension;
    using Core.Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Declarative change applied to an <see cref="Attribute"/>.
    /// Each instance represents a single operation (add, multiply, or override) referenced by an <see cref="AttributeEffect"/>.
    /// </summary>
    /// <remarks>
    /// <para>Key properties:</para>
    /// <list type="bullet">
    /// <item><description>Non-destructive: temporary handles can add/remove modifications without mutating base values.</description></item>
    /// <item><description>Deterministic ordering: <see cref="Attribute"/> always processes Addition, then Multiplication, then Override.</description></item>
    /// <item><description>Flexible authoring: supports both instant (permanent) and duration-based effects.</description></item>
    /// </list>
    /// <para>Stack processing order:</para>
    /// <list type="number">
    /// <item><description>Addition (value += x)</description></item>
    /// <item><description>Multiplication (value *= x)</description></item>
    /// <item><description>Override (value = x)</description></item>
    /// </list>
    /// <para>
    /// The <see cref="attribute"/> field must match an <see cref="Attribute"/> field on the target <see cref="AttributesComponent"/>.
    /// The Attribute Metadata Cache generator can provide editor dropdowns to avoid typos. Unknown names are ignored at runtime.
    /// </para>
    /// <para>Sample definitions:</para>
    /// <code>
    /// // +50 flat health
    /// new AttributeModification { attribute = "Health", action = ModificationAction.Addition, value = 50f };
    ///
    /// // +50% speed
    /// new AttributeModification { attribute = "Speed", action = ModificationAction.Multiplication, value = 1.5f };
    ///
    /// // Hard-set defense to 0
    /// new AttributeModification { attribute = "Defense", action = ModificationAction.Override, value = 0f };
    /// </code>
    /// <para>Authoring tips:</para>
    /// <list type="bullet">
    /// <item><description>Use Addition for flat buffs/debuffs; Multiplication for percentage-style adjustments.</description></item>
    /// <item><description>Reserve Override for hard clamps (it always executes last).</description></item>
    /// <item><description>Negative Addition subtracts; Multiplication values below 1.0 reduce the attribute.</description></item>
    /// </list>
    /// </remarks>
    [Serializable]
    [ProtoContract]
    [WProtoContract]
    public partial struct AttributeModification
        : IEquatable<AttributeModification>,
            IComparable<AttributeModification>,
            IComparable
    {
        /// <summary>
        /// The name of the attribute to modify. This should match a field name in an <see cref="AttributesComponent"/> subclass.
        /// </summary>
        [StringInList(typeof(AttributeUtilities), nameof(AttributeUtilities.GetAllAttributeNames))]
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string attribute;

        /// <summary>
        /// The type of modification action to perform (Addition, Multiplication, or Override).
        /// </summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public ModificationAction action;

        /// <summary>
        /// The value to use for the modification. Interpretation depends on the <see cref="action"/>:
        /// <para>- Addition: The amount to add (can be negative for subtraction)</para>
        /// <para>- Multiplication: The multiplier to apply (e.g., 1.5 for +50%, 0.5 for -50%)</para>
        /// <para>- Override: The new absolute value to set</para>
        /// </summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float value;

        [JsonConstructor]
        public AttributeModification(string attribute, ModificationAction action, float value)
        {
            this.attribute = attribute;
            this.action = action;
            this.value = value;
        }

        /// <summary>
        /// Converts this modification to a JSON string representation.
        /// </summary>
        /// <returns>A JSON string representing this modification.</returns>
        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            if (obj is AttributeModification other)
            {
                return CompareTo(other);
            }

            return -1;
        }

        public int CompareTo(AttributeModification other)
        {
            return ((int)action).CompareTo((int)other.action);
        }

        /// <summary>
        /// Determines whether two attribute modifications are not equal.
        /// </summary>
        /// <param name="lhs">The first modification to compare.</param>
        /// <param name="rhs">The second modification to compare.</param>
        /// <returns><c>true</c> if the modifications are not equal; otherwise, <c>false</c>.</returns>
        public static bool operator !=(AttributeModification lhs, AttributeModification rhs)
        {
            return !(lhs == rhs);
        }

        /// <summary>
        /// Determines whether two attribute modifications are equal.
        /// </summary>
        /// <param name="lhs">The first modification to compare.</param>
        /// <param name="rhs">The second modification to compare.</param>
        /// <returns><c>true</c> if the modifications are equal; otherwise, <c>false</c>.</returns>
        public static bool operator ==(AttributeModification lhs, AttributeModification rhs)
        {
            return lhs.Equals(rhs);
        }

        /// <summary>
        /// Determines whether this modification equals the specified object.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns><c>true</c> if the object is an AttributeModification with equal values; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return obj is AttributeModification other && Equals(other);
        }

        /// <summary>
        /// Determines whether this modification equals another modification by comparing all fields.
        /// </summary>
        /// <param name="other">The modification to compare with.</param>
        /// <returns><c>true</c> if all fields are equal; otherwise, <c>false</c>.</returns>
        public bool Equals(AttributeModification other)
        {
            return string.Equals(attribute, other.attribute, StringComparison.Ordinal)
                && action == other.action
                && value.Equals(other.value);
        }

        /// <summary>
        /// Returns the hash code for this modification.
        /// </summary>
        /// <returns>A hash code combining the attribute name, action, and value.</returns>
        public override int GetHashCode()
        {
            return Objects.HashCode(attribute, action, value);
        }
    }
}
