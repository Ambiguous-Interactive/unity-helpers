"""Check documentation, navigation, and tracked-file ignore policies with Git."""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast
from urllib.parse import unquote, urlsplit

import yaml
from cli_encoding import configure_output
from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode


def git_command(
    root: Path,
    arguments: list[str],
    payload: bytes | None = None,
    acceptable: tuple[int, ...] = (0,),
) -> bytes:
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required to validate documentation ignore rules")
    result = subprocess.run(  # noqa: S603 - fixed read-only Git commands with separate arguments.
        [git, "-C", str(root), *arguments],
        input=payload,
        capture_output=True,
        check=False,
        timeout=60,
    )
    if result.returncode not in acceptable:
        raise ValueError(
            f"Git {arguments[0]} failed: {result.stderr.decode('utf-8', errors='replace').strip()}"
        )
    return result.stdout


def nul_paths(data: bytes) -> list[str]:
    return [os.fsdecode(part) for part in data.split(b"\0") if part]


def protected_paths(root: Path) -> list[str]:
    paths: list[str] = []
    pending = [root / name for name in ("docs", ".llm") if (root / name).exists()]
    while pending:
        directory = pending.pop()
        if directory.is_symlink():
            raise ValueError(f"Cannot inspect linked documentation directory: {directory}")
        for path in directory.iterdir():
            if path.is_dir():
                pending.append(path)
            elif path.is_file() or path.is_symlink():
                paths.append(path.relative_to(root).as_posix())
    return sorted(paths)


def mapping_value(node: MappingNode, name: str) -> Node | None:
    for key, value in node.value:
        if isinstance(key, ScalarNode) and key.value == name:
            return cast("Node", value)
    return None


def nav_paths(root: Path) -> list[Path]:
    config = root / "mkdocs.yml"
    if not config.is_file():
        return []
    loader = yaml.SafeLoader(config.read_text(encoding="utf-8-sig"))
    try:
        document: Node | None = loader.get_single_node()
    finally:
        loader.dispose()
    if not isinstance(document, MappingNode):
        raise ValueError("mkdocs.yml must contain a YAML mapping")
    docs = mapping_value(document, "docs_dir")
    docs_directory = root / (docs.value if isinstance(docs, ScalarNode) else "docs")
    nav = mapping_value(document, "nav")
    if nav is None:
        return []
    paths: list[Path] = []
    pending = [nav]
    visited: set[int] = set()
    while pending:
        node = pending.pop()
        if id(node) in visited:
            continue
        visited.add(id(node))
        if isinstance(node, SequenceNode):
            pending.extend(reversed(node.value))
        elif isinstance(node, MappingNode):
            pending.extend(value for _, value in reversed(node.value))
        elif isinstance(node, ScalarNode):
            target = urlsplit(cast("str", node.value))
            if target.scheme or target.netloc:
                continue
            name = unquote(target.path)
            if name.lower().endswith(".md"):
                path = docs_directory / name
                if not path.resolve().is_relative_to(root):
                    raise ValueError(f"Nav references a file outside the repository: {name}")
                paths.append(path)
    return paths


def check(root: Path, *, verbose: bool = False) -> int:
    actual_root = (
        git_command(root, ["rev-parse", "--show-toplevel"]).decode("utf-8").removesuffix("\n")
    )
    repository = Path(actual_root).resolve()
    protected = protected_paths(repository)
    navigation = nav_paths(repository)
    existing = [path.relative_to(repository).as_posix() for path in navigation if path.is_file()]
    candidates = sorted(set(protected) | set(existing))
    ignored: set[str] = set()
    if candidates:
        payload = b"\0".join(os.fsencode(path) for path in candidates) + b"\0"
        ignored.update(
            nul_paths(git_command(repository, ["check-ignore", "-z", "--stdin"], payload, (0, 1)))
        )
    tracked = nul_paths(git_command(repository, ["ls-files", "--cached", "-z"]))
    tracked_ignored = nul_paths(
        git_command(
            repository,
            ["ls-files", "--cached", "--ignored", "--exclude-per-directory=.gitignore", "-z"],
        )
    )
    errors: list[str] = []
    errors.extend(f"Protected file is gitignored: {path}" for path in protected if path in ignored)
    errors.extend(
        f"Nav references missing file: {path.relative_to(repository).as_posix()}"
        for path in navigation
        if not path.is_file()
    )
    errors.extend(f"Nav references gitignored file: {path}" for path in existing if path in ignored)
    errors.extend(f"Tracked file is also gitignored: {path}" for path in tracked_ignored)
    errors.extend(
        f"Agent progress artifact is tracked: {path}"
        for path in tracked
        if path.startswith("progress/") or path == "progress.meta"
    )
    for error in errors:
        print(f"UNH-GITIGNORE: {error}")
    if verbose or errors:
        print(
            f"Scanned {len(protected)} protected files, {len(navigation)} navigation references, and {len(tracked)} tracked files."
        )
    if errors:
        print(f"Gitignore docs safety check failed with {len(errors)} error(s).")
        return 1
    print("Gitignore docs/llm safety: OK")
    return 0


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--verbose", action="store_true")
    options = parser.parse_args(arguments)
    try:
        return check(options.repo_root.resolve(), verbose=options.verbose)
    except (
        OSError,
        ValueError,
        RecursionError,
        subprocess.SubprocessError,
        yaml.YAMLError,
    ) as error:
        print(f"Gitignore docs safety check failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
