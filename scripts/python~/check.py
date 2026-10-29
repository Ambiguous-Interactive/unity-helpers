"""Validate every migrated Python source with the locked analysis toolchain."""

from __future__ import annotations

import shutil
import subprocess
import sys
import tomllib
from pathlib import Path
from typing import cast


def verify_registry(repository: Path, files: list[str], legacy: list[str]) -> bool:
    selected = {Path(source).relative_to(repository).as_posix() for source in files}
    git = shutil.which("git")
    if git is None:
        print("Git is required to inventory Python sources.", file=sys.stderr)
        return False
    result = subprocess.run(  # noqa: S603 - fixed read-only Git inventory.
        [git, "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "*.py"],
        cwd=repository,
        capture_output=True,
        text=True,
        check=True,
    )
    discovered: set[str] = set(result.stdout.rstrip("\0").split("\0")) if result.stdout else set()
    expected = selected | set(legacy)
    valid = (
        bool(selected)
        and len(selected) == len(files)
        and len(set(legacy)) == len(legacy)
        and not (selected & set(legacy))
    )
    if not valid:
        print(
            "Python source registry is empty, duplicated, or overlaps the legacy inventory.",
            file=sys.stderr,
        )
    for source in sorted(discovered - expected):
        print(f"Python source must join the strict validation scope: {source}", file=sys.stderr)
    for source in sorted(expected - discovered):
        print(f"Python registry names a missing source: {source}", file=sys.stderr)
    return valid and discovered == expected


def main() -> int:
    project = Path(__file__).resolve().parent
    repository = project.parents[1]
    config = project / "pyproject.toml"
    with config.open("rb") as stream:
        settings = tomllib.load(stream)
    tool = cast("dict[str, object]", settings["tool"])
    pyright = cast("dict[str, object]", tool["pyright"])
    includes = cast("list[str]", pyright["include"])
    if not includes:
        print("Python validation requires at least one source file.", file=sys.stderr)
        return 1
    files = [str((project / source).resolve()) for source in includes]
    for source in files:
        if not Path(source).is_file():
            print(f"Missing Python validation source: {source}", file=sys.stderr)
            return 1
    policy = cast("dict[str, object]", tool["unity-helpers-python"])
    legacy = cast("list[str]", policy["legacy"])
    if not verify_registry(repository, files, legacy):
        return 1
    print(
        f"Strict Python scope: {len(files)} files; legacy migration backlog: {len(legacy)} files.",
        flush=True,
    )
    commands = [
        ["ruff", "check", "--config", str(config), *files],
        ["ruff", "format", "--check", "--config", str(config), *files],
        ["mypy", "--config-file", str(config), *files],
        ["pyright", "--project", str(config)],
    ]
    failed = False
    for command in commands:
        print(f"Running {' '.join(command)}", flush=True)
        result = subprocess.run(command, cwd=repository, check=False)  # noqa: S603 - fixed tools and repository paths.
        failed |= result.returncode != 0
    return int(failed)


if __name__ == "__main__":
    sys.exit(main())
