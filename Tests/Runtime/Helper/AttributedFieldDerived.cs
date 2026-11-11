// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    public class AttributedFieldDerived : AttributedFieldBase
    {
        [RuntimeMarker]
        public new int sharedValue;
        public int unmarkedValue;
    }
}
