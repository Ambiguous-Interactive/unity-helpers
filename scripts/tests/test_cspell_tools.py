"""Pin spelling tool behavior, file preservation, and executable diagnostics."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import add_cspell_word as add
import cspell_config as model
import lint_cspell_config as lint


def config(tmp_path: Path, value: object, newline: str = "\n") -> Path:
    path = tmp_path / "cspell.json"
    path.write_bytes(
        (json.dumps(value, ensure_ascii=False, indent=2) + "\n")
        .replace("\n", newline)
        .encode("utf-8")
    )
    return path


def base() -> dict[str, object]:
    return {
        "caseSensitive": False,
        "dictionaryDefinitions": [
            {"name": name, "words": []} for name in add.BUCKETS if name != "words"
        ],
        "words": [],
        "unrelated": {"keep": [1, True, "café"]},
    }


@pytest.fixture(autouse=True)
def isolate_formatter(monkeypatch: pytest.MonkeyPatch) -> None:
    def omit_format(_path: Path) -> None:
        return

    monkeypatch.setattr(add, "format_config", omit_format)


@pytest.mark.parametrize("bucket", add.BUCKETS)
@pytest.mark.parametrize("newline", ["\n", "\r\n"])
def test_add_retains_config_and_line_endings(bucket: str, newline: str, tmp_path: Path) -> None:
    path = config(tmp_path, base(), newline)
    assert add.add_words(path, bucket, ["NewWord", "café"]) == 0
    raw, updated = model.read_config(path)
    selected = "root:words" if bucket == "words" else f"dictionary:{bucket}"
    assert next(group.words() for group in model.buckets(updated) if group.name == selected) == [
        "NewWord",
        "café",
    ]
    assert updated["unrelated"] == {"keep": [1, True, "café"]}
    assert ("\r\n" in raw) == (newline == "\r\n")


@pytest.mark.parametrize("words", [["UNH"], ["unh"], ["UnH", "UNH"]])
def test_existing_target_is_a_byte_preserving_noop(words: list[str], tmp_path: Path) -> None:
    value = base()
    value["words"] = ["UNH"]
    path = config(tmp_path, value)
    before = path.read_bytes()
    assert add.add_words(path, "words", words) == 0
    assert path.read_bytes() == before


@pytest.mark.parametrize("dry_run", [False, True])
def test_cross_bucket_conflict_preserves_all_bytes(dry_run: bool, tmp_path: Path) -> None:
    value = base()
    value["words"] = ["UNH"]
    path = config(tmp_path, value)
    before = path.read_bytes()
    assert add.add_words(path, "tech-terms", ["ValidNew", "unh"], dry_run=dry_run) == 1
    assert path.read_bytes() == before


@pytest.mark.parametrize("word", ["", "two words", "x\t", "\nx", "x\u00a0y", "\ufeffx", "x\u2028y"])
def test_invalid_word_preserves_prior_accepted_candidates(word: str, tmp_path: Path) -> None:
    path = config(tmp_path, base())
    before = path.read_bytes()
    assert add.add_words(path, "words", ["ValidNew", word]) == 1
    assert path.read_bytes() == before


def test_dry_run_does_not_write_or_format(tmp_path: Path) -> None:
    path = config(tmp_path, base())
    before = path.read_bytes()
    assert add.add_words(path, "words", ["NewWord"], dry_run=True) == 0
    assert path.read_bytes() == before


def test_repeated_new_word_is_added_once(tmp_path: Path) -> None:
    path = config(tmp_path, base())
    assert add.add_words(path, "words", ["NewWord", "newword", "Another"]) == 0
    assert model.read_config(path)[1]["words"] == ["NewWord", "Another"]


@pytest.mark.parametrize("case_sensitive", [True, False, None])
@pytest.mark.parametrize("fix", [False, True])
def test_case_redundancy_policy(case_sensitive: bool | None, fix: bool, tmp_path: Path) -> None:
    value = base()
    if case_sensitive is None:
        del value["caseSensitive"]
    else:
        value["caseSensitive"] = case_sensitive
    value["words"] = ["Keep", "KEEP", "Other", "keep"]
    path = config(tmp_path, value)
    before = path.read_bytes()
    expected = 1 if case_sensitive is False and not fix else 0
    assert lint.check(path, fix=fix) == expected
    if fix and case_sensitive is False:
        assert model.read_config(path)[1]["words"] == ["Keep", "Other"]
    else:
        assert path.read_bytes() == before


@pytest.mark.parametrize("fix", [False, True])
def test_cross_dictionary_duplicates_only_warn(
    fix: bool, tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    value = base()
    value["words"] = ["Shared"]
    dictionaries = cast("list[dict[str, object]]", value["dictionaryDefinitions"])
    dictionaries[0]["words"] = ["shared"]
    path = config(tmp_path, value)
    before = path.read_bytes()
    assert lint.check(path, fix=fix) == 0
    assert path.read_bytes() == before
    output = capsys.readouterr()
    assert "dictionary:unity-terms, root:words" in output.err
    assert "1 warning(s)" in output.out


def test_check_summary_names_every_enforced_rule(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = config(tmp_path, base())
    assert lint.check(path) == 0
    output = capsys.readouterr().out
    assert "Case-redundant entries (blocking)" in output
    assert "Cross-dictionary duplicates (warning only)" in output


@pytest.mark.parametrize(
    "value",
    [
        None,
        [],
        {"words": False},
        {"words": [False]},
        {"dictionaryDefinitions": {}},
        {"dictionaryDefinitions": [False]},
        {"dictionaryDefinitions": [{"name": False}]},
        {"dictionaryDefinitions": [{"name": "words", "words": "bad"}]},
    ],
)
def test_invalid_schema_is_never_rewritten(value: object, tmp_path: Path) -> None:
    path = config(tmp_path, value)
    before = path.read_bytes()
    assert lint.check(path, fix=True) == 1
    assert add.add_words(path, "words", ["NewWord"]) == 1
    assert path.read_bytes() == before


def test_unknown_bucket_is_rejected(tmp_path: Path) -> None:
    path = config(tmp_path, base())
    before = path.read_bytes()
    assert add.add_words(path, "unknown", ["NewWord"]) == 1
    assert path.read_bytes() == before


def test_absent_dictionary_bucket_is_rejected(tmp_path: Path) -> None:
    path = config(tmp_path, {"words": []})
    assert add.add_words(path, "unity-terms", ["NewWord"]) == 1


@pytest.mark.parametrize("raw", [b"{invalid", b"\xff", b"[false]"])
def test_malformed_file_is_never_rewritten(raw: bytes, tmp_path: Path) -> None:
    path = tmp_path / "cspell.json"
    path.write_bytes(raw)
    assert lint.check(path, fix=True) == 1
    assert add.add_words(path, "words", ["NewWord"]) == 1
    assert path.read_bytes() == raw


def test_cli_runs_from_another_directory(tmp_path: Path) -> None:
    path = config(tmp_path, base())
    result = subprocess.run(
        [
            sys.executable,
            str(model.ROOT / "scripts/add_cspell_word.py"),
            "--config",
            str(path),
            "--dry-run",
            "words",
            "NewWord",
        ],
        cwd=tmp_path,
        capture_output=True,
        encoding="utf-8",
        timeout=20,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "Would add" in result.stdout
    assert model.read_config(path)[1]["words"] == []


@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize("dry_run", [False, True])
def test_cli_keeps_unicode_output_under_legacy_windows_encoding(
    legacy: bool, dry_run: bool, tmp_path: Path
) -> None:
    if legacy:
        node = shutil.which("node")
        if node is None:
            pytest.skip("Node is required for its compatibility entry point")
        command = [node, str(model.ROOT / "scripts/add-cspell-word.js")]
    else:
        command = [sys.executable, str(model.ROOT / "scripts/add_cspell_word.py")]
    path = config(tmp_path, base())
    original = path.read_bytes()
    arguments = ["--config", str(path), *(["--dry-run"] if dry_run else []), "words", "λ"]
    result = subprocess.run(
        [*command, *arguments],
        cwd=tmp_path,
        env=dict(os.environ, PYTHONUTF8="0", PYTHONIOENCODING="cp1252"),
        capture_output=True,
        encoding="utf-8",
        timeout=30,
        check=False,
    )
    assert result.returncode == 0, result.stderr
    assert "λ" in result.stdout
    if dry_run:
        assert path.read_bytes() == original
    else:
        assert model.read_config(path)[1]["words"] == ["λ"]


@pytest.mark.parametrize("arguments", [[], ["unknown", "Word"], ["words"]])
def test_word_cli_retains_usage_exit_one(arguments: list[str]) -> None:
    assert add.main(arguments) == 1


@pytest.mark.parametrize(
    "arguments",
    [["--dry-run", "words", "First", "Second"], ["words", "First", "--dry-run", "Second"]],
)
def test_word_cli_accepts_dry_run_among_words(arguments: list[str], tmp_path: Path) -> None:
    path = config(tmp_path, base())
    before = path.read_bytes()
    assert add.main(["--config", str(path), *arguments]) == 0
    assert path.read_bytes() == before
