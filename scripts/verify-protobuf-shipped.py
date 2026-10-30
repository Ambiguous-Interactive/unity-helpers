"""Verify shipped protobuf assemblies and patch against a fresh candidate build."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import cast

ROOT = Path(__file__).resolve().parents[1]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify(candidate: Path, shipped: Path, patch: Path) -> None:
    raw: object = json.loads((candidate / "candidate-receipt.json").read_text(encoding="utf-8"))
    if not isinstance(raw, dict):
        raise ValueError("Candidate receipt must be an object")
    receipt = cast("dict[str, object]", raw)
    files = receipt.get("files")
    if not isinstance(files, dict):
        raise ValueError("Candidate files must be an object")
    hashes = cast("dict[str, object]", files)
    expected = {
        "sourceCommit": "3aa672eaaf7763b86d62bec9ed7d7896b0511a8b",
        "sourceUrl": "https://github.com/protobuf-net/protobuf-net.git",
        "sdk": "9.0.319",
        "targetFramework": "netstandard2.1",
    }
    for name, value in expected.items():
        if receipt.get(name) != value:
            raise ValueError(f"Candidate {name} does not match the pinned build")
    for name in ("protobuf-net.dll", "protobuf-net.Core.dll"):
        expected_hash = hashes.get(name)
        if not isinstance(expected_hash, str):
            raise ValueError(f"Candidate {name} hash must be a string")
        if digest(candidate / name) != expected_hash or digest(shipped / name) != expected_hash:
            raise ValueError(f"Shipped {name} differs from the source build")
    if (
        digest(shipped / "il2cpp-aot.patch") != digest(patch)
        or digest(patch) != receipt["patchSha256"]
    ):
        raise ValueError("Shipped AOT patch differs from the source build")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-root", required=True, type=Path)
    arguments = parser.parse_args()
    try:
        verify(
            arguments.candidate_root,
            ROOT / "Runtime/Protobuf-Net",
            ROOT / "Generator~/ProtobufNetCurrentCandidate/il2cpp-aot.patch",
        )
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(str(error), file=sys.stderr)
        return 1
    print("Both shipped protobuf DLLs and their AOT patch match the pinned source build.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
