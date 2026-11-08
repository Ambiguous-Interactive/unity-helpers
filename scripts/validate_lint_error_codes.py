"""Verify lint-code prefixes with the repository's actual cspell configuration."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import is_link
from doc_counts import read_text

ROOT = Path(__file__).resolve().parents[1]
CODES = re.compile(r"\b([A-Z]{2,})[0-9]{3}\b")
VERDICTS = re.compile(r"(?:Unknown|Forbidden) word \(([A-Z]{2,})(?:[0-9]{3})?\)")
UPSTREAM = frozenset(("MD", "SC", "CS", "SA"))
EXTENSIONS = frozenset((".ps1", ".js", ".mjs", ".sh", ".py"))


def selected_files(repository: Path) -> list[Path]:
    """Select lint sources, their controls, and hook text without following links."""
    if not repository.is_dir() or is_link(repository):
        raise ValueError(f"Repository is missing or linked: {repository}")
    scripts = repository / "scripts"
    if not scripts.is_dir() or is_link(scripts):
        raise ValueError(f"Scripts directory is missing or linked: {scripts}")
    selected: list[Path] = []
    for directory, kind in (
        (scripts, "lint"),
        (scripts / "tests", "test"),
        (repository / ".githooks", "hook"),
    ):
        try:
            linked = is_link(directory)
        except FileNotFoundError:
            continue
        if linked or not directory.is_dir():
            raise ValueError(f"Source directory is not a directory or is linked: {directory}")
        for path in sorted(directory.iterdir()):
            name = path.name.casefold()
            matches = (
                (
                    kind == "hook"
                    and name not in ("readme.md", ".gitattributes")
                    and path.suffix.casefold() != ".meta"
                )
                or (
                    kind == "lint"
                    and name.startswith(("lint-", "lint_"))
                    and path.suffix.casefold() in EXTENSIONS
                )
                or (
                    kind == "test"
                    and name.startswith(("test-lint-", "test_lint_"))
                    and path.suffix.casefold() in EXTENSIONS
                )
            )
            if not matches:
                continue
            if is_link(path):
                raise ValueError(f"Lint input is linked or not a regular file: {path}")
            if path.is_dir():
                continue
            if not path.is_file():
                raise ValueError(f"Lint input is linked or not a regular file: {path}")
            selected.append(path)
    if not selected:
        raise ValueError("No lint/test/hook files found; a pass would mean nothing")
    return sorted(selected)


def harvest(repository: Path, paths: list[Path]) -> dict[str, list[str]]:
    """Keep up to five source locations per package-owned prefix, including literal text."""
    sources: dict[str, list[str]] = {}
    for path in paths:
        relative = path.relative_to(repository).as_posix()
        for number, line in enumerate(read_text(path).splitlines(), 1):
            for match in CODES.finditer(line):
                prefix = match[1]
                if prefix in UPSTREAM:
                    continue
                locations = sources.setdefault(prefix, [])
                if len(locations) < 5:
                    locations.append(f"{relative}:{number}")
    return dict(sorted(sources.items()))


def roundtrip(repository: Path, prefixes: list[str], node_command: str) -> set[str]:
    """Send one batch to cspell and distinguish rejected words from tool failures."""
    node = shutil.which(node_command)
    if node is None:
        raise ValueError(f"Node command not found: {node_command}")
    runner = repository / "scripts/run-node-bin.js"
    config = repository / "cspell.json"
    for path in (runner, config):
        if not path.is_file() or is_link(path):
            raise ValueError(f"cspell input is missing or linked: {path}")
    result = subprocess.run(  # noqa: S603 - fixed repository cspell entry point and separate arguments.
        [node, str(runner), "cspell", "stdin", "--no-progress", "--no-color"],
        input="".join(f"{prefix}001\n" for prefix in prefixes),
        cwd=repository,
        capture_output=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
        check=False,
    )
    output = result.stdout + "\n" + result.stderr
    rejected = set(VERDICTS.findall(output))
    if result.returncode == 127:
        raise ValueError(
            "cspell is not installed. Run 'npm install' on the host that runs git hooks"
        )
    if rejected.difference(prefixes) or (result.returncode and not rejected):
        raise ValueError(
            f"cspell prefix roundtrip failed without matching verdicts (exit {result.returncode}): {output.strip()}"
        )
    return rejected


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--node-command", default="node")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    try:
        repository = args.repo_root.absolute()
        paths = selected_files(repository)
        sources = harvest(repository, paths)
        print(f"Scanned {len(paths)} lint/test/hook files; harvested {len(sources)} prefixes")
        if not sources:
            print("No lint-error-code tokens found. Nothing to validate.")
            return 0
        rejected = roundtrip(repository, list(sources), args.node_command)
        if args.verbose:
            print("Harvested prefixes: " + ", ".join(sources))
        if not rejected:
            print("All lint-error-code prefixes are known to cspell.")
            return 0
        print("Unknown lint-error-code prefixes:")
        for prefix in sorted(rejected):
            print(f"  {prefix}")
            for source in sources[prefix]:
                print(f"    - {source}")
        print("Copy-pasteable patch (cspell.json -> words):")
        print(json.dumps({"add_to_root_words": sorted(rejected)}, indent=2))
        return 1
    except (OSError, UnicodeError, ValueError, subprocess.SubprocessError) as error:
        print(f"[validate-lint-error-codes] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
