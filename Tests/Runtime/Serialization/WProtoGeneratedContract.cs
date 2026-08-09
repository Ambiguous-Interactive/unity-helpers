// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A contract whose formatter is written by the shipped Roslyn generator, not by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This type exists to make every Unity leg a gate on the generator. The generator's own test
    /// suite runs under a modern <c>dotnet</c> SDK; Unity compiles with its own bundled Roslyn --
    /// 3.9 on 2021.3, 4.x from 2022.2 -- and then IL2CPP compiles the result ahead of time. An
    /// analyzer that fails to load, or emits code Unity's compiler rejects, or emits code IL2CPP
    /// cannot AOT-compile, shows up here and nowhere else.
    /// </para>
    /// <para>
    /// The members deliberately cover the shapes with the least obvious encodings: a negative
    /// integer, which is written sign-extended across ten bytes; a string, which is written when
    /// empty and omitted when null; and a <c>Nullable</c>, whose presence is decided by
    /// <c>HasValue</c> rather than by comparison against a default.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoGeneratedContract
    {
        /// <summary>A signed integer, tagged first.</summary>
        [WProtoMember(1)]
        public int Count;

        /// <summary>A string, to pin the empty-is-not-absent rule.</summary>
        [WProtoMember(2)]
        public string Label;

        /// <summary>A nullable double, to pin presence by <c>HasValue</c>.</summary>
        [WProtoMember(3)]
        public double? Ratio;

        /// <summary>Set by the after-deserialization hook, never read from the wire.</summary>
        public bool Rebuilt;

        [WProtoAfterDeserialization]
        private void OnAfterDeserialization()
        {
            Rebuilt = true;
        }
    }
}
