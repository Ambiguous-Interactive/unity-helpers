// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_TEST_FRAMEWORK
    internal sealed class TestRunAssemblyInput
    {
        internal string Name { get; }
        internal string OutputPath { get; }
        internal string[] SourceFiles { get; }
        internal string[] References { get; }

        internal TestRunAssemblyInput(
            string name,
            string outputPath,
            string[] sourceFiles,
            string[] references
        )
        {
            Name = name;
            OutputPath = outputPath;
            SourceFiles = sourceFiles ?? System.Array.Empty<string>();
            References = references ?? System.Array.Empty<string>();
        }
    }
#endif
}
