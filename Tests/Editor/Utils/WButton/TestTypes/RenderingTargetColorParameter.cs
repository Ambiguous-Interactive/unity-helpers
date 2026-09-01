// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetColorParameter : ScriptableObject
    {
        public Color LastValue;

        [WButton]
        public void ColorButton(Color color)
        {
            LastValue = color;
        }

        [WButton]
        public void ColorWithDefault(Color color = default)
        {
            LastValue = color;
        }
    }
}
#endif
