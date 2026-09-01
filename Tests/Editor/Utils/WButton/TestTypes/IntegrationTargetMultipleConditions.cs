// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetMultipleConditions : ScriptableObject
    {
        public bool condition1;
        public bool condition2;
        public int threshold;

        [WButton]
        public void ConditionalButton() { }
    }
}
#endif
