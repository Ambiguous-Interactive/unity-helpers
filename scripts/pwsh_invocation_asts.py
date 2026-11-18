"""Apply PowerShell invocation rules to facts from PowerShell's native AST."""

from __future__ import annotations

import json
import sys
from typing import TypedDict, cast

JOINED_ARGUMENT_TYPES = {"ArrayLiteralAst", "ArrayExpressionAst", "VariableExpressionAst"}


class SyntaxErrorFact(TypedDict):
    line: int
    message: str
    text: str


class ArgumentFact(TypedDict):
    name: str
    kind: str


class CommandFact(TypedDict):
    line: int
    text: str
    arguments: list[ArgumentFact]


class ParameterFact(TypedDict):
    name: str
    array_type: bool
    catches_remaining: bool


class ParameterBlockFact(TypedDict):
    line: int
    text: str
    positional_disabled: bool
    parameters: list[ParameterFact]


class ScriptFact(TypedDict):
    path: str
    error: SyntaxErrorFact | None
    commands: list[CommandFact]
    comment_lines: list[int]
    marker_lines: list[int]
    block: ParameterBlockFact | None


class Violation(TypedDict):
    path: str
    line: int
    code: str
    message: str
    content: str


def allow_start_process(line: int, comment_lines: set[int], marker_lines: set[int]) -> bool:
    if line in marker_lines:
        return True
    for above in range(line - 1, 0, -1):
        if above not in comment_lines:
            break
        if above in marker_lines:
            return True
    return False


def start_process_violation(
    path: str, command: CommandFact, comment_lines: set[int], marker_lines: set[int]
) -> Violation | None:
    for argument in command["arguments"]:
        name = argument["name"]
        if not name.lower().startswith("arg") or argument["kind"] not in JOINED_ARGUMENT_TYPES:
            continue
        if allow_start_process(command["line"], comment_lines, marker_lines):
            return None
        return {
            "path": path,
            "line": command["line"],
            "code": "PWS006",
            "message": (
                f"Start-Process -{name} is handed an array or a variable, which it joins with spaces "
                "and does NOT quote, so any element containing a space is split into separate "
                "arguments -- and the failure is a non-zero exit with a usage banner, which reads "
                "exactly like the child running and failing. Use "
                "System.Diagnostics.ProcessStartInfo.ArgumentList, which quotes each element. The "
                "conversion is not always mechanical: Start-Process -Wait also waits on the child's "
                "descendants, which Process.WaitForExit() does not. If the arguments are genuinely "
                "fixed switches, opt out on this site with a comment '# lint-pwsh-invocations: "
                "allow-start-process-argument-list <rationale>'. See .llm/skills/bash-pwsh-invocation.md."
            ),
            "content": command["text"],
        }
    return None


def array_parameter_violation(path: str, block: ParameterBlockFact) -> Violation | None:
    arrays: list[str] = []
    catches_remaining = False
    for parameter in block["parameters"]:
        if parameter["catches_remaining"]:
            catches_remaining = True
        elif parameter["array_type"]:
            arrays.append(parameter["name"])
    positional_disabled = block["positional_disabled"]
    if not arrays or (catches_remaining and positional_disabled):
        return None
    missing: list[str] = []
    if not catches_remaining:
        missing.append("a [Parameter(ValueFromRemainingArguments = $true)] sibling")
    if not positional_disabled:
        missing.append("[CmdletBinding(PositionalBinding = $false)]")
    return {
        "path": path,
        "line": block["line"],
        "code": "PWS005",
        "message": (
            f"declares array parameter(s) {', '.join(arrays)} but is missing {' and '.join(missing)}, "
            f"so `pwsh -File {path} -{arrays[0]} a b` binds only 'a' and puts 'b' on whichever "
            "parameter is positionally next -- silently. BOTH are required: the sibling catches "
            "the remainder, PositionalBinding = $false is what sends it there. See "
            ".llm/skills/bash-pwsh-invocation.md."
        ),
        "content": block["text"],
    }


def violations(scripts: list[ScriptFact]) -> list[Violation]:
    report: list[Violation] = []
    for script in scripts:
        path = script["path"]
        error = script["error"]
        if error is not None:
            report.append(
                {
                    "path": path,
                    "line": error["line"],
                    "code": "PWS005",
                    "message": (
                        f"could not be parsed, so its parameter block was never checked: {error['message']}"
                    ),
                    "content": error["text"],
                }
            )
            continue
        comment_lines = set(script["comment_lines"])
        marker_lines = set(script["marker_lines"])
        for command in script["commands"]:
            finding = start_process_violation(path, command, comment_lines, marker_lines)
            if finding is not None:
                report.append(finding)
        block = script["block"]
        if block is not None:
            finding = array_parameter_violation(path, block)
            if finding is not None:
                report.append(finding)
    return report


def main() -> int:
    try:
        payload: object = json.load(sys.stdin.buffer)
        if not isinstance(payload, list):
            raise ValueError("Expected a list of PowerShell script facts.")
        report = violations(cast("list[ScriptFact]", payload))
        output = json.dumps(report, ensure_ascii=False, separators=(",", ":")) + "\n"
        sys.stdout.buffer.write(output.encode("utf-8", errors="backslashreplace"))
    except (ValueError, KeyError, TypeError, IndexError) as error:
        print(f"[pwsh-invocation-asts] Invalid AST input: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
