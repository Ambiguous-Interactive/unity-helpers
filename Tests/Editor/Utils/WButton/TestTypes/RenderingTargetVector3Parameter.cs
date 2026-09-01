// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetVector3Parameter : ScriptableObject
    {
        public Vector3 LastValue;

        [WButton]
        public void Vector3Button(Vector3 position)
        {
            LastValue = position;
        }

        [WButton]
        public void Vector3WithDefault(Vector3 position = default)
        {
            LastValue = position;
        }
    }
}
#endif
