"""Verify the files retained by one loaded Unity Test Framework snapshot."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import stat
import sys
import zipfile
from pathlib import Path, PurePosixPath
from typing import IO, TypedDict, cast

_SOURCE = re.compile(r"package/[0-9]{4}\.(?:cs|asmdef)")
_PACKAGE = "com.unity.test-framework"
_PACKAGE_METADATA = "package/package.json"
_REQUIRED = {"manifest.json", "packages-lock.json", _PACKAGE_METADATA}
_MODULES = {"UnityEditor.TestRunner", "UnityEngine.TestRunner", "nunit.framework"}


class _Module(TypedDict):
    path: str
    name: str
    mvid: str


class _VerifiedSnapshot(TypedDict):
    project: str
    unity: str
    version: str
    source: str
    source_count: int
    files: dict[str, str]
    modules: list[_Module]


def _object(value: object) -> dict[str, object]:
    if not isinstance(value, dict):
        raise TypeError("Test Framework evidence requires a JSON object.")
    return cast("dict[str, object]", value)


def _array(value: object) -> list[object]:
    if not isinstance(value, list):
        raise TypeError("Test Framework evidence requires a JSON array.")
    return cast("list[object]", value)


def _string(value: object) -> str:
    if not isinstance(value, str):
        raise TypeError("Test Framework evidence requires a JSON string.")
    return value


def _integer(value: object) -> int:
    if not isinstance(value, int) or isinstance(value, bool):
        raise TypeError("Test Framework evidence requires a JSON integer.")
    return value


def _path(value: object, paths: set[str], description: str) -> str:
    if (
        not isinstance(value, str)
        or not value.strip()
        or re.search(r"[\\:\x00-\x1f\x7f]", value)
        or any(part in {"", ".", ".."} for part in value.split("/"))
        or value in paths
    ):
        raise ValueError(f"Malformed or duplicate {description} Test Framework evidence path.")
    paths.add(value)
    return value


def _ordinary_path(snapshot: Path, relative: str) -> Path:
    path = snapshot / relative
    for entry in (path, *path.parents):
        if entry == snapshot:
            break
        info = entry.lstat()
        if stat.S_ISLNK(info.st_mode) or (
            getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
        ):
            raise ValueError(f"Linked Test Framework evidence path: {relative}")
    if not path.is_file():
        raise ValueError(f"Test Framework evidence is not a file: {relative}")
    return path


def _read_payload(snapshot: Path, relative: str, physical: dict[str, str]) -> bytes:
    data = _ordinary_path(snapshot, relative).read_bytes()
    physical[relative] = hashlib.sha256(data).hexdigest()
    return data


def _digest(stream: IO[bytes]) -> tuple[str, int]:
    digest = hashlib.sha256()
    length = 0
    while block := stream.read(128 * 1024):
        digest.update(block)
        length += len(block)
    return digest.hexdigest(), length


def _source_entries(archive: zipfile.ZipFile) -> dict[str, zipfile.ZipInfo]:
    entries: dict[str, zipfile.ZipInfo] = {}
    for entry in archive.infolist():
        if (
            not _SOURCE.fullmatch(entry.filename)
            or entry.file_size > 16 * 1024 * 1024
            or entry.filename in entries
        ):
            raise ValueError("Malformed or duplicate archived Test Framework source.")
        entries[entry.filename] = entry
    return entries


def _verify_files(
    snapshot: Path,
    receipt: dict[str, object],
    physical: dict[str, str],
    archive: zipfile.ZipFile | None,
) -> tuple[set[str], dict[str, bytes]]:
    entries = {} if archive is None else _source_entries(archive)
    paths: set[str] = set()
    originals: set[str] = set()
    metadata: dict[str, bytes] = {}
    for value in _array(receipt["Files"]):
        file = _object(value)
        path = _path(file["Path"], paths, "retained")
        original = _path(file["OriginalPath"], originals, "original")
        packed_source = path.startswith("package/") and path != _PACKAGE_METADATA
        if packed_source:
            if (
                not _SOURCE.fullmatch(path)
                or not original.startswith("package/")
                or PurePosixPath(path).suffix != PurePosixPath(original).suffix
            ):
                raise ValueError("Malformed compact framework source mapping.")
        elif original != path:
            raise ValueError("Framework metadata/module mapping differs.")
        if archive is not None and packed_source:
            archived = entries.pop(path, None)
            if archived is None:
                raise ValueError(f"Missing archived Test Framework source: {path}")
            with archive.open(archived) as stream:
                digest, length = _digest(stream)
        elif path in {_PACKAGE_METADATA, "packages-lock.json"}:
            metadata[path] = _read_payload(snapshot, path, physical)
            digest, length = physical[path], len(metadata[path])
        else:
            with _ordinary_path(snapshot, path).open("rb") as stream:
                digest, length = _digest(stream)
            physical[path] = digest
        if length != _integer(file["Length"]) or digest != file["Sha256"]:
            raise ValueError(f"Test Framework evidence bytes differ: {path}")
    if entries:
        raise ValueError("Unlisted archived Test Framework source.")
    for required in sorted(_REQUIRED - paths):
        raise ValueError(f"Missing Test Framework metadata: {required}")
    return paths, metadata


def verify(snapshot: Path, process_id: int) -> _VerifiedSnapshot:
    if (snapshot / "capture-error.txt").exists():
        raise ValueError("Test Framework source capture was incomplete.")
    physical: dict[str, str] = {}
    receipt = _object(json.loads(_read_payload(snapshot, "receipt.json", physical)))
    schema = _integer(receipt["SchemaVersion"])
    if schema not in (2, 3) or process_id <= 0 or _integer(receipt["ProcessId"]) != process_id:
        raise ValueError("Test Framework snapshot does not match this Unity process/project.")
    if schema == 3:
        if receipt["SourceArchive"] != "sources.zip":
            raise ValueError("Malformed Test Framework source archive path.")
        with _ordinary_path(snapshot, "sources.zip").open("rb") as stream:
            physical["sources.zip"] = hashlib.file_digest(stream, "sha256").hexdigest()
            stream.seek(0)
            with zipfile.ZipFile(stream) as archive:
                paths, metadata = _verify_files(snapshot, receipt, physical, archive)
    else:
        paths, metadata = _verify_files(snapshot, receipt, physical, None)
    source_count = sum(path.startswith("package/") and path.endswith(".cs") for path in paths)
    if source_count < 1 or source_count != _integer(receipt["SourceCount"]):
        raise ValueError("Test Framework source inventory is empty or incomplete.")
    package = _object(json.loads(metadata[_PACKAGE_METADATA]))
    lock = _object(json.loads(metadata["packages-lock.json"]))
    dependencies = _object(lock["dependencies"])
    locked_package = _object(dependencies[_PACKAGE])
    version = _string(receipt["PackageVersion"])
    if (
        package["name"] != _PACKAGE
        or package["version"] != version
        or locked_package["version"] != version
    ):
        raise ValueError("Resolved Test Framework package identity differs from captured metadata.")
    expected = _MODULES.copy()
    modules: list[_Module] = []
    for value in _array(receipt["Assemblies"]):
        assembly = _object(value)
        name = _string(assembly["Name"])
        path = f"assemblies/{name}.dll"
        if name not in expected or assembly["SavedPath"] != path or path not in paths:
            raise ValueError("Missing or duplicate loaded Test Framework assembly identity.")
        expected.remove(name)
        modules.append(
            {
                "path": path,
                "name": _string(assembly["FullName"]),
                "mvid": _string(assembly["ModuleVersionId"]),
            }
        )
    if expected:
        raise ValueError("Missing loaded Test Framework assembly identity.")
    return {
        "project": _string(receipt["ProjectPath"]),
        "unity": _string(receipt["UnityVersion"]),
        "version": version,
        "source": _string(receipt["PackageSource"]),
        "source_count": source_count,
        "files": physical,
        "modules": modules,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--process-id", type=int, required=True)
    parser.add_argument("snapshot", type=Path)
    args = parser.parse_args()
    try:
        verified = verify(args.snapshot, args.process_id)
    except (
        KeyError,
        OSError,
        TypeError,
        ValueError,
        zipfile.BadZipFile,
        RuntimeError,
    ) as error:
        print(str(error), file=sys.stderr)
        return 1
    print(json.dumps(verified, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
