"""Reject oversized expression-bearing GitHub Actions run blocks."""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path

import yaml
from cli_encoding import configure_output
from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode

MAXIMUM_LENGTH = 20000


@dataclass(frozen=True)
class RunBlock:
    line: int
    body: str

    @property
    def length(self) -> int:
        return len(self.body.encode("utf-16-le")) // 2


def run_blocks(text: str) -> list[RunBlock]:
    loader = yaml.SafeLoader(text)
    try:
        root: Node | None = loader.get_single_node()
    finally:
        loader.dispose()
    if root is None:
        return []
    lines = text.splitlines()
    blocks: list[RunBlock] = []
    pending: list[tuple[Node, bool]] = [(root, True)]
    visited: set[tuple[int, bool]] = set()
    while pending:
        node, command_context = pending.pop()
        identity = (id(node), command_context)
        if identity in visited:
            continue
        visited.add(identity)
        if isinstance(node, SequenceNode):
            pending.extend((child, False) for child in reversed(node.value))
        elif isinstance(node, MappingNode):
            for key, value in node.value:
                if command_context and isinstance(key, ScalarNode) and key.value == "run":
                    if not isinstance(value, ScalarNode):
                        raise ValueError(f"line {key.start_mark.line + 1}: run must be a scalar")
                    if value.style in {"|", ">"}:
                        body_end = value.end_mark.line + int(value.end_mark.column != 0)
                        body_lines = lines[value.start_mark.line + 1 : body_end]
                        while body_lines and not body_lines[-1].strip():
                            body_lines.pop()
                        body = "\n".join(body_lines)
                    else:
                        body = text[value.start_mark.index : value.end_mark.index].strip()
                    blocks.append(RunBlock(key.start_mark.line + 1, body))
                elif (
                    isinstance(key, ScalarNode)
                    and key.value == "steps"
                    and isinstance(value, SequenceNode)
                ):
                    pending.extend((step, True) for step in reversed(value.value))
                else:
                    pending.append((value, False))
    return sorted(blocks, key=lambda block: block.line)


def target_files(repository: Path) -> list[Path]:
    if not repository.is_dir():
        raise ValueError(f"Repository directory does not exist: {repository}")
    workflows = repository / ".github" / "workflows"
    actions = repository / ".github" / "actions"
    targets: set[Path] = set()
    for directory in (workflows, actions):
        if directory.is_symlink() or (directory.exists() and not directory.is_dir()):
            raise ValueError(f"Expected a physical workflow or action directory: {directory}")
    if workflows.is_dir():
        targets.update(path for path in workflows.iterdir() if path.suffix in {".yml", ".yaml"})
    pending = [actions] if actions.is_dir() else []
    while pending:
        for path in pending.pop().iterdir():
            if path.is_symlink():
                raise ValueError(f"Expected a physical action input: {path}")
            if path.is_dir():
                pending.append(path)
            elif path.name in {"action.yml", "action.yaml"}:
                targets.add(path)
    if not targets:
        raise ValueError("No workflow or composite action files were found")
    for path in targets:
        if path.is_symlink() or not path.is_file():
            raise ValueError(f"Expected a physical workflow or action file: {path}")
        if not path.resolve().is_relative_to(repository.resolve()):
            raise ValueError(f"Workflow or action escapes the repository: {path}")
    return sorted(targets)


def check(repository: Path, *, verbose: bool = False) -> int:
    failures = 0
    targets = target_files(repository)
    for path in targets:
        relative = path.relative_to(repository).as_posix()
        try:
            blocks = run_blocks(path.read_text(encoding="utf-8-sig"))
        except (OSError, UnicodeError, ValueError, RecursionError, yaml.YAMLError) as error:
            print(f"{relative}: could not inspect run blocks: {error}", file=sys.stderr)
            failures += 1
            continue
        for block in blocks:
            has_expression = "${{" in block.body
            if verbose:
                print(
                    f"  {relative}: run block at line {block.line} -> "
                    f"length={block.length}, hasExpression={has_expression}"
                )
            if has_expression and block.length > MAXIMUM_LENGTH:
                print(
                    f"{relative}:{block.line}: WFL001 run: block contains a ${{{{ }}}} "
                    f"expression and is {block.length} characters long, exceeding the "
                    f"{MAXIMUM_LENGTH}-character safe threshold. Move interpolated inputs "
                    "into an env: mapping and read them via $env:NAME or process.env.NAME."
                )
                failures += 1
    print(
        f"[lint-workflow-run-expression-length] Scanned {len(targets)} files; {failures} violation(s)."
    )
    return int(bool(failures))


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--verbose", action="store_true")
    options = parser.parse_args(arguments)
    try:
        return check(options.repo_root.resolve(), verbose=options.verbose)
    except (OSError, ValueError) as error:
        print(f"[lint-workflow-run-expression-length] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
