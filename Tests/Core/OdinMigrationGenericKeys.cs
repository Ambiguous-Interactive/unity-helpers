// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;

    /// <summary>Supplies nested generic enum metadata for migration binding checks.</summary>
    public static class OdinMigrationGenericKeys<T>
    {
        /// <summary>Identifies a retained dictionary entry.</summary>
        public enum Key
        {
            [Obsolete("Use Value for a populated entry.")]
            None = 0,
            Value = 1,
        }
    }
}
