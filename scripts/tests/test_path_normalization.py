"""Controls for the package's manual path-normalization ban."""

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_path_normalization import ANCHORS, IMPLEMENTATION, find_manual_normalizations, scan


@pytest.mark.parametrize(
    "source",
    [
        r"path.Replace('\\', '/')",
        r'path.Replace("\\", "/")',
        r'path.Replace("\\", "/", StringComparison.Ordinal)',
        r"path.@Replace('\\', '/')",
        r'path?.Replace(@"\", @"/")',
        r"path.Replace ( '\\' /* slash */ , '/' )",
        r"path.Replace('\u005c', '\u002f')",
        "var result = $\"{path.Replace('\\\\', '/')}\";",
    ],
)
def test_manual_normalization_is_rejected(source: str) -> None:
    assert find_manual_normalizations(source) == [1]


@pytest.mark.parametrize(
    "source",
    [
        "path.SanitizePath()",
        "PathHelper.Sanitize(path)",
        r"path.Replace('/', '\\')",
        r"path.Replace('\\', '_')",
        r"// path.Replace('\\', '/')",
        r"/* path.Replace('\\', '/') */",
        r"""var text = "path.Replace('\\', '/')";""",
    ],
)
def test_other_code_and_literal_text_are_accepted(source: str) -> None:
    assert find_manual_normalizations(source) == []


def populate(root: Path) -> None:
    for relative in ANCHORS.values():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            r"path.Replace('\\', '/')" if relative == IMPLEMENTATION else "", encoding="utf-8"
        )


def test_scan_enforces_new_files_and_only_shared_implementation_is_exempt(tmp_path: Path) -> None:
    populate(tmp_path)
    assert scan(tmp_path) == 0
    (tmp_path / "Editor/New.cs").write_text(r"path.Replace('\\', '/')", encoding="utf-8")
    assert scan(tmp_path) == 1


@pytest.mark.parametrize("missing_root", ["Runtime", "Editor", "Tests", "Generator~"])
def test_missing_census_cannot_pass(tmp_path: Path, missing_root: str) -> None:
    populate(tmp_path)
    (tmp_path / ANCHORS[missing_root]).unlink()
    with pytest.raises(ValueError, match="census or anchor"):
        scan(tmp_path)


def test_shared_implementation_exception_cannot_grow(tmp_path: Path) -> None:
    populate(tmp_path)
    (tmp_path / IMPLEMENTATION).write_text(
        r"path.Replace('\\', '/'); path.Replace('\\', '/')", encoding="utf-8"
    )
    with pytest.raises(ValueError, match="exactly one"):
        scan(tmp_path)


@pytest.mark.parametrize("has_violation", [False, True])
def test_physical_cli_reports_clean_and_rejected_corpora(
    tmp_path: Path, has_violation: bool
) -> None:
    populate(tmp_path)
    scripts = tmp_path / "scripts"
    scripts.mkdir()
    source_scripts = Path(__file__).resolve().parents[1]
    for name in ("lint_path_normalization.py", "lint_comparison_direction.py", "cli_encoding.py"):
        shutil.copyfile(source_scripts / name, scripts / name)
    if has_violation:
        (tmp_path / "Editor/New.cs").write_text(r"path.Replace('\\', '/')", encoding="utf-8")
    result = subprocess.run(
        [sys.executable, str(scripts / "lint_path_normalization.py")],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == int(has_violation), result.stdout + result.stderr
    assert ("Editor/New.cs:1:" in result.stdout) == has_violation
    for root in ANCHORS:
        assert f"Path normalization examined {root}:" in result.stdout
