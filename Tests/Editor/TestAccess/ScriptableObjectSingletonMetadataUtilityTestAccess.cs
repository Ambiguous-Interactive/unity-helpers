// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Utils.ScriptableObjectSingletonMetadataUtility;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for ScriptableObjectSingletonMetadataUtility.</summary>
    internal static class ScriptableObjectSingletonMetadataUtilityTestAccess
    {
        /// <summary>
        /// Resets legacy state for testing. AssetDatabase batch cleanup is now handled
        /// by the unified <see cref="AssetDatabaseBatchHelper"/>.
        /// </summary>
        /// <remarks>
        /// This method is kept for backward compatibility with test cleanup code.
        /// The actual AssetDatabase state cleanup is handled by AssetDatabaseBatchHelper.ResetBatchDepth().
        /// </remarks>
        internal static void ResetAssetEditingDepth()
        {
            // CommonTestBase owns batch cleanup; this compatibility entry point remains inert.
        }
    }
#endif
}
