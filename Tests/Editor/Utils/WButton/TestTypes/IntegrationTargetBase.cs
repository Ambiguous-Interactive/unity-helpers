// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal abstract class IntegrationTargetBase : ScriptableObject
    {
        public int BaseCallCount;

        [WButton]
        public void BaseButton()
        {
            BaseCallCount++;
        }
    }
}
#endif
