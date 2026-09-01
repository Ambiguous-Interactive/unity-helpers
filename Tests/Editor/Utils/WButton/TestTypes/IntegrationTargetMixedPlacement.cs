// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetMixedPlacement : ScriptableObject
    {
        [WButton(groupName: "TopGroup", groupPlacement: WButtonGroupPlacement.Top)]
        public void TopButton() { }

        [WButton(groupName: "BottomGroup", groupPlacement: WButtonGroupPlacement.Bottom)]
        public void BottomButton() { }
    }
}
#endif
