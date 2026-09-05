// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target where one button in a group has explicit placement and others use defaults.
    /// This should NOT generate a placement conflict warning since only one explicit value is set.
    /// Mirrors the user scenario: one button sets groupPlacement explicitly, others default to UseGlobalSetting.
    /// </summary>
    public sealed class WButtonMixedExplicitAndDefaultPlacementTarget : ScriptableObject
    {
        [WButton("Log State", groupName: "Debug Tools", groupPlacement: WButtonGroupPlacement.Top)]
        public void LogState() { }

        [WButton("Clear Console", groupName: "Debug Tools")]
        public void ClearConsole() { }

        [WButton(
            "Save Game",
            groupName: "Save System",
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        public void SaveGame() { }

        [WButton("Load Game", groupName: "Save System")]
        public void LoadGame() { }

        [WButton("Delete Save", groupName: "Save System")]
        public void DeleteSave() { }
    }
}
