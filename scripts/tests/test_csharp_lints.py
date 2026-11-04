"""Hold migrated C# checks to real subjects, lexical controls and their legacy launchers."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import csharp_sources
import lint_conditional_call_chains as chains
import lint_license_headers as licenses
import lint_odin_undo_safety as odin

ROOT = Path(__file__).resolve().parents[2]


def corpus(tmp_path: Path) -> Path:
    tmp_path.mkdir(parents=True, exist_ok=True)
    for name in ("Runtime", "Editor", "Tests", "Styles"):
        (tmp_path / name).mkdir()
    return tmp_path


def conditional(body: str, *, inline: bool = False, modifiers: str = "public static") -> str:
    separator = " " if inline else "\n"
    return f'[Conditional("DEBUG")]{separator}{modifiers} void Outer() {{ {body} }}\n'


@pytest.mark.parametrize(
    "header", ["// MIT License", "// mit license", "// No license header required", ""]
)
def test_license_accepts_existing_header_contract(tmp_path: Path, header: str) -> None:
    path = tmp_path / "A.cs"
    path.write_text(header, encoding="utf-8")
    assert licenses.header_is_valid(path)


@pytest.mark.parametrize("ending", ["\n", "\r\n", "\r"])
@pytest.mark.parametrize("line", [1, 20, 21])
def test_license_reads_exactly_twenty_lines(tmp_path: Path, ending: str, line: int) -> None:
    path = tmp_path / "A.cs"
    path.write_bytes((ending * (line - 1) + "// MIT License" + ending).encode("utf-8-sig"))
    assert licenses.header_is_valid(path) == (line <= 20)


@pytest.mark.parametrize("inline", [False, True])
@pytest.mark.parametrize("modifiers", ["public static", "private", "static", ""])
def test_conditional_calls_include_same_line_bodies_and_attributes(
    inline: bool, modifiers: str
) -> None:
    source = (
        conditional("Inner();", inline=inline, modifiers=modifiers)
        + '[Conditional("DEBUG")]\npublic static void Inner() { }'
    )
    methods, errors = chains.methods_in(source, "Runtime/A.cs")
    assert not errors
    assert len(methods) == 2
    findings = chains.violations(methods)
    assert len(findings) == 1
    assert "'Outer' calls [Conditional] method 'Inner'" in findings[0]


@pytest.mark.parametrize(
    "attribute",
    [
        "Conditional",
        "ConditionalAttribute",
        "System.Diagnostics.Conditional",
        "global::System.Diagnostics.ConditionalAttribute",
    ],
)
def test_conditional_multiline_attributes_comments_and_generic_calls(attribute: str) -> None:
    source = f'[{attribute}(\n"DEBUG"\n)]\n/* ignored {{ }} */\npublic static void Outer()\n{{\n Inner<int>();\n}}\n[Conditional("DEBUG")]\npublic static void Inner<T>() {{ }}'
    methods, errors = chains.methods_in(source, "Runtime/A.cs")
    assert not errors
    assert len(methods) == 2
    assert len(chains.violations(methods)) == 1


@pytest.mark.parametrize(
    "noise",
    [
        "// Inner(); }",
        "/* Inner(); } */",
        'string x = "Inner(); }";',
        'string x = @"Inner(); }";',
        'string x = @$"Inner(); }}";',
        'string x = """Inner(); }""";',
        "char x = '}';",
    ],
)
def test_literals_and_comments_cannot_create_calls_or_truncate_a_body(noise: str) -> None:
    tail = '[Conditional("DEBUG")]\npublic static void Inner() { }'
    methods, errors = chains.methods_in(conditional(noise + "\n") + tail, "Runtime/A.cs")
    assert not errors
    assert len(methods) == 2
    assert not chains.violations(methods)
    methods, errors = chains.methods_in(conditional(noise + "\nInner();") + tail, "Runtime/A.cs")
    assert not errors
    assert len(chains.violations(methods)) == 1


def test_external_receivers_and_nonconditional_names_are_not_chains() -> None:
    source = (
        conditional(
            'Debug.LogError("x"); UnityEngine.Debug.LogError("x"); Debug.unityLogger.LogError("x"); Other();'
        )
        + '[Conditional("DEBUG")]\npublic static void LogError() { }'
    )
    methods, errors = chains.methods_in(source, "Runtime/A.cs")
    assert not errors
    assert len(methods) == 2
    assert not chains.violations(methods)


def test_recursive_conditional_calls_are_refused() -> None:
    methods, errors = chains.methods_in(conditional("Outer();"), "Runtime/A.cs")
    assert not errors
    assert len(chains.violations(methods)) == 1


@pytest.mark.parametrize(
    "source",
    [
        '[Conditional("DEBUG")]\npublic static void Missing();',
        '[Conditional("DEBUG")]\npublic static void Missing() {',
    ],
)
def test_unparsed_conditional_methods_are_not_reported_clean(source: str) -> None:
    methods, errors = chains.methods_in(source, "Runtime/A.cs")
    assert not methods
    assert len(errors) == 1


def test_comment_attributes_do_not_create_subjects() -> None:
    source = '// [Conditional("DEBUG")]\npublic static void A() {}\n/* [Conditional("DEBUG")] */\nprivate static void B() {}'
    methods, errors = chains.methods_in(source, "Runtime/A.cs")
    assert not methods
    assert not errors


def test_corpus_requires_all_roots_and_nonzero_files(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="root not found"):
        csharp_sources.source_files(tmp_path, licenses.SOURCE_ROOTS)
    corpus(tmp_path)
    with pytest.raises(ValueError, match="no C# files"):
        csharp_sources.source_files(tmp_path, licenses.SOURCE_ROOTS)
    (tmp_path / "Runtime/A.cs").write_text("// MIT License", encoding="utf-8")
    assert len(csharp_sources.source_files(tmp_path, licenses.SOURCE_ROOTS)) == 1
    assert chains.main(["--repo-root", str(tmp_path)]) == 1
    assert licenses.main(["--repo-root", str(tmp_path)]) == 0


def test_generated_and_linked_trees_are_not_followed(tmp_path: Path) -> None:
    root = corpus(tmp_path)
    (root / "Runtime/A.cs").write_text("// MIT License", encoding="utf-8")
    (root / "Runtime/obj").mkdir()
    (root / "Runtime/obj/Ignored.cs").write_text("bad header", encoding="utf-8")
    outside = root / "outside"
    outside.mkdir()
    (outside / "Unowned.cs").write_text("bad header", encoding="utf-8")
    try:
        (root / "Runtime/link").symlink_to(outside, target_is_directory=True)
    except (OSError, NotImplementedError) as error:
        pytest.skip(f"Symbolic links unavailable: {error}")
    assert csharp_sources.source_files(root, licenses.SOURCE_ROOTS) == [root / "Runtime/A.cs"]


@pytest.mark.parametrize("kind", ["license", "conditional"])
@pytest.mark.parametrize("legacy", [False, True])
@pytest.mark.parametrize("valid", [False, True])
def test_direct_and_legacy_cli_preserve_status_and_unicode_paths(
    tmp_path: Path, kind: str, legacy: bool, valid: bool
) -> None:
    root = corpus(tmp_path / "repository ü spaces")
    source = ("// MIT License\n" if valid else "") + conditional("" if valid else "Outer();")
    (root / "Runtime/名字 ü.cs").write_text(source, encoding="utf-8")
    if legacy:
        shell = shutil.which("pwsh")
        assert shell is not None
        filename = (
            "lint-license-headers.ps1" if kind == "license" else "lint-conditional-call-chains.ps1"
        )
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts" / filename),
            "-RepoRoot",
            str(root),
            "-VerboseOutput",
        ]
    else:
        filename = (
            "lint_license_headers.py" if kind == "license" else "lint_conditional_call_chains.py"
        )
        command = [
            sys.executable,
            str(ROOT / "scripts" / filename),
            "--repo-root",
            str(root),
            "--verbose",
        ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, encoding="utf-8", check=False
    )
    assert result.returncode == (0 if valid else 1), result.stdout + result.stderr


def test_invalid_encoding_is_an_error_not_an_empty_corpus(tmp_path: Path) -> None:
    root = corpus(tmp_path)
    (root / "Runtime/A.cs").write_bytes(b"\xff\xfe\x80")
    assert licenses.main(["--repo-root", str(root)]) == 1
    assert chains.main(["--repo-root", str(root)]) == 1


@pytest.mark.parametrize(
    "source",
    [
        "var values = (UnityEngine.Object[])Property.Tree.WeakTargets;",
        "var values = Property.Tree.WeakTargets.Cast<UnityEngine.Object>().ToArray();",
        "var value = Property.Tree.WeakTargets[0] as UnityEngine.Object;",
        'Undo.RecordObjects((UnityEngine.Object[])Property.Tree.WeakTargets, "Change");',
    ],
)
def test_odin_forbidden_casts_are_reported(source: str) -> None:
    findings = odin.analyze(source)
    assert findings
    assert all("UNH006" in message for _, message in findings)


@pytest.mark.parametrize(
    "source",
    [
        "if (targets[i] is UnityEngine.Object obj && obj != null) values.Add(obj);",
        'Undo.RecordObjects(serializedObject.targetObjects, "Change");',
        "var values = Property.Tree.WeakTargets.OfType<UnityEngine.Object>();",
    ],
)
def test_odin_existing_safe_forms_remain_accepted(source: str) -> None:
    assert not odin.analyze(source)


@pytest.mark.parametrize("prefix", ["// ", "/* ", 'string value = "'])
def test_odin_comments_and_literals_cannot_create_findings(prefix: str) -> None:
    suffix = " */" if prefix == "/* " else '";' if prefix.startswith("string") else ""
    assert not odin.analyze(prefix + "(UnityEngine.Object[])Property.Tree.WeakTargets" + suffix)


def test_odin_unrelated_nearby_filter_cannot_hide_a_direct_cast() -> None:
    source = "if (other is UnityEngine.Object obj && obj != null) values.Add(obj);\nvar values = (UnityEngine.Object[])Property.Tree.WeakTargets;"
    assert odin.analyze(source)


@pytest.mark.parametrize("marker", ["// UNH-SUPPRESS", "/* UNH-SUPPRESS */"])
def test_odin_explicit_comment_suppression_remains_supported(marker: str) -> None:
    assert not odin.analyze(
        "var values = (UnityEngine.Object[])Property.Tree.WeakTargets; " + marker
    )


def test_odin_a_suppression_string_does_not_hide_an_unsafe_cast() -> None:
    assert odin.analyze(
        'string marker = "// UNH-SUPPRESS"; var values = (UnityEngine.Object[])Property.Tree.WeakTargets;'
    )


def test_odin_missing_explicit_files_are_not_silent_success(tmp_path: Path) -> None:
    assert odin.main(["--paths", str(tmp_path / "absent.cs")]) == 1


def test_odin_empty_default_corpus_is_not_success(tmp_path: Path) -> None:
    assert odin.main(["--repo-root", str(tmp_path)]) == 1


@pytest.mark.parametrize("legacy", [False, True])
def test_odin_cli_checks_every_explicit_path_and_ignores_caller_directory(
    tmp_path: Path, legacy: bool
) -> None:
    good = tmp_path / "first ü.cs"
    bad = tmp_path / "second 名字.cs"
    good.write_text("// nothing forbidden", encoding="utf-8")
    bad.write_text(
        "var values = (UnityEngine.Object[])Property.Tree.WeakTargets;", encoding="utf-8"
    )
    if legacy:
        shell = shutil.which("pwsh")
        assert shell is not None
        command = [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-odin-undo-safety.ps1"),
            "-Paths",
            str(good),
            str(bad),
        ]
    else:
        command = [
            sys.executable,
            str(ROOT / "scripts/lint_odin_undo_safety.py"),
            "--paths",
            str(good),
            str(bad),
        ]
    result = subprocess.run(
        command, cwd=tmp_path, capture_output=True, encoding="utf-8", check=False
    )
    assert result.returncode == 1, result.stdout + result.stderr
    assert "UNH006" in result.stdout
    assert bad.name in result.stdout
