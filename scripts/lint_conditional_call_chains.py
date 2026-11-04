"""Reject calls between Conditional methods, whose symbols belong to the calling assembly."""

from __future__ import annotations

import argparse
import re
from dataclasses import dataclass
from pathlib import Path

from cli_encoding import configure_output
from csharp_sources import mask_noise, source_files

ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOTS = ("Runtime", "Editor", "Styles")
ATTRIBUTE = re.compile(
    r"\[\s*(?:(?:global::)?System\.Diagnostics\.)?Conditional(?:Attribute)?\s*\("
)
SIGNATURE = re.compile(
    r"(?:^[ \t]*|(?<=\])[ \t]*)(?:(?:public|internal|protected|private|static|"
    r"virtual|override|sealed|abstract|new|unsafe|partial|extern|async)\s+)*"
    r"void\s+@?(\w+)\s*(?:<[^>()]*>)?\s*\(",
    re.MULTILINE,
)
EXTERNAL_RECEIVERS = (
    "Math",
    "Mathf",
    "System.Math",
    "Debug",
    "UnityEngine.Debug",
    "Assert",
    "UnityEngine.Assertions.Assert",
    "Console",
    "Trace",
    "Debug.unityLogger",
)
EXTERNAL_CALLS = tuple(
    re.compile(r"(?<![\w.])" + re.escape(name) + r"\s*\.\s*\w+\s*\(") for name in EXTERNAL_RECEIVERS
)


@dataclass(frozen=True)
class Method:
    name: str
    path: str
    line: int
    body: str


def closing(text: str, start: int, left: str, right: str) -> int | None:
    depth = 0
    for index in range(start, len(text)):
        if text[index] == left:
            depth += 1
        elif text[index] == right:
            depth -= 1
            if depth == 0:
                return index
    return None


def methods_in(source: str, path: str) -> tuple[list[Method], list[str]]:
    if "Conditional" not in source:
        return [], []
    code = mask_noise(source)
    found: list[Method] = []
    errors: list[str] = []
    for signature in SIGNATURE.finditer(code):
        end = signature.start()
        begin = end
        while begin > 0:
            cursor = begin - 1
            while cursor >= 0 and code[cursor].isspace():
                cursor -= 1
            if cursor < 0 or code[cursor] != "]":
                break
            depth = 1
            cursor -= 1
            while cursor >= 0 and depth:
                depth += (code[cursor] == "]") - (code[cursor] == "[")
                cursor -= 1
            if depth:
                break
            begin = cursor + 1
        if ATTRIBUTE.search(code[begin:end]) is None:
            continue
        name = signature.group(1)
        line = source.count("\n", 0, signature.start()) + 1
        parameters_end = closing(code, signature.end() - 1, "(", ")")
        body_start = (
            None if parameters_end is None else re.search(r"=>|[;{]", code[parameters_end + 1 :])
        )
        if parameters_end is None or body_start is None or body_start.group() == ";":
            errors.append(
                f"{path}:{line}: Could not locate a body for [Conditional] method '{name}'."
            )
            continue
        start = parameters_end + 1 + body_start.start()
        end_body = (
            code.find(";", start) if body_start.group() == "=>" else closing(code, start, "{", "}")
        )
        if end_body is None or end_body < 0:
            errors.append(f"{path}:{line}: Unclosed [Conditional] method '{name}'.")
            continue
        found.append(Method(name, path, line, code[start : end_body + 1]))
    return found, errors


def violations(methods: list[Method]) -> list[str]:
    names = sorted({method.name for method in methods})
    patterns = {
        name: re.compile(r"(?<![\w])" + re.escape(name) + r"\s*(?:<[^;{}()]*>)?\s*\(")
        for name in names
    }
    findings: list[str] = []
    for method in methods:
        body = method.body
        for pattern in EXTERNAL_CALLS:
            body = pattern.sub("ExternalCall(", body)
        findings.extend(
            f"::error file={method.path},line={method.line}::[Conditional] method "
            f"'{method.name}' calls [Conditional] method '{name}'. "
            "Route through a non-conditional core instead."
            for name in names
            if patterns[name].search(body)
        )
    return findings


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository: Path = args.repo_root
    try:
        methods: list[Method] = []
        errors: list[str] = []
        files = source_files(repository, SOURCE_ROOTS)
        for path in files:
            found, unparsed = methods_in(
                path.read_text(encoding="utf-8-sig"),
                path.relative_to(repository).as_posix(),
            )
            methods.extend(found)
            errors.extend(unparsed)
        if not methods:
            errors.append(
                f"No [Conditional] methods found across {len(files)} files; a pass would mean nothing."
            )
        errors.extend(violations(methods))
        for error in errors:
            print(error)
        if errors:
            return 1
        if args.verbose:
            print(
                "[lint-conditional-call-chains] Methods: "
                + ", ".join(sorted({method.name for method in methods}))
            )
        print(
            f"[lint-conditional-call-chains] OK: no [Conditional] method calls another ({len(methods)} checked, 0 unparsed)."
        )
        return 0
    except (OSError, UnicodeError, ValueError) as error:
        print(f"[lint-conditional-call-chains] {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
