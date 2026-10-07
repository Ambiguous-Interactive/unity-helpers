// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer.Editor
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using UnityEditor;
    using UnityEditor.Build.Reporting;
    using UnityEditor.Compilation;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;

    public static class ConsumerBuild
    {
        public static void Build()
        {
            BuildPlayer();
        }

        public static void Prepare()
        {
            PrepareProject();
        }

        private static void PrepareProject()
        {
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Standalone,
                ScriptingImplementation.IL2CPP
            );
            PlayerSettings.SetManagedStrippingLevel(
                BuildTargetGroup.Standalone,
                ManagedStrippingLevel.High
            );
            PlayerSettings.SetIl2CppCompilerConfiguration(
                BuildTargetGroup.Standalone,
                Il2CppCompilerConfiguration.Release
            );
            PlayerSettings.SetScriptingDefineSymbolsForGroup(
                BuildTargetGroup.Standalone,
                string.Empty
            );
            EditorUserBuildSettings.buildScriptsOnly = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            const string scenePath = "Assets/ConsumerScene.unity";
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                return;
            }
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );
            GameObject host = new GameObject(nameof(ConsumerApp));
            host.AddComponent<ConsumerApp>();
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new IOException("Cannot save consumer scene");
            }
        }

        private static void BuildPlayer()
        {
            string output = Environment.GetEnvironmentVariable("CONSUMER_OUTPUT");
            if (string.IsNullOrWhiteSpace(output) || !Path.IsPathFullyQualified(output))
            {
                throw new ArgumentException("CONSUMER_OUTPUT must be an absolute executable path");
            }
            PrepareProject();
            const string scenePath = "Assets/ConsumerScene.unity";
            string directory = Path.GetDirectoryName(output);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "settings.txt"),
                "unity="
                    + Application.unityVersion
                    + "\nbackend="
                    + PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)
                    + "\nstripping="
                    + PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Standalone)
                    + "\ncompiler="
                    + PlayerSettings.GetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone)
                    + "\ndevelopment="
                    + EditorUserBuildSettings.development
                    + "\ndefines="
                    + PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone)
            );
            StringBuilder inputs = new StringBuilder();
            foreach (
                UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies(
                    AssembliesType.Player
                )
            )
            {
                if (
                    0 <= assembly.name.IndexOf(".Tests", StringComparison.Ordinal)
                    || 0 <= assembly.name.IndexOf("ConsumerMigration", StringComparison.Ordinal)
                )
                {
                    throw new InvalidOperationException(
                        "Package test assembly must not enter consumer build: " + assembly.name
                    );
                }
                inputs.AppendLine("assembly=" + assembly.name);
            }
            PackageInfo package = PackageInfo.FindForAssetPath(
                "Packages/com.wallstop-studios.unity-helpers"
            );
            if (package == null)
            {
                throw new InvalidOperationException("Consumer package provenance unavailable");
            }
            inputs.AppendLine("packageRoot=" + package.resolvedPath);
            foreach (string name in new[] { "protobuf-net.dll", "protobuf-net.Core.dll" })
            {
                string path = Path.Combine(package.resolvedPath, "Runtime", "Protobuf-Net", name);
                using (SHA256 hash = SHA256.Create())
                {
                    inputs.AppendLine(
                        name
                            + "="
                            + BitConverter
                                .ToString(hash.ComputeHash(File.ReadAllBytes(path)))
                                .Replace("-", string.Empty)
                    );
                }
            }
            foreach (string path in Directory.GetFiles("Assets/Consumer", "*.cs"))
            {
                using (SHA256 hash = SHA256.Create())
                {
                    inputs.AppendLine(
                        path
                            + "="
                            + BitConverter
                                .ToString(hash.ComputeHash(File.ReadAllBytes(path)))
                                .Replace("-", string.Empty)
                    );
                }
            }
            using (SHA256 hash = SHA256.Create())
            {
                inputs.AppendLine(
                    "Assets/Consumer/link.xml="
                        + BitConverter
                            .ToString(
                                hash.ComputeHash(File.ReadAllBytes("Assets/Consumer/link.xml"))
                            )
                            .Replace("-", string.Empty)
                );
            }
            File.WriteAllText(Path.Combine(directory, "inputs.txt"), inputs.ToString());
            BuildReport report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.DetailedBuildReport,
                }
            );
            File.WriteAllText(
                Path.Combine(directory, "build-report.txt"),
                "result="
                    + report.summary.result
                    + "\nerrors="
                    + report.summary.totalErrors
                    + "\nwarnings="
                    + report.summary.totalWarnings
            );
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Consumer IL2CPP build failed");
            }
        }
    }
}
