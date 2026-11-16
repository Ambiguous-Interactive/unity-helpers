"""Exercise frozen hook-mode decisions and blob-preserving coordinated repairs."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import hook_permissions as permissions

FIXTURE = cast(
    "dict[str, object]",
    json.loads((Path(__file__).parent / "fixtures/hook-permissions-v1.json").read_bytes()),
)
CASES = cast("list[dict[str, object]]", FIXTURE["cases"])


@pytest.fixture(autouse=True)
def isolate_git(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    for key in list(os.environ):
        if key.startswith("GIT_"):
            monkeypatch.delenv(key, raising=False)
    for scope in ("GLOBAL", "SYSTEM"):
        target = tmp_path / scope
        target.write_bytes(b"")
        monkeypatch.setenv("GIT_CONFIG_" + scope, str(target))


def git(root: Path, *arguments: str, data: bytes | None = None) -> bytes:
    executable = shutil.which("git")
    assert executable is not None
    return subprocess.run(
        [executable, "-C", str(root), *arguments], input=data, capture_output=True, check=True
    ).stdout


def prepare(root: Path, files: dict[str, str] | None) -> None:
    root.mkdir()
    git(root, "init", "--quiet")
    if files is None:
        return
    (root / ".githooks").mkdir()
    for name, mode in files.items():
        target = root / ".githooks" / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(b"#!/usr/bin/env bash\nexit 0\n")
        relative = target.relative_to(root).as_posix()
        git(root, "add", "--", relative)
        git(
            root,
            "update-index",
            "--chmod=" + ("+" if mode == "100755" else "-") + "x",
            "--",
            relative,
        )


@pytest.mark.parametrize("case", CASES, ids=lambda case: str(case["name"]))
def test_frozen_original_cli_decisions(
    case: dict[str, object], tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    root = tmp_path / "repo"
    prepare(root, cast("dict[str, str] | None", case["files"]))
    status = permissions.main(["--repo-root", str(root), *cast("list[str]", case["arguments"])])
    output = capsys.readouterr().out
    checked = re.search(r"Files checked: (\d+)", output)
    assert status == case["status"]
    assert (int(checked[1]) if checked else None) == case["checked"]


@pytest.mark.parametrize("flag", ["normal", "assume", "skip", "both", "intent"])
def test_mode_repair_preserves_staged_content_and_index_flags(tmp_path: Path, flag: str) -> None:
    root = tmp_path / "repo"
    prepare(root, {})
    hook = root / ".githooks/pre-commit"
    hook.write_bytes(b"staged\n")
    git(root, "add", "-N" if flag == "intent" else "--", ".githooks/pre-commit")
    if flag != "intent":
        git(root, "update-index", "--chmod=-x", ".githooks/pre-commit")
    if flag in ("assume", "both"):
        git(root, "update-index", "--assume-unchanged", ".githooks/pre-commit")
    if flag in ("skip", "both"):
        git(root, "update-index", "--skip-worktree", ".githooks/pre-commit")
    before = (
        git(root, "rev-parse", ":.githooks/pre-commit"),
        git(root, "ls-files", "-v", "-z"),
        git(root, "ls-files", "--debug"),
    )
    hook.write_bytes(b"unstaged\n")
    assert permissions.validate(root)[0] == 1
    status, lines = permissions.validate(root, fix=True)
    assert status == 0, lines
    assert (
        git(root, "rev-parse", ":.githooks/pre-commit"),
        git(root, "ls-files", "-v", "-z"),
        git(root, "ls-files", "--debug"),
    ) == before
    assert git(root, "ls-files", "--stage").startswith(b"100755 ")
    assert hook.read_bytes() == b"unstaged\n"


@pytest.mark.parametrize(
    "name", ["pre commit", "pre[commit]", "pré-commit", "nested.name/pre-commit"]
)
def test_literal_paths_survive_mode_repairs(tmp_path: Path, name: str) -> None:
    root = tmp_path / "repo"
    prepare(root, {name: "100644"})
    (root / ".githooks" / name).write_bytes(b"unstaged\n")
    before = git(root, "rev-parse", ":.githooks/" + name)
    status, lines = permissions.validate(root, fix=True)
    assert status == 0, lines
    assert git(root, "rev-parse", ":.githooks/" + name) == before
    assert permissions.validate(root)[0] == 0


def test_alternate_index_is_repaired_without_changing_primary(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    primary = root / ".git/index"
    before = primary.read_bytes()
    alternate = tmp_path / "alternate index"
    alternate.write_bytes(before)
    monkeypatch.setenv("GIT_INDEX_FILE", str(alternate))
    status, lines = permissions.validate(root, fix=True)
    assert status == 0, lines
    assert git(root, "ls-files", "--stage").startswith(b"100755 ")
    assert primary.read_bytes() == before


def test_existing_index_lock_is_retained_and_failure_is_visible(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    before = (root / ".git/index").read_bytes()
    lock = root / ".git/index.lock"
    lock.write_bytes(b"another writer")
    for name, value in {"GIT_LOCK_MAX_ATTEMPTS": "1", "GIT_LOCK_WAIT_TIMEOUT_MS": "10"}.items():
        monkeypatch.setenv(name, value)
    status, lines = permissions.validate(root, fix=True)
    assert status == 1
    assert any("repair failed" in line for line in lines)
    assert lock.read_bytes() == b"another writer"
    assert (root / ".git/index").read_bytes() == before


def test_unmerged_entries_cannot_report_success(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100755"})
    oid = git(root, "rev-parse", ":.githooks/pre-commit").strip()
    git(root, "update-index", "--force-remove", ".githooks/pre-commit")
    records = b"".join(
        b"100755 " + oid + b" " + str(stage).encode() + b"\t.githooks/pre-commit\n"
        for stage in (1, 2, 3)
    )
    git(root, "update-index", "--index-info", data=records)
    before = (root / ".git/index").read_bytes()
    assert permissions.validate(root)[0] == 1
    assert permissions.validate(root, fix=True)[0] == 1
    assert (root / ".git/index").read_bytes() == before


@pytest.mark.parametrize("code", [2, 128])
def test_git_read_errors_fail_closed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, code: int
) -> None:
    root = tmp_path / "repo"
    prepare(root, {})

    def broken(
        _executable: str, _root: Path, *_arguments: str
    ) -> subprocess.CompletedProcess[bytes]:
        return subprocess.CompletedProcess([], code, b"", b"fixture read failure")

    monkeypatch.setattr(permissions, "run_git", broken)
    status, lines = permissions.validate(root)
    assert status == 1
    assert any(f"exit {code}" in line for line in lines)


def test_missing_native_coordinator_fails_only_when_repair_needed(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    original = shutil.which

    def which(name: str) -> str | None:
        return None if name == "pwsh" else original(name)

    monkeypatch.setattr(shutil, "which", which)
    assert permissions.validate(root, fix=True)[0] == 1
    git(root, "update-index", "--chmod=+x", ".githooks/pre-commit")
    assert permissions.validate(root, fix=True)[0] == 0


def test_one_batch_preserves_mixed_flags_and_all_staged_blobs(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644", "pre push": "100644", "pre-merge": "100644"})
    git(root, "update-index", "--assume-unchanged", ".githooks/pre-commit")
    git(root, "update-index", "--skip-worktree", ".githooks/pre push")
    before = (git(root, "ls-files", "-v", "-z"), git(root, "ls-files", "--debug"))
    blobs = [
        git(root, "rev-parse", ":" + path)
        for path in [".githooks/pre-commit", ".githooks/pre push", ".githooks/pre-merge"]
    ]
    for file in (root / ".githooks").iterdir():
        file.write_bytes(b"unstaged\n")
    status, lines = permissions.validate(root, fix=True)
    assert status == 0, lines
    assert (git(root, "ls-files", "-v", "-z"), git(root, "ls-files", "--debug")) == before
    assert [
        git(root, "rev-parse", ":" + path)
        for path in [".githooks/pre-commit", ".githooks/pre push", ".githooks/pre-merge"]
    ] == blobs


def test_linked_worktree_repair_leaves_main_index_untouched(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    git(
        root,
        "-c",
        "user.name=Fixture",
        "-c",
        "user.email=fixture@example.com",
        "commit",
        "--quiet",
        "-m",
        "fixture",
    )
    primary = (root / ".git/index").read_bytes()
    linked = tmp_path / "linked tree"
    git(root, "worktree", "add", "--quiet", "--detach", str(linked), "HEAD")
    blob = git(linked, "rev-parse", ":.githooks/pre-commit")
    (linked / ".githooks/pre-commit").write_bytes(b"unstaged\n")
    status, lines = permissions.validate(linked, fix=True)
    assert status == 0, lines
    assert git(linked, "rev-parse", ":.githooks/pre-commit") == blob
    assert (root / ".git/index").read_bytes() == primary


@pytest.mark.skipif(
    os.name == "nt", reason="These literal filename characters are unavailable on Windows."
)
@pytest.mark.parametrize("name", ["pre\tcommit", "pre\ncommit", 'pre"commit'])
def test_control_and_quote_characters_are_literal_paths(tmp_path: Path, name: str) -> None:
    root = tmp_path / "repo"
    prepare(root, {name: "100644"})
    before = git(root, "rev-parse", ":.githooks/" + name)
    (root / ".githooks" / name).write_bytes(b"unstaged\n")
    status, lines = permissions.validate(root, fix=True)
    assert status == 0, lines
    assert git(root, "rev-parse", ":.githooks/" + name) == before


@pytest.mark.parametrize(
    "payload",
    [
        b"100644 hash 0\t.githooks/pre-commit",
        b"invalid\0",
        b"100644 xyz 0\t.githooks/pre-commit\0",
        b"100644 " + b"a" * 40 + b" 5\t.githooks/pre-commit\0",
    ],
)
def test_malformed_index_data_cannot_pass(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, payload: bytes
) -> None:
    root = tmp_path / "repo"
    prepare(root, {})

    def broken(
        _executable: str, _root: Path, *_arguments: str
    ) -> subprocess.CompletedProcess[bytes]:
        return subprocess.CompletedProcess([], 0, payload, b"")

    monkeypatch.setattr(permissions, "run_git", broken)
    status, lines = permissions.validate(root)
    assert status == 1
    assert any("Error:" in line for line in lines)


def test_frozen_receipt_has_positive_and_negative_subjects() -> None:
    assert len(CASES) == 16
    assert len(str(FIXTURE["original_sha256"])) == 64
    assert any(case["status"] == 0 and case["checked"] for case in CASES)
    assert any(case["status"] == 1 and case["checked"] for case in CASES)


@pytest.mark.parametrize(
    "launcher",
    ["scripts/validate-hook-permissions.sh", "scripts/tests/test-validate-hook-permissions.sh"],
)
def test_legacy_shell_launchers_work_outside_the_repository(tmp_path: Path, launcher: str) -> None:
    bash = shutil.which("bash")
    if bash is None:
        pytest.skip("Bash is unavailable for the compatibility entry point.")
    probe = subprocess.run(
        [
            bash,
            "-c",
            'test -f "$1"',
            "fixture",
            (permissions.ROOT / "scripts/hook_permissions.py").as_posix(),
        ],
        check=False,
        capture_output=True,
    )
    if probe.returncode:
        pytest.skip("The available Bash cannot access host files.")
    command = [bash, str(permissions.ROOT / launcher)]
    if "scripts/tests/" in launcher:
        command.extend(["-k", "frozen_receipt_has_positive"])
    else:
        root = tmp_path / "repo"
        prepare(root, {"pre-commit": "100755"})
        command.extend(["--repo-root", str(root)])
    result = subprocess.run(command, cwd=tmp_path, check=False, capture_output=True, timeout=60)
    assert result.returncode == 0, result.stdout + result.stderr
    assert (
        b"1 passed" in result.stdout
        if "scripts/tests/" in launcher
        else b"All hook entrypoints have executable permissions." in result.stdout
    )


@pytest.mark.parametrize("entry_point", ["validate", "main"])
def test_unavailable_directory_metadata_fails_visibly(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    entry_point: str,
) -> None:
    root = tmp_path / "repo"
    prepare(root, {})

    def denied(_path: Path) -> bool:
        raise PermissionError("fixture directory access denied")

    monkeypatch.setattr(Path, "is_dir", denied)
    if entry_point == "validate":
        status, lines = permissions.validate(root)
        assert status == 1
        assert any("access denied" in line for line in lines)
    else:
        assert permissions.main(["--repo-root", str(root)]) == 1
        assert "directory" in capsys.readouterr().out


def test_missing_git_is_a_visible_failure(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    def missing(_name: str) -> None:
        return None

    monkeypatch.setattr(shutil, "which", missing)
    status, lines = permissions.validate(tmp_path)
    assert status == 1
    assert "git is not available" in "\\n".join(lines)


def test_git_read_deadline_is_a_visible_failure(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(root, {})

    def timeout(
        _executable: str, _root: Path, *_arguments: str
    ) -> subprocess.CompletedProcess[bytes]:
        raise subprocess.TimeoutExpired("fixture git", 30)

    monkeypatch.setattr(permissions, "run_git", timeout)
    status, lines = permissions.validate(root)
    assert status == 1
    assert "timed out" in "\\n".join(lines)


def test_successful_child_without_a_mode_change_cannot_pass(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    before = (root / ".git/index").read_bytes()

    def no_op(_root: Path, _paths: list[str]) -> None:
        return None

    monkeypatch.setattr(permissions, "repair", no_op)
    status, lines = permissions.validate(root, fix=True)
    assert status == 1
    assert any("inspect the index before retrying" in line for line in lines)
    assert (root / ".git/index").read_bytes() == before


@pytest.mark.skipif(
    os.name == "nt",
    reason="POSIX symbolic link fixture; native Windows mode controls run independently.",
)
def test_symbolic_hook_is_not_replaced_by_a_regular_file(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {})
    (root / "target").write_bytes(b"target")
    hook = root / ".githooks/pre-commit"
    hook.symlink_to("../target")
    git(root, "add", "--", ".githooks/pre-commit")
    before = (root / ".git/index").read_bytes()
    assert permissions.validate(root, fix=True)[0] == 1
    assert hook.is_symlink()
    assert (root / ".git/index").read_bytes() == before


def test_native_adapter_preserves_a_leading_dash_path(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {})
    file = root / "-hook"
    file.write_bytes(b"staged")
    git(root, "add", "--", "-hook")
    git(root, "update-index", "--chmod=-x", "--", "-hook")
    blob = git(root, "rev-parse", ":-hook")
    file.write_bytes(b"unstaged")
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [pwsh, "-NoProfile", "-File", str(permissions.ROOT / "scripts/set-hook-index-mode.ps1")],
        cwd=root,
        input=json.dumps({"files": ["-hook"]}).encode(),
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert git(root, "rev-parse", ":-hook") == blob
    assert git(root, "ls-files", "--stage").startswith(b"100755 ")


def test_native_mode_and_content_update_switches_cannot_mix(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    prepare(root, {"pre-commit": "100644"})
    before = (root / ".git/index").read_bytes()
    probe = tmp_path / "incompatible.ps1"
    probe.write_text(
        'param([string]$Helper)\n. $Helper\nexit (Invoke-GitAddWithRetry -Items @(".githooks/pre-commit") -UpdateOnly -ExecutableOnly)\n',
        encoding="utf-8",
        newline="\n",
    )
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(probe),
            str(permissions.ROOT / "scripts/git-staging-helpers.ps1"),
        ],
        cwd=root,
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 64, result.stdout + result.stderr
    assert (root / ".git/index").read_bytes() == before
