"""Compute Unity workspace choices and retention plans for the native host adapter."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import stat
import sys
import uuid
from pathlib import Path
from typing import cast

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cli_encoding import configure_output

WHITESPACE = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000"
RESIDUE = re.compile(
    r"^Odin(ApplyIntegration|ImportVerification|BoundConversion|SceneScope|MigrationImport|MigrationReferences|PairedPreview|LiveAddress)-[0-9a-f]{32}$",
    re.IGNORECASE | re.ASCII,
)


def leaf_name(version: str, mode: str, scope: str = "", *, share_editor: bool = False) -> str:
    if share_editor and mode.lower() == "playmode":
        mode = "editmode"
    scope = scope.strip(WHITESPACE)
    return f"{version}-{mode}" + (f"-{scope}" if scope else "")


def join_path(root: str, *children: str) -> str:
    result = root.replace("\\", os.sep).replace("/", os.sep)
    for child in children:
        result = os.path.join(  # noqa: PTH118 - retain native lexical dot components.
            result, child.replace("\\", os.sep).replace("/", os.sep).lstrip(os.sep)
        )
    return result


def workspace(
    repo: str,
    version: str,
    mode: str,
    scope: str = "",
    *,
    share_editor: bool = False,
    explicit: str = "",
    persistent: str = "",
    fingerprint: str = "",
) -> dict[str, str | bool]:
    leaf = leaf_name(version, mode, scope, share_editor=share_editor)
    projects = join_path(repo, ".artifacts", "unity", "projects")
    caches = join_path(repo, ".artifacts", "unity", "cache")
    if explicit.strip(WHITESPACE):
        return {
            "LegacyProjectPath": explicit,
            "ProjectPath": explicit,
            "CacheRoot": join_path(caches, version),
            "Persistent": False,
            "PruneRoot": "",
            "LeafName": leaf,
        }
    legacy = leaf
    if fingerprint.strip(WHITESPACE):
        if re.fullmatch(r"[a-f0-9]{16}", fingerprint) is None:
            raise ValueError(
                "Project generation fingerprint must contain sixteen lowercase hexadecimal characters."
            )
        leaf = "f-" + hashlib.sha256((legacy + "\n" + fingerprint).encode()).hexdigest()[:16]
    persistent = persistent.strip(WHITESPACE)
    if persistent:
        projects = join_path(persistent, "projects")
        caches = join_path(persistent, "cache")
    return {
        "LegacyProjectPath": join_path(projects, legacy),
        "ProjectPath": join_path(projects, leaf),
        "CacheRoot": join_path(caches, version),
        "Persistent": bool(persistent),
        "PruneRoot": projects if persistent else "",
        "LeafName": leaf,
    }


def prune_plan(
    ordered_candidates: list[tuple[str, object]], free: float, minimum: float
) -> list[str]:
    if not math.isfinite(free) or not math.isfinite(minimum):
        raise ValueError("Prune free-space values must be finite.")
    planned: list[str] = []
    for name, raw_size in ordered_candidates:
        if free >= minimum:
            break
        size = number(raw_size)
        if not math.isfinite(size) or size < 0:
            raise ValueError("Prune candidate sizes must be finite and nonnegative.")
        planned.append(name)
        free += size
    return planned


def reject_linked_ancestors(path: Path) -> None:
    for ancestor in (path, *path.parents):
        try:
            info = ancestor.lstat()
        except FileNotFoundError:
            continue
        attributes: object = getattr(info, "st_file_attributes", 0)
        if stat.S_ISLNK(info.st_mode) or (isinstance(attributes, int) and attributes & 0x400):
            raise ValueError(f"Generated project isolation refuses linked paths: {ancestor}")


def decode_text(data: bytes) -> str:
    for marker, encoding in [
        (b"\xff\xfe\x00\x00", "utf-32"),
        (b"\x00\x00\xfe\xff", "utf-32"),
        (b"\xff\xfe", "utf-16"),
        (b"\xfe\xff", "utf-16"),
    ]:
        if data.startswith(marker):
            return data.decode(encoding, errors="replace")
    return data.decode("utf-8-sig", errors="replace")


def retention_plan(project: str, *, explicit: bool = False) -> dict[str, object] | None:
    path = Path(os.path.abspath(project))  # noqa: PTH100 - inspect links before resolving ancestors.
    retained_root = path.parent.parent / "retained-projects"
    pointer = "Library/WallstopOdinMigrationApply/active.txt"
    for candidate in (
        path,
        path / "Assets/Editor",
        path / "Library/WallstopOdinMigrationApply",
        retained_root,
    ):
        reject_linked_ancestors(candidate)
    if not path.is_dir():
        return None
    reasons: list[str] = []
    try:
        (path / pointer).stat()
    except FileNotFoundError:
        pass
    else:
        reasons.append(pointer)
    assets = path / "Assets"
    if assets.is_dir():
        reasons.extend(
            "Assets/" + entry.name
            for entry in sorted(assets.iterdir(), key=lambda entry: entry.name.lower())
            if entry.is_dir() and RESIDUE.fullmatch(entry.name)
        )
    if not reasons:
        return None
    if explicit:
        raise ValueError(
            f"Migration residue remains in explicit project '{path}'. Preserve and isolate that project before running tests."
        )
    configurator = path / "Assets/Editor/UhCiTestConfigurator.cs"
    reject_linked_ancestors(configurator)
    if not configurator.is_file() or configurator.stat().st_size > 65536:
        raise ValueError(
            "Migration residue was found, but generated project provenance could not be established."
        )
    with configurator.open("rb") as stream:
        data = stream.read(65537)
    if (
        len(data) > 65536
        or re.search(
            r"public static class UhCiTestConfigurator\b",
            decode_text(data),
            re.IGNORECASE | re.ASCII,
        )
        is None
    ):
        raise ValueError(
            "Migration residue was found, but generated project provenance could not be established."
        )
    return {
        "OriginalPath": str(path),
        "RetainedRoot": str(retained_root),
        "RetainedPath": str(retained_root / (path.name + "-" + uuid.uuid4().hex)),
        "Reasons": reasons,
    }


def text(request: dict[str, object], key: str, default: str = "") -> str:
    value = request.get(key, default)
    if not isinstance(value, str):
        raise ValueError(f"Expected text for {key}.")
    return value


def boolean(request: dict[str, object], key: str) -> bool:
    value = request.get(key, False)
    if not isinstance(value, bool):
        raise ValueError(f"Expected a boolean for {key}.")
    return value


def number(value: object) -> float:
    if value is None:
        return 0.0
    if not isinstance(value, (int, float, str)):
        raise ValueError("Expected a numeric prune value.")
    return float(value)


def dispatch(request: dict[str, object]) -> object:
    action = text(request, "action")
    if action == "leaf":
        return leaf_name(
            text(request, "version"),
            text(request, "mode"),
            text(request, "scope"),
            share_editor=boolean(request, "share_editor"),
        )
    if action == "workspace":
        return workspace(
            text(request, "repo"),
            text(request, "version"),
            text(request, "mode"),
            text(request, "scope"),
            share_editor=boolean(request, "share_editor"),
            explicit=text(request, "explicit"),
            persistent=text(request, "persistent"),
            fingerprint=text(request, "fingerprint"),
        )
    if action == "retention-plan":
        return retention_plan(text(request, "project"), explicit=boolean(request, "explicit"))
    if action == "prune-plan":
        rows = request.get("candidates")
        if not isinstance(rows, list):
            raise ValueError("Expected an ordered candidate array.")
        candidates: list[tuple[str, object]] = []
        for value in cast("list[object]", rows):
            if not isinstance(value, dict):
                raise ValueError("Expected candidate objects.")
            row = cast("dict[str, object]", value)
            candidates.append((text(row, "name"), row.get("size")))
        return prune_plan(candidates, number(request.get("free")), number(request.get("minimum")))
    raise ValueError("Unknown workspace action.")


def main() -> int:
    try:
        request: object = json.load(sys.stdin)
        if not isinstance(request, dict):
            raise ValueError("Expected a workspace request object.")
        value = dispatch(cast("dict[str, object]", request))
        print(json.dumps({"value": value, "error": ""}, ensure_ascii=True))
        return 0
    except (OSError, ValueError, OverflowError) as error:
        print(json.dumps({"value": None, "error": str(error)}, ensure_ascii=True))
        return 1


if __name__ == "__main__":
    configure_output()
    raise SystemExit(main())
