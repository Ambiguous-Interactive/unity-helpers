"""Hold source completeness, path ownership and immutable hashing during migration."""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from io import BufferedReader
    from os import stat_result
import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import frozen_files

ROOT = Path(__file__).resolve().parents[2]


def write(path: Path, contents: bytes = b"controlled bytes") -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(contents)
    return path


def sources(root: Path) -> None:
    for name in frozen_files.SOURCE_ROOTS:
        (root / name).mkdir(parents=True, exist_ok=True)
    for relative in frozen_files.EXPLICIT_SOURCES:
        write(root / relative)


def test_source_inventory_has_independent_exact_hashes_and_order(
    tmp_path: Path,
) -> None:
    sources(tmp_path)
    extras = (
        "Runtime/Untracked.cs",
        "Runtime/.hidden/resource.bin",
        "Editor/Inspector.cs",
        "Tests/check.txt",
        "Generator~/nested/source.CS",
        "Generator~/nested/payload.dll.meta",
        "scripts/unity/new.py",
        "Directory.Build.props",
    )
    for relative in extras:
        write(tmp_path / relative, relative.encode())
    for relative in (
        "Generator~/bin/skip.cs",
        "Generator~/OBJ/skip.cs",
        "Generator~/.git/config",
        "Generator~/README.md",
    ):
        write(tmp_path / relative)
    (tmp_path / "ordinary.props").mkdir()
    actual = frozen_files.source_files(tmp_path)
    expected = sorted(set(frozen_files.EXPLICIT_SOURCES) | set(extras))
    assert [row["Path"] for row in actual] == expected
    assert actual == [
        {
            "Path": relative,
            "Sha256": hashlib.sha256((tmp_path / relative).read_bytes()).hexdigest(),
        }
        for relative in expected
    ]


@pytest.mark.parametrize(
    "missing", [*frozen_files.SOURCE_ROOTS, "package.json", "scripts/python~/uv.lock"]
)
def test_missing_source_root_or_required_input_refuses(missing: str, tmp_path: Path) -> None:
    sources(tmp_path)
    path = tmp_path / missing
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink()
    with pytest.raises(FileNotFoundError):
        frozen_files.source_files(tmp_path)


@pytest.mark.parametrize("kind", ["root", "ancestor", "file", "directory", "pruned"])
def test_reparse_paths_refuse(kind: str, tmp_path: Path) -> None:
    sources(tmp_path / "repository")
    root = tmp_path / "repository"
    target = write(tmp_path / "outside" / "file")
    link = {
        "root": tmp_path / "link",
        "ancestor": tmp_path / "link",
        "file": root / "Runtime/link",
        "directory": root / "Editor/link",
        "pruned": root / "Generator~/bin",
    }[kind]
    directory = kind != "file"
    destination = root if kind in ("root", "ancestor") else target.parent if directory else target
    try:
        link.symlink_to(destination, target_is_directory=directory)
    except OSError as error:
        pytest.skip(f"Symlink creation unavailable: {error}")
    if kind == "ancestor":
        root = link / "Runtime"
        with pytest.raises(ValueError, match="reparse"):
            frozen_files.source_files(root)
    else:
        with pytest.raises(ValueError, match="reparse"):
            frozen_files.source_files(link if kind == "root" else root)


@pytest.mark.parametrize("ancestor", ["scripts", ".github"])
def test_source_and_explicit_input_ancestors_refuse_redirects(
    ancestor: str, tmp_path: Path
) -> None:
    root = tmp_path / "repository"
    sources(root)
    original = root / ancestor
    moved = tmp_path / "redirected"
    original.rename(moved)
    try:
        original.symlink_to(moved, target_is_directory=True)
    except OSError as error:
        pytest.skip(f"Symlink creation unavailable: {error}")
    with pytest.raises(ValueError, match="reparse"):
        frozen_files.source_files(root)


def test_same_length_mutation_during_hashing_refuses(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = write(tmp_path / "file", b"before")
    original = hashlib.file_digest

    def mutate(stream: BufferedReader, algorithm: str) -> object:
        digest = original(stream, algorithm)
        path.write_bytes(b"after!")
        return digest

    monkeypatch.setattr(hashlib, "file_digest", mutate)
    with pytest.raises(ValueError, match="changed while hashing"):
        frozen_files.file_hash(path)


def test_source_bound_covers_every_delivered_input(tmp_path: Path) -> None:
    sources(tmp_path)
    for index in range(16_385):
        write(tmp_path / "Runtime" / f"{index}.cs", b"")
    with pytest.raises(ValueError, match="exceeds 16384"):
        frozen_files.source_files(tmp_path)


@pytest.mark.parametrize("missing", ["Runtime", "Editor", "scripts/python~/uv.lock"])
def test_cli_failures_publish_no_partial_json(missing: str, tmp_path: Path) -> None:
    sources(tmp_path)
    path = tmp_path / missing
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink()
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/unity/frozen_files.py"), str(tmp_path)],
        text=True,
        capture_output=True,
        check=False,
        timeout=10,
    )
    assert result.returncode == 1
    assert result.stdout == ""
    assert "WProto source census failed:" in result.stderr


def test_cli_reads_current_bytes_each_time(tmp_path: Path) -> None:
    sources(tmp_path)
    path = write(tmp_path / "Runtime" / "fresh.cs", b"before")
    captures: list[object] = []
    for contents in (b"before", b"after!"):
        path.write_bytes(contents)
        result = subprocess.run(
            [
                sys.executable,
                str(ROOT / "scripts/unity/frozen_files.py"),
                str(tmp_path),
            ],
            text=True,
            capture_output=True,
            check=False,
            timeout=10,
        )
        assert result.returncode == 0, result.stderr
        captures.append(json.loads(result.stdout))
    assert captures[0] != captures[1]
    assert captures[1] == frozen_files.source_files(tmp_path)


def host_sources(root: Path) -> None:
    sources(root)
    write(root / "Runtime/Core/Serialization/WallstopProto/WProtoReader.cs")
    for relative in frozen_files.HOST_TOOL_INPUTS:
        write(root / relative)
    git = shutil.which("git")
    assert git is not None
    subprocess.run([git, "-C", str(root), "init", "--quiet"], check=True, timeout=10)


def test_host_census_preserves_its_distinct_physical_scope(tmp_path: Path) -> None:
    host_sources(tmp_path)
    write(tmp_path / ".gitignore", b"**/bin/\nIgnoredByGit.cs\n")
    extras = (
        "Runtime/Arbitrary.bin",
        "Runtime/IgnoredByGit.cs",
        "Editor/.hidden/file",
        "Tests/OBJ/kept.json",
        "Generator~/README.md",
        "Generator~/BIN/kept.cs",
        ".editorconfig",
    )
    for relative in extras:
        write(tmp_path / relative)
    for relative in ("Runtime/bin/skip.cs", "Editor/obj/skip.cs", "Generator~/.git/skip"):
        write(tmp_path / relative)
    expected = {
        "Runtime/Core/Serialization/WallstopProto/WProtoReader.cs",
        ".gitignore",
        *extras,
        *frozen_files.HOST_TOOL_INPUTS,
    }
    actual = frozen_files.host_source_files(tmp_path)
    assert actual == [
        {"path": relative, "sha256": hashlib.sha256((tmp_path / relative).read_bytes()).hexdigest()}
        for relative in sorted(expected, key=frozen_files.ordinal_key)
    ]


@pytest.mark.parametrize("missing", [*frozen_files.HOST_ROOTS, *frozen_files.HOST_TOOL_INPUTS])
def test_host_census_missing_roots_or_tool_inputs_refuse(missing: str, tmp_path: Path) -> None:
    host_sources(tmp_path)
    path = tmp_path / missing
    if path.is_dir():
        shutil.rmtree(path)
    else:
        path.unlink()
    with pytest.raises((FileNotFoundError, ValueError)):
        frozen_files.host_source_files(tmp_path)


@pytest.mark.parametrize("missing_git", [False, True])
def test_host_census_refuses_missing_git_and_parent_repositories(
    missing_git: bool, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    host_sources(tmp_path)
    if missing_git:

        def no_git(_: str) -> None:
            return None

        monkeypatch.setattr(shutil, "which", no_git)
        with pytest.raises(ValueError, match="Git is required"):
            frozen_files.host_source_files(tmp_path)
    else:
        child = tmp_path / "child"
        host_sources(child)
        shutil.rmtree(child / ".git")
        with pytest.raises(ValueError, match="repository root"):
            frozen_files.host_source_files(child)


def test_host_census_refuses_a_missing_reader_anchor(tmp_path: Path) -> None:
    host_sources(tmp_path)
    (tmp_path / "Runtime/Core/Serialization/WallstopProto/WProtoReader.cs").unlink()
    with pytest.raises(ValueError, match="Missing WProto source"):
        frozen_files.host_source_files(tmp_path)


def test_host_cli_matches_current_physical_inputs(tmp_path: Path) -> None:
    tmp_path = tmp_path / "owner's \u03b4 repository"
    host_sources(tmp_path)
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/unity/frozen_files.py"), str(tmp_path), "--host"],
        text=True,
        capture_output=True,
        check=False,
        timeout=10,
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == frozen_files.host_source_files(tmp_path)


@pytest.mark.parametrize("host", [False, True])
def test_source_changes_after_enumeration_refuse(
    host: bool, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    if host:
        host_sources(tmp_path)
    else:
        sources(tmp_path)
    path = write(tmp_path / "Runtime/raced.cs", b"before")
    original_walk = frozen_files.walk

    def mutate(
        root: Path, *, generator: bool = False, host: bool = False
    ) -> list[tuple[Path, bool, stat_result]]:
        entries = original_walk(root, generator=generator, host=host)
        if root == path.parent:
            path.write_bytes(b"after!")
        return entries

    monkeypatch.setattr(frozen_files, "walk", mutate)
    scan = frozen_files.host_source_files if host else frozen_files.source_files
    with pytest.raises(ValueError, match="changed before hashing"):
        scan(tmp_path)


@pytest.mark.parametrize("host", [False, True])
def test_source_paths_preserve_dotnet_utf16_ordinal_order(host: bool, tmp_path: Path) -> None:
    if host:
        host_sources(tmp_path)
    else:
        sources(tmp_path)
    names = ["Runtime/a.cs", "Runtime/\U00010000.cs", "Runtime/\ue000.cs"]
    for name in reversed(names):
        write(tmp_path / name)
    if host:
        actual = [row["path"] for row in frozen_files.host_source_files(tmp_path)]
    else:
        actual = [row["Path"] for row in frozen_files.source_files(tmp_path)]
    assert [name for name in actual if name in names] == names


def server_request(root: Path) -> dict[str, object]:
    return {"protocol": 1, "requestId": "a" * 32, "operation": "sources", "root": str(root)}


def response_object(line: str) -> dict[str, object]:
    value: object = json.loads(line)
    assert isinstance(value, dict)
    return cast("dict[str, object]", value)


@pytest.mark.parametrize(
    "invalid",
    [
        {"protocol": True},
        {"maximum": True},
        {"root": 7},
        {"operation": "absent"},
        {"unknown": 1},
        {"requestId": "missing"},
        {"declared": None},
    ],
)
def test_worker_refuses_invalid_requests_without_poisoning_the_next_request(
    invalid: dict[str, object], tmp_path: Path
) -> None:
    sources(tmp_path)
    request = server_request(tmp_path)
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/unity/frozen_files.py"), "--serve"],
        input=json.dumps(request | invalid) + "\n" + json.dumps(request) + "\n",
        text=True,
        capture_output=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    responses = [response_object(line) for line in result.stdout.splitlines()]
    assert len(responses) == 2
    assert responses[0]["ok"] is False
    assert "value" not in responses[0]
    assert responses[1]["ok"] is True
    assert responses[0]["processId"] == responses[1]["processId"]
    assert (
        responses[1]["toolSha256"]
        == hashlib.sha256((ROOT / "scripts/unity/frozen_files.py").read_bytes()).hexdigest()
    )


@pytest.mark.parametrize("change_tool", [False, True])
def test_worker_rehashes_inputs_and_refuses_a_changed_implementation(
    change_tool: bool, tmp_path: Path
) -> None:
    script = tmp_path / "frozen_files.py"
    shutil.copyfile(ROOT / "scripts/unity/frozen_files.py", script)
    root = tmp_path / "root with spaces"
    sources(root)
    path = write(root / "Runtime/file", b"before")
    with (
        subprocess.Popen(
            [sys.executable, str(script), "--serve"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        ) as process,
        ThreadPoolExecutor(max_workers=1) as readers,
    ):
        assert process.stdin is not None
        assert process.stdout is not None
        assert process.stderr is not None
        try:
            process.stdin.write(json.dumps(server_request(root)) + "\n")
            process.stdin.flush()
            first = response_object(readers.submit(process.stdout.readline).result(timeout=10))
            assert first["ok"] is True
            if change_tool:
                with script.open("a", encoding="utf-8") as stream:
                    stream.write("\n")
            else:
                path.write_bytes(b"after!")
            process.stdin.write(json.dumps(server_request(root)) + "\n")
            process.stdin.flush()
            second = response_object(readers.submit(process.stdout.readline).result(timeout=10))
            assert second["processId"] == first["processId"]
            if change_tool:
                assert second["ok"] is False
                assert "implementation changed" in str(second["error"])
                assert "value" not in second
            else:
                assert second["ok"] is True
                assert first["value"] != second["value"]
                assert second["value"] == frozen_files.source_files(root)
            process.stdin.close()
            assert process.wait(timeout=20) == 0
            assert process.stderr.read() == ""
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=20)


@pytest.mark.parametrize("message", ["{}", "x" * 8_388_609 + "\n"])
def test_worker_refuses_incomplete_or_oversized_packets_and_exits(message: str) -> None:
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/unity/frozen_files.py"), "--serve"],
        input=message,
        text=True,
        capture_output=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 1
    reply = response_object(result.stdout)
    assert reply["ok"] is False
    assert "incomplete" in str(reply["error"])
    assert "value" not in reply


@pytest.mark.parametrize("changed", ["script", "project", "lock", "delete-script"])
def test_powershell_worker_reuses_fresh_snapshots_restarts_and_stops(
    changed: str, tmp_path: Path
) -> None:
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        pytest.skip("PowerShell is unavailable.")
    toolkit = tmp_path / "toolkit"
    for relative in (
        "scripts/unity/lib/source-census-python.ps1",
        "scripts/unity/frozen_files.py",
        "scripts/python~/pyproject.toml",
        "scripts/python~/uv.lock",
    ):
        target = toolkit / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / relative, target)
    root = tmp_path / "owner's \u03b4 inputs"
    sources(root)
    write(root / "Runtime/file.txt", b"before")
    changed_path = (
        toolkit
        / {
            "script": "scripts/unity/frozen_files.py",
            "delete-script": "scripts/unity/frozen_files.py",
            "project": "scripts/python~/pyproject.toml",
            "lock": "scripts/python~/uv.lock",
        }[changed]
    )

    def literal(path: Path) -> str:
        return "'" + str(path).replace("'", "''") + "'"

    control = tmp_path / "probe.ps1"
    control.write_text(
        """$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. __LIBRARY__
try {
    $first = @(Invoke-WProtoSourceCensus -Operation sources -Root __ROOT__)
    $firstSession = $script:WProtoSourceCensusWorker.SessionId
    [IO.File]::WriteAllText((Join-Path __ROOT__ 'Runtime/file.txt'), 'after!')
    $second = @(Invoke-WProtoSourceCensus -Operation sources -Root __ROOT__)
    $secondSession = $script:WProtoSourceCensusWorker.SessionId
    if (__DELETE__) {
        [IO.File]::Delete(__CHANGED__)
        $refused = $false
        try { Invoke-WProtoSourceCensus -Operation sources -Root __ROOT__ | Out-Null }
        catch { $refused = $true }
        [ordered]@{ refused = $refused; stopped = ($null -eq $script:WProtoSourceCensusWorker) } | ConvertTo-Json
        return
    }
    [IO.File]::AppendAllText(__CHANGED__, "`n")
    $third = @(Invoke-WProtoSourceCensus -Operation sources -Root __ROOT__)
    $thirdSession = $script:WProtoSourceCensusWorker.SessionId
    $ownedId = $script:WProtoSourceCensusWorker.WorkerId
    $owned = [Diagnostics.Process]::GetProcessById($ownedId)
    $started = $owned.StartTime
    $owned.Dispose()
    Stop-WProtoSourceCensusWorker
    $exited = $false
    try {
        $remaining = [Diagnostics.Process]::GetProcessById($ownedId)
        try { $exited = $remaining.HasExited -or $remaining.StartTime -ne $started }
        finally { $remaining.Dispose() }
    } catch [ArgumentException] { $exited = $true }
    [ordered]@{ first = $first; second = $second; third = $third; firstSession = $firstSession;
        secondSession = $secondSession; thirdSession = $thirdSession;
        stopped = ($null -eq $script:WProtoSourceCensusWorker); workerExited = $exited } | ConvertTo-Json -Depth 8
} finally { Stop-WProtoSourceCensusWorker }
""".replace("__LIBRARY__", literal(toolkit / "scripts/unity/lib/source-census-python.ps1"))
        .replace("__ROOT__", literal(root))
        .replace("__CHANGED__", literal(changed_path))
        .replace("__DELETE__", "$true" if changed == "delete-script" else "$false"),
        encoding="utf-8",
    )
    result = subprocess.run(
        [pwsh, "-NoProfile", "-File", str(control)],
        env=os.environ
        | {"UV_PROJECT_ENVIRONMENT": str(tmp_path / "environment"), "PYTHONIOENCODING": "cp1252"},
        text=True,
        encoding="utf-8",
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    reply = response_object(result.stdout)
    if changed == "delete-script":
        assert reply["refused"] is True
        assert reply["stopped"] is True
        return
    assert reply["firstSession"] == reply["secondSession"]
    assert reply["thirdSession"] != reply["secondSession"]
    assert reply["first"] != reply["second"]
    assert reply["third"] == reply["second"]
    assert reply["stopped"] is True
    assert reply["workerExited"] is True
