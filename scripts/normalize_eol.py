"""Normalize selected worktree text files and report the exact changed paths."""

from __future__ import annotations

import argparse
import codecs
import os
import stat
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path

from check_eol import ROOT, git_output, relative_path, requires_lf, supported
from cli_encoding import configure_output
from csharp_sources import is_link


@dataclass(frozen=True)
class Edit:
    """The observed and replacement bytes of one planned repair."""

    path: Path
    original: bytes
    replacement: bytes
    bom: bool
    endings: bool
    mode: int


def selected_files(repository: Path, paths: list[str] | None) -> list[Path]:
    """Select literal paths without widening an explicit scope."""
    if paths is None:
        names = [
            os.fsdecode(record)
            for record in git_output(repository, ["ls-files", "-z"]).split(b"\0")
            if record
        ]
    else:
        names = [relative_path(repository, name) for name in paths if name.strip()]
    selected: dict[Path, None] = {}
    for name in names:
        if not supported(name):
            continue
        path = (repository / name).resolve()
        if not path.is_relative_to(repository):
            raise ValueError(f"Path is outside the repository: {name}")
        selected[path] = None
    return list(selected)


def plan_edits(repository: Path, files: list[Path]) -> list[Edit]:
    """Validate every input before making the first edit."""
    edits: list[Edit] = []
    checked_directories = {repository}
    for path in files:
        if not path.is_relative_to(repository):
            raise ValueError(f"Path is outside the repository: {path}")
        try:
            info = path.lstat()
        except FileNotFoundError:
            continue
        name = path.relative_to(repository).as_posix()
        current = path.parent
        while current not in checked_directories:
            if is_link(current):
                raise ValueError(f"Cannot normalize linked input: {name}")
            checked_directories.add(current)
            current = current.parent
        if is_link(path):
            raise ValueError(f"Cannot normalize linked input: {name}")
        if not stat.S_ISREG(info.st_mode):
            continue
        original = path.read_bytes()
        content = original.removeprefix(codecs.BOM_UTF8)
        normalized = content.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
        if not requires_lf(name):
            normalized = normalized.replace(b"\n", b"\r\n")
        if original == normalized:
            continue
        if info.st_nlink != 1:
            raise ValueError(f"Cannot normalize hard-linked input: {name}")
        if b"\0" in content:
            raise ValueError(f"Cannot normalize non-text input: {name}")
        try:
            content.decode("utf-8")
        except UnicodeError as error:
            raise ValueError(f"Cannot normalize invalid UTF-8: {name}") from error
        edits.append(
            Edit(
                path,
                original,
                normalized,
                bom=original != content,
                endings=content != normalized,
                mode=stat.S_IMODE(info.st_mode),
            )
        )
    return edits


def replace_file(path: Path, data: bytes, mode: int) -> None:
    """Publish complete bytes without following a destination link."""
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
        temporary.chmod(mode)
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--modified-path-list", type=Path)
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    try:
        repository = Path(os.path.abspath(args.repo_root))  # noqa: PTH100 - retain root links for rejection.
        if not repository.is_dir() or is_link(repository):
            raise ValueError(f"Repository is missing or linked: {repository}")
        files = selected_files(repository, args.paths)
        edits = plan_edits(repository, files)
        if args.paths is None and not files:
            raise ValueError("No tracked text files were selected")
        names = [edit.path.relative_to(repository).as_posix() for edit in edits]
        report: Path | None = args.modified_path_list
        report_data = b""
        if report is not None:
            report = Path(os.path.abspath(report))  # noqa: PTH100 - keep the requested destination.
            if report in files or report.resolve() in files:
                raise ValueError("The modified-path list cannot replace a selected text file")
            report_data = b"".join(name.encode("utf-8") + b"\0" for name in names)
        for edit, name in zip(edits, names, strict=True):
            if not args.dry_run:
                info = edit.path.lstat()
                if (
                    not stat.S_ISREG(info.st_mode)
                    or info.st_nlink != 1
                    or is_link(edit.path)
                    or edit.path.read_bytes() != edit.original
                ):
                    raise ValueError(f"Input changed during normalization: {name}")
                replace_file(edit.path, edit.replacement, edit.mode)
            if args.verbose:
                print(f"{'Would fix' if args.dry_run else 'Fixed'}: {name}")
        if report is not None:
            replace_file(report, report_data, 0o600)
    except (OSError, ValueError, RuntimeError, subprocess.SubprocessError) as error:
        print(f"normalize-eol: {error}", file=sys.stderr)
        return 1
    endings = sum(edit.endings for edit in edits)
    boms = sum(edit.bom for edit in edits)
    print(f"Files fixed: {len(edits)} (EOL:{endings}, BOMRemoved:{boms})")
    return 2 if args.dry_run and edits else 0


if __name__ == "__main__":
    raise SystemExit(main())
