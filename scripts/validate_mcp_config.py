"""Validate repository-local MCP configuration without changing it."""

from __future__ import annotations

import argparse
import codecs
import re
import shutil
import subprocess
import sys
from pathlib import Path
from typing import cast

import powershell_json
from cli_encoding import configure_output, escape_command_data

ROOT = Path(__file__).resolve().parents[1]
LOCAL_CONFIGS = (
    ".mcp.json",
    ".cursor/mcp.json",
    ".vscode/mcp.json",
    ".codex/config.toml",
    "opencode.json",
    ".nanocoder/mcp.json",
    ".copilot/mcp-config.json",
    ".env.local",
)
JSON_CONFIGS = {
    ".mcp.json": "mcpServers",
    ".cursor/mcp.json": "mcpServers",
    ".vscode/mcp.json": "servers",
    "opencode.json": "mcp",
    ".nanocoder/mcp.json": "mcpServers",
    ".copilot/mcp-config.json": "mcpServers",
}
CODEX_CONFIG = ".codex/config.toml"
BRIDGE = "scripts/mcp/unity-mcp.mjs"
DOCS = ("scripts/mcp/README.md", "docs/guides/mcp-local-setup.md")
SIBLING_PORTS = {
    "9003": "DxMessaging",
    "9004": "IshoBoy",
    "9010": "DoxReloaded",
    "9020": "qora-redux",
}
DOC_REFERENCE = re.compile(r"(?<![\w/])scripts/mcp/[A-Za-z0-9._/-]+\.(?:sh|ps1|mjs)\b")
CODEX_UNITY = re.compile(r"(?ms)^\s*\[mcp_servers\.unity_mcp_remote\]\s*(.*?)(?=^\s*\[|\Z)")
CODEX_GITHUB = re.compile(r"(?im)^\s*\[mcp_servers\.github\]\s*$")
PREFIX = "[validate-mcp-config]"


def read_text(path: Path) -> str:
    data = path.read_bytes()
    if data.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE)):
        return data.decode("utf-32")
    if data.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
        return data.decode("utf-16")
    return data.decode("utf-8-sig")


def ignored_configs(root: Path) -> set[str]:
    git = shutil.which("git")
    if git is None:
        raise ValueError(
            "git is not available on PATH. validate-mcp-config requires git check-ignore."
        )
    paths = ("\0".join(LOCAL_CONFIGS) + "\0").encode("ascii")
    result = subprocess.run(  # noqa: S603
        [git, "check-ignore", "-z", "--stdin"],
        cwd=root,
        input=paths,
        capture_output=True,
        check=False,
    )
    if result.returncode not in (0, 1):
        raise ValueError(
            f"git check-ignore failed (exit {result.returncode}). Is '{root}' a git repository?"
        )
    return set(result.stdout.decode("ascii").split("\0"))


def property_names(value: object) -> dict[str, object]:
    if isinstance(value, dict):
        return {key.lower(): item for key, item in cast("dict[str, object]", value).items()}
    return {}


def url_values(value: object) -> tuple[str, ...]:
    if value is None:
        return ()
    if isinstance(value, list):
        return tuple(str(item) if item is not None else "" for item in cast("list[object]", value))
    return (str(value),)


class Validation:
    def __init__(self, root: Path, *, verbose: bool) -> None:
        self.root = root
        self.verbose = verbose
        self.errors: list[str] = []

    def info(self, message: str) -> None:
        if self.verbose:
            print(f"{PREFIX} {message}")

    def error(self, path: str, code: str, message: str) -> None:
        self.errors.append(f"::error file={path}::{code}: {escape_command_data(message)}")

    def check_ignores(self) -> None:
        self.info("Check 1: machine-local MCP configs are gitignored...")
        ignored = ignored_configs(self.root)
        for path in LOCAL_CONFIGS:
            if path in ignored:
                self.info(f"  gitignored OK: {path}")
            else:
                self.error(
                    ".gitignore",
                    "UNH-MCP-TRACKED",
                    f"'{path}' is a machine-local MCP config (per-developer host:port) "
                    "and MUST be gitignored. Add it to .gitignore.",
                )

    def check_json_config(self, path: str, server_key: str) -> None:
        config = self.root / path
        if not config.exists():
            return
        try:
            text = read_text(config)
            document = powershell_json.loads(text) if text.strip() else None
        except (OSError, ValueError, RecursionError):
            self.error(path, "UNH-MCP-INVALID", "not valid JSON.")
            return
        if isinstance(document, list):
            members = cast("list[object]", document)
            document = members[0] if len(members) == 1 else None
        properties = property_names(document)
        if server_key.lower() not in properties:
            raise ValueError(f"{path}: the property '{server_key}' cannot be found on this object.")
        servers = property_names(properties[server_key.lower()])
        server = property_names(servers.get("unity-mcp-remote"))
        urls = url_values(server.get("url"))
        url = " ".join(urls)
        if not url.strip():
            if "github" in servers:
                self.info(f"  shared-only config OK: {path}")
            else:
                self.error(
                    path,
                    "UNH-MCP-INVALID",
                    f"missing '{server_key}.unity-mcp-remote.url'.",
                )
        elif any(re.search(r"/mcp/?$", item) is None for item in urls):
            self.error(
                path,
                "UNH-MCP-INVALID",
                f"unity-mcp-remote url '{url}' should end with '/mcp'.",
            )
        else:
            self.info(f"  config OK: {path} -> {escape_command_data(url)}")

    def check_codex_config(self) -> None:
        config = self.root / CODEX_CONFIG
        if not config.exists():
            return
        text = read_text(config)
        block = CODEX_UNITY.search(text)
        if block is None:
            if CODEX_GITHUB.search(text):
                self.info(f"  shared-only config OK: {CODEX_CONFIG}")
            else:
                self.error(
                    CODEX_CONFIG,
                    "UNH-MCP-INVALID",
                    "missing [mcp_servers.unity_mcp_remote] block.",
                )
            return
        body = "\n".join(line for line in block[1].split("\n") if not line.lstrip().startswith("#"))
        if not re.search(r'url\s*=\s*"[^"]*/mcp"', body):
            self.error(
                CODEX_CONFIG,
                "UNH-MCP-INVALID",
                "unity_mcp_remote url must be set and end with '/mcp'.",
            )
        else:
            self.info(f"  config OK: {CODEX_CONFIG}")

    def check_docs(self) -> None:
        self.info("Check 3: MCP doc script references resolve...")
        for doc in DOCS:
            path = self.root / doc
            if not path.exists():
                continue
            for reference in dict.fromkeys(DOC_REFERENCE.findall(read_text(path))):
                if (self.root / reference).exists():
                    self.info(f"  reference OK: {reference}")
                else:
                    self.error(
                        doc,
                        "UNH-MCP-MISSINGREF",
                        f"references '{reference}' which does not exist. Add the script or remove the reference.",
                    )

    def check_bridge(self) -> None:
        self.info("Check 4: the bridge default port is this repository's own...")
        bridge = self.root / BRIDGE
        if not bridge.exists():
            self.error(BRIDGE, "UNH-MCP-MISSINGREF", "the Unity MCP bridge is missing.")
            return
        text = read_text(bridge)
        if not re.search(r"(?im)^\s*port:\s*9007,", text):
            self.error(BRIDGE, "UNH-MCP-PORT", "the bridge default port must be 9007.")
        fallback = r"FALLBACK_PORTS\s*=\s*Object\.freeze\(\["
        if not re.search(fallback + r"9007\]\)", text, re.IGNORECASE):
            self.error(
                BRIDGE,
                "UNH-MCP-PORT",
                "discovery must fall back to 9007 only; probing a sibling's port is how a config "
                "lands on another project's editor.",
            )
        for port, project in SIBLING_PORTS.items():
            if re.search(rf"(?im)^\s*port:\s*{port},", text) or re.search(
                fallback + rf"[^\]]*{port}", text, re.IGNORECASE
            ):
                self.error(
                    BRIDGE,
                    "UNH-MCP-PORT",
                    f"port {port} belongs to {project}; this repository uses 9007.",
                )

    def run(self) -> int:
        self.check_ignores()
        self.info("Check 2: present MCP configs are valid and target /mcp...")
        for path, server_key in JSON_CONFIGS.items():
            self.check_json_config(path, server_key)
        self.check_codex_config()
        self.check_docs()
        self.check_bridge()
        if self.errors:
            print("MCP config validation FAILED:", *self.errors, "", sep="\n")
            print("See docs/guides/mcp-local-setup.md and scripts/mcp/README.md.")
            return 1
        print(f"{PREFIX} OK: MCP client configs are gitignored, valid, and doc references resolve.")
        return 0


def main() -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__, allow_abbrev=False)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args()
    try:
        return Validation(args.repo_root.resolve(strict=True), verbose=args.verbose).run()
    except (OSError, ValueError) as error:
        print(f"{PREFIX} {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
