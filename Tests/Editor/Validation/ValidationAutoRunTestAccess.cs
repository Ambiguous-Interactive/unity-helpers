// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using static WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationAutoRun;

    /// <summary>Provides test-side setup and inspection for ValidationAutoRun.</summary>
    internal static class ValidationAutoRunTestAccess
    {
        internal static void CompleteRun(ValidationRun run)
        {
            ValidationAutoRun.CompleteRun(run);
        }

        internal static void ClearPending()
        {
            Pending.Clear();
            TriggerSources.Clear();
        }
    }
#endif
}
