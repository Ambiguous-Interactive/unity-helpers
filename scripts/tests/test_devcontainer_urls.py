"""Replay the Bash validator's executable contracts without live network retries."""

from __future__ import annotations

import json
import os
import re
import select
import shutil
import subprocess
import sys
import tarfile
import time
from pathlib import Path
from typing import TypedDict, cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import validate_devcontainer_urls as validator

ROOT = validator.ROOT
DOCKERFILE = ROOT / validator.DEFAULT_DOCKERFILE
LAUNCHER = ROOT / "scripts/validate-devcontainer-urls.sh"
POWERSHELL_RELEASE = (
    "https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/"
    "powershell-${POWERSHELL_VERSION}-linux-${POWERSHELL_ARCH}.tar.gz"
)


class Observation(TypedDict):
    args: list[str]
    dockerfile: str | None
    network: str | None
    exit: int
    stdout: str
    stderr: str
    calls: list[str]


class ToolVersion(TypedDict):
    version: str


class DotnetManifest(TypedDict):
    tools: dict[str, ToolVersion]


class Corpus(TypedDict):
    provenance: dict[str, str]
    cases: dict[str, Observation]


CORPUS = cast(
    "Corpus",
    json.loads(
        (Path(__file__).parent / "fixtures/devcontainer-urls-v1.json").read_text(encoding="utf-8")
    ),
)
HEAD_RESPONSES = {
    "head-ok": (0, "200"),
    "head-old-ok": (0, "200"),
    "head-transport": (7, "302"),
    "missing": (0, "404"),
}
VERSIONS = {
    "old-curl": (0, "curl 7.88.1 example"),
    "head-old-ok": (0, "curl 7.88.1 example"),
    "curl-8.3": (0, "curl 8.3.99 example"),
    "new-curl": (0, "curl 9.0.0 example"),
    "bad-version": (0, "unknown curl version"),
    "version-error": (7, ""),
}
GET_RESPONSES = {
    "range-ok": (0, "206"),
    "new-curl": (0, "206"),
    "range-ignored": (0, "200"),
    "size-limited": (63, "200"),
    "missing": (0, "404"),
    "gateway": (0, "504"),
    "redirect": (0, "302"),
    "transport": (7, "206"),
    "head-transport": (7, "206"),
    "size-limited-missing": (63, "404"),
}


@pytest.mark.parametrize("case_name", CORPUS["cases"])
def test_originating_executable_observations(
    case_name: str,
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
) -> None:
    case = CORPUS["cases"][case_name]
    args = list(case["args"])
    fixture = tmp_path / "spaced λ.Dockerfile"
    if case["dockerfile"] is not None:
        fixture.write_bytes(case["dockerfile"].encode())
        args.extend(("--dockerfile", str(fixture)))
    before = fixture.read_bytes() if fixture.is_file() else None
    calls: list[str] = []

    def fake_curl(arguments: list[str], *, suppress_errors: bool = False) -> tuple[int, str]:
        mode = case["network"]
        assert mode is not None, "An offline operation attempted a network probe."
        calls.append(" ".join("/dev/null" if part == os.devnull else part for part in arguments))
        if arguments == ["--version"]:
            assert not suppress_errors
            return VERSIONS.get(mode, (0, "curl 8.4.0 example"))
        if arguments[0] == "-sI":
            assert suppress_errors
            return HEAD_RESPONSES.get(mode, (0, "504"))
        assert not suppress_errors
        assert arguments[arguments.index("--range") + 1] == "0-0"
        assert arguments[arguments.index("--max-filesize") + 1] == "1"
        return GET_RESPONSES[mode]

    def record_sleep(seconds: int) -> None:
        calls.append(f"sleep {seconds}")

    monkeypatch.setenv("UH_DEVCONTAINER_URL_PROGRAM", "<PROGRAM>")
    monkeypatch.setattr(validator, "run_curl", fake_curl)
    monkeypatch.setattr(time, "sleep", record_sleep)
    assert validator.main(args) == case["exit"]
    output = capsys.readouterr()
    assert output.out.replace(str(fixture), "<DOCKERFILE>") == case["stdout"]
    assert output.err.replace(str(fixture), "<DOCKERFILE>") == case["stderr"]
    assert calls == case["calls"]
    if before is not None:
        assert fixture.read_bytes() == before


def test_repository_keeps_architecture_checksum_and_tool_pins() -> None:
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    assert re.search(r"^\s*ARG\s+TARGETARCH\s*$", dockerfile, re.MULTILINE)
    assert not re.search(r"^\s*ARG\s+TARGETARCH=", dockerfile, re.MULTILINE)
    assert POWERSHELL_RELEASE in dockerfile
    assert 'amd64) POWERSHELL_ARCH="x64"' in dockerfile
    assert 'arm64) POWERSHELL_ARCH="arm64"' in dockerfile
    assert "sha256sum -c -" in dockerfile
    tools = {tool.name: tool for tool in validator.extract_tools(dockerfile.split("\n")[:-1])}
    dotnet_url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/{VERSION}/dotnet-sdk-{VERSION}-linux-{ARCH}.tar.gz"
    assert tools["dotnet_sdk"] == validator.Tool(
        "dotnet_sdk", "9.0.320", "x64", "arm64", dotnet_url
    )
    assert tools["csharpier_sdk"] == validator.Tool(
        "csharpier_sdk", "10.0.403", "x64", "arm64", dotnet_url
    )
    manifest = cast(
        "DotnetManifest",
        json.loads((ROOT / ".config/dotnet-tools.json").read_text(encoding="utf-8")),
    )
    image_version = re.search(
        r"^RUN dotnet tool install -g csharpier --version ([0-9.]+)", dockerfile, re.MULTILINE
    )
    assert image_version is not None
    assert image_version[1] == manifest["tools"]["CSharpier"]["version"]


def test_compatibility_entry_points_remain_executable() -> None:
    git = shutil.which("git")
    assert git is not None
    for name in (LAUNCHER, ROOT / "scripts/tests/test-validate-devcontainer-urls.sh"):
        assert name.is_file()
        if os.name != "nt":
            assert os.access(name, os.X_OK)
        staged = subprocess.check_output(
            [git, "ls-files", "-s", "--", name.relative_to(ROOT).as_posix()],
            cwd=ROOT,
            text=True,
            encoding="utf-8",
            timeout=15,
        )
        assert staged.startswith("100755 ")


@pytest.mark.skipif(os.name == "nt", reason="The compatibility shell is tested on Linux.")
def test_original_shell_entry_point_handles_a_foreign_directory(tmp_path: Path) -> None:
    bash = shutil.which("bash")
    assert bash is not None
    result = subprocess.run(
        [bash, str(LAUNCHER), "--list-tools", "--dockerfile", validator.DEFAULT_DOCKERFILE],
        capture_output=True,
        cwd=tmp_path,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=True,
    )
    direct = subprocess.run(
        [sys.executable, str(ROOT / "scripts/validate_devcontainer_urls.py"), "--list-tools"],
        capture_output=True,
        cwd=ROOT,
        text=True,
        encoding="utf-8",
        timeout=30,
        check=True,
    )
    assert result.stdout == direct.stdout
    assert "csharpier_sdk|" in result.stdout
    assert result.stderr == ""


@pytest.mark.parametrize(
    ("architecture", "triple"),
    [
        ("amd64", "x86_64-unknown-linux-musl"),
        ("arm64", "aarch64-unknown-linux-musl"),
    ],
)
@pytest.mark.skipif(
    os.name == "nt", reason="The Dockerfile RUN command requires Linux Bash and tar."
)
def test_lychee_run_command_installs_nested_binary(
    tmp_path: Path, architecture: str, triple: str
) -> None:
    dockerfile = DOCKERFILE.read_text(encoding="utf-8")
    bash = shutil.which("bash")
    assert bash is not None
    command = re.search(
        r"^RUN (LYCHEE_VERSION=[^\n]*\\\n(?:[^\n]*\\\n)*[^\n]*)", dockerfile, re.MULTILINE
    )
    assert command is not None
    prefix = tmp_path / "output bin"
    prefix.mkdir()
    source = tmp_path / "lychee"
    source.write_bytes(b'#!/bin/sh\nprintf "nested archive %s\\n" "$TARGETARCH"\n')
    source.chmod(0o755)
    archive = tmp_path / "lychee.tar.gz"
    with tarfile.open(archive, "w:gz") as output:
        output.add(source, arcname=f"lychee-{triple}/lychee")
    shell_code = (
        'curl() { [[ "${!#}" == */"lychee-$FIXTURE_TRIPLE.tar.gz" ]] || return 1; '
        'cat "$FIXTURE_ARCHIVE"; }\n'
        + command[1].replace("/usr/local/bin", '"$FIXTURE_BIN"')
        + '\n"$FIXTURE_BIN/lychee"'
    )
    result = subprocess.run(
        [bash, "-e", "-o", "pipefail", "-c", shell_code],
        cwd=tmp_path,
        env={
            **os.environ,
            "TARGETARCH": architecture,
            "FIXTURE_TRIPLE": triple,
            "FIXTURE_ARCHIVE": str(archive),
            "FIXTURE_BIN": str(prefix),
        },
        capture_output=True,
        encoding="utf-8",
        timeout=30,
        check=True,
    )
    assert result.stdout == f"nested archive {architecture}\n"


def test_run_curl_uses_native_executable() -> None:
    if shutil.which("curl") is None:
        pytest.skip("curl is unavailable on this host.")
    status, output = validator.run_curl(["--version"])
    assert status == 0
    assert output.startswith("curl ")


def test_missing_path_leaves_stdout_empty(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    path = tmp_path / "Dockerfile"
    assert validator.main(["--dockerfile", str(path)]) == 1
    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err == f"Error: Dockerfile not found at {path}\n"


def test_publication_bootstraps_uv_before_the_direct_url_validator() -> None:
    workflow = (ROOT / ".github/workflows/build-publish-devcontainer.yml").read_text(
        encoding="utf-8"
    )
    setup = "uses: ./.github/actions/setup-python-tools"
    direct = "uv run --quiet --project scripts/python~ --locked --no-dev python scripts/validate_devcontainer_urls.py"
    assert workflow.count(setup) == workflow.count(direct) == 1
    assert workflow.index(setup) < workflow.index(direct)


@pytest.mark.skipif(os.name == "nt", reason="This control waits on a Unix subprocess pipe.")
def test_reports_progress_while_a_network_probe_is_pending(tmp_path: Path) -> None:
    dockerfile = tmp_path / "Dockerfile"
    fixture = CORPUS["cases"]["head-ok"]["dockerfile"]
    assert fixture is not None
    dockerfile.write_bytes(fixture.encode())
    curl = tmp_path / "curl"
    curl.write_text(
        '#!/bin/sh\n> "$PROBE_STARTED"\n'
        'while [ ! -f "$PROBE_RELEASE" ]; do sleep 0.02; done\n'
        "printf '200'\n",
        encoding="utf-8",
    )
    curl.chmod(0o755)
    started = tmp_path / "started"
    release = tmp_path / "release"
    with subprocess.Popen(
        [
            sys.executable,
            str(ROOT / "scripts/validate_devcontainer_urls.py"),
            "--dockerfile",
            str(dockerfile),
        ],
        cwd=tmp_path,
        env={
            **os.environ,
            "PATH": str(tmp_path) + os.pathsep + os.environ.get("PATH", ""),
            "PROBE_STARTED": str(started),
            "PROBE_RELEASE": str(release),
        },
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    ) as child:
        try:
            deadline = time.monotonic() + 5
            while not started.exists() and time.monotonic() < deadline:
                time.sleep(0.02)
            assert started.is_file()
            assert child.stdout is not None
            ready, _, _ = select.select([child.stdout], [], [], 0)
        finally:
            release.touch()
            try:
                stdout, stderr = child.communicate(timeout=10)
            except subprocess.TimeoutExpired:
                child.kill()
                child.communicate()
                raise
    assert child.returncode == 0, stderr
    assert b"Devcontainer Download URL Validation" in stdout
    assert ready, "The validator buffered all progress until the download probe finished."
