// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools.OdinMigration
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using UnityEditor.Compilation;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    [Timeout(60000)]
    public sealed class OdinMigrationCompilerProcessTests
    {
        private string _directory;

        private static Assembly CurrentAssembly(bool editorOnly)
        {
            string name = editorOnly
                ? typeof(OdinMigrationCompilerProcessTests).Assembly.GetName().Name
                : typeof(OdinMigrationBindingAsset).Assembly.GetName().Name;
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
            {
                if (string.Equals(assembly.name, name, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }
            Assert.Fail("The test assembly is missing from the editor compilation plan.");
            return null;
        }

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                "OdinCompilerControls",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_directory, true);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SupportedCompilerLayoutsUseOnlyOwnedArguments(int layout)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            string compiler =
                layout == 3
                    ? Path.Combine(
                        _directory,
                        "DotNetSdk",
                        "sdk",
                        "9.0.306",
                        "Roslyn",
                        "bincore",
                        "csc.dll"
                    )
                : layout == 2 ? Path.Combine(_directory, "Tools", "Roslyn", "csc.exe")
                : Path.Combine(_directory, "DotNetSdkRoslyn", "csc.dll");
            string runtime =
                layout == 3
                    ? Path.Combine(_directory, "DotNetSdk", windows ? "dotnet.exe" : "dotnet")
                : layout == 0
                    ? Path.Combine(
                        _directory,
                        "netcorerun",
                        windows ? "netcorerun.exe" : "netcorerun"
                    )
                : layout == 1
                    ? Path.Combine(_directory, "NetCoreRuntime", windows ? "dotnet.exe" : "dotnet")
                : windows ? compiler
                : Path.Combine(_directory, "MonoBleedingEdge", "bin", "mono");
            Directory.CreateDirectory(Path.GetDirectoryName(compiler));
            File.WriteAllText(compiler, "compiler control");
            Directory.CreateDirectory(Path.GetDirectoryName(runtime));
            File.WriteAllText(runtime, "runtime control");
            string response = Path.Combine(_directory, "arguments with spaces.rsp");
            ProcessStartInfo start = OdinMigrationCompilerProcess.CompilerStartInfo(
                _directory,
                response
            );
            Assert.AreEqual(runtime, start.FileName);
            if (!string.Equals(compiler, runtime, StringComparison.Ordinal))
            {
                StringAssert.StartsWith("\"" + compiler + "\" ", start.Arguments);
            }
            StringAssert.Contains("/noconfig @\"" + response + "\"", start.Arguments);
            Assert.IsFalse(start.UseShellExecute);
            Assert.IsTrue(start.RedirectStandardOutput);
            Assert.IsTrue(start.RedirectStandardError);
            Assert.IsTrue(start.CreateNoWindow);
        }

        [Test]
        public void MissingCompilerFailsBeforeAnyProcessStarts()
        {
            Assert.Throws<FileNotFoundException>(() =>
                OdinMigrationCompilerProcess.CompilerStartInfo(_directory, "candidate.rsp")
            );
        }

        [TestCase("9.0.306", false, true)]
        [TestCase("9.0.306-preview", true, true)]
        [TestCase("invalid", true, true)]
        [TestCase("9.0.306", true, false)]
        public void AnIncompleteBundledSdkCannotFallBackToAnotherCompiler(
            string version,
            bool hasCompiler,
            bool hasRuntime
        )
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            string sdk = Path.Combine(_directory, "DotNetSdk");
            string compiler = Path.Combine(sdk, "sdk", version, "Roslyn", "bincore", "csc.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(compiler));
            if (hasRuntime)
            {
                File.WriteAllText(
                    Path.Combine(sdk, windows ? "dotnet.exe" : "dotnet"),
                    "runtime control"
                );
            }
            if (hasCompiler)
            {
                File.WriteAllText(compiler, "compiler control");
            }
            string legacy = Path.Combine(_directory, "Tools", "Roslyn", "csc.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy));
            File.WriteAllText(legacy, "legacy compiler control");
            if (!windows)
            {
                string mono = Path.Combine(_directory, "MonoBleedingEdge", "bin", "mono");
                Directory.CreateDirectory(Path.GetDirectoryName(mono));
                File.WriteAllText(mono, "legacy runtime control");
            }
            Assert.Throws<FileNotFoundException>(() =>
                OdinMigrationCompilerProcess.CompilerStartInfo(_directory, "candidate.rsp")
            );
        }

        [TestCase("a\nb")]
        [TestCase("a\rb")]
        [TestCase("a\"b")]
        [TestCase("a\0b")]
        public void ResponseFilePathsCannotInjectCompilerArguments(string unsafePath)
        {
            string compiler = Path.Combine(_directory, "DotNetSdkRoslyn", "csc.dll");
            string runtime = Path.Combine(
                _directory,
                "netcorerun",
                Path.DirectorySeparatorChar == '\\' ? "netcorerun.exe" : "netcorerun"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(compiler));
            Directory.CreateDirectory(Path.GetDirectoryName(runtime));
            File.WriteAllText(compiler, "compiler control");
            File.WriteAllText(runtime, "runtime control");
            Assert.Throws<InvalidDataException>(() =>
                OdinMigrationCompilerProcess.CompilerStartInfo(_directory, unsafePath)
            );
        }

        [UnityTest]
        public IEnumerator RuntimeAndEditorAssemblyProfilesCompileWithTheirExactSymbols()
        {
            string[] profiles =
            {
                "NET_STANDARD_2_0",
                "NET_STANDARD_2_1",
                "NET_STANDARD",
                "NET_4_6",
                "NET_UNITY_4_8",
                "ENABLE_IL2CPP",
                "SINGLE_THREADED",
            };
            foreach (bool editorOnly in new[] { false, true })
            {
                Assembly assembly = CurrentAssembly(editorOnly);
                HashSet<string> defines = new HashSet<string>(
                    assembly.defines,
                    StringComparer.Ordinal
                );
                StringBuilder source = new StringBuilder();
                foreach (string profile in profiles)
                {
                    source
                        .Append("#if ")
                        .Append(defines.Contains(profile) ? "!" : "")
                        .AppendLine(profile)
                        .AppendLine("#error The captured assembly profile changed.")
                        .AppendLine("#endif");
                }
                source.AppendLine("internal sealed class CapturedProfileControl { }");
                string sourcePath = Path.Combine(
                    _directory,
                    editorOnly ? "EditorProfile.cs" : "RuntimeProfile.cs"
                );
                File.WriteAllText(sourcePath, source.ToString());
                using (
                    OdinMigrationCompilerProcess compiler = new OdinMigrationCompilerProcess(
                        Path.ChangeExtension(sourcePath, ".dll"),
                        new[] { sourcePath },
                        assembly.allReferences,
                        assembly
                    )
                )
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    while (!compiler.Completed && watch.Elapsed.TotalSeconds < 30)
                    {
                        yield return null;
                    }
                    Assert.IsTrue(compiler.Completed, "The compiler control did not finish.");
                    Assert.AreEqual(0, compiler.ExitCode, string.Join("\n", compiler.Diagnostics));
                    Assert.IsTrue(File.Exists(compiler.OutputPath));
                }
            }
        }
    }
}
