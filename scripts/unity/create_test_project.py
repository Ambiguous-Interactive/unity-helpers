"""Scaffold the local Docker project for testing the Unity Helpers package."""

from __future__ import annotations

import json
import os
import shutil
import sys
from pathlib import Path
from typing import cast

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output

UNITY_SCRIPTS = Path(__file__).resolve().parent
REPOSITORY = UNITY_SCRIPTS.parents[1]
PACKAGE_REFERENCE = "file:/workspace"
FRAMEWORK_PACKAGE = "com.unity.test-framework"
PROJECT_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  productName: UnityHelpers-TestProject
  companyName: WallstopStudios
  defaultScreenWidth: 1024
  defaultScreenHeight: 768
  runInBackground: 1
"""
# Match the ephemeral CI project so default sprite and texture import settings agree.
EDITOR_SETTINGS = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!159 &1
EditorSettings:
  m_DefaultBehaviorMode: 1
"""


def manifest_text() -> str:
    from stage_unitypackage import read_object, test_framework_version

    modules_source = REPOSITORY / ".github/unity-test-project-modules.json"
    modules = read_object(modules_source).get("modules")
    if not isinstance(modules, dict) or not modules:
        raise ValueError(f"{modules_source} must declare a nonempty modules object.")
    if FRAMEWORK_PACKAGE in modules:
        raise ValueError(
            f"The {FRAMEWORK_PACKAGE} version belongs in .github/unity-test-framework.json."
        )
    framework = test_framework_version(REPOSITORY)
    dependencies = {FRAMEWORK_PACKAGE: framework}
    for name, version in cast("dict[str, object]", modules).items():
        if not isinstance(version, str) or not version.strip():
            raise ValueError(f"{modules_source} requires a nonblank version for {name}.")
        dependencies[name] = version
    dependencies["com.wallstop-studios.unity-helpers"] = PACKAGE_REFERENCE
    return json.dumps({"dependencies": dependencies}, ensure_ascii=False, indent=2) + "\n"


def create_project(project_name: str, unity_version: str, *, force: bool) -> None:
    if force:
        operand = project_name.rstrip(os.sep + (os.altsep or ""))
        leaf = os.path.basename(operand)  # noqa: PTH119 - Path removes a trailing dot.
        if leaf in (".", ".."):
            raise ValueError("Use a named project directory with --force.")

    project = Path(project_name)
    resolved = project.resolve()
    if resolved.is_relative_to(REPOSITORY) or REPOSITORY.is_relative_to(resolved):
        raise ValueError(
            f"Set UNITY_TEST_PROJECT_DIR outside the package checkout, "
            f"such as a sibling directory: {project_name}"
        )
    assets = project / "Assets"
    settings = project / "ProjectSettings"
    packages = project / "Packages"
    manifest = packages / "manifest.json"
    if not force and assets.is_dir() and manifest.is_file():
        print(
            f"==> [create-test-project] Test project already exists at {project_name}. "
            "Use --force to recreate."
        )
        return

    # Verify every repository input before creating or removing a project.
    manifest_content = manifest_text()
    evidence_sources = {
        extension: (UNITY_SCRIPTS / f"test-framework-evidence.{extension}.txt").read_bytes()
        for extension in ("cs", "asmdef")
    }
    if force and project.is_dir():
        print("==> [create-test-project] Removing existing test project (--force)...")
        if project.is_symlink():
            project.unlink()
        else:
            shutil.rmtree(project)

    print(f"==> [create-test-project] Creating test project at {project_name}...")
    print("    [1/4] Creating directory structure...")
    for directory in (assets, settings, packages):
        directory.mkdir(parents=True, exist_ok=True)

    print("    [2/4] Writing ProjectSettings/ProjectVersion.txt...")
    (settings / "ProjectVersion.txt").write_text(
        f"m_EditorVersion: {unity_version}\n", encoding="utf-8", newline="\n"
    )
    print("    [3/4] Writing ProjectSettings/ProjectSettings.asset...")
    (settings / "ProjectSettings.asset").write_text(
        PROJECT_SETTINGS, encoding="utf-8", newline="\n"
    )
    (settings / "EditorSettings.asset").write_text(EDITOR_SETTINGS, encoding="utf-8", newline="\n")

    print("    [4/4] Writing Packages/manifest.json...")
    evidence = assets / "UhCiTestFrameworkEvidence/Editor"
    evidence.mkdir(parents=True, exist_ok=True)
    for extension, source in evidence_sources.items():
        (evidence / f"UhCiTestFrameworkEvidence.{extension}").write_bytes(source)
    # Callers use the manifest to detect a finished scaffold, so publish it last.
    manifest.write_text(manifest_content, encoding="utf-8", newline="\n")

    print("==> [create-test-project] Test project created successfully.")
    print(f"    Project dir: {project_name}")
    print(f"    Unity version: {unity_version}")
    print(f"    Package reference: {PACKAGE_REFERENCE}")


def main() -> int:
    configure_output()
    arguments = sys.argv[1:]
    for argument in arguments:
        if argument != "--force":
            print(f"WARNING: Unknown argument: {argument}")
    try:
        create_project(
            os.environ.get("UNITY_TEST_PROJECT_DIR") or "/home/vscode/.unity-test-project",
            os.environ.get("UNITY_VERSION") or "2021.3.45f1",
            force="--force" in arguments,
        )
    except (OSError, RuntimeError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
