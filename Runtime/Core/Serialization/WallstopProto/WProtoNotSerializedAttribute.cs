// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// Records that a subclass of a <see cref="WProtoContractAttribute"/> is deliberately never
    /// serialized, so <c>WPROTO044</c> stops asking it to declare a subtype relationship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A contract that is neither sealed nor a value type carries a closing guard in its dispatch
    /// chain, so an instance of any subclass the contract does not declare throws
    /// <c>UnexpectedSubtype</c> the first time it reaches the serializer. Deriving from a
    /// serializable base without wanting the subclass on the wire is an ordinary thing to do -- a
    /// presentation-only variant, a test double, an editor-only subclass -- and it is only wrong
    /// when an instance actually reaches the serializer.
    /// </para>
    /// <para>
    /// This is how that decision is recorded where the next reader is already looking, rather than
    /// inferred from the absence of an attribute. It is a statement about this type alone, not
    /// about its descendants: a subclass of an opted-out type has no declared ancestor between it
    /// and the contract either, so nothing writes it as the contract and nothing asks it to.
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
