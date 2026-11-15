"""Add inferred languages to closed, unlabeled Markdown fences."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import json5
from cli_encoding import configure_output

if TYPE_CHECKING:
    from collections.abc import Iterable

OPENING = re.compile(r"^([ \t]{0,3})(`{3,}|~{3,})([ \t]*)(.*)$")
CLOSING = re.compile(r"^([ \t]{0,3})(`{3,}|~{3,})[ \t]*$")
JSON_TOKENS = re.compile(
    r""""(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|//[^\r\n]*|/\*[\s\S]*?\*/"""
    r"|(?P<special>[+-]?(?:Infinity|NaN)\b)"
    r"|(?P<number>[+-]?(?:0[xX][0-9a-fA-F]+|(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?))"
    r"|(?P<identifier>[A-Za-z_$][A-Za-z0-9_$]*)|[\s\S]"
)


def json_object(pairs: Iterable[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    spelling: dict[str, str] = {}
    for key, value in pairs:
        if not key:
            raise ValueError("PowerShell objects require nonempty property names.")
        folded_parts: list[str] = []
        for character in key:
            upper = character.upper()
            folded_parts.append(
                upper
                if len(upper) == 1 and (character.isascii() or not upper.isascii())
                else character
            )
        folded = "".join(folded_parts)
        if folded in spelling and spelling[folded] != key:
            raise ValueError("PowerShell object properties cannot differ only by case.")
        spelling[folded] = key
        result[key] = value
    return result


def normalize_json_tokens(text: str) -> str:
    normalized: list[str] = []
    for match in JSON_TOKENS.finditer(text):
        lexeme = match[0]
        if match.lastgroup == "special" and lexeme.startswith(("+", "-NaN")):
            raise ValueError("ConvertFrom-Json refuses this signed constant.")
        if match.lastgroup == "number":
            if lexeme.startswith("+"):
                raise ValueError("ConvertFrom-Json refuses explicit positive numeric signs.")
            if re.fullmatch(r"-?0\d+", lexeme):
                lexeme = str(int(lexeme, 8))
        if lexeme == "undefined" and re.match(r"\s*:", text[match.end() :]) is None:
            lexeme = "null"
        normalized.append(lexeme)
    return "".join(normalized)


def looks_like_json(text: str) -> bool:
    text = text.strip()
    if not (
        (text.startswith("{") and text.endswith("}"))
        or (text.startswith("[") and text.endswith("]"))
    ):
        return False
    try:
        normalized = normalize_json_tokens(text)
        json5.loads(normalized, object_pairs_hook=json_object)
    except (ValueError, RecursionError):
        return False
    return True


def infer_language(lines: list[str]) -> str:
    content = [line for line in lines if line.strip()]
    if not content:
        return "text"
    first = content[0].strip()
    text = "\n".join(content)

    def matches(pattern: str, value: str = text) -> bool:
        return re.search(pattern, value, re.IGNORECASE | re.MULTILINE) is not None

    if matches(
        r"^(graph|flowchart)\s+|^(sequenceDiagram|classDiagram|stateDiagram|erDiagram|journey|gantt|pie)\b",
        first,
    ):
        return "mermaid"
    if matches(r"^diff --git |^(---|\+\+\+) |^@@ ", first):
        return "diff"
    if looks_like_json(text):
        return "json"
    if matches(r"^<\?xml\b|^</?[A-Za-z!][^>]*>", first):
        return "xml"
    if matches(
        r"^\s*(Param\s*\(|function\s+[A-Za-z0-9_-]+\s*\{|Write-Host\b|Get-[A-Za-z]+\b|Set-[A-Za-z]+\b|Invoke-[A-Za-z]+\b|\$env:|\$LASTEXITCODE\b)"
    ):
        return "powershell"
    if matches(
        r"^\s*(#!/usr/bin/env bash|#!/bin/(ba)?sh|npm\s+run\b|git\s+\S+|bash\s+\S+|pwsh\s+|dotnet\s+|node\s+|export\s+[A-Za-z_][A-Za-z0-9_]*=|cd\s+|chmod\s+|curl\s+|UNITY_[A-Z0-9_]+=)|^\s*(if\s+\[|for\s+\S+\s+in\s+.+;\s+do|while\s+.+;\s+do)"
    ):
        return "bash"
    if matches(
        r"^\s*(using\s+[A-Za-z0-9_.]+;|namespace\s+[A-Za-z0-9_.]+|public\s+(sealed\s+|static\s+|partial\s+)?(class|struct|enum|interface)\b|private\s+|protected\s+|internal\s+)|\b(UnityEngine|MonoBehaviour|ScriptableObject|IEnumerable<|List<|Dictionary<)\b"
    ):
        return "csharp"
    if matches(
        r"^\s*(const|let|var)\s+[A-Za-z_$][A-Za-z0-9_$]*\s*=|^\s*(async\s+)?function\s+[A-Za-z_$][A-Za-z0-9_$]*\s*\(|\b(console\.log|require\(|process\.argv|module\.exports|=>)\b"
    ):
        return "javascript"
    yaml_lines = [line.strip() for line in content if not line.strip().startswith("#")]
    if yaml_lines and all(
        matches(r"^[-]?\s*[A-Za-z0-9_.-]+:\s*[^:]*$|^-\s+[A-Za-z0-9_.-]+:\s*", line)
        for line in yaml_lines
    ):
        return "yaml"
    return "text"


def repair(content: str) -> tuple[str, int]:
    newline = "\r\n" if "\r\n" in content else "\n"
    lines = re.split(r"\r\n|\n|\r", content)
    opening_index: int | None = None
    indent = ""
    fence = ""
    missing_language = False
    body: list[str] = []
    fixed = 0
    for index, line in enumerate(lines):
        if opening_index is None:
            match = OPENING.fullmatch(line)
            if match is not None:
                opening_index = index
                indent, fence = match[1], match[2]
                missing_language = not match[4].strip()
                body = []
            continue
        closing = CLOSING.fullmatch(line)
        if closing is not None and closing[2][0] == fence[0] and len(fence) <= len(closing[2]):
            if missing_language:
                lines[opening_index] = indent + fence + infer_language(body)
                fixed += 1
            opening_index = None
            continue
        if missing_language:
            body.append(line)
    return newline.join(lines), fixed


def repository_root() -> Path:
    git = shutil.which("git")
    if git is not None:
        result = subprocess.run(  # noqa: S603 - fixed Git inventory with no shell.
            [git, "rev-parse", "--show-toplevel"],
            capture_output=True,
            check=False,
            encoding="utf-8",
        )
        if result.returncode == 0 and result.stdout.strip():
            return Path(result.stdout.strip())
    return Path.cwd()


def markdown_paths(root: Path, candidates: list[str]) -> list[Path]:
    if not candidates:
        git = shutil.which("git")
        if git is None:
            raise FileNotFoundError("Git is required to discover staged Markdown files.")
        result = subprocess.run(  # noqa: S603 - NUL-delimited staged path inventory.
            [
                git,
                "-C",
                str(root),
                "diff",
                "--cached",
                "--name-only",
                "--diff-filter=ACM",
                "-z",
                "--",
                "*.md",
                "*.markdown",
            ],
            capture_output=True,
            check=False,
            encoding="utf-8",
        )
        if result.returncode != 0:
            raise RuntimeError(result.stderr.strip() or "Could not discover staged Markdown files.")
        candidates = result.stdout.rstrip("\0").split("\0") if result.stdout else []
    found: dict[str, Path] = {}
    for candidate in candidates:
        if not candidate.strip():
            continue
        path = Path(candidate.replace("\\", "/"))
        if path.suffix.lower() not in {".md", ".markdown"}:
            continue
        if not path.is_absolute():
            path = root / path
        if path.is_file():
            found.setdefault(str(path).casefold(), path)
    return sorted(found.values(), key=lambda path: str(path).casefold())


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--paths", nargs="*", default=[])
    parser.add_argument("--verbose", action="store_true")
    options = parser.parse_args(arguments)
    changed_files = 0
    changed_fences = 0
    try:
        for path in markdown_paths(repository_root(), options.paths):
            content = path.read_bytes().decode("utf-8-sig", errors="replace")
            updated, count = repair(content)
            if not count or updated == content:
                continue
            path.write_bytes(updated.encode("utf-8"))
            changed_files += 1
            changed_fences += count
            if options.verbose:
                print(
                    f"[markdown-fence-fix] Added language specifiers to {count} fenced code block(s): {path}"
                )
    except (OSError, RuntimeError) as error:
        print(f"[markdown-fence-fix] {error}", file=sys.stderr)
        return 1
    if changed_fences:
        print(
            f"[markdown-fence-fix] Added language specifiers to {changed_fences} fenced code block(s) across {changed_files} file(s)."
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
