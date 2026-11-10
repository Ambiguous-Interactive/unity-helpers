"""Check Dependabot configuration structure, schedules and dependency groups."""

from __future__ import annotations

import argparse
import os
import subprocess
from dataclasses import dataclass
from pathlib import Path

import yaml
from cli_encoding import configure_output
from csharp_sources import is_link
from lint_yaml import escape_diagnostic
from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode

ROOT = Path(__file__).resolve().parents[1]
INTERVALS = frozenset(("daily", "weekly", "monthly", "quarterly", "semiannually", "yearly", "cron"))
MERGE_TAG = "tag:yaml.org,2002:merge"
STRING_TAG = "tag:yaml.org,2002:str"
SCALAR_TAGS = frozenset(
    "tag:yaml.org,2002:" + name
    for name in ("str", "int", "float", "bool", "null", "timestamp", "binary")
)


@dataclass(frozen=True)
class Diagnostic:
    code: str
    line: int
    message: str


def scalar(node: Node | None) -> str:
    return node.value if isinstance(node, ScalarNode) and node.tag == STRING_TAG else ""


def fields(node: MappingNode) -> dict[str, Node]:
    """Read mapping aliases and merges without expanding an object graph."""
    result: dict[str, Node] = {}
    pending = [node]
    visited: set[int] = set()
    while pending:
        current = pending.pop()
        if id(current) in visited:
            continue
        visited.add(id(current))
        inherited: list[MappingNode] = []
        for key, value in current.value:
            if key.tag == MERGE_TAG:
                if isinstance(value, MappingNode):
                    inherited.append(value)
                elif isinstance(value, SequenceNode):
                    inherited.extend(item for item in value.value if isinstance(item, MappingNode))
            else:
                result.setdefault(scalar(key), value)
        pending.extend(reversed(inherited))
    return result


def graph_error(root: Node) -> Diagnostic | None:
    """Reject cycles, duplicate keys and malformed merges before schema reads."""
    pending = [(root, False)]
    active: set[int] = set()
    visited: set[int] = set()
    while pending:
        node, leaving = pending.pop()
        identity = id(node)
        if leaving:
            active.remove(identity)
            visited.add(identity)
            continue
        if identity in active:
            return Diagnostic(
                "DEP008",
                node.start_mark.line + 1,
                "Cyclic YAML aliases are not supported",
            )
        if identity in visited:
            continue
        if (
            (isinstance(node, MappingNode) and node.tag != "tag:yaml.org,2002:map")
            or (isinstance(node, SequenceNode) and node.tag != "tag:yaml.org,2002:seq")
            or (isinstance(node, ScalarNode) and node.tag not in SCALAR_TAGS)
        ):
            return Diagnostic("DEP008", node.start_mark.line + 1, "Unsupported YAML tag")
        active.add(identity)
        pending.append((node, True))
        if isinstance(node, MappingNode):
            names: set[str] = set()
            for key, value in node.value:
                if (
                    not isinstance(key, ScalarNode)
                    or key.tag not in (STRING_TAG, MERGE_TAG)
                    or key.value in names
                ):
                    return Diagnostic(
                        "DEP008",
                        key.start_mark.line + 1,
                        "Mapping keys must be unique scalars",
                    )
                names.add(key.value)
                if key.tag == MERGE_TAG and not (
                    isinstance(value, MappingNode)
                    or (
                        isinstance(value, SequenceNode)
                        and all(isinstance(item, MappingNode) for item in value.value)
                    )
                ):
                    return Diagnostic(
                        "DEP008",
                        value.start_mark.line + 1,
                        "A YAML merge needs a mapping or mapping list",
                    )
                pending.append((value, False))
        elif isinstance(node, SequenceNode):
            pending.extend((value, False) for value in node.value)
    return None


def string_list(node: Node | None) -> bool:
    return (
        isinstance(node, SequenceNode)
        and bool(node.value)
        and all(bool(scalar(item).strip()) for item in node.value)
    )


def valid_schedule(node: Node | None) -> bool:
    if not isinstance(node, MappingNode):
        return False
    values = fields(node)
    interval = scalar(values.get("interval"))
    return interval in INTERVALS and (
        interval != "cron" or bool(scalar(values.get("cronjob")).strip())
    )


def validate(text: str) -> list[Diagnostic]:
    """Validate the supported structure without invoking YAML constructors."""
    try:
        loader = yaml.SafeLoader(text)
        try:
            root = loader.get_single_node()
        finally:
            loader.dispose()
    except yaml.YAMLError as error:
        mark = getattr(error, "problem_mark", None)
        line = int(getattr(mark, "line", 0)) + 1
        return [Diagnostic("DEP008", line, "Invalid YAML document")]
    if not isinstance(root, MappingNode):
        return [Diagnostic("DEP008", 1, "The configuration must be a mapping")]
    failure = graph_error(root)
    if failure:
        return [failure]
    problems: list[Diagnostic] = []
    top = fields(root)
    version = top.get("version")
    if (
        not isinstance(version, ScalarNode)
        or version.value != "2"
        or version.tag not in (STRING_TAG, "tag:yaml.org,2002:int")
    ):
        problems.append(Diagnostic("DEP001", 1, "A top-level version: 2 is required"))
    multi: dict[str, Node] = {}
    if "multi-ecosystem-groups" in top:
        declared = top["multi-ecosystem-groups"]
        if not isinstance(declared, MappingNode):
            problems.append(
                Diagnostic(
                    "DEP002",
                    declared.start_mark.line + 1,
                    "Multi-ecosystem groups must be a mapping",
                )
            )
        else:
            multi = fields(declared)
            problems.extend(
                Diagnostic(
                    "DEP005",
                    definition.start_mark.line + 1,
                    "A multi-ecosystem group needs a valid schedule",
                )
                for definition in multi.values()
                if not isinstance(definition, MappingNode)
                or not valid_schedule(fields(definition).get("schedule"))
            )
    updates = top.get("updates")
    if not isinstance(updates, SequenceNode):
        problems.append(Diagnostic("DEP007", 1, "An updates list is required"))
        return problems
    for entry in updates.value:
        line = entry.start_mark.line + 1
        if not isinstance(entry, MappingNode):
            problems.append(Diagnostic("DEP008", line, "Each updates entry must be a mapping"))
            continue
        values = fields(entry)
        if not scalar(values.get("package-ecosystem")).strip():
            problems.append(
                Diagnostic("DEP008", line, "An updates entry needs a package-ecosystem")
            )
        directory = scalar(values.get("directory"))
        directories = values.get("directories")
        if (not directory.startswith("/") and not string_list(directories)) or (
            "directory" in values and "directories" in values
        ):
            problems.append(
                Diagnostic(
                    "DEP008",
                    line,
                    "Specify directory or directories for each updates entry",
                )
            )
        elif isinstance(directories, SequenceNode) and any(
            not scalar(item).startswith("/") for item in directories.value
        ):
            problems.append(Diagnostic("DEP008", line, "Directories must start with /"))
        group_name = scalar(values.get("multi-ecosystem-group"))
        if "multi-ecosystem-group" in values:
            if not group_name or group_name not in multi:
                problems.append(
                    Diagnostic(
                        "DEP003",
                        line,
                        "The multi-ecosystem group must name a declared group",
                    )
                )
            if not string_list(values.get("patterns")):
                problems.append(
                    Diagnostic(
                        "DEP004",
                        line,
                        "A multi-ecosystem updates entry needs a nonempty patterns list",
                    )
                )
        elif "patterns" in values:
            problems.append(
                Diagnostic(
                    "DEP004",
                    line,
                    "Entry-level patterns require a multi-ecosystem group",
                )
            )
        group_definition = multi.get(group_name)
        inherited_schedule = isinstance(group_definition, MappingNode) and valid_schedule(
            fields(group_definition).get("schedule")
        )
        if ("schedule" in values and not valid_schedule(values["schedule"])) or (
            "schedule" not in values and not inherited_schedule
        ):
            problems.append(
                Diagnostic(
                    "DEP005",
                    line,
                    "An updates entry needs a valid or inherited schedule",
                )
            )
        groups = values.get("groups")
        if groups is not None:
            if not isinstance(groups, MappingNode):
                problems.append(
                    Diagnostic("DEP006", groups.start_mark.line + 1, "Groups must be a mapping")
                )
                continue
            for definition in fields(groups).values():
                if not isinstance(definition, MappingNode):
                    problems.append(
                        Diagnostic(
                            "DEP006",
                            definition.start_mark.line + 1,
                            "Each dependency group must be a mapping",
                        )
                    )
                    continue
                options = fields(definition)
                problems.extend(
                    Diagnostic(
                        "DEP006",
                        options[key].start_mark.line + 1,
                        "Group filters must be nonempty scalar lists",
                    )
                    for key in ("patterns", "exclude-patterns", "update-types")
                    if key in options and not string_list(options[key])
                )
    return problems


def main(argv: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--paths", type=Path, nargs="+")
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(argv)
    repository = args.repo_root.resolve()
    try:
        paths = args.paths or [repository / ".github/dependabot.yml"]
        inputs: list[tuple[Path, str]] = []
        for candidate in paths:
            selected = candidate if candidate.is_absolute() else repository / candidate
            path = Path(os.path.abspath(selected))  # noqa: PTH100 - inspect links before resolving them.
            if not path.is_file():
                raise ValueError(f"{path.name} not found or not a regular file: {path}")
            if is_link(path) or any(
                is_link(parent)
                for parent in path.parents
                if parent != repository and parent.is_relative_to(repository)
            ):
                raise ValueError(f"Configuration is linked: {path}")
            inputs.append((path, path.read_text(encoding="utf-8-sig")))
        count = 0
        for path, text in inputs:
            problems = validate(text)
            count += len(problems)
            for problem in problems:
                print(
                    f"{escape_diagnostic(str(path))}:{problem.line}: {problem.code}: {problem.message}"
                )
        if args.verbose or count:
            print(f"[lint-dependabot] Checked {len(inputs)} configuration(s); {count} error(s).")
        return int(bool(count))
    except (OSError, ValueError, RecursionError, subprocess.SubprocessError) as error:
        print(f"[lint-dependabot] ERROR: {escape_diagnostic(str(error))}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
