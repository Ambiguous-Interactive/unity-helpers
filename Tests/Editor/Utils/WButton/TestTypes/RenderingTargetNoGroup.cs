// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetNoGroup : ScriptableObject
    {
        [WButton]
        public void UngroupedButton1() { }

        [WButton]
        public void UngroupedButton2() { }
    }
}
#endif
