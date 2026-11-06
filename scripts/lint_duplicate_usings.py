"""Reject repeated C# imports within the same namespace declaration."""

from __future__ import annotations

import argparse
import re
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import is_link, mask_noise
from lint_unity_file_naming import files_to_check, read_source

ROOT = Path(__file__).resolve().parents[1]
DIRECTIVE = re.compile(r"\b(?:global\s+)?using\s+([^;{}]+);")
TOKENS = re.compile(
    r"^[ \t]*\x01[ \t]*(?P<conditional>if|elif|else|endif)\b[^\r\n]*"
    r"|\bnamespace\s+(?P<namespace>[\w.@\s]+?)\s*(?P<terminator>[{;])"
    r"|(?P<extern>\bextern\s+alias\s+@?\w+\s*;)"
    r"|(?P<using>\b(?:global\s+)?using\s+[^;{}]+;)"
    r"|(?P<brace>[{}])|(?P<other>[^\s{};]+)",
    re.MULTILINE,
)
ConditionPath = tuple[tuple[int, int], ...]


@dataclass
class Scope:
    """Keep imports local to one namespace declaration."""

    name: str
    depth: int
    collecting: bool = True
    seen: dict[str, list[tuple[int, ConditionPath]]] = field(
        default_factory=dict[str, list[tuple[int, ConditionPath]]]
    )


@dataclass
class Conditional:
    """Isolate import comparisons and declaration state across conditional arms."""

    number: int
    arm: int
    collecting: tuple[tuple[Scope, bool], ...]


def normalized(directive: str) -> str | None:
    """Distinguish imports and aliases from using statements and declarations."""
    directive = re.sub(r"\s+", " ", directive.strip())
    match = re.fullmatch(
        r"(?P<global>global )?using (?P<static>static )?"
        r"(?:(?P<alias>@?\w+)\s*=\s*)?"
        r"(?P<name>[^;=]+)\s*;",
        directive,
    )
    if match is None or (match["static"] and match["alias"]):
        return None
    name = re.sub(r"\s+", "", match["name"])
    if (
        match["alias"] is None
        and match["static"] is None
        and not re.fullmatch(r"(?:@?\w+\s*::\s*)?@?\w+(?:\s*\.\s*@?\w+)*", match["name"].strip())
    ):
        return None
    prefix = "global " if match["global"] else ""
    alias = f"{match['alias']} = " if match["alias"] else ""
    static = "static " if match["static"] else ""
    return f"{prefix}using {static}{alias}{name};"


def can_coexist(first: ConditionPath, second: ConditionPath) -> bool:
    """Refuse imports from different arms of the same conditional group."""
    return not any(
        group == other_group and arm != other_arm
        for group, arm in first
        for other_group, other_arm in second
    )


def analyze(source: str) -> list[tuple[int, str]]:
    """Report duplicate imports after removing comments and literal contents."""
    candidates: set[str] = set()
    uncertain = re.search(r"\b(?:global|using)\s*/[/*]", source) is not None
    for match in DIRECTIVE.finditer(source):
        key = normalized(match.group())
        if key is None:
            uncertain = True
        if key is not None:
            if key in candidates:
                break
            candidates.add(key)
    else:
        if not uncertain:
            return []

    protected = re.sub(
        r"(?m)^([ \t]*)#(?=[ \t]*(?:if|elif|else|endif)\b)",
        lambda match: match[1] + "\x01",
        source,
    )
    code = mask_noise(protected)
    scopes = [Scope("<global>", -1)]
    branches: list[Conditional] = []
    next_condition = 0
    depth = 0
    findings: list[tuple[int, str]] = []
    for token in TOKENS.finditer(code):
        if token["conditional"] is not None:
            if token["conditional"] == "if":
                next_condition += 1
                branches.append(
                    Conditional(next_condition, 0, tuple((s, s.collecting) for s in scopes))
                )
            elif branches:
                branch = branches[-1]
                for scope, collecting in branch.collecting:
                    scope.collecting = collecting
                if token["conditional"] == "endif":
                    branches.pop()
                else:
                    branch.arm += 1
        elif token["namespace"] is not None:
            if token["terminator"] == "{":
                depth += 1
                scopes.append(Scope(token["namespace"].strip(), depth))
            else:
                scopes.append(Scope(token["namespace"].strip(), -1))
        elif token["brace"] is not None:
            depth += 1 if token["brace"] == "{" else -1
            while len(scopes) > 1 and scopes[-1].depth >= 0 and depth < scopes[-1].depth:
                scopes.pop()
        elif token["extern"] is not None:
            continue
        elif scopes[-1].collecting:
            directive = token["using"]
            key = normalized(directive) if directive is not None else None
            if key is None:
                scopes[-1].collecting = False
                continue
            line = code.count("\n", 0, token.start()) + 1
            scope = scopes[-1]
            path = tuple((branch.number, branch.arm) for branch in branches)
            previous = scope.seen.setdefault(key, [])
            for first, conditions in previous:
                if can_coexist(conditions, path):
                    findings.append(
                        (
                            line,
                            (
                                f"UNH007: Duplicate using directive '{key}' in scope '{scope.name}' "
                                f"(first declaration at line {first})."
                            ),
                        )
                    )
                    break
            previous.append((line, path))
    return findings


def explicit_files(paths: list[Path], repository: Path) -> list[Path]:
    """Refuse redirects inside the checkout while allowing its linked ancestors."""
    selected: set[Path] = set()
    root = repository.resolve()
    for requested in paths:
        path = requested.absolute()
        boundary = next((p for p in path.parents if p.resolve() == root), None)
        if not path.is_file() or is_link(path):
            raise ValueError(f"Source is missing or linked: {path}")
        if boundary is not None:
            for parent in path.parents:
                if parent == boundary:
                    break
                if is_link(parent):
                    raise ValueError(f"Source has a linked parent inside the checkout: {path}")
        if path.suffix.lower() == ".cs":
            selected.add(path)
    return sorted(selected)


def main(argv: list[str] | None = None) -> int:
    """Run the checker against the selected source inputs."""
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--staged-only", action="store_true")
    parser.add_argument("--paths", nargs="+", type=Path)
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    try:
        paths: list[Path] | None = args.paths
        staged = bool(args.staged_only and not paths)
        files = (
            explicit_files(paths, repository)
            if paths
            else files_to_check(repository, staged_only=staged)
        )
        failures = 0
        for path in files:
            try:
                source = read_source(repository, path, staged_only=staged)
                for line, message in analyze(source):
                    print(f"::error file={path},line={line}::{message}")
                    failures += 1
            except (
                OSError,
                UnicodeError,
                ValueError,
                subprocess.SubprocessError,
            ) as error:
                print(f"[lint-duplicate-usings] {path}: {error}")
                failures += 1
        if args.verbose or failures or (not paths and not staged):
            print(f"[lint-duplicate-usings] Checked {len(files)} files; {failures} findings.")
        return int(bool(failures))
    except (OSError, UnicodeError, ValueError, subprocess.SubprocessError) as error:
        print(f"[lint-duplicate-usings] {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
