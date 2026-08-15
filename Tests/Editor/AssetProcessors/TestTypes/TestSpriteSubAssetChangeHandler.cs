// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Watches <see cref="Sprite"/> — the canonical case <c>HasMatchingSubAsset</c> exists for,
    /// since a sprite is never a texture's main asset type.
    /// </summary>
    internal static class TestSpriteSubAssetChangeHandler
    {
        /// <summary>
        /// The only folder whose changes this handler records, and the test root of the fixture
        /// that owns it. <see cref="Sprite"/> is a project-wide type, so an unfiltered recorder
        /// would capture texture imports made by every other fixture and leave state behind for the
        /// next fixture's cross-fixture pollution tripwire to trip over.
        /// </summary>
        public const string WatchedFolder = "Assets/__DetectAssetChangePrefabGuardTests__";

        private static readonly List<AssetChangeContext> Recorded = new();

        /// <summary>
        /// Change contexts recorded for assets under <see cref="WatchedFolder"/>.
        /// </summary>
        public static IReadOnlyList<AssetChangeContext> RecordedContexts => Recorded;

        /// <summary>
        /// Clears every recorded context.
        /// </summary>
        public static void Clear()
        {
            Recorded.Clear();
        }

        [DetectAssetChanged(typeof(Sprite))]
        private static void OnSpriteAssetChanged(AssetChangeContext context)
        {
            if (context == null)
            {
                return;
            }

            IReadOnlyList<string> createdPaths = context.CreatedAssetPaths;
            for (int i = 0; i < createdPaths.Count; i++)
            {
                string createdPath = createdPaths[i];
                if (
                    !string.IsNullOrEmpty(createdPath)
                    && createdPath.StartsWith(
                        WatchedFolder + "/",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    Recorded.Add(context);
                    return;
                }
            }
        }
    }
}
#endif
