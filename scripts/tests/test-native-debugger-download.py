# cspell:ignore cls
"""Exercise the real range downloader against a bounded local archive server."""

from __future__ import annotations

import hashlib
import importlib.util
import io
import tempfile
import threading
import unittest
import zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import TYPE_CHECKING, ClassVar, Protocol, cast

import pytest

if TYPE_CHECKING:
    from collections.abc import Mapping, Sequence


class Window(Protocol):
    def read(self, count: int = -1) -> bytes: ...
    def seek(self, offset: int, whence: int = 0) -> int: ...


class DownloaderModule(Protocol):
    MAX_READ: int
    PACKAGE_NAME: str

    def HttpWindow(self, url: str, size: int, base: int = 0, total: int = ...) -> Window: ...
    def extract_files(
        self, stream: Window, destination: Path, pins: Sequence[Mapping[str, object]]
    ) -> None: ...


SPEC = importlib.util.spec_from_file_location(
    "native_debugger", Path(__file__).parents[1] / "unity/download-native-debugger.py"
)
if SPEC is None or SPEC.loader is None:
    raise ImportError("native_debugger")
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)
DOWNLOADER = cast("DownloaderModule", module)


class RangeHandler(BaseHTTPRequestHandler):
    payload = b""
    malformed = False
    response_mode = "exact"
    ranges: ClassVar[list[tuple[int, int]]] = []

    def do_GET(self) -> None:
        header = self.headers.get("Range")
        if header is None:
            self.send_error(400)
            return
        start, end = map(int, header.removeprefix("bytes=").split("-"))
        self.ranges.append((start, end))
        self.send_response(200 if self.malformed else 206)
        self.send_header(
            "Content-Range",
            f"bytes {start}-{end}/{len(self.payload) + (self.response_mode == 'wrong-range')}",
        )
        self.end_headers()
        data = self.payload[start : end + 1]
        if self.response_mode == "short":
            data = data[:-1]
        elif self.response_mode == "long":
            data += b"x"
        self.wfile.write(data)

    def log_message(self, format: str, *args: object) -> None:  # noqa: A002 - HTTP handler override.
        pass


class DownloadTests(unittest.TestCase):
    server: ClassVar[ThreadingHTTPServer]
    worker: ClassVar[threading.Thread]

    @classmethod
    def setUpClass(cls) -> None:
        inner = io.BytesIO()
        with zipfile.ZipFile(inner, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("amd64/cdb.exe", b"qualified fixture")
            archive.writestr("amd64/../../unselected.exe", b"never extracted")
        outer = io.BytesIO()
        with zipfile.ZipFile(outer, "w", zipfile.ZIP_STORED) as archive:
            archive.writestr(DOWNLOADER.PACKAGE_NAME, inner.getvalue())
        RangeHandler.payload = outer.getvalue()
        cls.server = ThreadingHTTPServer(("127.0.0.1", 0), RangeHandler)
        cls.worker = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.worker.start()

    @classmethod
    def tearDownClass(cls) -> None:
        cls.server.shutdown()
        cls.server.server_close()
        cls.worker.join()

    def setUp(self) -> None:
        RangeHandler.malformed = False
        RangeHandler.response_mode = "exact"
        RangeHandler.ranges = []
        self.pin: dict[str, object] = {
            "Name": "amd64/cdb.exe",
            "Bytes": 17,
            "Sha256": hashlib.sha256(b"qualified fixture").hexdigest(),
        }

    def stream(self) -> Window:
        size = len(RangeHandler.payload)
        return DOWNLOADER.HttpWindow(
            f"http://127.0.0.1:{self.server.server_port}/bundle", size, total=size
        )

    def test_nested_stored_package_and_deflated_selected_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            DOWNLOADER.extract_files(self.stream(), destination, [self.pin])
            assert (destination / "cdb.exe").read_bytes() == b"qualified fixture"
            assert list(destination.iterdir()) == [destination / "cdb.exe"]
        assert RangeHandler.ranges
        assert all(end - start + 1 <= DOWNLOADER.MAX_READ for start, end in RangeHandler.ranges)

    def test_wrong_hash_is_rejected_before_write(self) -> None:
        self.pin["Sha256"] = "0" * 64
        with tempfile.TemporaryDirectory() as directory:
            with pytest.raises(ValueError, match="digest"):
                DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])
            assert list(Path(directory).iterdir()) == []

    def test_wrong_size_is_rejected(self) -> None:
        self.pin["Bytes"] = 18
        with tempfile.TemporaryDirectory() as directory, pytest.raises(ValueError, match="size"):
            DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])

    def test_whole_bundle_response_is_rejected(self) -> None:
        RangeHandler.malformed = True
        with pytest.raises(ValueError, match="range"):
            self.stream().read(1)

    def test_unbounded_read_is_rejected_before_request(self) -> None:
        stream = DOWNLOADER.HttpWindow(
            "http://127.0.0.1:1/unused",
            DOWNLOADER.MAX_READ + 1,
            total=DOWNLOADER.MAX_READ + 1,
        )
        with pytest.raises(ValueError, match="bound"):
            stream.read()

    def test_range_header_and_delivered_length_must_be_exact(self) -> None:
        for mode in ("wrong-range", "short", "long"):
            with self.subTest(mode=mode):
                RangeHandler.response_mode = mode
                with pytest.raises(ValueError, match=r"range|length"):
                    self.stream().read(4)

    def test_seek_origins_and_zero_reads(self) -> None:
        stream = self.stream()
        assert stream.read(0) == b""
        assert RangeHandler.ranges == []
        assert stream.seek(2) == 2
        assert stream.seek(3, 1) == 5
        size = len(RangeHandler.payload)
        assert stream.seek(-2, 2) == size - 2
        assert stream.read() == RangeHandler.payload[-2:]
        assert stream.read() == b""
        with pytest.raises(ValueError, match="Invalid seek origin"):
            stream.seek(0, 3)

    def test_invalid_pin_types_and_sizes_never_write(self) -> None:
        cases: list[tuple[str, object]] = [
            ("Name", None),
            ("Name", 4),
            ("Bytes", True),
            ("Bytes", "17"),
            ("Bytes", -1),
            ("Bytes", DOWNLOADER.MAX_READ + 1),
            ("Sha256", None),
        ]
        for name, value in cases:
            pin = dict(self.pin)
            pin[name] = value
            with self.subTest(name=name, value=value), tempfile.TemporaryDirectory() as directory:
                with pytest.raises(ValueError, match="Invalid pinned file metadata"):
                    DOWNLOADER.extract_files(self.stream(), Path(directory), [pin])
                assert list(Path(directory).iterdir()) == []

    def test_window_and_seek_bounds(self) -> None:
        with pytest.raises(ValueError, match="Invalid archive window"):
            DOWNLOADER.HttpWindow("unused", 10, base=11, total=20)
        with pytest.raises(ValueError, match="Seek outside archive window"):
            self.stream().seek(-1)

    def test_pinned_path_cannot_escape_destination(self) -> None:
        self.pin["Name"] = "amd64/../../unselected.exe"
        with tempfile.TemporaryDirectory() as directory, pytest.raises(ValueError, match="path"):
            DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])


if __name__ == "__main__":
    unittest.main()
