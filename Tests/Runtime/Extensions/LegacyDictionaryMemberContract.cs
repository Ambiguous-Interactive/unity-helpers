// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using ProtoBuf;

#pragma warning disable WPROTO030 // This fixture deliberately exercises a legacy consumer map member.
    [ProtoContract]
    public sealed class LegacyDictionaryMemberContract<TMap>
    {
        [ProtoMember(1)]
        public int Revision;

        [ProtoMember(7)]
        public TMap Entries;
    }
#pragma warning restore WPROTO030
}
