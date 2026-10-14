// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    internal sealed class DecliningProtoFormatter<T>
        : IWProtoFormatter<T>,
            IWProtoConditionalFormatter
    {
        public bool CanServe()
        {
            return false;
        }

        public int Measure(in T value)
        {
            return 0;
        }

        public bool Write(ref WProtoWriter writer, in T value)
        {
            return false;
        }

        public bool TryRead(ref WProtoReader reader, out T value)
        {
            value = default;
            return false;
        }
    }
}
