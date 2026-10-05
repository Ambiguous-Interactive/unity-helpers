// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes
{
#if UNITY_EDITOR
    internal class DropDownDisplayText
    {
        private readonly string _text;

        internal DropDownDisplayText(string text)
        {
            _text = text;
        }

        public override string ToString()
        {
            return _text;
        }
    }
#endif
}
