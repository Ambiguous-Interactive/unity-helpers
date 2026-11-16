"""Preserve check selection, failure gates, and native CLI behavior during migration."""

from __future__ import annotations

import json
import subprocess
import sys
from dataclasses import asdict
from pathlib import Path
from typing import Literal

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_runner
from check_registries import ROOT, checks_for, load_checks, main


def write_registry(root: Path, name: str, checks: object) -> None:
    destination = root / "scripts" / f"{name}-checks.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(checks), encoding="utf-8")


def entry(identifier: str = "subject", command: str = "exit 0") -> dict[str, str]:
    return {"id": identifier, "name": "Subject", "run": command}


@pytest.mark.parametrize("registry", ["lint", "contracts"])
def test_cli_lists_exact_order_without_a_node_process(
    registry: Literal["lint", "contracts"], tmp_path: Path
) -> None:
    result = subprocess.run(
        [sys.executable, str(ROOT / "scripts/check_registries.py"), registry, "--list"],
        cwd=tmp_path,
        capture_output=True,
        text=True,
        check=False,
        timeout=20,
    )
    assert result.returncode == 0, result.stderr
    assert result.stdout.splitlines() == [check.id for check in checks_for(registry)]
    assert '{"kind"' not in result.stdout


@pytest.mark.parametrize("registry", ["lint", "contracts"])
@pytest.mark.parametrize("direct", [False, True])
def test_direct_selection_retains_every_check_and_policy(
    registry: Literal["lint", "contracts"], direct: bool
) -> None:
    original = checks_for(registry)
    selected = checks_for(registry, direct=direct)
    assert len(original) == len(selected) > 0
    scripts = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))["scripts"]
    for before, after in zip(original, selected, strict=True):
        assert asdict(before) | {"run": after.run} == asdict(after)
        expected = (
            scripts[before.run[8:]] if direct and before.run.startswith("npm run ") else before.run
        )
        assert after.run == expected


def test_hooks_precede_shared_checks_and_retain_exclusion_policy() -> None:
    checks = checks_for("contracts", include_hooks=True)
    assert checks == [*load_checks("hooks"), *load_checks("contracts")]
    assert len({check.id for check in checks}) == len(checks)
    assert all(check.exclusive for check in checks if check.id == "npm-package-changelog")
    completion = [check for check in checks if check.completion_pattern is not None]
    assert any(check.id == "unity-acceptance-runner" for check in completion)


@pytest.mark.parametrize(
    "contents",
    [
        [],
        [entry(), entry()],
        [entry("")],
        [entry(command=" ")],
        [entry() | {"name": ""}],
        {},
    ],
)
def test_rejects_empty_duplicate_or_invalid_registry(contents: object, tmp_path: Path) -> None:
    write_registry(tmp_path, "lint", contents)
    with pytest.raises(ValueError, match=r"[Cc]heck (registry|array)"):
        load_checks("lint", tmp_path)


def test_duplicate_ids_across_hook_and_contract_registries_are_rejected(
    tmp_path: Path,
) -> None:
    write_registry(tmp_path, "hooks", [entry()])
    write_registry(tmp_path, "contracts", [entry()])
    with pytest.raises(ValueError, match="duplicate"):
        checks_for("contracts", include_hooks=True, root=tmp_path)


@pytest.mark.parametrize("value", [None, 5, "", " "])
def test_missing_or_invalid_package_command_fails_closed(value: object, tmp_path: Path) -> None:
    write_registry(tmp_path, "contracts", [entry(command="npm run absent")])
    (tmp_path / "package.json").write_text(
        json.dumps({"scripts": {"absent": value}}), encoding="utf-8"
    )
    with pytest.raises(ValueError, match="No package script resolves"):
        checks_for("contracts", direct=True, root=tmp_path)


def test_non_npm_commands_survive_direct_selection(tmp_path: Path) -> None:
    write_registry(tmp_path, "lint", [entry(command="python subject.py --flag")])
    (tmp_path / "package.json").write_text('{"scripts": {}}', encoding="utf-8")
    assert checks_for("lint", direct=True, root=tmp_path)[0].run == "python subject.py --flag"


@pytest.mark.parametrize("arguments", [[], ["unknown"], ["lint", "--include-hook-regressions"]])
def test_invalid_cli_fails_closed(arguments: list[str], monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(arguments) == 1


@pytest.mark.parametrize("arguments", [["--only", "absent"], ["--jobs", "0"], ["--jobs", "nan"]])
def test_selection_errors_return_failure_without_running_checks(
    arguments: list[str],
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(["contracts", *arguments]) == 1
    assert "::error::" in capsys.readouterr().out


@pytest.mark.parametrize("field", [{"exclusive": "true"}, {"completionPatern": "done"}])
def test_misspelled_or_mistyped_safety_policy_fails_closed(
    field: dict[str, str], tmp_path: Path
) -> None:
    write_registry(tmp_path, "contracts", [entry() | field])
    with pytest.raises(ValueError, match=r"(configuration field|boolean)"):
        checks_for("contracts", root=tmp_path)


def test_affected_selection_keeps_dependencies_and_unclassified_checks(
    tmp_path: Path,
) -> None:
    from check_registries import affected_checks

    write_registry(
        tmp_path,
        "lint",
        [
            entry("docs") | {"inputs": ["docs/**"], "dependencies": ["control"]},
            entry("control") | {"inputs": ["scripts/**"]},
            entry("code") | {"inputs": ["Runtime/**"]},
            entry("unclassified"),
        ],
    )
    checks = load_checks("lint", tmp_path)
    assert [check.id for check in affected_checks(checks, ["docs/guide.md"])] == [
        "docs",
        "control",
        "unclassified",
    ]
    assert [check.id for check in affected_checks(checks, ["Runtime/Thing.cs"])] == [
        "code",
        "unclassified",
    ]
    assert affected_checks(checks, []) == []


@pytest.mark.parametrize(
    ("identifier", "owned"),
    [
        ("unsafe-code-test", "scripts/lint-unsafe-code.js"),
        ("unsafe-code-test", "Generator~/WallstopStudios.UnityHelpers.SyntaxPolicy/Program.cs"),
        ("unity-mode-coordinator", "scripts/unity/run_ci_modes.py"),
        ("unity-mode-coordinator", "scripts/tests/test_editmode_shards.py"),
    ],
)
def test_expensive_tooling_controls_follow_their_implementation_inputs(
    identifier: str, owned: str
) -> None:
    from check_registries import affected_checks

    checks = load_checks("lint")
    assert identifier not in {
        check.id for check in affected_checks(checks, ["scripts/run-prettier.js"])
    }
    for file in (owned, "package.json", "scripts/git-staging-helpers.ps1"):
        assert identifier in {check.id for check in affected_checks(checks, [file])}


@pytest.mark.parametrize(
    "file", [".github/workflows/build-publish-devcontainer.yml", ".devcontainer/devcontainer.json"]
)
def test_developer_configuration_inputs_keep_their_ci_gate(file: str) -> None:
    from check_registries import affected_checks

    assert "devcontainer-config" in {
        check.id for check in affected_checks(load_checks("lint"), [file])
    }


@pytest.mark.parametrize(
    ("registry", "file", "expected"),
    [
        ("contracts", "Runtime/Utils/RuntimeSingleton.cs", ("sync-script-contracts",)),
        (
            "contracts",
            "Tests/Runtime/Performance/CachePerformanceTests.cs",
            ("unity-workflow-matrix-contract",),
        ),
        ("contracts", "Tests/Runtime/Fixture.cs", ("lint-tests",)),
        (
            "contracts",
            "Tests/Runtime/Fixtures.asmdef",
            ("asmdef-discovery", "lint-typecheck-asmdef-references"),
        ),
        (
            "contracts",
            "Tests/Runtime/Serialization/ProtoBenchmarkCorpusIdentity.cs",
            ("wproto-corpus",),
        ),
        ("contracts", "Tests/Runtime/SingletonTests.cs", ("sync-script-contracts",)),
        ("contracts", "Runtime/WallstopStudios.UnityHelpers.asmdef", ("sync-script-contracts",)),
        ("lint", ".prettierrc.json", ("format-md", "format-json", "format-js", "format-yaml")),
        ("lint", ".prettierrc.yaml", ("format-md", "format-json", "format-js", "format-yaml")),
        ("lint", "prettier.config.js", ("format-md", "format-json", "format-js", "format-yaml")),
        ("lint", "Runtime/Assembly.asmdef", ("format-json",)),
        ("lint", "Runtime/Assembly.asmref", ("format-json",)),
        ("lint", "Runtime/settings.jsonc", ("format-json",)),
        ("lint", "Runtime/plugin.jslib", ("format-js",)),
        ("lint", "docs/guide.markdown", ("format-md",)),
    ],
)
def test_shared_source_and_formatter_inputs_keep_their_controls(
    registry: str, file: str, expected: tuple[str, ...]
) -> None:
    from check_registries import affected_checks

    selected = {check.id for check in affected_checks(load_checks(registry), [file])}
    assert set(expected).issubset(selected)


def test_affected_selection_rejects_missing_dependencies_and_cycles(
    tmp_path: Path,
) -> None:
    from check_registries import affected_checks

    for checks in [
        [entry("a") | {"dependencies": ["absent"]}],
        [entry("a") | {"dependencies": ["b"]}, entry("b") | {"dependencies": ["a"]}],
    ]:
        write_registry(tmp_path, "lint", checks)
        with pytest.raises(ValueError, match="dependency"):
            affected_checks(load_checks("lint", tmp_path), ["docs/guide.md"])


@pytest.mark.parametrize(
    "pattern",
    [
        "docs/features/**",
        "docs/*/serialization/*.md",
        "docs/features/serialization/guide.?d",
        "docs/features/serialization/[g]uide.md",
    ],
)
def test_focused_selection_shares_patterns_and_dependencies(pattern: str) -> None:
    from check_registries import affected_checks

    checks = check_runner.read_checks(
        [
            entry("owner") | {"inputs": [pattern, "@tooling"], "dependencies": ["control"]},
            entry("control") | {"inputs": ["@tooling"]},
            entry("broad") | {"inputs": ["@tooling"]},
            entry("unscoped"),
        ]
    )
    assert [
        check.id
        for check in affected_checks(
            checks, ["docs/features/serialization/guide.md"], direct_inputs_only=True
        )
    ] == ["owner", "control"]
    assert affected_checks(checks, ["scripts/unowned.js"], direct_inputs_only=True) == []
    assert affected_checks(checks, ["unknown.config"], direct_inputs_only=True) == []


def test_focused_ids_cli_returns_owned_controls(capsys: pytest.CaptureFixture[str]) -> None:
    assert (
        main(["contracts", "--focused-ids", "--files", "docs/features/serialization/guide.md"]) == 0
    )
    assert "wproto-annotations" in json.loads(capsys.readouterr().out)
    assert main(["contracts", "--focused-ids"]) == 1
    assert "requires --files" in capsys.readouterr().err


@pytest.mark.parametrize("ci", [False, True])
def test_affected_cli_delegates_base_inventory_to_shared_planner(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str], ci: bool
) -> None:
    import agent_check

    def inventory(base: str) -> list[str]:
        assert base == "origin/main"
        return ["docs/guide.md"]

    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    monkeypatch.setattr(agent_check, "changed_files", inventory)
    monkeypatch.setenv("GITHUB_EVENT_NAME", "pull_request")
    monkeypatch.setenv("AGENT_CHECK_BASE", "origin/main")
    assert main(["lint", "--list", *(["--ci"] if ci else ["--base", "origin/main"])]) == 0
    output = capsys.readouterr().out.splitlines()
    assert "doc-links" in output
    assert "doc-links-test" in output
    assert "csharp-naming" not in output


@pytest.mark.parametrize(
    "arguments",
    [["lint", "--base"], ["lint", "--files"], ["lint", "--base", "main", "--files", "docs/a.md"]],
)
def test_affected_cli_refuses_ambiguous_or_empty_inventory(
    arguments: list[str], monkeypatch: pytest.MonkeyPatch
) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(arguments) == 1


def test_known_documentation_without_relevant_suites_is_inapplicable(tmp_path: Path) -> None:
    from check_registries import affected_checks

    write_registry(tmp_path, "contracts", [entry("tool") | {"inputs": ["scripts/**"]}])
    checks = load_checks("contracts", tmp_path)
    assert affected_checks(checks, ["docs/guide.md"]) == []
    assert affected_checks(checks, ["unknown.config"]) == checks
    assert affected_checks(checks, ["docs/program.sh"]) == checks


def test_empty_affected_scope_cannot_hide_invalid_check_request(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    assert main(["contracts", "--only", "absent", "--files", "docs/guide.md"]) == 1


def test_ci_main_keeps_full_registry_even_with_stale_pr_base(
    monkeypatch: pytest.MonkeyPatch, capsys: pytest.CaptureFixture[str]
) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    monkeypatch.setenv("GITHUB_EVENT_NAME", "push")
    monkeypatch.setenv("AGENT_CHECK_BASE", "not-a-real-revision")
    assert main(["lint", "--ci", "--list"]) == 0
    assert capsys.readouterr().out.splitlines() == [check.id for check in checks_for("lint")]


def test_ci_pr_requires_its_base(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(check_runner, "OUTPUT_FORMAT", "json")
    monkeypatch.setenv("GITHUB_EVENT_NAME", "pull_request")
    monkeypatch.delenv("AGENT_CHECK_BASE", raising=False)
    assert main(["lint", "--ci", "--list"]) == 1
    assert main(["lint", "--ci", "--base", "HEAD"]) == 1
