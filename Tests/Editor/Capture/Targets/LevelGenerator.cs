// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the procedural-generation example, whose point is that a
    /// WButton method can take parameters and the inspector draws fields for them.
    /// </summary>
    public sealed class LevelGenerator : MonoBehaviour
    {
        [WButton("Generate Seed", historyCapacity: 20)]
        private int GenerateSeed()
        {
            return 1234;
        }

        [WButton("Generate Level")]
        private void GenerateLevel() { }

        [WButton("Clear Level", colorKey: "Default-Dark")]
        private void ClearLevel() { }

        [WButton("Generate with Seed")]
        private void GenerateWithSeed(int seed) { }
    }
}
