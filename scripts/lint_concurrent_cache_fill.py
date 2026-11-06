"""Require atomic concurrent cache fills and static cache-factory lambdas."""

from __future__ import annotations

import argparse
import codecs
import re
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output, escape_command_data, escape_command_property
from csharp_sources import mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Styles")
DECLARATION = re.compile(
    r"ConcurrentDictionary\s*<(?:[^<>]|<(?:[^<>]|<[^<>]*>)*>)*>\s+(@?\w+)", re.DOTALL
)
DIRECTIVE = re.compile(r"^\s*#\s*(if|elif|else|endif)\b(.*)$")
FACTORY = re.compile(
    r"\.\s*(?:GetOrAdd|AddOrUpdate|GetOrCreateValue|GetValue)\s*(?:<[^;{}()]*>)?\s*\("
)
MARKER = "concurrent-overwrite:"


@dataclass(frozen=True)
class Finding:
    """Identify a non-atomic write or potentially allocating factory."""

    line: int
    message: str


@dataclass
class Scan:
    """Retain diagnostics and positive subject counts for one source."""

    findings: list[Finding]
    cache_files: int = 0
    skipped: int = 0
    exempted: int = 0
    lambdas: int = 0


def condition_state(condition: str) -> bool | None:
    """Exempt only a guard that proves this branch requires SINGLE_THREADED."""
    condition = condition.strip()
    if condition == "SINGLE_THREADED":
        return True
    if condition == "!SINGLE_THREADED":
        return False
    return None


def overwrite_context(lines: list[str], index: int) -> str:
    context = [lines[index]]
    inside_block = False
    for back in range(index - 1, -1, -1):
        above = lines[back].strip()
        if inside_block:
            context.append(above)
            if above.startswith("/*"):
                inside_block = False
        elif above.startswith("/*") and above.endswith("*/"):
            context.append(above)
        elif above.startswith("*/"):
            context.append(above)
            inside_block = True
        elif above.startswith("//"):
            context.append(above)
        else:
            break
    return "\n".join(reversed(context))


def has_overwrite_reason(context: str) -> bool:
    """Accept only a marker that occurs in a real comment."""
    without_comments = mask_noise(context, preserve_strings=True)
    return any(
        not without_comments[match.start() : match.end()].strip()
        for match in re.finditer(MARKER, context, re.IGNORECASE)
    )


def static_lambda(text: str, arrow: int) -> bool:
    index = arrow - 1
    while index >= 0 and text[index].isspace():
        index -= 1
    if index >= 0 and text[index] == ")":
        depth = 0
        while index >= 0:
            if text[index] == ")":
                depth += 1
            elif text[index] == "(":
                depth -= 1
                if depth == 0:
                    break
            index -= 1
        index -= 1
    else:
        while index >= 0 and (text[index].isalnum() or text[index] in "_@"):
            index -= 1
    while index >= 0 and text[index].isspace():
        index -= 1
    end = index + 1
    while index >= 0 and (text[index].isalnum() or text[index] == "_"):
        index -= 1
    keyword = text[index + 1 : end]
    if keyword == "async":
        while index >= 0 and text[index].isspace():
            index -= 1
        end = index + 1
        while index >= 0 and (text[index].isalnum() or text[index] == "_"):
            index -= 1
        keyword = text[index + 1 : end]
    return keyword == "static"


def scan_source(text: str) -> Scan:
    result = Scan([])
    if "ConcurrentDictionary" not in text and FACTORY.search(text) is None:
        return result
    original_lines = text.splitlines()
    protected = re.sub(
        r"(?m)^([ \t]*)#(?=[ \t]*(?:if|elif|else|endif)\b)",
        lambda match: match[1] + "\x01",
        text,
    )
    code = mask_noise(protected).replace("\x01", "#")
    names = set(DECLARATION.findall(code))
    if names:
        result.cache_files = 1
        patterns = {
            name: re.compile(
                r"(?<![\w.])(?:this\s*\.\s*)?" + re.escape(name) + r"\s*\[[^\]]*\]\s*=(?!=)"
            )
            for name in names
        }
        lines = code.splitlines()
        stack: list[bool | None] = []
        for index, line in enumerate(lines):
            directive = DIRECTIVE.match(line)
            if directive:
                kind, condition = directive.groups()
                if kind == "if":
                    stack.append(condition_state(condition))
                elif stack:
                    if kind == "elif":
                        stack[-1] = condition_state(condition)
                    elif kind == "else" and stack[-1] is not None:
                        stack[-1] = not stack[-1]
                    elif kind == "endif":
                        stack.pop()
                continue
            for name in sorted(names):
                if not patterns[name].search(line):
                    continue
                if True in stack:
                    result.skipped += 1
                elif has_overwrite_reason(overwrite_context(original_lines, index)):
                    result.exempted += 1
                else:
                    result.findings.append(
                        Finding(
                            index + 1,
                            f"'{name}' is a ConcurrentDictionary filled through its indexer. Use GetOrAdd with a static lambda, or TryAdd. A deliberate overwrite must give its reason with '{MARKER} <reason>'.",
                        )
                    )
    text = code
    for call in FACTORY.finditer(text):
        opening = call.end() - 1
        depth = 0
        closing = -1
        for index in range(opening, len(text)):
            if text[index] == "(":
                depth += 1
            elif text[index] == ")":
                depth -= 1
                if depth == 0:
                    closing = index
                    break
        if closing < 0:
            continue
        depth = 0
        saw_lambda = False
        for index in range(opening + 1, closing):
            char = text[index]
            if char in "([{":
                depth += 1
            elif char in ")]}":
                depth -= 1
            elif depth == 0 and char == ",":
                saw_lambda = False
            elif depth == 0 and text.startswith("=>", index) and not saw_lambda:
                saw_lambda = True
                result.lambdas += 1
                if not static_lambda(text, index):
                    result.findings.append(
                        Finding(
                            text.count("\n", 0, index) + 1,
                            "A lambda handed to a cache factory must be 'static'. Pass captured values through the state-taking overload.",
                        )
                    )
    return result


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    total = Scan([])
    try:
        files = source_files(repository, SOURCE_ROOTS)
        for path in files:
            raw = path.read_bytes()
            encoding = (
                "utf-32"
                if raw.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE))
                else "utf-16"
                if raw.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE))
                else "utf-8-sig"
            )
            result = scan_source(raw.decode(encoding))
            total.cache_files += result.cache_files
            total.skipped += result.skipped
            total.exempted += result.exempted
            total.lambdas += result.lambdas
            total.findings.extend(result.findings)
            for finding in result.findings:
                relative = escape_command_property(path.relative_to(repository).as_posix())
                print(
                    f"::error file={relative},line={finding.line}::{escape_command_data(finding.message)}"
                )
        if args.verbose:
            print(
                f"[lint-concurrent-cache-fill] Scanned {total.cache_files} file(s) declaring a ConcurrentDictionary; skipped {total.skipped} write(s) inside SINGLE_THREADED branches; {total.exempted} deliberate overwrite(s) exempted; checked {total.lambdas} cache-factory lambda(s); {len(files)} C# sources."
            )
        if total.findings:
            return 1
        print(
            f"[lint-concurrent-cache-fill] OK: every ConcurrentDictionary fill is atomic and every cache-factory lambda is static ({total.cache_files} file(s) scanned, {total.exempted} exempted, {total.lambdas} lambda(s) checked)."
        )
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"::error::{escape_command_data(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
