// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    internal sealed class WProtoRefusingSurrogateFormatter : SurrogateMarshalFormatter<int, string>
    {
        internal WProtoRefusingSurrogateFormatter(IWProtoFormatter<string> formatter)
            : base(formatter) { }

        protected override string ToSurrogate(in int value)
        {
            return string.Empty;
        }

        protected override int FromSurrogate(in string surrogate)
        {
            return 42;
        }
    }
}
