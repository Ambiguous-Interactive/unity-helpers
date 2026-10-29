"""Create a deterministic Unity package from the validated npm release payload."""

from __future__ import annotations

import argparse
import gzip
import hashlib
import re
import subprocess
import sys
import tarfile
import tempfile
from dataclasses import dataclass
from pathlib import Path
from typing import cast

from stage_unitypackage import package_metadata, release_version, stage_unity_package

ASSET_ROOT = "Assets/WallstopStudios/UnityHelpers"
BLOCK_SIZE = 512


@dataclass(frozen=True)
class Asset:
    guid: str
    pathname: str
    meta: bytes
    source_path: Path | None = None


def tar_header(name: str, size: int) -> bytes:
    encoded_name = name.encode("utf-8")
    if len(encoded_name) > 100 or b"\0" in encoded_name:
        raise ValueError(f"Unity package archive path is too long or invalid: {name}")
    header = bytearray(BLOCK_SIZE)
    header[: len(encoded_name)] = encoded_name
    for offset, length, value in (
        (100, 8, 0o644),
        (108, 8, 0),
        (116, 8, 0),
        (124, 12, size),
        (136, 12, 0),
    ):
        _write_octal(header, offset, length, value)
    header[148:156] = b" " * 8
    header[156] = ord("0")
    header[257:265] = b"ustar\x0000"
    _write_octal(header, 148, 8, sum(header))
    return bytes(header)


def _write_octal(header: bytearray, offset: int, length: int, value: int) -> None:
    encoded = f"{value:0{length - 1}o}".encode("ascii")
    if value < 0 or len(encoded) != length - 1:
        raise ValueError(f"Tar value {value} does not fit in {length} bytes.")
    header[offset : offset + length] = encoded + b"\0"


def generated_folder_metadata(pathname: str) -> Asset:
    guid = hashlib.sha256(pathname.encode("utf-8")).hexdigest()[:32]
    meta = (
        f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n"
        "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    ).encode()
    return Asset(guid, pathname, meta)


def collect_assets(staged_root: Path) -> list[Asset]:
    assets = [generated_folder_metadata(ASSET_ROOT)]
    pending = [staged_root]
    while pending:
        directory = pending.pop()
        for source in sorted(directory.iterdir()):
            if source.name.startswith(".") or source.name.endswith(".meta"):
                continue
            if source.is_symlink() or not (source.is_file() or source.is_dir()):
                raise ValueError(f"Unity package payload contains an unsupported entry: {source}")
            if source.is_dir():
                pending.append(source)
            meta_path = Path(f"{source}.meta")
            pathname = f"{ASSET_ROOT}/{source.relative_to(staged_root).as_posix()}"
            if meta_path.exists():
                meta = meta_path.read_bytes()
                match = re.search(rb"^guid: ([0-9a-f]{32})\r?$", meta, re.MULTILINE)
                if match is None:
                    raise ValueError(f"Unity metadata has no valid GUID: {meta_path}")
                assets.append(
                    Asset(
                        match[1].decode("ascii"),
                        pathname,
                        meta,
                        source if source.is_file() else None,
                    )
                )
            elif source.is_dir():
                assets.append(generated_folder_metadata(pathname))
            else:
                raise ValueError(f"Unity package payload entry has no metadata: {source}")
    assets.sort(key=lambda asset: asset.guid)
    if len({asset.guid for asset in assets}) != len(assets):
        raise ValueError("Unity package payload contains duplicate GUIDs.")
    return assets


def _write_entry(archive: gzip.GzipFile, name: str, content: bytes) -> None:
    archive.write(tar_header(name, len(content)))
    archive.write(content)
    archive.write(bytes(-len(content) % BLOCK_SIZE))


def write_unity_package(assets: list[Asset], output_path: Path) -> str:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    # Separate scratch directories keep concurrent publishers from deleting each other's files.
    with tempfile.TemporaryDirectory(
        prefix="unitypackage-write-", dir=output_path.parent
    ) as scratch_name:
        scratch = Path(scratch_name)
        temporary_output = scratch / "package.gz"
        with (
            temporary_output.open("wb") as raw,
            gzip.GzipFile(filename="", mode="wb", compresslevel=9, fileobj=raw, mtime=0) as archive,
        ):
            for asset in assets:
                if asset.source_path is not None:
                    _write_entry(archive, f"{asset.guid}/asset", asset.source_path.read_bytes())
                _write_entry(archive, f"{asset.guid}/asset.meta", asset.meta)
                _write_entry(archive, f"{asset.guid}/pathname", asset.pathname.encode("utf-8"))
            archive.write(bytes(BLOCK_SIZE * 2))
        with temporary_output.open("rb") as package_file:
            digest = hashlib.file_digest(package_file, "sha256").hexdigest()
        temporary_checksum = scratch / "checksum"
        temporary_checksum.write_text(
            f"{digest}  {output_path.name}\n", encoding="utf-8", newline="\n"
        )
        temporary_output.replace(output_path)
        temporary_checksum.replace(f"{output_path}.sha256")
    return digest


def create_unity_package(
    repo_root: Path, output_path: Path, unity_version: str
) -> tuple[list[Asset], str]:
    with tempfile.TemporaryDirectory(prefix="unitypackage-create-") as scratch_name:
        project = stage_unity_package(repo_root, Path(scratch_name) / "project", unity_version)
        assets = collect_assets(project / ASSET_ROOT)
        digest = write_unity_package(assets, output_path)
        print(f"Created {output_path} with {len(assets)} Unity assets.")
        return assets, digest


def main() -> int:
    repo_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    try:
        name, version = package_metadata(repo_root)
        output_path = (
            cast("Path | None", args.output)
            or repo_root / ".artifacts/release" / f"{name}-{version}.unitypackage"
        )
        create_unity_package(repo_root, output_path.absolute(), release_version(repo_root))
    except (OSError, ValueError, tarfile.TarError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
