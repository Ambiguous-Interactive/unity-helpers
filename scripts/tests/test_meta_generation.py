"""Historical importer outputs and metadata ownership controls."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from generate_meta import generate_metadata, render_metadata

COMMAND_TIMEOUT_SECONDS = 300
ROOT = Path(__file__).resolve().parents[2]
GOLDENS = cast(
    "dict[str, str]",
    json.loads(
        (Path(__file__).parent / "fixtures/meta-generation-golden.json").read_text(encoding="utf-8")
    ),
)


@pytest.mark.parametrize(("name", "digest"), sorted(GOLDENS.items()))
def test_every_legacy_importer_matches_frozen_bytes(tmp_path: Path, name: str, digest: str) -> None:
    asset = tmp_path / name
    if name == "folder":
        asset.mkdir()
    else:
        asset.write_bytes(b"")
    content = render_metadata(asset, "0" * 32, 0)
    assert hashlib.sha256(content).hexdigest() == digest
    assert content.count(b"\r\n") == content.count(b"\n")
    assert all(not line.endswith((b" ", b"\t")) for line in content.splitlines())


def test_existing_guid_and_importer_settings_are_preserved(tmp_path: Path) -> None:
    asset = tmp_path / "Custom.cs"
    asset.write_bytes(b"")
    destination = Path(f"{asset}.meta")
    retained = (
        b"fileFormatVersion: 2\nguid: " + b"1" * 32 + b"\nMonoImporter:\n  executionOrder: 123\n"
    )
    destination.write_bytes(retained)
    with pytest.raises(ValueError, match="already exists"):
        generate_metadata(asset)
    assert destination.read_bytes() == retained
    replaced, guid = generate_metadata(asset, force=True)
    assert replaced == destination
    assert re.fullmatch("[0-9a-f]{32}", guid)
    assert f"guid: {guid}\r\n".encode() in destination.read_bytes()
    assert b"executionOrder: 0" in destination.read_bytes()


def test_competing_creation_cannot_overwrite_existing_metadata(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    asset = tmp_path / "Race.cs"
    asset.write_bytes(b"")
    destination = Path(f"{asset}.meta")
    retained = b"another publisher's metadata"

    def competing_render(path: Path, guid: str, timestamp: int) -> bytes:
        destination.write_bytes(retained)
        return render_metadata(path, guid, timestamp)

    monkeypatch.setattr("generate_meta.render_metadata", competing_render)
    with pytest.raises(FileExistsError):
        generate_metadata(asset)
    assert destination.read_bytes() == retained


@pytest.mark.parametrize("name", ["space and 日本語.cs", "folder with spaces/"])
def test_cli_handles_file_and_folder_paths(tmp_path: Path, name: str) -> None:
    asset = tmp_path / name
    if name.endswith("/"):
        asset.mkdir()
    else:
        asset.write_bytes(b"")
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/generate_meta.py"), str(tmp_path / name)],
        capture_output=True,
        text=True,
        check=False,
        timeout=COMMAND_TIMEOUT_SECONDS,
    )
    assert result.returncode == 0, result.stderr
    assert "Created:" in result.stdout
    destination = Path(f"{asset}.meta")
    assert destination.is_file()
    content = destination.read_bytes()
    assert re.search(rb"(?m)^guid: [a-f0-9]{32}\r$", content)
    assert b"DefaultImporter:" in content if name.endswith("/") else b"MonoImporter:" in content


def test_missing_assets_do_not_create_metadata(tmp_path: Path) -> None:
    asset = tmp_path / "missing.cs"
    with pytest.raises(ValueError, match="does not exist"):
        generate_metadata(asset)
    assert not Path(f"{asset}.meta").exists()


def test_relative_asset_path_uses_callers_directory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.chdir(tmp_path)
    Path("Relative.cs").write_bytes(b"")
    destination, _guid = generate_metadata(Path("Relative.cs"))
    assert destination == tmp_path / "Relative.cs.meta"
    assert destination.read_bytes().startswith(b"fileFormatVersion: 2\r\nguid: ")


def test_no_arguments_and_invalid_paths_fail_clearly(tmp_path: Path) -> None:
    for arguments, message in (([], "usage:"), ([str(tmp_path / "missing")], "does not exist")):
        result = subprocess.run(
            [sys.executable, str(ROOT / "scripts/generate_meta.py"), *arguments],
            capture_output=True,
            text=True,
            check=False,
            timeout=COMMAND_TIMEOUT_SECONDS,
        )
        assert result.returncode == 1
        assert message in result.stderr
