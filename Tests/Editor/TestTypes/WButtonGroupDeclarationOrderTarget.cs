// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Test target that matches the user's exact scenario:
    /// - Setup group declared first with two buttons
    /// - Debug group declared second with one button
    /// - All at same draw order (-1)
    /// Expected: Setup group should render before Debug group
    /// </summary>
    public sealed class WButtonGroupDeclarationOrderTarget : ScriptableObject
    {
        [WButton("Initialize Level", drawOrder: -1, groupName: "Setup")]
        public void Initialize() { }

        [WButton("Validate Configuration", drawOrder: -1, groupName: "Setup")]
        public void ValidateConfig() { }

        [WButton("Roll Dice", drawOrder: -1, groupName: "Debug")]
        public int RollDice() => PRNG.Instance.Next(1, 7);
    }
}
