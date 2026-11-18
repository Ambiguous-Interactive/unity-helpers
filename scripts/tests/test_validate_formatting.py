"""Exercise formatter commands, final newline discovery and staged attribute safety."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_formatting as validator

if TYPE_CHECKING:
    from collections.abc import Iterator
    from contextlib import AbstractContextManager

ROOT = Path(__file__).resolve().parents[2]
POLICY = "scripts/validate_formatting.py"
FORMAT_LAUNCHER = "scripts/validate-formatting.sh"
NEWLINE_LAUNCHER = "scripts/tests/test-final-newline.sh"
PROJECT = "scripts/python~"
NODE_FIXTURE = """const fs = require("node:fs");
const args = process.argv.slice(2);
fs.appendFileSync("formatter-calls.jsonl", JSON.stringify(args) + "\\n");
if (process.env.NEWLINE_FORMATTER_FAULT && args[0] === "--check" && args[2] === "**/*.{md,markdown}") {
  process.exitCode = 1;
}
if (args[0] === "--list-different") {
  console.log("sample.md");
  process.exitCode = process.env.NEWLINE_FORMATTER_FAULT === "listing-error" ? 1 : 0;
}
if (args[0] === "--write") {
  console.log("wrote " + args[2]);
  if (process.env.NEWLINE_FORMATTER_FAULT === "write-error" && args[2] === "**/*.{md,markdown}") {
    process.exitCode = 1;
  }
}
"""


def environment(**updates: str) -> dict[str, str]:
    return {
        **os.environ,
        "GIT_CONFIG_GLOBAL": os.devnull,
        "GIT_CONFIG_NOSYSTEM": "1",
        **updates,
    }


def git(root: Path, *arguments: str) -> bytes:
    executable = shutil.which("git")
    assert executable is not None
    result = subprocess.run(
        [executable, *arguments],
        cwd=root,
        env=environment(),
        check=True,
        capture_output=True,
        timeout=20,
    )
    return result.stdout


def setup(root: Path, *, checkout: bool = True) -> None:
    (root / "scripts").mkdir()
    shutil.copyfile(ROOT / POLICY, root / POLICY)
    (root / "scripts/run-prettier.js").write_text(NODE_FIXTURE, encoding="utf-8")
    (root / "node_modules").mkdir()
    if checkout:
        git(root, "init", "--quiet")
        git(root, "config", "core.autocrlf", "false")


def run_policy(root: Path, *arguments: str, **overrides: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(root / POLICY), *arguments],
        cwd=root.parent,
        env=environment(**overrides),
        check=False,
        capture_output=True,
        timeout=60,
    )


def write(root: Path, name: str, content: bytes) -> Path:
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    return path


def read_calls(root: Path) -> list[list[str]]:
    return [
        cast("list[str]", json.loads(line))
        for line in (root / "formatter-calls.jsonl").read_text("utf-8").splitlines()
    ]


def test_final_newline_uses_tracked_paths_and_explicit_config(tmp_path: Path) -> None:
    setup(tmp_path)
    write(tmp_path, "package.json", b"{}")
    write(tmp_path, ".yml", b"head\n")
    write(tmp_path, "empty.txt", b"")
    write(tmp_path, "sub/z.cs", b"head")
    write(tmp_path, "untracked.md", b"not selected")
    write(tmp_path, "site/vendored.txt", b"not selected")
    git(tmp_path, "add", "--", ".yml", "empty.txt", "sub/z.cs", "site/vendored.txt")
    before = (tmp_path / ".git/index").read_bytes()
    result = run_policy(tmp_path, "newline", "--verbose")
    assert result.returncode == 1
    assert result.stderr == b""
    report = result.stdout
    assert b"Files checked: 3" in report
    assert b"package.json (LF)" in report
    assert b"sub/z.cs (LF)" in report
    assert b"Detected: LF | Bytes: 2 | Sources: config-list" in report
    assert b"Tail hex: 7b 7d" in report
    assert rb"printf '\r\n'" in report
    assert b"Sources: glob:*.cs" in report
    assert b"empty.txt" not in report
    assert b"vendored.txt" not in report
    assert b"untracked.md" not in report
    assert (tmp_path / ".git/index").read_bytes() == before


@pytest.mark.parametrize(
    ("policy", "style", "wanted"),
    [
        ("-text", b"line", b"line"),
        ("binary", b"line", b"line"),
        ("!text", b"line", b"line\n"),
        ("text", b"line\r\nlast", b"line\r\nlast\r\n"),
    ],
)
def test_repair_preserves_staged_attributes_bytes_and_index(
    tmp_path: Path, policy: str, style: bytes, wanted: bytes
) -> None:
    setup(tmp_path)
    name = "captured \u03bb sample.txt"
    attrs = write(tmp_path, ".gitattributes", f'"{name}" {policy}\n'.encode())
    file = write(tmp_path, name, style)
    git(tmp_path, "add", "--", ".gitattributes", name)
    attrs.write_text(
        f'"{name}" {"text" if policy in {"-text", "binary"} else "-text"}\n', encoding="utf-8"
    )
    before = (tmp_path / ".git/index").read_bytes()
    result = run_policy(tmp_path, "format", "scripts/validate-formatting.sh", "--fix")
    assert result.returncode == 0
    assert result.stderr == b""
    assert b"Checks: 5/5 passed" in result.stdout
    assert file.read_bytes() == wanted
    assert (tmp_path / ".git/index").read_bytes() == before
    assert read_calls(tmp_path) == [
        ["--version"],
        ["--write", "--", "**/*.{md,markdown}"],
        ["--write", "--", "**/*.{json,jsonc,asmdef,asmref}"],
        ["--write", "--", "**/*.{yml,yaml}"],
        ["--write", "--", "**/*.js"],
    ]
    assert run_policy(tmp_path, "format", "scripts/validate-formatting.sh", "--fix").returncode == 0
    assert file.read_bytes() == wanted


def test_corrupt_index_exits_without_newline_writes(tmp_path: Path) -> None:
    setup(tmp_path)
    file = write(tmp_path, "bad.txt", b"unchanged")
    git(tmp_path, "add", "--", file.name)
    bad_index = write(tmp_path, "corrupt-index", b"not a git index")
    result = run_policy(
        tmp_path,
        "format",
        "scripts/validate-formatting.sh",
        "--fix",
        GIT_INDEX_FILE=str(bad_index),
    )
    assert result.returncode == 1
    assert b"Git ls-files exited" in result.stderr
    assert file.read_bytes() == b"unchanged"
    assert bad_index.read_bytes() == b"not a git index"


def test_report_outside_git_checks_only_visible_regular_files(tmp_path: Path) -> None:
    setup(tmp_path, checkout=False)
    write(tmp_path, ".editorconfig", b"ok\n")
    write(tmp_path, "sub/file.ts", b"ok\n")
    write(tmp_path, "node_modules/ignored.md", b"ignored")
    write(tmp_path, "site/ignored.txt", b"ignored")
    result = run_policy(tmp_path, "newline", "-v")
    assert result.returncode == 0
    assert b"./sub/file.ts" in result.stdout
    assert b"Files checked: 3" in result.stdout
    assert b"ignored" not in result.stdout
    assert result.stderr == b""


def test_missing_git_fails_for_a_checkout_or_worktree(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    setup(tmp_path, checkout=False)
    monkeypatch.setenv("PATH", "")
    monkeypatch.delenv("GIT_DIR", raising=False)
    for marker_is_dir in (True, False):
        checkout = tmp_path / str(marker_is_dir)
        checkout.mkdir()
        marker = checkout / ".git"
        if marker_is_dir:
            marker.mkdir()
        else:
            marker.write_text("gitdir: /example/linked-worktree\n", encoding="utf-8")
        with pytest.raises(validator.ValidationError, match="Git is unavailable"):
            validator.report_paths(checkout)


def test_missing_git_allows_an_exported_source_tree(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setenv("PATH", "")
    monkeypatch.delenv("GIT_DIR", raising=False)
    write(tmp_path, "sample.txt", b"present\n")
    assert validator.report_paths(tmp_path) == ["./sample.txt"]


def test_report_fails_when_the_fallback_cannot_read_a_subdirectory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    unreadable = tmp_path / "unreadable"
    unreadable.mkdir()
    write(tmp_path, "visible.txt", b"present\n")
    monkeypatch.setenv("PATH", "")
    monkeypatch.delenv("GIT_DIR", raising=False)
    default_scandir = os.scandir

    def scandir(path: str | Path) -> AbstractContextManager[Iterator[os.DirEntry[str]]]:
        if Path(path) == unreadable:
            raise PermissionError(f"controlled unreadable directory: {unreadable}")
        return default_scandir(path)

    monkeypatch.setattr(os, "scandir", scandir)
    with pytest.raises(validator.ValidationError, match="controlled unreadable directory"):
        validator.report_paths(tmp_path)


def test_repair_refuses_a_hardlinked_missing_newline(tmp_path: Path) -> None:
    setup(tmp_path)
    original = write(tmp_path, "sample.txt", b"unchanged")
    alias = tmp_path / "outside-extension-link"
    try:
        os.link(original, alias)
    except OSError as error:
        pytest.skip(f"Hard links are unavailable in this fixture: {error}")
    other = write(tmp_path, "earlier.json", b"{}")
    git(tmp_path, "add", "--", original.name, other.name)
    result = run_policy(tmp_path, "format", "caller", "--fix")
    assert result.returncode == 1
    assert b"Refusing to replace a linked file" in result.stderr
    assert original.read_bytes() == alias.read_bytes() == b"unchanged"
    assert other.read_bytes() == b"{}"


def test_report_refuses_a_redirected_tracked_file(tmp_path: Path) -> None:
    setup(tmp_path)
    original = write(tmp_path, "sample.txt", b"expected\n")
    git(tmp_path, "add", "--", original.name)
    outside = write(tmp_path, "separate.data", b"must not copy")
    original.unlink()
    try:
        original.symlink_to(outside)
    except OSError as error:
        pytest.skip(f"File symlinks are unavailable in this fixture: {error}")
    result = run_policy(tmp_path, "newline")
    assert result.returncode == 1
    assert b"Refusing a nonregular file" in result.stderr
    assert b"must not copy" not in result.stdout
    assert outside.read_bytes() == b"must not copy"


@pytest.mark.parametrize("option", ["--help", "-h", "--unknown"])
def test_formatter_help_precedes_prettier_bootstrap(tmp_path: Path, option: str) -> None:
    setup(tmp_path)
    (tmp_path / "node_modules").rmdir()
    result = run_policy(tmp_path, "format", "foreign-format-launcher.sh", option)
    assert result.returncode == int(option == "--unknown")
    assert b"Usage: foreign-format-launcher.sh [OPTIONS]" in result.stdout
    assert not (tmp_path / "formatter-calls.jsonl").exists()
    assert result.stderr == b""


def test_formatter_preserves_listing_failure_exit_and_output(tmp_path: Path) -> None:
    setup(tmp_path)
    result = run_policy(tmp_path, "format", "caller", NEWLINE_FORMATTER_FAULT="listing-error")
    assert result.returncode == 1
    assert b"sample.md" in result.stdout
    assert b"Summary" not in result.stdout
    assert read_calls(tmp_path) == [
        ["--version"],
        ["--check", "--", "**/*.{md,markdown}"],
        ["--list-different", "--", "**/*.{md,markdown}"],
    ]


def test_fix_preserves_child_output_order_and_reports_failure(tmp_path: Path) -> None:
    setup(tmp_path)
    result = run_policy(
        tmp_path, "format", "caller", "--fix", NEWLINE_FORMATTER_FAULT="write-error"
    )
    assert result.returncode == 1
    assert result.stdout.index(b"Markdown (*.md") < result.stdout.index(b"wrote **/*.{md,markdown}")
    assert b"prettier --write failed" in result.stdout
    assert b"Checks: 4/5 passed" in result.stdout
    assert len(read_calls(tmp_path)) == 5


@pytest.mark.skipif(
    os.name == "nt", reason="The POSIX compatibility launchers run in the Bash hook suite"
)
@pytest.mark.parametrize(
    ("launcher", "option", "text"),
    [
        (FORMAT_LAUNCHER, "--help", b"Formatting Validation Script"),
        (NEWLINE_LAUNCHER, "--verbose", b"All files have a final newline"),
    ],
)
def test_legacy_bash_launchers_call_the_locked_policy(
    tmp_path: Path, launcher: str, option: str, text: bytes
) -> None:
    setup(tmp_path)
    destination = tmp_path / launcher
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(ROOT / launcher, destination)
    for name in ("pyproject.toml", "uv.lock"):
        project_file = tmp_path / PROJECT / name
        project_file.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / PROJECT / name, project_file)
    bash = shutil.which("bash")
    assert bash is not None
    result = subprocess.run(
        [bash, str(destination), option],
        cwd=tmp_path.parent,
        env=environment(UV_PROJECT_ENVIRONMENT=str(tmp_path / "isolated-uv")),
        check=False,
        capture_output=True,
        timeout=45,
    )
    assert result.returncode == 0
    assert text in result.stdout
    assert result.stderr == b""


def test_failed_publication_keeps_bytes_and_cleans_temporary_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    file = write(tmp_path, "original.txt", b"keep these bytes")
    finding = validator.MissingNewline(file.name, file.stat(), False, b"")
    initial_files = list(tmp_path.iterdir())

    def fail_replace(source: Path, destination: Path) -> Path:
        assert source.read_bytes() == b"keep these bytes\n"
        assert destination == file
        raise OSError("controlled publication failure")

    monkeypatch.setattr(Path, "replace", fail_replace)
    with pytest.raises(OSError, match="controlled publication failure"):
        validator.repair(tmp_path, finding)
    assert file.read_bytes() == b"keep these bytes"
    assert list(tmp_path.iterdir()) == initial_files


@pytest.mark.skipif(os.name == "nt", reason="Windows does not expose Unix executable modes")
def test_newline_repair_preserves_executable_mode(tmp_path: Path) -> None:
    file = write(tmp_path, "tool.sh", b"arg")
    file.chmod(0o751)
    finding = validator.MissingNewline(file.name, file.stat(), False, b"")
    validator.repair(tmp_path, finding)
    assert file.stat().st_mode & 0o777 == 0o751
    assert file.read_bytes() == b"arg\n"


def test_staged_binary_is_excluded_before_its_body_is_read(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    setup(tmp_path)
    file = write(tmp_path, "captured.txt", b"binary data")
    write(tmp_path, ".gitattributes", b"captured.txt binary\n")
    git(tmp_path, "add", "--", file.name, ".gitattributes")

    def fail_on_body(root: Path, name: str, info: os.stat_result) -> validator.MissingNewline:
        pytest.fail(f"Unexpected read of {root / name} with {info.st_size} bytes.")

    monkeypatch.setattr(validator, "inspect_missing", fail_on_body)
    assert validator.scan(tmp_path, [file.name]) == {}
