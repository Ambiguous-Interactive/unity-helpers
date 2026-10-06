// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

#pragma warning disable WPROTO030 // This fixture deliberately exercises the legacy consumer contract.
    [ProtoContract(IgnoreListHandling = true)]
    public sealed class LegacyComparerContractDictionary : SerializableDictionary<string, int>
    {
        [ProtoMember(7)]
        public int Revision;
    }
#pragma warning restore WPROTO030
}
