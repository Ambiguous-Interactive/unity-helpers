"""Fetch hash-pinned CDB files from Microsoft's bundle using bounded HTTP ranges."""

import hashlib
import io
import json
from pathlib import Path
import struct
import sys
import urllib.request
import zipfile


MAX_READ = 16 * 1024 * 1024
BUNDLE_SIZE = 1203512387
PACKAGE_NAME = "windbg_win-x64.msix"


class HttpWindow(io.RawIOBase):
    def __init__(self, url, size, base=0, total=BUNDLE_SIZE):
        self.url, self.size, self.base, self.total = url, size, base, total
        self.position = 0
        if min(size, base) < 0 or base + size > total:
            raise ValueError("Invalid archive window")

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.position

    def seek(self, offset, whence=0):
        if whence not in (0, 1, 2):
            raise ValueError("Invalid seek origin")
        position = offset + (0, self.position, self.size)[whence]
        if not 0 <= position <= self.size:
            raise ValueError("Seek outside archive window")
        self.position = position
        return position

    def read(self, count=-1):
        count = min(self.size - self.position, count if count >= 0 else self.size)
        if count > MAX_READ:
            raise ValueError("Archive read exceeds bound")
        if not count:
            return b""
        start = self.base + self.position
        end = start + count - 1
        request = urllib.request.Request(
            self.url, headers={"Range": f"bytes={start}-{end}"}
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            expected = f"bytes {start}-{end}/{self.total}"
            if response.status != 206 or response.headers.get("Content-Range") != expected:
                raise ValueError("Server did not honor exact bounded range")
            data = response.read(count + 1)
        if len(data) != count:
            raise ValueError("Range response length mismatch")
        self.position += count
        return data


def extract_files(stream, destination, pins):
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
                relative = name.removeprefix("amd64/")
                if name == relative or ".." in relative.split("/") or ":" in relative or "\\" in relative or relative.startswith("/"):
                    raise ValueError("Invalid pinned path")
                item = inner.getinfo(name)
                if item.file_size != pin["Bytes"] or item.file_size > MAX_READ:
                    raise ValueError("Pinned file size mismatch")
                with inner.open(item) as source:
                    data = source.read(MAX_READ + 1)
                if len(data) != pin["Bytes"] or hashlib.sha256(data).hexdigest() != pin["Sha256"]:
                    raise ValueError("Pinned file digest mismatch")
                target = destination / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                with target.open("xb") as output:
                    output.write(data)


def main():
    pins = json.loads(Path(__file__).with_name("native-debugger-files.json").read_text())
    destination = Path(sys.argv[1])
    destination.mkdir(parents=True, exist_ok=False)
    extract_files(HttpWindow(pins["Source"], BUNDLE_SIZE), destination, pins["Files"])
    print(f"Verified {len(pins['Files'])} native debugger files")


if __name__ == "__main__":
    main()
