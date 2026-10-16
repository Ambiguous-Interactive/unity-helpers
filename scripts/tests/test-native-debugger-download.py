# cspell:ignore cls
"""Exercise the real range downloader against a bounded local archive server."""

import hashlib
import importlib.util
import io
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import tempfile
import threading
import unittest
import zipfile


SPEC = importlib.util.spec_from_file_location(
    "native_debugger", Path(__file__).parents[1] / "unity/download-native-debugger.py"
)
DOWNLOADER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DOWNLOADER)


class RangeHandler(BaseHTTPRequestHandler):
    payload = b""
    malformed = False
    ranges = []

    def do_GET(self):
        start, end = map(int, self.headers["Range"].removeprefix("bytes=").split("-"))
        self.ranges.append((start, end))
        self.send_response(200 if self.malformed else 206)
        self.send_header("Content-Range", f"bytes {start}-{end}/{len(self.payload)}")
        self.end_headers()
        self.wfile.write(self.payload[start:end + 1])

    def log_message(self, *_):
        pass


class DownloadTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
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
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()
        cls.worker.join()

    def setUp(self):
        RangeHandler.malformed = False
        RangeHandler.ranges = []
        self.pin = {
            "Name": "amd64/cdb.exe", "Bytes": 17,
            "Sha256": hashlib.sha256(b"qualified fixture").hexdigest(),
        }

    def stream(self):
        size = len(RangeHandler.payload)
        return DOWNLOADER.HttpWindow(
            f"http://127.0.0.1:{self.server.server_port}/bundle", size, total=size
        )

    def test_nested_stored_package_and_deflated_selected_file(self):
        with tempfile.TemporaryDirectory() as directory:
            destination = Path(directory)
            DOWNLOADER.extract_files(self.stream(), destination, [self.pin])
            self.assertEqual((destination / "cdb.exe").read_bytes(), b"qualified fixture")
            self.assertEqual(list(destination.iterdir()), [destination / "cdb.exe"])
        self.assertTrue(RangeHandler.ranges)
        self.assertTrue(all(end - start + 1 <= DOWNLOADER.MAX_READ for start, end in RangeHandler.ranges))

    def test_wrong_hash_is_rejected_before_write(self):
        self.pin["Sha256"] = "0" * 64
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "digest"):
                DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])
            self.assertEqual(list(Path(directory).iterdir()), [])

    def test_wrong_size_is_rejected(self):
        self.pin["Bytes"] += 1
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "size"):
                DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])

    def test_whole_bundle_response_is_rejected(self):
        RangeHandler.malformed = True
        with self.assertRaisesRegex(ValueError, "range"):
            self.stream().read(1)

    def test_unbounded_read_is_rejected_before_request(self):
        stream = DOWNLOADER.HttpWindow("http://127.0.0.1:1/unused", DOWNLOADER.MAX_READ + 1, total=DOWNLOADER.MAX_READ + 1)
        with self.assertRaisesRegex(ValueError, "bound"):
            stream.read()

    def test_window_and_seek_bounds(self):
        with self.assertRaises(ValueError):
            DOWNLOADER.HttpWindow("unused", 10, base=11, total=20)
        with self.assertRaises(ValueError):
            self.stream().seek(-1)

    def test_pinned_path_cannot_escape_destination(self):
        self.pin["Name"] = "amd64/../../unselected.exe"
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(ValueError, "path"):
                DOWNLOADER.extract_files(self.stream(), Path(directory), [self.pin])


if __name__ == "__main__":
    unittest.main()
