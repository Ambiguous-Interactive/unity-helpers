"""Create and verify the on-disk nullable consumer player inventory."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
from contextlib import ExitStack
from pathlib import Path
from typing import TypedDict, cast

from frozen_files import (
    file_hash,
    git_output,
    identity,
    open_source,
    ordinal_key,
    regular_path,
    retain_source,
    walk,
)

_REQUIRED = (
    "IndependentConsumer.exe",
    "GameAssembly.dll",
    "UnityPlayer.dll",
    "IndependentConsumer_Data/il2cpp_data/Metadata/global-metadata.dat",
)
_TOOL_INPUTS = (
    "scripts/unity/player_manifest.py",
    "scripts/unity/frozen_files.py",
    "scripts/unity/lib/il2cpp-conversion-inputs.ps1",
    "scripts/python~/pyproject.toml",
    "scripts/python~/uv.lock",
)
_HEX = re.compile(r"[a-fA-F0-9]{64}")
_BAD_PATH = re.compile(r"(^/|:|[\r\n]|(?:^|/)\.{1,2}(?:/|$)|//|/$)")


class _Entry(TypedDict):
    path: str
    sha256: str


def _scan(root: Path) -> dict[Path, os.stat_result]:
    try:
        return {root: root.stat(), **{path: state for path, _, state in walk(root)}}
    except ValueError as error:
        if "reparse" in str(error):
            raise ValueError("Player inventory cannot contain linked paths") from error
        raise


def _inventory(
    root: Path, retained: ExitStack
) -> tuple[dict[str, str], dict[Path, os.stat_result]]:
    if not root.is_dir():
        raise ValueError("Actual player directory missing")
    try:
        root = regular_path(root, directory=True)
    except ValueError as error:
        raise ValueError("Player inventory cannot contain linked paths") from error
    states = _scan(root)
    files: dict[str, str] = {}
    for path, state in states.items():
        if not stat.S_ISREG(state.st_mode):
            continue
        if os.name == "nt":
            retained.enter_context(retain_source(path))
        files[path.relative_to(root).as_posix()] = file_hash(path, state)[0]
    for required in _REQUIRED:
        if required not in files:
            raise ValueError(f"Required actual player file missing: {required}")
    return files, states


def _check_snapshot(root: Path, states: dict[Path, os.stat_result]) -> None:
    current = _scan(root)
    if current.keys() != states.keys():
        raise ValueError("Actual player file inventory differs")
    for path, state in states.items():
        if identity(current[path]) != identity(state):
            raise ValueError(f"Actual player file differs: {path.relative_to(root).as_posix()}")


def _parse_manifest(data: bytes) -> dict[str, str]:
    rows: object = json.loads(data.decode("utf-8-sig"))
    if not isinstance(rows, list):
        raise ValueError("Malformed or duplicated actual player manifest entry")
    result: dict[str, str] = {}
    for row in cast("list[object]", rows):
        if not isinstance(row, dict):
            raise ValueError("Malformed or duplicated actual player manifest entry")
        fields = cast("dict[str, object]", row)
        path = fields.get("path")
        sha = fields.get("sha256")
        if (
            not isinstance(path, str)
            or not path.strip()
            or "\\" in path
            or _BAD_PATH.search(path)
            or path in result
            or not isinstance(sha, str)
            or not _HEX.fullmatch(sha)
        ):
            raise ValueError("Malformed or duplicated actual player manifest entry")
        result[path] = sha.lower()
    return result


def _match_manifest(
    root: Path, manifest: Path, expected_sha: str, retained: ExitStack
) -> tuple[dict[Path, os.stat_result], os.stat_result]:
    if not _HEX.fullmatch(expected_sha):
        raise ValueError("Actual player manifest hash differs")
    initial = manifest.stat()
    retained.enter_context(retain_source(manifest))
    with open_source(manifest) as stream:
        data = stream.read()
    if hashlib.sha256(data).hexdigest() != expected_sha.lower():
        raise ValueError("Actual player manifest hash differs")
    expected = _parse_manifest(data)
    files, states = _inventory(root, retained)
    if len(expected) != len(files):
        raise ValueError("Actual player file inventory differs")
    for path, digest in files.items():
        if expected.get(path) != digest:
            raise ValueError(f"Actual player file differs: {path}")
    return states, initial


def _prepare_manifest(
    root: Path, manifest: Path, retained: ExitStack
) -> tuple[bytes, dict[Path, os.stat_result]]:
    if manifest.is_relative_to(root):
        raise ValueError("Player manifest must be outside the player directory")
    if manifest.exists():
        raise ValueError("Refusing existing actual player manifest")
    files, states = _inventory(root, retained)
    rows = [_Entry(path=path, sha256=files[path]) for path in sorted(files, key=ordinal_key)]
    text = (json.dumps(rows, ensure_ascii=False, indent=2) + "\n").replace("\n", os.linesep)
    return text.encode("utf-8"), states


def _publish(manifest: Path, payload: bytes) -> str:
    try:
        with manifest.open("xb") as output:
            output.write(payload)
    except FileExistsError as error:
        raise ValueError("Refusing existing actual player manifest") from error
    return hashlib.sha256(payload).hexdigest()


def _tool_identity(repository: Path) -> tuple[bytes, tuple[str, ...]]:
    revision = git_output(repository, ["rev-parse", "--verify", "HEAD"]).strip()
    expected = os.environ.get("GITHUB_SHA")
    if expected and revision != expected.encode("ascii"):
        raise ValueError("Player manifest checkout differs from the recorded revision")
    objects = git_output(
        repository, ["rev-parse", *(f"{revision.decode('ascii')}:{path}" for path in _TOOL_INPUTS)]
    ).splitlines()
    actual = git_output(repository, ["hash-object", "--", *_TOOL_INPUTS]).splitlines()
    if objects != actual or len(actual) != len(_TOOL_INPUTS):
        raise ValueError("Player manifest tool inputs differ from the recorded checkout")
    return revision, tuple(
        file_hash(regular_path(repository / path, directory=False))[0] for path in _TOOL_INPUTS
    )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("create", "verify"))
    parser.add_argument("player_root", type=Path)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--expected-sha")
    args = parser.parse_args()
    if (args.operation == "verify") != (args.expected_sha is not None):
        parser.error("verify requires an expected hash and create takes none")
    try:
        with ExitStack() as retained:
            repository = Path(__file__).resolve().parents[2]
            if os.name == "nt":
                for path in _TOOL_INPUTS:
                    retained.enter_context(
                        retain_source(regular_path(repository / path, directory=False))
                    )
            before = _tool_identity(repository)
            root = Path(os.path.abspath(args.player_root))  # noqa: PTH100 - retain path entries.
            manifest = regular_path(args.manifest.parent, directory=True) / args.manifest.name
            if args.expected_sha is not None:
                manifest = regular_path(manifest, directory=False)
                states, initial = _match_manifest(root, manifest, args.expected_sha, retained)
                if _tool_identity(repository) != before:
                    raise ValueError("Player manifest tool inputs changed during verification")
                _check_snapshot(root, states)
                if identity(manifest.lstat()) != identity(initial):
                    raise ValueError("Actual player manifest hash differs")
            else:
                payload, states = _prepare_manifest(root, manifest, retained)
                if _tool_identity(repository) != before:
                    raise ValueError("Player manifest tool inputs changed during verification")
                _check_snapshot(root, states)
                print(_publish(manifest, payload))
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
