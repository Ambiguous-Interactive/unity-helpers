"""Reject test-only members and markers in production C# and emitted source."""

from __future__ import annotations

import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import cast

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
SCAN_ROOTS = ("Runtime", "Editor", "Generator~", "Samples~", "Styles")
SKIPPED = {"bin", "obj", ".git", "node_modules"}
HOST_PROJECTS = {
    "ProtobufNetV2Oracle",
    "WallstopStudios.UnityHelpers.CountingLoopAudit",
    "WallstopStudios.UnityHelpers.DocSamplesCheck",
    "WallstopStudios.UnityHelpers.EditorCheck",
    "WallstopStudios.UnityHelpers.EditorTestCheck",
    "WallstopStudios.UnityHelpers.IntegrationCheck",
    "WallstopStudios.UnityHelpers.RandomQuality",
    "WallstopStudios.UnityHelpers.SyntaxPolicy",
    "WallstopStudios.UnityHelpers.TestCheck",
    "WallstopStudios.UnityHelpers.TypeCheck",
}
SPACE = r"[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]"
TRIM = re.compile(r"^" + SPACE + r"+|" + SPACE + r"+$")
ESCAPE = re.compile(r"\\(?:u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|x[0-9a-fA-F]{1,4}|[^\r\n\u2028\u2029])")
IDENTIFIER_ESCAPE = re.compile(r"\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8}))")
IDENTIFIER = re.compile(r"\b[A-Za-z_][A-Za-z0-9_]*\b", re.ASCII)
TOKEN_START = re.compile(r"//|/\*|[\"']")
WIDE_CHARACTER = re.compile(r"[\U00010000-\U0010ffff]")
MASKED_CHARACTER = re.compile(r"[^\r\n]")
FORBIDDEN = re.compile(r"ForTest|TestHook|TestOnly", re.IGNORECASE | re.ASCII)
MARKER = re.compile(
    r"\btest[- ]only"
    + SPACE
    + r"+(?:hook|callback|seam|API|member|method|field|instrumentation|code|override|latch)\b"
    r"|\bexists for tests\b",
    re.IGNORECASE | re.ASCII,
)
EMITTED_CONDITIONAL = re.compile(
    r"(?:^|(?<=[\r\n\u2028\u2029]))"
    + SPACE
    + r"*#if"
    + SPACE
    + r"+UNITY_INCLUDE_TESTS"
    + SPACE
    + r"*(?=$|[\r\n\u2028\u2029])"
)


@dataclass(frozen=True)
class LiteralRange:
    start: int
    end: int


@dataclass(frozen=True)
class Tokens:
    code: str
    literals: list[str]
    ranges: list[LiteralRange]
    comments: list[str]


def is_production_path(relative: str) -> bool:
    parts = relative.replace("\\", "/").split("/")
    if parts[0] not in SCAN_ROOTS:
        return False
    project = parts[1] if len(parts) > 1 else ""
    return parts[0] != "Generator~" or not (project.endswith(".Tests") or project in HOST_PROJECTS)


def mask(value: str) -> str:
    return MASKED_CHARACTER.sub(" ", WIDE_CHARACTER.sub("  ", value))


def tokenize(source: str) -> Tokens:
    code: list[str] = []
    literals: list[str] = []
    ranges: list[LiteralRange] = []
    comments: list[str] = []
    index = 0
    while index < len(source):
        token = TOKEN_START.search(source, index)
        if token is None:
            code.append(source[index:])
            break
        code.append(source[index : token.start()])
        index = token.start()
        start = index
        if source.startswith(("//", "/*"), index):
            block = source.startswith("/*", index)
            if block:
                end = source.find("*/", index + 2)
                index = len(source) if end < 0 else end + 2
            else:
                match = re.search(r"[\r\n]", source[index:])
                index = len(source) if match is None else index + match.start()
            comments.append(source[start:index])
        elif source[index] in "\"'":
            quote = source[index]
            index += 1
            verbatim = quote == '"' and start > 0 and source[start - 1] == "@"
            raw_count = 0
            if quote == '"':
                while start + raw_count < len(source) and source[start + raw_count] == '"':
                    raw_count += 1
            value: list[str] = []
            if raw_count >= 3:
                end = source.find('"' * raw_count, start + raw_count)
                index = len(source) if end < 0 else end + raw_count
                value.append(source[start + raw_count : len(source) if end < 0 else end])
            else:
                while index < len(source):
                    if source[index] == quote:
                        if verbatim and source[index + 1 : index + 2] == quote:
                            value.append(quote)
                            index += 2
                            continue
                        index += 1
                        break
                    if not verbatim and source[index] == "\\":
                        escape = ESCAPE.match(source, index)
                        if escape is not None:
                            text = escape[0]
                            value.append(
                                chr(int(text[2:], 16))
                                if text[1] in "uUx"
                                else {"n": "\n", "r": "\r", "t": "\t"}.get(text[1], text[1])
                            )
                            index += len(text)
                            continue
                    value.append(source[index])
                    index += 1
            if quote == '"':
                literals.append("".join(value))
                ranges.append(LiteralRange(start, index))
        code.append(mask(source[start:index]))
    return Tokens("".join(code), literals, ranges, comments)


def forbidden_identifiers(code: str) -> list[str]:
    decoded = IDENTIFIER_ESCAPE.sub(lambda match: chr(int(match[1] or match[2], 16)), code)
    return [match[0] for match in IDENTIFIER.finditer(decoded) if FORBIDDEN.search(match[0])]


def analyze_file(source: str, generated: bool = False) -> list[str]:
    tokens = tokenize(source)
    violations = [
        f"forbidden production identifier {name}" for name in forbidden_identifiers(tokens.code)
    ]
    if any(MARKER.search(comment) for comment in tokens.comments):
        violations.append("explicit test-only production marker")
    if generated:
        emitted: list[str] = []
        for index, literal in enumerate(tokens.literals):
            previous = tokens.ranges[index - 1] if index > 0 else None
            if (
                previous is not None
                and TRIM.sub("", source[previous.end : tokens.ranges[index].start]) == "+"
            ):
                emitted[-1] += literal
            else:
                emitted.append(literal)
        for literal in emitted:
            violations.extend(
                f"forbidden emitted identifier {name}"
                for name in forbidden_identifiers(tokenize(literal).code)
            )
            if EMITTED_CONDITIONAL.search(literal):
                violations.append("emitted test-only conditional code")
    return list(dict.fromkeys(violations))


def collect_sources(root: Path) -> list[Path]:
    files: list[Path] = []

    def visit(directory: Path) -> None:
        with os.scandir(directory) as entries:
            for entry in entries:
                if entry.name in SKIPPED:
                    continue
                path = Path(entry.path)
                if not is_production_path(path.relative_to(root).as_posix()):
                    continue
                if entry.is_dir(follow_symlinks=False):
                    visit(path)
                elif entry.is_file(follow_symlinks=False) and entry.name.endswith(".cs"):
                    files.append(path)

    for relative in SCAN_ROOTS:
        directory = root / relative
        if not directory.is_dir():
            raise ValueError(
                f"missing production root directory {relative}; refusing partial acceptance."
            )
        visit(directory)
    if not files:
        raise ValueError("no production C# files found; refusing empty acceptance.")
    return sorted(files)


def main(root: Path = ROOT) -> int:
    configure_output()
    try:
        files = collect_sources(root)
        failed = 0
        for path in files:
            relative = path.relative_to(root).as_posix()
            for finding in analyze_file(
                path.read_text(encoding="utf-8"), relative.startswith("Generator~/")
            ):
                print(f"[production-test-hooks] {relative}: {finding}", file=sys.stderr)
                failed += 1
        print(f"[production-test-hooks] {len(files)} production files scanned, {failed} findings.")
        return int(failed != 0)
    except (OSError, ValueError) as error:
        print(f"[production-test-hooks] {error}", file=sys.stderr)
        return 1


def utf16_length(value: str) -> int:
    return len(value.encode("utf-16-le", errors="surrogatepass")) // 2


def api(payload: object) -> object:
    if not isinstance(payload, dict):
        raise ValueError("Expected an API request object")
    request = cast("dict[str, object]", payload)
    source = request.get("source")
    if not isinstance(source, str):
        raise ValueError("Expected a string source")
    action = request.get("action")
    if action == "isProductionPath":
        return is_production_path(source)
    if action == "analyzeFile":
        generated = request.get("generated", False)
        if not isinstance(generated, bool):
            raise ValueError("Expected a boolean generated flag")
        return analyze_file(source, generated)
    if action == "tokenize":
        tokens = tokenize(source)
        ranges: list[dict[str, int]] = []
        previous = 0
        offset = 0
        for item in tokens.ranges:
            offset += utf16_length(source[previous : item.start])
            start = offset
            offset += utf16_length(source[item.start : item.end])
            ranges.append({"start": start, "end": offset})
            previous = item.end
        return {
            "code": tokens.code,
            "literals": tokens.literals,
            "comments": tokens.comments,
            "literalRanges": ranges,
        }
    raise ValueError("Unknown production test-hook API action")


def entry() -> int:
    configure_output()
    if sys.argv[1:] == ["--api"]:
        try:
            payload: object = json.loads(sys.stdin.buffer.read().decode("utf-8"))
            print(json.dumps(api(payload), ensure_ascii=True))
            return 0
        except (OSError, ValueError) as error:
            print(str(error), file=sys.stderr)
            return 1
    return main(Path(os.environ.get("PRODUCTION_TEST_HOOKS_ROOT") or str(ROOT)))


if __name__ == "__main__":
    sys.exit(entry())
