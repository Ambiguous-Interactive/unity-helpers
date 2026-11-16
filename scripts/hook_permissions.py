"""Validate literal hook index modes and delegate repairs to the native coordinator."""

from __future__ import annotations

import json
import os
import shlex
import shutil
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
HOOKS = ".githooks"


@dataclass(frozen=True)
class Entry:
    mode: str
    oid: str
    stage: str
    path: str


def run_git(executable: str, root: Path, *arguments: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(  # noqa: S603 - resolved Git, literal arguments, no shell.
        [executable, "-C", str(root), *arguments], capture_output=True, check=False, timeout=30
    )


def read_entries(executable: str, root: Path) -> list[Entry]:
    result = run_git(executable, root, "ls-files", "--stage", "-z", "--", HOOKS)
    if result.returncode:
        raise ValueError(f"git ls-files failed with exit {result.returncode}")
    if result.stdout and not result.stdout.endswith(b"\0"):
        raise ValueError("Incomplete NUL-delimited index records")
    entries: list[Entry] = []
    for raw in result.stdout.split(b"\0"):
        if not raw:
            continue
        header, path = raw.split(b"\t", 1)
        mode, oid, stage = header.decode("ascii").split()
        if (
            len(mode) != 6
            or any(character not in "01234567" for character in mode)
            or len(oid) not in (40, 64)
            or any(character not in "0123456789abcdef" for character in oid)
            or stage not in ("0", "1", "2", "3")
            or not path.startswith(b".githooks/")
        ):
            raise ValueError("Invalid hook index record")
        name = os.fsdecode(path)
        if "." not in name.rsplit("/", 1)[-1]:
            entries.append(Entry(mode, oid, stage, name))
    return entries


def display(path: str) -> str:
    return path if path.isprintable() else json.dumps(path, ensure_ascii=True)


def repair(root: Path, paths: list[str]) -> None:
    pwsh = shutil.which("pwsh")
    if pwsh is None:
        raise ValueError("pwsh is required for the native index mutex and retry coordinator")
    result = subprocess.run(  # noqa: S603 - fixed native coordinator; literal paths travel through JSON stdin.
        [pwsh, "-NoProfile", "-File", str(ROOT / "scripts/set-hook-index-mode.ps1")],
        cwd=root,
        input=json.dumps({"files": paths}, ensure_ascii=True).encode("ascii"),
        capture_output=True,
        check=False,
    )
    if result.returncode:
        details = (result.stdout + result.stderr).decode("utf-8", errors="replace").strip()
        raise ValueError(f"Index mode repair failed with exit {result.returncode}: {details}")


def validate(root: Path, *, fix: bool = False) -> tuple[int, list[str]]:
    lines: list[str] = []
    executable = shutil.which("git")
    if executable is None:
        return 1, ["Error: git is not available."]
    try:
        if not (root / HOOKS).is_dir():
            return 1, [f"Error: {HOOKS} directory not found."]
        entries = read_entries(executable, root)
        if not entries:
            return 1, [
                f"WARNING: No tracked files found in {HOOKS}/",
                "Make sure hook files are committed to git.",
            ]
        lines.extend(["", "── Validating git hook file permissions ──"])
        failures = [entry for entry in entries if entry.mode != "100755" or entry.stage != "0"]
        if fix and failures:
            if any(
                entry.mode not in ("100644", "100755") or entry.stage != "0" for entry in failures
            ):
                raise ValueError("Refusing to repair unmerged or non-regular hook index entries")
            repair(root, [entry.path for entry in failures])
            after = read_entries(executable, root)
            expected = [Entry("100755", entry.oid, "0", entry.path) for entry in entries]
            if after != expected:
                raise ValueError(
                    "Hook index readback differs from the expected mode-only repair; inspect the index before retrying"
                )
            lines.extend(f"  PASS {display(entry.path)} (fixed to 100755)" for entry in failures)
        else:
            for entry in entries:
                name = display(entry.path)
                if entry.mode != "100755" or entry.stage != "0":
                    lines.extend(
                        [
                            f"  FAIL {name} ({entry.mode} - NOT executable or unmerged)",
                            "         Expected: 100755 (executable, stage 0)",
                            f"         Actual:   {entry.mode} (stage {entry.stage})",
                            "         Fix with: "
                            + shlex.join(
                                [
                                    "bash",
                                    (ROOT / "scripts/validate-hook-permissions.sh").as_posix(),
                                    "--fix",
                                    "--repo-root",
                                    root.as_posix(),
                                ]
                            ),
                        ]
                    )
                else:
                    lines.append(f"  PASS {name} ({entry.mode} - executable)")
        lines.extend(["", "── Summary ──", f"  Files checked: {len(entries)}"])
        if failures and not fix:
            lines.extend(
                [
                    f"{len(failures)} file(s) missing executable permissions.",
                    "Git silently skips non-executable hook files.",
                ]
            )
            return 1, lines
        lines.append(
            "All hook entrypoint permissions are correct (fixed)."
            if fix
            else "All hook entrypoints have executable permissions."
        )
        return 0, lines
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        lines.append(f"Error: {error}")
        return 1, lines


def help_text() -> str:
    return """Git Hook Permissions Validation Script

Usage: scripts/validate-hook-permissions.sh [OPTIONS]

  --fix               Repair executable modes through the native index coordinator
  --repo-root <dir>   Repository to validate (default: this script's repository)
  --help              Show this help message

Checks extensionless .githooks/ files for index mode 100755 and stage 0.
Repairs preserve staged content and existing index flags.
"""


def main(arguments: list[str] | None = None) -> int:
    arguments = list(sys.argv[1:] if arguments is None else arguments)
    root, fix = ROOT, False
    while arguments:
        argument = arguments.pop(0)
        if argument in ("--help", "-h"):
            print(help_text())
            return 0
        if argument == "--fix":
            fix = True
        elif argument == "--repo-root":
            if not arguments:
                print("--repo-root requires a directory argument")
                return 1
            root = Path(arguments.pop(0))
            try:
                if not root.is_dir():
                    print(f"--repo-root directory not found: {root}")
                    return 1
                root = root.resolve()
            except OSError as error:
                print(f"--repo-root directory unavailable: {root}: {error}")
                return 1
        else:
            print(f"Unknown option: {argument}\n{help_text()}")
            return 1
    status, lines = validate(root, fix=fix)
    print("\n".join(lines))
    return status


if __name__ == "__main__":
    configure_output()
    raise SystemExit(main())
