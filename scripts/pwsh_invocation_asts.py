"""Apply PowerShell invocation rules to facts from PowerShell's native AST."""

from __future__ import annotations

import json
import re
import sys
from typing import TypedDict, cast

JOINED_ARGUMENT_TYPES = {"ArrayLiteralAst", "ArrayExpressionAst", "VariableExpressionAst"}
ALLOW_START_PROCESS = re.compile(
    r"#\s*lint-pwsh-invocations:\s*allow-start-process-argument-list\s+\S+", re.IGNORECASE
)
POSITIONAL_FALSE = re.compile(r"PositionalBinding\s*=\s*\$false", re.IGNORECASE)
COMMENT = re.compile(r"^\s*#")


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
    attributes: list[str]


class ParameterBlockFact(TypedDict):
    line: int
    text: str
    attributes: list[str]
    parameters: list[ParameterFact]


class ScriptFact(TypedDict):
    path: str
    error: SyntaxErrorFact | None
    commands: list[CommandFact]
    lines: list[str]
    block: ParameterBlockFact | None


class Violation(TypedDict):
    path: str
    line: int
    code: str
    message: str
    content: str


def allow_start_process(line_number: int, lines: list[str]) -> bool:
    if line_number <= len(lines) and ALLOW_START_PROCESS.search(lines[line_number - 1]):
        return True
    for index in range(line_number - 2, -1, -1):
        line = lines[index]
        if not COMMENT.match(line):
            break
        if ALLOW_START_PROCESS.search(line):
            return True
    return False


def start_process_violation(path: str, command: CommandFact, lines: list[str]) -> Violation | None:
    for argument in command["arguments"]:
        name = argument["name"]
        if not name.lower().startswith("arg") or argument["kind"] not in JOINED_ARGUMENT_TYPES:
            continue
        if allow_start_process(command["line"], lines):
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
        if any(
            "valuefromremainingarguments" in attribute.lower()
            for attribute in parameter["attributes"]
        ):
            catches_remaining = True
        elif parameter["array_type"]:
            arrays.append(parameter["name"])
    positional_disabled = any(
        POSITIONAL_FALSE.search(attribute) for attribute in block["attributes"]
    )
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
        for command in script["commands"]:
            finding = start_process_violation(path, command, script["lines"])
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
