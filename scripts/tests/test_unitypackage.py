"""Release archive, policy, CLI, and destructive-path regression controls."""

from __future__ import annotations

import base64
import gzip
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tarfile
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "python~"))
from check import verify_registry
from create_unitypackage import (
    ASSET_ROOT,
    Asset,
    collect_assets,
    create_unity_package,
    tar_header,
    write_unity_package,
)
from stage_unitypackage import (
    REQUIRED_ENTRIES,
    npm_command,
    package_metadata,
    release_version,
    resolve_project_path,
    stage_unity_package,
)
from stage_unitypackage import (
    test_framework_version as framework_version,
)

if TYPE_CHECKING:
    from collections.abc import Callable

REPO_ROOT = Path(__file__).resolve().parents[2]


def metadata(guid: str) -> str:
    return f"fileFormatVersion: 2\nguid: {guid}\n"


def parse_archive(path: Path) -> dict[str, bytes]:
    archive = gzip.decompress(path.read_bytes())
    entries: dict[str, bytes] = {}
    offset = 0
    while offset + 512 <= len(archive):
        header = archive[offset : offset + 512]
        if header == bytes(512):
            assert archive[offset:] == bytes(1024)
            return entries
        assert header[257:265] == b"ustar\x0000"
        checksum = int(header[148:156].split(b"\0")[0].strip(), 8)
        assert checksum == sum(header[:148] + b" " * 8 + header[156:])
        name = header[:100].split(b"\0")[0].decode("utf-8")
        assert name not in entries
        size = int(header[124:136].split(b"\0")[0], 8)
        offset += 512
        entries[name] = archive[offset : offset + size]
        offset += ((size + 511) // 512) * 512
    pytest.fail("Archive is missing its two terminating zero blocks")


@pytest.fixture
def payload(tmp_path: Path) -> Path:
    root = tmp_path / "payload"
    folder = root / "Samples"
    folder.mkdir(parents=True)
    (root / "Readme.txt").write_text("read me", encoding="utf-8")
    (root / "Readme.txt.meta").write_text(metadata("1" * 32), encoding="utf-8")
    (folder / "Example.cs").write_text("class Example {}\n", encoding="utf-8")
    (folder / "Example.cs.meta").write_text(metadata("2" * 32), encoding="utf-8")
    (root / ".gitkeep").write_text("ignored", encoding="utf-8")
    return root


def test_archive_contract_and_repeatability(payload: Path, tmp_path: Path) -> None:
    assets = collect_assets(payload)
    assert sorted(asset.pathname for asset in assets) == sorted(
        [
            ASSET_ROOT,
            f"{ASSET_ROOT}/Readme.txt",
            f"{ASSET_ROOT}/Samples",
            f"{ASSET_ROOT}/Samples/Example.cs",
        ]
    )
    first, second = tmp_path / "first.unitypackage", tmp_path / "second.unitypackage"
    digest = write_unity_package(assets, first)
    assert digest == write_unity_package(assets, second)
    assert first.read_bytes() == second.read_bytes()
    assert digest == hashlib.sha256(first.read_bytes()).hexdigest()
    assert Path(f"{first}.sha256").read_text(encoding="ascii") == f"{digest}  first.unitypackage\n"
    entries = parse_archive(first)
    assert len(entries) == 10
    for asset in assets:
        assert entries[f"{asset.guid}/pathname"].decode("utf-8") == asset.pathname
        assert entries[f"{asset.guid}/asset.meta"] == asset.meta
        assert (f"{asset.guid}/asset" in entries) == (asset.source_path is not None)
    with tarfile.open(first) as archive:
        assert len(archive.getmembers()) == 10


@pytest.mark.parametrize(
    ("contents", "message"),
    [
        (None, "has no metadata"),
        ("guid: invalid\n", "no valid GUID"),
        (metadata("1" * 32), "duplicate GUIDs"),
    ],
)
def test_invalid_metadata_rejected(payload: Path, contents: str | None, message: str) -> None:
    (payload / "Bad.txt").write_text("bad", encoding="utf-8")
    if contents is not None:
        (payload / "Bad.txt.meta").write_text(contents, encoding="utf-8")
    with pytest.raises(ValueError, match=message):
        collect_assets(payload)


@pytest.mark.parametrize(
    ("name", "size"), [("x" * 101, 0), ("é" * 51, 0), ("x", -1), ("x", 8**11), ("x\0y", 0)]
)
def test_unrepresentable_tar_fields_rejected(name: str, size: int) -> None:
    with pytest.raises(ValueError, match=r"too long|invalid|does not fit"):
        tar_header(name, size)


def test_failed_write_preserves_archive_and_cleans_scratch(tmp_path: Path) -> None:
    output = tmp_path / "old.unitypackage"
    output.write_bytes(b"retained")
    checksum = Path(f"{output}.sha256")
    checksum.write_bytes(b"retained checksum")
    missing = Asset("1" * 32, "missing", b"meta", tmp_path / "missing")
    with pytest.raises(FileNotFoundError):
        write_unity_package([missing], output)
    assert output.read_bytes() == b"retained"
    assert checksum.read_bytes() == b"retained checksum"
    assert sorted(file.name for file in tmp_path.iterdir()) == [output.name, checksum.name]


def test_project_boundary_and_symlink_aliases(tmp_path: Path) -> None:
    repository = tmp_path / "repository"
    runtime = repository / "Runtime"
    runtime.mkdir(parents=True)
    link = tmp_path / "checkout-link"
    link.symlink_to(repository, target_is_directory=True)
    artifacts = repository / ".artifacts"
    for requested in (repository, runtime, tmp_path, link, link / "Runtime", artifacts):
        with pytest.raises(ValueError, match="Refusing"):
            resolve_project_path(repository, requested, tmp_path)
    for target in (repository, runtime, tmp_path):
        artifacts.symlink_to(target, target_is_directory=True)
        with pytest.raises(ValueError, match="Refusing"):
            resolve_project_path(repository, runtime / "Keep", tmp_path)
        artifacts.unlink()
    scratch = tmp_path / "external-scratch"
    scratch.mkdir()
    artifacts.symlink_to(scratch, target_is_directory=True)
    with pytest.raises(ValueError, match="Refusing"):
        resolve_project_path(repository, artifacts, tmp_path)
    assert resolve_project_path(repository, artifacts / "safe", tmp_path) == scratch / "safe"
    assert resolve_project_path(repository, tmp_path / "safe", tmp_path) == tmp_path / "safe"


@pytest.mark.parametrize(
    "policy",
    [
        {},
        {"requestedVersion": None},
        {"requestedVersion": 1.4},
        {"requestedVersion": "latest"},
        {"requestedVersion": " 1.4.6"},
        {"requestedVersion": "1.4.6-preview.1"},
    ],
)
def test_invalid_framework_policy_preserves_existing_project(
    tmp_path: Path, policy: dict[str, object]
) -> None:
    (tmp_path / ".github").mkdir()
    (tmp_path / "package.json").write_text('{"name":"fixture","version":"1"}', encoding="utf-8")
    (tmp_path / ".github/unity-test-framework.json").write_text(
        json.dumps(policy), encoding="utf-8"
    )
    project = tmp_path / ".artifacts/project"
    project.mkdir(parents=True)
    marker = project / "retained"
    marker.write_bytes(b"retained")
    with pytest.raises(ValueError, match="stable requestedVersion"):
        stage_unity_package(tmp_path, project, "6000.6.0f1")
    assert marker.read_bytes() == b"retained"


@pytest.mark.parametrize("function", [package_metadata, framework_version, release_version])
def test_json_policy_rejects_nonobjects(tmp_path: Path, function: Callable[[Path], object]) -> None:
    (tmp_path / ".github").mkdir()
    for name in (
        "package.json",
        ".github/unity-test-framework.json",
        ".github/unity-versions.json",
    ):
        (tmp_path / name).write_text("[]", encoding="utf-8")
    with pytest.raises(ValueError, match="JSON object"):
        function(tmp_path)


def test_windows_npm_invocation_uses_node_without_shell(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    node = tmp_path / "node.exe"
    cli = tmp_path / "node_modules/npm/bin/npm-cli.js"
    cli.parent.mkdir(parents=True)
    cli.write_text("", encoding="utf-8")
    monkeypatch.setattr("stage_unitypackage.platform.system", lambda: "Windows")

    def find_node(_name: str) -> str:
        return str(node)

    monkeypatch.setattr("stage_unitypackage.shutil.which", find_node)
    assert npm_command() == [str(node), str(cli)]


def test_real_release_payload_roundtrip(tmp_path: Path) -> None:
    output = tmp_path / "release.unitypackage"
    assets, digest = create_unity_package(REPO_ROOT, output, release_version(REPO_ROOT))
    assert len(assets) > 1000
    assert digest == hashlib.sha256(output.read_bytes()).hexdigest()
    entries = parse_archive(output)
    assert len(entries) == len(assets) * 2 + sum(asset.source_path is not None for asset in assets)
    by_path = {asset.pathname: asset for asset in assets}
    for entry in REQUIRED_ENTRIES:
        staged = "Samples" if entry == "Samples~" else entry
        if not staged.endswith(".meta"):
            assert f"{ASSET_ROOT}/{staged}" in by_path
    runtime = by_path[f"{ASSET_ROOT}/Runtime/WallstopStudios.UnityHelpers.asmdef"]
    asmdef = cast("dict[str, object]", json.loads(entries[f"{runtime.guid}/asset"]))
    assert {"name": "Unity", "expression": "2021.3", "define": "WALLSTOP_PROTO"} in cast(
        "list[object]", asmdef["versionDefines"]
    )


@pytest.mark.parametrize("script", ["create_unitypackage.py", "stage_unitypackage.py"])
def test_cli_rejects_unknown_and_incomplete_arguments(script: str) -> None:
    for arguments in (
        ["--unknown"],
        ["--output"] if script.startswith("create") else ["--project-dir"],
    ):
        result = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/unity" / script), *arguments],
            capture_output=True,
            text=True,
            check=False,
        )
        assert result.returncode != 0
        assert "usage:" in result.stderr


@pytest.mark.parametrize(
    ("tool", "arguments", "source", "diagnostic"),
    [
        ("mypy", ["--strict"], "def missing(value):\n    return value\n", "no-untyped-def"),
        (
            "pyright",
            ["--project", str(REPO_ROOT / "scripts/python~/pyproject.toml")],
            'value: int = "wrong"\n',
            "reportAssignmentType",
        ),
        (
            "ruff",
            ["check", "--config", str(REPO_ROOT / "scripts/python~/pyproject.toml")],
            "import os\n",
            "F401",
        ),
        (
            "ruff",
            ["format", "--check", "--config", str(REPO_ROOT / "scripts/python~/pyproject.toml")],
            "value=1\n",
            "would be reformatted",
        ),
    ],
)
def test_python_gates_reject_real_defects(
    tmp_path: Path, tool: str, arguments: list[str], source: str, diagnostic: str
) -> None:
    control = tmp_path / "control.py"
    control.write_text(source, encoding="utf-8")
    result = subprocess.run(
        [tool, *arguments, str(control)], capture_output=True, text=True, check=False
    )
    assert result.returncode != 0
    assert diagnostic in result.stdout + result.stderr


def test_python_registry_rejects_missing_new_duplicate_and_empty_scope(tmp_path: Path) -> None:
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "init", "--quiet", str(tmp_path)], check=True)
    selected = tmp_path / "selected.py"
    selected.write_text("", encoding="utf-8")
    legacy = tmp_path / "legacy.py"
    legacy.write_text("", encoding="utf-8")
    assert verify_registry(tmp_path, [str(selected)], ["legacy.py"])
    assert not verify_registry(tmp_path, [], ["selected.py", "legacy.py"])
    assert not verify_registry(tmp_path, [str(selected), str(selected)], ["legacy.py"])
    assert not verify_registry(tmp_path, [str(selected)], ["legacy.py", "legacy.py"])
    assert not verify_registry(tmp_path, [str(selected)], ["selected.py", "legacy.py"])
    extra = tmp_path / "untyped.py"
    extra.write_text("def missing(value): return value", encoding="utf-8")
    assert not verify_registry(tmp_path, [str(selected)], ["legacy.py"])
    extra.unlink()
    legacy.unlink()
    assert not verify_registry(tmp_path, [str(selected)], ["legacy.py"])


def test_unicode_archive_filename_has_a_usable_checksum(payload: Path, tmp_path: Path) -> None:
    output = tmp_path / "日本語.unitypackage"
    digest = write_unity_package(collect_assets(payload), output)
    assert Path(f"{output}.sha256").read_text(encoding="utf-8") == f"{digest}  {output.name}\n"
    assert parse_archive(output)


@pytest.mark.parametrize(
    "version", ["latest", "6000.6.0f1\n", "\u0666\u0660\u0660\u0660.\u0666.\u0660f\u0661"]
)
def test_invalid_unity_version_preserves_project(tmp_path: Path, version: str) -> None:
    (tmp_path / "package.json").write_text('{"name":"fixture","version":"1"}', encoding="utf-8")
    project = tmp_path / ".artifacts/project"
    project.mkdir(parents=True)
    marker = project / "retained"
    marker.write_bytes(b"retained")
    with pytest.raises(ValueError, match="Invalid release Unity version"):
        stage_unity_package(tmp_path, project, version)
    assert marker.read_bytes() == b"retained"


def test_npm_failure_reports_stderr_and_preserves_project(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    (tmp_path / "package.json").write_text('{"name":"fixture","version":"1"}', encoding="utf-8")
    (tmp_path / ".github").mkdir()
    (tmp_path / ".github/unity-test-framework.json").write_text(
        '{"requestedVersion":"1.4.6"}', encoding="utf-8"
    )
    (tmp_path / ".github/unity-test-project-modules.json").write_text(
        '{"modules":{}}', encoding="utf-8"
    )
    project = tmp_path / ".artifacts/project"
    project.mkdir(parents=True)
    marker = project / "retained"
    marker.write_bytes(b"retained")

    def fail_pack(_command: list[str], **_kwargs: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(_command, 17, stdout="", stderr="npm failure diagnostic")

    monkeypatch.setattr("stage_unitypackage.subprocess.run", fail_pack)
    with pytest.raises(ValueError, match=r"npm pack failed \(17\): npm failure diagnostic"):
        stage_unity_package(tmp_path, project, "6000.6.0f1")
    assert marker.read_bytes() == b"retained"


@pytest.mark.parametrize("entrypoint", ["shell", "npm"])
def test_runtime_metadata_avoids_dev_installs_and_retains_warm_tools(
    tmp_path: Path, entrypoint: str
) -> None:
    uv = shutil.which("uv")
    assert uv is not None
    environment = dict(os.environ)
    environment.pop("VIRTUAL_ENV", None)
    virtual_environment = tmp_path / "isolated-environment"
    environment.update(
        {
            "UV_PROJECT_ENVIRONMENT": str(virtual_environment),
            "UV_PYTHON": sys.executable,
            "UV_OFFLINE": "1",
        }
    )
    if entrypoint == "shell":
        bash = shutil.which("bash")
        assert bash is not None
        command = [bash, str(REPO_ROOT / "scripts/generate-meta.sh")]
    else:
        command = [*npm_command(), "run", "meta:generate", "--"]
    python = virtual_environment / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    inspection = "import importlib.metadata as m,json; print(json.dumps({d.metadata['Name'].lower():d.version for d in m.distributions()}))"

    wheel = tmp_path / "dev_tool_marker-0.0.0-py3-none-any.whl"
    wheel_metadata = "dev_tool_marker-0.0.0.dist-info"
    wheel_entries = {
        "dev_tool_marker.py": b"def main():\n    print('retained developer tool')\n",
        f"{wheel_metadata}/METADATA": b"Metadata-Version: 2.1\nName: dev-tool-marker\nVersion: 0.0.0\n",
        f"{wheel_metadata}/WHEEL": b"Wheel-Version: 1.0\nRoot-Is-Purelib: true\nTag: py3-none-any\n",
    }
    records: list[str] = []
    with zipfile.ZipFile(wheel, "w") as archive:
        for name, content in wheel_entries.items():
            archive.writestr(name, content)
            digest = (
                base64.urlsafe_b64encode(hashlib.sha256(content).digest()).rstrip(b"=").decode()
            )
            records.append(f"{name},sha256={digest},{len(content)}")
        records.append(f"{wheel_metadata}/RECORD,,")
        archive.writestr(f"{wheel_metadata}/RECORD", "\n".join(records) + "\n")

    for state in ("cold", "warm"):
        installed_before: dict[str, str] = {}
        if state == "warm":
            installation = subprocess.run(
                [uv, "pip", "install", "--python", str(python), str(wheel)],
                env=environment,
                check=False,
                capture_output=True,
                text=True,
            )
            assert installation.returncode == 0, installation.stdout + installation.stderr
            installed_before = {"dev-tool-marker": "0.0.0"}
        asset = tmp_path / f"{state}.cs"
        asset.write_bytes(b"")
        result = subprocess.run(
            [*command, str(asset)],
            cwd=REPO_ROOT,
            env=environment,
            check=False,
            capture_output=True,
            text=True,
        )
        assert result.returncode == 0, result.stdout + result.stderr
        assert Path(f"{asset}.meta").is_file()
        after = subprocess.run(
            [str(python), "-c", inspection],
            env=environment,
            check=True,
            capture_output=True,
            text=True,
        )
        assert json.loads(after.stdout) == installed_before

    retained = subprocess.run(
        [str(python), "-c", "import dev_tool_marker; dev_tool_marker.main()"],
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )
    assert retained.stdout.strip() == "retained developer tool"
    removed = subprocess.run(
        [
            uv,
            "run",
            "--project",
            str(REPO_ROOT / "scripts/python~"),
            "--locked",
            "--no-dev",
            "--exact",
            "python",
            "-c",
            inspection,
        ],
        env=environment,
        check=True,
        capture_output=True,
        text=True,
    )
    assert json.loads(removed.stdout) == {}
