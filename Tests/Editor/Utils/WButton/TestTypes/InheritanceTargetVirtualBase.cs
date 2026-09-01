// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal abstract class InheritanceTargetVirtualBase : ScriptableObject
    {
        public int BaseCallCount;

        [WButton]
        public virtual void VirtualButton()
        {
            BaseCallCount++;
        }
    }
}
#endif
