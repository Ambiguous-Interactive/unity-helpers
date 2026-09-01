// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetFloatParameter : ScriptableObject
    {
        public float LastValue;

        [WButton]
        public void FloatButton(float value)
        {
            LastValue = value;
        }

        [WButton]
        public void FloatWithDefault(float value = 3.14f)
        {
            LastValue = value;
        }
    }
}
#endif
