"""Exercise the local Unity project launcher with disposable project directories."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
from pathlib import Path
from typing import TypedDict, cast

import pytest

ROOT = Path(__file__).resolve().parents[2]
UNITY_FILES = (
    "create-test-project.sh",
    "create_test_project.py",
    "stage_unitypackage.py",
    "test-framework-evidence.cs.txt",
    "test-framework-evidence.asmdef.txt",
)


class ModulesPolicy(TypedDict):
    modules: dict[str, str]


class FrameworkPolicy(TypedDict):
    requestedVersion: str


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    checkout = tmp_path / "checkout Ω"
    for directory, names in (
        ("scripts/unity", UNITY_FILES),
        ("scripts", ("cli_encoding.py",)),
        ("scripts/python~", ("pyproject.toml", "uv.lock")),
        (".github", ("unity-test-framework.json", "unity-test-project-modules.json")),
    ):
        destination = checkout / directory
        destination.mkdir(parents=True, exist_ok=True)
        for name in names:
            shutil.copy2(ROOT / directory / name, destination)
    return checkout


def run_launcher(
    repository: Path, project: Path | str, *args: str
) -> subprocess.CompletedProcess[str]:
    bash = shutil.which("bash")
    assert bash is not None
    return subprocess.run(
        [bash, str(repository / "scripts/unity/create-test-project.sh"), *args],
        cwd=repository.parent,
        env={
            **os.environ,
            "CDPATH": ".",
            "UNITY_VERSION": "6000.6.0f1",
            "UNITY_TEST_PROJECT_DIR": str(project),
        },
        capture_output=True,
        check=False,
        timeout=45,
        encoding="utf-8",
    )


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
def test_scaffold_preserves_project_files_and_recreates_on_force(
    repository: Path, tmp_path: Path
) -> None:
    project = tmp_path / "local project"
    result = run_launcher(repository, project, "--unknown")
    assert result.returncode == 0, result.stderr
    assert result.stdout.startswith("WARNING: Unknown argument: --unknown\n")
    config = cast(
        "ModulesPolicy",
        json.loads(
            (repository / ".github/unity-test-project-modules.json").read_text(encoding="utf-8")
        ),
    )
    framework = cast(
        "FrameworkPolicy",
        json.loads((repository / ".github/unity-test-framework.json").read_text(encoding="utf-8")),
    )
    manifest = project / "Packages/manifest.json"
    assert json.loads(manifest.read_text(encoding="utf-8")) == {
        "dependencies": {
            "com.unity.test-framework": framework["requestedVersion"],
            **config["modules"],
            "com.wallstop-studios.unity-helpers": "file:/workspace",
        }
    }
    assert (project / "ProjectSettings/ProjectVersion.txt").read_bytes() == (
        b"m_EditorVersion: 6000.6.0f1\n"
    )
    assert (
        b"m_DefaultBehaviorMode: 1"
        in (project / "ProjectSettings/EditorSettings.asset").read_bytes()
    )
    evidence = project / "Assets/UhCiTestFrameworkEvidence/Editor"
    for extension in ("cs", "asmdef"):
        assert (evidence / f"UhCiTestFrameworkEvidence.{extension}").read_bytes() == (
            repository / f"scripts/unity/test-framework-evidence.{extension}.txt"
        ).read_bytes()
    assert not (project / "Packages/packages-lock.json").exists()

    marker = project / "Assets/retention.txt"
    marker.write_text("caller data", encoding="utf-8")
    repeated = run_launcher(repository, project)
    assert repeated.returncode == 0, repeated.stderr
    assert "already exists" in repeated.stdout
    assert marker.read_text(encoding="utf-8") == "caller data"

    recreated = run_launcher(repository, project, "--force")
    assert recreated.returncode == 0, recreated.stderr
    assert "Removing existing test project" in recreated.stdout
    assert not marker.exists()
    assert manifest.is_file()


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
@pytest.mark.parametrize(
    ("file", "content", "message"),
    [
        (
            ".github/unity-test-framework.json",
            '{"requestedVersion":"latest"}',
            "stable requestedVersion",
        ),
        (".github/unity-test-project-modules.json", '{"modules":[1]}', "nonempty modules object"),
        (
            ".github/unity-test-project-modules.json",
            '{"modules":{"com.unity.modules.ui":42}}',
            "nonblank version",
        ),
        ("scripts/unity/test-framework-evidence.cs.txt", None, "test-framework-evidence.cs.txt"),
    ],
)
def test_failed_input_validation_leaves_existing_and_missing_projects_untouched(
    repository: Path, tmp_path: Path, file: str, content: str | None, message: str
) -> None:
    target = repository / file
    if content is None:
        target.unlink()
    else:
        target.write_text(content, encoding="utf-8")

    project = tmp_path / "existing"
    (project / "Assets").mkdir(parents=True)
    marker = project / "Assets/retention.txt"
    marker.write_text("previous project", encoding="utf-8")
    forced = run_launcher(repository, project, "--force")
    assert forced.returncode == 1
    assert message in forced.stderr
    assert marker.read_text(encoding="utf-8") == "previous project"
    assert list(project.iterdir()) == [project / "Assets"]

    missing_project = tmp_path / "missing"
    new = run_launcher(repository, missing_project)
    assert new.returncode == 1
    assert message in new.stderr
    assert not missing_project.exists()


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
def test_force_does_not_recreate_the_checkout_or_a_parent(repository: Path) -> None:
    result = run_launcher(repository, repository.parent, "--force")
    assert result.returncode == 1
    assert "outside the package checkout" in result.stderr
    assert (repository / "scripts/unity/create_test_project.py").is_file()
    assert not (repository.parent / "Assets").exists()


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
@pytest.mark.parametrize("via_symlink", [False, True])
def test_force_keeps_checkout_descendants(
    repository: Path, tmp_path: Path, via_symlink: bool
) -> None:
    package_content = repository / "Runtime"
    package_content.mkdir()
    marker = package_content / "source.txt"
    marker.write_text("existing package source", encoding="utf-8")
    project = package_content
    if via_symlink:
        project = tmp_path / "linked project"
        project.symlink_to(package_content, target_is_directory=True)

    result = run_launcher(repository, project, "--force")
    assert marker.read_text(encoding="utf-8") == "existing package source"
    assert project.is_symlink() == via_symlink
    assert result.returncode == 1
    assert "outside the package checkout" in result.stderr


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
def test_scaffold_refuses_to_create_projects_within_the_checkout(repository: Path) -> None:
    project = repository / "generated"
    result = run_launcher(repository, project)
    assert not project.exists()
    assert result.returncode == 1
    assert "outside the package checkout" in result.stderr


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
@pytest.mark.parametrize("suffix", ["/.", "/child/..", "/child/../"])
def test_force_rejects_dot_operands_before_normalizing_them(
    repository: Path, tmp_path: Path, suffix: str
) -> None:
    victim = tmp_path / "disposable source"
    (victim / "child").mkdir(parents=True)
    marker = victim / "source.txt"
    marker.write_text("retain this file", encoding="utf-8")

    result = run_launcher(repository, f"{victim}{suffix}", "--force")
    assert marker.read_text(encoding="utf-8") == "retain this file"
    assert (victim / "child").is_dir()
    assert result.returncode == 1
    assert "named project directory" in result.stderr


@pytest.mark.skipif(os.name != "posix", reason="The local Docker project launcher uses POSIX Bash")
def test_scaffold_preserves_unicode_paths_and_utf8_marked_policy(
    repository: Path, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    for name in ("unity-test-framework.json", "unity-test-project-modules.json"):
        policy = repository / ".github" / name
        policy.write_text(policy.read_text(encoding="utf-8"), encoding="utf-8-sig")
    monkeypatch.setenv("PYTHONIOENCODING", "ascii")
    project = tmp_path / "Ω project"
    result = run_launcher(repository, project)
    assert result.returncode == 0, result.stderr
    assert f"Project dir: {project}" in result.stdout
    assert (project / "Packages/manifest.json").is_file()
