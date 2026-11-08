// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Benchmarks
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using UnityEditor.Compilation;
    using UnityEditor.SceneManagement;
    using UnityEngine.SceneManagement;

    /// <summary>Builds the isolated collection benchmark without including test assemblies.</summary>
    public static class RelationalCollectionPlayerBuild
    {
        /// <summary>Builds a Release IL2CPP player in the generated project's Build folder.</summary>
        public static void Build()
        {
            try
            {
                PlayerSettings.SetScriptingBackend(
                    NamedBuildTarget.Standalone,
                    ScriptingImplementation.IL2CPP
                );
                PlayerSettings.SetIl2CppCompilerConfiguration(
                    NamedBuildTarget.Standalone,
                    Il2CppCompilerConfiguration.Release
                );
                PlayerSettings.SetManagedStrippingLevel(
                    NamedBuildTarget.Standalone,
                    ManagedStrippingLevel.Disabled
                );
                EditorUserBuildSettings.development = false;
                CompilationPipeline.codeOptimization = CodeOptimization.Release;
                Scene scene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single
                );
                const string scenePath = "Assets/RelationalCollectionBenchmark.unity";
                if (!EditorSceneManager.SaveScene(scene, scenePath))
                {
                    throw new InvalidOperationException("Cannot save the benchmark scene.");
                }
                UnityEngine.Debug.Log(
                    $"Collection benchmark config: backend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}, optimization={CompilationPipeline.codeOptimization}, il2cpp={PlayerSettings.GetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone)}, development={EditorUserBuildSettings.development}"
                );
                BuildReport report = BuildPipeline.BuildPlayer(
                    new BuildPlayerOptions
                    {
                        scenes = new[] { scenePath },
                        locationPathName = Path.Combine(
                            "Build",
                            "CollectionBenchmark",
                            "CollectionBenchmark.exe"
                        ),
                        target = BuildTarget.StandaloneWindows64,
                        options = BuildOptions.None,
                    }
                );
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
