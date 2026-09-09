// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler for detailed signature (created assets array + deleted paths array).
    /// </summary>
    internal sealed class TestDetailedSignatureHandler : ScriptableObject
    {
        public static TestDetectableAsset[] LastCreatedAssets => _lastCreatedAssets;

        public static string[] LastDeletedPaths => _lastDeletedPaths;

        private static TestDetectableAsset[] _lastCreatedAssets =
            Array.Empty<TestDetectableAsset>();
        private static string[] _lastDeletedPaths = Array.Empty<string>();

        public static void Clear()
        {
            _lastCreatedAssets = Array.Empty<TestDetectableAsset>();
            _lastDeletedPaths = Array.Empty<string>();
        }

        [DetectAssetChanged(
            typeof(TestDetectableAsset),
            AssetChangeFlags.Created | AssetChangeFlags.Deleted
        )]
        private static void OnDetailedChange(
            TestDetectableAsset[] createdAssets,
            string[] deletedPaths
        )
        {
            _lastCreatedAssets = createdAssets ?? Array.Empty<TestDetectableAsset>();
            _lastDeletedPaths = deletedPaths ?? Array.Empty<string>();
        }
    }
}
#endif
