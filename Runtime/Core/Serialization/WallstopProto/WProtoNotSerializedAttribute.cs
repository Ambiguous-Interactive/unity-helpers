// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// Records that a subclass of a <see cref="WProtoContractAttribute"/> is deliberately never
    /// serialized, so no formatter is generated for it and no field number is spent on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deriving from a contract IS the declaration, so a subclass is serialized by default: it
    /// joins the base's dispatch chain and the assigner commits a field number for it. Deriving
    /// without wanting the subclass on the wire is an ordinary thing to do -- a presentation-only
    /// variant, a test double, an editor-only subclass -- and this is how that decision is recorded
    /// where the next reader is already looking.
    /// </para>
    /// <para>
    /// It is a statement about this type alone, and it stops the walk: a subclass of an opted-out
    /// type has no serialized ancestor between it and the contract either, so nothing writes it as
    /// the contract and nothing generates for it.
    /// </para>
    /// <para>
    /// A promise rather than an enforcement. A contract that is neither sealed nor a value type
    /// carries a closing guard in its dispatch chain, so a value that reaches the serializer anyway
    /// throws <c>UnexpectedSubtype</c> rather than being written as its base -- which would lose a
    /// level of type identity from saved data with nothing to report it.
    /// </para>
    /// <code>
    /// [WProtoContract]
    /// [WProtoInclude(100, typeof(Melee))]
    /// public partial class Weapon { [WProtoMember(1)] public int Damage; }
    ///
    /// [WProtoNotSerialized]                       // never reaches the serializer
    /// public sealed class PreviewWeapon : Weapon { public float Charge; }
    /// </code>
    /// <para>
    /// It does not silence a type that also carries <see cref="WProtoContractAttribute"/> or
    /// <see cref="WProtoSubtypeAttribute"/>: those declare the opposite intent, and the two
    /// together are a contradiction rather than a suppression.
    /// </para>
    /// </remarks>
    [Preserve]
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct,
        AllowMultiple = false,
        Inherited = false
    )]
    public sealed class WProtoNotSerializedAttribute : Attribute { }
}
