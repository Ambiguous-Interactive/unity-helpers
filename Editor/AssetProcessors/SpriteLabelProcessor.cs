// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public sealed class SpriteLabelProcessor : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingImportedPaths = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Action DrainAction = DrainPendingImports;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            if (Helpers.IsRunningInBatchMode || Helpers.IsRunningInContinuousIntegration)
            {
                return;
            }

            if (importedAssets == null || importedAssets.Length == 0)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (!IsCandidatePath(path))
                {
                    continue;
                }

                PendingImportedPaths.Add(path);
            }

            if (PendingImportedPaths.Count == 0)
            {
                return;
            }

            AssetPostprocessorDeferral.Schedule(DrainAction);
        }

        private static bool IsCandidatePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (!path.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        internal static int PendingImportedPathCountForTesting
        {
            get { return PendingImportedPaths.Count; }
        }

        internal static string[] SnapshotPendingImportedPathsForTesting()
        {
            string[] snapshot = new string[PendingImportedPaths.Count];
            PendingImportedPaths.CopyTo(snapshot);
            return snapshot;
        }

        internal static void EnqueueImportedPathsForTesting(string[] importedAssets)
        {
            if (importedAssets == null || importedAssets.Length == 0)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (IsCandidatePath(path))
                {
                    PendingImportedPaths.Add(path);
                }
            }
        }

        internal static void ResetForTesting()
        {
            PendingImportedPaths.Clear();
        }

        private static void DrainPendingImports()
        {
            if (PendingImportedPaths.Count == 0)
            {
                return;
            }

            string[] batch = new string[PendingImportedPaths.Count];
            PendingImportedPaths.CopyTo(batch);
            PendingImportedPaths.Clear();
            ProcessImportedPaths(batch);
        }

        private static void ProcessImportedPaths(string[] paths)
        {
            bool anyChanged = Helpers.CachedLabels.Count == 0;

            foreach (string path in paths)
            {
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti == null || ti.textureType != TextureImporterType.Sprite)
                {
                    continue;
                }

                Object mainObj = AssetDatabase.LoadMainAssetAtPath(path);
                if (mainObj == null)
                {
                    continue;
                }

                string[] newLabels = AssetDatabase.GetLabels(mainObj);
                if (newLabels.Length != 0)
                {
                    if (
                        !Helpers.CachedLabels.TryGetValue(path, out string[] oldLabels)
                        || !AreEqual(oldLabels, newLabels)
                    )
                    {
                        anyChanged = true;
                        if (newLabels.Length == 0)
                        {
                            Helpers.CachedLabels.Remove(path);
                            continue;
                        }

                        Helpers.CachedLabels[path] = newLabels;
                    }
                }
                else if (Helpers.CachedLabels.Remove(path))
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                using PooledResource<HashSet<string>> labelSetResource =
                    Buffers<string>.HashSet.Get(out HashSet<string> labelSet);
                foreach (string[] labels in Helpers.CachedLabels.Values)
                {
                    foreach (string label in labels)
                    {
                        labelSet.Add(label);
                    }
                }

                using PooledResource<List<string>> orderedLabelsResource = Buffers<string>.List.Get(
                    out List<string> orderedLabels
                );
                orderedLabels.AddRange(labelSet);
                orderedLabels.Sort(StringComparer.Ordinal);
                Helpers.SetSpriteLabelCache(orderedLabels, alreadySorted: true);
            }
        }

        private static bool AreEqual(string[] a, string[] b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            using PooledResource<HashSet<string>> setABuffer = SetBuffers<string>
                .GetHashSetPool(StringComparer.Ordinal)
                .Get(out HashSet<string> setA);
            using PooledResource<HashSet<string>> setBBuffer = SetBuffers<string>
                .GetHashSetPool(StringComparer.Ordinal)
                .Get(out HashSet<string> setB);

            foreach (string inputA in a)
            {
                setA.Add(inputA);
            }

            foreach (string inputB in b)
            {
                setB.Add(inputB);
            }

            return setA.SetEquals(setB);
        }
    }
#endif
}
