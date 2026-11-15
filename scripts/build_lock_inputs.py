"""Check build-lock action input contracts at the exact commits workflows use."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import TYPE_CHECKING, cast

import yaml
from cli_encoding import configure_output
from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode

if TYPE_CHECKING:
    from collections.abc import Iterator

ROOT = Path(__file__).resolve().parents[1]
ACTION_PREFIX = "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/"
MAX_ACTION_BYTES = 8 * 1024 * 1024
KEY = re.compile(r"^([A-Za-z0-9_][A-Za-z0-9_-]*):(?:\s|$)")


@dataclass(frozen=True)
class Call:
    action: str
    ref: str
    file: str
    line: int
    supplied: list[str]


@dataclass(frozen=True)
class Result:
    call_count: int
    file_count: int
    version_count: int
    failures: list[str]


def is_blank_or_comment(line: str) -> bool:
    trimmed = line.strip()
    return not trimmed or trimmed.startswith("#")


def key_column(line: str) -> int:
    indent = len(line) - len(line.lstrip())
    return indent + 2 if re.match(r"-\s", line.strip()) else indent


def collect_with_keys(lines: list[str], uses_index: int, step_column: int) -> list[str]:
    with_column = -1
    key_column_in_with = -1
    start = uses_index
    supplied: list[str] = []
    for index in range(uses_index, -1, -1):
        line = lines[index]
        if is_blank_or_comment(line):
            continue
        indent = key_column(line)
        if step_column < indent:
            continue
        if indent < step_column:
            break
        start = index
        if re.match(r"-\s", line.strip()):
            break
    for index in range(start, len(lines)):
        line = lines[index]
        if is_blank_or_comment(line):
            continue
        indent = key_column(line)
        if with_column == -1:
            if indent < step_column:
                return supplied
            if start < index and indent == step_column and re.match(r"-\s", line.strip()):
                return supplied
            if indent == step_column and re.fullmatch(r"with:\s*", line.strip()):
                with_column = indent
            continue
        if indent <= with_column:
            return supplied
        if key_column_in_with == -1:
            key_column_in_with = indent
        if indent != key_column_in_with:
            continue
        match = KEY.match(line.strip())
        if match:
            supplied.append(match[1])
    return supplied


def split_lines(content: str) -> list[str]:
    return re.split(r"\r?\n", content)


def yaml_mappings(content: str, label: str) -> Iterator[dict[str, Node]]:
    try:
        document = yaml.compose(content, Loader=yaml.SafeLoader)
    except (yaml.YAMLError, RecursionError) as error:
        raise ValueError(f"{label}: YAML could not be parsed: {error}") from error
    pending = [document] if document is not None else []
    visited: set[int] = set()
    while pending:
        node = pending.pop()
        if id(node) in visited:
            continue
        visited.add(id(node))
        if isinstance(node, MappingNode):
            fields: dict[str, Node] = {}
            for key, value in node.value:
                if not isinstance(key, ScalarNode) or key.value in fields or key.value == "<<":
                    raise ValueError(f"{label}: YAML requires unique scalar keys without merges")
                fields[key.value] = value
                pending.append(value)
            yield fields
        elif isinstance(node, SequenceNode):
            pending.extend(reversed(node.value))


def collect_calls(file: Path, relative: str) -> list[Call]:
    calls: list[Call] = []
    for fields in yaml_mappings(file.read_text(encoding="utf-8"), relative):
        uses = fields.get("uses")
        if uses is None:
            continue
        if not isinstance(uses, ScalarNode):
            raise ValueError(f"{relative}: action reference could not be parsed")
        reference = uses.value
        if ACTION_PREFIX.lower() not in reference.lower():
            continue
        line = uses.start_mark.line + 1
        if not reference.startswith(ACTION_PREFIX):
            raise ValueError(
                f"{relative}:{line}: build-lock action reference could not be parsed: {reference}"
            )
        parts = reference[len(ACTION_PREFIX) :].split("@")
        if len(parts) != 2 or not parts[0] or not parts[1]:
            raise ValueError(
                f"{relative}:{line}: build-lock action reference is not pinned to a commit: {reference}"
            )
        supplied = fields.get("with")
        if supplied is not None and not isinstance(supplied, MappingNode):
            raise ValueError(f"{relative}:{line}: action inputs must be a mapping")
        calls.append(
            Call(
                parts[0],
                parts[1],
                relative,
                line,
                [key.value for key, _ in supplied.value if isinstance(key, ScalarNode)]
                if isinstance(supplied, MappingNode)
                else [],
            )
        )
    return calls


def parse_action_inputs(content: str, label: str) -> dict[str, bool]:
    lines = split_lines(content)
    inputs: dict[str, bool] = {}
    defaulted: set[str] = set()
    inputs_index = next(
        (index for index, line in enumerate(lines) if re.fullmatch(r"inputs:\s*", line)), -1
    )
    if inputs_index == -1:
        return inputs
    name_column = -1
    current = ""
    attribute_column = -1
    for index in range(inputs_index + 1, len(lines)):
        line = lines[index]
        if is_blank_or_comment(line):
            continue
        indent = len(line) - len(line.lstrip())
        if indent == 0:
            break
        if name_column == -1:
            name_column = indent
        if indent == name_column:
            match = KEY.match(line.strip())
            if not match:
                raise ValueError(f"{label}: unparsable input declaration on line {index + 1}")
            current = match[1]
            attribute_column = -1
            inputs[current] = False
            continue
        if not current:
            continue
        if attribute_column == -1:
            attribute_column = indent
        if indent != attribute_column:
            continue
        if line.strip().startswith("default:"):
            defaulted.add(current)
            continue
        required = re.fullmatch(r"required:\s*(\S+)\s*", line.strip())
        if required:
            inputs[current] = re.sub(r"^[\"']|[\"']$", "", required[1]) == "true"
    for name in defaulted:
        inputs[name] = False
    return inputs


def git_environment() -> dict[str, str]:
    return {
        **{
            name: value for name, value in os.environ.items() if not name.upper().startswith("GIT_")
        },
        "GIT_TERMINAL_PROMPT": "0",
        "GIT_NO_REPLACE_OBJECTS": "1",
    }


def read_action_at_ref(policy_root: Path, action: str, ref: str) -> str:
    return read_policy_object(policy_root, f"{ref}:.github/actions/{action}/action.yml")


def read_policy_object(policy_root: Path, target: str) -> str:
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required to read the central policy checkout.")
    with tempfile.TemporaryFile() as output:
        try:
            result = subprocess.run(  # noqa: S603 - read-only Git object lookup, without a shell.
                [git, "-C", str(policy_root), "show", "--end-of-options", target],
                stdout=output,
                stderr=subprocess.STDOUT,
                timeout=30,
                check=False,
                env=git_environment(),
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise ValueError(
                f"Could not read {target} from the central policy checkout at {policy_root}. The checkout needs full history (fetch-depth: 0) because different actions can be pinned to different commits. Underlying error: {error}"
            ) from error
        size = output.tell()
        if size > MAX_ACTION_BYTES:
            raise ValueError(
                f"{target}: action definition exceeds the {MAX_ACTION_BYTES}-byte limit."
            )
        output.seek(0)
        content = output.read().decode("utf-8", errors="replace")
    if result.returncode:
        raise ValueError(
            f"Could not read {target} from the central policy checkout at {policy_root}. The checkout needs full history (fetch-depth: 0) because different actions can be pinned to different commits. Underlying error: {content.strip()}"
        )
    return content


def collect_yaml_files(directory: Path) -> list[Path]:
    found: list[Path] = []
    with os.scandir(directory) as entries:
        for entry in entries:
            if entry.is_dir(follow_symlinks=False):
                found.extend(collect_yaml_files(Path(entry.path)))
            elif entry.name.endswith((".yml", ".yaml")):
                found.append(Path(entry.path))
    return sorted(found)


def approved_pins(policy_root: Path, ref: str) -> dict[str, set[str]]:
    document: object = json.loads(
        read_policy_object(policy_root, f"{ref}:unity-enrollment-policy.json")
    )
    if not isinstance(document, dict):
        raise ValueError("Central approval policy must be an object.")
    policy = cast("dict[str, object]", document)
    approved: dict[str, set[str]] = {}
    for name in ("approvedLockShas", "approvedReturnShas"):
        values = policy.get(name)
        if (
            not isinstance(values, list)
            or not values
            or any(
                not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{40}", value) is None
                for value in cast("list[object]", values)
            )
        ):
            raise ValueError(f"Central approval policy {name} must contain full commit SHAs.")
        approved[name] = set(cast("list[str]", values))
    return approved


def validate_build_lock_action_inputs(
    root: Path, policy_root: Path, approval_ref: str | None = None
) -> Result:
    calls: list[Call] = []
    files: set[str] = set()
    for file in collect_yaml_files(root / ".github"):
        relative = file.relative_to(root).as_posix()
        found = collect_calls(file, relative)
        if found:
            files.add(relative)
            calls.extend(found)
    declarations: dict[str, dict[str, bool]] = {}
    failures: list[str] = []
    approvals = approved_pins(policy_root, approval_ref) if approval_ref else None
    for call in calls:
        if approvals is not None:
            approval = (
                "approvedReturnShas"
                if call.action == "return-unity-license"
                else "approvedLockShas"
            )
            if call.ref not in approvals[approval]:
                failures.append(
                    f"{call.file}:{call.line}: {call.action}@{call.ref} is not approved by {approval_ref}:{approval}."
                )
                continue
        key = f"{call.action}@{call.ref}"
        if key not in declarations:
            declarations[key] = parse_action_inputs(
                read_action_at_ref(policy_root, call.action, call.ref), key
            )
        declared = declarations[key]
        supplied = dict.fromkeys(call.supplied)
        for name, required in declared.items():
            if required and name not in supplied:
                failures.append(
                    f"{call.file}:{call.line}: {call.action}@{call.ref[:8]} requires input '{name}', which this call does not pass."
                )
        failures.extend(
            f"{call.file}:{call.line}: {call.action}@{call.ref[:8]} declares no input '{name}', which this call passes."
            for name in supplied
            if name not in declared
        )
    return Result(len(calls), len(files), len(declarations), failures)


def resolve_build_lock_pin(action_name: str, root: Path = ROOT) -> str:
    github = root / ".github"
    if not github.exists():
        raise ValueError(f"No .github directory under {root}")
    usages: list[tuple[str, str]] = []
    for file in collect_yaml_files(github):
        usages.extend(
            (call.ref, call.file)
            for call in collect_calls(file, file.relative_to(root).as_posix())
            if call.action == action_name
        )
    if not usages:
        raise ValueError(
            f"No workflow or composite action references the build-lock action '{action_name}'"
        )
    unpinned = [
        (reference, file)
        for reference, file in usages
        if not re.fullmatch(r"[0-9a-f]{40}", reference)
    ]
    if unpinned:
        detail = ", ".join(f"{file} -> @{reference}" for reference, file in unpinned)
        raise ValueError(
            f"Build-lock action '{action_name}' must be pinned to a full 40-character commit SHA, never a tag or branch. Offending: {detail}"
        )
    distinct = dict.fromkeys(reference for reference, _ in usages)
    if len(distinct) != 1:
        detail = ", ".join(f"{file} -> @{reference}" for reference, file in usages)
        raise ValueError(
            f"Build-lock action '{action_name}' is pinned to {len(distinct)} different commits. Usages: {detail}"
        )
    return next(iter(distinct))


def main(argv: list[str] | None = None) -> int:
    arguments = sys.argv[1:] if argv is None else argv
    if arguments and arguments[0] == "--resolve":
        if len(arguments) not in (2, 4) or (len(arguments) == 4 and arguments[2] != "--root"):
            print(
                "usage: python scripts/build_lock_inputs.py --resolve <build-lock-action-name> [--root path]",
                file=sys.stderr,
            )
            return 2
        try:
            root = Path(arguments[3]).resolve() if len(arguments) == 4 else ROOT
            print(resolve_build_lock_pin(arguments[1], root))
            return 0
        except (OSError, ValueError) as error:
            print(str(error), file=sys.stderr)
            return 1
    if arguments and arguments != ["--approved"]:
        print("usage: python scripts/build_lock_inputs.py [--approved]", file=sys.stderr)
        return 2
    configured = os.environ.get("BUILD_LOCK_POLICY_ROOT", "")
    if not configured:
        if os.environ.get("GITHUB_ACTIONS") == "true" or arguments == ["--approved"]:
            print(
                "BUILD_LOCK_POLICY_ROOT is unset under GitHub Actions. The central policy checkout step must run before this gate; skipping here would drop the input contract silently.",
                file=sys.stderr,
            )
            return 1
        print(
            "[validate-build-lock-action-inputs] SKIPPED: set BUILD_LOCK_POLICY_ROOT to a full-history checkout of ambiguous-organization-build-lock to run the pinned input contract."
        )
        return 0
    try:
        result = validate_build_lock_action_inputs(
            ROOT,
            Path(configured).resolve(),
            "refs/remotes/origin/main" if arguments == ["--approved"] else None,
        )
    except (OSError, ValueError) as error:
        print(f"[validate-build-lock-action-inputs] {error}", file=sys.stderr)
        return 1
    if result.call_count == 0:
        print(
            "[validate-build-lock-action-inputs] Found 0 pinned build-lock action calls under .github/. This repository has many, so the scan is broken rather than the calls being absent.",
            file=sys.stderr,
        )
        return 1
    for failure in result.failures:
        print(f"::error::{failure}", file=sys.stderr)
    if result.failures:
        print(
            f"[validate-build-lock-action-inputs] {len(result.failures)} input contract violation(s). A pinned action's `with:` block must satisfy the inputs that action declares at its pin.",
            file=sys.stderr,
        )
        return 1
    print(
        f"[validate-build-lock-action-inputs] OK: {result.call_count} call(s) across {result.file_count} file(s) match {result.version_count} pinned action version(s)."
    )
    return 0


if __name__ == "__main__":
    configure_output()
    sys.exit(main())
