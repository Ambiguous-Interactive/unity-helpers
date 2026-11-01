"""Compatibility entry point for the locked WebGL consumer runner."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    uv = shutil.which("uv")
    if uv is None:
        print("uv is required to run the WebGL consumer.", file=sys.stderr)
        return 1
    return subprocess.call(  # noqa: S603 - fixed toolchain and caller arguments remain separate.
        [
            uv,
            "run",
            "--project",
            str(root / "scripts/python~"),
            "--locked",
            "--no-dev",
            "--group",
            "browser",
            "python",
            str(root / "scripts/unity/browser_consumer.py"),
            *sys.argv[1:],
        ],
        cwd=root,
    )


if __name__ == "__main__":
    sys.exit(main())
