// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;

    /// <summary>A byte-backed map key matching the consumer's button kind.</summary>
    public enum ZeroMapKeyKind : byte
    {
        /// <summary>No key.</summary>
        [Obsolete("Use a defined key unless verifying the protobuf default.")]
        None = 0,

        /// <summary>A nondefault key.</summary>
        Other = 7,
    }
}
