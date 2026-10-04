// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using static WallstopStudios.UnityHelpers.Editor.Utils.ScriptableObjectSingletonCreator;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for ScriptableObjectSingletonCreator.</summary>
    internal static class ScriptableObjectSingletonCreatorTestAccess
    {
        internal static bool IncludeTestAssemblies;
        internal static bool DisableAutomaticRetries;
        internal static Func<Type, bool> TypeFilter;
        internal static bool IgnoreExclusionAttribute;
        internal static bool AllowAssetCreationDuringSuppression;

        internal static void EnsureSingletonAssets()
        {
            if (ScriptableObjectSingletonCreator.IsRunningInsideAssetImportWorkerProcess())
            {
                return;
            }
            if (!AllowAssetCreationDuringSuppression && EditorUi.Suppress)
            {
                ScriptableObjectSingletonCreator.EnsureSingletonAssets();
                return;
            }
            List<Type> candidates = new();
            foreach (
                Type candidate in ReflectionHelpers.GetTypesDerivedFrom(
                    typeof(UnityHelpers.Utils.ScriptableObjectSingleton<>),
                    includeAbstract: false
                )
            )
            {
                if (
                    !candidate.IsGenericType
                    && (IncludeTestAssemblies || !TestAssemblyHelper.IsTestType(candidate))
                    && (TypeFilter == null || TypeFilter(candidate))
                    && (
                        IgnoreExclusionAttribute
                        || !ReflectionHelpers.TryGetAttributeSafe<ExcludeFromSingletonCreationAttribute>(
                            candidate,
                            out _,
                            inherit: false
                        )
                    )
                )
                {
                    candidates.Add(candidate);
                }
            }
            ScriptableObjectSingletonCreator.EnsureSingletonAssets(candidates);
            if (DisableAutomaticRetries)
            {
                CancelScheduledEnsureInvocation();
            }
        }

        internal static void ResetAssetImportWorkerDetectionState()
        {
            _assetImportWorkerEnvCachedValue = null;
            _defaultAssetImportWorkerDetector = null;
            _mainThreadConfirmed = false;
            _mainThreadConfirmationPending = false;
            _capturedMainThreadId = 0;
        }

        internal static void ResetRetryState()
        {
            _retryAttempts = 0;
            _consecutiveZeroProgressRetries = 0;
            CancelScheduledEnsureInvocation();
        }

        internal static void ResetInitialEnsureState()
        {
            UnityHelpers.Utils.ScriptableObjectSingletonInitState.InitialEnsureCompleted = false;
        }

        /// <summary>
        /// Resets state for testing. Cleanup of AssetDatabase batch state is now handled
        /// by the unified <see cref="AssetDatabaseBatchHelper"/>.
        /// </summary>
        internal static void ResetAssetEditingScopeDepth()
        {
            _isEnsuring = false;

            CancelScheduledEnsureInvocation();
            _retryAttempts = 0;
            _consecutiveZeroProgressRetries = 0;
        }
    }
#endif
}
