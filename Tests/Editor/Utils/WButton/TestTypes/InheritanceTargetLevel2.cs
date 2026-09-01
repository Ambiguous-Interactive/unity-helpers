// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal abstract class InheritanceTargetLevel2 : InheritanceTargetLevel1
    {
        public int Level2CallCount;

        [WButton]
        public void Level2Button()
        {
            Level2CallCount++;
        }
    }
}
#endif
