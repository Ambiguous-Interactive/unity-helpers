// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class InheritanceTargetDerivedNoOverride : InheritanceTargetVirtualBase
    {
        public int OtherCallCount;

        [WButton]
        public void OtherButton()
        {
            OtherCallCount++;
        }
    }
}
#endif
