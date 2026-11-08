"""Preserve drawer advisories and exercise actual Python and PowerShell entry points."""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from lint_drawer_multiobject import analyze, containing_method, selected_files

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/lint_drawer_multiobject.py"


@pytest.mark.parametrize(
    ("lines", "line", "expected"),
    [
        (
            [
                "public class MyDrawer : PropertyDrawer",
                "{",
                "    public override void OnGUI(Rect pos)",
                "    {",
                "        property.intValue = 5;",
                "    }",
                "}",
            ],
            5,
            "OnGUI",
        ),
        (
            [
                "public override void OnGUI(Rect pos)",
                "{",
                "    if (condition) {",
                "        property.intValue = 5;",
                "    }",
                "}",
            ],
            4,
            "OnGUI",
        ),
        (
            [
                "public override void OnGUI(Rect pos) {",
                "    if (something) {",
                "        for (int i = 0; i < 10; i++) {",
                "            property.intValue = 5;",
                "        }",
                "    }",
                "}",
            ],
            4,
            "OnGUI",
        ),
        (
            [
                "public override void OnGUI(Rect pos) {",
                "    if (a) {",
                "        if (b) {",
                "            for (int i = 0; i < 5; i++) {",
                "                property.intValue = 5;",
                "            }",
                "        }",
                "    }",
                "}",
            ],
            5,
            "OnGUI",
        ),
        (
            [
                "public override void OnGUI(Rect pos) {",
                '    menu.AddItem(new GUIContent("X"), false, () =>',
                "    {",
                "        property.intValue = 5;",
                "    });",
                "}",
            ],
            4,
            "OnGUI",
        ),
        (
            [
                "public override void OnGUI(Rect pos)",
                "{",
                "    if (x)",
                "    {",
                "        property.intValue = 5;",
                "    }",
                "}",
            ],
            5,
            "OnGUI",
        ),
        (
            [
                "private static void DrawToggleWithValue(SerializedProperty prop) {",
                "    if (condition) {",
                "        property.boolValue = true;",
                "    }",
                "}",
            ],
            3,
            "DrawToggleWithValue",
        ),
        (["public class MyDrawer {", "    private int _value = 5;", "}"], 2, None),
        (["if (condition)", "{", "    property.intValue = 5;", "}"], 3, None),
        (
            ["for (int i = 0; i < 10; i++)", "{", "    property.intValue = 5;", "}"],
            3,
            None,
        ),
    ],
    ids=[
        "simple",
        "one-if",
        "two-levels",
        "three-levels",
        "lambda",
        "separate-brace",
        "static",
        "class-level",
        "if-is-not-method",
        "for-is-not-method",
    ],
)
def test_original_ten_containing_method_controls(
    lines: list[str], line: int, expected: str | None
) -> None:
    assert containing_method(lines, line) == expected


@pytest.mark.parametrize(
    "value",
    [
        "intValue",
        "stringValue",
        "floatValue",
        "boolValue",
        "enumValueIndex",
        "objectReferenceValue",
    ],
)
def test_render_writes_retain_property_type_and_line(value: str) -> None:
    findings = analyze(
        f"public override void OnGUI(Rect pos) {{\n    property.{value} = value;\n}}"
    )
    assert [(finding.line, finding.message) for finding in findings] == [
        (2, f"Property assignment during render: property.{value} =")
    ]


@pytest.mark.parametrize(
    "method",
    [
        "ApplySelectionToProperty",
        "ApplySelection",
        "SetPropertyValue",
        "UpdateProperty",
        "WriteValue",
        "WritePropertyValue",
        "OtherMethod",
    ],
)
def test_non_render_methods_are_not_render_writes(method: str) -> None:
    assert analyze(f"private void {method}() {{\n    property.intValue = 7;\n}}") == []


@pytest.mark.parametrize(
    "callback",
    [
        "() => ",
        "delegate() ",
        "menu.AddItem(",
        "RegisterValueChangedCallback(",
        "onClick += ",
    ],
)
def test_same_line_callback_write_is_exempt(callback: str) -> None:
    assert analyze(f"void OnGUI() {{\n    {callback}property.intValue = 7;\n}}") == []


@pytest.mark.parametrize(
    ("line", "message"),
    [
        (
            "index = Array.IndexOf(items, property.intValue);",
            "Property used in index calculation",
        ),
        ("index = 0;", "Index clamping"),
        ("bool isSelected = chosen == currentIndex;", "GenericMenu isSelected"),
    ],
)
def test_each_mixed_value_advisory_has_a_reporting_control(line: str, message: str) -> None:
    findings = analyze(line)
    assert len(findings) == 1
    assert findings[0].message.startswith(message)
    assert analyze(f"{line} // UNH-SUPPRESS: intentional") == []
    assert analyze(f"{line} /* UNH-SUPPRESS: intentional */") == []
    assert len(analyze(f'{line} string marker = "UNH-SUPPRESS";')) == 1
    guarded = analyze(f"{MIXED_CHECK}\n{line}")
    if "isSelected" in line:
        assert [(finding.line, finding.message) for finding in guarded] == [
            (2, findings[0].message)
        ]
    else:
        assert guarded == []


MIXED_CHECK = "if (property.hasMultipleDifferentValues) return;"


@pytest.mark.parametrize(
    "comment",
    [
        "// property.intValue = 9;",
        "/* property.intValue = 9; */",
        'string text = "property.intValue = 9;";',
        'string text = @"property.intValue = 9;";',
        "if (property.intValue == 9) return;",
    ],
)
def test_non_writes_do_not_trigger_render_assignment(comment: str) -> None:
    assert analyze(f"void OnGUI() {{\n    {comment}\n}}") == []


@pytest.mark.parametrize(
    "check",
    ["// hasMultipleDifferentValues", 'string check = "hasMultipleDifferentValues";'],
)
def test_comments_and_strings_cannot_hide_missing_mixed_value_guard(check: str) -> None:
    assert len(analyze(f"{check}\nindex = 0;")) == 1


def run_cli(root: Path, *arguments: str, script: Path = SCRIPT) -> subprocess.CompletedProcess[str]:
    executable = shutil.which("pwsh") if script.suffix == ".ps1" else sys.executable
    if executable is None:
        pytest.skip("PowerShell is unavailable")
    command = (
        [executable, "-NoProfile", "-File", str(script)]
        if script.suffix == ".ps1"
        else [executable, str(script)]
    )
    return subprocess.run(
        command + list(arguments),
        cwd=root,
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=60,
        check=False,
    )


def drawer(
    root: Path, name: str = "SampleDrawer.cs", source: str = "class SampleDrawer {}"
) -> Path:
    path = root / "Editor/CustomDrawers" / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(source, encoding="utf-8-sig")
    return path


def test_real_cli_advisories_remain_nonblocking_and_paths_keep_all_inputs(
    tmp_path: Path,
) -> None:
    first = drawer(tmp_path, "FirstDrawer.cs", "void OnGUI() {\nproperty.intValue = 3;\n}")
    second = drawer(tmp_path, "雪Drawer.cs", "index = 0;")
    before = first.read_bytes(), second.read_bytes()
    result = run_cli(
        tmp_path,
        "--repo-root",
        str(tmp_path),
        "--paths",
        str(first),
        str(second),
        "--verbose",
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "FirstDrawer.cs:2" in result.stdout
    assert "雪Drawer.cs:1" in result.stdout
    assert "Checked 2 property drawers" in result.stdout
    assert before == (first.read_bytes(), second.read_bytes())


@pytest.mark.parametrize(
    "state",
    ["missing-root", "empty-root", "missing-path", "not-drawer", "invalid-encoding"],
)
def test_real_cli_refuses_unusable_inputs(tmp_path: Path, state: str) -> None:
    arguments = ["--repo-root", str(tmp_path)]
    if state == "empty-root":
        (tmp_path / "Editor/CustomDrawers").mkdir(parents=True)
    elif state == "missing-path":
        arguments += ["--paths", "missingDrawer.cs"]
    elif state == "not-drawer":
        path = drawer(tmp_path, "Plain.cs")
        arguments += ["--paths", str(path)]
    elif state == "invalid-encoding":
        drawer(tmp_path).write_bytes(b"\xff\xfe\x80")
    result = run_cli(tmp_path, *arguments)
    assert result.returncode == 1, result.stdout + result.stderr


def test_real_powershell_launcher_preserves_multiple_paths(tmp_path: Path) -> None:
    first = drawer(tmp_path, "FirstDrawer.cs", "index = 0;")
    second = drawer(tmp_path, "SecondDrawer.cs", "index = 0;")
    result = run_cli(
        tmp_path,
        "-RepoRoot",
        str(tmp_path),
        "-Paths",
        str(first),
        str(second),
        "-VerboseOutput",
        script=ROOT / "scripts/lint-drawer-multiobject.ps1",
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Checked 2 property drawers" in result.stdout


def test_selected_linked_input_is_refused(tmp_path: Path) -> None:
    source = drawer(tmp_path)
    linked = tmp_path / "LinkedDrawer.cs"
    try:
        linked.symlink_to(source)
    except OSError:
        pytest.skip("Symlink creation is unavailable")
    with pytest.raises(ValueError, match="linked"):
        selected_files(tmp_path, [str(linked)])


def test_cli_does_not_depend_on_caller_working_directory(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    drawer(root)
    result = run_cli(tmp_path, "--repo-root", str(root), "--verbose")
    assert result.returncode == 0, result.stdout + result.stderr
    assert "Checked 1 property drawers" in result.stdout


def test_outside_and_parent_traversal_inputs_are_refused(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    outside = drawer(tmp_path)
    with pytest.raises(ValueError, match="outside"):
        selected_files(root, [str(outside)])
    with pytest.raises(ValueError, match="outside"):
        selected_files(root, [str(Path("..") / "Editor/CustomDrawers/SampleDrawer.cs")])


def test_default_scan_refuses_a_linked_editor_parent(tmp_path: Path) -> None:
    outside = tmp_path / "outside"
    outside.mkdir()
    folder = outside / "CustomDrawers"
    folder.mkdir()
    (folder / "SampleDrawer.cs").write_text("class SampleDrawer {}", encoding="utf-8")
    root = tmp_path / "repository"
    root.mkdir()
    try:
        (root / "Editor").symlink_to(outside, target_is_directory=True)
    except OSError:
        pytest.skip("Symlink creation is unavailable")
    with pytest.raises(ValueError, match="linked"):
        selected_files(root, None)


def test_explicit_scan_refuses_a_linked_repository_root(tmp_path: Path) -> None:
    root = tmp_path / "repository"
    root.mkdir()
    subject = drawer(root)
    link = tmp_path / "linked"
    try:
        link.symlink_to(root, target_is_directory=True)
    except OSError:
        pytest.skip("Directory links are unavailable on this host")
    with pytest.raises(ValueError, match="linked"):
        selected_files(link, [str(subject.relative_to(root))])


def test_legacy_test_launcher_runs_selected_control(tmp_path: Path) -> None:
    shell = shutil.which("pwsh")
    if shell is None:
        pytest.skip("PowerShell is unavailable")
    result = subprocess.run(
        [
            shell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/tests/test-lint-drawer-multiobject.ps1"),
            "-TestFilter",
            "test_outside_and_parent_traversal_inputs_are_refused",
        ],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


def test_explicit_paths_accept_parent_segments_in_repository_root(tmp_path: Path) -> None:
    nested = tmp_path / "child"
    nested.mkdir()
    drawer = tmp_path / "Editor/CustomDrawers/ExampleDrawer.cs"
    drawer.parent.mkdir(parents=True)
    drawer.write_text("class ExampleDrawer {}", encoding="utf-8")
    repository = nested / ".."
    assert selected_files(repository, ["Editor/CustomDrawers/ExampleDrawer.cs"]) == [
        repository / "Editor/CustomDrawers/ExampleDrawer.cs"
    ]
    with pytest.raises(ValueError, match="outside"):
        selected_files(repository, ["../ExampleDrawer.cs"])
