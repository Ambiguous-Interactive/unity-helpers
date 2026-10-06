// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Core.Helper
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    public enum EnumDisplayLabelSample
    {
        [Obsolete("Use a specific display label sample instead of Unknown.")]
        Unknown = 0,

        [InspectorName(null)]
        NullLabel = 1,

        [InspectorName("")]
        EmptyLabel = 2,

        [InspectorName("   ")]
        SpaceLabel = 3,

        [InspectorName("\t\r\n")]
        ControlLabel = 4,

        [InspectorName("\u00a0\u2003\u202f")]
        UnicodeSpaceLabel = 5,

        [InspectorName("  Custom Label  ")]
        PaddedLabel = 6,

        [InspectorName("\u200b")]
        ZeroWidthLabel = 7,

        [InspectorName("Custom Label")]
        DecoratedLabel = 8,
    }
#endif
}
