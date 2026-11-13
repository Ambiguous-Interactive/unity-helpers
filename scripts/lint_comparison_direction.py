"""Keep C# comparisons left-to-right without rewriting generic or pattern operators."""

from __future__ import annotations

import os
import re
import sys
from bisect import bisect_left
from pathlib import Path
from typing import Literal, NamedTuple

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ROOTS = ("Runtime", "Editor", "Tests", "Generator~")
PUNCTUATORS = (
    "??=",
    "...",
    "=>",
    "==",
    "!=",
    "&&",
    "||",
    "??",
    "?.",
    "?[",
    "++",
    "--",
    "+=",
    "-=",
    "*=",
    "/=",
    "%=",
    "&=",
    "|=",
    "^=",
    "->",
    "::",
)
PUNCTUATION = re.compile("|".join(re.escape(value) for value in PUNCTUATORS))
KEYWORDS = frozenset(
    [
        "abstract",
        "as",
        "base",
        "bool",
        "break",
        "byte",
        "case",
        "catch",
        "char",
        "checked",
        "class",
        "const",
        "continue",
        "decimal",
        "default",
        "delegate",
        "do",
        "double",
        "else",
        "enum",
        "event",
        "explicit",
        "extern",
        "false",
        "finally",
        "fixed",
        "float",
        "for",
        "foreach",
        "goto",
        "if",
        "implicit",
        "in",
        "int",
        "interface",
        "internal",
        "is",
        "lock",
        "long",
        "namespace",
        "new",
        "null",
        "object",
        "operator",
        "out",
        "override",
        "params",
        "private",
        "protected",
        "public",
        "readonly",
        "ref",
        "return",
        "sbyte",
        "sealed",
        "short",
        "sizeof",
        "stackalloc",
        "static",
        "string",
        "struct",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "uint",
        "ulong",
        "unchecked",
        "unsafe",
        "ushort",
        "using",
        "virtual",
        "void",
        "volatile",
        "while",
    ]
)
TYPE_KEYWORDS = frozenset(
    [
        "bool",
        "byte",
        "char",
        "decimal",
        "double",
        "float",
        "int",
        "long",
        "object",
        "sbyte",
        "short",
        "string",
        "uint",
        "ulong",
        "ushort",
        "void",
        "in",
        "out",
        "ref",
        "readonly",
    ]
)
TYPE_PUNCTUATION = frozenset((".", ",", "?", "[", "]", "*", "::", "(", ")"))
TERMINATING_KEYWORDS = frozenset(("this", "base", "true", "false", "default"))
TERMINATING_PUNCTUATION = frozenset((")", "]", "++", "--"))
PATTERN_CONNECTIVES = frozenset(("and", "or", "not"))
BOUNDARY_PUNCTUATION = frozenset(
    (
        "(",
        ")",
        "[",
        "]",
        "{",
        "}",
        ",",
        ";",
        ":",
        "?",
        "=>",
        "==",
        "!=",
        "&&",
        "||",
        "??",
        "&",
        "|",
        "^",
        "=",
        "+=",
        "-=",
        "*=",
        "/=",
        "%=",
        "&=",
        "|=",
        "^=",
        "??=",
        "...",
    )
)
BOUNDARY_KEYWORDS = frozenset(
    [
        "return",
        "if",
        "while",
        "do",
        "for",
        "foreach",
        "switch",
        "case",
        "is",
        "as",
        "throw",
        "else",
        "in",
        "out",
        "ref",
        "lock",
        "using",
        "goto",
        "when",
        "yield",
        "params",
        "const",
    ]
)
IDENTIFIER = re.compile(r"@?[A-Za-z_][A-Za-z0-9_]*")
JS_WHITESPACE = " \t\v\f\r\n\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff"
SPACE = re.compile("[" + re.escape(JS_WHITESPACE.replace("\n", "")) + "]+")
NUMBER = re.compile(r"[0-9](?:[eE][+-]|[0-9A-Za-z_]|[.](?=[0-9]))*")
HEX_NUMBER = re.compile(r"[0-9](?:[0-9A-Za-z_]|[.](?=[0-9]))*")


class Token(NamedTuple):
    kind: Literal["keyword", "identifier", "number", "string", "angle", "punctuation"]
    text: str
    start: int
    end: int
    line: int
    column: int
    hole: str | None
    glued: bool


class Violation(NamedTuple):
    operator: str
    operator_start: int
    operator_end: int
    line: int
    column: int
    operator_index: int
    operator_token_end: int


class Plan(NamedTuple):
    start: int = 0
    end: int = 0
    replacement: str | None = None
    reason: str | None = None


class TextRange(NamedTuple):
    start: int
    end: int


def utf16_length(text: str) -> int:
    return len(text.encode("utf-16-le", errors="surrogatepass")) // 2


def consume_literal(text: str, start: int) -> tuple[int, list[TextRange]] | None:
    index = start
    interpolated = False
    verbatim = False
    length = len(text)
    while index < length and text[index] in ("@", "$"):
        interpolated |= text[index] == "$"
        verbatim |= text[index] == "@"
        index += 1
    if index == length or text[index] not in ('"', "'"):
        return None
    quote = text[index]
    if quote == '"' and text.startswith('"""', index):
        fence = 0
        while index + fence < length and text[index + fence] == '"':
            fence += 1
        found = text.find('"' * fence, index + fence)
        return (length if found < 0 else found + fence, [])
    index += 1
    holes: list[TextRange] = []
    while index < length:
        char = text[index]
        if verbatim and char == quote and text[index : index + 2] == quote * 2:
            index += 2
            continue
        if not verbatim and char == "\\":
            index += 2
            continue
        if interpolated and char == "{":
            if text[index : index + 2] == "{{":
                index += 2
                continue
            hole_start = index + 1
            depth = 1
            index += 1
            while index < length and depth > 0:
                if text[index] == "{":
                    depth += 1
                elif text[index] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                elif text[index] in ('"', "'"):
                    nested = consume_literal(text, index)
                    if nested is not None:
                        index = nested[0]
                        continue
                index += 1
            holes.append(TextRange(hole_start, index))
            index += 1
            continue
        if char == quote:
            return index + 1, holes
        if not verbatim and char == "\n":
            return None
        index += 1
    return index, holes


def tokenize(
    text: str, line_offset: int = 0, column_offset: int = 0, hole_tag: str | None = None
) -> list[Token]:
    tokens: list[Token] = []
    index = 0
    line = 1
    line_start = 0
    length = len(text)
    ascii_text = text.isascii()

    def push(
        kind: Literal["keyword", "identifier", "number", "string", "angle", "punctuation"],
        start: int,
        end: int,
    ) -> None:
        tokens.append(
            Token(
                kind,
                text[start:end],
                start,
                end,
                line + line_offset,
                (start - line_start if ascii_text else utf16_length(text[line_start:start]))
                + 1
                + (column_offset if line == 1 else 0),
                hole_tag,
                start > 0 and text[start - 1] not in JS_WHITESPACE,
            )
        )

    def advance_lines(start: int, end: int) -> None:
        nonlocal line, line_start
        line += text.count("\n", start, end)
        last_newline = text.rfind("\n", start, end)
        if last_newline >= 0:
            line_start = last_newline + 1

    while index < length:
        char = text[index]
        if char == "\n":
            line += 1
            index += 1
            line_start = index
            continue
        if char in JS_WHITESPACE:
            whitespace = SPACE.match(text, index)
            index = whitespace.end() if whitespace is not None else index + 1
            continue
        if (char == "#" and not text[line_start:index].strip(JS_WHITESPACE)) or text.startswith(
            "//", index
        ):
            end = text.find("\n", index)
            index = length if end < 0 else end
            continue
        if text.startswith("/*", index):
            end = text.find("*/", index + 2)
            stop = length if end < 0 else end + 2
            advance_lines(index, stop)
            index = stop
            continue
        if char in ('"', "'", "@", "$"):
            literal = consume_literal(text, index)
            if literal is not None:
                literal_end, holes = literal
                for hole in holes:
                    hole_line = line + text.count("\n", line_start, hole.start)
                    hole_line_start = text.rfind("\n", 0, hole.start) + 1
                    hole_tokens = tokenize(
                        text[hole.start : hole.end],
                        hole_line + line_offset - 1,
                        utf16_length(text[hole_line_start : hole.start]),
                        f"{hole_tag or ''}/{hole.start}",
                    )
                    tokens.extend(
                        token._replace(start=token.start + hole.start, end=token.end + hole.start)
                        for token in hole_tokens
                    )
                push("string", index, literal_end)
                advance_lines(index, literal_end)
                index = literal_end
                continue
        identifier = IDENTIFIER.match(text, index)
        if identifier is not None:
            stop = identifier.end()
            word = identifier.group()
            push("keyword" if word in KEYWORDS else "identifier", index, stop)
            index = stop
            continue
        if char in "0123456789":
            number = (HEX_NUMBER if text[index : index + 2].lower() == "0x" else NUMBER).match(
                text, index
            )
            if number is not None:
                stop = number.end()
                push("number", index, stop)
                index = stop
                continue
        if char in ("<", ">"):
            push("angle", index, index + 1)
            index += 1
            continue
        punctuation = PUNCTUATION.match(text, index)
        punctuator = punctuation.group() if punctuation is not None else char
        push("punctuation", index, index + len(punctuator))
        index += len(punctuator)
    return tokens


def mark_generics(tokens: list[Token]) -> set[int]:
    generic: set[int] = set()
    for index, token in enumerate(tokens):
        if token.kind != "angle" or token.text != "<" or index in generic or index == 0:
            continue
        previous = tokens[index - 1]
        if not (
            previous.kind == "identifier"
            or (previous.kind == "keyword" and previous.text in TYPE_KEYWORDS)
        ):
            continue
        depth = 0
        parentheses = 0
        matched = -1
        for inner_index in range(index, len(tokens)):
            inner = tokens[inner_index]
            if inner.kind == "angle" and inner.text == "<":
                depth += 1
                continue
            if inner.kind == "angle" and inner.text == ">":
                depth -= 1
                if depth == 0 and parentheses == 0:
                    matched = inner_index
                if depth <= 0:
                    break
                continue
            if inner.kind == "punctuation" and inner.text == "(":
                parentheses += 1
            elif inner.kind == "punctuation" and inner.text == ")":
                parentheses -= 1
                if parentheses < 0:
                    break
            if not (
                inner.kind == "identifier"
                or (inner.kind == "keyword" and inner.text in TYPE_KEYWORDS)
                or (inner.kind == "punctuation" and inner.text in TYPE_PUNCTUATION)
            ):
                break
        if matched >= 0:
            generic.update(
                position
                for position in range(index, matched + 1)
                if tokens[position].kind == "angle"
            )
    return generic


def find_violations(tokens: list[Token], generic: set[int]) -> list[Violation]:
    violations: list[Violation] = []
    index = 0
    while index < len(tokens):
        current = index
        index += 1
        token = tokens[current]
        if token.kind != "angle" or token.text != ">" or current in generic:
            continue
        following = tokens[index] if index < len(tokens) else None
        if following and following.glued and following.kind == "angle" and following.text == ">":
            while index < len(tokens) and tokens[index].glued and tokens[index].kind == "angle":
                index += 1
            continue
        operator = ">"
        operator_end = token.end
        if following and following.glued and following.kind == "punctuation":
            if following.text == "=":
                operator = ">="
                operator_end = following.end
            elif following.text == "==":
                continue
        if current == 0:
            continue
        left = tokens[current - 1]
        if (
            (left.kind == "identifier" and left.text not in PATTERN_CONNECTIVES)
            or left.kind in ("number", "string")
            or (left.kind == "keyword" and left.text in TERMINATING_KEYWORDS)
            or (left.kind == "punctuation" and left.text in TERMINATING_PUNCTUATION)
            or (left.kind == "angle" and left.text == ">" and current - 1 in generic)
        ):
            violations.append(
                Violation(
                    operator,
                    token.start,
                    operator_end,
                    token.line,
                    token.column,
                    current,
                    current + (operator == ">="),
                )
            )
    return violations


def is_boundary(token: Token) -> bool:
    return (token.kind == "punctuation" and token.text in BOUNDARY_PUNCTUATION) or (
        token.kind == "keyword" and token.text in BOUNDARY_KEYWORDS
    )


def operand_range(
    tokens: list[Token], violation: Violation, direction: Literal["left", "right"]
) -> TextRange | None:
    step = -1 if direction == "left" else 1
    opens = (")", "]", "}") if direction == "left" else ("(", "[", "{")
    closes = ("(", "[", "{") if direction == "left" else (")", "]", "}")
    depth = 0
    index = (
        violation.operator_index - 1 if direction == "left" else violation.operator_token_end + 1
    )
    last = -1
    hole = tokens[violation.operator_index].hole
    while 0 <= index < len(tokens):
        token = tokens[index]
        if token.hole != hole:
            break
        if token.kind == "punctuation" and token.text in opens:
            depth += 1
        elif token.kind == "punctuation" and token.text in closes:
            if depth == 0:
                break
            depth -= 1
        elif depth == 0 and is_boundary(token):
            break
        last = index
        index += step
    if last < 0 or depth > 0:
        return None
    return (
        TextRange(last, violation.operator_index)
        if direction == "left"
        else TextRange(violation.operator_token_end + 1, last + 1)
    )


def is_literal_constant(tokens: list[Token], extent: TextRange) -> bool:
    count = extent.end - extent.start
    if count == 2 and tokens[extent.start].text in ("+", "-"):
        return tokens[extent.start + 1].kind == "number"
    if count != 1:
        return False
    token = tokens[extent.start]
    return (
        token.kind == "number"
        or (token.kind == "string" and token.text.startswith(('"', "'", '@"')))
        or (token.kind == "keyword" and token.text in ("true", "false", "null"))
    )


def plan_fix(text: str, tokens: list[Token], generic: set[int], violation: Violation) -> Plan:
    left = operand_range(tokens, violation, "left")
    right = operand_range(tokens, violation, "right")
    if left is None or right is None:
        return Plan(reason="operand boundary not resolvable")
    for index in range(left.start, right.end):
        if tokens[index].kind == "angle" and index != violation.operator_index:
            if index in generic:
                return Plan(reason="operand contains generic arguments")
            return Plan(reason="operand contains a shift or another comparison")
    start = tokens[left.start].start
    end = tokens[right.end - 1].end
    span = text[start:end]
    if "\n" in span:
        return Plan(reason="comparison spans more than one line")
    if "//" in span or "/*" in span:
        return Plan(reason="comparison contains a comment")
    if not (is_literal_constant(tokens, left) or is_literal_constant(tokens, right)):
        return Plan(reason="operand independence requires a literal constant")
    left_text = text[tokens[left.start].start : tokens[left.end - 1].end]
    right_text = text[tokens[right.start].start : tokens[right.end - 1].end]
    flipped = "<" if violation.operator == ">" else "<="
    return Plan(start, end, f"{right_text} {flipped} {left_text}")


def overlapping_plans(plans: list[Plan]) -> set[int]:
    rejected: set[int] = set()
    furthest_end = -1
    furthest_index = -1
    for index, plan in sorted(
        ((index, plan) for index, plan in enumerate(plans) if plan.replacement is not None),
        key=lambda pair: (pair[1].start, pair[1].end),
    ):
        if plan.start < furthest_end:
            rejected.update((index, furthest_index))
        if furthest_end < plan.end:
            furthest_end = plan.end
            furthest_index = index
    return rejected


def display_path(file: Path) -> str:
    try:
        return os.path.relpath(file, ROOT).replace(os.sep, "/")
    except ValueError:
        return file.as_posix()


def collect_cs_files(root: Path) -> list[Path]:
    files: list[Path] = []
    if not root.exists():
        return files
    pending = [root]
    while pending:
        directory = pending.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                if entry.is_dir(follow_symlinks=False):
                    if entry.name not in ("bin", "obj"):
                        pending.append(Path(entry.path))
                elif entry.name.endswith(".cs") and entry.is_file(follow_symlinks=False):
                    file = Path(entry.path)
                    relative = display_path(file)
                    if not relative.startswith("Runtime/Utils/SevenZip/"):
                        files.append(file)
    return sorted(files)


def main(argv: list[str] | None = None) -> int:
    arguments = sys.argv[1:] if argv is None else argv
    fix = "--fix" in arguments
    verbose = "--verbose" in arguments or "-VerboseOutput" in arguments
    configured = os.environ.get("COMPARISON_DIRECTION_ROOTS")
    roots = (
        [part for part in configured.split(os.pathsep) if part]
        if configured
        else list(DEFAULT_ROOTS)
    )
    explicit_files = [argument for argument in arguments if argument.endswith(".cs")]
    files = (
        [(ROOT / file).absolute() for file in explicit_files]
        if explicit_files
        else [file for root in roots for file in collect_cs_files(ROOT / root)]
    )
    remaining = 0
    fixed = 0
    manual: list[str] = []
    for file in files:
        with file.open(encoding="utf-8", newline="") as stream:
            text = stream.read()
        tokens = tokenize(text)
        generic = mark_generics(tokens)
        violations = find_violations(tokens, generic)
        if not violations:
            continue
        relative = display_path(file)
        edits: list[Plan] = []
        lines = text.split("\n")
        plans = [
            plan_fix(text, tokens, generic, violation) if fix else Plan(reason="not fixing")
            for violation in violations
        ]
        overlaps = overlapping_plans(plans) if fix else set[int]()
        operator_positions = (
            sorted(violation.operator_start for violation in violations) if fix else []
        )
        for index, (violation, plan) in enumerate(zip(violations, plans, strict=True)):
            if index in overlaps or (
                plan.replacement is not None
                and bisect_left(operator_positions, plan.end)
                - bisect_left(operator_positions, plan.start)
                > 1
            ):
                plan = Plan(reason="overlapping rewrite plans")
            if fix and plan.replacement is not None:
                edits.append(plan)
                fixed += 1
                continue
            remaining += 1
            line = lines[violation.line - 1] if violation.line <= len(lines) else ""
            entry = f"{relative}:{violation.line}:{violation.column}: '{violation.operator}' should read left-to-right -- {line.strip(JS_WHITESPACE)}"
            manual.append(f"{entry}\n    ({plan.reason})" if fix else entry)
        if edits:
            for edit in sorted(edits, key=lambda item: item.start, reverse=True):
                text = text[: edit.start] + (edit.replacement or "") + text[edit.end :]
            with file.open("w", encoding="utf-8", newline="") as stream:
                stream.write(text)
    if fix:
        print(f"[comparison-direction] rewrote {fixed} comparison(s) across {len(files)} file(s).")
    if remaining:
        print(
            f"[comparison-direction] {remaining} comparison(s) do not read left-to-right. Use only '<' and '<=': swap the operands, not the meaning (issue #554).",
            file=sys.stderr,
        )
        for entry in manual:
            print(f"  {entry}", file=sys.stderr)
        return 1
    if not files:
        print(
            f"[comparison-direction] no C# files were found under {', '.join(roots)}, so this run checked nothing.",
            file=sys.stderr,
        )
        return 1
    if verbose:
        print(f"[comparison-direction] {len(files)} file(s) clean.")
    return 0


if __name__ == "__main__":
    configure_output()
    try:
        sys.exit(main())
    except (OSError, UnicodeError, RecursionError) as error:
        print(f"[comparison-direction] {error}", file=sys.stderr)
        sys.exit(1)
