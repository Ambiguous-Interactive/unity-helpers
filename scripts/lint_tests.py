"""Check Unity test lifecycle, naming and iteration budgets, with conservative null repair."""

from __future__ import annotations

import argparse
import bisect
import json
import os
import re
import stat
import sys
import tempfile
from dataclasses import dataclass, field
from itertools import pairwise
from pathlib import Path
from typing import cast

from cli_encoding import configure_output, escape_command_data
from csharp_sources import EXCLUDED, is_link, source_files, source_tokens
from lint_comment_block_form import checked_path

HELPERS = (
    "Tests/Runtime/Visuals/VisualsTestHelpers.cs",
    "Tests/Core/TextureTestHelper.cs",
    "Tests/Editor/Sprites/SpriteSheetExtractor/SharedSpriteTestFixtures.cs",
    "Tests/Editor/TestAssets/SharedAnimationTestFixtures.cs",
    "Tests/Editor/TestAssets/SharedEditorTestFixtures.cs",
    "Tests/Editor/TestAssets/SharedTextureTestFixtures.cs",
)
ATTRIBUTE = r"\[\s*(?:global\s*::\s*)?(?:[A-Za-z_][\w.]*\.)?"
TEST_ATTRIBUTE = re.compile(
    ATTRIBUTE + r"(?:Test|TestCase|TestCaseSource|UnityTest)(?:Attribute)?(?:\s*\(|\s*\]|\s*,)"
)
TEST_FILE = re.compile(
    ATTRIBUTE
    + r"(?:Test|TestCase|TestCaseSource|TestFixture|UnityTest)(?:Attribute)?(?:\s*\(|\s*\]|\s*,)"
)
UNITY_TEST = re.compile(ATTRIBUTE + r"UnityTest(?:Attribute)?(?:\s*\(|\s*\])")
METHOD = re.compile(
    r"^\s*(?:\[[^\]]+\]\s*)*(?:(?:public|private|protected|internal|static|async|override|virtual|sealed|new|extern|unsafe|partial)\s+)+[\w.<>?,\s\[\]]+?\s+(?P<name>[A-Za-z_]\w*)\s*\("
)
NULL_CALL = re.compile(r"(?m)^(?P<indent>[ \t]*)Assert\.(?P<kind>IsNotNull|IsNull)\s*\(")
SOURCE_NAME = re.compile(
    r'\bTestCaseSource(?:Attribute)?\s*\(\s*(?:nameof\s*\(\s*(\w+)\s*\)|"([^"\r\n]+)")',
    re.DOTALL,
)
PERF = re.compile(
    r'\[\s*(?:NUnit\.Framework\.)?Category(?:Attribute)?\(\s*"(?:Performance|Stress)"\s*\)\s*\]'
)
CREATION = r"\bnew\s+(?:GameObject|Texture2D|Material|Mesh|Camera)\s*\("
BASE = r":\s*(?:CommonTestBase|AttributeTagsTestBase|TagsTestBase|EditorCommonTestBase|SpriteSheetExtractorTestBase|BatchedEditorTestBase|DetectAssetChangeTestBase|RelationalExactBindingTestBase)\b"


@dataclass(frozen=True)
class Finding:
    line: int
    code: str
    message: str
    advisory: bool = False


@dataclass(frozen=True)
class Source:
    text: str
    code: str = field(init=False)
    comments_masked: str = field(init=False)
    lines: list[str] = field(init=False)
    code_lines: list[str] = field(init=False)
    starts: list[int] = field(init=False)
    comments: tuple[tuple[int, int], ...] = field(init=False)
    comment_text: str = field(init=False)

    def __post_init__(self) -> None:
        lexical = self.text.replace("\r\n", " \n").translate(
            str.maketrans("\r\x85\u2028\u2029", "\n\n\n\n")
        )
        code = list(lexical)
        comments_masked = list(lexical)
        comments: list[tuple[int, int]] = []
        for start, end, kind in source_tokens(lexical):
            if kind == "directive":
                continue
            if kind != "literal":
                comments.append((start, end))
            for index in range(start, end):
                if lexical[index] != "\n":
                    code[index] = " "
                    if kind != "literal":
                        comments_masked[index] = " "
        scrubbed = "".join(code)
        object.__setattr__(self, "code", scrubbed)
        object.__setattr__(self, "comments_masked", "".join(comments_masked))
        object.__setattr__(self, "lines", re.split(r"\r\n|[\r\n\x85\u2028\u2029]", self.text))
        object.__setattr__(self, "code_lines", scrubbed.split("\n"))
        object.__setattr__(self, "starts", [0, *(m.end() for m in re.finditer("\n", lexical))])
        object.__setattr__(self, "comments", tuple(comments))
        object.__setattr__(
            self,
            "comment_text",
            "\n".join(lexical[start:end] for start, end in comments),
        )

    def line_at(self, offset: int) -> int:
        return bisect.bisect_right(self.starts, offset)

    def suppressed(self, line: int) -> bool:
        first = self.starts[line - 1]
        last = self.starts[line] if line < len(self.starts) else len(self.text)
        return any(
            "UNH-SUPPRESS" in self.text[max(first, start) : min(last, end)]
            for start, end in self.comments
            if start < last and first < end
        )

    def specific_suppression(self, start: int, end: int, code: str) -> bool:
        first, last = self.line_at(start), self.line_at(end)
        for low, high in self.comments:
            if self.text[low : low + 2] != "//":
                continue
            if first <= self.line_at(low) <= last and re.match(
                r"//\s*UNH-SUPPRESS\s+" + re.escape(code) + r"\b", self.text[low:high]
            ):
                return True
        return False


def closing(code: str, start: int, opening: str = "(", close: str = ")") -> int:
    depth = 0
    for index in range(start, len(code)):
        if code[index] == opening:
            depth += 1
        elif code[index] == close:
            depth -= 1
            if depth == 0:
                return index
    return -1


def generic_angle(code: str, index: int) -> bool:
    previous = code[:index].rstrip()
    if not previous or not re.search(r"[\w)>\]]$", previous):
        return False
    token = re.search(r"\w+$", previous)
    lower = token is not None and token[0][0].islower()
    if lower and len(previous) != index:
        return False
    end = closing(code, index, "<", ">")
    if end < 0 or (lower and "," in code[index + 1 : end]):
        return False
    following = code[end + 1 :].lstrip()
    return not following or following[0] in "()[]{}.,;?"


def ambiguous_angle(code: str) -> bool:
    for match in re.finditer(r"\b([a-z]\w*)<", code):
        start = match.end() - 1
        end = closing(code, start, "<", ">")
        if end < 0 or "," not in code[start + 1 : end]:
            continue
        following = code[end + 1 :].lstrip()
        if following and following[0] in "()[]{}.,;?":
            return True
    return False


def split_arguments(text: str) -> list[str]:
    code = Source(text).code
    ignored: set[int] = set()
    while True:
        depth = 0
        angles: list[int] = []
        boundaries = [0]
        for index, char in enumerate(code):
            if char in "([{":
                depth += 1
            elif char in ")]}":
                depth = max(0, depth - 1)
            elif not depth and char == "<" and index not in ignored and generic_angle(code, index):
                angles.append(index)
            elif not depth and char == ">" and angles:
                angles.pop()
            elif char == "," and not depth and not angles:
                boundaries.append(index + 1)
        if angles:
            ignored.update(angles)
            continue
        return [text[start : end - 1] for start, end in pairwise(boundaries)] + [
            text[boundaries[-1] :]
        ]


def repair_null_assertions(text: str) -> str:
    source = Source(text)
    replacements: list[tuple[int, int, str]] = []
    for match in NULL_CALL.finditer(source.code):
        end = closing(source.code, match.end() - 1)
        if end < 0:
            continue
        semicolon = end + 1
        while semicolon < len(text) and text[semicolon].isspace():
            semicolon += 1
        if semicolon == len(text) or text[semicolon] != ";":
            continue
        argument_text = text[match.end() : end]
        if ambiguous_angle(Source(argument_text).code):
            continue
        arguments = split_arguments(argument_text)
        expression = arguments[0].strip()
        expression_source = Source(expression)
        if any(
            expression[start : start + 2] == "//" and end == len(expression)
            for start, end in expression_source.comments
        ):
            continue
        if not expression or re.match(r"@?[A-Za-z_]\w*\s*:(?!:)", Source(expression).code):
            continue
        if re.search(r"\?|[&|^]|(?<![=!<>])=(?!=)", Source(expression).code):
            expression = f"({expression})"
        suffix = ",".join(arguments[1:]).strip()
        suffix = f", {suffix}" if suffix else ""
        operator = "!=" if match["kind"] == "IsNotNull" else "=="
        replacement = f"{match['indent']}Assert.IsTrue({expression} {operator} null{suffix});"
        replacements.append((match.start(), semicolon + 1, replacement))
    for start, end, replacement in reversed(replacements):
        text = text[:start] + replacement + text[end:]
    return text


def attribute_block(source: Source, line_index: int, prefix_end: int) -> tuple[str, str, int]:
    original = source.lines[line_index][:prefix_end]
    code = source.code_lines[line_index][:prefix_end]
    first = line_index
    index = line_index - 1
    while index >= 0:
        if not source.code_lines[index].strip():
            index -= 1
            continue
        top = index
        block = source.code_lines[top]
        while (
            block.count("[") != block.count("]") or block.count("(") != block.count(")")
        ) and top > 0:
            top -= 1
            block = source.code_lines[top] + "\n" + block
        if re.fullmatch(r"\s*(?:\[[\s\S]*?\]\s*)+", block) is None:
            break
        original = "\n".join(source.lines[top : index + 1]) + "\n" + original
        code = block + "\n" + code
        first = top
        index = top - 1
    return original, code, first


def coroutine_sources(source: Source) -> list[Finding]:
    findings: list[Finding] = []
    for line_index, line in enumerate(source.code_lines):
        method = re.search(r"\bIEnumerator\s+\w+\s*\(", line)
        if method is None or source.suppressed(line_index + 1):
            continue
        original, attributes, _ = attribute_block(source, line_index, method.start())
        if not UNITY_TEST.search(attributes):
            continue
        for source_name in SOURCE_NAME.finditer(original):
            name = source_name[1] or source_name[2]
            declaration = re.search(
                r"(?:IEnumerable\s*<\s*TestCaseData\s*>|IEnumerable|TestCaseData\s*\[\]|List\s*<\s*TestCaseData\s*>)\s+"
                + re.escape(name)
                + r"\b",
                source.code,
            )
            if declaration is None:
                continue
            start = declaration.end()
            brace = source.code.find("{", start)
            semicolon = source.code.find(";", start)
            if brace >= 0 and (semicolon < 0 or brace < semicolon):
                end = closing(source.code, brace, "{", "}")
            else:
                end = semicolon
            if end < 0:
                continue
            for chain in re.finditer(r"new\s+TestCaseData\s*\(", source.code[start:end]):
                chain_start = start + chain.start()
                depth = 0
                chain_end = end
                for position in range(chain_start, end):
                    char = source.code[position]
                    if char in "([{":
                        depth += 1
                    elif char in ")]}":
                        if not depth:
                            chain_end = position
                            break
                        depth -= 1
                    elif char in ";," and not depth:
                        chain_end = position
                        break
                if not re.search(r"\.Returns\s*\(\s*null\s*\)", source.code[chain_start:chain_end]):
                    findings.append(
                        Finding(
                            source.line_at(chain_start),
                            "UNH006",
                            f"TestCaseData used by [UnityTest] coroutine source '{name}' must call .Returns(null).",
                        )
                    )
    return findings


def json_mapping(path: Path) -> dict[str, object]:
    if is_link(path):
        raise ValueError(f"Assembly metadata is linked: {path}")
    try:
        value = cast("object", json.loads(path.read_text(encoding="utf-8-sig")))
        return cast("dict[str, object]", value) if isinstance(value, dict) else {}
    except (UnicodeError, json.JSONDecodeError):
        return {}


@dataclass
class AssemblyScope:
    root: Path
    directories: dict[Path, bool] = field(default_factory=dict[Path, bool])
    references: dict[str, bool] | None = None

    def definition(self, path: Path) -> bool:
        platforms = json_mapping(path).get("includePlatforms")
        if not isinstance(platforms, list):
            return False
        entries = cast("list[object]", platforms)
        return bool(entries) and all(item == "Editor" for item in entries)

    def reference(self, path: Path) -> bool:
        reference = json_mapping(path).get("reference")
        if not isinstance(reference, str) or not reference.strip():
            return False
        if self.references is None:
            references: dict[str, bool] = {}
            for folder, dirs, files in os.walk(self.root, followlinks=False):
                directory = Path(folder)
                dirs[:] = [
                    name
                    for name in dirs
                    if name not in EXCLUDED
                    and not name.endswith("~")
                    and not is_link(directory / name)
                ]
                for name in sorted(files):
                    if not name.endswith(".asmdef"):
                        continue
                    definition_path = directory / name
                    definition = json_mapping(definition_path)
                    editor = self.definition(definition_path)
                    assembly_name = definition.get("name")
                    if isinstance(assembly_name, str) and assembly_name.strip():
                        references[assembly_name.casefold()] = editor
                    meta = definition_path.with_name(name + ".meta")
                    if meta.is_file():
                        if is_link(meta):
                            raise ValueError(f"Assembly metadata is linked: {meta}")
                        match = re.search(
                            r"(?m)^guid:\s*(\S+)\s*$",
                            meta.read_text(encoding="utf-8-sig"),
                        )
                        if match:
                            references[("GUID:" + match[1]).casefold()] = editor
            self.references = references
        return self.references.get(reference.casefold(), False)

    def editor_only(self, path: Path) -> bool:
        walked: list[Path] = []
        current = path.parent
        while current.is_relative_to(self.root):
            if current in self.directories:
                result = self.directories[current]
                break
            walked.append(current)
            refs = sorted(current.glob("*.asmref"))
            definitions = sorted(current.glob("*.asmdef"))
            if refs:
                result = self.reference(refs[0])
                break
            if definitions:
                result = self.definition(definitions[0])
                break
            if current == self.root:
                return path.relative_to(self.root).as_posix().startswith("Tests/Editor/")
            current = current.parent
        else:
            raise ValueError(f"Source is outside the selected root: {path}")
        for directory in walked:
            self.directories[directory] = result
        return result


def condition_in_player(expression: str) -> bool | None:
    expression = re.sub(r"/\*.*?\*/|//.*", "", expression)
    tokens = re.findall(r"[A-Za-z_]\w*|&&|\|\||==|!=|[!()]", expression)
    if "".join(tokens) != re.sub(r"\s+", "", expression):
        return None
    position = 0
    precedence = {"||": 1, "&&": 2, "==": 3, "!=": 3}

    def parse(minimum: int = 1) -> bool | None:
        nonlocal position
        if position == len(tokens):
            raise ValueError("Incomplete preprocessor condition")
        atom = tokens[position]
        position += 1
        if atom == "!":
            operand = parse(4)
            value = None if operand is None else not operand
        elif atom == "(":
            value = parse()
            if position == len(tokens) or tokens[position] != ")":
                raise ValueError("Unbalanced preprocessor condition")
            position += 1
        elif atom in {"false", "UNITY_EDITOR"}:
            value = False
        elif atom == "true":
            value = True
        elif re.fullmatch(r"[A-Za-z_]\w*", atom):
            value = None
        else:
            raise ValueError("Unsupported preprocessor token")
        while position < len(tokens) and precedence.get(tokens[position], 0) >= minimum:
            operator = tokens[position]
            position += 1
            right = parse(precedence[operator] + 1)
            if operator == "&&":
                value = (
                    False
                    if value is False or right is False
                    else None
                    if value is None or right is None
                    else True
                )
            elif operator == "||":
                value = (
                    True
                    if value is True or right is True
                    else None
                    if value is None or right is None
                    else False
                )
            elif value is None or right is None:
                value = None
            else:
                value = (value == right) if operator == "==" else (value != right)
        return value

    try:
        result = parse()
        return result if position == len(tokens) else None
    except (ValueError, RecursionError):
        return None


def editor_references(source: Source) -> list[Finding]:
    stack: list[tuple[bool, bool]] = []
    findings: list[Finding] = []
    for line_number, line in enumerate(source.code_lines, 1):
        directive = re.match(r"\s*#\s*(if|elif)\s+(.+)", line)
        if directive:
            condition = condition_in_player(directive[2])
            frame = (condition is not False, condition is not True)
            if directive[1] == "elif" and stack:
                remaining = stack[-1][1]
                frame = (remaining and frame[0], remaining and frame[1])
                stack[-1] = frame
            else:
                stack.append(frame)
            continue
        if re.match(r"\s*#\s*else\b", line):
            if stack:
                stack[-1] = (stack[-1][1], False)
            continue
        if re.match(r"\s*#\s*endif\b", line):
            if stack:
                stack.pop()
            continue
        if any(not active for active, _ in stack) or source.suppressed(line_number):
            continue
        if re.search(r"\bUnityEditor|WallstopStudios\.UnityHelpers\.Editor\b", line):
            findings.append(
                Finding(
                    line_number,
                    "UNH011",
                    "editor-only reference in player-compiled test code must be inside #if UNITY_EDITOR",
                )
            )
    return findings


def analyze(text: str, relative: str, *, editor_only: bool) -> list[Finding]:
    source = Source(text)
    findings: list[Finding] = []

    def add(offset: int, code: str, message: str, *, advisory: bool = False) -> None:
        line = source.line_at(offset)
        if not source.suppressed(line):
            findings.append(Finding(line, code, message, advisory))

    for destroy in re.finditer(
        r"\b(?:UnityEngine\.)?Object\.(?:DestroyImmediate|Destroy)\s*\(", source.code
    ):
        end = closing(source.code, destroy.end() - 1)
        argument = source.comments_masked[destroy.end() : end].strip() if end >= 0 else ""
        allowed = False
        if argument:
            line = source.line_at(destroy.start())
            start = source.starts[max(0, line - 100)]
            for track in re.finditer(r"\bTrack\s*\(", source.code[start : destroy.start()]):
                track_start = start + track.start()
                track_end = closing(source.code[: destroy.start()], start + track.end() - 1)
                if track_end < 0:
                    continue
                tracked = source.comments_masked[start + track.end() : track_end].strip()
                assignment = re.search(
                    r"(?<![\w.@])(@?[A-Za-z_]\w*)\s*=\s*$",
                    source.code[start:track_start],
                )
                expressions = [tracked, *([assignment[1]] if assignment else [])]
                for expression in expressions:
                    if (
                        expression
                        and argument.startswith(expression)
                        and (
                            not argument[len(expression) :].strip()
                            or argument[len(expression) :].lstrip().startswith(",")
                        )
                    ):
                        allowed = True
                        break
                if allowed:
                    break
        if not allowed:
            add(
                destroy.start(),
                "UNH001",
                "Avoid direct destroy in tests; track object and let teardown clean up (or add // UNH-SUPPRESS)",
            )

    assignments = list(
        re.finditer(
            r"(?P<var>\b\w+)\s*=\s*new\s+(?:GameObject|Texture2D|Material|Mesh|Camera)\s*\(",
            source.code,
        )
    )
    scriptable = list(
        re.finditer(r"(?P<var>\b\w+)\s*=\s*ScriptableObject\.CreateInstance\s*<", source.code)
    )
    for allocation in [*assignments, *scriptable]:
        line = source.line_at(allocation.start())
        suppression_end = line + (2 if allocation in scriptable else 0)
        if any(
            source.suppressed(index)
            for index in range(line, min(suppression_end, len(source.lines)) + 1)
        ):
            continue
        following = "\n".join(source.code_lines[line - 1 : line + 10])
        if not re.search(r"Track\s*\(\s*" + re.escape(allocation["var"]) + r"\b", following):
            add(
                allocation.start(),
                "UNH002",
                f"Unity object allocation should be tracked: add Track({allocation['var']})",
            )
    creation = re.search(CREATION, source.code)
    if creation and not re.search(
        r"\bTrack\s*\(\s*new\s+(?:GameObject|Texture2D|Material|Mesh|Camera)\s*\(",
        source.code,
    ):
        add(
            creation.start(),
            "UNH002",
            "Inline Unity object creation should be passed to Track(new ...)",
        )
    if (
        (creation or scriptable)
        and not re.search(BASE, source.code)
        and not re.search(
            r"UNH-SUPPRESS.*UNH003|UNH-SUPPRESS:\s*Complex|UNH-SUPPRESS:\s*This IS the CommonTestBase",
            source.comment_text,
        )
        and re.search(r"\bnamespace\s+WallstopStudios", source.code)
    ):
        findings.append(
            Finding(
                1,
                "UNH003",
                "Test classes creating Unity objects should inherit CommonTestBase (Editor or Runtime variant)",
            )
        )

    for pattern, message in (
        (
            r'TestName\s*=\s*@?"[^"\r\n]*_[^"\r\n]*"',
            "TestName contains underscore. Use PascalCase or dot notation",
        ),
        (
            r'\.SetName\s*\(\s*@?"[^"\r\n]*_[^"\r\n]*"\s*\)',
            "SetName() contains underscore. Use PascalCase or dot notation",
        ),
    ):
        for match in re.finditer(pattern, source.comments_masked):
            if source.code[match.start()].isspace():
                continue
            add(match.start(), "UNH004", message)
    for match in SOURCE_NAME.finditer(source.comments_masked):
        if source.code[match.start()].isspace():
            continue
        name = match[1] or match[2]
        if "_" in name:
            add(
                match.start(),
                "UNH004",
                f"TestCaseSource method '{name}' contains underscore. Use PascalCase",
            )
    for index, method_line in enumerate(source.code_lines):
        method = METHOD.match(method_line)
        if method is None or "_" not in method["name"]:
            continue
        _, attributes, first = attribute_block(source, index, method.end())
        if TEST_ATTRIBUTE.search(attributes) and not any(
            source.suppressed(number) for number in range(first + 1, index + 2)
        ):
            findings.append(
                Finding(
                    index + 1,
                    "UNH004",
                    f"Test method name '{method['name']}' contains underscore. Use PascalCase.",
                )
            )
    findings.extend(coroutine_sources(source))
    for match in re.finditer(r"Assert\.Is(?:Not)?Null\s*\(", source.code):
        add(
            match.start(),
            "UNH005",
            "Use Assert.IsTrue(expr == null) or Assert.IsTrue(expr != null) for Unity object null checks",
        )

    perf = PERF.search(source.comments_masked) is not None
    test = TEST_FILE.search(source.code) is not None
    tags = relative.startswith("Tests/Runtime/Tags/")
    infra = relative.startswith("Tests/Core/") or relative.endswith("TestBase.cs")
    if not perf:
        for match in re.finditer(
            r"\bfor\s*\([^;\n]*;[^;\n]*<\s*=?\s*([0-9][0-9_]{2,})", source.code
        ):
            digits = match[1].replace("_", "")
            if len(digits) <= 19 and 50_000 <= int(digits) <= 2**63 - 1:
                add(
                    match.start(),
                    "UNH007",
                    f"loop of {int(digits)} iterations in a non-perf test; tag the fixture Performance/Stress, reduce the count, or add // UNH-SUPPRESS",
                )
    if (
        test
        and (
            "/Performance/" in "/" + relative
            or re.search(r"\bclass\s+\w*(?:Performance|Benchmark)\w*Tests\b", source.code)
        )
        and not perf
        and not re.search(r"UNH-SUPPRESS.*UNH008", source.comment_text)
    ):
        findings.append(
            Finding(
                1,
                "UNH008",
                'Performance/benchmark fixture must declare [Category("Performance")] or [Category("Stress")]',
            )
        )
    if not infra and not re.search(
        r":\s*(?:BatchedEditorTestBase|SpriteSheetExtractorTestBase|DetectAssetChangeTestBase)\b",
        source.code,
    ):
        for match in re.finditer(
            r"AssetDatabase\.Refresh\s*\(|\.SaveAndReimport\s*\(", source.code
        ):
            add(
                match.start(),
                "UNH009",
                "per-test AssetDatabase.Refresh()/SaveAndReimport() churns imports; prefer BatchedEditorTestBase (advisory)",
                advisory=True,
            )
    if test and not perf and not infra:
        wait = re.compile(
            r"new\s+WaitForSeconds(?:Realtime)?\s*\(\s*[0-9][0-9.]*f?\s*\)|\bThread\.Sleep\s*\(\s*[0-9]+\s*\)|\bTask\.Delay\s*\(\s*[0-9]+\s*\)"
        )
        for match in wait.finditer(source.code):
            if not tags or "WaitForSeconds" not in match[0]:
                add(
                    match.start(),
                    "UNH010",
                    f"real-time wait '{match[0].strip()}' blocks the serial test clock; prefer frame-stepping/deterministic completion/injectable clock (advisory)",
                    advisory=True,
                )
    if not editor_only and not re.search(r"UNH-SUPPRESS.*UNH011", source.comment_text):
        findings.extend(editor_references(source))
    for match in re.finditer(
        r"(?:yield\s+return\b[^;\r\n]*\bWaitForEndOfFrame\b)|(?:\bnew\s+WaitForEndOfFrame\s*\()",
        source.code,
    ):
        add(
            match.start(),
            "UNH012",
            "[UnityTest] must not 'yield return' WaitForEndOfFrame under batchmode; use 'yield return null'",
        )
    if tags and not perf:
        aliases = [
            re.escape(m[1])
            for m in re.finditer(
                r"(?m)^\s*(?:global\s+)?using\s+(\w+)\s*=\s*(?:global\s*::\s*)?UnityEngine\s*(?:\.|::)\s*WaitForSeconds(?:Realtime)?\s*;",
                source.code,
            )
        ]
        aliases.extend(
            re.escape(m[1]) + r"\s*(?:\.|::)\s*WaitForSeconds(?:Realtime)?"
            for m in re.finditer(
                r"(?m)^\s*(?:global\s+)?using\s+(\w+)\s*=\s*(?:global\s*::\s*)?UnityEngine\s*;",
                source.code,
            )
        )
        patterns = [r"WaitForSeconds(?:Realtime)?"]
        if aliases:
            patterns.append(r"\bnew\s+(?:" + "|".join(aliases) + r")\s*\(")
        for pattern in patterns:
            findings.extend(
                Finding(
                    source.line_at(match.start()),
                    "UNH013",
                    "Tests/Runtime/Tags tests must not use WaitForSeconds/WaitForSecondsRealtime; prefer deterministic handler clock seams or add // UNH-SUPPRESS UNH013 with justification",
                )
                for match in re.finditer(pattern, source.code)
                if not source.specific_suppression(match.start(), match.end() - 1, "UNH013")
            )
    return findings


def selected_sources(root: Path, paths: list[str] | None) -> list[Path]:
    if paths is None:
        selected = source_files(root, ("Tests",))
    else:
        selected = [
            checked_path(
                root,
                name if os.path.lexists(root / name) else name.replace("\\", "/"),
                literal=True,
            )
            for name in paths
            if name.lower().endswith(".cs")
        ]
    allowed = {name.casefold() for name in HELPERS}
    result = sorted(
        {
            path
            for path in selected
            if path.relative_to(root).as_posix().casefold() not in allowed
            and not any(part.endswith("~") for part in path.relative_to(root).parts)
        }
    )
    if not selected:
        raise ValueError("No C# test sources selected; this scan would examine nothing")
    return result


def write_repair(path: Path, data: bytes) -> None:
    info = path.stat()
    if info.st_nlink != 1:
        raise ValueError(f"Refusing to rewrite a hard-linked source: {path}")
    temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=path.parent, prefix=".lint-tests-", delete=False
        ) as stream:
            temporary = Path(stream.name)
            stream.write(data)
        temporary.chmod(stat.S_IMODE(info.st_mode))
        temporary.replace(path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--paths", nargs="+")
    parser.add_argument("--fix-null-checks", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    root = Path(os.path.abspath(args.repo_root))  # noqa: PTH100
    try:
        if (root / "package.json").is_file():
            for helper in HELPERS:
                if not (root / helper).is_file():
                    raise ValueError(f"Allowlisted helper file not found: {helper}")
        paths = selected_sources(root, args.paths)
        scope = AssemblyScope(root)
        snapshots: list[tuple[Path, bytes, bytes, list[Finding]]] = []
        for path in paths:
            original = path.read_bytes()
            encoding = "utf-8-sig" if original.startswith(b"\xef\xbb\xbf") else "utf-8"
            text = original.decode(encoding)
            relative = path.relative_to(root).as_posix()
            if args.fix_null_checks:
                revised = repair_null_assertions(text)
                found = [
                    Finding(
                        Source(revised).line_at(m.start()),
                        "UNH005",
                        "Assert.IsNull/Assert.IsNotNull could not be auto-fixed; repair this call manually",
                    )
                    for m in NULL_CALL.finditer(Source(revised).code)
                ]
            else:
                revised = text
                found = analyze(text, relative, editor_only=scope.editor_only(path))
            revised_bytes = original if revised == text else revised.encode(encoding)
            snapshots.append((path, original, revised_bytes, found))
        if args.fix_null_checks:
            for path, original, revised_bytes, _ in snapshots:
                if path.read_bytes() != original:
                    raise ValueError(f"Source changed during repair: {path}")
                if original != revised_bytes and path.stat().st_nlink != 1:
                    raise ValueError(f"Refusing to rewrite a hard-linked source: {path}")
            for path, original, revised_bytes, _ in snapshots:
                if original != revised_bytes:
                    write_repair(path, revised_bytes)
        failures = 0
        for path, _, _, found in snapshots:
            relative = escape_command_data(path.relative_to(root).as_posix())
            for finding in found:
                print(f"{relative}:{finding.line}: {finding.code}: {finding.message}")
                failures += not finding.advisory
        print(f"[lint-tests] Checked {len(paths)} test source(s); {failures} error(s).")
        return int(bool(failures))
    except (OSError, ValueError, UnicodeError) as error:
        print(f"[lint-tests] {escape_command_data(str(error))}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
