// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the combined priority and placement example. It is a
    /// <see cref="ScriptableObject"/> because the guide's snippet is one, and because the catalog
    /// has to prove the harness captures asset inspectors as well as component inspectors.
    /// </summary>
    public sealed class AdvancedButtonLayout : ScriptableObject
    {
        public int property1;
        public string property2;

        [WButton(
            "Validate Data",
            groupName: "Validation",
            groupPriority: 1,
            groupPlacement: WButtonGroupPlacement.Top
        )]
        private void ValidateData() { }

        [WButton(
            "Generate IDs",
            groupName: "Authoring",
            groupPriority: 0,
            groupPlacement: WButtonGroupPlacement.Top
        )]
        private void GenerateIds() { }

        [WButton(
            "Submit to Server",
            groupName: "Network",
            groupPriority: 10,
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void Submit() { }

        [WButton(
            "Export",
            groupName: "IO",
            groupPriority: 0,
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void Export() { }

        [WButton(
            "Import",
            groupName: "IO",
            groupPriority: 0,
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void Import() { }
    }
}
