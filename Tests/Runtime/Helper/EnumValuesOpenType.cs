// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;

    internal sealed class EnumValuesOpenType<T>
    {
        public enum Nested
        {
            [Obsolete("Reserved zero value.")]
            Unknown = 0,
            First = 1,
        }
    }
}
