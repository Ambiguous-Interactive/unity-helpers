"""Run the local package exporter with staged inputs and controlled POSIX host commands."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import tarfile
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
from export_unitypackage import run_export

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = b"Unity export fixture\x00\xff\n"
LOG = b"unity stdout\x00\xff\nunity stderr\n"
UNITY_ARGUMENTS = (
    "-batchmode",
    "-nographics",
    "-quit",
    "-releaseCodeOptimization",
    "-projectPath",
    "/project",
    "-executeMethod",
    "UnityHelpersPackageExporter.Export",
    "-exportOutput",
)
pytestmark = pytest.mark.skipif(
    os.name != "posix", reason="Local Unity export uses POSIX Docker tooling"
)


def write_executable(path: Path, content: str) -> None:
    path.write_text(content, encoding="utf-8", newline="\n")
    path.chmod(0o755)


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "checkout Ω"
    paths = (
        "scripts/unity/export-unitypackage.sh",
        "scripts/unity/export_unitypackage.py",
        "scripts/unity/stage_unitypackage.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
        ".github/unity-versions.json",
        ".github/unity-test-framework.json",
        ".github/unity-test-project-modules.json",
    )
    for path in paths:
        destination = root / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / path, destination)
    (root / "package.json").write_text(
        '{"name": "test-export", "version": "1.2.3"}', encoding="utf-8"
    )
    packed = tmp_path / "packed/package"
    packed.mkdir(parents=True)
    for directory in ("Runtime", "Editor", "Samples~", "Shaders", "Styles", "URP"):
        (packed / directory).mkdir()
        (packed / directory / "marker").write_text(directory, encoding="utf-8")
    for file in (
        "package.json",
        "package.json.meta",
        "README.md",
        "README.md.meta",
        "LICENSE",
        "LICENSE.meta",
        "CHANGELOG.md",
        "CHANGELOG.md.meta",
        "Runtime.meta",
        "Editor.meta",
        "Shaders.meta",
        "Styles.meta",
        "URP.meta",
        "link.xml",
        "link.xml.meta",
    ):
        (packed / file).write_text(file, encoding="utf-8")
    with tarfile.open(root / "packed.tgz", "w:gz") as archive:
        archive.add(packed, arcname="package")
    tools = root / "fixture-tools"
    tools.mkdir()
    write_executable(
        tools / "npm",
        """#!/bin/sh
set -eu
test "$#" = 4
test "$1 $2 $3" = 'pack --json --pack-destination'
printf '%s\n' called > "$UH_EXPORT_FIXTURE/pack-called"
cp "$UH_EXPORT_FIXTURE/packed.tgz" "$4/packed.tgz"
printf '%s\n' '[{"filename":"packed.tgz"}]'
""",
    )
    write_executable(
        root / "scripts/unity/run-unity-docker.sh",
        r"""#!/bin/sh
set -eu
printf '%s\0' "$UNITY_TEST_PROJECT_DIR" "$UNITY_VERSION" "$UNITY_TIMEOUT" "$@" > "$UH_EXPORT_FIXTURE/runner-arguments"
printf 'unity stdout\000\377\n'
printf 'unity stderr\n' >&2
while [ "$#" -gt 0 ]; do
    if [ "$1" = '-exportOutput' ]; then
        destination="$UNITY_TEST_PROJECT_DIR/${2#/project/}"
        case "${UH_EXPORT_MODE:-success}" in
            success) printf 'Unity export fixture\000\377\n' > "$destination" ;;
            empty) : > "$destination" ;;
            missing) ;;
            *) exit "$UH_EXPORT_MODE" ;;
        esac
        exit 0
    fi
    shift
done
exit 75
""",
    )
    return root


def run_launcher(
    repository: Path, *arguments: str, **overrides: str
) -> subprocess.CompletedProcess[bytes]:
    environment = {
        **os.environ,
        "CDPATH": ".",
        "PATH": os.pathsep.join((str(repository / "fixture-tools"), os.environ["PATH"])),
        "UH_EXPORT_FIXTURE": str(repository),
        "UH_EXPORT_MODE": "",
        "UNITY_PACKAGE_PROJECT_DIR": "",
        "UNITY_TIMEOUT": "",
        "UNITY_VERSION": "",
    }
    environment.pop("VIRTUAL_ENV", None)
    return subprocess.run(
        [str(repository / "scripts/unity/export-unitypackage.sh"), *arguments],
        cwd=repository.parent,
        env=environment | overrides,
        capture_output=True,
        check=False,
        timeout=45,
    )


def test_export_writes_exact_log_payload_and_checksum(repository: Path) -> None:
    output_name = "local Ω.unitypackage"
    output = repository.parent / output_name
    output.write_bytes(b"old package")
    output.chmod(0o640)
    project = repository.parent / "Unity project"
    result = run_launcher(
        repository,
        "--output",
        output_name,
        UNITY_PACKAGE_PROJECT_DIR=str(project),
        UNITY_VERSION="6000.6.0f1",
        UNITY_TIMEOUT="55",
    )
    assert result.returncode == 0, result.stderr
    assert result.stderr == b""
    assert result.stdout == (
        f"Unity package test-export@1.2.3 staged for 6000.6.0f1: {project}\n".encode()
        + LOG
        + f"==> [export-unitypackage] Exported {output_name}\n".encode()
    )
    assert (project / "unitypackage-output/unity.log").read_bytes() == LOG
    assert output.read_bytes() == PACKAGE
    assert output.stat().st_mode & 0o777 == 0o640
    digest = hashlib.sha256(PACKAGE).hexdigest()
    assert (
        output.with_name(f"{output.name}.sha256").read_bytes()
        == f"{digest}  {output_name}\n".encode()
    )
    expected = (
        str(project),
        "6000.6.0f1",
        "55",
        *UNITY_ARGUMENTS,
        f"/project/unitypackage-output/{output_name}",
        "-logFile",
        "-",
    )
    assert (repository / "runner-arguments").read_bytes().split(b"\x00") == [
        *(os.fsencode(value) for value in expected),
        b"",
    ]
    assert (project / "Assets/WallstopStudios/UnityHelpers/Samples/marker").read_text(
        encoding="utf-8"
    ) == "Samples~"


def test_default_paths_and_version(repository: Path) -> None:
    project = repository / ".artifacts/unity/unitypackage-project"
    output = repository / ".artifacts/release/test-export-1.2.3.unitypackage"
    result = run_launcher(repository)
    assert result.returncode == 0, result.stderr
    assert result.stderr == b""
    assert output.read_bytes() == PACKAGE
    expected_version = json.loads(
        (repository / ".github/unity-versions.json").read_text(encoding="utf-8")
    )["release"]
    assert (repository / "runner-arguments").read_bytes().split(b"\x00")[:3] == [
        os.fsencode(project),
        expected_version.encode(),
        b"7200",
    ]


def test_stage_only_uses_flag_over_environment_and_does_not_launch_docker(
    repository: Path,
) -> None:
    other = repository.parent / "used only if option precedence regresses"
    project = repository.parent / "staged"
    result = run_launcher(
        repository,
        "--stage-only",
        "--project-dir",
        str(project),
        "--output",
        "not-created.unitypackage",
        UNITY_PACKAGE_PROJECT_DIR=str(other),
    )
    assert result.returncode == 0, result.stderr
    assert result.stderr == b""
    assert result.stdout.endswith(b"==> [export-unitypackage] Stage-only mode complete.\n")
    assert (project / "ProjectSettings/ProjectVersion.txt").is_file()
    assert not other.exists()
    assert not (repository / "runner-arguments").exists()
    assert not (repository.parent / "not-created.unitypackage").exists()


@pytest.mark.parametrize(
    ("mode", "status", "message"),
    [
        ("17", 17, b"Unity package export failed with exit code 17."),
        ("missing", 1, b"Unity package was not exported:"),
        ("empty", 1, b"Unity package was not exported:"),
    ],
)
def test_failure_preserves_existing_release_and_log(
    repository: Path, mode: str, status: int, message: bytes
) -> None:
    project = repository.parent / "candidate"
    output = repository.parent / "retained.unitypackage"
    checksum = output.with_suffix(".unitypackage.sha256")
    output.write_bytes(b"existing release")
    checksum.write_bytes(b"existing checksum")
    result = run_launcher(
        repository,
        "--output",
        str(output),
        "--project-dir",
        str(project),
        UH_EXPORT_MODE=mode,
    )
    assert (result.returncode, result.stderr.startswith(b"ERROR: " + message)) == (
        status,
        True,
    )
    assert (project / "unitypackage-output/unity.log").read_bytes() == LOG
    assert output.read_bytes() == b"existing release"
    assert checksum.read_bytes() == b"existing checksum"


def test_tee_failure_takes_precedence_over_unity_status(repository: Path) -> None:
    write_executable(repository / "fixture-tools/tee", "#!/bin/sh\ncat\nexit 37\n")
    output = repository.parent / "preserved.unitypackage"
    result = run_launcher(repository, "--output", str(output), UH_EXPORT_MODE="17")
    assert result.returncode == 37
    assert result.stdout.endswith(LOG)
    assert result.stderr.startswith(
        b"ERROR: Failed to persist Unity package export log with tee exit code 37:"
    )
    assert not output.exists()


@pytest.mark.parametrize("alias", ["package-to-log", "digest-to-log", "digest-to-package"])
def test_publication_alias_cannot_overwrite_export_evidence(repository: Path, alias: str) -> None:
    project = repository.parent / "staged"
    internal_directory = project / "unitypackage-output"
    log = internal_directory / "unity.log"
    output = repository.parent / "release.unitypackage"
    checksum = output.with_suffix(".unitypackage.sha256")
    if alias == "package-to-log":
        output.symlink_to(log)
    elif alias == "digest-to-log":
        checksum.symlink_to(log)
    else:
        checksum.symlink_to(output)

    result = run_launcher(repository, "--output", str(output), "--project-dir", str(project))

    assert result.returncode == 1
    assert b"must use distinct files" in result.stderr
    assert log.read_bytes() == LOG
    assert (internal_directory / output.name).read_bytes() == PACKAGE
    assert not output.exists() or output.is_symlink()


@pytest.mark.parametrize("symlink", [False, True])
def test_publish_replaces_read_only_output(repository: Path, symlink: bool) -> None:
    project = repository.parent / "staged"
    output = repository.parent / "release.unitypackage"
    existing = repository.parent / "retained.unitypackage" if symlink else output
    existing.write_bytes(b"old archive")
    existing.chmod(0o444)
    if symlink:
        output.symlink_to(existing)
    can_overwrite = os.access(existing, os.W_OK)

    result = run_launcher(repository, "--output", str(output), "--project-dir", str(project))

    assert result.returncode == 0, result.stderr
    assert output.read_bytes() == PACKAGE
    checksum = output.with_suffix(".unitypackage.sha256")
    assert (
        checksum.read_text(encoding="ascii")
        == f"{hashlib.sha256(PACKAGE).hexdigest()}  {output.name}\n"
    )
    if not can_overwrite:
        staged_package = project / "unitypackage-output" / output.name
        assert stat.S_IMODE(output.stat().st_mode) == stat.S_IMODE(staged_package.stat().st_mode)
        if symlink:
            assert not output.is_symlink()
            assert existing.read_bytes() == b"old archive"


@pytest.mark.parametrize("alias", ["log", "directory"])
def test_export_rejects_preexisting_log_location(
    repository: Path, monkeypatch: pytest.MonkeyPatch, alias: str
) -> None:
    project = repository.parent / "staged"
    project.mkdir()
    internal_directory = project / "unitypackage-output"
    protected_directory = repository.parent / "keep"
    protected_directory.mkdir()
    protected_log = protected_directory / "unity.log"
    protected_log.write_bytes(b"keep these bytes")
    if alias == "directory":
        internal_directory.symlink_to(protected_directory, target_is_directory=True)
    else:
        internal_directory.mkdir()
        (internal_directory / "unity.log").symlink_to(protected_log)

    monkeypatch.setenv("UH_EXPORT_FIXTURE", str(repository))
    monkeypatch.setenv("UH_EXPORT_MODE", "success")
    output = repository.parent / "release.unitypackage"
    with pytest.raises(FileExistsError):
        run_export(repository, project, str(output), "6000.6.0f1")
    assert protected_log.read_bytes() == b"keep these bytes"
    assert not (repository / "runner-arguments").exists()
    assert not output.exists()


@pytest.mark.parametrize(
    ("arguments", "message"),
    [
        (("--unknown",), b"Unknown argument: --unknown"),
        (("--output",), b"--output requires a path."),
        (("--project-dir",), b"--project-dir requires a path."),
        (("--project-dir", ""), b"--project-dir requires a path."),
        (
            ("--output", "./unity.log"),
            b"The output filename must differ from the Unity log",
        ),
        (
            ("--output", "UNITY.LOG"),
            b"The output filename must differ from the Unity log",
        ),
        (("--project-dir", "."), b"Refusing to create the export project"),
    ],
)
def test_invalid_calls_fail_before_packing(
    repository: Path, arguments: tuple[str, ...], message: bytes
) -> None:
    result = run_launcher(repository, *arguments)
    assert result.returncode == 1
    assert message in result.stderr
    assert result.stdout == b""
    assert not (repository / "pack-called").exists()
    assert not (repository / "runner-arguments").exists()


def test_checksum_escapes_special_filename_bytes(repository: Path) -> None:
    name = "line\nbreak\\key\r.unitypackage"
    result = run_launcher(repository, "--output", name)
    assert result.returncode == 0, result.stderr
    digest = hashlib.sha256(PACKAGE).hexdigest().encode("ascii")
    assert (repository.parent / f"{name}.sha256").read_bytes() == (
        b"\\" + digest + b"  line\\nbreak\\\\key\\r.unitypackage\n"
    )
