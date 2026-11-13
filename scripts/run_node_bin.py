"""Run repository-local Node tools without a shell or consuming their standard input."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

from cli_encoding import configure_output

TOOLS = {
    "prettier": ("prettier", "prettier"),
    "cspell": ("cspell", "cspell"),
    "markdownlint": ("markdownlint-cli", "markdownlint"),
}
ROOT = Path(__file__).resolve().parents[1]


def main(arguments: list[str] | None = None, *, root: Path = ROOT) -> int:
    configure_output()
    arguments = sys.argv[1:] if arguments is None else arguments
    if not arguments or arguments[0] not in TOOLS:
        print(
            f"Usage: node scripts/run-node-bin.js <{', '.join(TOOLS)}> [args...]",
            file=sys.stderr,
        )
        return 2
    requested = arguments[0]
    package_name, bin_name = TOOLS[requested]
    manifest_path = root / "node_modules" / package_name / "package.json"
    try:
        if not manifest_path.exists():
            print(
                f"{requested} is not installed in this repository. "
                "Run `npm install` on the same host that runs git hooks.",
                file=sys.stderr,
            )
            return 127
        manifest = cast("object", json.loads(manifest_path.read_text(encoding="utf-8")))
    except (OSError, ValueError) as error:
        print(f"Failed to read {manifest_path}: {error}", file=sys.stderr)
        return 1
    binary: object = None
    if isinstance(manifest, dict):
        binary = cast("dict[str, object]", manifest).get("bin")
    if isinstance(binary, dict):
        binary = cast("dict[str, object]", binary).get(bin_name)
    if not isinstance(binary, str) or not binary:
        print(f"{package_name} does not declare a '{bin_name}' bin entry.", file=sys.stderr)
        return 1
    try:
        binary_path = (manifest_path.parent / binary).resolve()
        binary_exists = binary_path.is_file()
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Failed to resolve {requested} bin entry: {error}", file=sys.stderr)
        return 1
    if not binary_exists:
        print(
            f"{requested} bin entry does not exist at {binary_path}. Run `npm install`.",
            file=sys.stderr,
        )
        return 127
    if len(arguments) > 1 and arguments[1] == "--check-install":
        return 0
    node = os.environ.get("UH_NODE_EXECUTABLE") or shutil.which("node")
    if not node:
        print(f"Failed to launch {requested}: Node is not installed.", file=sys.stderr)
        return 1
    try:
        result = subprocess.run(  # noqa: S603 - fixed tool allowlist, shell-free argument forwarding.
            [node, str(binary_path), *arguments[1:]], cwd=root, check=False
        )
    except OSError as error:
        print(f"Failed to launch {requested}: {error}", file=sys.stderr)
        return 1
    return result.returncode if result.returncode >= 0 else 1


if __name__ == "__main__":
    sys.exit(main())
