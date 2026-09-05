// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target where one button in a group has explicit priority and others use defaults.
    /// This should NOT generate a priority conflict warning since only one explicit value is set.
    /// </summary>
    public sealed class WButtonMixedExplicitAndDefaultPriorityTarget : ScriptableObject
    {
        [WButton("Initialize", groupName: "Setup", groupPriority: 0)]
        public void Initialize() { }

        [WButton("Configure", groupName: "Setup")]
        public void Configure() { }

        [WButton("Reset", groupName: "Cleanup", groupPriority: 10)]
        public void Reset() { }

        [WButton("Clear Cache", groupName: "Cleanup")]
        public void ClearCache() { }

        [WButton("Delete Temp", groupName: "Cleanup")]
        public void DeleteTemp() { }
    }
}
