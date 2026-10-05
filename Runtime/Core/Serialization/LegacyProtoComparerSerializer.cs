// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if !WALLSTOP_PROTO_ONLY
    using System.Collections.Generic;
    using System.IO;
    using ProtoBuf;
    using ProtoBuf.Meta;
    using ProtoBuf.Serializers;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using PbSerializer = ProtoBuf.Serializer;

    internal static class LegacyProtoComparerSerializer
    {
        internal static void Serialize<T>(Stream destination, T value, bool useRuntimeType)
        {
            if (
                ProtoEqualityComparer<T>.IsReferenceType
                && value is ILegacyProtobufMap map
                && map.TrySerialize(destination)
            )
            {
                return;
            }

            if (useRuntimeType)
            {
                PbSerializer.NonGeneric.Serialize(destination, value);
            }
            else
            {
                PbSerializer.Serialize(destination, value);
            }
        }

        internal static void SerializeMap<TCollection, TKey, TValue>(
            Stream destination,
            TCollection value
        )
            where TCollection : IDictionary<TKey, TValue>
        {
            ProtoWriter.State writer = ProtoWriter.State.Create(
                destination,
                RuntimeTypeModel.Default
            );
            try
            {
                writer.SerializeRoot(
                    value,
                    MapSerializer.CreateDictionary<TCollection, TKey, TValue>()
                );
                writer.Close();
            }
            finally
            {
                writer.Dispose();
            }
        }
    }
#endif
}
