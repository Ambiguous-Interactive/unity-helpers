"""Select task validation without starting native Unity work."""

from __future__ import annotations

import contextlib
import io
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path
from typing import TypedDict, cast

from check_registries import affected_checks, checks_for
from check_runner import mapping, strings
from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]


class Reason(TypedDict):
    file: str
    coverage: str
    reason: str


class Selection(TypedDict):
    tier: str
    status: str
    files: list[str]
    unity: str
    reasons: list[Reason]
    commands: list[list[str]]
    nativeExecution: str
    omitted: list[str]


def changed_files(base: str, cwd: Path = ROOT) -> list[str]:
    if not base or base.startswith("-"):
        raise ValueError("An explicit base revision is required")
    git = shutil.which("git")
    if git is None:
        raise ValueError("Git is required")

    def query(arguments: list[str]) -> str:
        try:
            return subprocess.run(  # noqa: S603 - read-only Git argument vectors.
                [git, *arguments],
                cwd=cwd,
                check=True,
                capture_output=True,
                encoding="utf-8",
                timeout=60,
            ).stdout
        except subprocess.CalledProcessError as error:
            raise ValueError(f"Affected inventory failed: {error.stderr.strip()}") from error
        except subprocess.TimeoutExpired as error:
            raise ValueError("Affected inventory timed out") from error

    revision = query(["rev-parse", "--verify", f"{base}^{{commit}}"]).strip()
    inventories = [
        query(["diff", "--name-only", "--no-renames", "-z", f"{revision}...HEAD", "--"]),
        query(["diff", "--name-only", "--no-renames", "-z", "HEAD", "--"]),
        query(["ls-files", "--others", "--exclude-standard", "-z"]),
    ]
    return sorted(
        {file for inventory in inventories for file in inventory.split("\0") if file},
        key=lambda file: file.encode("utf-16-be", errors="surrogatepass"),
    )


def select(files: list[str], tier: str, root: Path = ROOT) -> Selection:
    if tier not in {"iterate", "pr", "full"}:
        raise ValueError("Unknown validation tier")
    normalized = [file.replace("\\", "/").removeprefix("./") for file in files]
    if any(
        not file
        or file.startswith("/")
        or any(part in {"..", "."} for part in file.split("/"))
        or re.match(r"^[A-Za-z]:", file)
        or "\0" in file
        for file in normalized
    ):
        raise ValueError("Expected repository-relative paths")
    files = sorted(
        set(normalized), key=lambda file: file.encode("utf-16-be", errors="surrogatepass")
    )
    unity = "full" if tier == "full" else "none"
    reasons: list[Reason] = []
    for file in files:
        docs = (
            bool(re.search(r"\.(md|png|jpg|jpeg|svg)(\.meta)?$", file))
            if file.startswith(("docs/", ".llm/", ".agents/"))
            else bool(re.fullmatch(r"(README|CHANGELOG|CONTRIBUTING|AGENTS)\.md", file))
        )
        code = bool(re.match(r"^(Runtime|Editor|Tests)/.*\.cs$", file))
        risk = bool(
            re.search(
                r"serial|thread|pool|buffer|singleton|lifecycle|odin|integrations",
                file,
                re.I,
            )
        )
        coverage = "none" if docs else "latest" if code and not risk else "full"
        if coverage == "full" or (coverage == "latest" and unity == "none"):
            unity = coverage
        reasons.append(
            {
                "file": file,
                "coverage": coverage,
                "reason": {
                    "none": "documentation or instructions",
                    "latest": "ordinary C#",
                    "full": "compatibility-sensitive or unknown input",
                }[coverage],
            }
        )

    def npm(name: str) -> list[str]:
        return ["npm", "run", name]

    def tooling(file: str) -> bool:
        return file.startswith(
            (
                "scripts/",
                ".github/",
                ".githooks/",
                ".devcontainer/",
                ".llm/",
                ".agents/",
            )
        ) or file in {"package.json", "cspell.json"}

    compiler = tier == "full" or any(
        file.startswith(("Runtime/", "Editor/", "Tests/", "Generator~/", "Samples~/", "Styles/"))
        or re.match(r"^scripts/(typecheck|verify-shipped-analyzers)", file)
        or (not tooling(file) and not re.search(r"\.(md|png|jpg|svg)(\.meta)?$", file))
        for file in files
    )
    commands: list[list[str]] = []
    if tier == "full":
        commands = [
            npm(name)
            for name in (
                "lint:repo",
                "validate:tests",
                "typecheck:unity",
                "typecheck:controls",
            )
        ]
    elif files:
        preflight = [
            "pwsh",
            "-NoProfile",
            "-File",
            "scripts/agent-preflight.ps1",
            "-Paths",
            *files,
        ]
        if tier == "pr":
            commands.append(preflight)
        if compiler:
            if tier == "pr":
                commands.append(npm("typecheck:unity"))
            else:
                scopes: dict[str, None] = {}
                for file in files:
                    if file.startswith("Tests/Editor/"):
                        scopes["editor-tests"] = None
                    elif file.startswith("Tests/Core/"):
                        scopes.update({"tests": None, "editor-tests": None})
                    elif file.startswith("Tests/Runtime/"):
                        scopes["tests"] = None
                    elif file.startswith("Editor/"):
                        scopes["editor"] = None
                    elif file.startswith("Runtime/Integrations/"):
                        scopes["integrations"] = None
                    elif file.startswith("Runtime/"):
                        scopes["runtime"] = None
                source_only = all(
                    re.match(r"^(Runtime|Editor|Tests)/.*\.cs(?:\.meta)?$", file) for file in files
                )
                if not scopes or (unity == "full" and not source_only):
                    commands.append(npm("typecheck:unity"))
                else:
                    fixtures = [file for file in files if file.endswith(".cs")]
                    fixture_only = (
                        bool(fixtures)
                        and all(
                            re.match(r"^Tests/(Runtime|Editor)/.*\.cs(?:\.meta)?$", file)
                            for file in files
                        )
                        and all((root / file).exists() for file in fixtures)
                    )
                    for scope in scopes:
                        command = ["npm", "run", "typecheck:iterate", "--", scope]
                        prefix = "Tests/Runtime/" if scope == "tests" else "Tests/Editor/"
                        scoped = (
                            [file for file in fixtures if file.startswith(prefix)]
                            if fixture_only
                            else []
                        )
                        if scoped:
                            command.extend(["--files", *scoped])
                        commands.append(command)
        if tier == "pr":
            commands.extend(
                ["npm", "run", name, "--", "--files", *files]
                for name in ("lint:repo", "validate:tests")
            )
        else:
            if any(file.endswith(".md") for file in files):
                commands.append(npm("lint:docs"))
            controls = affected_checks(
                checks_for("contracts", root=root), files, direct_inputs_only=True
            )
            if controls:
                commands.append(
                    [
                        "node",
                        "scripts/run-contract-tests.js",
                        "--only",
                        ",".join(check.id for check in controls),
                    ]
                )
        if not commands:
            commands.append(preflight)
    return {
        "tier": tier,
        "status": "selected" if commands else "not-applicable",
        "files": files,
        "unity": unity,
        "reasons": reasons,
        "commands": commands,
        "nativeExecution": "not performed by this runner; use the real Unity fixture runner or CI",
        "omitted": []
        if tier == "full"
        else [
            "unaffected checks",
            *(
                [
                    "publication preflight; run tier pr before publication",
                    "unselected contract suites; retain the task's focused behavior witness",
                ]
                if tier == "iterate"
                else []
            ),
            "full native compatibility matrix unless escalated",
        ],
    }


def execute_checks(
    commands: list[list[str]], cwd: Path = ROOT, *, capture_output: bool = False
) -> int:
    if any(not command or not command[0].strip() for command in commands):
        raise ValueError("Expected nonempty command argument vectors")
    for command in commands:
        started = time.monotonic()
        argv = command
        npm_path = os.environ.get("npm_execpath")  # noqa: SIM112 - npm defines this lowercase key.
        if command[0] == "npm" and npm_path:
            node = shutil.which("node")
            if node is None:
                raise ValueError("Node is required to run npm")
            argv = [node, npm_path, *command[1:]]
        elif executable := shutil.which(command[0]):
            argv = [executable, *command[1:]]
        if capture_output:
            result = subprocess.run(  # noqa: S603 - selected validation commands captured for the JSON bridge.
                argv, cwd=cwd, check=False, capture_output=True
            )
            sys.stdout.write(result.stdout.decode("utf-8", errors="replace"))
            sys.stderr.write(result.stderr.decode("utf-8", errors="replace"))
            sys.stderr.flush()
        else:
            result = subprocess.run(argv, cwd=cwd, check=False)  # noqa: S603 - selected validation commands inherit live settings.
        print(
            json.dumps(
                {
                    "command": command,
                    "status": result.returncode if result.returncode >= 0 else None,
                    "signal": -result.returncode if result.returncode < 0 else None,
                    "seconds": time.monotonic() - started,
                }
            ),
            flush=True,
        )
        if result.returncode:
            return result.returncode if result.returncode > 0 else 1
    return 0


def main(arguments: list[str], *, capture_output: bool = False) -> int:
    tier = "iterate"
    base = "origin/main"
    plan = False
    compact = False
    timings = False
    files: list[str] = []
    arguments = list(arguments)
    while arguments:
        argument = arguments.pop(0)
        if argument in {"--tier", "--base"}:
            if not arguments or arguments[0].startswith("--"):
                raise ValueError(f"Missing value for {argument}")
            value = arguments.pop(0)
            if argument == "--tier":
                tier = value
            else:
                base = value
        elif argument == "--plan":
            plan = True
        elif argument == "--json":
            compact = True
        elif argument == "--timings":
            timings = True
        elif argument == "--files":
            if not arguments or arguments[0].startswith("--"):
                raise ValueError("Missing paths for --files")
            while arguments and not arguments[0].startswith("--"):
                files.append(arguments.pop(0))
        else:
            raise ValueError(f"Unknown argument: {argument}")
    started = time.monotonic()
    inventory = files or changed_files(base)
    discovered = time.monotonic()
    selection = select(inventory, tier)
    selected = time.monotonic()
    print(json.dumps(selection, indent=None if compact else 2), flush=True)
    execute = not plan and selection["status"] != "not-applicable"
    status = execute_checks(selection["commands"], capture_output=capture_output) if execute else 0
    if timings:
        print(
            json.dumps(
                {
                    "tier": tier,
                    "discoverySeconds": discovered - started,
                    "selectionSeconds": selected - discovered,
                    "reportAndRunSeconds": time.monotonic() - selected,
                    "executed": execute,
                    "exitCode": status,
                }
            ),
            flush=True,
        )
    return status


def bridge() -> int:
    request = mapping(cast("object", json.load(sys.stdin)))
    operation = request.get("operation")
    cwd = request.get("cwd", str(ROOT))
    if not isinstance(cwd, str):
        raise ValueError("Expected cwd string")
    if operation == "select":
        tier = request.get("tier")
        if not isinstance(tier, str):
            raise ValueError("Expected tier string")
        value: object = select(strings(request.get("files")), tier)
    elif operation == "changedFiles":
        base = request.get("base")
        if not isinstance(base, str):
            raise ValueError("An explicit base revision is required")
        value = changed_files(base, Path(cwd))
    elif operation == "executeChecks":
        commands = request.get("commands")
        if not isinstance(commands, list):
            raise ValueError("Expected commands")
        return execute_checks(
            [strings(command) for command in cast("list[object]", commands)], Path(cwd)
        )
    elif operation == "main":
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            value = main(strings(request.get("args")), capture_output=True)
        print(json.dumps({"value": value, "output": output.getvalue()}))
        return 0
    else:
        raise ValueError("Unknown bridge operation")
    print(json.dumps({"value": value}))
    return 0


if __name__ == "__main__":
    configure_output()
    try:
        raise SystemExit(bridge() if sys.argv[1:] == ["--bridge"] else main(sys.argv[1:]))
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(error, file=sys.stderr)
        raise SystemExit(1) from error
