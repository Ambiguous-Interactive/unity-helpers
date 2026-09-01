// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetSameGroup : ScriptableObject
    {
        [WButton(groupName: "TestGroup")]
        public void Button1() { }

        [WButton(groupName: "TestGroup")]
        public void Button2() { }

        [WButton(groupName: "TestGroup")]
        public void Button3() { }
    }
}
#endif
