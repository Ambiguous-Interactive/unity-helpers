// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetManyButtons : ScriptableObject
    {
        [WButton(groupName: "ManyGroup")]
        public void Button1() { }

        [WButton(groupName: "ManyGroup")]
        public void Button2() { }

        [WButton(groupName: "ManyGroup")]
        public void Button3() { }

        [WButton(groupName: "ManyGroup")]
        public void Button4() { }

        [WButton(groupName: "ManyGroup")]
        public void Button5() { }

        [WButton(groupName: "ManyGroup")]
        public void Button6() { }

        [WButton(groupName: "ManyGroup")]
        public void Button7() { }

        [WButton(groupName: "ManyGroup")]
        public void Button8() { }

        [WButton(groupName: "ManyGroup")]
        public void Button9() { }

        [WButton(groupName: "ManyGroup")]
        public void Button10() { }
    }
}
#endif
