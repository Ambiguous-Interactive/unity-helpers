"""Stage the validated npm payload as a disposable Unity export project."""

from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
from pathlib import Path
from typing import cast

REQUIRED_ENTRIES: tuple[str, ...] = (
    "package.json",
    "package.json.meta",
    "README.md",
    "README.md.meta",
    "LICENSE",
    "LICENSE.meta",
    "CHANGELOG.md",
    "CHANGELOG.md.meta",
    "Runtime",
    "Runtime.meta",
    "Editor",
    "Editor.meta",
    "Samples~",
    "Shaders",
    "Shaders.meta",
    "Styles",
    "Styles.meta",
    "URP",
    "URP.meta",
    "link.xml",
    "link.xml.meta",
)
EXPORTER_SOURCE: str = r"""using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetPackage;

public static class UnityHelpersPackageExporter
{
    public static void Export()
    {
        string outputPath = GetArgument("-exportOutput");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Missing -exportOutput argument.");
        }

        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(outputDirectory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Package.Export(new ExportPackageParameters(
            "Assets/WallstopStudios/UnityHelpers",
            outputPath,
            flags: ExportPackageOptions.Recurse
        ));

        FileInfo exported = new FileInfo(outputPath);
        if (!exported.Exists || exported.Length <= 0)
        {
            throw new InvalidOperationException("Unity package export did not produce a non-empty file: " + outputPath);
        }
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }
}
"""


def read_object(path: Path) -> dict[str, object]:
    value = cast("object", json.loads(path.read_text(encoding="utf-8")))
    if not isinstance(value, dict):
        raise ValueError(f"Expected a JSON object: {path}")
    return cast("dict[str, object]", value)


def package_metadata(repo_root: Path) -> tuple[str, str]:
    metadata = read_object(repo_root / "package.json")
    name, version = metadata.get("name"), metadata.get("version")
    if (
        not isinstance(name, str)
        or not name.strip()
        or not isinstance(version, str)
        or not version.strip()
    ):
        raise ValueError(
            f"{repo_root / 'package.json'} must define non-empty string name and version fields."
        )
    return name, version


def release_version(repo_root: Path) -> str:
    version = read_object(repo_root / ".github/unity-versions.json").get("release")
    if (
        not isinstance(version, str)
        or re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+f[0-9]+", version) is None
    ):
        raise ValueError("Invalid release Unity version.")
    return version


def is_within(parent: Path, candidate: Path) -> bool:
    return parent != candidate and candidate.is_relative_to(parent)


def resolve_project_path(
    repo_root: Path, requested_path: Path, temporary_root: Path | None = None
) -> Path:
    repository = repo_root.resolve()
    lexical_artifacts = repository / ".artifacts"
    artifacts = lexical_artifacts.resolve()
    temporary = (temporary_root or Path(tempfile.gettempdir())).resolve()
    project = requested_path.resolve()
    artifacts_are_scratch = artifacts == lexical_artifacts or (
        artifacts != repository
        and not is_within(repository, artifacts)
        and not is_within(artifacts, repository)
    )
    overlaps_checkout = project == repository or is_within(project, repository)
    if (
        overlaps_checkout
        or project == artifacts
        or not (
            (artifacts_are_scratch and is_within(artifacts, project))
            or (is_within(temporary, project) and not is_within(repository, project))
        )
    ):
        raise ValueError(
            f"Refusing to create the export project unless it is a subdirectory under {artifacts}, "
            f"or under {temporary} and outside {repository}: {project}"
        )
    return project


def npm_command() -> list[str]:
    if platform.system() != "Windows":
        return ["npm"]
    node = shutil.which("node")
    if node is None:
        raise ValueError("Cannot locate Node on PATH.")
    roots = [
        Path(node).parent,
        *(Path(root) for root in os.environ.get("PATH", "").split(os.pathsep) if root),
    ]
    for root in roots:
        cli = root / "node_modules/npm/bin/npm-cli.js"
        if cli.is_file():
            return [node, str(cli)]
    raise ValueError("Cannot locate npm-cli.js beside Node or on PATH.")


def test_framework_version(repo_root: Path) -> str:
    policy = read_object(repo_root / ".github/unity-test-framework.json")
    version = policy.get("requestedVersion")
    if not isinstance(version, str) or re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) is None:
        raise ValueError("Unity Test Framework policy requires a stable requestedVersion string.")
    return version


def stage_unity_package(repo_root: Path, project_path: Path, unity_version: str) -> Path:
    name, version = package_metadata(repo_root)
    if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+f[0-9]+", unity_version) is None:
        raise ValueError("Invalid release Unity version.")
    project = resolve_project_path(repo_root, project_path)
    # Validate policy before replacing any existing export project.
    framework_version = test_framework_version(repo_root)
    modules = read_object(repo_root / ".github/unity-test-project-modules.json").get("modules")
    if not isinstance(modules, dict):
        raise ValueError("Unity module policy requires a modules object.")
    dependencies: dict[str, str] = {"com.unity.test-framework": framework_version}
    for key, value in cast("dict[str, object]", modules).items():
        if not isinstance(value, str):
            raise ValueError(f"Invalid Unity module version: {key}")
        dependencies[key] = value
    with tempfile.TemporaryDirectory(prefix="unitypackage-pack-") as scratch_name:
        scratch = Path(scratch_name)
        result = subprocess.run(  # noqa: S603 - npm executable and arguments are repository-owned.
            [*npm_command(), "pack", "--json", "--pack-destination", str(scratch)],
            cwd=repo_root,
            capture_output=True,
            encoding="utf-8",
            check=False,
        )
        if result.returncode != 0:
            raise ValueError(f"npm pack failed ({result.returncode}): {result.stderr}")
        pack = cast("object", json.loads(result.stdout))
        if not isinstance(pack, list) or len(cast("list[object]", pack)) != 1:
            raise ValueError("npm pack did not produce exactly one package tarball.")
        entry = cast("list[object]", pack)[0]
        filename = (
            cast("dict[str, object]", entry).get("filename") if isinstance(entry, dict) else None
        )
        if not isinstance(filename, str) or not filename or Path(filename).name != filename:
            raise ValueError("npm pack did not produce exactly one package tarball.")
        with tarfile.open(scratch / filename, "r:gz") as archive:
            archive.extractall(scratch, filter="data")
        packed_root = scratch / "package"
        for required in REQUIRED_ENTRIES:
            if not (packed_root / required).exists():
                raise ValueError(
                    f"Packed npm package is missing required Unity export entry: {required}"
                )
        if project.exists():
            shutil.rmtree(project)
        staged_root = project / "Assets/WallstopStudios/UnityHelpers"
        for directory in (
            staged_root,
            project / "Assets/Editor",
            project / "Packages",
            project / "ProjectSettings",
        ):
            directory.mkdir(parents=True, exist_ok=True)
        for required in (*REQUIRED_ENTRIES, "docs", "docs.meta"):
            source = packed_root / required
            destination = staged_root / ("Samples" if required == "Samples~" else required)
            if source.is_dir():
                shutil.copytree(source, destination)
            elif source.exists():
                shutil.copy2(source, destination)
        settings = project / "ProjectSettings"
        (settings / "ProjectVersion.txt").write_text(
            f"m_EditorVersion: {unity_version}\n", encoding="utf-8", newline="\n"
        )
        (settings / "ProjectSettings.asset").write_text(
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n"
            "  productName: UnityHelpers-PackageExport\n  companyName: WallstopStudios\n  runInBackground: 1\n",
            encoding="utf-8",
            newline="\n",
        )
        (settings / "EditorSettings.asset").write_text(
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!159 &1\nEditorSettings:\n  m_DefaultBehaviorMode: 1\n",
            encoding="utf-8",
            newline="\n",
        )
        (project / "Packages/manifest.json").write_text(
            json.dumps({"dependencies": dependencies}, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )
        (project / "Assets/Editor/UnityHelpersPackageExporter.cs").write_text(
            EXPORTER_SOURCE, encoding="utf-8", newline="\n"
        )
    print(f"Unity package {name}@{version} staged for {unity_version}: {project}")
    return project


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project-dir",
        type=Path,
        default=repo_root / ".artifacts/unity/unitypackage-project",
    )
    args = parser.parse_args()
    try:
        project_path = cast("Path", args.project_dir)
        stage_unity_package(
            repo_root,
            project_path,
            os.environ.get("UNITY_VERSION") or release_version(repo_root),
        )
    except (
        OSError,
        ValueError,
        tarfile.TarError,
        subprocess.CalledProcessError,
    ) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
