// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer.Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using UnityEditor.UnityLinker;

    /// <summary>Archives generated floor linker inputs before Unity removes staging files.</summary>
    public sealed class ConsumerLinkerCapture : IUnityLinkerProcessor
    {
        /// <inheritdoc />
        public int callbackOrder => int.MaxValue;

        /// <inheritdoc />
        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data
        )
        {
#if !UNITY_2022_1_OR_NEWER
            string output = Environment.GetEnvironmentVariable("CONSUMER_OUTPUT");
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
            {
                throw new BuildFailedException(
                    "Consumer linker capture needs the actual player output"
                );
            }
            string root = Path.Combine(
                Directory.GetParent(Path.GetDirectoryName(output)).FullName,
                "linker-lifecycle"
            );
            Directory.CreateDirectory(root);
            string manifest = Path.Combine(root, "source-paths.SHA256SUMS");
            if (File.Exists(manifest))
            {
                throw new BuildFailedException("Refusing repeated consumer linker snapshot");
            }
            List<string> rows = new List<string>();
            string directory = data.inputDirectory;
            foreach (
                string name in new[]
                {
                    "MethodsToPreserve.xml",
                    "TypesInScenes.xml",
                    "SerializedTypes.xml",
                }
            )
            {
                string path = Path.GetFullPath(Path.Combine(directory, name));
                if (!File.Exists(path))
                {
                    continue;
                }
                if (0 <= path.IndexOf('\r') || 0 <= path.IndexOf('\n'))
                {
                    throw new BuildFailedException("Invalid linker snapshot source path");
                }
                byte[] bytes = File.ReadAllBytes(path);
                using (SHA256 hash = SHA256.Create())
                {
                    string digest = BitConverter
                        .ToString(hash.ComputeHash(bytes))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                    string archived = Path.Combine(root, digest + ".xml");
                    if (!File.Exists(archived))
                    {
                        File.WriteAllBytes(archived, bytes);
                    }
                    rows.Add(digest + "  " + path);
                }
            }
            if (rows.Count == 0)
            {
                throw new BuildFailedException(
                    "Generated consumer linker inputs were not captured"
                );
            }
            File.WriteAllLines(manifest, rows);
#endif
            return null;
        }
    }
}
