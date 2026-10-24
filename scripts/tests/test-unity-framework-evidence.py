#!/usr/bin/env python3
"""Compile the actual collector against labeled host shims and test its receipts."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class FrameworkEvidenceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workspace = tempfile.TemporaryDirectory(
            prefix="framework-evidence-controls-"
        )
        cls.root = Path(cls.workspace.name)
        sources = {
            "nunit.framework": "namespace NUnit.Framework { public class TestAttribute : System.Attribute {} }",
            "UnityEngine.TestRunner": """namespace UnityEngine {
                public static class Application {
                    public static string dataPath => System.Environment.GetEnvironmentVariable("CONTROL_PROJECT") + "/Assets";
                    public static string unityVersion => "HOST-SHIM-NOT-UNITY";
                }
                public static class JsonUtility { public static string ToJson(object value, bool pretty) =>
                    System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), new System.Text.Json.JsonSerializerOptions { IncludeFields = true, WriteIndented = pretty }); }
                public static class Debug { public static void LogError(string value) { System.Console.WriteLine(value); } }
                } namespace UnityEngine.TestTools { public class UnityTestAttribute : System.Attribute {} }""",
            "UnityEditor.TestRunner": """namespace UnityEditor { public class InitializeOnLoadAttribute : System.Attribute {} public class PackageInfo {} }
                namespace UnityEditor.TestTools.TestRunner.Api { public class TestRunnerApi {} }
                namespace UnityEditor.PackageManager {
                    public class PackageInfo {
                        public string name = "com.unity.test-framework";
                        public string version = System.Environment.GetEnvironmentVariable("CONTROL_VERSION") ?? "1.4.6";
                        public string source = "HOST-SHIM-NOT-UNITY";
                        public string resolvedPath = System.Environment.GetEnvironmentVariable("CONTROL_PACKAGE");
                        public static PackageInfo FindForAssembly(System.Reflection.Assembly assembly) => new PackageInfo();
                    }
                }""",
            "Driver": """namespace Controls { internal static class Program {
                private static void Main() { System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                    typeof(WallstopStudios.UnityHelpers.Ci.UhCiTestFrameworkEvidence).TypeHandle); }
                } }""",
        }
        for name, source in sources.items():
            folder = cls.root / name
            folder.mkdir()
            (folder / "Source.cs").write_text(source)
            references = ""
            if name == "Driver":
                references = "".join(
                    f'<ProjectReference Include="../{other}/{other}.csproj" />'
                    for other in sources
                    if other != "Driver"
                )
                template = ROOT / "scripts/unity/test-framework-evidence.cs.txt"
                references += f'<Compile Include="{template}" Link="Collector.cs" />'
            (folder / f"{name}.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                "<TargetFramework>net9.0</TargetFramework><LangVersion>9.0</LangVersion>"
                f'<OutputType>{"Exe" if name == "Driver" else "Library"}</OutputType>'
                "</PropertyGroup><ItemGroup>" + references + "</ItemGroup></Project>"
            )
        build = subprocess.run(
            [
                "dotnet",
                "build",
                str(cls.root / "Driver/Driver.csproj"),
                "--nologo",
                "-v:q",
                "-o",
                str(cls.root / "bin"),
            ],
            capture_output=True,
            text=True,
            timeout=120,
        )
        if build.returncode:
            raise AssertionError(build.stdout + build.stderr)
        cls.verifier = cls.root / "verify.ps1"
        cls.verifier.write_text(
            """param([string]$Library, [string]$Directory, [int]$ExpectedProcessId, [string]$Project)
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            . $Library
            Assert-UnityTestFrameworkEvidence -Directory $Directory -ProcessId $ExpectedProcessId -Project $Project
        """
        )

        cls.invoker = cls.root / "invoke.ps1"
        cls.invoker.write_text(
            r"""param([string]$Root, [string]$Project, [string]$Driver, [string]$LogPath, [string]$ArtifactsPath, [string]$Control)
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $script:ArtifactsPath = $ArtifactsPath
            . (Join-Path $Root 'scripts/unity/lib/test-framework-evidence.ps1')
            $tokens = $null
            $errors = $null
            $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $Root 'scripts/unity/run-ci-tests.ps1'), [ref]$tokens, [ref]$errors)
            $definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-UnityObservedEditor' }, $true)
            Invoke-Expression $definition.Extent.Text
            function Get-UnityNativeEvidence { param($Capture, $LogPath) return @{ State = 'HOST-SHIM-NOT-UNITY' } }
            function Write-UnityExecutionObservation {
                param($LogPath, $ProcessObservation, $Label)
                $savedObservation = $ProcessObservation.Clone()
                if ($Control -ceq 'stale-diagnostics') { $savedObservation.ProcessId = 1 }
                @{ Process = $savedObservation } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ([IO.Path]::ChangeExtension($LogPath, 'execution-diagnostics.json'))
            }
            function Invoke-ProcessWithTreeKillTimeout {
                param($FilePath, $Arguments, $TimeoutSeconds, $LogPath, $Label, $CompletionPattern, $CompletionGraceSeconds, $StallSeconds, $ProcessObservation)
                $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
                $start.ArgumentList.Add($Driver)
                foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
                $process = [Diagnostics.Process]::Start($start)
                try {
                    $ProcessObservation.ProcessId = $process.Id
                    $ProcessObservation.LaunchUtc = [DateTime]::UtcNow.ToString('o')
                    $ProcessObservation.LogPersistence = if ($Control -ceq 'stale-log') { 'write-failed' } else { 'written' }
                    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Host collector control timed out.' }
                    if ($process.ExitCode -ne 0) { throw 'Host collector control failed.' }
                } finally { $process.Dispose() }
                $null = New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force
                [IO.File]::WriteAllText($LogPath, ('HOST ORIGINAL PID ' + $ProcessObservation.ProcessId))
                $capture = $Arguments[[Array]::IndexOf($Arguments, '-uhFrameworkEvidenceDirectory') + 1]
                if ($Control -ceq 'missing') { Get-ChildItem -LiteralPath $capture -Directory | Remove-Item -Recurse -Force }
                if ($Control -ceq 'wrong-process') { $ProcessObservation.ProcessId = 1 }
                return @{ ExitCode = 0 }
            }
            if ($IsWindows) { throw 'These process-integration controls run in the Linux host suite.' }
            $first = Invoke-UnityObservedEditor -EditorPath 'labeled-host-shim' -Arguments @('-projectPath', $Project) -LogPath $LogPath -Label 'HOST CONTROL' -TimeoutSeconds 30
            if ($Control -ceq 'twice') {
                $second = Invoke-UnityObservedEditor -EditorPath 'labeled-host-shim' -Arguments @('-projectPath', $Project) -LogPath $LogPath -Label 'HOST CONTROL' -TimeoutSeconds 30
                if (@(Get-ChildItem -LiteralPath (Join-Path $ArtifactsPath 'framework') -Directory).Count -ne 2) { throw 'Invocations reused evidence directories.' }
            }
            [IO.File]::WriteAllText($LogPath, 'OVERWRITTEN RETRY LOG')
            [IO.File]::WriteAllText([IO.Path]::ChangeExtension($LogPath, 'execution-diagnostics.json'), '{"Process":{"ProcessId":1}}')
        """
        )

    @classmethod
    def tearDownClass(cls) -> None:
        cls.workspace.cleanup()

    def setUp(self) -> None:
        self.case = Path(tempfile.mkdtemp(prefix="case-", dir=self.root))
        self.project = self.case / "project with spaces"
        self.package = self.case / "package"
        self.output = self.case / "evidence"
        (self.project / "Assets").mkdir(parents=True)
        (self.project / "Packages").mkdir()
        (self.package / "Editor").mkdir(parents=True)
        self.manifest = {"dependencies": {"com.unity.test-framework": "1.4.6"}}
        (self.project / "Packages/manifest.json").write_text(json.dumps(self.manifest))
        (self.project / "Packages/packages-lock.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        "com.unity.test-framework": {
                            "version": "1.4.6",
                            "source": "registry",
                        }
                    }
                }
            )
        )
        (self.package / "package.json").write_text(
            '{"name":"com.unity.test-framework","version":"1.4.6"}'
        )
        (self.package / "Editor/Source.cs").write_bytes(
            b"// HOST SHIM package source\r\n"
        )
        (self.package / "Editor/Source.asmdef").write_text('{"name":"HostControl"}')
        self.environment = {
            **os.environ,
            "CONTROL_PROJECT": str(self.project),
            "CONTROL_PACKAGE": str(self.package),
            "CONTROL_VERSION": "1.4.6",
        }

    def capture(
        self, *, with_argument: bool = True
    ) -> subprocess.CompletedProcess[str]:
        args = ["dotnet", str(self.root / "bin/Driver.dll")]
        if with_argument:
            args += ["-uhFrameworkEvidenceDirectory", str(self.output)]
        return subprocess.run(
            args, env=self.environment, capture_output=True, text=True, timeout=30
        )

    def receipt(self) -> tuple[Path, dict]:
        paths = list(self.output.glob("*/receipt.json"))
        self.assertEqual(len(paths), 1)
        return paths[0], json.loads(paths[0].read_text())

    def retained(self, original: str) -> Path:
        receipt_path, receipt = self.receipt()
        entry = next(e for e in receipt["Files"] if e["OriginalPath"] == original)
        return receipt_path.parent / entry["Path"]

    def verify(
        self, process_id: int | None = None, project: Path | None = None
    ) -> subprocess.CompletedProcess[str]:
        if process_id is None:
            _, receipt = self.receipt()
            process_id = receipt["ProcessId"]
        return subprocess.run(
            [
                "pwsh",
                "-NoProfile",
                "-File",
                str(self.verifier),
                "-Library",
                str(ROOT / "scripts/unity/lib/test-framework-evidence.ps1"),
                "-Directory",
                str(self.output),
                "-ExpectedProcessId",
                str(process_id),
                "-Project",
                str(project or self.project),
            ],
            capture_output=True,
            text=True,
            timeout=30,
        )

    def test_actual_collector_retains_source_metadata_and_three_bound_modules(
        self,
    ) -> None:
        result = self.capture()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        path, receipt = self.receipt()
        self.assertEqual(receipt["UnityVersion"], "HOST-SHIM-NOT-UNITY")
        self.assertEqual(receipt["SourceCount"], 1)
        self.assertEqual(len(receipt["Assemblies"]), 3)
        self.assertEqual(
            self.retained("package/Editor/Source.cs").read_bytes(),
            (self.package / "Editor/Source.cs").read_bytes(),
        )
        for entry in receipt["Files"]:
            payload = (path.parent / entry["Path"]).read_bytes()
            self.assertEqual(hashlib.sha256(payload).hexdigest(), entry["Sha256"])
        verified = self.verify()
        self.assertEqual(verified.returncode, 0, verified.stdout + verified.stderr)

    def test_core_resolution_is_reported_separately_from_manifest_request(self) -> None:
        self.environment["CONTROL_VERSION"] = "1.8.0"
        (self.package / "package.json").write_text(
            '{"name":"com.unity.test-framework","version":"1.8.0"}'
        )
        (self.project / "Packages/packages-lock.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        "com.unity.test-framework": {
                            "version": "1.8.0",
                            "source": "builtin",
                        }
                    }
                }
            )
        )
        self.capture()
        path, receipt = self.receipt()
        requested = json.loads((path.parent / "manifest.json").read_text())
        self.assertEqual(requested["dependencies"]["com.unity.test-framework"], "1.4.6")
        self.assertEqual(receipt["PackageVersion"], "1.8.0")
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_collector_is_inactive_without_capture_argument(self) -> None:
        self.assertEqual(self.capture(with_argument=False).returncode, 0)
        self.assertFalse(self.output.exists())

    def test_missing_lock_retains_error_and_is_rejected(self) -> None:
        (self.project / "Packages/packages-lock.json").unlink()
        self.capture()
        self.assertEqual(len(list(self.output.glob("*/capture-error.txt"))), 1)
        result = self.verify(process_id=1)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("capture was incomplete", result.stderr)

    def test_empty_source_scope_is_rejected(self) -> None:
        (self.package / "Editor/Source.cs").unlink()
        self.capture()
        self.assertEqual(len(list(self.output.glob("*/capture-error.txt"))), 1)
        self.assertNotEqual(self.verify(process_id=1).returncode, 0)

    def test_valid_unity_package_source_paths_are_retained(self) -> None:
        relative = "Samples~/1_RunningTest_Project_Solution/Tests_1s/MyMathTests.cs"
        source = self.package / relative
        source.parent.mkdir(parents=True)
        source.write_text("// Registry sample path control")
        additional = self.package / "Samples~/Space and plus+/Unicode-λ.cs"
        additional.parent.mkdir(parents=True)
        additional.write_text("// Legal relative source path control")
        self.capture()
        result = self.verify()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        receipt_path, receipt = self.receipt()
        self.assertEqual(receipt["SourceCount"], 3)
        self.assertEqual(
            self.retained("package/" + relative).read_bytes(),
            source.read_bytes(),
        )

    def test_nested_package_sources_have_short_storage_paths_and_original_names(self) -> None:
        relative = "Samples~/10_LongRunningTests_Solution/PlayModeTests_10s/PlayModeTests_10s.asmdef"
        source = self.package / relative
        source.parent.mkdir(parents=True)
        source.write_text('{"name":"LongPathControl"}')
        self.capture()
        path, receipt = self.receipt()
        entry = next(e for e in receipt["Files"] if e.get("OriginalPath") == "package/" + relative)
        self.assertLessEqual(len(entry["Path"]), 19)
        self.assertEqual((path.parent / entry["Path"]).read_bytes(), source.read_bytes())
        self.assertEqual(self.verify().returncode, 0)

    def test_oversized_source_is_refused_before_copy(self) -> None:
        (self.package / "Editor/Source.cs").write_bytes(b"x" * (16777216 + 1))
        self.capture()
        errors = list(self.output.glob("*/capture-error.txt"))
        self.assertEqual(len(errors), 1)
        self.assertIn("oversized", errors[0].read_text())
        self.assertFalse((errors[0].parent / "package/Editor/Source.cs").exists())
        self.assertNotEqual(self.verify(process_id=1).returncode, 0)

    def test_changed_source_after_capture_does_not_rewrite_retained_bytes(self) -> None:
        self.capture()
        path, _ = self.receipt()
        before = self.retained("package/Editor/Source.cs").read_bytes()
        (self.package / "Editor/Source.cs").write_text("CHANGED EXTERNAL PACKAGE")
        self.assertEqual(
            self.retained("package/Editor/Source.cs").read_bytes(), before
        )
        self.assertEqual(self.verify().returncode, 0)

    def test_stale_process_or_project_is_rejected(self) -> None:
        self.capture()
        self.assertNotEqual(self.verify(process_id=1).returncode, 0)
        self.assertNotEqual(
            self.verify(project=self.case / "other project").returncode, 0
        )

    def test_tampered_metadata_source_and_module_bytes_are_rejected(self) -> None:
        self.capture()
        path, receipt = self.receipt()
        for entry in receipt["Files"]:
            target = path.parent / entry["Path"]
            original = target.read_bytes()
            target.write_bytes(original + b"tampered")
            result = self.verify()
            self.assertNotEqual(result.returncode, 0, entry["Path"])
            self.assertIn("evidence bytes differ", result.stderr)
            target.write_bytes(original)

    def test_removed_module_duplicate_identity_and_wrong_mvid_are_rejected(
        self,
    ) -> None:
        self.capture()
        path, original = self.receipt()
        for mutation in (
            "removed",
            "duplicate",
            "mvid",
            "fullname",
            "source-count",
            "package-version",
        ):
            receipt = json.loads(json.dumps(original))
            if mutation == "removed":
                receipt["Assemblies"].pop()
            elif mutation == "duplicate":
                receipt["Assemblies"][1] = receipt["Assemblies"][0]
            elif mutation == "mvid":
                receipt["Assemblies"][0][
                    "ModuleVersionId"
                ] = "00000000-0000-0000-0000-000000000000"
            elif mutation == "fullname":
                receipt["Assemblies"][0]["FullName"] = "Wrong, Version=0.0.0.0"
            elif mutation == "source-count":
                receipt["SourceCount"] = 0
            else:
                receipt["PackageVersion"] = "1.4.99"
            path.write_text(json.dumps(receipt))
            self.assertNotEqual(self.verify().returncode, 0, mutation)
        path.write_text(json.dumps(original))
        self.assertEqual(self.verify().returncode, 0)

    def test_missing_metadata_and_unsafe_paths_are_rejected(self) -> None:
        self.capture()
        path, original = self.receipt()
        for relative in (
            "../escape",
            "/absolute",
            "package/../escape",
            "package\\escape",
            "package//escape",
            "package/./escape",
            "C:/absolute",
            "package/name:stream",
            "package/control\ncharacter",
            "   ",
        ):
            receipt = json.loads(json.dumps(original))
            receipt["Files"][0]["Path"] = relative
            path.write_text(json.dumps(receipt))
            result = self.verify()
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Malformed or duplicate", result.stderr)
        receipt = json.loads(json.dumps(original))
        receipt["Files"] = [
            entry for entry in receipt["Files"] if entry["Path"] != "manifest.json"
        ]
        path.write_text(json.dumps(receipt))
        self.assertNotEqual(self.verify().returncode, 0)

    def test_invalid_original_paths_and_duplicate_source_mappings_are_rejected(self) -> None:
        self.capture()
        path, original = self.receipt()
        for value in ("../escape", "/absolute", "package/name:stream", "package//empty", "package/./part", "package/control\nname", "package\\part"):
            receipt = json.loads(json.dumps(original))
            receipt["Files"][0]["OriginalPath"] = value
            path.write_text(json.dumps(receipt))
            self.assertNotEqual(self.verify().returncode, 0, value)
        receipt = json.loads(json.dumps(original))
        receipt["Files"][1]["OriginalPath"] = receipt["Files"][0]["OriginalPath"]
        path.write_text(json.dumps(receipt))
        self.assertNotEqual(self.verify().returncode, 0)

    @unittest.skipIf(os.name == "nt", "Linux host orchestration control")
    def test_real_invocation_path_binds_pid_and_never_reuses_capture(self) -> None:
        source = (
            self.project
            / "Assets/UhCiTestFrameworkEvidence/Editor/UhCiTestFrameworkEvidence.cs"
        )
        source.parent.mkdir(parents=True)
        source.write_text(
            (ROOT / "scripts/unity/test-framework-evidence.cs.txt").read_text()
        )
        for control in ("twice", "deep", "missing", "wrong-process", "stale-log", "stale-diagnostics"):
            result = subprocess.run(
                [
                    "pwsh",
                    "-NoProfile",
                    "-File",
                    str(self.invoker),
                    "-Root",
                    str(ROOT),
                    "-Project",
                    str(self.project),
                    "-Driver",
                    str(self.root / "bin/Driver.dll"),
                    "-LogPath",
                    str(self.case / control / ("migration/OdinMigrationDictionaryImportVerificationTests/unity.log" if control == "deep" else control + ".log")),
                    "-ArtifactsPath",
                    str(self.case / control),
                    "-Control",
                    control,
                ],
                env=self.environment,
                capture_output=True,
                text=True,
                timeout=60,
            )
            if control in ("twice", "deep"):
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                for capture in (self.case / control / "framework").iterdir():
                    self.assertEqual(
                        json.loads((capture / "requested-manifest.json").read_text()),
                        self.manifest,
                    )
                    invocation = json.loads((capture / "invocation.json").read_text())
                    receipt = json.loads(next(capture.glob("*/receipt.json")).read_text())
                    self.assertEqual(invocation["ProcessId"], receipt["ProcessId"])
                    self.assertEqual(invocation["ProjectPath"], receipt["ProjectPath"])
                    retained_log = self.case / control / invocation["LogRelativePath"]
                    self.assertEqual(retained_log.read_text(), "HOST ORIGINAL PID " + str(receipt["ProcessId"]))
                    retained_diagnostics = retained_log.with_suffix(".execution-diagnostics.json")
                    self.assertEqual(json.loads(retained_diagnostics.read_text())["Process"]["ProcessId"], receipt["ProcessId"])
                    self.assertEqual(invocation["OriginalLogRelativePath"], "migration/OdinMigrationDictionaryImportVerificationTests/unity.log" if control == "deep" else "twice.log")
            else:
                self.assertNotEqual(result.returncode, 0, control)

    def test_no_snapshot_is_not_success(self) -> None:
        self.output.mkdir()
        self.assertNotEqual(self.verify(process_id=1).returncode, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
