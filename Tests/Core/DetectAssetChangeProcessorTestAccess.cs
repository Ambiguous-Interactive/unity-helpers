// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.AssetProcessors.DetectAssetChangeProcessor;

    /// <summary>Provides test-side setup and inspection for DetectAssetChangeProcessor.</summary>
    internal static class DetectAssetChangeProcessorTestAccess
    {
        internal static void ProcessChanges(
            string[] imported,
            string[] deleted,
            string[] moved,
            string[] movedFrom
        )
        {
            EnsureInitialized();
            EnqueueAssetChanges(
                imported ?? Array.Empty<string>(),
                deleted ?? Array.Empty<string>(),
                moved ?? Array.Empty<string>(),
                movedFrom ?? Array.Empty<string>()
            );
            DetectAssetChangeProcessor.ProcessPendingAssetChangesCore();
        }

        internal static AssetWatcherSettings GetSettings()
        {
            return new AssetWatcherSettings
            {
                Initialized = _initialized,

                WatchersByAssetType = CloneWatchers(WatchersByAssetType),
                PendingAssetChanges = ClonePendingChanges(PendingAssetChanges),
                ProcessingAssetChanges = _processingAssetChanges,
                LoopProtectionActive = _loopProtectionActive,
                ConsecutiveChangeBatches = _consecutiveChangeBatches,
                LastChangeProcessTimestamp = _lastChangeProcessTimestamp,

                DiagnosticsEnabled = _diagnosticsEnabled,
                EnabledOverride = _enabledOverride,
            };
        }

        internal static void Reset(AssetWatcherSettings settings = null)
        {
            _initialized = settings?.Initialized ?? false;

            WatchersByAssetType.Clear();
            if (settings?.WatchersByAssetType != null)
            {
                foreach (KeyValuePair<Type, AssetWatcher> pair in settings.WatchersByAssetType)
                {
                    WatchersByAssetType.Add(pair.Key, CloneWatcher(pair.Value));
                }
            }
            PendingAssetChanges.Clear();
            if (settings?.PendingAssetChanges != null)
            {
                foreach (PendingAssetChangeSet pendingChange in settings.PendingAssetChanges)
                {
                    PendingAssetChanges.Enqueue(ClonePendingChange(pendingChange));
                }
            }
            _processingAssetChanges = settings?.ProcessingAssetChanges ?? false;
            _loopProtectionActive = settings?.LoopProtectionActive ?? false;
            _consecutiveChangeBatches = settings?.ConsecutiveChangeBatches ?? 0;
            _lastChangeProcessTimestamp = settings?.LastChangeProcessTimestamp ?? 0;

            DiagnosticsEnabled = settings?.DiagnosticsEnabled ?? false;
            _enabledOverride = settings?.EnabledOverride;
        }

        internal static void EnsureInitialized()
        {
            if (!_initialized)
            {
                _initialized = true;
                DetectAssetChangeProcessor.BuildWatchers();
            }
        }

        internal static bool ValidateMethodSignature(Type declaringType, string methodName)
        {
            if (declaringType == null)
            {
                throw new ArgumentNullException(nameof(declaringType));
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException(nameof(methodName));
            }

            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic;
            MethodInfo method = declaringType.GetMethod(methodName, flags);
            if (method == null)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Method {0}.{1} was not found.",
                        declaringType.FullName,
                        methodName
                    ),
                    nameof(methodName)
                );
            }

            return TryResolveParameterMode(declaringType, method, out _, out _);
        }

        internal static Dictionary<Type, AssetWatcher> CloneWatchers(
            IReadOnlyDictionary<Type, AssetWatcher> source
        )
        {
            Dictionary<Type, AssetWatcher> clones = new(source.Count);
            foreach (KeyValuePair<Type, AssetWatcher> pair in source)
            {
                clones.Add(pair.Key, CloneWatcher(pair.Value));
            }
            return clones;
        }

        internal static AssetWatcher CloneWatcher(AssetWatcher source)
        {
            if (source == null)
            {
                return null;
            }

            AssetWatcher clone = new(source.AssetType, source.IncludeAssignableTypes);
            clone.SubAssetSearchFilter = source.SubAssetSearchFilter;
            if (source.SearchPrefabs)
            {
                clone.EnablePrefabSearch();
            }
            if (source.SearchSceneObjects)
            {
                clone.EnableSceneObjectSearch();
            }
            clone.KnownAssetPaths.UnionWith(source.KnownAssetPaths);
            foreach (MethodSubscription subscription in source.Subscriptions)
            {
                clone.Subscriptions.Add(
                    subscription == null ? null : CloneSubscription(subscription)
                );
            }
            return clone;
        }

        internal static Queue<PendingAssetChangeSet> ClonePendingChanges(
            IEnumerable<PendingAssetChangeSet> source
        )
        {
            Queue<PendingAssetChangeSet> clones = new();
            foreach (PendingAssetChangeSet pendingChange in source)
            {
                clones.Enqueue(ClonePendingChange(pendingChange));
            }
            return clones;
        }

        internal static PendingAssetChangeSet ClonePendingChange(PendingAssetChangeSet source)
        {
            if (source == null)
            {
                return null;
            }

            return new PendingAssetChangeSet(
                CopyPaths(source.Imported),
                CopyPaths(source.Deleted),
                CopyPaths(source.Moved),
                CopyPaths(source.MovedFrom)
            );
        }

        internal static MethodSubscription CloneSubscription(MethodSubscription source)
        {
            return new MethodSubscription
            {
                _declaringType = source._declaringType,
                _method = source._method,
                _flags = source._flags,
                _parameterMode = source._parameterMode,
                _createdParameterElementType = source._createdParameterElementType,
                _searchPrefabs = source._searchPrefabs,
                _searchSceneObjects = source._searchSceneObjects,
            };
        }

        private static string[] CopyPaths(IReadOnlyList<string> source)
        {
            int sourceCount = source.Count;
            string[] copy = new string[sourceCount];
            if (source is string[] sourceArray)
            {
                Array.Copy(sourceArray, copy, sourceArray.Length);
                return copy;
            }
            if (source is ICollection<string> sourceCollection)
            {
                sourceCollection.CopyTo(copy, 0);
                return copy;
            }
            for (int index = 0; index < sourceCount; ++index)
            {
                copy[index] = source[index];
            }
            return copy;
        }

        internal sealed class AssetWatcherSettings
        {
            internal bool IncludeAssignableTypes { get; set; }
            internal bool SearchPrefabs { get; set; }
            internal bool SearchSceneObjects { get; set; }
            internal HashSet<string> KnownAssetPaths { get; set; }
            internal List<MethodSubscription> Subscriptions { get; set; }
            internal Dictionary<Type, AssetWatcher> WatchersByAssetType { get; set; } = new();
            internal Queue<PendingAssetChangeSet> PendingAssetChanges { get; set; } = new();
            internal bool Initialized { get; set; }

            internal bool ProcessingAssetChanges { get; set; }
            internal bool LoopProtectionActive { get; set; }
            internal int ConsecutiveChangeBatches { get; set; }
            internal double LastChangeProcessTimestamp { get; set; }

            internal bool DiagnosticsEnabled { get; set; }
            internal bool? EnabledOverride { get; set; }
        }
    }
#endif
}
