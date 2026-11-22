"""Hold source completeness, path ownership and immutable hashing during migration."""

from __future__ import annotations

import errno
import hashlib
import json
import mmap
import os
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from types import SimpleNamespace
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
    initial = path.stat()
    os.utime(path, ns=(initial.st_atime_ns, initial.st_mtime_ns - 2_000_000_000))
    original = hashlib.file_digest

    def mutate(stream: BufferedReader, algorithm: str) -> object:
        digest = original(stream, algorithm)
        path.write_bytes(b"after!")
        return digest

    monkeypatch.setattr(hashlib, "file_digest", mutate)
    if sys.platform == "win32":
        with pytest.raises(PermissionError):
            frozen_files.file_hash(path)
        assert path.read_bytes() == b"before"
    else:
        with pytest.raises(ValueError, match="changed while hashing"):
            frozen_files.file_hash(path)


@pytest.mark.skipif(sys.platform != "win32", reason="Windows file sharing contract")
@pytest.mark.parametrize("operation", ["write", "replace", "delete"])
def test_windows_hash_handle_excludes_writes_and_allows_readers(
    operation: str, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = write(tmp_path / "file", b"before")
    replacement = write(tmp_path / "replacement", b"after!")
    original = hashlib.file_digest

    def mutate_path() -> None:
        if operation == "write":
            path.write_bytes(b"after!")
        elif operation == "replace":
            replacement.replace(path)
        else:
            path.unlink()

    def attempt_mutation(stream: BufferedReader, algorithm: str) -> object:
        assert path.read_bytes() == b"before"
        with pytest.raises(PermissionError):
            mutate_path()
        return original(stream, algorithm)

    monkeypatch.setattr(hashlib, "file_digest", attempt_mutation)
    assert frozen_files.file_hash(path) == (hashlib.sha256(b"before").hexdigest(), 6)
    path.write_bytes(b"after!")
    assert path.read_bytes() == b"after!"


@pytest.mark.skipif(sys.platform != "win32", reason="Windows file sharing contract")
def test_windows_hash_refuses_an_existing_writer(tmp_path: Path) -> None:
    path = write(tmp_path / "file", b"before")
    with path.open("r+b"), pytest.raises(PermissionError):
        frozen_files.file_hash(path)
    assert frozen_files.file_hash(path) == (hashlib.sha256(b"before").hexdigest(), 6)


def test_hash_failure_closes_the_owned_handle(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    path = write(tmp_path / "file", b"before")

    def fail(_: BufferedReader, __: str) -> object:
        raise ValueError("injected digest failure")

    monkeypatch.setattr(hashlib, "file_digest", fail)
    with pytest.raises(ValueError, match="injected digest failure"):
        frozen_files.file_hash(path)
    path.write_bytes(b"after!")
    path.unlink()


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


@pytest.mark.parametrize("entrypoint", ["direct", "cli", "worker"])
@pytest.mark.parametrize(
    "name",
    [
        *(
            pytest.param(
                f"repository{suffix}",
                marks=pytest.mark.skipif(
                    sys.platform == "win32",
                    reason="Win32 trims ASCII spaces or rejects control characters",
                ),
            )
            for suffix in (" ", "\t", "\r", "\n", "\r\n")
        ),
        "repository \u00a0",
        pytest.param(
            "repository\udcff",
            marks=pytest.mark.skipif(
                sys.platform == "win32", reason="Non-UTF-8 byte names require POSIX paths"
            ),
            id="non-utf8",
        ),
    ],
)
def test_host_census_preserves_repository_root_names(
    entrypoint: str, name: str, tmp_path: Path
) -> None:
    root = tmp_path / name
    try:
        root.mkdir()
    except OSError as error:
        if error.errno not in (errno.EILSEQ, errno.EINVAL):
            raise
        pytest.skip(f"Filesystem cannot create the repository name: {error}")
    host_sources(root)
    expected = [
        {"path": relative, "sha256": hashlib.sha256((root / relative).read_bytes()).hexdigest()}
        for relative in sorted(
            {
                "Runtime/Core/Serialization/WallstopProto/WProtoReader.cs",
                *frozen_files.HOST_TOOL_INPUTS,
            }
        )
    ]
    actual: object
    if entrypoint == "direct":
        actual = frozen_files.host_source_files(root)
    else:
        arguments = [sys.executable, str(ROOT / "scripts/unity/frozen_files.py")]
        request: str | None = None
        if entrypoint == "worker":
            arguments.append("--serve")
            request = json.dumps(server_request(root) | {"operation": "host-sources"}) + "\n"
        else:
            arguments.extend([str(root), "--host"])
        result = subprocess.run(
            arguments,
            input=request,
            text=True,
            capture_output=True,
            check=False,
            timeout=10,
        )
        assert result.returncode == 0, result.stderr
        if entrypoint == "worker":
            response = response_object(result.stdout)
            assert response["ok"] is True, response
            actual = response["value"]
        else:
            actual = json.loads(result.stdout)
    assert actual == expected


@pytest.mark.parametrize("host", [False, True])
def test_source_changes_after_enumeration_refuse(
    host: bool, tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    if host:
        host_sources(tmp_path)
    else:
        sources(tmp_path)
    path = write(tmp_path / "Runtime/raced.cs", b"before")
    initial = path.stat()
    os.utime(path, ns=(initial.st_atime_ns, initial.st_mtime_ns - 2_000_000_000))
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


def test_player_inventory_keeps_sorted_rows_and_declared_refusal(
    tmp_path: Path,
) -> None:
    digest = hashlib.sha256(b"content").hexdigest()
    write(tmp_path / "one" / "entry.txt", b"content")
    (tmp_path / "empty").mkdir()
    result = frozen_files.frozen_player_inventory(tmp_path, None, 4096)
    assert result == {
        "rows": [
            {"Path": "empty", "Kind": "directory", "Length": None, "Sha256": None},
            {"Path": "one", "Kind": "directory", "Length": None, "Sha256": None},
            {"Path": "one/entry.txt", "Kind": "file", "Length": 7, "Sha256": digest},
        ],
        "error": None,
    }
    declared = [{"Path": "one/entry.txt", "Sha256": "0" * 64}]
    outcome = frozen_files.frozen_player_inventory(tmp_path, declared, 1)
    assert outcome["rows"] == result["rows"][2:]
    assert outcome["error"] == "Frozen declared file hash does not match: one/entry.txt"


@pytest.mark.parametrize("declared", [False, True])
def test_player_inventory_refuses_persistent_same_length_change(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, declared: bool
) -> None:
    first = write(tmp_path / "01.txt", b"AAAA")
    second = write(tmp_path / "02.txt", b"CCCC")
    rows = [
        {"Path": path.name, "Sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        for path in (first, second)
    ]
    original_hash = frozen_files.file_hash

    def hash_then_change(path: Path, observed: stat_result | None = None) -> tuple[str, int]:
        if path == second:
            if sys.platform == "win32":
                with pytest.raises(PermissionError):
                    first.write_bytes(b"BBBB")
            else:
                first.write_bytes(b"BBBB")
        return original_hash(path, observed)

    monkeypatch.setattr(frozen_files, "file_hash", hash_then_change)
    if sys.platform == "win32":
        result = frozen_files.frozen_player_inventory(tmp_path, rows if declared else None, 2)
        assert result["error"] is None
        assert first.read_bytes() == b"AAAA"
    else:
        with pytest.raises(ValueError, match="changed during inventory"):
            frozen_files.frozen_player_inventory(tmp_path, rows if declared else None, 2)
        assert first.read_bytes() == b"BBBB"


def test_player_inventory_request_rechecks_the_checkout(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    write(tmp_path / "file", b"data")
    original_git = frozen_files.git_output
    head_reads = 0

    def git_with_changed_head(root: Path, arguments: list[str]) -> bytes:
        nonlocal head_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            head_reads += 1
            if head_reads == 2:
                return b"0" * 40 + b"\n"
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "git_output", git_with_changed_head)
    request = {
        "protocol": 1,
        "requestId": "1" * 32,
        "operation": "inventory",
        "root": str(tmp_path),
        "repository": str(ROOT),
        "declaredFiles": None,
        "maximumFiles": 4096,
    }
    with pytest.raises(ValueError, match="checkout changed during the request"):
        frozen_files.execute_request(request)
    assert head_reads == 2
    request["repository"] = str(ROOT / "scripts")
    with pytest.raises((ValueError, FileNotFoundError)):
        frozen_files.execute_request(request)


@pytest.mark.parametrize("complete", [False, True])
def test_recorded_inventory_verifies_each_file_until_completion(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, complete: bool
) -> None:
    first = write(tmp_path / "01.txt", b"old!")
    second = write(tmp_path / "02.txt", b"next")
    rows = frozen_files.frozen_player_inventory(tmp_path, None, 2)["rows"]
    frozen_files.verify_recorded_inventory(tmp_path, rows, complete=complete)
    original_hash = frozen_files.file_hash

    def change_after_first_hash(path: Path, observed: stat_result | None = None) -> tuple[str, int]:
        if path == second:
            if sys.platform == "win32":
                with pytest.raises(PermissionError):
                    first.write_bytes(b"new!")
            else:
                first.write_bytes(b"new!")
        return original_hash(path, observed)

    monkeypatch.setattr(frozen_files, "file_hash", change_after_first_hash)
    if sys.platform == "win32":
        frozen_files.verify_recorded_inventory(tmp_path, rows, complete=complete)
        assert first.read_bytes() == b"old!"
        first.write_bytes(b"new!")
    else:
        with pytest.raises(ValueError, match="WProto recorded file changed"):
            frozen_files.verify_recorded_inventory(tmp_path, rows, complete=complete)
    assert first.read_bytes() == b"new!"


def test_recorded_inventory_distinguishes_subsets_from_complete_membership(tmp_path: Path) -> None:
    write(tmp_path / "build" / "player.txt", b"retained")
    (tmp_path / "empty").mkdir()
    rows = frozen_files.frozen_player_inventory(tmp_path, None, 2)["rows"]
    frozen_files.verify_recorded_inventory(tmp_path, rows, complete=True)
    unrecorded = write(tmp_path / "build" / "unrecorded.txt", b"extra")
    frozen_files.verify_recorded_inventory(
        tmp_path, rows, complete=False, check_inputs=unrecorded.touch
    )
    with pytest.raises(ValueError, match="WProto complete inventory membership changed"):
        frozen_files.verify_recorded_inventory(tmp_path, rows, complete=True)
    with pytest.raises(ValueError, match="invalid or repeated relative path"):
        frozen_files.verify_recorded_inventory(tmp_path, [*rows, rows[0]], complete=False)


def recorded_request(
    tmp_path: Path, contents: bytes = b"retained"
) -> tuple[dict[str, object], Path, Path]:
    payload = tmp_path / "retained"
    player = write(payload / "nested" / "player.txt", contents)
    rows = frozen_files.frozen_player_inventory(payload, None, 1)["rows"]
    records = write(tmp_path / "rows.json", json.dumps(rows).encode())
    request: dict[str, object] = {
        "protocol": 1,
        "requestId": "1" * 32,
        "operation": "verify-recorded",
        "root": str(payload),
        "repository": str(ROOT),
        "recordsPath": str(records),
        "recordsSha256": hashlib.sha256(records.read_bytes()).hexdigest(),
        "complete": True,
    }
    return request, player, records


@pytest.mark.parametrize("changed", ["receipt", "checkout", "hash"])
def test_recorded_inventory_request_has_bound_inputs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, changed: str
) -> None:
    request, path, receipt = recorded_request(tmp_path)
    digest = request["recordsSha256"]
    assert frozen_files.execute_request(request) is None
    if changed == "hash":
        request["recordsSha256"] = "0" * 64
        with pytest.raises(ValueError, match="does not match its input hash"):
            frozen_files.execute_request(request)
        return

    original_hash = frozen_files.file_hash
    original_git = frozen_files.git_output
    head_reads = 0
    changed_receipt = receipt.read_bytes().replace(b"player.txt", b"Player.txt")

    def try_receipt_write(candidate: Path, observed: stat_result | None = None) -> tuple[str, int]:
        if changed == "receipt" and candidate == path:
            if sys.platform == "win32":
                with pytest.raises(PermissionError):
                    receipt.write_bytes(changed_receipt)
            else:
                receipt.write_bytes(changed_receipt)
        return original_hash(candidate, observed)

    def read_checkout(root: Path, arguments: list[str]) -> bytes:
        nonlocal head_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            head_reads += 1
            if changed == "checkout" and head_reads == 2:
                return b"0" * 40 + b"\n"
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "file_hash", try_receipt_write)
    monkeypatch.setattr(frozen_files, "git_output", read_checkout)
    if changed == "checkout":
        with pytest.raises(ValueError, match="checkout changed during the request"):
            frozen_files.execute_request(request)
        assert head_reads == 2
    elif sys.platform == "win32":
        assert frozen_files.execute_request(request) is None
        assert hashlib.sha256(receipt.read_bytes()).hexdigest() == digest
    else:
        with pytest.raises(ValueError, match="request changed during verification"):
            frozen_files.execute_request(request)
        assert receipt.read_bytes() == changed_receipt


@pytest.mark.skipif(sys.platform != "linux", reason="Linux filesystem change observation")
@pytest.mark.parametrize("complete", [False, True])
def test_recorded_inventory_refuses_change_after_earlier_final_stat(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, complete: bool
) -> None:
    first = write(tmp_path / "01.txt", b"old!")
    second = write(tmp_path / "02.txt", b"next")
    rows = frozen_files.frozen_player_inventory(tmp_path, None, 2)["rows"]
    original_stat = frozen_files.recorded_stat
    second_stats = 0

    def change_after_earlier_observation(root: Path, path: Path) -> stat_result:
        nonlocal second_stats
        result = original_stat(root, path)
        if path == second:
            second_stats += 1
            if second_stats == 2:
                first.write_bytes(b"new!")
        return result

    monkeypatch.setattr(frozen_files, "recorded_stat", change_after_earlier_observation)
    with pytest.raises(ValueError, match="recorded file changed"):
        frozen_files.verify_recorded_inventory(tmp_path, rows, complete=complete)
    assert second_stats == 2
    assert first.read_bytes() == b"new!"


@pytest.mark.skipif(
    sys.platform not in ("linux", "win32"), reason="Requires filesystem observation or read sharing"
)
@pytest.mark.parametrize("phase", ["records", "checkout", "receipt-after-hash"])
def test_recorded_inventory_keeps_files_observed_through_request_checks(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, phase: str
) -> None:
    request, player, records = recorded_request(tmp_path, b"before")
    assert frozen_files.execute_request(request) is None
    original_hash = frozen_files.file_hash
    original_git = frozen_files.git_output
    checkout_reads = 0
    attempts = 0

    target = records if phase == "receipt-after-hash" else player
    original_contents = target.read_bytes()
    changed_contents = b"x" * len(original_contents)

    def change_input() -> None:
        nonlocal attempts
        attempts += 1
        if sys.platform == "win32":
            with pytest.raises(PermissionError):
                target.write_bytes(changed_contents)
        else:
            target.write_bytes(changed_contents)

    def check_receipt(path: Path, observed: stat_result | None = None) -> tuple[str, int]:
        if phase == "records" and path == records:
            change_input()
        return original_hash(path, observed)

    def check_checkout(root: Path, arguments: list[str]) -> bytes:
        nonlocal checkout_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            checkout_reads += 1
            if phase != "records" and checkout_reads == 2:
                change_input()
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "file_hash", check_receipt)
    monkeypatch.setattr(frozen_files, "git_output", check_checkout)
    outcome = frozen_files.execute_request(request)
    assert attempts == 1
    assert checkout_reads == 2
    if sys.platform == "win32":
        assert outcome is None
        assert target.read_bytes() == original_contents
        target.write_bytes(changed_contents)
    else:
        assert outcome == "WProto recorded file changed during verification."
    assert target.read_bytes() == changed_contents


def test_recorded_inventory_rechecks_checkout_after_the_final_scan(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    request, player, _ = recorded_request(tmp_path)
    assert frozen_files.execute_request(request) is None
    original_git = frozen_files.git_output
    original_stat = frozen_files.recorded_stat
    observations = 0

    def show_checkout_change(root: Path, arguments: list[str]) -> bytes:
        if observations == 2 and arguments == ["rev-parse", "--verify", "HEAD"]:
            return b"0" * 40 + b"\n"
        return original_git(root, arguments)

    def mark_final_observation(root: Path, path: Path) -> stat_result:
        nonlocal observations
        if path == player:
            observations += 1
        return original_stat(root, path)

    monkeypatch.setattr(frozen_files, "git_output", show_checkout_change)
    monkeypatch.setattr(frozen_files, "recorded_stat", mark_final_observation)
    with pytest.raises(ValueError, match="checkout changed during the request"):
        frozen_files.execute_request(request)
    assert observations == 2


@pytest.mark.skipif(sys.platform != "linux", reason="Linux mapped writes and change observation")
@pytest.mark.parametrize("changed", ["player", "receipt"])
@pytest.mark.parametrize(
    ("already_dirty", "close_before_recheck"), [(False, True), (True, True), (True, False)]
)
def test_recorded_inventory_rechecks_mapped_writes_during_checkout(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    changed: str,
    already_dirty: bool,
    close_before_recheck: bool,
) -> None:
    request, player, receipt = recorded_request(tmp_path)
    target = player if changed == "player" else receipt
    content = b"x" * target.stat().st_size
    original_git = frozen_files.git_output
    checkout_reads = 0

    def check_checkout(root: Path, arguments: list[str]) -> bytes:
        nonlocal checkout_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            checkout_reads += 1
            if checkout_reads == 2:
                mapped[:] = content
                if not already_dirty:
                    mapped.flush()
                if close_before_recheck:
                    mapped.close()
        return original_git(root, arguments)

    with target.open("r+b") as stream:
        mapped = mmap.mmap(stream.fileno(), 0)
    with mapped:
        if already_dirty:
            mapped[:1] = mapped[:1]
        before = frozen_files.identity(target.stat())
        monkeypatch.setattr(frozen_files, "git_output", check_checkout)
        outcome = frozen_files.execute_request(request)
        if already_dirty:
            assert frozen_files.identity(target.stat()) == before
    assert checkout_reads == 2
    assert target.read_bytes() == content
    assert (
        outcome
        == {
            "player": "WProto recorded file changed.",
            "receipt": "WProto recorded receipt changed during verification.",
        }[changed]
    )


@pytest.mark.skipif(sys.platform != "linux", reason="Linux file leases")
def test_recorded_inventory_retains_earlier_files_through_final_hashes(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    request, first, receipt = recorded_request(tmp_path)
    root = first.parent.parent
    later = write(root / "zz-player.txt")
    rows = frozen_files.frozen_player_inventory(root, None, 2)["rows"]
    receipt.write_bytes(json.dumps(rows).encode())
    request["recordsSha256"] = hashlib.sha256(receipt.read_bytes()).hexdigest()
    original_hash = frozen_files.file_hash
    later_hashes = 0
    write_open_refused = False

    def check_hash(path: Path, info: stat_result | None = None) -> tuple[str, int]:
        nonlocal later_hashes, write_open_refused
        if path == later:
            later_hashes += 1
            if later_hashes == 2:
                try:
                    descriptor = os.open(first, os.O_WRONLY | frozen_files.INOTIFY_OPEN_FLAGS)
                except BlockingIOError:
                    write_open_refused = True
                else:
                    os.close(descriptor)
        return original_hash(path, info)

    monkeypatch.setattr(frozen_files, "file_hash", check_hash)
    outcome = frozen_files.execute_request(request)
    assert later_hashes == 2
    assert write_open_refused
    assert outcome == "WProto recorded file changed during verification."
    first.write_bytes(b"after lease release")


@pytest.mark.skipif(
    sys.platform not in ("linux", "win32"), reason="Requires directory change notifications"
)
@pytest.mark.parametrize("complete", [False, True])
def test_recorded_inventory_observes_nested_membership_through_checkout(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, complete: bool
) -> None:
    request, player, _ = recorded_request(tmp_path)
    request["complete"] = complete
    assert frozen_files.execute_request(request) is None
    original_git = frozen_files.git_output
    checkout_reads = 0
    extra = player.with_name("extra.txt")

    def check_checkout(root: Path, arguments: list[str]) -> bytes:
        nonlocal checkout_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            checkout_reads += 1
            if checkout_reads == 2:
                extra.touch()
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "git_output", check_checkout)
    outcome = frozen_files.execute_request(request)
    assert checkout_reads == 2
    assert extra.is_file()
    if not complete:
        assert outcome is None
    elif sys.platform == "win32":
        assert outcome == "WProto complete inventory membership changed."
    else:
        assert outcome == "WProto recorded file changed during verification."


@pytest.mark.skipif(sys.platform == "win32", reason="Exercises the portable filesystem observation")
@pytest.mark.parametrize(
    ("changed", "complete"),
    [("player", True), ("receipt", True), ("membership", True), ("membership", False)],
)
def test_portable_recorded_inventory_rechecks_after_request_inputs(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, changed: str, complete: bool
) -> None:
    request, player, receipt = recorded_request(tmp_path)
    request["complete"] = complete
    target = {
        "player": player,
        "receipt": receipt,
        "membership": player.with_name("extra.txt"),
    }[changed]
    content = b"x" * (target.stat().st_size if target.exists() else 1)
    monkeypatch.setattr(frozen_files, "INOTIFY_OPEN_FLAGS", 0)
    original_git = frozen_files.git_output
    checkout_reads = 0

    def check_checkout(root: Path, arguments: list[str]) -> bytes:
        nonlocal checkout_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            checkout_reads += 1
            if checkout_reads == 2:
                target.write_bytes(content)
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "git_output", check_checkout)
    outcome = frozen_files.execute_request(request)
    assert checkout_reads == 2
    assert target.read_bytes() == content
    expected = {
        "player": "WProto recorded file changed.",
        "receipt": "WProto recorded receipt changed during verification.",
        "membership": "WProto complete inventory membership changed.",
    }[changed]
    assert outcome == (expected if complete else None)


@pytest.mark.parametrize("change", ["none", "parent", "file"])
def test_recorded_request_binds_the_file_inside_an_aliased_temp_directory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, change: str
) -> None:
    request, _, receipt = recorded_request(tmp_path / "original")
    alias = tmp_path / "native-temp"
    try:
        alias.symlink_to(receipt.parent, target_is_directory=True)
        if change == "file":
            (receipt.parent / "alias.json").symlink_to(receipt)
    except OSError as error:
        pytest.skip(f"Symlink creation unavailable: {error}")
    request["recordsPath"] = str(alias / ("alias.json" if change == "file" else receipt.name))
    if change == "file":
        with pytest.raises(ValueError, match="reparse ancestor"):
            frozen_files.execute_request(request)
        return
    assert frozen_files.execute_request(request) is None
    if change == "none":
        return

    replacement = write(tmp_path / "replacement" / receipt.name, receipt.read_bytes())
    original_git = frozen_files.git_output
    checkout_reads = 0

    def replace_alias(root: Path, arguments: list[str]) -> bytes:
        nonlocal checkout_reads
        if arguments == ["rev-parse", "--verify", "HEAD"]:
            checkout_reads += 1
            if checkout_reads == 2:
                alias.unlink()
                alias.symlink_to(replacement.parent, target_is_directory=True)
        return original_git(root, arguments)

    monkeypatch.setattr(frozen_files, "git_output", replace_alias)
    with pytest.raises(ValueError, match="request directory changed during verification"):
        frozen_files.execute_request(request)
    assert checkout_reads == 2


@pytest.mark.parametrize(
    ("scope", "replacement_is_file", "message"),
    [
        ("root", False, "WProto recorded inventory root changed"),
        ("nested", False, "WProto recorded directory changed kind"),
        ("nested", True, "WProto recorded directory changed kind"),
    ],
)
def test_recorded_subset_refuses_a_directory_replaced_during_input_checks(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    scope: str,
    replacement_is_file: bool,
    message: str,
) -> None:
    root = tmp_path / "retained"
    (root / "nested").mkdir(parents=True)
    rows = [{"Path": "nested", "Kind": "directory", "Length": None, "Sha256": None}]
    if sys.platform != "win32":
        monkeypatch.setattr(frozen_files, "sys", SimpleNamespace(platform="win32"))
        monkeypatch.setattr(frozen_files, "INOTIFY_OPEN_FLAGS", 0)
    frozen_files.verify_recorded_inventory(root, rows, complete=False)

    replaced = root if scope == "root" else root / "nested"
    previous = replaced.with_name("previous")
    nested = root / "nested"

    def replace_directory() -> None:
        replaced.rename(previous)
        if replacement_is_file:
            nested.touch()
        else:
            nested.mkdir(parents=True)

    with pytest.raises(ValueError, match=message):
        frozen_files.verify_recorded_inventory(
            root, rows, complete=False, check_inputs=replace_directory
        )
    assert previous.is_dir()
    assert nested.is_file() if replacement_is_file else nested.is_dir()


def test_recorded_subset_refuses_duplicate_normalized_paths(tmp_path: Path) -> None:
    write(tmp_path / "nested" / "player.txt")
    rows = frozen_files.frozen_player_inventory(tmp_path, None, 2)["rows"]
    file = next(row for row in rows if row["Kind"] == "file")
    alias = {**file, "Path": "nested//player.txt"}
    frozen_files.verify_recorded_inventory(tmp_path, [alias], complete=False)
    with pytest.raises(ValueError, match="invalid or repeated relative path"):
        frozen_files.verify_recorded_inventory(tmp_path, [file, alias], complete=False)
