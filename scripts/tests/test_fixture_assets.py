"""Hold decoded pixels from the original generators and preserve Unity metadata identity."""

from __future__ import annotations

import hashlib
import os
import re
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import cast

import pytest
import yaml
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from fixture_assets import write_metadata

ROOT = Path(__file__).resolve().parents[2]
PIXELS: dict[str, tuple[int, int, str]] = {
    "test_2x2_grid.png": (
        64,
        64,
        "768b01f1a3557534bb8ddb18bee87af95c68097e82dc6c368df35f8519190647",
    ),
    "test_4x4_grid.png": (
        128,
        128,
        "0b3da26db7f19b6074bd2314bc55d3d1eb1d600855e61f7f60f7969a9cc6fceb",
    ),
    "test_8x8_grid.png": (
        256,
        256,
        "d38e7267a52a6699f555160c7d3468db9aa03899450f77c5f6519b51ba240dbf",
    ),
    "test_boundary_256.png": (
        256,
        256,
        "3cfdc2a9a5a7182cab2e7ef24469f1b598022fe65a2ab6f275493e34603e0cb0",
    ),
    "test_large_512.png": (
        512,
        512,
        "153fb6d0706df217901e8832186e25f65b200f531c6ddc8ed398f3aae5080a97",
    ),
    "test_npot_100x200.png": (
        100,
        200,
        "4161aa7ee4eaeb51b6dd94a753fdd8d2bc54578ba39468d8fc62b8c50a866370",
    ),
    "test_npot_150x75.png": (
        150,
        75,
        "94b48bddbf4963443574642f39e9043c6057292b88c44d81cd264af7549fc3dd",
    ),
    "test_odd.png": (
        63,
        63,
        "c12d80a58584afe5cca71ef315cbbfd82dcf4181a8dbddb077b5c0f369ffb4a1",
    ),
    "test_prime_127.png": (
        127,
        127,
        "3e2f7f5bd22ce005186ee36c4433960736d1b4bb84b6135875a4d652aeb4c868",
    ),
    "test_single.png": (
        32,
        32,
        "4ab589b8423c13f8f33994356d5b696d9b3b56d968e1939f2088c53b5149e6cf",
    ),
    "test_small_16x16.png": (
        16,
        16,
        "8485f72a3f18aefc7e2f859a54cd39557bb06b542aaaac99e4f6b03c68c4b8c0",
    ),
    "test_tall.png": (
        64,
        128,
        "a657cc74e9556d2fbc1b830dfca9ccd16f5f0cebf2ba2710798aa98c3964f15d",
    ),
    "test_wide.png": (
        128,
        64,
        "480c126873b7a4b24d766d210db0f182e87cfbc92ff58d2c35f30c2b55f9caa6",
    ),
    "solid_100x200_green.png": (
        100,
        200,
        "3f1d6bdfc3b23ff71aa9e9843385fa78c8a37ea014041343421db2a9b927f3fb",
    ),
    "solid_1024x1024_black.png": (
        1024,
        1024,
        "4571a45562870efcbcbaacbe46976b780a3be00a4b3651d8a0a3329e6f38ac3b",
    ),
    "solid_128x128_white.png": (
        128,
        128,
        "71189f7fb6aed638640078fba3a35fda6c39c8962e74dcc75935aac948da9063",
    ),
    "solid_1x1_black.png": (
        1,
        1,
        "e3820096cb82366b860b8a4e668453a7aaaf423af03bdf289fa308ea03a79332",
    ),
    "solid_1x512_yellow.png": (
        1,
        512,
        "06118cf24835ed70dcc3ff5e5c3ad065b371e4862843fea5434bedbdc61debe8",
    ),
    "solid_2048x2048_black.png": (
        2048,
        2048,
        "5eab350b8220ac9944dbdfa4ac3288c7c17e24c3bf5df0dfcf6173d64dae00a1",
    ),
    "solid_255x255_gray.png": (
        255,
        255,
        "d578b88600c23cc3cc0e7b76a18b7ee7d4fbd0764e6c4d21b3385f4e20c2d4fe",
    ),
    "solid_256x256_cyan.png": (
        256,
        256,
        "6f32e32ab8c91612b72d3fc34ee58ae737f95d076c7d5c34e037c1330786e25d",
    ),
    "solid_257x64_gray.png": (
        257,
        64,
        "33590c0ddefb60325311d564a4b66271e4a7f78202c8b105c0b8995a74fb26d7",
    ),
    "solid_2x2_black.png": (
        2,
        2,
        "cc0fa51d4d0a97b664030be5052f3b2b69a1267f89ace4c9bbc65007566725df",
    ),
    "solid_300x100_magenta.png": (
        300,
        100,
        "8e762823c4b79fbf34b75385402e0f57ac01d4f3ab622bb704f15e88d7e47d81",
    ),
    "solid_32x32_black.png": (
        32,
        32,
        "df894663e850afa3619ed3d3825d9d1c6520f15fcd76aac25c4407c405fa8cfe",
    ),
    "solid_384x10_blue.png": (
        384,
        10,
        "178f7bd97d8b66bd838768ec326df84eaeae411c445c36f2b066a0691fed7ca6",
    ),
    "solid_400x240_blue.png": (
        400,
        240,
        "12f5dd9f81319485d7d8b71e4fed418f70ee5f91fd79f4005eef6e4c72e96c4f",
    ),
    "solid_4096x4096_black.png": (
        4096,
        4096,
        "4d7cd28460b9a0b3d471afda89be93c5fed1f52f7fbc74011e5528d16087e73d",
    ),
    "solid_450x254_blue.png": (
        450,
        254,
        "ababe4dc76153dbaceabab20279c6fd0a4ad179d01b329df6782625e55da66e7",
    ),
    "solid_511x511_gray.png": (
        511,
        511,
        "c1f80b84271f6ff79d79a21eb91eaac95130e7bc2042a27db1fc8c66584a5c4a",
    ),
    "solid_512x1_yellow.png": (
        512,
        1,
        "06118cf24835ed70dcc3ff5e5c3ad065b371e4862843fea5434bedbdc61debe8",
    ),
    "solid_512x512_gray.png": (
        512,
        512,
        "afd67069293fe32c301caee4e4599edc42783d96f521ba83ee57bb9e68fa8963",
    ),
    "solid_513x400_gray.png": (
        513,
        400,
        "0598ea4295b962860c15543020c6cafd1c0b87380aa3239fe718dd0b68c449b0",
    ),
    "solid_64x64_red.png": (
        64,
        64,
        "0bcc07de1631aa0395013f35790f719bd754f61e4bb2847a86fdc2365d3d8d63",
    ),
}


def run_generator(
    kind: str, output: Path, *, extended: bool = False
) -> subprocess.CompletedProcess[str]:
    environment = dict(os.environ, PYTHONUTF8="0", PYTHONIOENCODING="cp1252")
    return subprocess.run(
        [
            sys.executable,
            str(ROOT / f"scripts/generate-test-{kind}.py"),
            "--output-dir",
            str(output),
            *(["--extended"] if extended else []),
        ],
        capture_output=True,
        encoding="utf-8",
        check=False,
        timeout=60,
        env=environment,
    )


def read_mapping(value: object) -> dict[str, object]:
    assert isinstance(value, dict)
    assert all(isinstance(key, str) for key in cast("dict[object, object]", value))
    return cast("dict[str, object]", value)


def read_guid(metadata: str) -> str:
    """Read the literal GUID because YAML can coerce numeric-looking values."""
    match = re.search(r"^guid: ([0-9a-f]{32})\r?$", metadata, re.MULTILINE)
    assert match is not None
    return match[1]


@pytest.mark.parametrize(
    "guid",
    [
        "11111111111141118111111111111111",
        "00000000000040008000000000000000",
        "abcdefabcdef4abc8abcabcdefabcdef",
    ],
)
def test_guid_reader_preserves_numeric_and_leading_zero_identifiers(guid: str) -> None:
    assert read_guid(f"fileFormatVersion: 2\r\nguid: {guid}\r\n") == guid


@pytest.mark.parametrize("guid", ["123", "x" * 32, "1" * 33])
def test_guid_reader_rejects_invalid_literal_identifiers(guid: str) -> None:
    with pytest.raises(AssertionError):
        read_guid(f"guid: {guid}\r\n")


def assert_image_metadata(path: Path, kind: str) -> str:
    width, height, _ = PIXELS[path.name]
    metadata_bytes = path.with_suffix(".png.meta").read_bytes()
    assert b"\n" not in metadata_bytes.replace(b"\r\n", b"")
    metadata_text = metadata_bytes.decode("utf-8")
    metadata = read_mapping(yaml.safe_load(metadata_text))
    guid = read_guid(metadata_text)
    importer = read_mapping(metadata["TextureImporter"])
    assert importer["isReadable"] == 1
    assert importer["nPOTScale"] == 0
    expected_mode = (
        0
        if kind == "textures"
        else 1
        if path.name in ("test_single.png", "test_prime_127.png")
        else 2
    )
    assert importer["spriteMode"] == expected_mode
    assert importer["textureType"] == (0 if kind == "textures" else 8)
    sheet = read_mapping(importer["spriteSheet"])
    if kind == "textures":
        assert importer["spriteMode"] == 0
    elif importer["spriteMode"] == 1:
        assert sheet["sprites"] == []
        assert sheet["nameFileIdTable"] == {}
        metadata_text = path.with_suffix(".png.meta").read_text(encoding="utf-8")
        assert "    sprites: []\n" in metadata_text
        assert "    nameFileIdTable: {}\n" in metadata_text
    else:
        sprites = sheet["sprites"]
        assert isinstance(sprites, list)
        assert sprites
        table = read_mapping(sheet["nameFileIdTable"])
        assert len(cast("list[object]", sprites)) == len(table)
        local_ids: set[int] = set()
        for value in cast("list[object]", sprites):
            sprite = read_mapping(value)
            name = sprite["name"]
            identifier = sprite["internalID"]
            assert isinstance(name, str)
            assert isinstance(identifier, int)
            assert -(1 << 63) <= identifier < (1 << 63)
            assert identifier not in local_ids
            local_ids.add(identifier)
            assert table[name] == identifier
            rect = read_mapping(sprite["rect"])
            x, y = cast("int", rect["x"]), cast("int", rect["y"])
            cell_width, cell_height = (
                cast("int", rect["width"]),
                cast("int", rect["height"]),
            )
            assert 0 < cell_width <= width
            assert 0 < cell_height <= height
            assert 0 <= x <= width - cell_width
            assert 0 <= y <= height - cell_height
    return guid


@pytest.mark.parametrize(
    ("kind", "extended", "count"),
    [("sprites", False, 13), ("textures", False, 6), ("textures", True, 21)],
)
def test_fixture_pixels_and_metadata_survive_regeneration(
    tmp_path: Path, kind: str, extended: bool, count: int
) -> None:
    output = tmp_path / "fixtures-雪-é"
    first = run_generator(kind, output, extended=extended)
    assert first.returncode == 0, first.stdout + first.stderr
    assert str(output) in first.stdout
    images = sorted(output.glob("*.png"))
    assert len(images) == count
    folder_metadata = Path(f"{output}.meta") if kind == "textures" else None
    folder_before = b""
    if folder_metadata is not None:
        assert folder_metadata.is_file()
        folder_text = folder_metadata.read_text(encoding="utf-8")
        read_guid(folder_text)
        assert "folderAsset: yes\n" in folder_text
        folder_before = folder_metadata.read_bytes()
    identities: set[str] = set()
    for path in images:
        width, height, digest = PIXELS[path.name]
        with Image.open(path) as image:
            assert image.mode == "RGBA"
            assert image.size == (width, height)
            assert hashlib.sha256(image.tobytes()).hexdigest() == digest
        guid = assert_image_metadata(path, kind)
        assert guid not in identities
        identities.add(guid)
    before = {path.name: path.read_bytes() for path in output.iterdir()}
    second = run_generator(kind, output, extended=extended)
    assert second.returncode == 0, second.stdout + second.stderr
    assert {path.name: path.read_bytes() for path in output.iterdir()} == before
    if folder_metadata is not None:
        assert folder_metadata.read_bytes() == folder_before
    survivor = images[0].with_suffix(".png.meta")
    survivor.write_bytes(b"existing authored metadata\r\n")
    missing = images[1].with_suffix(".png.meta")
    missing.unlink()
    if folder_metadata is not None:
        folder_metadata.write_bytes(b"existing authored folder metadata\r\n")
    third = run_generator(kind, output, extended=extended)
    assert third.returncode == 0, third.stdout + third.stderr
    assert survivor.read_bytes() == b"existing authored metadata\r\n"
    assert missing.is_file()
    regenerated_guid = assert_image_metadata(images[1], kind)
    # The authored survivor deliberately has no GUID.
    other_guids = {
        read_guid(path.with_suffix(".png.meta").read_text(encoding="utf-8")) for path in images[2:]
    }
    assert regenerated_guid not in other_guids
    if folder_metadata is not None:
        assert folder_metadata.read_bytes() == b"existing authored folder metadata\r\n"
        folder_metadata.unlink()
        fourth = run_generator(kind, output, extended=extended)
        assert fourth.returncode == 0, fourth.stdout + fourth.stderr
        assert folder_metadata.is_file()
        recreated = folder_metadata.read_text(encoding="utf-8")
        read_guid(recreated)
        assert "folderAsset: yes\n" in recreated
    for name, original in before.items():
        if name not in (survivor.name, missing.name):
            assert (output / name).read_bytes() == original


def test_competing_metadata_creation_preserves_winner(tmp_path: Path) -> None:
    path = tmp_path / "fixture.png.meta"

    def write(index: int) -> bool:
        return write_metadata(path, str(index))

    with ThreadPoolExecutor(max_workers=8) as executor:
        outcomes = list(executor.map(write, range(32)))
    assert sum(outcomes) == 1
    assert path.read_text(encoding="utf-8") == str(outcomes.index(True))


def test_failed_metadata_encoding_leaves_no_destination_or_temporary_file(
    tmp_path: Path,
) -> None:
    path = tmp_path / "fixture.png.meta"
    with pytest.raises(UnicodeEncodeError):
        write_metadata(path, "prefix\n\ud800")
    assert list(tmp_path.iterdir()) == []
    assert write_metadata(path, "complete\n")
    assert path.read_bytes() == b"complete\r\n"


@pytest.mark.parametrize("phase", ["write", "close", "publish"])
def test_failed_metadata_io_leaves_no_destination_or_temporary_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch, phase: str
) -> None:
    path = tmp_path / "fixture.png.meta"
    original_factory = tempfile.NamedTemporaryFile

    def failing_stream(**_parameters: object) -> object:
        stream = original_factory(
            mode="w", encoding="utf-8", newline="\r\n", dir=tmp_path, delete=False
        )
        original_write, original_close = stream.write, stream.close

        def fail_write(content: str) -> int:
            original_write(content[:3])
            raise OSError("synthetic metadata write failure")

        def fail_close() -> None:
            original_close()
            raise OSError("synthetic metadata close failure")

        monkeypatch.setattr(
            stream,
            "write" if phase == "write" else "close",
            fail_write if phase == "write" else fail_close,
        )
        return stream

    def fail_publish(source: Path, destination: Path) -> None:
        assert source.read_bytes() == b"complete\r\n"
        assert destination == path
        raise OSError("synthetic metadata publish failure")

    with monkeypatch.context() as faults:
        if phase == "publish":
            faults.setattr(os, "link", fail_publish)
        else:
            faults.setattr(tempfile, "NamedTemporaryFile", failing_stream)
        with pytest.raises(OSError, match=f"synthetic metadata {phase} failure"):
            write_metadata(path, "complete\n")
        assert list(tmp_path.iterdir()) == []
    assert write_metadata(path, "complete\n")
    assert path.read_bytes() == b"complete\r\n"


@pytest.mark.parametrize("kind", ["sprites", "textures"])
def test_invalid_output_preserves_existing_file(tmp_path: Path, kind: str) -> None:
    output = tmp_path / "occupied"
    output.write_bytes(b"retain")
    result = run_generator(kind, output)
    assert result.returncode != 0
    assert output.read_bytes() == b"retain"
