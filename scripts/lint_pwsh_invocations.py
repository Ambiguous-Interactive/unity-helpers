"""Lint PowerShell invocation sites using one batch of native syntax facts."""

from __future__ import annotations

import argparse
import codecs
import json
import os
import shutil
import stat
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING, cast

from cli_encoding import configure_output
from pwsh_invocation_asts import (
    read_facts,
    require_fields,
    violations,
    word_range_expression,
)
from pwsh_invocation_text import CulturePatterns, case_mode, scan_source

if TYPE_CHECKING:
    from collections.abc import Iterator

    from pwsh_invocation_asts import Violation

SCRIPTS = Path(__file__).resolve().parent
LABEL = "[lint-pwsh-invocations]"
SELF = frozenset(
    (
        "scripts/lint-pwsh-invocations.ps1",
        "scripts/read-pwsh-invocation-facts.ps1",
        "scripts/tests/test-lint-pwsh-invocations.ps1",
    )
)
IGNORED_ROOTS = frozenset(("node_modules", "site", ".git"))
BOM_ENCODINGS = (
    (codecs.BOM_UTF32_LE, "utf-32"),
    (codecs.BOM_UTF32_BE, "utf-32"),
    (codecs.BOM_UTF16_LE, "utf-16"),
    (codecs.BOM_UTF16_BE, "utf-16"),
)


def walk_files(
    root: Path, *, recursive: bool = False, excluded_roots: frozenset[str] = frozenset()
) -> Iterator[Path]:
    pending = [root]
    while pending:
        directory = pending.pop()
        try:
            directory_info = directory.lstat()
            directory_attributes = cast("int", getattr(directory_info, "st_file_attributes", 0))
            if stat.S_ISLNK(directory_info.st_mode) or (
                directory_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT
            ):
                continue
            with os.scandir(directory) as entries:
                for entry in entries:
                    info = entry.stat(follow_symlinks=False)
                    attributes = cast("int", getattr(info, "st_file_attributes", 0))
                    hidden = (
                        bool(attributes & stat.FILE_ATTRIBUTE_HIDDEN)
                        if os.name == "nt"
                        else entry.name.startswith(".")
                    )
                    if hidden:
                        continue
                    if entry.is_dir(follow_symlinks=False):
                        if (
                            recursive
                            and not attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT
                            and (directory != root or entry.name.casefold() not in excluded_roots)
                        ):
                            pending.append(Path(entry.path))
                    elif entry.is_file(follow_symlinks=True):
                        yield Path(entry.path)
        except (FileNotFoundError, NotADirectoryError):
            continue


def file_order(path: Path) -> tuple[str, str]:
    return path.as_posix().casefold(), path.as_posix()


def ignored_ast_paths(root: Path) -> set[str]:
    git = shutil.which("git")
    if git is None:
        return set()
    result = subprocess.run(  # noqa: S603 - resolved Git with fixed, read-only arguments.
        [
            git,
            "-C",
            str(root),
            "ls-files",
            "-z",
            "--others",
            "--ignored",
            "--exclude-standard",
            "--",
            "scripts",
            ".githooks",
        ],
        capture_output=True,
        check=False,
        timeout=30,
    )
    if result.returncode != 0:
        return set()
    return {os.fsdecode(path) for path in result.stdout.split(b"\0") if path}


def targets(root: Path) -> tuple[list[str], list[str]]:
    scripts = [
        path.relative_to(root)
        for path in walk_files(root / "scripts", recursive=True)
        if path.suffix.casefold() == ".ps1"
    ]
    hooks = list(walk_files(root / ".githooks"))
    workflow_paths = walk_files(root / ".github" / "workflows")
    package = root / "package.json"
    text_paths = set(scripts)
    text_paths.update(path.relative_to(root) for path in hooks)
    text_paths.update(
        path.relative_to(root)
        for path in workflow_paths
        if path.suffix.casefold() in {".yaml", ".yml"}
    )
    text_paths.update(
        path.relative_to(root)
        for path in walk_files(root, recursive=True, excluded_roots=IGNORED_ROOTS)
        if path.suffix.casefold() == ".sh"
    )
    if package.exists():
        text_paths.add(package.relative_to(root))

    owned_hooks = [
        path.relative_to(root)
        for path in walk_files(root / ".githooks", recursive=True)
        if path.suffix.casefold() == ".ps1"
    ]
    ignored = ignored_ast_paths(root)
    ast_paths = [
        path.as_posix()
        for group in (scripts, owned_hooks)
        for path in sorted(group, key=file_order)
        if path.as_posix().casefold() not in SELF and path.as_posix() not in ignored
    ]
    return [path.as_posix() for path in sorted(text_paths, key=file_order)], ast_paths


def read_lines(path: Path) -> list[str]:
    data = path.read_bytes()
    encoding = next((name for bom, name in BOM_ENCODINGS if data.startswith(bom)), "utf-8-sig")
    text = data.decode(encoding, errors="replace").replace("\r\n", "\n").replace("\r", "\n")
    return text.removesuffix("\n").split("\n") if text else []


def native_facts(
    root: Path, ast_paths: list[str], culture: str | None, pshome: Path | None
) -> dict[str, object]:
    pwsh = (
        shutil.which("pwsh")
        if pshome is None
        else str(pshome / ("pwsh.exe" if os.name == "nt" else "pwsh"))
    )
    if pwsh is None:
        raise ValueError("PowerShell is required to read native invocation syntax.")
    request = {"root": str(root), "paths": ast_paths, "culture": culture}
    result = subprocess.run(  # noqa: S603 - native parser gets literal paths in JSON.
        [
            pwsh,
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-File",
            str(SCRIPTS / "read-pwsh-invocation-facts.ps1"),
        ],
        input=json.dumps(request).encode("ascii"),
        cwd=root,
        capture_output=True,
        check=False,
        timeout=120,
    )
    if result.returncode != 0 or result.stderr:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"PowerShell syntax reader failed (exit {result.returncode}): {detail}")
    return require_fields(json.loads(result.stdout), ast=list, culture=str)


def lint(root: Path, culture: str | None, pshome: Path | None, *, verbose: bool) -> list[Violation]:
    text_paths, ast_paths = targets(root)
    if verbose:
        print(f"{LABEL} Scanning {len(text_paths)} file(s)")
    batch = native_facts(root, ast_paths, culture, pshome)
    patterns = CulturePatterns(
        case_mode(cast("str", batch["culture"])),
        word_range_expression(batch["word_ranges"]),
    )
    findings: list[Violation] = []
    for relative in text_paths:
        if relative.casefold() in SELF:
            continue
        try:
            lines = read_lines(root / relative)
        except OSError:
            if verbose:
                print(f"{LABEL} Skipping unreadable file: {relative}")
            continue
        findings.extend(scan_source(relative, lines, patterns))
    scripts = read_facts(batch["ast"])
    if [fact["path"] for fact in scripts] != ast_paths:
        raise ValueError("PowerShell syntax reader did not report every requested script in order.")
    findings.extend(violations(scripts))
    return findings


def main() -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=SCRIPTS.parent)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--culture", help=argparse.SUPPRESS)
    parser.add_argument("--pshome", type=Path, help=argparse.SUPPRESS)
    args = parser.parse_args()
    try:
        root = cast("Path", args.repo_root).resolve(strict=True)
        if not root.is_dir():
            raise ValueError(f"Repository root is not a directory: {root}")
        findings = lint(root, args.culture, args.pshome, verbose=args.verbose)
    except (
        OSError,
        subprocess.SubprocessError,
        ValueError,
        KeyError,
        TypeError,
    ) as error:
        print(f"{LABEL} Checks failed: {error}", file=sys.stderr)
        return 1
    for finding in findings:
        print(f"{finding['path']}:{finding['line']}: {finding['code']} {finding['message']}")
        if args.verbose:
            print(f"    > {finding['content']}")
    if findings:
        print(f"\n{LABEL} {len(findings)} violation(s) found.")
        return 1
    if args.verbose:
        print(f"{LABEL} OK: No pwsh/powershell invocation anti-patterns detected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
