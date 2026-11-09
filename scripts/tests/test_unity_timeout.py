"""Require a supported supervisor before Unity credentials or Docker activation are reached."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parents[2]
WRAPPER = ROOT / "scripts/unity/run-unity-docker.sh"
pytestmark = pytest.mark.skipif(
    sys.platform == "win32", reason="Docker wrapper qualification requires Unix process supervision"
)


def bash() -> str:
    shell = shutil.which("bash")
    if shell is None:
        pytest.skip("Bash is unavailable")
    return shell


def fixture_repository(root: Path) -> Path:
    scripts = root / "scripts/unity"
    (scripts / "lib").mkdir(parents=True)
    wrapper = scripts / WRAPPER.name
    shutil.copyfile(WRAPPER, wrapper)
    shutil.copyfile(
        ROOT / "scripts/unity/lib/license-cache-dir.sh", scripts / "lib/license-cache-dir.sh"
    )
    return wrapper


@pytest.mark.parametrize("kind", ["uutils", "missing", "failing"])
def test_unsupported_timeout_is_refused_before_credentials(tmp_path: Path, kind: str) -> None:
    wrapper = fixture_repository(tmp_path)
    secrets = tmp_path / ".unity-secrets"
    secrets.mkdir()
    if sys.platform != "win32":
        os.mkfifo(secrets / "credentials.env")
    unsupported = tmp_path / "unsupported timeout"
    body = "#!/bin/sh\nprintf 'timeout (uutils coreutils) 0.10.0\\n'\n"
    if kind == "failing":
        body = "#!/bin/sh\nexit 1\n"
    if kind != "missing":
        unsupported.write_text(body, encoding="utf-8")
        unsupported.chmod(0o755)
    environment = os.environ.copy()
    environment["UNITY_TIMEOUT_EXECUTABLE"] = str(unsupported)
    environment["UNITY_TEST_PROJECT_DIR"] = str(tmp_path / "project")
    environment["UNITY_LICENSE_CACHE_DIR"] = str(tmp_path / "cache")
    try:
        result = subprocess.run(
            [bash(), str(wrapper)],
            env=environment,
            capture_output=True,
            text=True,
            check=False,
            timeout=3,
        )
    except subprocess.TimeoutExpired:
        pytest.fail("Unsupported timeout reached the credential FIFO instead of failing early")
    assert result.returncode == 2, result.stdout + result.stderr
    assert "GNU coreutils" in result.stderr
    assert "UNITY_TIMEOUT_EXECUTABLE" in result.stderr
    assert not (tmp_path / "project").exists()
    assert not (tmp_path / "cache").exists()


def selector() -> str:
    source = WRAPPER.read_text(encoding="utf-8")
    start = source.index("select_gnu_timeout() {")
    end = source.index(
        '\nGNU_TIMEOUT_BIN="$(select_gnu_timeout "${UNITY_TIMEOUT_EXECUTABLE:-}")"', start
    )
    return source[start:end]


@pytest.mark.parametrize("name", ["timeout", "gnutimeout", "gtimeout"])
def test_selector_finds_gnu_fallback(tmp_path: Path, name: str) -> None:
    source = selector()
    selected = tmp_path / name
    selected.write_text("#!/bin/sh\nprintf 'timeout (GNU coreutils) 9.7\\n'\n", encoding="utf-8")
    selected.chmod(0o755)
    for other in ("timeout", "gnutimeout", "gtimeout"):
        if other != name:
            target = tmp_path / other
            target.write_text(
                "#!/bin/sh\nprintf 'timeout (uutils coreutils) 0.10.0\\n'\n", encoding="utf-8"
            )
            target.chmod(0o755)
    environment = os.environ.copy()
    environment["PATH"] = str(tmp_path) + os.pathsep + environment.get("PATH", "")
    result = subprocess.run(
        [bash(), "-c", source + "\nselect_gnu_timeout"],
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=5,
    )
    assert result.returncode == 0, result.stderr
    assert Path(result.stdout.strip()).resolve() == selected.resolve()


def test_qualified_timeout_escalates_term_resistant_pipe_child() -> None:
    selected = subprocess.run(
        [bash(), "-c", selector() + "\nselect_gnu_timeout"],
        capture_output=True,
        text=True,
        check=False,
        timeout=5,
    )
    if selected.returncode:
        pytest.skip("GNU coreutils timeout is unavailable")
    script = "trap '' TERM; printf 'ready\\n'; while :; do :; done"
    result = subprocess.run(
        [selected.stdout.strip(), "--signal=TERM", "--kill-after=0.2", "0.2", bash(), "-c", script],
        capture_output=True,
        text=True,
        check=False,
        timeout=5,
    )
    assert result.returncode in (137, -9)
    assert result.stdout == "ready\n"


def test_container_timeout_is_qualified_before_activation(tmp_path: Path) -> None:
    shell = bash()
    selected = subprocess.run(
        [shell, "-c", selector() + "\nselect_gnu_timeout"],
        capture_output=True,
        text=True,
        check=False,
        timeout=5,
    )
    if selected.returncode:
        pytest.skip("GNU coreutils timeout is unavailable")
    wrapper = fixture_repository(tmp_path)
    binaries = tmp_path / "host-bin"
    binaries.mkdir()
    inner = tmp_path / "inner-bin"
    inner.mkdir()
    timeout = inner / "timeout"
    timeout.write_text(
        "#!/bin/sh\nprintf 'timeout (uutils coreutils) 0.10.0\\n'\n", encoding="utf-8"
    )
    timeout.chmod(0o755)
    docker = binaries / "docker"
    docker.write_text(
        "#!/usr/bin/env bash\n"
        'case "$1" in\n'
        'run) PATH="$TIMEOUT_TEST_INNER" exec "$TIMEOUT_TEST_BASH" -c "${@: -1}" ;;\n'
        'inspect) printf "No such object\\n" >&2; exit 1 ;;\n'
        "*) exit 0 ;;\n"
        "esac\n",
        encoding="utf-8",
    )
    docker.chmod(0o755)
    environment = os.environ.copy()
    environment.update(
        {
            "PATH": str(binaries) + os.pathsep + environment.get("PATH", ""),
            "UNITY_TIMEOUT_EXECUTABLE": selected.stdout.strip(),
            "UNITY_TEST_PROJECT_DIR": str(tmp_path / "project"),
            "UNITY_LICENSE_CACHE_DIR": str(tmp_path / "cache"),
            "UNITY_SERIAL": "fixture-serial",
            "UNITY_EMAIL": "fixture@example.invalid",
            "UNITY_PASSWORD": "fixture-password",
            "TIMEOUT_TEST_INNER": str(inner),
            "TIMEOUT_TEST_BASH": shell,
        }
    )
    result = subprocess.run(
        [shell, str(wrapper)],
        env=environment,
        capture_output=True,
        text=True,
        check=False,
        timeout=10,
    )
    output = result.stdout + result.stderr
    assert result.returncode == 2, output
    assert "GNU coreutils timeout is required" in output
    assert "Activating Unity" not in output
    assert "Running Unity command" not in output
