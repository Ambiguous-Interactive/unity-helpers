"""Exercise every retained Unity naming fixture and lexical, Git and launcher boundaries."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_unity_file_naming as naming

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / "scripts/tests/fixtures"


def test_fixture_catalog_has_positive_and_negative_controls() -> None:
    for group in ("should-pass", "should-fail"):
        assert list((FIXTURES / group).glob("*.cs")), group


@pytest.mark.parametrize(
    "path", sorted((FIXTURES / "should-pass").glob("*.cs")), ids=lambda p: p.name
)
def test_retained_passing_fixtures(path: Path, tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    shutil.copyfile(path, runtime / path.name)
    assert naming.main(["--repo-root", str(tmp_path), "--verbose"]) == 0


@pytest.mark.parametrize(
    "path", sorted((FIXTURES / "should-fail").glob("*.cs")), ids=lambda p: p.name
)
def test_retained_failing_fixtures(path: Path, tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    shutil.copyfile(path, runtime / path.name)
    assert naming.main(["--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize("base", sorted(naming.UNITY_BASES))
@pytest.mark.parametrize("qualifier", ["", "UnityEngine.", "global::UnityEngine.", "global::"])
def test_all_direct_bases_require_exact_filename(base: str, qualifier: str) -> None:
    generic = "<A>" if base == "ScriptableObjectSingleton" else ""
    source = f"namespace N {{ public class A : {qualifier}{base}{generic} {{ }} }}"
    assert not naming.analyze(source, "A.cs")
    result = naming.analyze(source, "a.cs")
    assert len(result) == 1
    assert "UNH006" in result[0][1]


@pytest.mark.parametrize("ending", ["\n", "\r\n", "\r"])
def test_multiline_inheritance_is_detected_without_indentation_assumptions(
    ending: str,
) -> None:
    source = ending.join(
        (
            "namespace N",
            "{",
            "        public sealed class A",
            "            : MonoBehaviour",
            "        { }",
            "}",
        )
    )
    assert "UNH006" in naming.analyze(source, "Wrong.cs")[0][1]


@pytest.mark.parametrize(
    "noise",
    [
        "// public class Wrong : MonoBehaviour { }\n",
        "/* public class Wrong : MonoBehaviour { } */",
        'const string S = "public class Wrong : MonoBehaviour { }";',
        'const string S = @"\npublic class Wrong : MonoBehaviour { }\n";',
        'const string S = """\npublic class Wrong : MonoBehaviour { }\n""";',
    ],
)
def test_comments_and_literals_do_not_create_types(noise: str) -> None:
    assert not naming.analyze(noise + "\npublic class A : MonoBehaviour { }", "A.cs")


def test_suppression_requires_actual_comment() -> None:
    declaration = "public class A : MonoBehaviour { }"
    assert not naming.analyze("// lint-disable unity-file-naming\n" + declaration, "Wrong.cs")
    source = 'const string S = "// lint-disable unity-file-naming";\n' + declaration
    assert "UNH006" in naming.analyze(source, "Wrong.cs")[0][1]


def test_generic_argument_is_not_a_unity_base() -> None:
    assert not naming.analyze("class Other : List<MonoBehaviour> { }", "Other.cs")


def test_nested_unity_type_does_not_hide_behind_indentation() -> None:
    source = "class Host { class Child : MonoBehaviour { } }"
    assert "UNH006" in naming.analyze(source, "Host.cs")[0][1]


def test_multiple_types_and_multiline_location_are_reported() -> None:
    source = "\n\nclass A\n: MonoBehaviour { }\nclass B : ScriptableObject { }"
    findings = naming.analyze(source, "A.cs")
    assert len(findings) == 1
    assert findings[0][0] == 3
    assert "UNH005" in findings[0][1]
    assert "A, B" in findings[0][1]


def test_empty_or_missing_corpus_cannot_pass(tmp_path: Path) -> None:
    assert naming.main(["--repo-root", str(tmp_path)]) == 1
    (tmp_path / "Runtime").mkdir()
    assert naming.main(["--repo-root", str(tmp_path)]) == 1


def test_unicode_bom_and_invalid_encoding(tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    source = runtime / "Étoile.cs"
    source.write_text("class Étoile : MonoBehaviour { }", encoding="utf-8-sig")
    assert naming.main(["--repo-root", str(tmp_path)]) == 0
    source.write_bytes(b"\xff\xff")
    assert naming.main(["--repo-root", str(tmp_path)]) == 1


def git(repository: Path, *args: str) -> None:
    executable = shutil.which("git")
    assert executable is not None
    subprocess.run([executable, "-C", str(repository), *args], check=True, capture_output=True)


def test_staged_selection_is_anchored_and_includes_renames(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    git(tmp_path, "init", "-q")
    git(tmp_path, "config", "user.name", "Naming Fixture")
    git(tmp_path, "config", "user.email", "fixture@example.invalid")
    git(tmp_path, "config", "commit.gpgsign", "true")
    git(tmp_path, "config", "gpg.program", "naming-fixture-missing-signing-program")
    source = tmp_path / "A.cs"
    source.write_text("class A : MonoBehaviour { }", encoding="utf-8")
    git(tmp_path, "add", "A.cs")
    git(
        tmp_path,
        "-c",
        "core.hooksPath=/dev/null",
        "-c",
        "commit.gpgsign=false",
        "commit",
        "-qm",
        "fixture",
    )
    git(tmp_path, "mv", "A.cs", "Wrong.cs")
    other = tmp_path / "other"
    other.mkdir()
    monkeypatch.chdir(other)
    assert [p.name for p in naming.files_to_check(tmp_path, staged_only=True)] == ["Wrong.cs"]
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == 1
    assert naming.main(["--repo-root", str(other), "--staged-only"]) == 1


def test_empty_index_is_valid_but_git_failure_is_not(tmp_path: Path) -> None:
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == 1
    git(tmp_path, "init", "-q")
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == 0


def test_missing_staged_file_refuses_a_vacuous_pass(tmp_path: Path) -> None:
    git(tmp_path, "init", "-q")
    source = tmp_path / "A.cs"
    source.write_text("class A : MonoBehaviour { }", encoding="utf-8")
    git(tmp_path, "add", "A.cs")
    source.unlink()
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == 1


def test_generated_and_linked_entries_do_not_expand_scope(tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    (runtime / "A.cs").write_text("class A : MonoBehaviour { }", encoding="utf-8")
    excluded = runtime / "obj"
    excluded.mkdir()
    (excluded / "Bad.cs").write_text("class Wrong : MonoBehaviour { }", encoding="utf-8")
    assert naming.main(["--repo-root", str(tmp_path)]) == 0


def directory_redirect(link: Path, target: Path) -> None:
    if os.name == "nt":
        command = shutil.which("cmd")
        assert command is not None
        subprocess.run(
            [command, "/c", "mklink", "/J", str(link), str(target)], capture_output=True, check=True
        )
    else:
        link.symlink_to(target, target_is_directory=True)


def test_linked_root_is_refused(tmp_path: Path) -> None:
    real = tmp_path / "real"
    real.mkdir()
    (real / "A.cs").write_text("class A : MonoBehaviour { }", encoding="utf-8")
    directory_redirect(tmp_path / "Runtime", real)
    assert naming.main(["--repo-root", str(tmp_path)]) == 1


def test_linked_children_are_not_traversed(tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    (runtime / "A.cs").write_text("class A : MonoBehaviour { }", encoding="utf-8")
    real = tmp_path / "unselected"
    real.mkdir()
    (real / "Wrong.cs").write_text("class B : MonoBehaviour { }", encoding="utf-8")
    directory_redirect(runtime / "redirect", real)
    assert naming.main(["--repo-root", str(tmp_path)]) == 0


def test_staged_linked_parent_is_refused(tmp_path: Path) -> None:
    git(tmp_path, "init", "-q")
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    (runtime / "A.cs").write_text("class A : MonoBehaviour { }", encoding="utf-8")
    git(tmp_path, "add", "Runtime/A.cs")
    saved = tmp_path / "saved"
    runtime.rename(saved)
    directory_redirect(runtime, saved)
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == 1


def test_staged_unicode_paths_are_utf8(tmp_path: Path) -> None:
    repository = tmp_path / "répertoire avec espaces"
    repository.mkdir()
    git(repository, "init", "-q")
    (repository / "Étoile.cs").write_text("class Étoile : MonoBehaviour { }", encoding="utf-8")
    git(repository, "add", "Étoile.cs")
    assert naming.main(["--repo-root", str(repository), "--staged-only"]) == 0
    (repository / "Étoile.cs").write_text("class Autre : MonoBehaviour { }", encoding="utf-8")
    assert naming.main(["--repo-root", str(repository), "--staged-only"]) == 0
    git(repository, "add", "Étoile.cs")
    assert naming.main(["--repo-root", str(repository), "--staged-only"]) == 1


@pytest.mark.parametrize("staged_valid", [False, True])
def test_staged_check_reads_index_instead_of_unstaged_corrections(
    tmp_path: Path, staged_valid: bool
) -> None:
    git(tmp_path, "init", "-q")
    source = tmp_path / "A.cs"
    valid = "class A : MonoBehaviour { }"
    invalid = "class Wrong : MonoBehaviour { }"
    source.write_text(valid if staged_valid else invalid, encoding="utf-8-sig")
    git(tmp_path, "add", "A.cs")
    source.write_text(invalid if staged_valid else valid, encoding="utf-8")
    assert naming.main(["--repo-root", str(tmp_path), "--staged-only"]) == (
        0 if staged_valid else 1
    )


@pytest.mark.parametrize("valid", [False, True])
def test_direct_cli_checks_physical_sources_and_returns_status(tmp_path: Path, valid: bool) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    (runtime / "A.cs").write_text(
        "class A : MonoBehaviour { }" if valid else "class Wrong : MonoBehaviour { }",
        encoding="utf-8",
    )
    result = subprocess.run(
        [
            sys.executable,
            str(ROOT / "scripts/lint_unity_file_naming.py"),
            "--repo-root",
            str(tmp_path),
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == (0 if valid else 1), result.stdout + result.stderr
    assert ("UNH006" in result.stdout) is not valid


@pytest.mark.parametrize(
    ("test_filter", "expected_exit", "expected_output"),
    [
        ("test_fixture_catalog_has_positive_and_negative_controls", 0, "1 passed"),
        ("fixture_that_does_not_exist", 5, "deselected"),
    ],
    ids=["selected", "empty"],
)
def test_compatibility_test_launcher_preserves_selected_results(
    test_filter: str, expected_exit: int, expected_output: str
) -> None:
    powershell = shutil.which("pwsh")
    assert powershell is not None
    result = subprocess.run(
        [
            powershell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-unity-file-naming.ps1"),
            "-TestFilter",
            test_filter,
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == expected_exit, result.stdout + result.stderr
    assert expected_output in result.stdout


def test_compatibility_launcher_from_another_directory(tmp_path: Path) -> None:
    runtime = tmp_path / "Runtime"
    runtime.mkdir()
    source = runtime / "Wrong.cs"
    source.write_text("class A : MonoBehaviour { }", encoding="utf-8")
    powershell = shutil.which("pwsh")
    assert powershell is not None
    launcher = ROOT / "scripts/lint-unity-file-naming.ps1"
    result = subprocess.run(
        [powershell, "-NoProfile", "-File", str(launcher), "-VerboseOutput"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "UNH006" in result.stdout
    source.rename(runtime / "A.cs")
    result = subprocess.run(
        [powershell, "-NoProfile", "-File", str(launcher), "-RepoRoot", str(tmp_path)],
        cwd=ROOT,
        capture_output=True,
        text=True,
        check=False,
    )
    assert result.returncode == 0, result.stdout + result.stderr
