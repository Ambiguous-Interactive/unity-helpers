"""Replay original skill outputs and mutate isolated instruction fixtures."""

from __future__ import annotations

import json
import secrets
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import generate_agent_skill_stubs
import generate_skills_index as index
import lint_llm_instructions as lint
import migrate_agent_skills as migration
from check_runner import mapping

ROOT = Path(__file__).resolve().parents[2]


def corpus(name: str) -> list[dict[str, object]]:
    data = mapping(
        cast(
            "object",
            json.loads((ROOT / "scripts/tests/fixtures" / name).read_text(encoding="utf-8")),
        )
    )
    cases = data["cases"]
    assert isinstance(cases, list)
    return [mapping(case) for case in cast("list[object]", cases)]


INDEX_CASES = corpus("skills-index-v1.json")
SPLIT_CASES = corpus("skill-splits-v1.json")
POLICY_CASES = corpus("instruction-policy-v1.json")


@pytest.mark.parametrize("case", INDEX_CASES)
def test_original_index_bytes(case: dict[str, object], tmp_path: Path) -> None:
    directory = tmp_path / ".llm/skills"
    directory.mkdir(parents=True)
    for name, value in mapping(case["files"]).items():
        assert isinstance(value, str)
        (directory / name).write_bytes(value.encode("utf-8"))
    if case["intentional_empty_rejection"]:
        with pytest.raises(ValueError, match="no authored"):
            index.render(directory)
    else:
        assert index.render(directory) == case["stdout"]


def split_fixture(root: Path, case: dict[str, object]) -> Path:
    for directory in (".llm/skills", ".llm/references"):
        (root / directory).mkdir(parents=True, exist_ok=True)
    source = root / ".llm/skills/demo.md"
    assert isinstance(case["source"], str)
    source.write_bytes(case["source"].encode("utf-8"))
    shutil.copyfile(ROOT / ".prettierrc.json", root / ".prettierrc.json")
    return source


@pytest.mark.parametrize("case", SPLIT_CASES)
def test_original_split_bytes(
    case: dict[str, object], tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    split_fixture(tmp_path, case)
    observations = cast("list[dict[str, object]]", case["observations"])
    for observation in observations:
        mode = observation["mode"]
        assert isinstance(mode, str)
        assert migration.run(mode, tmp_path) == observation["status"]
        captured = capsys.readouterr()
        assert captured.out == observation["stdout"]
        assert captured.err == observation["stderr"]
    actual = {
        path.relative_to(tmp_path).as_posix(): path.read_bytes().decode("utf-8")
        for path in (tmp_path / ".llm").rglob("*")
        if path.is_file()
    }
    assert actual == case["files"]


def test_split_refresh_and_router_guard(tmp_path: Path) -> None:
    source = split_fixture(tmp_path, SPLIT_CASES[0])
    assert migration.run("--write", tmp_path) == 0
    reference = tmp_path / ".llm/references/demo-part-1.md"
    reference.write_bytes(reference.read_bytes() + b"\nAdditional instruction.\n")
    with pytest.raises(ValueError, match="Reconstruction mismatch"):
        migration.run("--check", tmp_path)
    assert migration.run("--refresh", tmp_path) == 0
    assert migration.run("--check", tmp_path) == 0
    original = source.read_bytes()
    source.write_bytes(original + b"\nUntracked router instruction.\n")
    before = reference.read_bytes()
    with pytest.raises(ValueError, match="Router changed outside refresh"):
        migration.run("--refresh", tmp_path)
    assert reference.read_bytes() == before
    source.write_bytes(original.replace(b"Demo description", b"Updated demo description"))
    assert migration.run("--refresh", tmp_path) == 0
    assert migration.run("--check", tmp_path) == 0


def test_original_parent_link_refusal(tmp_path: Path) -> None:
    case = dict(SPLIT_CASES[0])
    assert isinstance(case["source"], str)
    case["source"] += "\n[Parent](../).\n"
    source = split_fixture(tmp_path, case)
    before = source.read_bytes()
    with pytest.raises(ValueError, match="Migration would change source content"):
        migration.run("--plan", tmp_path)
    assert source.read_bytes() == before
    assert not list((tmp_path / ".llm/references").iterdir())


@pytest.mark.parametrize(("filename", "fails"), [("UPPER.MD", False), ("INDEX.md", True)])
def test_split_source_census_preserves_original_case_rules(
    filename: str, fails: bool, tmp_path: Path
) -> None:
    root = tmp_path
    (root / ".llm/skills").mkdir(parents=True)
    (root / ".llm/references").mkdir()
    (root / ".llm/skills/small.md").write_text(
        "# Small\n\n<!-- trigger: small | Small | Core -->\n", encoding="utf-8"
    )
    (root / ".llm/skills" / filename).write_text(
        "# Large\n\n<!-- trigger: large | Large | Core -->\n\n" + "long\n" * 200, encoding="utf-8"
    )
    (root / ".llm/skill-splits.json").write_text('{"version":1,"skills":[]}', encoding="utf-8")
    if fails:
        with pytest.raises(ValueError, match="Skill exceeds"):
            migration.run("--check", root)
    else:
        assert migration.run("--check", root) == 0


def test_split_and_stub_rename_integration(tmp_path: Path) -> None:
    source = split_fixture(tmp_path, SPLIT_CASES[0])
    assert migration.run("--write", tmp_path) == 0
    assert generate_agent_skill_stubs.generate(tmp_path, write=True) == 0
    assert generate_agent_skill_stubs.generate(tmp_path, write=False) == 0
    source.rename(source.with_name("renamed.md"))
    assert generate_agent_skill_stubs.generate(tmp_path, write=False) == 1
    assert generate_agent_skill_stubs.generate(tmp_path, write=True) == 0
    assert generate_agent_skill_stubs.generate(tmp_path, write=False) == 0
    for directory in (".agents", ".claude"):
        assert not (tmp_path / directory / "skills/demo").exists()
        assert (tmp_path / directory / "skills/renamed/SKILL.md").is_file()


def test_read_only_index_gate_keeps_git_and_empty_scope_guards(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    directory = tmp_path / ".llm/skills"
    directory.mkdir(parents=True)
    (directory / "a.md").write_text("# A\n\n<!-- trigger: a | Alpha | Core -->\n", encoding="utf-8")
    expected = index.render(directory).encode("utf-8")
    destination = directory / "index.md"
    destination.write_bytes(expected)
    git = shutil.which("git")
    assert git is not None

    def command(*arguments: str) -> None:
        subprocess.run(
            [git, "-C", str(tmp_path), *arguments], check=True, capture_output=True, timeout=30
        )

    command("init", "-q")
    command("config", "user.name", "Test")
    command("config", "user.email", "test@example.com")
    command("config", "core.hooksPath", str(tmp_path / "no-hooks"))
    with pytest.raises(ValueError, match="Git check failed"):
        index.check_index(tmp_path, expected)
    assert destination.read_bytes() == expected
    command("add", ".")
    with pytest.raises(ValueError, match="uncommitted"):
        index.check_index(tmp_path, expected)
    command("commit", "-qm", "base")
    index.check_index(tmp_path, expected)
    monkeypatch.setenv("GIT_DIR", str(tmp_path / "wrong-git"))
    index.check_index(tmp_path, expected)
    destination.write_bytes(expected + b"drift\n")
    with pytest.raises(ValueError, match="out of date"):
        index.check_index(tmp_path, expected)
    assert destination.read_bytes() == expected + b"drift\n"


def test_formatter_failure_preserves_unsplit_inputs(tmp_path: Path) -> None:
    source = split_fixture(tmp_path, SPLIT_CASES[0])
    before = source.read_bytes()
    (tmp_path / ".prettierrc.json").write_text("{", encoding="utf-8")
    with pytest.raises(ValueError, match="formatting failed"):
        migration.run("--write", tmp_path)
    assert source.read_bytes() == before
    assert not (tmp_path / ".llm/skill-splits.json").exists()
    assert not list((tmp_path / ".llm/references").iterdir())


@pytest.mark.parametrize(
    "part", ["../../../outside.md", "../outside.md", "C:/outside.md", "other\\outside.md", ""]
)
def test_split_manifest_refuses_redirected_names(part: str, tmp_path: Path) -> None:
    split_fixture(tmp_path, SPLIT_CASES[0])
    (tmp_path / ".llm/skill-splits.json").write_text(
        json.dumps({"version": 1, "skills": [{"name": "demo", "sha256": "bad", "parts": [part]}]}),
        encoding="utf-8",
    )
    before = (tmp_path / ".llm/skills/demo.md").read_bytes()
    with pytest.raises(ValueError, match="literal skill or reference filename"):
        migration.run("--refresh", tmp_path)
    assert (tmp_path / ".llm/skills/demo.md").read_bytes() == before


def test_refresh_preserves_authored_file_outside_instruction_roots(tmp_path: Path) -> None:
    root = tmp_path / "repo"
    source = split_fixture(root, SPLIT_CASES[0])
    source.write_text(
        "# Skill: Demo\n\n<!-- trigger: demo | Demo description | Core -->\n\n## Reference Parts\n\n- [Part 1](../references/../../../outside.md)\n",
        encoding="utf-8",
    )
    outside = tmp_path / "outside.md"
    outside.write_text("# Outside\n\n## Split Content\n\nAuthored text.\n", encoding="utf-8")
    manifest = root / ".llm/skill-splits.json"
    manifest.write_text(
        json.dumps(
            {
                "version": 1,
                "skills": [{"name": "demo", "sha256": "bad", "parts": ["../../../outside.md"]}],
            }
        ),
        encoding="utf-8",
    )
    before = {path: path.read_bytes() for path in (source, outside, manifest)}
    with pytest.raises(ValueError, match="literal skill or reference filename"):
        migration.run("--refresh", root)
    assert {path: path.read_bytes() for path in before} == before
    assert not list((root / ".llm/references").iterdir())


def test_index_hardlink_and_failed_publication(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    original = tmp_path / "external.md"
    destination = tmp_path / "index.md"
    original.write_bytes(b"authored bytes")
    if sys.platform != "win32":
        original.chmod(0o640)
    destination.hardlink_to(original)
    index.publish(destination, "new index\n")
    assert original.read_bytes() == b"authored bytes"
    assert destination.read_bytes() == b"new index\n"
    if sys.platform != "win32":
        assert destination.stat().st_mode & 0o777 == 0o640

    def fail(_source: Path, _target: Path) -> Path:
        raise OSError("controlled publication failure")

    monkeypatch.setattr(Path, "replace", fail)
    with pytest.raises(OSError, match="publication failure"):
        index.publish(destination, "bad replacement\n")
    assert destination.read_bytes() == b"new index\n"
    assert sorted(path.name for path in tmp_path.iterdir()) == ["external.md", "index.md"]


def test_competing_temporary_file_is_preserved(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    temporary = tmp_path / ".skills-index-collision"
    temporary.write_bytes(b"competing writer")

    def collision(_size: int) -> str:
        return "collision"

    monkeypatch.setattr(secrets, "token_hex", collision)
    with pytest.raises(FileExistsError):
        index.publish(tmp_path / "index.md", "candidate\n")
    assert temporary.read_bytes() == b"competing writer"
    assert not (tmp_path / "index.md").exists()


def test_real_index_and_independent_cli_outputs(tmp_path: Path) -> None:
    expected = (ROOT / ".llm/skills/index.md").read_bytes()
    assert index.render(ROOT / ".llm/skills").encode("utf-8") == expected
    for name in ("first index.md", "second index.md"):
        destination = tmp_path / name
        result = subprocess.run(
            [
                sys.executable,
                str(ROOT / "scripts/generate_skills_index.py"),
                "--output-path",
                str(destination),
            ],
            cwd=tmp_path,
            check=False,
            capture_output=True,
            timeout=30,
        )
        assert result.returncode == 0, result.stdout + result.stderr
        assert destination.read_bytes() == expected
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/generate_skills_index.py"), "--stdout"],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=30,
    )
    assert result.returncode == 0
    assert result.stdout == expected


def test_legacy_generator_stdout_bytes_and_foreign_directory(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [pwsh, "-NoProfile", "-File", str(ROOT / "scripts/generate-skills-index.ps1"), "-Stdout"],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert result.stdout == (ROOT / ".llm/skills/index.md").read_bytes()


def test_legacy_linter_policy_only_foreign_directory(tmp_path: Path) -> None:
    pwsh = shutil.which("pwsh")
    assert pwsh is not None
    result = subprocess.run(
        [
            pwsh,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/lint-llm-instructions.ps1"),
            "-AuthorshipPolicyOnly",
        ],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "validation passed" in result.stdout


def test_instruction_cli_policy_success_and_failure(guidance: Path, tmp_path: Path) -> None:
    command = [
        sys.executable,
        str(ROOT / "scripts/lint_llm_instructions.py"),
        "--repo-root",
        str(guidance),
        "--authorship-policy-only",
    ]
    result = subprocess.run(
        command, cwd=tmp_path, check=False, capture_output=True, encoding="utf-8", timeout=30
    )
    assert result.returncode == 0, result.stdout + result.stderr
    source = guidance / "AGENTS.md"
    source.write_text(
        source.read_text(encoding="utf-8").replace("](./.llm/context.md)", "](./.llm/missing.md)"),
        encoding="utf-8",
    )
    result = subprocess.run(
        command, cwd=tmp_path, check=False, capture_output=True, encoding="utf-8", timeout=30
    )
    assert result.returncode == 1
    assert "must delegate to .llm/context.md" in result.stdout


def test_legacy_migration_node_launcher_foreign_directory(tmp_path: Path) -> None:
    node = shutil.which("node")
    assert node is not None
    result = subprocess.run(
        [node, str(ROOT / "scripts/migrate-agent-skills.mjs"), "--check"],
        cwd=tmp_path,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=60,
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "reconstructed skill splits" in result.stdout


@pytest.mark.parametrize(
    "launcher",
    [
        "scripts/tests/test-agent-skill-migration.mjs",
        "scripts/tests/test-llm-instructions-lint.ps1",
    ],
)
def test_legacy_test_launchers_reach_selected_control(launcher: str, tmp_path: Path) -> None:
    executable = shutil.which("node" if launcher.endswith(".mjs") else "pwsh")
    assert executable is not None
    command = [executable]
    if launcher.endswith(".ps1"):
        command.extend(["-NoProfile", "-File"])
    command.append(str(ROOT / launcher))
    command.extend(
        ["-TestFilter" if launcher.endswith(".ps1") else "-k", "test_original_parent_link_refusal"]
    )
    result = subprocess.run(
        command, cwd=tmp_path, check=False, capture_output=True, encoding="utf-8", timeout=60
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert "1 passed" in result.stdout


@pytest.fixture(scope="session")
def guidance_template(tmp_path_factory: pytest.TempPathFactory) -> Path:
    root = tmp_path_factory.mktemp("instruction-template")
    git = shutil.which("git")
    assert git is not None
    result = subprocess.run(
        [git, "ls-files", "-z", "--", ".llm", ".agents/skills", ".claude/skills"],
        cwd=ROOT,
        check=True,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    paths = [file for file in result.stdout.split("\0") if file]
    paths.extend(
        [
            "AGENTS.md",
            "CLAUDE.md",
            ".cursorrules",
            ".github/copilot-instructions.md",
            ".prettierrc.json",
            "scripts/pr_feedback.py",
            "scripts/generate_skills_index.py",
            "scripts/generate_agent_skill_stubs.py",
            "scripts/migrate_agent_skills.py",
        ]
    )
    for name in paths:
        destination = root / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(ROOT / name, destination)
    return root


@pytest.fixture
def guidance(guidance_template: Path, tmp_path: Path) -> Path:
    shutil.copytree(guidance_template, tmp_path, dirs_exist_ok=True)
    return tmp_path


POLICY_MUTATIONS = [
    (
        ".llm/references/context-agent-operations.md",
        r"GitHub MCP server \*\*FIRST\*\*",
        "GitHub MCP when convenient",
    ),
    (
        ".llm/references/context-agent-operations.md",
        r"(?i)announce the\s+capability gap in the same message as the fallback",
        "mention the gap eventually",
    ),
    (
        ".llm/references/context-agent-operations.md",
        "DISCLOSURE: LLM-GENERATED TEXT",
        "REMOVED DISCLOSURE",
    ),
    (".llm/skills/github-operations.md", "DISCLOSURE: LLM-GENERATED TEXT", "REMOVED DISCLOSURE"),
    (
        ".llm/skills/github-operations.md",
        r"(?i)announce the\s+missing\s+capability in the same message that runs the fallback",
        "quietly note the gap eventually",
    ),
    (
        ".llm/references/ship-changes-part-2.md",
        "DISCLOSURE: LLM-GENERATED TEXT",
        "REMOVED DISCLOSURE",
    ),
    (
        "scripts/pr_feedback.py",
        "OUTSIDE OR UNKNOWN -- USER INPUT REQUIRED",
        "AUTOMATIC ACTION ALLOWED",
    ),
    ("AGENTS.md", r"\]\(\./\.llm/context\.md\)", "](./.llm/missing.md)"),
]


@pytest.mark.parametrize(("filename", "pattern", "replacement"), POLICY_MUTATIONS)
def test_each_original_policy_mutation_fails(
    filename: str, pattern: str, replacement: str, guidance: Path
) -> None:
    import re

    source = guidance / filename
    original = source.read_text(encoding="utf-8")
    mutated = re.sub(pattern, replacement, original)
    assert mutated != original
    source.write_text(mutated, encoding="utf-8")
    assert lint.main(["--repo-root", str(guidance), "--authorship-policy-only"]) == 1


@pytest.mark.parametrize("case", POLICY_CASES)
def test_each_frozen_policy_requirement_remains_enforced(
    case: dict[str, object], guidance: Path
) -> None:
    group, pattern, label, fragment = (
        case[name] for name in ("group", "pattern", "label", "fragment")
    )
    assert isinstance(group, str)
    assert isinstance(pattern, str)
    assert isinstance(label, str)
    assert isinstance(fragment, str)
    assert (pattern, label) in lint.POLICY_REQUIREMENTS[group]
    files = {
        "context": [
            guidance / ".llm/context.md",
            guidance / ".llm/references/context-agent-operations.md",
        ],
        "github-operations": [guidance / ".llm/skills/github-operations.md"],
        "ship-changes": [
            guidance / ".llm/skills/ship-changes.md",
            *sorted((guidance / ".llm/references").glob("ship-changes-part-*.md")),
        ],
        "pr-feedback": [guidance / "scripts/pr_feedback.py"],
    }[group]
    fragment = fragment.replace("\r\n", "\n")
    if not fragment:
        assert label == "fail-closed HTTP and JSON handling"
        path = guidance / "scripts/pr_feedback.py"
        content = path.read_text(encoding="utf-8")
        assert "raise FeedbackError" in content
        path.write_text(
            content.replace("raise FeedbackError", "raise RemovedFeedbackError"), encoding="utf-8"
        )
    else:
        owners = [path for path in files if fragment in path.read_text(encoding="utf-8")]
        assert owners, label
        path = owners[0]
        content = path.read_text(encoding="utf-8")
        path.write_text(content.replace(fragment, "REMOVED POLICY REQUIREMENT"), encoding="utf-8")
    errors = lint.policy_errors(guidance)
    assert any(error.endswith(f": {label}.") for error in errors), errors


def test_clean_guidance_full_and_policy_only(guidance: Path) -> None:
    assert lint.validate(guidance, authorship_only=True) == 0
    assert lint.validate(guidance) == 0


@pytest.mark.parametrize("mutation", ["drift", "bom", "crlf", "missing"])
def test_index_drift_encoding_and_presence_fail(mutation: str, guidance: Path) -> None:
    path = guidance / ".llm/skills/index.md"
    data = path.read_bytes()
    if mutation == "missing":
        path.unlink()
    else:
        path.write_bytes(
            {
                "drift": data + b"drift\n",
                "bom": b"\xef\xbb\xbf" + data,
                "crlf": data.replace(b"\n", b"\r\n"),
            }[mutation]
        )
    assert lint.main(["--repo-root", str(guidance)]) == 1


def test_index_fix_restores_original_bytes(guidance: Path) -> None:
    path = guidance / ".llm/skills/index.md"
    data = path.read_bytes()
    path.write_bytes(data + b"drift\n")
    assert lint.main(["--repo-root", str(guidance), "--fix"]) == 0
    assert path.read_bytes() == data


@pytest.mark.parametrize(
    "replacement",
    [
        "exception contract — every",
        "exception contract | extra | every",
        "exception contract - every",
    ],
)
def test_trigger_mutations_fail_without_partial_fix(replacement: str, guidance: Path) -> None:
    path = guidance / ".llm/skills/serialization-safety.md"
    original = path.read_text(encoding="utf-8")
    if replacement == "exception contract - every":
        mutated = original.replace("| Core -->", "| Core Skills -->")
    else:
        mutated = original.replace("exception contract - every", replacement)
    assert mutated != original
    path.write_text(mutated, encoding="utf-8")
    index_path = guidance / ".llm/skills/index.md"
    before = index_path.read_bytes()
    assert lint.main(["--repo-root", str(guidance), "--fix"]) == 1
    assert index_path.read_bytes() == before


@pytest.mark.parametrize(
    "filename",
    [".llm/context.md", "AGENTS.md", "scripts/pr_feedback.py", "scripts/generate_skills_index.py"],
)
def test_missing_required_inputs_fail(filename: str, guidance: Path) -> None:
    (guidance / filename).unlink()
    assert lint.main(["--repo-root", str(guidance), "--authorship-policy-only"]) == 1


@pytest.mark.parametrize(
    ("content", "expected"),
    [
        ("# Real H1\n\n```bash\n# comment\n```\nx", 1),
        ("# Real H1\n\n~~~sh\n# fenced\n~~~", 1),
        ("# First\n\n# Second", 2),
        ("```\n# fenced\n```\n# Real H1", 1),
        ("# Real H1\n```bash\n# swallowed", 1),
        ("# Real H1\n````\n```\n# inside\n```\n````", 1),
    ],
)
def test_original_code_block_heading_controls(content: str, expected: int) -> None:
    assert len(lint.h1_lines(content)) == expected
