// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class InheritanceTargetLevel3 : InheritanceTargetLevel2
    {
        public int Level3CallCount;

        [WButton]
        public void Level3Button()
        {
            Level3CallCount++;
        }
    }
}
#endif
