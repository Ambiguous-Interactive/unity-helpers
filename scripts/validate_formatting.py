"""Run formatter checks and inspect final newlines in one Git inventory."""

from __future__ import annotations

import os
import shutil
import stat
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from io import TextIOWrapper
from pathlib import Path
from typing import TYPE_CHECKING, NoReturn

if TYPE_CHECKING:
    from collections.abc import Iterable

RED = "\x1b[0;31m"
GREEN = "\x1b[0;32m"
YELLOW = "\x1b[1;33m"
BLUE = "\x1b[0;34m"
RESET = "\x1b[0m"
CONFIG_FILES = (
    "package.json",
    ".prettierrc.json",
    ".editorconfig",
    ".markdownlint.json",
    ".yamllint.yaml",
    "cspell.json",
    "mkdocs.yml",
    "_config.yml",
    "Gemfile",
    "requirements-docs.txt",
)
GROUPS = (
    ("GitHub workflow files", (".yml", ".yaml")),
    ("shell scripts", (".sh",)),
    ("PowerShell scripts", (".ps1",)),
    ("JavaScript files", (".js",)),
    ("TypeScript files", (".ts",)),
    ("JSON / asmdef / asmref files", (".json", ".jsonc", ".asmdef", ".asmref")),
    ("Markdown files", (".md", ".markdown")),
    ("C# source files", (".cs",)),
    ("text / HTML / CSS / XML files", (".txt", ".html", ".css", ".xml")),
)
FORMAT_SUFFIXES = (
    ".json",
    ".jsonc",
    ".asmdef",
    ".asmref",
    ".md",
    ".markdown",
    ".yml",
    ".yaml",
    ".js",
    ".ts",
    ".cs",
    ".sh",
    ".ps1",
    ".txt",
    ".html",
    ".css",
    ".xml",
)
EXCLUDED_ROOTS = {".git", "node_modules", "site"}
GIT_REPOSITORY_SELECTORS = {
    "GIT_DIR",
    "GIT_WORK_TREE",
    "GIT_COMMON_DIR",
    "GIT_OBJECT_DIRECTORY",
    "GIT_ALTERNATE_OBJECT_DIRECTORIES",
    "GIT_IMPLICIT_WORK_TREE",
}
FORMAT_CHECKS = (
    ("Markdown (*.md, *.markdown)", "**/*.{md,markdown}"),
    ("JSON (*.json, *.jsonc, *.asmdef, *.asmref)", "**/*.{json,jsonc,asmdef,asmref}"),
    ("YAML (*.yml, *.yaml)", "**/*.{yml,yaml}"),
    ("JavaScript (*.js)", "**/*.js"),
)
NEWLINE_LABEL = "Final newline (tracked text files)"


class ValidationError(Exception):
    """An input or tool failed, so the scan could not finish."""


@dataclass(frozen=True)
class MissingNewline:
    name: str
    info: os.stat_result
    crlf: bool
    tail: bytes


def git_result(
    root: Path, args: list[str], payload: bytes | None = None
) -> subprocess.CompletedProcess[bytes]:
    git = shutil.which("git")
    if git is None:
        raise ValidationError("Git is not available.")
    # Anchor discovery to this checkout while keeping an explicit alternate index.
    env = {
        name: value for name, value in os.environ.items() if name not in GIT_REPOSITORY_SELECTORS
    }
    return subprocess.run(  # noqa: S603 - resolved Git and fixed read-only commands.
        [git, *args], cwd=root, env=env, input=payload, capture_output=True, check=False, timeout=30
    )


def git_stdout(root: Path, args: list[str], payload: bytes | None = None) -> bytes:
    result = git_result(root, args, payload)
    if result.returncode:
        detail = result.stderr.decode("utf-8", "replace").strip()
        raise ValidationError(f"Git {args[0]} exited {result.returncode}. {detail}")
    return result.stdout


def nul_fields(payload: bytes) -> list[bytes]:
    if not payload:
        return []
    if not payload.endswith(b"\0"):
        raise ValidationError("Git returned an unterminated NUL-delimited record.")
    return payload[:-1].split(b"\0")


def tracked_paths(root: Path) -> list[str]:
    return list(
        dict.fromkeys(
            os.fsdecode(entry) for entry in nul_fields(git_stdout(root, ["ls-files", "-z", "--"]))
        )
    )


def fail_walk(error: OSError) -> NoReturn:
    raise ValidationError(f"Cannot read the source tree. {error}") from error


def report_paths(root: Path) -> tuple[list[str], bool]:
    git_available = shutil.which("git") is not None
    if git_available:
        probe = git_result(root, ["rev-parse", "--is-inside-work-tree"])
        if probe.returncode == 0 and probe.stdout.strip() == b"true":
            return tracked_paths(root), True
    markers = (directory / ".git" for directory in (root, *root.parents))
    if any(marker.is_symlink() or marker.exists() for marker in markers):
        message = "Git could not identify the checkout." if git_available else "Git is unavailable."
        raise ValidationError(message)
    paths: list[str] = []
    for current, folders, files in os.walk(root, onerror=fail_walk):
        parent = Path(current)
        if parent == root:
            folders[:] = [name for name in folders if name not in EXCLUDED_ROOTS]
        for name in files:
            child = parent / name
            if stat.S_ISREG(child.lstat().st_mode):
                paths.append(f"./{child.relative_to(root).as_posix()}")
    return sorted(paths, key=os.fsencode), False


def path_suffix(name: str) -> str:
    leaf = name.rsplit("/", 1)[-1]
    _, dot, extension = leaf.rpartition(".")
    return dot + extension if dot else ""


def grouped_paths(names: Iterable[str], suffixes: tuple[str, ...]) -> list[str]:
    groups: dict[str, list[str]] = {suffix: [] for suffix in suffixes}
    for name in names:
        if name.removeprefix("./").split("/", 1)[0] in EXCLUDED_ROOTS:
            continue
        suffix = path_suffix(name)
        if suffix in groups:
            groups[suffix].append(name)
    return [name for suffix in suffixes for name in groups[suffix]]


def cached_text_attributes(root: Path, names: list[str]) -> dict[str, bytes]:
    if not names:
        return {}
    payload = b"".join(os.fsencode(name) + b"\0" for name in names)
    output = git_stdout(root, ["check-attr", "--cached", "-z", "--stdin", "text"], payload)
    fields = nul_fields(output)
    keys: list[str] = []
    attributes: dict[str, bytes] = {}
    for raw_name, attribute, value in zip(fields[0::3], fields[1::3], fields[2::3], strict=True):
        name = os.fsdecode(raw_name)
        if attribute != b"text":
            raise ValidationError("Git returned an unexpected staged attribute.")
        keys.append(name)
        attributes[name] = value
    if set(keys) != set(names) or len(keys) != len(names):
        raise ValidationError("Git did not return one staged text attribute per path.")
    return attributes


def stable_file(left: os.stat_result, right: os.stat_result) -> bool:
    return (
        left.st_dev == right.st_dev
        and left.st_ino == right.st_ino
        and left.st_size == right.st_size
        and left.st_mtime_ns == right.st_mtime_ns
        and left.st_ctime_ns == right.st_ctime_ns
        and left.st_nlink == right.st_nlink
    )


def inspect_missing(root: Path, name: str, info: os.stat_result) -> MissingNewline:
    with (root / name).open("rb") as stream:
        if not stable_file(info, os.fstat(stream.fileno())):
            raise ValidationError(f"File changed during scan: {display(name)}")
        stream.seek(max(0, info.st_size - 16))
        tail = stream.read(16)
        stream.seek(0)
        crlf = any(b"\r" in block for block in iter(lambda: stream.read(64 * 1024), b""))
        if not stable_file(info, os.fstat(stream.fileno())):
            raise ValidationError(f"File changed during scan: {display(name)}")
    return MissingNewline(name, info, crlf, tail)


def local_file_info(root: Path, name: str, safe_parents: set[Path]) -> os.stat_result | None:
    relative = Path(name)
    if relative.anchor or ".." in relative.parts:
        raise ValidationError(f"Refusing a path outside the checkout: {display(name)}")
    path = root / relative
    try:
        info = path.lstat()
    except FileNotFoundError:
        return None
    parent = path.parent
    if parent not in safe_parents:
        resolved = parent.resolve(strict=True)
        if resolved != parent or not resolved.is_relative_to(root):
            raise ValidationError(f"Refusing a redirected path: {display(name)}")
        safe_parents.add(parent)
    if not stat.S_ISREG(info.st_mode):
        raise ValidationError(f"Refusing a nonregular file: {display(name)}")
    return info


def scan(
    root: Path, names: Iterable[str], *, staged_attributes: bool
) -> dict[str, MissingNewline | None]:
    observed: dict[str, MissingNewline | None] = {}
    incomplete: dict[str, os.stat_result] = {}
    safe_parents: set[Path] = {root}
    for name in dict.fromkeys(names):
        info = local_file_info(root, name, safe_parents)
        if info is None or not info.st_size:
            continue
        with (root / name).open("rb") as stream:
            if not stable_file(info, os.fstat(stream.fileno())):
                raise ValidationError(f"File changed during scan: {display(name)}")
            stream.seek(-1, os.SEEK_END)
            if stream.read(1) != b"\n":
                incomplete[name] = info
        observed[name] = None

    attributes: dict[str, bytes] = {}
    if staged_attributes:
        try:
            attributes = cached_text_attributes(root, list(incomplete))
        except (ValueError, ValidationError) as error:
            raise ValidationError(f"Cannot inspect staged text attribute. {error}") from error
    for name, info in incomplete.items():
        if attributes.get(name) == b"unset":
            del observed[name]
        else:
            observed[name] = inspect_missing(root, name, info)
    return observed


def repair(root: Path, finding: MissingNewline) -> None:
    path = root / finding.name
    if finding.info.st_nlink != 1 or path.parent.resolve(strict=True) != path.parent:
        raise ValidationError(f"Refusing to replace a linked file: {display(finding.name)}")
    temporary: Path | None = None
    try:
        with path.open("rb") as source:
            if not stable_file(finding.info, os.fstat(source.fileno())):
                raise ValidationError(f"File changed before repair: {display(finding.name)}")
            with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as swap:
                temporary = Path(swap.name)
                shutil.copyfileobj(source, swap)
                swap.write(b"\r\n" if finding.crlf else b"\n")
            if not stable_file(finding.info, os.fstat(source.fileno())):
                raise ValidationError(f"File changed during repair: {display(finding.name)}")
        temporary.chmod(stat.S_IMODE(finding.info.st_mode))
        if (
            not stable_file(finding.info, path.lstat())
            or path.parent.resolve(strict=True) != path.parent
        ):
            raise ValidationError(f"File changed before publication: {display(finding.name)}")
        # Repair assumes no concurrent writers between the identity check and replacement.
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def display(name: str) -> str:
    return name.replace("\r", r"\r").replace("\n", r"\n").replace("\x1b", r"\x1b")


def show_report(root: Path, *, verbose: bool) -> int:
    print(f"{BLUE}── Final Newline Check ──{RESET}\n")
    names, from_checkout = report_paths(root)
    groups: list[tuple[str, list[str], str]] = [
        ("configuration files", list(CONFIG_FILES), "config-list")
    ]
    groups.extend((title, grouped_paths(names, suffixes), "") for title, suffixes in GROUPS)
    observed = scan(
        root,
        (name.removeprefix("./") for _, paths, _ in groups for name in paths),
        staged_attributes=from_checkout,
    )
    rejected: list[str] = []
    seen: set[str] = set()
    passed = 0
    for title, paths, config_source in groups:
        print(f"{BLUE}Checking {title}...{RESET}")
        for name in paths:
            key = name.removeprefix("./")
            if key in seen or key not in observed:
                continue
            seen.add(key)
            entry = observed[key]
            label = display(name)
            if entry is None:
                passed += 1
                if verbose:
                    print(f"  {GREEN}PASS{RESET} {label}")
                continue
            ending = "CRLF" if entry.crlf else "LF"
            source = config_source or f"glob:*{path_suffix(name)}"
            print(f"  {RED}FAIL{RESET} {label} ({ending})")
            print(f"      Detected: {ending} | Bytes: {entry.info.st_size} | Sources: {source}")
            print(f"      Tail hex: {entry.tail.hex(' ')}")
            rejected.append(label)
    print(f"\n{BLUE}── Summary ──{RESET}")
    print(f"  Files checked: {len(seen)}")
    print(f"  {GREEN}Passed{RESET}:  {passed}")
    print(f"  {RED}Failed{RESET}:  {len(rejected)}\n")
    if not rejected:
        print(f"{GREEN}All files have a final newline.{RESET}")
        return 0
    print(f"{RED}The following files are missing a final newline:{RESET}")
    for name in rejected:
        print(f"  {RED}-{RESET} {name}")
    print("\nFix with (CRLF-aware):")
    print(rf"  {YELLOW}For CRLF files: printf '\r\n' >> <file>{RESET}")
    print(rf"  {YELLOW}For LF files:   printf '\n' >> <file>{RESET}")
    if from_checkout:
        print(
            f"Or run:   {YELLOW}./scripts/validate-formatting.sh --fix{RESET} (auto-detects line endings)"
        )
    return 1


def formatter_help(caller: str) -> None:
    print(f"""Formatting Validation Script

Usage: {caller} [OPTIONS]

Options:
  --fix     Auto-fix formatting issues instead of just checking
  --help    Show this help message

Checks performed:
  1. Markdown formatting    (*.md, *.markdown)
  2. JSON formatting        (*.json, *.jsonc, *.asmdef, *.asmref)
  3. YAML formatting        (*.yml, *.yaml)
  4. JavaScript formatting  (*.js)
  5. Final newline          (tracked text files)

This script mirrors the CI formatting checks so you can
catch issues before pushing.""")


def prettier(root: Path, node: str, args: list[str], *, stdout: int | None = None) -> int:
    result = subprocess.run(  # noqa: S603 - resolved Node and owned formatter entry point.
        [node, "scripts/run-prettier.js", *args],
        cwd=root,
        stdout=stdout,
        stderr=subprocess.DEVNULL,
        check=False,
        timeout=300,
    )
    return result.returncode if result.returncode >= 0 else 128 - result.returncode


def show_formatting(root: Path, caller: str, option: str) -> int:
    if option in {"--help", "-h"}:
        formatter_help(caller)
        return 0
    if option not in {"", "--fix"}:
        print(f"Unknown option: {option}")
        formatter_help(caller)
        return 1
    node = shutil.which("node")
    if node is None:
        print(f"{RED}Error: node is not available. Install Node.js first.{RESET}")
        return 1
    if not (root / "node_modules").is_dir():
        print(f"{RED}Error: node_modules not found. Run 'npm install' first.{RESET}")
        return 1
    if prettier(root, node, ["--version"], stdout=subprocess.DEVNULL):
        print(f"{RED}Error: prettier not found. Run 'npm install' first.{RESET}")
        return 1

    fix = option == "--fix"
    names = grouped_paths(tracked_paths(root), FORMAT_SUFFIXES)
    if fix:
        safe_parents = {root}
        for name in names:
            info = local_file_info(root, name, safe_parents)
            if info is not None and info.st_nlink != 1:
                raise ValidationError(f"Refusing to replace a linked file: {display(name)}")
    title = (
        "Auto-fixing formatting issues" if fix else "Checking formatting (use --fix to auto-fix)"
    )
    print(f"\n{BLUE}── {title} ──{RESET}")
    passed = 0
    for label, pattern in FORMAT_CHECKS:
        if fix:
            print(f"{BLUE}  INFO{RESET} {label}: auto-fixing...", flush=True)
            if prettier(root, node, ["--write", "--", pattern]):
                print(f"{RED}  FAIL{RESET} {label}: prettier --write failed")
                continue
        elif prettier(root, node, ["--check", "--", pattern], stdout=subprocess.DEVNULL):
            print(f"{RED}  FAIL{RESET} {label}")
            print("         Failing files:")
            result = subprocess.run(  # noqa: S603 - resolved Node, fixed formatter command and patterns.
                [node, "scripts/run-prettier.js", "--list-different", "--", pattern],
                cwd=root,
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                check=False,
                timeout=300,
            )
            for line in result.stdout.decode("utf-8", "replace").split("\n")[:-1]:
                print(f"           {RED}-{RESET} {line}")
            if result.returncode and (result.returncode != 1 or not result.stdout.strip()):
                return result.returncode if result.returncode > 0 else 128 - result.returncode
            print(
                f'         Fix with: {YELLOW}node scripts/run-prettier.js --write -- "{pattern}"{RESET}'
            )
            continue
        print(f"{GREEN}  PASS{RESET} {label}")
        passed += 1

    missing = [
        entry for entry in scan(root, names, staged_attributes=True).values() if entry is not None
    ]
    if fix and missing:
        for finding in missing:
            if finding.info.st_nlink != 1:
                raise ValidationError(f"Refusing to replace a linked file: {display(finding.name)}")
        print(f"{BLUE}  INFO{RESET} {NEWLINE_LABEL}: auto-fixing...")
        for finding in missing:
            repair(root, finding)
        print(f"{GREEN}  PASS{RESET} {NEWLINE_LABEL} (fixed {len(missing)} file(s))")
        passed += 1
    elif missing:
        print(f"{RED}  FAIL{RESET} {NEWLINE_LABEL}")
        print("         Files missing final newline:")
        for finding in missing:
            print(f"           {RED}-{RESET} {display(finding.name)}")
        print(f"         Fix with: {YELLOW}{caller} --fix{RESET} or add a newline at end of file")
    else:
        print(f"{GREEN}  PASS{RESET} {NEWLINE_LABEL}")
        passed += 1
    print(f"\n{BLUE}── Summary ──{RESET}")
    print(f"  Checks: {passed}/5 passed")
    if passed == 5:
        print(f"{GREEN}All formatting checks passed.{RESET}")
        return 0
    print(f"\n{RED}Formatting issues found.{RESET}")
    print(f"Run {YELLOW}{caller} --fix{RESET} to auto-fix all issues.")
    print(f"Or run {YELLOW}npm run format:check{RESET} then {YELLOW}npm run format:fix{RESET}.")
    return 1


def main(args: list[str]) -> int:
    for stream in (sys.stdout, sys.stderr):
        if isinstance(stream, TextIOWrapper):
            stream.reconfigure(encoding="utf-8", errors="backslashreplace", newline="\n")
    root = Path(__file__).resolve().parents[1]
    try:
        if args and args[0] == "newline":
            return show_report(root, verbose=len(args) > 1 and args[1] in {"--verbose", "-v"})
        if len(args) > 1 and args[0] == "format":
            return show_formatting(root, args[1], args[2] if len(args) > 2 else "")
        raise ValidationError("Expected the newline or format launcher.")
    except (OSError, RuntimeError, ValidationError, subprocess.TimeoutExpired) as error:
        sys.stdout.flush()
        print(f"[validate-formatting] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
