"""Keep compiler-input verification fail-closed and refresh publication recoverable."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_shipped_analyzers as verifier

ASSEMBLIES = tuple(f"{name}.dll" for name in verifier.ANALYZERS)


def payload(name: str) -> bytes:
    return f"fresh bytes of {name}".encode()


@pytest.fixture
def shipped(tmp_path: Path) -> Path:
    path = tmp_path / "Runtime" / "Analyzers"
    path.mkdir(parents=True)
    for name in ASSEMBLIES:
        (path / name).write_bytes(payload(name))
    return path


def stub_build(omit: tuple[str, ...] = ()) -> verifier.Build:
    def build(_project: str, output_directory: Path) -> verifier.BuildResult:
        for name in ASSEMBLIES:
            if name not in omit:
                (output_directory / name).write_bytes(payload(name))
        return verifier.BuildResult(True)

    return build


def run(
    shipped: Path, *, fix: bool = False, build: verifier.Build | None = None
) -> tuple[int, str]:
    lines: list[str] = []
    code = verifier.verify(
        shipped,
        shipped.parent / "scratch",
        fix=fix,
        build=stub_build() if build is None else build,
        log=lines.append,
        log_error=lines.append,
    )
    return code, "\n".join(lines)


def test_identical_payloads_pass(shipped: Path) -> None:
    assert run(shipped) == (0, "")


@pytest.mark.parametrize("name", ASSEMBLIES)
@pytest.mark.parametrize("mutation", ["stale", "missing"])
def test_each_payload_is_required_and_byte_compared(
    shipped: Path, name: str, mutation: str
) -> None:
    target = shipped / name
    if mutation == "stale":
        target.write_bytes(payload(name) + b" ")
    else:
        target.unlink()
    code, output = run(shipped)
    assert code == 1
    assert (
        f"{name} {'differs from a fresh Release build' if mutation == 'stale' else 'is missing'}"
        in output
    )


@pytest.mark.parametrize("fix", [False, True])
def test_missing_directory_is_not_created(tmp_path: Path, fix: bool) -> None:
    absent = tmp_path / "absent"
    code, output = run(absent, fix=fix)
    assert code == 1
    assert "does not exist" in output
    assert not absent.exists()


def test_empty_shipped_directory_rejects_every_missing_payload(shipped: Path) -> None:
    for path in shipped.iterdir():
        path.unlink()
    code, output = run(shipped)
    assert code == 1
    for name in ASSEMBLIES:
        assert f"{name} is missing" in output


@pytest.mark.parametrize("name", ASSEMBLIES)
def test_build_must_produce_every_payload(shipped: Path, name: str) -> None:
    code, output = run(shipped, build=stub_build((name,)))
    assert code == 1
    assert "did not produce every analyzer" in output
    assert name in output


def test_stale_scratch_payload_cannot_stand_in_for_a_build(shipped: Path) -> None:
    assert run(shipped)[0] == 0
    scratch = shipped.parent / "scratch"
    for name in ASSEMBLIES:
        (scratch / name).write_bytes(payload(name))
    code, output = run(shipped, build=stub_build((ASSEMBLIES[0],)))
    assert code == 1
    assert "did not produce every analyzer" in output


@pytest.mark.parametrize("fix", [False, True])
def test_unexpected_payload_is_rejected_without_touching_it(shipped: Path, fix: bool) -> None:
    unlisted = shipped / "Someone.Elses.dll"
    unlisted.write_bytes(b"unlisted")
    code, output = run(shipped, fix=fix)
    assert code == 1
    assert "Someone.Elses.dll" in output
    assert unlisted.read_bytes() == b"unlisted"


def test_failed_build_stops_comparison(shipped: Path) -> None:
    def failed(_project: str, _output: Path) -> verifier.BuildResult:
        return verifier.BuildResult(False, "fixture build failed")

    code, output = run(shipped, fix=True, build=failed)
    assert code == 1
    assert "failed to build" in output
    assert "fixture build failed" in output
    assert (shipped / ASSEMBLIES[0]).read_bytes() == payload(ASSEMBLIES[0])


def test_fix_repairs_stale_and_missing_payloads_preserving_importers(shipped: Path) -> None:
    for name in ASSEMBLIES:
        (shipped / (name + ".meta")).write_bytes(b"retained importer")
    (shipped / ASSEMBLIES[0]).write_bytes(b"stale")
    for name in ASSEMBLIES[1:]:
        (shipped / name).unlink()
    code, output = run(shipped, fix=True)
    assert code == 0
    for name in ASSEMBLIES:
        assert name in output
        assert (shipped / name).read_bytes() == payload(name)
        assert (shipped / (name + ".meta")).read_bytes() == b"retained importer"
    assert not list(shipped.glob(".refresh-*"))
    assert run(shipped) == (0, "")


@pytest.mark.parametrize("operation", ["copy", "replace"])
@pytest.mark.parametrize("real_denial", [False, True])
def test_failed_publication_preserves_payload_and_allows_retry(
    shipped: Path, operation: str, real_denial: bool, monkeypatch: pytest.MonkeyPatch
) -> None:
    if real_denial and (os.name == "nt" or os.getuid() == 0):
        pytest.skip("Real chmod denial requires a non-root Unix account.")
    name = ASSEMBLIES[0]
    target = shipped / name
    target.write_bytes(b"retained payload")
    target.with_name(name + ".meta").write_bytes(b"retained importer")
    invoked = False
    copy = shutil.copyfile
    replace = Path.replace

    def perform(source: Path, destination: Path) -> object:
        return copy(source, destination) if operation == "copy" else replace(source, destination)

    def failure(source: Path, destination: Path) -> object:
        nonlocal invoked
        invoked = True
        if not real_denial:
            if operation == "copy":
                destination.write_bytes(b"partial payload")
            raise PermissionError("injected publication failure")
        parent = destination.parent
        mode = parent.stat().st_mode
        parent.chmod(0o500)
        try:
            return perform(source, destination)
        finally:
            parent.chmod(mode)

    with monkeypatch.context() as patched:
        if operation == "copy":
            patched.setattr(shutil, "copyfile", failure)
        else:
            patched.setattr(Path, "replace", failure)
        code, output = run(shipped, fix=True)
    assert invoked
    assert code == 1
    assert "Could not refresh" in output
    assert target.read_bytes() == b"retained payload"
    assert target.with_name(name + ".meta").read_bytes() == b"retained importer"
    assert not list(shipped.glob(".refresh-*"))
    assert run(shipped, fix=True)[0] == 0
    assert target.read_bytes() == payload(name)


@pytest.mark.parametrize("assembly", verifier.ANALYZERS)
@pytest.mark.parametrize(
    ("configuration", "explicit", "expected"),
    [("Debug", None, "false"), ("Release", None, "true"), ("Release", "false", "false")],
)
def test_project_copy_configuration(
    assembly: str, configuration: str, explicit: str | None, expected: str
) -> None:
    dotnet = shutil.which("dotnet")
    assert dotnet is not None
    args = [
        dotnet,
        "msbuild",
        str(verifier.ROOT / verifier.project_path(assembly)),
        "-nologo",
        "-getProperty:CopyAnalyzerPayload",
        f"-p:Configuration={configuration}",
    ]
    if explicit is not None:
        args.append(f"-p:CopyAnalyzerPayload={explicit}")
    result = subprocess.run(
        args, cwd=verifier.ROOT, capture_output=True, text=True, check=False, timeout=60
    )
    assert result.returncode == 0, result.stdout + result.stderr
    assert result.stdout.strip() == expected


@pytest.mark.parametrize("assembly", verifier.ANALYZERS)
def test_debug_payload_copy_is_rejected_before_writing(assembly: str, tmp_path: Path) -> None:
    dotnet = shutil.which("dotnet")
    assert dotnet is not None
    result = subprocess.run(
        [
            dotnet,
            "msbuild",
            str(verifier.ROOT / verifier.project_path(assembly)),
            "-nologo",
            "-t:CopyAnalyzerToPackage",
            "-p:Configuration=Debug",
            "-p:CopyAnalyzerPayload=true",
            f"-p:AnalyzerPayloadOutputDir={tmp_path}",
        ],
        cwd=verifier.ROOT,
        capture_output=True,
        text=True,
        check=False,
        timeout=60,
    )
    assert result.returncode != 0
    assert (
        "Analyzer payloads can only be copied from a Release build" in result.stdout + result.stderr
    )
    assert not (tmp_path / f"{assembly}.dll").exists()


def test_missing_build_tool_is_a_failure(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    def missing_tool(_name: str) -> None:
        return None

    monkeypatch.setattr(shutil, "which", missing_tool)
    result = verifier.dotnet_build("owned.csproj", tmp_path)
    assert not result.ok
    assert "dotnet is required" in result.output


@pytest.mark.parametrize(
    "error", [OSError("cannot execute"), subprocess.TimeoutExpired("dotnet", 600)]
)
def test_build_startup_and_deadline_failures_are_reported(
    error: Exception, monkeypatch: pytest.MonkeyPatch, tmp_path: Path
) -> None:
    def run(*_args: object, **_kwargs: object) -> subprocess.CompletedProcess[str]:
        raise error

    def fixture_tool(_name: str) -> str:
        return "/fixture/dotnet"

    monkeypatch.setattr(shutil, "which", fixture_tool)
    monkeypatch.setattr(subprocess, "run", run)
    result = verifier.dotnet_build("owned.csproj", tmp_path)
    assert not result.ok
    assert result.output
