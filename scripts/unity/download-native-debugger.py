"""Fetch hash-pinned CDB files from Microsoft's bundle using bounded HTTP ranges."""

from __future__ import annotations

import hashlib
import io
import json
import struct
import sys
import urllib.request
import zipfile
from pathlib import Path
from typing import TYPE_CHECKING, cast

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence

MAX_READ = 16 * 1024 * 1024
BUNDLE_SIZE = 1203512387
PACKAGE_NAME = "windbg_win-x64.msix"


class HttpWindow(io.RawIOBase):
    def __init__(self, url: str, size: int, base: int = 0, total: int = BUNDLE_SIZE) -> None:
        self.url, self.size, self.base, self.total = url, size, base, total
        self.position = 0
        if min(size, base) < 0 or base + size > total:
            raise ValueError("Invalid archive window")

    def readable(self) -> bool:
        return True

    def seekable(self) -> bool:
        return True

    def tell(self) -> int:
        return self.position

    def seek(self, offset: int, whence: int = 0) -> int:
        if whence not in (0, 1, 2):
            raise ValueError("Invalid seek origin")
        position = offset + (0, self.position, self.size)[whence]
        if not 0 <= position <= self.size:
            raise ValueError("Seek outside archive window")
        self.position = position
        return position

    def read(self, count: int = -1) -> bytes:
        count = min(self.size - self.position, count if count >= 0 else self.size)
        if count > MAX_READ:
            raise ValueError("Archive read exceeds bound")
        if not count:
            return b""
        start = self.base + self.position
        end = start + count - 1
        request = urllib.request.Request(  # noqa: S310 - pinned source or local test server.
            self.url, headers={"Range": f"bytes={start}-{end}"}
        )
        with urllib.request.urlopen(request, timeout=30) as response:  # noqa: S310 - pinned source or local test server.
            expected = f"bytes {start}-{end}/{self.total}"
            if response.status != 206 or response.headers.get("Content-Range") != expected:
                raise ValueError("Server did not honor exact bounded range")
            data = cast("bytes", response.read(count + 1))
        if len(data) != count:
            raise ValueError("Range response length mismatch")
        self.position += count
        return data


def extract_files(
    stream: HttpWindow, destination: Path, pins: Sequence[Mapping[str, object]]
) -> None:
    with zipfile.ZipFile(stream) as outer:
        package = outer.getinfo(PACKAGE_NAME)
        if package.compress_type != zipfile.ZIP_STORED or package.flag_bits & 1:
            raise ValueError("Expected an unencrypted stored x64 package")
        stream.seek(package.header_offset)
        header = stream.read(30)
        if header[:4] != b"PK\x03\x04":
            raise ValueError("Invalid package local header")
        name_size, extra_size = struct.unpack_from("<HH", header, 26)
        start = package.header_offset + 30 + name_size + extra_size
        with zipfile.ZipFile(
            HttpWindow(stream.url, package.file_size, start, stream.total)
        ) as inner:
            for pin in pins:
                name = pin["Name"]
                size = pin["Bytes"]
                expected_hash = pin["Sha256"]
                if (
                    not isinstance(name, str)
                    or not isinstance(size, int)
                    or isinstance(size, bool)
                    or not 0 <= size <= MAX_READ
                    or not isinstance(expected_hash, str)
                ):
                    raise ValueError("Invalid pinned file metadata")
                relative = name.removeprefix("amd64/")
                if (
                    name == relative
                    or ".." in relative.split("/")
                    or ":" in relative
                    or "\\" in relative
                    or relative.startswith("/")
                ):
                    raise ValueError("Invalid pinned path")
                item = inner.getinfo(name)
                if item.file_size != size or item.file_size > MAX_READ:
                    raise ValueError("Pinned file size mismatch")
                with inner.open(item) as source:
                    data = source.read(MAX_READ + 1)
                if len(data) != size or hashlib.sha256(data).hexdigest() != expected_hash:
                    raise ValueError("Pinned file digest mismatch")
                target = destination / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open("xb") as output:
                    output.write(data)


def main() -> None:
    raw: object = json.loads(
        Path(__file__).with_name("native-debugger-files.json").read_text(encoding="utf-8")
    )
    if not isinstance(raw, dict):
        raise ValueError("Debugger manifest must be an object")
    pins = cast("dict[str, object]", raw)
    source = pins.get("Source")
    files = pins.get("Files")
    if not isinstance(source, str) or not isinstance(files, list) or not files:
        raise ValueError("Debugger manifest needs a source and nonempty files")
    records: list[Mapping[str, object]] = []
    for item in cast("list[object]", files):
        if not isinstance(item, dict):
            raise ValueError("Debugger file metadata must be an object")
        records.append(cast("dict[str, object]", item))
    destination = Path(sys.argv[1])
    destination.mkdir(parents=True, exist_ok=False)
    extract_files(HttpWindow(source, BUNDLE_SIZE), destination, records)
    print(f"Verified {len(records)} native debugger files")


if __name__ == "__main__":
    main()
