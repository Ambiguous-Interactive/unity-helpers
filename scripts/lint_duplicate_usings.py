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
    r"\bnamespace\s+(?P<namespace>[\w.@\s]+?)\s*(?P<terminator>[{;])"
    r"|(?P<extern>\bextern\s+alias\s+@?\w+\s*;)"
    r"|(?P<using>\b(?:global\s+)?using\s+[^;{}]+;)"
    r"|(?P<brace>[{}])|(?P<other>[^\s{};]+)"
)


@dataclass
class Scope:
    name: str
    depth: int
    collecting: bool = True
    seen: dict[str, int] = field(default_factory=dict[str, int])


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


def analyze(source: str) -> list[tuple[int, str]]:
    """Report duplicate imports after removing comments and literal contents."""
    candidates: set[str] = set()
    uncertain = False
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

    code = mask_noise(source)
    scopes = [Scope("<global>", -1)]
    depth = 0
    findings: list[tuple[int, str]] = []
    for token in TOKENS.finditer(code):
        if token["namespace"] is not None:
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
            first = scope.seen.get(key)
            if first is None:
                scope.seen[key] = line
            else:
                findings.append(
                    (
                        line,
                        (
                            f"UNH007: Duplicate using directive '{key}' in scope '{scope.name}' "
                            f"(first declaration at line {first})."
                        ),
                    )
                )
    return findings


def explicit_files(paths: list[Path]) -> list[Path]:
    """Validate explicit physical inputs without following linked ancestors."""
    selected: set[Path] = set()
    for path in paths:
        path = path.absolute()
        if not path.is_file() or any(is_link(parent) for parent in (path, *path.parents)):
            raise ValueError(f"Source is missing or linked: {path}")
        if path.suffix.lower() == ".cs":
            selected.add(path)
    return sorted(selected)


def main(argv: list[str] | None = None) -> int:
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
        files = explicit_files(paths) if paths else files_to_check(repository, staged_only=staged)
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
