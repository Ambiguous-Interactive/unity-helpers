// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Disambiguates the constructor a generated formatter uses to build a contract whose members
    /// cannot be assigned after construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>readonly</c> field can only be assigned by a constructor of its declaring type. The
    /// generated formatter is a <b>nested</b> type, which is not enough — but the generator also
    /// reopens the contract as <c>partial</c>, and a constructor emitted there is a constructor of
    /// the declaring type. So a contract can keep its immutability and still be read, with no change
    /// to its public surface.
    /// </para>
    /// <para>
    /// This marker is the first parameter of that constructor purely so it cannot collide with one
    /// the author already wrote. A contract with fields <c>(int, int)</c> very plausibly has an
    /// <c>(int, int)</c> constructor of its own; none of them has one taking a
    /// <see cref="WProtoConstruct"/>.
    /// </para>
    /// </remarks>
    public readonly struct WProtoConstruct { }
}
