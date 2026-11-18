"""Exercise the validator with isolated Git repositories and synthetic MCP files."""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = ROOT / "scripts/validate_mcp_config.py"
CONFIG_PATHS = (
    ".mcp.json",
    ".cursor/mcp.json",
    ".vscode/mcp.json",
    ".codex/config.toml",
    "opencode.json",
    ".nanocoder/mcp.json",
    ".copilot/mcp-config.json",
    ".env.local",
)
JSON_CONFIGS = (
    (".mcp.json", "mcpServers"),
    (".cursor/mcp.json", "mcpServers"),
    (".vscode/mcp.json", "servers"),
    ("opencode.json", "mcp"),
    (".nanocoder/mcp.json", "mcpServers"),
    (".copilot/mcp-config.json", "mcpServers"),
)
BRIDGE = "scripts/mcp/unity-mcp.mjs"
BRIDGE_TEXT = (
    "export const DEFAULTS = {\n  port: 9007,\n};\nconst FALLBACK_PORTS = Object.freeze([9007]);\n"
)
CODEX_UNITY = "[mcp_servers.unity_mcp_remote]\n"
CODEX_GITHUB = '[mcp_servers.github]\ncommand = "bash"\n'
URL = "http://example.test:9007/mcp"
OK = "[validate-mcp-config] OK: MCP client configs are gitignored, valid, and doc references resolve.\n"
FOOTER = "See docs/guides/mcp-local-setup.md and scripts/mcp/README.md.\n"


def write(root: Path, path: str, text: str) -> None:
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8")


def git(root: Path, *args: str) -> None:
    executable = shutil.which("git")
    assert executable is not None
    subprocess.run([executable, *args], cwd=root, check=True, capture_output=True, timeout=10)


def run(root: Path, *args: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(VALIDATOR), "--repo-root", str(root), *args],
        cwd=root.parent,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=15,
    )


def snapshot(root: Path) -> dict[Path, bytes]:
    return {
        path.relative_to(root): path.read_bytes()
        for path in root.rglob("*")
        if path.is_file() and ".git" not in path.relative_to(root).parts
    }


@pytest.fixture
def repository(tmp_path: Path) -> Path:
    root = tmp_path / "mcp repo [fixture]"
    root.mkdir()
    git(root, "init", "-q")
    git(root, "config", "core.excludesFile", str(root / "empty-excludes"))
    write(root, "empty-excludes", "")
    write(root, ".gitignore", "\n".join(CONFIG_PATHS) + "\n")
    write(root, BRIDGE, BRIDGE_TEXT)
    return root


def assert_result(result: subprocess.CompletedProcess[str], *errors: str) -> None:
    assert result.stderr == ""
    if errors:
        assert result.returncode == 1
        assert (
            result.stdout == "MCP config validation FAILED:\n" + "\n".join(errors) + "\n\n" + FOOTER
        )
    else:
        assert result.returncode == 0
        assert result.stdout == OK


def ignored_error(path: str) -> str:
    return (
        f"::error file=.gitignore::UNH-MCP-TRACKED: '{path}' is a machine-local MCP config "
        "(per-developer host:port) and MUST be gitignored. Add it to .gitignore."
    )


@pytest.mark.parametrize("config_path", CONFIG_PATHS)
def test_each_local_config_must_be_ignored_even_when_absent(
    repository: Path, config_path: str
) -> None:
    write(
        repository,
        ".gitignore",
        "\n".join(path for path in CONFIG_PATHS if path != config_path),
    )
    assert_result(run(repository), ignored_error(config_path))


def test_all_unignored_paths_and_tracked_path_are_reported(repository: Path) -> None:
    write(repository, ".env.local", "SYNTHETIC=content")
    git(repository, "add", "-f", ".env.local")
    before = snapshot(repository)
    assert_result(run(repository), ignored_error(".env.local"))
    assert snapshot(repository) == before
    write(repository, ".gitignore", "# no rules")
    assert_result(run(repository), *(ignored_error(path) for path in CONFIG_PATHS))


@pytest.mark.parametrize(("path", "server_key"), JSON_CONFIGS)
@pytest.mark.parametrize("suffix", ["", "/", "/bad", " uppercase"])
def test_present_clients_preserve_url_rules(
    repository: Path, path: str, server_key: str, suffix: str
) -> None:
    endpoint = URL + suffix if suffix != " uppercase" else URL.upper()
    write(
        repository,
        path,
        json.dumps({server_key: {"unity-mcp-remote": {"url": endpoint}}}),
    )
    if suffix in ("", "/"):
        assert_result(run(repository))
    else:
        assert_result(
            run(repository),
            f"::error file={path}::UNH-MCP-INVALID: unity-mcp-remote url '{endpoint}' should end with '/mcp'.",
        )


@pytest.mark.parametrize(("path", "server_key"), JSON_CONFIGS)
def test_shared_only_client_can_omit_unity(repository: Path, path: str, server_key: str) -> None:
    write(repository, path, json.dumps({server_key: {"GitHub": None}}))
    assert_result(run(repository))


@pytest.mark.parametrize(
    "contents",
    [
        '{"mcpServers":{"github":{}}}',
        "/* comment */ {mcpServers: {github: {},},}",
        "{'mcpServers':{'github':{}}}",
        '{"mcpServers":{},"mcpServers":{"github":null}}',
        '[{"MCPSERVERS":{"UNITY-MCP-REMOTE":{"URL":["http://example.test/mcp"]}}}]',
    ],
)
def test_powershell_json_syntax_and_member_lookup(repository: Path, contents: str) -> None:
    write(repository, ".mcp.json", contents)
    assert_result(run(repository))


@pytest.mark.parametrize(
    "contents", ['{"mcpServers":', '{"mcpServers":{},"MCPSERVERS":{"github":null}}']
)
def test_json_parse_failures_are_config_annotations(repository: Path, contents: str) -> None:
    write(repository, ".mcp.json", contents)
    assert_result(run(repository), "::error file=.mcp.json::UNH-MCP-INVALID: not valid JSON.")


def test_json_bom_and_optional_env_contents(repository: Path) -> None:
    (repository / ".mcp.json").write_bytes('{"mcpServers":{"github":null}}'.encode("utf-16"))
    (repository / ".env.local").write_bytes(bytes(range(256)))
    assert_result(run(repository))


@pytest.mark.parametrize("value", [None, "", " \t", 4])
def test_missing_or_invalid_unity_url(repository: Path, value: object) -> None:
    write(
        repository,
        ".mcp.json",
        json.dumps({"mcpServers": {"unity-mcp-remote": {"url": value}}}),
    )
    message = (
        "unity-mcp-remote url '4' should end with '/mcp'."
        if value == 4
        else "missing 'mcpServers.unity-mcp-remote.url'."
    )
    assert_result(run(repository), "::error file=.mcp.json::UNH-MCP-INVALID: " + message)


@pytest.mark.parametrize("content", [CODEX_GITHUB, CODEX_UNITY + f'url = "{URL}"\n'])
def test_valid_codex_config(repository: Path, content: str) -> None:
    write(repository, ".codex/config.toml", content)
    assert_result(run(repository))


@pytest.mark.parametrize(
    "body",
    [
        f'url = "{URL}/"\n',
        f'# url = "{URL}"\n',
        f'url = "{URL.upper()}"\n',
        f'url = "wrong"\n[other]\nurl = "{URL}"\n',
    ],
)
def test_only_the_uncommented_unity_table_can_satisfy_codex(repository: Path, body: str) -> None:
    write(repository, ".codex/config.toml", CODEX_GITHUB + CODEX_UNITY + body)
    assert_result(
        run(repository),
        "::error file=.codex/config.toml::UNH-MCP-INVALID: unity_mcp_remote url must be set and end with '/mcp'.",
    )


def test_codex_requires_unity_or_shared_table(repository: Path) -> None:
    write(repository, ".codex/config.toml", f'[other]\nurl = "{URL}"\n')
    assert_result(
        run(repository),
        "::error file=.codex/config.toml::UNH-MCP-INVALID: missing [mcp_servers.unity_mcp_remote] block.",
    )


def test_doc_refs_are_deduplicated_per_file_and_checked_in_order(
    repository: Path,
) -> None:
    valid, missing = "scripts/mcp/existing.mjs", "scripts/mcp/missing.sh"
    write(repository, valid, "export {};")
    write(
        repository,
        "scripts/mcp/README.md",
        f"{valid} {missing} {missing} unrelated/{missing}",
    )
    write(repository, "docs/guides/mcp-local-setup.md", f"Only {missing}")
    assert_result(
        run(repository),
        *(
            f"::error file={doc}::UNH-MCP-MISSINGREF: references '{missing}' which does not exist. "
            "Add the script or remove the reference."
            for doc in ("scripts/mcp/README.md", "docs/guides/mcp-local-setup.md")
        ),
    )


def test_missing_bridge_is_an_error(repository: Path) -> None:
    (repository / BRIDGE).unlink()
    assert_result(
        run(repository),
        f"::error file={BRIDGE}::UNH-MCP-MISSINGREF: the Unity MCP bridge is missing.",
    )


@pytest.mark.parametrize(
    ("port", "project"),
    [
        ("9003", "DxMessaging"),
        ("9004", "IshoBoy"),
        ("9010", "DoxReloaded"),
        ("9020", "qora-redux"),
    ],
)
def test_sibling_ports_fail_in_a_default_or_discovery_list(
    repository: Path, port: str, project: str
) -> None:
    code = f"::error file={BRIDGE}::UNH-MCP-PORT:"
    sibling = f"{code} port {port} belongs to {project}; this repository uses 9007."
    write(repository, BRIDGE, BRIDGE_TEXT.replace("port: 9007", f"port: {port}"))
    assert_result(run(repository), f"{code} the bridge default port must be 9007.", sibling)
    write(repository, BRIDGE, BRIDGE_TEXT.replace("[9007]", f"[9007, {port}]"))
    assert_result(
        run(repository),
        f"{code} discovery must fall back to 9007 only; probing a sibling's port is how a config "
        "lands on another project's editor.",
        sibling,
    )
    write(repository, BRIDGE, f"// Sibling {project} uses {port}.\n" + BRIDGE_TEXT)
    assert_result(run(repository))


def test_verbose_stdout_exit_and_read_only_contract(repository: Path) -> None:
    write(
        repository,
        ".vscode/mcp.json",
        json.dumps({"servers": {"unity-mcp-remote": {"url": URL}}}),
    )
    doc = "scripts/mcp/README.md"
    write(repository, doc, BRIDGE)
    before = snapshot(repository)
    result = run(repository, "--verbose")
    assert result.returncode == 0
    assert result.stderr == ""
    assert (
        result.stdout
        == "\n".join(
            f"[validate-mcp-config] {line}"
            for line in (
                "Check 1: machine-local MCP configs are gitignored...",
                *(f"  gitignored OK: {path}" for path in CONFIG_PATHS),
                "Check 2: present MCP configs are valid and target /mcp...",
                f"  config OK: .vscode/mcp.json -> {URL}",
                "Check 3: MCP doc script references resolve...",
                f"  reference OK: {BRIDGE}",
                "Check 4: the bridge default port is this repository's own...",
            )
        )
        + "\n"
        + OK
    )
    assert snapshot(repository) == before


def test_invalid_root_and_missing_git_are_fatal(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    root = tmp_path / "not a Git repo"
    root.mkdir()
    bad_repo = run(root)
    assert bad_repo.returncode == 1
    assert bad_repo.stdout == ""
    assert "git check-ignore failed" in bad_repo.stderr
    monkeypatch.setenv("PATH", str(root))
    missing_git = run(root)
    assert missing_git.returncode == 1
    assert missing_git.stdout == ""
    assert "git is not available on PATH" in missing_git.stderr


def test_synthetic_url_cannot_print_a_second_workflow_command(repository: Path) -> None:
    write(
        repository,
        ".mcp.json",
        json.dumps({"mcpServers": {"unity-mcp-remote": {"url": "bad\n::error::injected"}}}),
    )
    result = run(repository)
    assert result.returncode == 1
    assert result.stderr == ""
    assert "::error::injected" not in result.stdout.splitlines()
    assert "bad%0A::error::injected" in result.stdout
    assert result.stdout.count("\n::error") == 1


def test_registry_and_npm_use_locked_python() -> None:
    package: dict[str, dict[str, str]] = json.loads(
        (ROOT / "package.json").read_text(encoding="utf-8")
    )
    assert package["scripts"]["validate:mcp-config"] == (
        "uv run --quiet --project scripts/python~ --locked --no-dev --group documentation "
        "python scripts/validate_mcp_config.py --verbose"
    )
    assert package["scripts"]["test:validate-mcp-config"] == (
        "uv run --quiet --project scripts/python~ --locked pytest scripts/tests/test_mcp_config.py -q"
    )
    for registry, entry_id, command in (
        ("lint-checks.json", "mcp-config", "npm run validate:mcp-config"),
        (
            "contracts-checks.json",
            "validate-mcp-config",
            "npm run test:validate-mcp-config",
        ),
    ):
        entries: list[dict[str, object]] = json.loads(
            (ROOT / "scripts" / registry).read_text(encoding="utf-8")
        )
        assert [entry["run"] for entry in entries if entry["id"] == entry_id] == [command]


@pytest.mark.skipif(shutil.which("pwsh") is None, reason="PowerShell CLI is unavailable")
def test_powershell_launcher_preserves_named_parameters_and_status(repository: Path) -> None:
    powershell = shutil.which("pwsh")
    assert powershell is not None
    endpoint = URL + "/invalid"
    write(
        repository, ".mcp.json", json.dumps({"mcpServers": {"unity-mcp-remote": {"url": endpoint}}})
    )
    result = subprocess.run(
        [
            powershell,
            "-NoProfile",
            "-File",
            str(ROOT / "scripts/validate-mcp-config.ps1"),
            "-RepoRoot",
            str(repository),
            "-VerboseOutput",
        ],
        cwd=repository.parent,
        check=False,
        capture_output=True,
        encoding="utf-8",
        timeout=30,
    )
    expected = run(repository, "--verbose")
    assert (result.returncode, result.stdout, result.stderr) == (
        expected.returncode,
        expected.stdout,
        expected.stderr,
    )
    assert result.returncode == 1


@pytest.mark.parametrize("content", ["", " \n ", "null", "{}", "[]", "0"])
def test_json_without_expected_container_is_fatal(repository: Path, content: str) -> None:
    write(repository, ".mcp.json", content)
    result = run(repository)
    assert result.returncode == 1
    assert result.stdout == ""
    assert "the property 'mcpServers' cannot be found" in result.stderr


def test_real_checkout_read_only_policy() -> None:
    if any((ROOT / path).exists() for path in CONFIG_PATHS):
        pytest.skip("Checkout has machine-local MCP configuration")
    assert_result(run(ROOT))
