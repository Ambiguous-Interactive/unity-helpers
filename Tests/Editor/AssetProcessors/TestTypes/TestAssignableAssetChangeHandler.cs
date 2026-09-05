// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler that uses interface-based matching with IncludeAssignableTypes.
    /// </summary>
    internal sealed class TestAssignableAssetChangeHandler : ScriptableObject
    {
        private static readonly List<ITestDetectableContract> recordedCreated = new();
        private static readonly List<string> recordedDeletedPaths = new();

        public static IReadOnlyList<ITestDetectableContract> RecordedCreated => recordedCreated;

        public static IReadOnlyList<string> RecordedDeletedPaths => recordedDeletedPaths;

        public static void Clear()
        {
            recordedCreated.Clear();
            recordedDeletedPaths.Clear();
        }

        [DetectAssetChanged(
            typeof(ITestDetectableContract),
            AssetChangeFlags.Created | AssetChangeFlags.Deleted,
            DetectAssetChangedOptions.IncludeAssignableTypes
        )]
        private static void OnAssignableAssetChanged(
            ITestDetectableContract[] createdAssets,
            string[] deletedPaths
        )
        {
            recordedCreated.Clear();
            recordedDeletedPaths.Clear();

            if (createdAssets != null)
            {
                foreach (
                    WallstopStudios.UnityHelpers.Tests.AssetProcessors.ITestDetectableContract createdAssetsElement in createdAssets
                )
                {
                    if (createdAssetsElement != null)
                    {
                        recordedCreated.Add(createdAssetsElement);
                    }
                }
            }

            if (deletedPaths != null)
            {
                foreach (string deletedPathsElement in deletedPaths)
                {
                    recordedDeletedPaths.Add(deletedPathsElement);
                }
            }
        }
    }
}
#endif
