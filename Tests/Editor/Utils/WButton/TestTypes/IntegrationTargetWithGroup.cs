// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetWithGroup : ScriptableObject
    {
        [WGroup("PropertyGroup")]
        public int someValue;

        [WButton(groupName: "TestGroup")]
        public void GroupedButton() { }
    }
}
#endif
