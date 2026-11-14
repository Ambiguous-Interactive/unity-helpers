"""Rebuild Unity source gates without stale compiler or analyzer outputs."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]


def clean_artifacts(root: Path) -> int:
    generator = root.resolve() / "Generator~"
    if generator.is_symlink() or generator.resolve() != generator.absolute():
        raise ValueError("Generator~ must not be a symbolic link.")
    if not generator.is_dir():
        raise FileNotFoundError(f"{generator} does not exist.")
    removed = 0
    for project in generator.iterdir():
        if project.is_symlink() or project.resolve() != project.absolute() or not project.is_dir():
            continue
        for name in ("obj", "bin"):
            target = project / name
            if target.is_symlink():
                target.unlink()
            elif target.resolve() != target.absolute():
                continue
            elif target.exists():
                if target.is_dir():
                    shutil.rmtree(target)
                else:
                    target.unlink()
            else:
                continue
            removed += 1
    return removed


def npm_command() -> list[str]:
    node = os.environ.get("UH_NODE_EXECUTABLE") or shutil.which("node")
    if node is None:
        raise FileNotFoundError("Node is not installed.")
    inherited = os.environ.get("npm_execpath")  # noqa: SIM112 - npm owns this lowercase variable.
    if inherited and Path(inherited).is_file():
        return [node, inherited]
    npm = shutil.which("npm")
    if npm is None:
        raise FileNotFoundError("npm is not installed.")
    executable = Path(npm).resolve()
    if os.name != "nt":
        return [node, str(executable)]
    cli = executable.parent / "node_modules/npm/bin/npm-cli.js"
    if not cli.is_file():
        raise FileNotFoundError(f"npm CLI does not exist at {cli}.")
    return [node, str(cli)]


def run(arguments: list[str], root: Path = ROOT) -> int:
    try:
        if not (root / "Generator~").is_dir():
            print(f"[typecheck-clean] {root / 'Generator~'} does not exist.", file=sys.stderr)
            return 2
        command = npm_command()
        removed = clean_artifacts(root)
        noun = "directory" if removed == 1 else "directories"
        print(
            f"[typecheck-clean] Removed {removed} build artifact {noun}; "
            "shared compilation is off for this run.",
            flush=True,
        )
        environment = {**os.environ, "UseSharedCompilation": "false"}
        result = subprocess.run(  # noqa: S603 - fixed npm executable and shell-free argument forwarding.
            [*command, "run", *(arguments or ["typecheck:unity"])],
            cwd=root,
            env=environment,
            check=False,
            timeout=10800,
        )
    except (OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(f"[typecheck-clean] {error}", file=sys.stderr)
        return 1
    return result.returncode if result.returncode >= 0 else 1


if __name__ == "__main__":
    configure_output()
    sys.exit(run(sys.argv[1:]))
