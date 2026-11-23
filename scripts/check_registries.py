"""Select and run the repository's lint and contract checks through typed Python."""

from __future__ import annotations

import fnmatch
import json
import os
import re
import sys
from dataclasses import replace
from pathlib import Path
from typing import Literal, cast

import check_runner
from check_runner import Check, mapping, read_checks, run_registry
from cli_encoding import configure_output

Registry = Literal["lint", "contracts"]
ROOT = Path(__file__).resolve().parents[1]
NPM_COMMAND = re.compile(r"npm run ([\w:.-]+)", re.ASCII)

# Run the two longest independent checks on separate hosted runners.
LINT_CHECK_SHARD = {
    "unity-mode-coordinator": 0,
    "bundled-assemblies": 1,
    "unsafe-code": 1,
    "unsafe-code-test": 1,
}

# Rounded median job timings from four full hosted runs. New or short checks get six seconds.
CONTRACT_CHECK_SECONDS = {
    "unity-framework": 160,
    "lint-pwsh-invocations": 135,
    "git-staging-helpers": 115,
    "license-year-copy-detection": 100,
    "runner-maintenance": 95,
    "precommit-integration": 85,
    "unity-test-filter": 80,
    "unity-frozen-player-provenance": 80,
    "version-sync": 50,
    "shell-portability": 50,
    "wproto-evidence": 45,
    "analyzer-placement": 45,
    "unity-workflow-matrix-contract": 40,
    "configure-git-defaults": 40,
    "il2cpp-conversion-inputs": 40,
    "native-fault-capture": 35,
    "python-compatibility": 35,
    "validate-hook-permissions": 35,
    "python-policy": 30,
    "csharp-syntax": 30,
    "agent-skill-migration": 30,
    "random-quality-stream": 25,
    "sync-script-contracts": 25,
    "native-build-readback": 25,
    "unity-grouped-modes": 25,
    "devcontainer-persistence": 25,
    "validate-lint-error-codes": 25,
    "unity-acceptance-runner": 20,
    "create-unitypackage": 20,
    "project-workspace": 20,
    "npm-package-changelog": 20,
}


def contract_shard(checks: list[Check], index: int, total: int) -> list[Check]:
    """Give the longest regression its own runner and balance the remaining work."""
    if index == 0:
        return [check for check in checks if check.id == "agent-preflight"]
    work = [check for check in checks if check.id != "agent-preflight"]
    ranked = sorted(work, key=lambda check: -CONTRACT_CHECK_SECONDS.get(check.id, 6))
    loads = [0] * (total - 1)
    selected: list[Check] = []
    for check in ranked:
        lightest = loads.index(min(loads))
        loads[lightest] += CONTRACT_CHECK_SECONDS.get(check.id, 6)
        if lightest == index - 1:
            selected.append(check)
    return selected


def load_checks(name: str, root: Path = ROOT) -> list[Check]:
    if name not in {"lint", "contracts", "hooks"}:
        raise ValueError(f"Unknown check registry: {name}")
    source = root / "scripts" / f"{name}-checks.json"
    checks = read_checks(cast("object", json.loads(source.read_text(encoding="utf-8"))))
    identifiers = [check.id for check in checks]
    if not checks or len(set(identifiers)) != len(identifiers):
        raise ValueError(f"Check registry must be nonempty with unique ids: {name}")
    if any(
        not check.id.strip() or not check.name.strip() or not check.run.strip() for check in checks
    ):
        raise ValueError(f"Check registry has a blank id, name, or command: {name}")
    return checks


def checks_for(
    registry: Registry,
    include_hooks: bool = False,
    direct: bool = False,
    root: Path = ROOT,
) -> list[Check]:
    checks = load_checks(registry, root)
    if include_hooks:
        if registry != "contracts":
            raise ValueError("Hook regressions belong to the contract registry")
        checks = [*load_checks("hooks", root), *checks]
    if len({check.id for check in checks}) != len(checks):
        raise ValueError("Selected check registries contain duplicate ids")
    affected_checks(checks, [])
    if not direct:
        return checks
    scripts = mapping(
        mapping(
            cast(
                "object",
                json.loads((root / "package.json").read_text(encoding="utf-8")),
            )
        )["scripts"]
    )
    resolved: list[Check] = []
    for check in checks:
        match = NPM_COMMAND.fullmatch(check.run)
        if match is None:
            resolved.append(check)
            continue
        command = scripts.get(match[1])
        if not isinstance(command, str) or not command.strip():
            raise ValueError(f"No package script resolves {check.id}: {check.run}")
        resolved.append(replace(check, run=command))
    return resolved


def affected_checks(
    checks: list[Check], files: list[str], *, direct_inputs_only: bool = False
) -> list[Check]:
    """Share pattern and dependency rules; focused iteration omits groups and fallback."""
    known = {check.id: check for check in checks}
    visited: set[str] = set()
    active: set[str] = set()

    def visit(identifier: str) -> None:
        if identifier not in known or identifier in active:
            raise ValueError(f"Invalid check dependency: {identifier}")
        if identifier in visited:
            return
        active.add(identifier)
        for dependency in known[identifier].dependencies:
            visit(dependency)
        active.remove(identifier)
        visited.add(identifier)

    for check in checks:
        visit(check.id)
    normalized = [file.replace("\\", "/").removeprefix("./") for file in files]
    if any(
        not file or file.startswith(("/", "-")) or ".." in file.split("/") for file in normalized
    ):
        raise ValueError("Expected repository-relative affected paths")
    groups: dict[str, list[str]] = {}
    if any(pattern.startswith("@") for check in checks for pattern in check.inputs):
        group_data = mapping(
            cast(
                "object",
                json.loads((ROOT / "scripts/check-inputs.json").read_text(encoding="utf-8")),
            )
        )
        groups = {name: check_runner.strings(value) for name, value in group_data.items()}
        if any(
            not values or any(not value or value.startswith("@") for value in values)
            for values in groups.values()
        ):
            raise ValueError("Input groups require nonempty literal patterns")

    def expanded(check: Check) -> tuple[str, ...]:
        if direct_inputs_only and check.inputs == ("*",):
            return ()
        result: list[str] = []
        for pattern in check.inputs:
            if pattern.startswith("@"):
                if pattern[1:] not in groups:
                    raise ValueError(f"Unknown check input group: {pattern}")
                if not direct_inputs_only:
                    result.extend(groups[pattern[1:]])
            else:
                result.append(pattern)
        return tuple(result)

    inputs = {check.id: expanded(check) for check in checks}
    if not files:
        return []
    patterns = [
        pattern for check in checks if check.inputs != ("*",) for pattern in inputs[check.id]
    ]
    if not direct_inputs_only and any(
        not any(fnmatch.fnmatchcase(file, pattern) for pattern in patterns)
        and not (
            re.search(r"\.(?:md|png|jpe?g|svg)(?:\.meta)?$", file)
            and (file.startswith("docs/") or "/" not in file)
        )
        for file in normalized
    ):
        return checks
    selected = {
        check.id
        for check in checks
        if any(
            fnmatch.fnmatchcase(file, pattern)
            for file in normalized
            for pattern in inputs[check.id]
        )
    }
    pending = list(selected)
    while pending:
        for dependency in known[pending.pop()].dependencies:
            if dependency not in selected:
                selected.add(dependency)
                pending.append(dependency)
    return [check for check in checks if check.id in selected]


def main(argv: list[str] | None = None) -> int:
    configure_output()
    previous_output = check_runner.OUTPUT_FORMAT
    check_runner.OUTPUT_FORMAT = "text"
    arguments = list(sys.argv[1:] if argv is None else argv)
    try:
        if not arguments or arguments[0] not in {"lint", "contracts"}:
            raise ValueError("Expected lint or contracts registry")
        registry = cast("Registry", arguments.pop(0))
        label = "Contract" if registry == "contracts" else "Lint"
        if "--focused-ids" in arguments:
            if arguments[:2] != ["--focused-ids", "--files"] or len(arguments) < 3:
                raise ValueError("--focused-ids requires --files and affected paths")
            checks = affected_checks(checks_for(registry), arguments[2:], direct_inputs_only=True)
            print(json.dumps([check.id for check in checks]))
            return 0
        if "--ci" in arguments:
            arguments.remove("--ci")
            if "--base" in arguments or "--files" in arguments:
                raise ValueError("--ci cannot be combined with explicit source selection")
            if os.environ.get("GITHUB_EVENT_NAME") == "pull_request":
                base = os.environ.get("AGENT_CHECK_BASE", "")
                if not base.strip():
                    raise ValueError("Pull request CI requires AGENT_CHECK_BASE")
                arguments.extend(["--base", base])
        include_hooks = "--include-hook-regressions" in arguments
        direct = "--direct" in arguments
        shard: tuple[int, int] | None = None
        if "--shard" in arguments:
            position = arguments.index("--shard")
            value = arguments[position + 1] if position + 1 < len(arguments) else ""
            match = re.fullmatch(r"([0-9]{1,3})/([0-9]{1,3})", value)
            if match is None or arguments.count("--shard") != 1:
                raise ValueError(f"{label} --shard requires one INDEX/TOTAL")
            index, total = int(match[1]), int(match[2])
            if not 0 <= index < total or total < 2 or "--only" in arguments:
                raise ValueError(
                    f"{label} --shard requires 0 <= INDEX < TOTAL, TOTAL >= 2 and no --only"
                )
            if registry == "lint" and total != 3:
                raise ValueError("Lint --shard requires TOTAL = 3")
            if check_runner.parse_arguments(arguments)["jobsInvalid"] is not None:
                raise ValueError("::error::--jobs must be a positive integer")
            shard = (index, total)
            del arguments[position : position + 2]
        checks = checks_for(registry, include_hooks, direct)
        if "--base" in arguments or "--files" in arguments:
            requested = check_runner.parse_arguments(arguments)
            unknown = [
                identifier
                for identifier in check_runner.strings(requested["only"])
                if identifier not in {check.id for check in checks}
            ]
            if unknown:
                raise ValueError(f"::error::Unknown check id(s): {', '.join(unknown)}")
            if requested["jobsInvalid"] is not None:
                raise ValueError("::error::--jobs must be a positive integer")
        if "--base" in arguments:
            if "--files" in arguments:
                raise ValueError("Choose --base or --files, not both")
            index = arguments.index("--base")
            if index + 1 >= len(arguments) or arguments[index + 1].startswith("--"):
                raise ValueError("--base requires a revision")
            base = arguments[index + 1]
            del arguments[index : index + 2]
            from agent_check import changed_files

            selected_files = changed_files(base)
            checks = affected_checks(checks, selected_files)
            if not checks:
                print("Not applicable: no affected checks selected; no validation performed.")
                return 0
        if "--files" in arguments:
            index = arguments.index("--files")
            paths = arguments[index + 1 :]
            if not paths:
                raise ValueError("--files requires affected paths")
            arguments = arguments[:index]
            checks = affected_checks(checks, paths)
            if not checks:
                print("Not applicable: no affected checks selected; no validation performed.")
                return 0
        options = [
            argument
            for argument in arguments
            if argument not in {"--include-hook-regressions", "--direct"}
        ]
        command = f"uv run --project scripts/python~ --locked --no-dev python scripts/check_registries.py {registry}"
        if include_hooks:
            command += " --include-hook-regressions"
        if direct:
            command += " --direct"
        if shard is not None:
            if registry == "contracts":
                checks = contract_shard(checks, *shard)
            else:
                checks = [
                    check for check in checks if LINT_CHECK_SHARD.get(check.id, 2) == shard[0]
                ]
            if not checks:
                if "--list" not in options:
                    print(f"{label} shard {shard[0]}/{shard[1]} has no selected checks.")
                return 0
        return run_registry(
            checks,
            "Repo Lint" if registry == "lint" else "Contract Tests",
            command,
            options,
        )
    except (OSError, ValueError, KeyError, re.error) as error:
        print(f"Check registry failed: {error}", file=sys.stderr)
        return 1
    finally:
        check_runner.OUTPUT_FORMAT = previous_output


if __name__ == "__main__":
    sys.exit(main())
