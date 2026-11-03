"""Select and run the repository's lint and contract checks through typed Python."""

from __future__ import annotations

import json
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


def main(argv: list[str] | None = None) -> int:
    configure_output()
    check_runner.OUTPUT_FORMAT = "text"
    arguments = list(sys.argv[1:] if argv is None else argv)
    try:
        if not arguments or arguments[0] not in {"lint", "contracts"}:
            raise ValueError("Expected lint or contracts registry")
        registry = cast("Registry", arguments.pop(0))
        include_hooks = "--include-hook-regressions" in arguments
        direct = "--direct" in arguments
        checks = checks_for(registry, include_hooks, direct)
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
        return run_registry(
            checks,
            "Repo Lint" if registry == "lint" else "Contract Tests",
            command,
            options,
        )
    except (OSError, ValueError, KeyError, re.error) as error:
        print(f"Check registry failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
