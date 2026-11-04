// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_TEST_FRAMEWORK
    using System;
    using System.Collections.Generic;
    using System.IO;

    internal static class TestRunAssemblyFreshness
    {
        internal static void Populate(
            TestRunResultNode root,
            IReadOnlyList<TestRunAssemblyInput> inputs
        )
        {
            if (root == null || inputs == null)
            {
                return;
            }
            Dictionary<string, TestRunAssemblyInput> byName = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < inputs.Count; ++index)
            {
                TestRunAssemblyInput input = inputs[index];
                if (input != null && !string.IsNullOrWhiteSpace(input.Name))
                {
                    byName[input.Name] = input;
                }
            }
            Dictionary<string, DateTime?> timestamps = new(StringComparer.Ordinal);
            foreach (TestRunResultNode node in root.children)
            {
                if (node == null)
                {
                    continue;
                }
                node.staleSources.Clear();
                node.freshnessErrors.Clear();
                node.assemblyBuiltUtc = null;
                node.sourceFilesInspected = false;
                string name = node.fullName ?? string.Empty;
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }
                if (!byName.TryGetValue(name, out TestRunAssemblyInput assembly))
                {
                    node.freshnessErrors.Add($"No compilation inputs for {name}.");
                    continue;
                }
                if (!TryTimestamp(assembly.OutputPath, timestamps, out DateTime rootBuilt))
                {
                    node.freshnessErrors.Add($"Cannot read assembly: {assembly.OutputPath}");
                    continue;
                }
                node.assemblyBuiltUtc = rootBuilt;
                Queue<TestRunAssemblyInput> pending = new();
                HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
                pending.Enqueue(assembly);
                while (pending.TryDequeue(out TestRunAssemblyInput current))
                {
                    if (!visited.Add(current.Name))
                    {
                        continue;
                    }
                    bool hasBuild = TryTimestamp(
                        current.OutputPath,
                        timestamps,
                        out DateTime built
                    );
                    if (!hasBuild)
                    {
                        node.freshnessErrors.Add(
                            $"Cannot read dependency assembly: {current.OutputPath}"
                        );
                    }
                    foreach (string source in current.SourceFiles)
                    {
                        if (!TryTimestamp(source, timestamps, out DateTime written))
                        {
                            node.freshnessErrors.Add(
                                $"Cannot read source: {current.Name}: {source}"
                            );
                            continue;
                        }
                        node.sourceFilesInspected = true;
                        if (rootBuilt < written || hasBuild && built < written)
                        {
                            node.staleSources.Add($"{current.Name}: {source}");
                        }
                    }
                    foreach (string reference in current.References)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(reference)
                            && byName.TryGetValue(reference, out TestRunAssemblyInput dependency)
                        )
                        {
                            pending.Enqueue(dependency);
                        }
                        else
                        {
                            node.freshnessErrors.Add(
                                $"No compilation inputs for dependency: {reference}"
                            );
                        }
                    }
                }
                if (!node.sourceFilesInspected)
                {
                    node.freshnessErrors.Add(
                        $"No readable source files in compilation closure: {name}"
                    );
                }
                node.staleSources.Sort(StringComparer.Ordinal);
                node.freshnessErrors.Sort(StringComparer.Ordinal);
            }
        }

        private static bool TryTimestamp(
            string path,
            Dictionary<string, DateTime?> cache,
            out DateTime timestamp
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                timestamp = default;
                return false;
            }
            if (cache.TryGetValue(path, out DateTime? cached))
            {
                timestamp = cached.GetValueOrDefault();
                return cached.HasValue;
            }
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    DateTime written = File.GetLastWriteTimeUtc(path);
                    cache[path] = written;
                    timestamp = written;
                    return true;
                }
            }
            catch (Exception) { }
            cache[path] = null;
            timestamp = default;
            return false;
        }
    }
#endif
}
