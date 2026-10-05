// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
#if UNITY_EDITOR
    using System;

    internal sealed class DropDownFormattableDisplayText : DropDownDisplayText, IFormattable
    {
        internal DropDownFormattableDisplayText(string text)
            : base(text) { }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            return ToString();
        }
    }
#endif
}
