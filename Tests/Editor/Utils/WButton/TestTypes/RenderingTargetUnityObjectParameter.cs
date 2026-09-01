// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetUnityObjectParameter : ScriptableObject
    {
        public GameObject LastGameObject;
        public Transform LastTransform;

        [WButton]
        public void GameObjectButton(GameObject obj)
        {
            LastGameObject = obj;
        }

        [WButton]
        public void TransformButton(Transform trans)
        {
            LastTransform = trans;
        }
    }
}
#endif
