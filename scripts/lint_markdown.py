"""Run the installed Markdown linter over the visible Git inventory."""

from __future__ import annotations

import json
import os
import shutil
import stat
import subprocess
import sys
import tempfile
from pathlib import Path, PurePosixPath
from typing import cast

from cli_encoding import configure_output
from run_node_bin import main as run_node_bin

ROOT = Path(__file__).resolve().parents[1]
ARGUMENT_BYTES = 12_000
INVENTORY_BYTES = 32 * 1024 * 1024
MARKDOWN_EXTENSIONS = frozenset((".md", ".markdown"))


def markdown_candidates(inventory: bytes, *, insensitive: bool) -> list[str]:
    files = set(inventory.decode("utf-8", errors="replace").split("\0"))
    candidates: list[str] = []
    for file in files:
        extension = PurePosixPath(file).suffix
        if insensitive:
            extension = extension.lower()
        if extension in MARKDOWN_EXTENSIONS and not any(
            part.startswith(".") for part in file.split("/")
        ):
            candidates.append(file)
    return sorted(candidates, key=lambda file: file.encode("utf-16-be"))


def read_inventory(root: Path) -> bytes:
    git = shutil.which("git")
    if git is None:
        raise ValueError("Markdown inventory failed: Git is missing from PATH.")
    try:
        with tempfile.TemporaryFile() as output:
            result = subprocess.run(  # noqa: S603 - fixed Git inventory command.
                [git, "ls-files", "-co", "--exclude-standard", "-z"],
                cwd=root,
                stdout=output,
                stderr=subprocess.PIPE,
                check=False,
            )
            output.seek(0)
            inventory = output.read(INVENTORY_BYTES + 1)
    except OSError as error:
        raise ValueError(f"Markdown inventory failed: {error}") from error
    if result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"Markdown inventory failed: {detail}")
    if len(inventory) > INVENTORY_BYTES:
        raise ValueError("Markdown inventory failed: Git output exceeds 32 MiB.")
    return inventory


def apply_markdownlint_ignore(root: Path, files: list[str]) -> list[str]:
    node = os.environ.get("UH_NODE_EXECUTABLE") or shutil.which("node")
    if node is None:
        raise ValueError("Cannot run Markdown lint without Node.")
    patterns = (root / ".markdownlintignore").read_bytes().decode("utf-8", errors="replace")
    request = json.dumps({"files": files, "patterns": patterns}).encode("ascii")
    result = subprocess.run(  # noqa: S603 - fixed repository adapter and structured stdin.
        [node, str(ROOT / "scripts/lint-markdown.mjs"), "--ignore-inventory"],
        cwd=root,
        input=request,
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        detail = result.stderr.decode("utf-8", errors="replace").strip()
        raise ValueError(f"Markdown ignore matching failed: {detail}")
    response = cast("object", json.loads(result.stdout))
    if not isinstance(response, list):
        raise ValueError("Markdown ignore matcher did not return a file list.")
    ignored: set[str] = set()
    requested = set(files)
    for path in cast("list[object]", response):
        if not isinstance(path, str) or path not in requested or path in ignored:
            raise ValueError("Markdown ignore matcher returned an unexpected file.")
        ignored.add(path)
    return [file for file in files if file not in ignored]


def discover_markdown_files(root: Path) -> list[str]:
    inventory = read_inventory(root)
    candidates = markdown_candidates(inventory, insensitive=sys.platform in ("win32", "darwin"))
    subjects: list[str] = []
    for file in apply_markdownlint_ignore(root, candidates):
        try:
            if stat.S_ISREG((root / file).stat().st_mode):
                subjects.append(file)
        except FileNotFoundError:
            continue
    return subjects


def batch_markdown_files(files: list[str]) -> list[list[str]]:
    batches: list[list[str]] = []
    batch: list[str] = []
    size = 0
    for file in files:
        argument_bytes = len(file.encode("utf-8")) + 3
        if argument_bytes > ARGUMENT_BYTES:
            raise ValueError(f"Markdown path exceeds the command argument budget: {file}")
        if batch and size + argument_bytes > ARGUMENT_BYTES:
            batches.append(batch)
            batch = []
            size = 0
        batch.append(file)
        size += argument_bytes
    if batch:
        batches.append(batch)
    return batches


def main(root: Path = ROOT) -> int:
    configure_output()
    try:
        files = discover_markdown_files(root)
        if not files:
            raise ValueError("Markdown lint found zero subjects.")
        print(f"Linting {len(files)} Markdown files from the Git inventory.", flush=True)
        batches = batch_markdown_files(files)
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    status = 0
    for batch in batches:
        batch_status = run_node_bin(
            [
                "markdownlint",
                "--config",
                ".markdownlint.json",
                "--ignore-path",
                ".markdownlintignore",
                "--",
                *batch,
            ],
            root=root,
        )
        if batch_status:
            status = batch_status
    return status


if __name__ == "__main__":
    sys.exit(main())
