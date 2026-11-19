"""Executable and filesystem controls for Unity artifact redaction."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1] / "unity"
sys.path.insert(0, str(SCRIPT_DIRECTORY))

from credential_patterns import PATTERNS, redact_credentials  # noqa: E402
from redact_unity_artifacts import (  # noqa: E402
    COMMAND,
    LEGACY_COMMAND,
    MAXIMUM_FILE_BYTES,
    USAGE,
    redact_directory,
)

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/unity/redact_unity_artifacts.py"
SERIAL = "SC-FAKE-FAKE-FAKE-FAKE-FAKE"
BEARER = "FAKEbearerFAKEbearerFAKEbearer"
KEY_BODY = "FAKEkeybodyFAKEkeybodyFAKEkeybody"
CREDENTIALS = (
    (
        "pem-private-key",
        "key follows\n",
        f"-----BEGIN RSA PRIVATE KEY-----\n{KEY_BODY}\n-----END RSA PRIVATE KEY-----",
        "\ndone\n",
    ),
    (
        "unity-access-token",
        "[Licensing::Module] Access token: ",
        f"SYNTHETIC-{SERIAL}-private-tail",
        " (expires in 60 seconds)",
    ),
    (
        "unity-license-id",
        '<License id="',
        "FAKE-LICENSE-0000-0000",
        '" version="1.0">\n',
    ),
    ("unity-serial", "Activated with serial ", SERIAL, " today\n"),
    ("github-token", "remote token ", "ghp_" + "FAKEfake0123456789" * 2, " rejected\n"),
    (
        "aws-access-key-id",
        "uploader used ",
        "AKIA0000FAKE0000FAKE",
        " for the bucket\n",
    ),
    ("http-bearer-token", "Authorization: Bearer\u00a0", BEARER, "\r\n"),
    ("credential-assignment", "UNITY_PASSWORD=", "fake-password-value-0000", "\n"),
)


def run_script(tmp_path: Path, *arguments: str) -> subprocess.CompletedProcess[bytes]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), *arguments],
        capture_output=True,
        cwd=tmp_path,
        check=False,
    )


def test_every_pattern_has_a_positive_control() -> None:
    assert [name for name, _ in PATTERNS] == [name for name, *_ in CREDENTIALS]


@pytest.mark.parametrize(("name", "prefix", "credential", "suffix"), CREDENTIALS)
def test_each_pattern_preserves_surrounding_diagnostics(
    name: str, prefix: str, credential: str, suffix: str
) -> None:
    redacted, counts = redact_credentials(prefix + credential + suffix)
    assert redacted == prefix + f"<redacted:{name}>" + suffix
    assert counts == {name: 1}
    assert redact_credentials(redacted) == (redacted, {})


@pytest.mark.parametrize(
    "line",
    [
        "GITHUB_TOKEN=***\n",
        "UNITY_SERIAL=***\n",
        "API_KEY=short\n",
        "Authorization: Bearer short\n",
        "The build uploaded a token to the store without a password.\n",
        "[Licensing::Client] Successfully resolved entitlements in 0.284 seconds\n",
        "Initialize engine version: 6000.5.2f1 (b9e1b8d9d3a2)\n",
        "[Licensing::Module] Successfully updated the access token \r\nerror CS1234: failure",
        "[Licensing::Module] Access token: ***",
        "[Licensing::Module] Access token: [REDACTED-UNITY-ACCESS-TOKEN]",
        "[Licensing::Module] Error: Access token is unavailable; Code 20111",
    ],
)
def test_clean_text_is_unchanged(line: str) -> None:
    assert redact_credentials(line) == (line, {})


@pytest.mark.parametrize(
    ("label", "token"),
    [
        (
            "Successfully updated the access token ",
            "SYNTHETIC-FULL.TOKEN_0123456789+/=",
        ),
        ("Successfully Updated The Access Token ", "SYN..."),
        ("access token = '", "SYNTHETIC-FULL.TOKEN"),
        ("Access token: ", "SYNTHETIC-PREVIEW..."),
    ],
)
def test_access_token_preview_preserves_label(label: str, token: str) -> None:
    assert redact_credentials(label + token) == (
        label + "<redacted:unity-access-token>",
        {"unity-access-token": 1},
    )


def test_truncated_pem_redacts_body() -> None:
    assert redact_credentials("-----BEGIN PRIVATE KEY-----\n" + KEY_BODY) == (
        "<redacted:pem-private-key>",
        {"pem-private-key": 1},
    )


def test_cli_scans_nested_files_and_handles_multiple_directories(
    tmp_path: Path,
) -> None:
    root = tmp_path / "artifacts"
    logs = root / "logs"
    logs.mkdir(parents=True)
    first = logs / "unity.log"
    second = logs / "configure.log"
    binary = root / "build.bin"
    clean = root / "untouched.log"
    binary_content = b"\x00\xff" + SERIAL.encode()
    clean_content = b"ordinary bytes\r\n\x80\xff"
    first.write_bytes(f"serial {SERIAL}\r\nserial {SERIAL}\r\n".encode())
    second.write_bytes(f"Authorization: Bearer {BEARER}\nserial {SERIAL}\n".encode())
    binary.write_bytes(binary_content)
    clean.write_bytes(clean_content)

    result = run_script(tmp_path, "missing", "artifacts")
    assert result.returncode == 0
    assert not result.stderr
    assert result.stdout == (
        b"Skipping missing; it does not exist.\n"
        b"Redacted 2 file(s) under artifacts: http-bearer-token x1, unity-serial x3.\n"
        b"  logs/configure.log: http-bearer-token, unity-serial\n"
        b"  logs/unity.log: unity-serial\n"
    )
    assert first.read_bytes() == b"serial <redacted:unity-serial>\r\n" * 2
    assert second.read_bytes() == (
        b"Authorization: Bearer <redacted:http-bearer-token>\nserial <redacted:unity-serial>\n"
    )
    assert binary.read_bytes() == binary_content
    assert clean.read_bytes() == clean_content
    repeated = run_script(tmp_path, "artifacts")
    assert repeated.returncode == 0
    assert repeated.stdout == b"No credential material found under artifacts.\n"


@pytest.mark.parametrize(("arguments", "status"), [((), 1), (("--help",), 0), (("-h",), 0)])
def test_cli_usage(tmp_path: Path, arguments: tuple[str, ...], status: int) -> None:
    result = run_script(tmp_path, *arguments)
    assert result.returncode == status
    assert result.stdout == USAGE.encode()
    assert not result.stderr


def test_cli_rejects_unknown_option_and_file_root(tmp_path: Path) -> None:
    bad_option = run_script(tmp_path, "--nope")
    assert bad_option.returncode == 1
    assert bad_option.stderr == b"Unknown option --nope.\n"
    (tmp_path / "file").touch()
    bad_root = run_script(tmp_path, "file")
    assert bad_root.returncode == 1
    assert bad_root.stderr.endswith(b"file is not a directory.\n")


def test_directory_reports_unreadable_and_oversized_files(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    unreadable = tmp_path / "unreadable.log"
    unreadable.touch()
    large = tmp_path / "oversized.log"
    with large.open("wb") as stream:
        stream.truncate(MAXIMUM_FILE_BYTES + 1)
    original_read = Path.read_bytes

    def read_control(path: Path) -> bytes:
        if path == unreadable:
            raise PermissionError("access denied")
        assert path != large
        return original_read(path)

    monkeypatch.setattr(Path, "read_bytes", read_control)
    assert redact_directory(tmp_path, "artifacts") == (
        "No credential material found under artifacts.\n"
        f"  WARNING: oversized.log was not scanned because it is {MAXIMUM_FILE_BYTES + 1} bytes, "
        f"over the {MAXIMUM_FILE_BYTES} cap.\n"
        "  WARNING: unreadable.log was not scanned because it could not be read: access denied."
    )


def test_failed_rewrite_fails_closed(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    path = tmp_path / "locked.log"
    path.write_text(f"serial {SERIAL}\n", encoding="utf-8")

    def deny_write(_path: Path, _data: bytes) -> int:
        raise PermissionError("access denied")

    monkeypatch.setattr(Path, "write_bytes", deny_write)
    with pytest.raises(ValueError, match=r"locked\.log contains credential material"):
        redact_directory(tmp_path, "artifacts")


def test_legacy_node_cli_preserves_usage_and_working_directory(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None, "The compatibility control must exercise the actual Node launcher"
    launcher = ROOT / "scripts/unity/redact-unity-artifacts.js"
    help_result = subprocess.run(
        [node, str(launcher), "--help"], capture_output=True, cwd=tmp_path, check=False
    )
    assert help_result.returncode == 0
    assert help_result.stdout == USAGE.replace(COMMAND, LEGACY_COMMAND, 1).encode()
    assert not help_result.stderr

    artifacts = tmp_path / "artifacts with spaces"
    artifacts.mkdir()
    log = artifacts / "unity.log"
    log.write_bytes(f"serial {SERIAL}\r\n".encode())
    result = subprocess.run(
        [node, str(launcher), artifacts.name], capture_output=True, cwd=tmp_path, check=False
    )
    assert result.returncode == 0
    assert result.stdout == (
        b"Redacted 1 file(s) under artifacts with spaces: unity-serial x1.\n"
        b"  unity.log: unity-serial\n"
    )
    assert not result.stderr
    assert log.read_bytes() == b"serial <redacted:unity-serial>\r\n"
