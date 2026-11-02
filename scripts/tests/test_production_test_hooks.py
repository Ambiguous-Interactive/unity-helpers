"""Retain original naming-gate vectors and exercise its Python and Node routes."""

from __future__ import annotations

import hashlib
import itertools
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import lint_production_test_hooks as hooks

ROOT = Path(__file__).resolve().parents[2]


ANALYSIS_CASES: tuple[tuple[str, bool, list[str]], ...] = (
    (
        "internal void ApplyForTesting() {}",
        False,
        ["forbidden production identifier ApplyForTesting"],
    ),
    ("internal int CountForTests => 1;", False, ["forbidden production identifier CountForTests"]),
    ("internal class TestHooks {}", False, ["forbidden production identifier TestHooks"]),
    (
        "private Action TestOnlyCallback;",
        False,
        ["forbidden production identifier TestOnlyCallback"],
    ),
    ("internal class TestHook {}", False, ["forbidden production identifier TestHook"]),
    ("internal void runforTesting() {}", False, ["forbidden production identifier runforTesting"]),
    (
        "private Action testOnlyCallback;",
        False,
        ["forbidden production identifier testOnlyCallback"],
    ),
    ("internal void @RunForTesting() {}", False, ["forbidden production identifier RunForTesting"]),
    (
        "internal void RunFor\\u0054esting() {}",
        False,
        ["forbidden production identifier RunForTesting"],
    ),
    (
        "#if UNITY_INCLUDE_TESTS\ninternal void RunForTests() {}\n#endif",
        False,
        ["forbidden production identifier RunForTests"],
    ),
    ("internal void RunForTests\r() {}", False, ["forbidden production identifier RunForTests"]),
    ('writer.Line("internal class testHook {}");', True, ["forbidden emitted identifier testHook"]),
    ("// RunForTesting is forbidden\ninternal void Run() {}", False, []),
    ("/* internal class TestHooks {} */ internal class Real {}", False, []),
    ('const string Message = "ForTesting";', False, []),
    ('const string Message = @"TestHooks "" quoted";', False, []),
    ('const string Message = """ForTests\nTestOnly""";', False, []),
    ("internal bool IsTestAssembly(Assembly assembly) => true;", False, []),
    (
        "#if UNITY_INCLUDE_TESTS\n// Runtime scans authoring assemblies.\ninternal bool IsTestAssemblyLoaded() => true;\n#endif",
        False,
        [],
    ),
    ("internal void Update(float elapsed) {}", False, []),
    ("internal Action Callback;", False, []),
    (
        "// Test-only callback injection\ninternal Action Callback;",
        False,
        ["explicit test-only production marker"],
    ),
    ("/// Excludes test-only singleton types from discovery.\ninternal class Filter {}", False, []),
    (
        'writer.Line("internal void RunForTesting() {}");',
        True,
        ["forbidden emitted identifier RunForTesting"],
    ),
    (
        'writer.Line(@"internal class TestHooks {}");',
        True,
        ["forbidden emitted identifier TestHooks"],
    ),
    (
        'writer.Line("internal void RunFor\\u0054esting() {}");',
        True,
        ["forbidden emitted identifier RunForTesting"],
    ),
    (
        'writer.Line("""internal int CountForTests;""");',
        True,
        ["forbidden emitted identifier CountForTests"],
    ),
    ('writer.Line("#if UNITY_INCLUDE_TESTS");', True, ["emitted test-only conditional code"]),
    ('writer.Line("internal static void Register() {}");', True, []),
    (
        "internal void RunFor\\U00000054esting() {}",
        False,
        ["forbidden production identifier RunForTesting"],
    ),
    (
        'writer.Line("internal void RunFor" + "Testing() {}");',
        True,
        ["forbidden emitted identifier RunForTesting"],
    ),
    (
        'writer.Line("internal void RunFor\\U00000054esting() {}");',
        True,
        ["forbidden emitted identifier RunForTesting"],
    ),
    ('writer.Line("/// ForTesting is forbidden.");', True, []),
    ('writer.Line("internal const string Message = \\"ForTesting\\";");', True, []),
    ("private char Quote = '\\''; internal void Run() {}", False, []),
    (
        "#if UNKNOWN_DEFINE\ninternal void RunForTests() {}\n#endif",
        False,
        ["forbidden production identifier RunForTests"],
    ),
    ('string Message => $"{Foreign.RunForTests()}";', False, []),
    (
        'internal void RunForTests() {} string Message => $"{RunForTests()}";',
        False,
        ["forbidden production identifier RunForTests"],
    ),
    (
        "internal bool GetConditionForTest() => true;",
        False,
        ["forbidden production identifier GetConditionForTest"],
    ),
    ("internal void RunFor\\u0054est() {}", False, ["forbidden production identifier RunForTest"]),
    (
        'writer.Line("internal void RunForTest() {}");',
        True,
        ["forbidden emitted identifier RunForTest"],
    ),
    (
        'writer.Line("internal void RunFor\\u0054est() {}");',
        True,
        ["forbidden emitted identifier RunForTest"],
    ),
)

PATH_CASES: tuple[tuple[str, bool], ...] = (
    ("Runtime/Helpers.cs", True),
    ("Editor/Helpers.cs", True),
    ("Generator~/WallstopStudios.UnityHelpers.Analyzers/Foo.cs", True),
    ("Generator~/WallstopStudios.UnityHelpers.Proto.Generator/Foo.cs", True),
    ("Generator~/NewProduction/Foo.cs", True),
    ("Samples~/Demo/Foo.cs", True),
    ("Styles/Foo.cs", True),
    ("Tests/Runtime/Foo.cs", False),
    ("Generator~/WallstopStudios.UnityHelpers.Analyzers.Tests/Foo.cs", False),
    ("Generator~/WallstopStudios.UnityHelpers.TestCheck/Foo.cs", False),
    ("Generator~/WallstopStudios.UnityHelpers.SyntaxPolicy/Foo.cs", False),
    ("Generator~/ProtobufNetV2Oracle/Foo.cs", False),
    ("Runtime/Tests/Hook.cs", True),
    ("Editor/WallstopStudios.UnityHelpers.TestCheck/Hook.cs", True),
    ("Generator~/Emitter/Tests/Hook.cs", True),
)


@pytest.mark.parametrize(("source", "generated", "expected"), ANALYSIS_CASES)
def test_original_analysis_vectors(source: str, generated: bool, expected: list[str]) -> None:
    assert hooks.analyze_file(source, generated) == expected


@pytest.mark.parametrize(("source", "expected"), PATH_CASES)
def test_original_path_vectors(source: str, expected: bool) -> None:
    assert hooks.is_production_path(source) == expected


@pytest.mark.parametrize("legacy", [False, True])
def test_cli_all_original_root_and_scan_controls(tmp_path: Path, legacy: bool) -> None:
    environment = dict(os.environ, PRODUCTION_TEST_HOOKS_ROOT=str(tmp_path))
    if legacy:
        node = shutil.which("node")
        assert node is not None
        command = [node, str(ROOT / "scripts/lint-production-test-hooks.js")]
    else:
        command = [sys.executable, str(ROOT / "scripts/lint_production_test_hooks.py")]

    def write(relative: str, source: str) -> None:
        path = tmp_path / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")

    def run(status: int, diagnostic: str = "") -> None:
        result = subprocess.run(
            command,
            cwd=tmp_path,
            env=environment,
            text=True,
            encoding="utf-8",
            capture_output=True,
            check=False,
            timeout=30,
        )
        assert result.returncode == status, result.stdout + result.stderr
        assert diagnostic in result.stdout + result.stderr

    write("Runtime/Real.cs", "internal class Real {}")
    run(1, "missing production root directory Editor")
    (tmp_path / "Runtime/Real.cs").unlink()
    for name in hooks.SCAN_ROOTS:
        (tmp_path / name).mkdir(exist_ok=True)
    run(1, "no production C# files found")
    write("Runtime/Real.cs", "internal class Real {}")
    write(
        "Generator~/WallstopStudios.UnityHelpers.Analyzers.Tests/Excluded.cs", "class TestHooks {}"
    )
    run(0, "1 production files scanned, 0 findings")
    write("Samples~/Demo/Hook.cs", "internal void RunForTesting() {}")
    run(1, "forbidden production identifier RunForTesting")
    (tmp_path / "Samples~/Demo/Hook.cs").unlink()
    (tmp_path / "Editor").rename(tmp_path / "RenamedEditor")
    run(1, "missing production root directory Editor")
    (tmp_path / "RenamedEditor").rename(tmp_path / "Editor")
    (tmp_path / "Styles").rmdir()
    (tmp_path / "Styles").write_text("not a directory", encoding="utf-8")
    run(1, "missing production root directory Styles")
    (tmp_path / "Styles").unlink()
    (tmp_path / "Styles").mkdir()
    write("Generator~/Emitter/Emitter.cs", 'writer.Line("internal class TestHooks {}");')
    run(1, "forbidden emitted identifier")


@pytest.mark.parametrize(
    "payload",
    [
        None,
        [],
        {},
        {"source": 1},
        {"source": "x", "action": "unknown"},
        {"source": "x", "action": "analyzeFile", "generated": "true"},
    ],
)
def test_api_rejects_invalid_shapes(payload: object) -> None:
    with pytest.raises(ValueError, match=r"Expected|Unknown"):
        hooks.api(payload)


def test_node_imports_preserve_unicode_ranges_and_crlf() -> None:
    node = shutil.which("node")
    assert node is not None
    source = '/*🦄*/ var x = "🦄";\r\n writer.Line("internal void RunFor" + "Test() {}");'
    result = subprocess.run(
        [
            node,
            "-e",
            "const p=require(process.argv[1]); process.stdout.write(JSON.stringify(p.tokenize(process.argv[2])));",
            str(ROOT / "scripts/lint-production-test-hooks.js"),
            source,
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    response: object = json.loads(result.stdout)
    assert response == hooks.api({"action": "tokenize", "source": source})
    assert isinstance(response, dict)
    ranges = cast("dict[str, object]", response)["literalRanges"]
    assert isinstance(ranges, list)
    assert cast("list[object]", ranges)[0] == {"start": 15, "end": 19}
    tokens = hooks.tokenize(source)
    assert "\r\n" in tokens.code
    assert hooks.utf16_length(tokens.code) == hooks.utf16_length(source)


@pytest.mark.parametrize("encoding", ["utf-8", "cp1252"])
def test_api_input_uses_utf8_independent_of_console_encoding(encoding: str) -> None:
    source = '/*漢字🦄*/ var x = "café🦄";\r\n'
    request = {"action": "tokenize", "source": source}
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/lint_production_test_hooks.py"), "--api"],
        input=json.dumps(request, ensure_ascii=False).encode("utf-8"),
        env={**os.environ, "PYTHONIOENCODING": encoding},
        capture_output=True,
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    response: object = json.loads(result.stdout)
    assert response == hooks.api(request)


def test_node_analysis_and_path_imports() -> None:
    node = shutil.which("node")
    assert node is not None
    source = 'writer.Line("internal class TestHook {}");'
    script = (
        "const p=require(process.argv[1]);"
        "console.log(JSON.stringify([p.analyzeFile(process.argv[2],1),"
        "p.analyzeFile(process.argv[2],null),p.isProductionPath('Runtime/Tests/Hook.cs'),"
        "p.isProductionPath('Generator~/Example.Tests/Hook.cs')]));"
    )
    result = subprocess.run(
        [node, "-e", script, str(ROOT / "scripts/lint-production-test-hooks.js"), source],
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert json.loads(result.stdout) == [["forbidden emitted identifier TestHook"], [], True, False]


def test_node_main_resolves_relative_roots_from_caller(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    root = tmp_path / "consumer ü"
    for name in hooks.SCAN_ROOTS:
        (root / name).mkdir(parents=True)
    (root / "Runtime/Real.cs").write_text("class Real {}", encoding="utf-8")
    result = subprocess.run(
        [
            node,
            "-e",
            "process.exitCode=require(process.argv[1]).main(process.argv[2]);",
            str(ROOT / "scripts/lint-production-test-hooks.js"),
            root.name,
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
        timeout=30,
    )
    assert result.returncode == 0, result.stderr
    assert "1 production files scanned, 0 findings" in result.stdout


@pytest.mark.parametrize("opening", ["//", "/*", '"', '"""', "'"])
def test_unterminated_tokens_finish_without_exposing_hidden_names(opening: str) -> None:
    assert hooks.analyze_file(opening + "RunForTesting") == []


def test_build_directories_do_not_join_scope(tmp_path: Path) -> None:
    for name in hooks.SCAN_ROOTS:
        (tmp_path / name).mkdir()
    path = tmp_path / "Runtime/Real.cs"
    path.write_text("class Real {}", encoding="utf-8")
    for name in hooks.SKIPPED:
        directory = tmp_path / "Runtime" / name
        directory.mkdir()
        (directory / "Bad.cs").write_text("class TestHooks {}", encoding="utf-8")
    assert hooks.collect_sources(tmp_path) == [path]


def test_invalid_utf8_is_refused(tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    for name in hooks.SCAN_ROOTS:
        (tmp_path / name).mkdir()
    (tmp_path / "Runtime/Invalid.cs").write_bytes(b"\xff")
    assert hooks.main(tmp_path) == 1
    assert "production-test-hooks" in capsys.readouterr().err


@pytest.mark.parametrize(
    ("space", "expected"), [("\ufeff", True), ("\u00a0", True), ("\u2028", True), ("\x1c", False)]
)
def test_javascript_whitespace_contract(space: str, expected: bool) -> None:
    assert bool(hooks.analyze_file(f"// Test-only{space}callback")) == expected
    source = 'writer.Line("internal void RunFor"' + space + "+" + space + '"Test() {}");'
    assert bool(hooks.analyze_file(source, generated=True)) == expected
    assert (
        bool(hooks.analyze_file(f'writer.Line("#if{space}UNITY_INCLUDE_TESTS");', generated=True))
        == expected
    )


@pytest.mark.parametrize("ending", ["\r", "\n", "\u2028", "\u2029"])
def test_literal_escape_does_not_consume_line_terminators(ending: str) -> None:
    content = "a\\" + ending + "b"
    assert hooks.tokenize('"' + content + '"').literals == [content]


@pytest.mark.parametrize("ending", ["\r", "\n", "\u2028", "\u2029"])
def test_emitted_directives_preserve_javascript_line_boundaries(ending: str) -> None:
    source = f'writer.Line(@"ordinary{ending}#if UNITY_INCLUDE_TESTS{ending}ordinary");'
    assert "emitted test-only conditional code" in hooks.analyze_file(source, generated=True)


def test_original_token_streams_across_fragment_combinations() -> None:
    fragments = (
        r"internal void RunFor\u0054est() {}",
        'writer.Line("internal void RunFor" \ufeff + "Test() {}");',
        'const string Message = @"TestHooks ""safe""";',
        "/*🦄 Test-only callback */",
        "// ForTesting is forbidden\r\n",
        '"unterminated RunForTests',
        'writer.Line("""internal class TestHooks {}""");',
    )
    digest = hashlib.sha256()
    for parts in itertools.product(fragments, repeat=3):
        source = "\n".join(parts)
        result = {
            "tokens": hooks.api({"action": "tokenize", "source": source}),
            "ordinary": hooks.analyze_file(source),
            "generated": hooks.analyze_file(source, generated=True),
        }
        encoded = json.dumps(result, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
        digest.update((encoded + "\n").encode("utf-8"))
    assert digest.hexdigest() == "33cff09b9143a65207144c45df8fb877ca462a2a1c318ebd439b2be8b7cc6b18"
