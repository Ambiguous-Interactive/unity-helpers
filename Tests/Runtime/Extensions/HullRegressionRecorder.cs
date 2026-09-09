// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    internal sealed class HullRegressionRecorder
    {
        private readonly string testName;
        private readonly string outputDirectory;

        public HullRegressionRecorder(string testName)
        {
            this.testName = testName;
            outputDirectory = Path.Combine(Application.dataPath, "HullRegressionSnapshots");
        }

        private static SerializableFastVector3Int[] ToSerializable(
            IReadOnlyList<FastVector3Int> source
        )
        {
            if (source == null)
            {
                return Array.Empty<SerializableFastVector3Int>();
            }

            SerializableFastVector3Int[] buffer = new SerializableFastVector3Int[source.Count];
            for (int i = 0; i < source.Count; ++i)
            {
                FastVector3Int point = source[i];
                buffer[i] = new SerializableFastVector3Int
                {
                    x = point.x,
                    y = point.y,
                    z = point.z,
                };
            }

            return buffer;
        }

        public void WriteSnapshot(
            string mode,
            int seed,
            IReadOnlyList<FastVector3Int> points,
            IReadOnlyList<FastVector3Int> convex,
            IReadOnlyList<FastVector3Int> concaveEdgeSplit,
            IReadOnlyList<FastVector3Int> concaveKnn,
            IReadOnlyList<FastVector3Int> concaveUnified
        )
        {
            try
            {
                Directory.CreateDirectory(outputDirectory);
                string fileName =
                    $"{testName}_{mode}_seed{seed}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.json";
                string fullPath = Path.Combine(outputDirectory, fileName);
                HullSnapshot payload = new HullSnapshot
                {
                    test = testName,
                    mode = mode,
                    seed = seed,
                    timestampUtc = DateTime.UtcNow.ToString("o"),
                    points = ToSerializable(points),
                    convex = ToSerializable(convex),
                    concaveEdgeSplit = ToSerializable(concaveEdgeSplit),
                    concaveKnn = ToSerializable(concaveKnn),
                    concaveUnified = ToSerializable(concaveUnified),
                };
                string json = JsonUtility.ToJson(payload, true);
                File.WriteAllText(fullPath, json);
                string relativePath = $"Assets{fullPath.Substring(Application.dataPath.Length)}";
                Debug.Log($"[HullRegressionRecorder] Wrote hull snapshot → {relativePath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HullRegressionRecorder] Failed to write snapshot: {ex}");
            }
        }

        [Serializable]
        private sealed class HullSnapshot
        {
            public string test;
            public string mode;
            public int seed;
            public string timestampUtc;
            public SerializableFastVector3Int[] points;
            public SerializableFastVector3Int[] convex;
            public SerializableFastVector3Int[] concaveEdgeSplit;
            public SerializableFastVector3Int[] concaveKnn;
            public SerializableFastVector3Int[] concaveUnified;
        }

        [Serializable]
        private struct SerializableFastVector3Int
        {
            public int x;
            public int y;
            public int z;
        }
    }
}
