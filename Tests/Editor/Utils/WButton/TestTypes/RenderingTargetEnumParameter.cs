// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetEnumParameter : ScriptableObject
    {
        public TestButtonEnum LastValue;

        [WButton]
        public void EnumButton(TestButtonEnum option)
        {
            LastValue = option;
        }

        [WButton]
        public void EnumWithDefault(TestButtonEnum option = TestButtonEnum.OptionB)
        {
            LastValue = option;
        }
    }
}
#endif
