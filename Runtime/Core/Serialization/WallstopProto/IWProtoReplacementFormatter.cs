// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>Replaces one contract level with a complete generated subtype chain.</summary>
    /// <typeparam name="T">The extended contract.</typeparam>
    public interface IWProtoReplacementFormatter<T>
        : IWProtoFormatter<T>,
            IWProtoPolymorphicFormatter { }
}
