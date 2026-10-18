// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;

    public enum WProtoSchemaUnsigned32 : uint
    {
        [Obsolete("Choose a specific value.")]
        None = 0,
        Maximum = uint.MaxValue,
    }

    public enum WProtoSchemaSigned64 : long
    {
        [Obsolete("Choose a specific value.")]
        None = 0,
        Minimum = long.MinValue,
        Maximum = long.MaxValue,
    }

    public enum WProtoSchemaUnsigned64 : ulong
    {
        [Obsolete("Choose a specific value.")]
        None = 0,
        Maximum = ulong.MaxValue,
    }
}
