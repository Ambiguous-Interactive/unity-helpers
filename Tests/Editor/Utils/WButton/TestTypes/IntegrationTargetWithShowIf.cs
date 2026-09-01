// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetWithShowIf : ScriptableObject
    {
        public bool showAdvanced;

        [WShowIf(nameof(showAdvanced))]
        public int advancedSetting;

        [WButton]
        public void ToggleAdvanced()
        {
            showAdvanced = !showAdvanced;
        }
    }
}
#endif
