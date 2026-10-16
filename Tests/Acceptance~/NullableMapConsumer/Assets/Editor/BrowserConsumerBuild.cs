// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    public static class BrowserConsumerBuild
    {
        public static void Build()
        {
            string output = Environment.GetEnvironmentVariable("CONSUMER_OUTPUT");
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
            {
                throw new ArgumentException("CONSUMER_OUTPUT must be an absolute directory");
            }
            if (Directory.Exists(output))
            {
                throw new IOException("Browser output must be fresh");
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                throw new InvalidOperationException("Start Unity with -buildTarget WebGL");
            }
            ConsumerCases.Require(
                PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL)
                    == "SINGLE_THREADED",
                "Prepare browser defines before player compilation"
            );
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.WebGL,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.SetManagedStrippingLevel(
                BuildTargetGroup.WebGL,
                ManagedStrippingLevel.High
            );
            PlayerSettings.SetIl2CppCompilerConfiguration(
                BuildTargetGroup.WebGL,
                Il2CppCompilerConfiguration.Release
            );
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithStacktrace;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );
            GameObject host = new GameObject(nameof(BrowserConsumerApp));
            host.AddComponent<BrowserConsumerApp>();
            const string scenePath = "Assets/BrowserConsumerScene.unity";
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new IOException("Cannot save browser consumer scene");
            }
            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.DetailedBuildReport | BuildOptions.CleanBuildCache,
                }
            );
            Directory.CreateDirectory(output);
            RecordPlayerAssemblies(output);
            File.WriteAllText(
                Path.Combine(output, "settings.txt"),
                "unity="
                    + Application.unityVersion
                    + "\ntarget="
                    + report.summary.platform
                    + "\nbackend="
                    + PlayerSettings.GetScriptingBackend(BuildTargetGroup.WebGL)
                    + "\nstripping="
                    + PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.WebGL)
                    + "\ncompiler="
                    + PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.WebGL)
                    + "\ndefines="
                    + PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.WebGL)
                    + "\ndevelopment="
                    + EditorUserBuildSettings.development
                    + "\nresult="
                    + report.summary.result
                    + "\nerrors="
                    + report.summary.totalErrors
                    + "\nwarnings="
                    + report.summary.totalWarnings
            );
            ConsumerCases.Require(
                report.summary.result == BuildResult.Succeeded,
                "WebGL build failed"
            );
        }

        public static void Prepare()
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.WebGL,
                "SINGLE_THREADED"
            );
        }

        private static void RecordPlayerAssemblies(string output)
        {
            StringBuilder manifest = new StringBuilder();
            bool runtime = false;
            bool consumer = false;
            foreach (
                UnityEditor.Compilation.Assembly assembly in UnityEditor.Compilation.CompilationPipeline.GetAssemblies(
                    UnityEditor.Compilation.AssembliesType.Player
                )
            )
            {
                ConsumerCases.Require(
                    assembly.name.IndexOf(".Tests", StringComparison.Ordinal) < 0,
                    "Test assembly entered consumer player"
                );
                bool ownedRuntime = assembly.name == "WallstopStudios.UnityHelpers";
                bool ownedConsumer = assembly.name == "NestedOnly.Consumer";
                if (ownedRuntime || ownedConsumer)
                {
                    if (ownedRuntime)
                    {
                        ConsumerCases.Require(
                            0 <= Array.IndexOf(assembly.defines, "WALLSTOP_PROTO"),
                            "Runtime lacks generated serializer dispatch"
                        );
                    }
                    ConsumerCases.Require(
                        0 <= Array.IndexOf(assembly.defines, "SINGLE_THREADED"),
                        "Assembly lacks SINGLE_THREADED: " + assembly.name
                    );
                    ConsumerCases.Require(
                        0 <= Array.IndexOf(assembly.defines, "UNITY_WEBGL"),
                        "Assembly lacks UNITY_WEBGL: " + assembly.name
                    );
                    runtime |= ownedRuntime;
                    consumer |= ownedConsumer;
                }
                manifest.Append("assembly=").AppendLine(assembly.name);
                manifest.Append("defines=").AppendLine(string.Join(";", assembly.defines));
            }
            ConsumerCases.Require(runtime && consumer, "Required player assembly missing");
            File.WriteAllText(Path.Combine(output, "player-assemblies.txt"), manifest.ToString());
        }
    }
}
