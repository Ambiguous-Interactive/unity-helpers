"""Check dedicated Unity object filenames without interpreting comments or string literals."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import is_link, mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Tests", "Samples~")
UNITY_BASES = frozenset(
    (
        "MonoBehaviour",
        "ScriptableObject",
        "ScriptableObjectSingleton",
        "StateMachineBehaviour",
        "NetworkBehaviour",
    )
)
DECLARATION = re.compile(
    r"\b(?P<kind>class|struct|interface|record)\s+(?P<name>@?\w+)"
    r"(?:\s*<[^{};]+?>)?\s*(?::\s*(?P<base>[^{};]+))?\s*\{"
)
BASE_NAME = re.compile(r"(?:global::)?(?:\w+\s*\.\s*)*(?P<name>\w+)")
DISABLE = re.compile(r"//\s*lint-disable\s+unity-file-naming\b")


def analyze(source: str, filename: str) -> list[tuple[int, str]]:
    """Return naming and cardinality findings for direct Unity object declarations."""
    if not any(base in source for base in UNITY_BASES):
        return []
    if DISABLE.search(source):
        comments_removed = mask_noise(source, preserve_strings=True)
        for suppression in DISABLE.finditer(source):
            if not comments_removed[suppression.start() : suppression.end()].strip():
                return []
    masked = mask_noise(source)
    classes: list[tuple[str, int]] = []
    for declaration in DECLARATION.finditer(masked):
        if declaration.group("kind") != "class":
            continue
        base_clause = declaration.group("base")
        base = BASE_NAME.match(base_clause) if base_clause else None
        if base is not None and base.group("name") in UNITY_BASES:
            classes.append(
                (
                    declaration.group("name").removeprefix("@"),
                    source.count("\n", 0, declaration.start()) + 1,
                )
            )
    if len(classes) > 1:
        names = ", ".join(name for name, _ in classes)
        return [
            (
                classes[0][1],
                (
                    f"UNH005: File contains multiple Unity Object types: {names}. "
                    "Each MonoBehaviour/ScriptableObject must be in its own file."
                ),
            )
        ]
    if classes:
        name, line = classes[0]
        expected = f"{name}.cs"
        if filename != expected:
            return [
                (
                    line,
                    (
                        f"UNH006: Unity Object '{name}' must be in a file named '{expected}', "
                        f"but found in '{filename}'."
                    ),
                )
            ]
    return []


def files_to_check(repository: Path, *, staged_only: bool) -> list[Path]:
    """Read a physical source corpus or the caller's staged C# paths anchored to its Git root."""
    if is_link(repository) or not repository.is_dir():
        raise ValueError("The repository root must be a physical directory.")
    if staged_only:
        git = shutil.which("git")
        if git is None:
            raise ValueError("Git is required to select staged C# paths.")
        result = subprocess.run(  # noqa: S603 - fixed read-only Git operation.
            [git, "-C", str(repository), "rev-parse", "--show-toplevel"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=True,
        )
        if Path(result.stdout.strip()).resolve() != repository.resolve():
            raise ValueError("The requested repository is not the Git root.")
        result = subprocess.run(  # noqa: S603 - fixed read-only Git operation.
            [
                git,
                "-C",
                str(repository),
                "diff",
                "--cached",
                "--name-only",
                "--diff-filter=ACMR",
                "-z",
                "--",
                "*.cs",
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=True,
        )
        files: list[Path] = []
        for relative in result.stdout.split("\0"):
            if not relative or relative.startswith("scripts/tests/fixtures/should-fail/"):
                continue
            path = repository / relative
            if not path.is_file() or is_link(path):
                raise ValueError(f"Staged C# input is missing or linked: {relative}")
            current = path.parent
            while current != repository:
                if is_link(current):
                    raise ValueError(f"Staged C# input has a linked parent: {relative}")
                current = current.parent
            files.append(path)
        return sorted(files)
    roots = tuple(
        name
        for name in SOURCE_ROOTS
        if (repository / name).exists() or (repository / name).is_symlink()
    )
    if not roots:
        raise ValueError("No Unity source roots were found.")
    return source_files(repository, roots)


def read_source(repository: Path, path: Path, *, staged_only: bool) -> str:
    """Read the index blob for staged checks and the physical file for ordinary scans."""
    if not staged_only:
        return path.read_text(encoding="utf-8-sig")
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required to read staged C# contents.")
    relative = path.relative_to(repository).as_posix()
    result = subprocess.run(  # noqa: S603 - fixed read-only Git operation.
        [git, "-C", str(repository), "show", "--no-ext-diff", "--no-textconv", f":{relative}"],
        capture_output=True,
        text=True,
        encoding="utf-8-sig",
        check=True,
    )
    return result.stdout


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--staged-only", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root.absolute()
    try:
        files = files_to_check(repository, staged_only=args.staged_only)
        failed = False
        for path in files:
            relative = path.relative_to(repository).as_posix()
            source = read_source(repository, path, staged_only=args.staged_only)
            for line, message in analyze(source, path.name):
                print(f"::error file={relative},line={line}::{message}")
                failed = True
        if args.verbose or not args.staged_only:
            print(f"[lint-unity-file-naming] Checked {len(files)} C# files.")
        return int(failed)
    except (OSError, UnicodeError, ValueError, subprocess.SubprocessError) as error:
        print(f"[lint-unity-file-naming] {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
