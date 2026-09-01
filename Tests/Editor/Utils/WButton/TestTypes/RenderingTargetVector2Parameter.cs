// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetVector2Parameter : ScriptableObject
    {
        public Vector2 LastValue;

        [WButton]
        public void Vector2Button(Vector2 position)
        {
            LastValue = position;
        }

        [WButton]
        public void Vector2WithDefault(Vector2 position = default)
        {
            LastValue = position;
        }
    }
}
#endif
