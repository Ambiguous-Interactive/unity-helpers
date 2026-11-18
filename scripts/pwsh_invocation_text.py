"""Check PowerShell invocation text in scripts, workflows and npm commands."""

from __future__ import annotations

import re
from functools import lru_cache, partial
from typing import TYPE_CHECKING, Literal

if TYPE_CHECKING:
    from collections.abc import Iterator

    from pwsh_invocation_asts import Violation

# .NET and Python disagree on the C0 information separators in \s, \S and Trim.
WHITESPACE = (
    "\t\n\v\f\r \x85\xa0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a"
    "\u2028\u2029\u202f\u205f\u3000"
)
REGEX_WHITESPACE = re.escape(WHITESPACE)
ASCII_LOWER = str.maketrans("ABCDEFGHIJKLMNOPQRSTUVWXYZ", "abcdefghijklmnopqrstuvwxyz")
CaseMode = Literal["invariant", "unicode", "turkic"]
EXECUTABLE = r"pwsh(?:\.exe)?|powershell(?:\.exe)?"
FILE_SWITCH = r"-(?:File|f)\b"
DIRECT = r"""(?:^|[\s;&|"'\x60(])(""" + EXECUTABLE + rf")\b(?:\s+\S+)*?\s+{FILE_SWITCH}\s+"
DASH = r"""(?:--|\\*"--\\*"|'--')(?=\s|$|")"""
ARRAY = r'(\\*"\$\{[A-Z0-9_]*(?:PWSH|POWERSHELL)[A-Z0-9_]*\[@\]\}\\*")\s+'
HOOK_NAMES = r"pre-commit|pre-push|pre-merge-commit|post-rewrite"
HOOK = rf"\.githooks[/\\]+(?:{HOOK_NAMES})"
TARGET_END = r"""(?=$|[\s;&,"'\x60)])"""
HOOK_TARGET = (
    rf"""(?:\\*"(?:[^"]*[/\\]+)?{HOOK}\\*"|"""
    rf"""'(?:[^']*[/\\]+)?{HOOK}'|(?:\S*[/\\]+)?{HOOK}){TARGET_END}"""
)
SIBLING_PATH = r"(?:\$PSScriptRoot|scripts)[/\\]"
VARIABLE_ASSIGNMENT = r"(?:^|[;\s])(?:\[[^\]]+\]\s*)?\$(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*.*"
SEPARATOR_ERROR = (
    "PowerShell -File does not honor POSIX '--' and will fail with "
    "'parameter name '' is ambiguous'. Use explicit named params like -Paths instead."
)
HOOK_ADVICE = (
    "Invoke .githooks/<hook> directly through Git/shell, or use "
    ".githooks/<hook>.ps1 for PowerShell debugging."
)
ARRAY_INVOCATION = 'pwsh/powershell invocation via bash array indirection ("${NAME[@]}")'
CONTINUATION_DESCRIPTION = r" (multi-line with '\' continuation)"


def case_mode(culture: str) -> CaseMode:
    if not culture:
        return "invariant"
    if culture.split("-", 1)[0].lower() in {"tr", "az"}:
        return "turkic"
    return "unicode"


REGEX_PART = re.compile(r"\\.|\[(?:\\.|[^\]\\])*\]|\(\?P<[^>]+>|[iI\u0130\u0131sS]")
ASCII_RANGES = {
    "[A-Z0-9_]": "A-Za-z0-9_",
    "[A-Za-z_]": "A-Za-z_",
    "[A-Za-z0-9_]": "A-Za-z0-9_",
    "[A-Za-z]": "A-Za-z",
    "[A-Za-z0-9]": "A-Za-z0-9",
}


@lru_cache(maxsize=128)
def pattern(source: str, mode: CaseMode = "invariant", word_ranges: str = "") -> re.Pattern[str]:
    def adapt(match: re.Match[str]) -> str:
        part = match[0]
        if part in ASCII_RANGES:
            letters = ASCII_RANGES[part]
            variants = {"invariant": "", "unicode": "\u0130", "turkic": "\u0130\u0131"}[mode]
            if mode == "turkic" and part == "[A-Z0-9_]":
                letters = "A-Za-hj-z0-9_"
                variants = "\u0131"
            return f"(?-i:[{letters}\u212a{variants}])"
        if part.startswith("["):
            return part.replace(r"\s", REGEX_WHITESPACE)
        if part == r"\s":
            return f"[{REGEX_WHITESPACE}]"
        if part == r"\S":
            return f"[^{REGEX_WHITESPACE}]"
        if part == r"\w":
            return f"(?-i:[{word_ranges}])"
        if part == r"\b":
            word = rf"(?-i:[{word_ranges}\u200c\u200d])"
            return f"(?:(?<!{word})(?={word})|(?<={word})(?!{word}))"
        if part in {"s", "S"}:
            return "(?-i:[sS])"
        if part in {"i", "I", "\u0130", "\u0131"}:
            if mode == "turkic":
                return "(?-i:[i\u0130])" if part in {"i", "\u0130"} else "(?-i:[I\u0131])"
            if part == "\u0131" or (part == "\u0130" and mode == "invariant"):
                return f"(?-i:{part})"
            return "(?-i:[iI\u0130])" if mode == "unicode" else "(?-i:[iI])"
        return part

    return re.compile(REGEX_PART.sub(adapt, source), re.IGNORECASE)


class CulturePatterns:
    def __init__(self, mode: CaseMode, word_ranges: str) -> None:
        self.compile = partial(pattern, mode=mode, word_ranges=word_ranges)
        self.direct_separator = self.compile(
            DIRECT + r"""(?:"[^"]+"|'[^']+'|\S+)(?:\s+\S+)*?\s+""" + DASH
        )
        self.array_separator = self.compile(ARRAY + r"(?:\S+\s+)*?\S+\.ps1(?:\s+\S+)*?\s+" + DASH)
        self.array_hook = self.compile(ARRAY + r"(?:\S+\s+)*?" + HOOK_TARGET)
        self.direct_hook = self.compile(DIRECT + HOOK_TARGET)
        self.in_process = self.compile(
            r"""(&)\s+(?:\([^)]*\.ps1[^)]*\)|\$[A-Za-z_][A-Za-z0-9_]*(?:Path|Script|Ps1|Cmd|Tool)?|"""
            r"""["'][^"']*\.ps1["']|[^\s"']+\.ps1)(?:\s+\S+)*?\s+""" + DASH,
        )
        self.subprocess = self.compile(
            r"(?:^|[\s;&|\x60(])("
            + EXECUTABLE
            + rf")\b(?:\s+-[A-Za-z][A-Za-z0-9]*(?:\s+\S+)?)*?\s+{FILE_SWITCH}\s+"
            + rf"""(?:"(?:[^"]*?[/\\])?{SIBLING_PATH}[^"]+\.ps1"|"""
            + rf"""'(?:[^']*?[/\\])?{SIBLING_PATH}[^']+\.ps1'|"""
            + rf"(?:\S*[/\\])?{SIBLING_PATH}\S+\.ps1|\$\w+)",
        )
        self.hook_assignment = self.compile(VARIABLE_ASSIGNMENT + HOOK_TARGET)
        self.hook_join_path = self.compile(
            VARIABLE_ASSIGNMENT
            + r"""\bJoin-Path\b.*(?:["']\.githooks["']|["'][^"']*[/\\]\.githooks["']).*"""
            + rf"""["'](?:{HOOK_NAMES})["'](?=$|[\s;&,)])""",
        )
        self.file_option = self.compile(r"\s-(?:File|f)\b")
        self.command_option = self.compile(r"\s-(?:Command|c)(?=\s|$)")
        self.command_help = self.compile(
            r"""\s-(?:Command|c)\s+\\?["']?\s*(?:Write-(?:Host|Output|Warning|Error|Verbose|Information)|echo|printf)\b""",
        )
        self.shell_help = self.compile(r"^\s*(?:-\s+)?(?:run\s*:\s*)?(?:echo|printf)\b")
        self.allow_subprocess = self.compile(
            r"^\s*#\s*lint-pwsh-invocations:\s*allow-subprocess-pwsh\s+\S+"
        )
        self.top_level_script = self.compile(r"scripts/[^/]+\.ps1")


COMMENT = pattern(r"^\s*#")
CONTINUATION = pattern(r"\\\s*$")
FOLDED_RUN = pattern(r"^(\s*)(?:-\s+)?run\s*:\s*>[-+]?\s*(#.*)?$")
NONSPACE = pattern(r"\S")
SPACE_RUN = pattern(r"\s+")
TEST_SCRIPT = pattern(r"scripts/tests/.*\.ps1")
QUOTE = re.compile(r"""["']""")


def trim(text: str) -> str:
    return text.strip(WHITESPACE)


def quote_spans(line: str) -> Iterator[tuple[int, int]]:
    opening: re.Match[str] | None = None
    for quote in QUOTE.finditer(line):
        if opening is None:
            opening = quote
        elif opening[0] == quote[0]:
            yield opening.start() + 1, quote.start() + 1
            opening = None
    if opening is not None:
        yield opening.start() + 1, len(line) + 1


def in_quote(line: str, index: int) -> bool:
    return any(start <= index < stop for start, stop in quote_spans(line[:index]))


def remove_inline_comment(line: str) -> str:
    index = line.find("#")
    if index == -1:
        return line
    for start, stop in quote_spans(line):
        if index < start:
            break
        if index < stop:
            index = line.find("#", stop)
            if index == -1:
                return line
    return line[:index]


def mask_literals(line: str) -> str:
    chunks: list[str] = []
    previous = 0
    for start, stop in quote_spans(line):
        end = stop - 1 if stop <= len(line) else len(line)
        chunks.extend((line[previous:start], " " * (end - start)))
        previous = end
    chunks.append(line[previous:])
    return "".join(chunks)


def matches_invocation(
    line: str,
    rule: re.Pattern[str],
    patterns: CulturePatterns,
    *,
    powershell: bool,
    shell_help: bool,
) -> bool:
    for match in rule.finditer(line):
        start = match.start(1)
        inside = in_quote(line, start)
        if powershell:
            if not inside:
                return True
            continue

        option = patterns.file_option.search(line[start:])
        if option is not None:
            option_start = start + option.start()
            before_option = line[start:option_start]
            if (
                patterns.command_option.search(before_option)
                and in_quote(line, option_start)
                and patterns.command_help.search(before_option)
                and ";" not in before_option
            ):
                continue
        if inside and (
            patterns.command_option.search(line[:start])
            or (shell_help and patterns.shell_help.match(line))
        ):
            continue
        return True
    return False


def opens_here_string(line: str, *, marker_scan: bool) -> str:
    text = line if marker_scan else trim(line)
    for quote in ('"', "'"):
        opener = "@" + quote
        opening = opener in text if marker_scan else text.endswith(opener)
        if opening:
            return quote + "@"
    return ""


def exempt_lines(lines: list[str]) -> set[int]:
    excluded: set[int] = set()
    comment = False
    here_end = ""
    for index, line in enumerate(lines):
        # The two legacy maps track their state independently, even on excluded lines.
        if comment or "<#" in line:
            comment = "#>" not in line
            excluded.add(index)
        if here_end:
            if line.lstrip(WHITESPACE).startswith(here_end):
                here_end = ""
            excluded.add(index)
        else:
            here_end = opens_here_string(line, marker_scan=False)
            if here_end:
                excluded.add(index)
    return excluded


def allows_subprocess(lines: list[str], patterns: CulturePatterns) -> bool:
    comment = False
    here_end = ""
    for line in lines[:40]:
        if here_end:
            if line.lstrip(WHITESPACE).startswith(here_end):
                here_end = ""
            continue
        if comment:
            comment = "#>" not in line
            continue
        terminator = opens_here_string(line, marker_scan=True)
        if terminator:
            if terminator not in line:
                here_end = terminator
            continue
        if "<#" in line:
            comment = "#>" not in line
            continue
        if patterns.allow_subprocess.match(line):
            return True
    return False


def continuations(lines: list[str], skip_hash_comments: bool) -> Iterator[tuple[int, str]]:
    index = 0
    while index < len(lines):
        start = index
        merged = lines[index]
        if not (skip_hash_comments and COMMENT.match(merged)):
            while (end := CONTINUATION.search(merged)) and index + 1 < len(lines):
                following = lines[index + 1]
                if skip_hash_comments and COMMENT.match(following):
                    break
                merged = merged[: end.start()] + " " + following
                index += 1
            if start != index:
                yield start, merged
        index += 1


def folded_runs(lines: list[str]) -> Iterator[tuple[int, str]]:
    for start, line in enumerate(lines):
        key = FOLDED_RUN.match(line)
        if key is None:
            continue
        indent = len(key[1])
        body: list[str] = []
        for index in range(start + 1, len(lines)):
            following = lines[index]
            first_character = NONSPACE.search(following)
            if first_character is None:
                body.append("")
            elif first_character.start() <= indent:
                break
            elif not COMMENT.match(following):
                body.append(following[first_character.start() :])
        if body:
            yield start, SPACE_RUN.sub(" ", " ".join(body))


def scan_command(
    line: str,
    patterns: CulturePatterns,
    *,
    powershell: bool,
    shell_help: bool,
    context: Literal["line", "continuation", "folded"],
    variable_hook: re.Pattern[str] | None = None,
    test_script: bool = False,
    sibling_rule: bool = False,
) -> tuple[str, str] | None:
    continuation = CONTINUATION_DESCRIPTION if context == "continuation" else ""
    folded = " inside YAML block scalar" if context == "folded" else ""
    if matches_invocation(
        line, patterns.direct_separator, patterns, powershell=powershell, shell_help=shell_help
    ):
        return (
            "PWS001",
            f"pwsh/powershell -File invocation{continuation}{folded} passes '--' as a separator; "
            + SEPARATOR_ERROR,
        )
    if matches_invocation(
        line, patterns.array_separator, patterns, powershell=powershell, shell_help=shell_help
    ):
        return (
            "PWS001",
            f"{ARRAY_INVOCATION}{continuation}{folded} passes '--' as a separator; if the array "
            "expands to a pwsh -File command, " + SEPARATOR_ERROR,
        )
    if matches_invocation(
        line, patterns.array_hook, patterns, powershell=powershell, shell_help=shell_help
    ):
        return (
            "PWS004",
            f"{ARRAY_INVOCATION} targets an extensionless git hook{continuation}{folded}. "
            + HOOK_ADVICE,
        )
    if matches_invocation(
        line, patterns.direct_hook, patterns, powershell=powershell, shell_help=shell_help
    ):
        return (
            "PWS004",
            f"pwsh/powershell -File targets an extensionless git hook{continuation}{folded}. "
            + HOOK_ADVICE,
        )
    if variable_hook is not None and matches_invocation(
        line, variable_hook, patterns, powershell=True, shell_help=False
    ):
        return (
            "PWS004",
            "pwsh/powershell -File targets a variable assigned to an extensionless git hook. "
            + HOOK_ADVICE,
        )
    if test_script and matches_invocation(
        line, patterns.in_process, patterns, powershell=powershell, shell_help=False
    ):
        return (
            "PWS002",
            (
                f"Test invokes .ps1 via in-process '&' with '--'{continuation}; tests must exercise "
                "the same invocation path production uses ('pwsh -NoProfile -File ... -Paths ...'), "
                "otherwise CLI-binding bugs are masked."
            ),
        )
    if sibling_rule and patterns.subprocess.search(mask_literals(line)):
        return (
            "PWS003",
            (
                "scripts/*.ps1 invokes 'pwsh|powershell -NoProfile -File <sibling>.ps1' via subprocess"
                f"{continuation}. This fails on Windows PowerShell 5.1 hosts (no pwsh on PATH) and drops "
                "the parent session's state. Prefer dot-sourcing a shared helper module, or refactor the "
                "callee into an in-process function. If subprocess isolation is truly required, opt out "
                "with a top-of-file comment '# lint-pwsh-invocations: allow-subprocess-pwsh <rationale>'."
            ),
        )
    return None


def scan_source(path: str, lines: list[str], patterns: CulturePatterns) -> list[Violation]:
    suffix_path = path.lower()
    powershell = suffix_path.endswith(".ps1")
    yaml = suffix_path.endswith((".yml", ".yaml"))
    shell = suffix_path.endswith(".sh")
    skip_hash_comments = shell or yaml
    shell_help = skip_hash_comments or (suffix_path.startswith(".githooks/") and not powershell)
    excluded = exempt_lines(lines) if powershell else set[int]()
    if powershell or skip_hash_comments:
        excluded.update(index for index, line in enumerate(lines) if COMMENT.match(line))
    test_script = powershell and TEST_SCRIPT.fullmatch(path) is not None
    sibling_rule = powershell and patterns.top_level_script.fullmatch(path) is not None
    sibling_rule = sibling_rule and not allows_subprocess(lines, patterns)

    results: list[Violation] = []
    names: dict[str, str] = {}
    variable_hook: re.Pattern[str] | None = None
    for index, line in enumerate(lines):
        if index in excluded:
            continue
        candidate = remove_inline_comment(line) if powershell else line
        if powershell and "$" in candidate and "=" in candidate:
            previous = len(names)
            for assignment in (patterns.hook_assignment, patterns.hook_join_path):
                for match in assignment.finditer(candidate):
                    if not in_quote(candidate, match.start("name")):
                        name = match["name"]
                        names.setdefault(name.translate(ASCII_LOWER), name)
            if len(names) != previous:
                names_pattern = "|".join(re.escape(name) for name in names.values())
                variable_hook = patterns.compile(DIRECT + rf"\$(?:{names_pattern})" + TARGET_END)
        finding = scan_command(
            candidate,
            patterns,
            powershell=powershell,
            shell_help=shell_help,
            context="line",
            variable_hook=variable_hook,
            test_script=test_script,
            sibling_rule=sibling_rule,
        )
        if finding:
            results.append(diagnostic(path, index, line, finding))

    for index, merged in continuations(lines, skip_hash_comments):
        if index in excluded:
            continue
        finding = scan_command(
            merged,
            patterns,
            powershell=powershell,
            shell_help=shell_help,
            context="continuation",
            test_script=test_script,
            sibling_rule=sibling_rule,
        )
        if finding:
            results.append(diagnostic(path, index, merged, finding))

    if yaml:
        for index, folded in folded_runs(lines):
            finding = scan_command(
                folded, patterns, powershell=False, shell_help=True, context="folded"
            )
            if finding:
                results.append(diagnostic(path, index, folded, finding))
    return results


def diagnostic(path: str, index: int, line: str, finding: tuple[str, str]) -> Violation:
    code, message = finding
    return {
        "path": path,
        "line": index + 1,
        "code": code,
        "message": message,
        "content": trim(line),
    }
