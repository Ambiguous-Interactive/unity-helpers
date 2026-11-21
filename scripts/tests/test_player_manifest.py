"""Test nullable consumer manifest publication and retained-input checks."""

from __future__ import annotations

import hashlib
import json
import os
import sys
from pathlib import Path
from typing import TYPE_CHECKING

import pytest

if TYPE_CHECKING:
    from contextlib import ExitStack

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "unity"))
import player_manifest


@pytest.fixture
def player(tmp_path: Path) -> Path:
    root = tmp_path / "player"
    for name in (
        "UnityPlayer.dll",
        "GameAssembly.dll",
        "IndependentConsumer.exe",
        "IndependentConsumer_Data/il2cpp_data/Metadata/global-metadata.dat",
    ):
        file = root / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_bytes(name.encode("utf-8"))
    return root


def run_cli(monkeypatch: pytest.MonkeyPatch, *args: str) -> int:
    monkeypatch.setattr(sys, "argv", ["player_manifest.py", *args])
    return player_manifest.main()


def create(
    player: Path,
    manifest: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> str:
    assert run_cli(monkeypatch, "create", str(player), str(manifest)) == 0
    output = capsys.readouterr()
    assert output.err == ""
    assert output.out.strip() == hashlib.sha256(manifest.read_bytes()).hexdigest()
    return output.out.strip()


@pytest.mark.parametrize("modified", ["manifest", "player"])
def test_persistent_change_after_inventory_never_reports_success(
    modified: str,
    player: Path,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    manifest = tmp_path / "manifest.json"
    digest = create(player, manifest, monkeypatch, capsys)
    original = player_manifest._inventory  # pyright: ignore[reportPrivateUsage]
    updated_path = manifest if modified == "manifest" else player / "UnityPlayer.dll"
    before = updated_path.read_bytes()
    attempted = False

    def inventory(
        root: Path, retained: ExitStack
    ) -> tuple[dict[str, str], dict[Path, os.stat_result]]:
        nonlocal attempted
        result = original(root, retained)
        attempted = True
        if os.name == "nt":
            with pytest.raises(PermissionError):
                updated_path.write_bytes(before + b" ")
        else:
            updated_path.write_bytes(before + b" ")
        return result

    monkeypatch.setattr(player_manifest, "_inventory", inventory)
    status = run_cli(monkeypatch, "verify", str(player), str(manifest), "--expected-sha", digest)
    assert attempted
    output = capsys.readouterr()
    assert output.out == ""
    if os.name == "nt":
        assert status == 0
        assert updated_path.read_bytes() == before
    else:
        assert status == 1
        assert "Actual player" in output.err
        assert updated_path.read_bytes() == before + b" "


def test_inventory_is_sorted_and_refuses_a_second_publication(
    player: Path,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    manifest = tmp_path / "manifest.json"
    digest = create(player, manifest, monkeypatch, capsys)
    payload = manifest.read_bytes()
    paths = sorted(
        file.relative_to(player).as_posix() for file in player.rglob("*") if file.is_file()
    )
    assert json.loads(payload) == [
        {"path": name, "sha256": hashlib.sha256((player / name).read_bytes()).hexdigest()}
        for name in paths
    ]
    assert (
        run_cli(monkeypatch, "verify", str(player), str(manifest), "--expected-sha", digest.upper())
        == 0
    )
    assert run_cli(monkeypatch, "create", str(player), str(manifest)) == 1
    assert "Refusing existing actual player manifest" in capsys.readouterr().err
    assert manifest.read_bytes() == payload


def test_wrong_checkout_leaves_no_manifest(
    player: Path,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    manifest = tmp_path / "manifest.json"
    monkeypatch.setenv("GITHUB_SHA", "0" * 40)
    assert run_cli(monkeypatch, "create", str(player), str(manifest)) == 1
    assert "Player manifest checkout differs" in capsys.readouterr().err
    assert not manifest.exists()


@pytest.mark.skipif(sys.platform != "win32", reason="Windows CRT descriptor limit")
def test_player_inventory_can_exceed_the_windows_descriptor_limit(
    player: Path,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    extra = player / "extra"
    extra.mkdir()
    for index in range(8200):
        (extra / f"{index}.bin").touch()
    manifest = tmp_path / "manifest.json"
    digest = create(player, manifest, monkeypatch, capsys)
    assert len(json.loads(manifest.read_bytes())) == 8204
    assert run_cli(monkeypatch, "verify", str(player), str(manifest), "--expected-sha", digest) == 0
    assert capsys.readouterr().err == ""
