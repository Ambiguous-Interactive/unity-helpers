"""Exercise reference audit manifests, report parity, failures and CLI ownership."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
import urllib.request
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from urllib.request import Request
from pathlib import Path
from typing import cast

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_reference_upstream_drift as audit


@pytest.fixture
def manifest() -> dict[str, object]:
    return cast(
        "dict[str, object]",
        json.loads((audit.ROOT / "scripts/reference-upstreams.json").read_text(encoding="utf-8")),
    )


def test_live_manifest(manifest: dict[str, object]) -> None:
    entries = audit.validate_manifest(manifest)
    assert {entry["kind"] for entry in entries} == {"sort", "random"}


@pytest.mark.parametrize(
    "value", [None, [], {}, {"implementations": []}, {"implementations": [None, None]}]
)
def test_invalid_manifest_shape(value: object) -> None:
    with pytest.raises(ValueError, match=r"manifest|implementation"):
        audit.validate_manifest(value)


@pytest.mark.parametrize(
    ("field", "value", "reason"),
    [
        ("name", "", "name"),
        ("name", "  ", "name"),
        ("kind", "other", "unsupported kind"),
        ("issue", 0, "positive issue"),
        ("issue", True, "positive issue"),
        ("issue", 1.2, "positive issue"),
        ("referenceCommit", "A" * 40, "lowercase reference commit"),
        ("referenceCommit", "a" * 39, "lowercase reference commit"),
        ("sha256", "short", "lowercase SHA-256"),
        ("upstreamUrl", "http://raw.githubusercontent.com/a", "HTTPS"),
        ("upstreamUrl", "https://raw.githubusercontent.com.attacker/a", "HTTPS"),
        ("upstreamUrl", "https://user@raw.githubusercontent.com/a", "HTTPS"),
        ("localPath", "missing.cs", "does not exist"),
        ("localPath", ".", "does not exist"),
        ("localPath", "../outside.cs", "does not exist"),
        ("localPath", None, "local source path"),
    ],
)
def test_rejected_entry_fields(
    manifest: dict[str, object], field: str, value: object, reason: str
) -> None:
    entries = cast("list[dict[str, object]]", manifest["implementations"])
    entries[0][field] = value
    with pytest.raises(ValueError, match=reason):
        audit.validate_manifest(manifest)


def test_duplicate_names(manifest: dict[str, object]) -> None:
    entries = cast("list[dict[str, object]]", manifest["implementations"])
    manifest["implementations"] = [entries[0], entries[0]]
    with pytest.raises(ValueError, match="Duplicate implementation name"):
        audit.validate_manifest(manifest)


def test_missing_kind(manifest: dict[str, object]) -> None:
    entries = cast("list[dict[str, object]]", manifest["implementations"])
    for entry in entries:
        entry["kind"] = "sort"
    with pytest.raises(ValueError, match="both sort and random"):
        audit.validate_manifest(manifest)


@pytest.mark.parametrize("status", ["current", "drifted", "unavailable"])
def test_audit_outcomes(manifest: dict[str, object], status: str) -> None:
    content = b"known upstream source"
    entries = audit.validate_manifest(manifest)
    for entry in entries:
        entry["sha256"] = hashlib.sha256(content).hexdigest()

    def loader(url: str) -> bytes:
        if status == "unavailable":
            raise OSError("network unavailable")
        return b"changed" if status == "drifted" and url == entries[0]["upstreamUrl"] else content

    report = audit.audit_entries(entries, loader)
    expected = [status, "current"] if status == "drifted" else [status, status]
    assert [check["status"] for check in report["checks"]] == expected
    assert report["passed"] == (status == "current")
    if status == "unavailable":
        assert report["checks"][0]["actualSha256"] is None
        assert report["checks"][0].get("error") == "network unavailable"


def test_empty_audit_is_not_success() -> None:
    with pytest.raises(ValueError, match="At least one"):
        audit.audit_entries([])


@pytest.mark.parametrize("status", ["current", "drifted", "unavailable"])
def test_cli_report_contract(
    manifest: dict[str, object], tmp_path: Path, monkeypatch: pytest.MonkeyPatch, status: str
) -> None:
    entries = audit.validate_manifest(manifest)

    def run(entries: list[audit.Entry]) -> audit.Report:
        def loader(url: str) -> bytes:
            del url
            if status == "unavailable":
                raise OSError("network unavailable")
            return b"source"

        if status == "current":
            for entry in entries:
                entry["sha256"] = hashlib.sha256(b"source").hexdigest()
        return audit.audit_entries(entries, loader)

    report = run(entries)

    def fixed_report(entries: list[audit.Entry]) -> audit.Report:
        del entries
        return report

    monkeypatch.setattr(audit, "audit_entries", fixed_report)
    output = tmp_path / "report.json"
    assert audit.main(["--report", str(output)]) == (0 if status == "current" else 1)
    actual: object = json.loads(output.read_text(encoding="utf-8"))
    checks = cast("dict[str, object]", actual)["checks"]
    for check in cast("list[dict[str, object]]", checks):
        assert ("error" in check) == (status == "unavailable")
    assert len(cast("list[object]", checks)) == len(entries)


@pytest.mark.parametrize("arguments", [["--unknown"], ["--manifest"], ["--report"]])
def test_cli_rejects_invalid_arguments(arguments: list[str], tmp_path: Path) -> None:
    result = subprocess.run(
        [sys.executable, str(audit.ROOT / "scripts/check_reference_upstream_drift.py"), *arguments],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        timeout=15,
        check=False,
    )
    assert result.returncode != 0
    assert "Unknown or incomplete argument" in result.stderr


@pytest.mark.parametrize("mode", ["current", "drifted", "unavailable"])
def test_frozen_original_reports(mode: str) -> None:
    corpus = cast(
        "dict[str, object]",
        json.loads(
            (Path(__file__).parent / "fixtures/reference-upstream-drift-v1.json").read_text(
                encoding="utf-8"
            )
        ),
    )
    entries = cast("list[audit.Entry]", corpus["entries"])

    def loader(url: str) -> bytes:
        if mode == "unavailable":
            raise OSError("network unavailable")
        return (
            b"changed"
            if mode == "drifted" and url == entries[0]["upstreamUrl"]
            else b"known upstream source"
        )

    expected = next(
        case["report"]
        for case in cast("list[dict[str, object]]", corpus["cases"])
        if case["mode"] == mode
    )
    assert audit.audit_entries(entries, loader) == expected


@pytest.mark.parametrize(
    "launcher",
    ["scripts/check-reference-upstream-drift.js", "scripts/tests/test-reference-upstream-drift.js"],
)
def test_node_compatibility_launchers(launcher: str, tmp_path: Path) -> None:
    import shutil

    node = shutil.which("node")
    if node is None:
        pytest.skip("Node is required for compatibility launcher controls")
    arguments = ["-k", "test_live_manifest"] if "/tests/" in launcher else ["--unknown"]
    result = subprocess.run(
        [node, str(audit.ROOT / launcher), *arguments],
        cwd=tmp_path,
        text=True,
        capture_output=True,
        check=False,
        timeout=60,
    )
    assert result.returncode == (0 if "/tests/" in launcher else 1)
    assert (
        "1 passed" in result.stdout
        if "/tests/" in launcher
        else "Unknown or incomplete argument" in result.stderr
    )


@pytest.mark.parametrize(
    "location",
    [
        "file:///tmp/source",
        "http://raw.githubusercontent.com/a",
        "https://example.com/a",
        "https://raw.githubusercontent.com:443/a",
    ],
)
def test_redirects_cannot_escape_source_host(location: str) -> None:
    from http.client import HTTPMessage
    from io import BytesIO
    from urllib.request import Request

    request = Request("https://raw.githubusercontent.com/a")
    with pytest.raises(ValueError, match="HTTPS"):
        audit.SourceRedirectHandler().redirect_request(
            request, BytesIO(), 302, "Found", HTTPMessage(), location
        )


@pytest.mark.parametrize("oversized", [False, True])
def test_download_is_bounded_and_uses_timeout(
    monkeypatch: pytest.MonkeyPatch, oversized: bool
) -> None:
    from io import BytesIO
    from typing import Self

    class Response(BytesIO):
        status = 200

        def __enter__(self) -> Self:
            return self

        def read(self, size: int | None = -1) -> bytes:
            assert size == audit.MAX_SOURCE_BYTES + 1
            return super().read(size)

    class Opener:
        def open(self, request: Request, *, timeout: int) -> Response:
            assert request.full_url == "https://raw.githubusercontent.com/a"
            assert timeout == 30
            return Response(b"a" * (audit.MAX_SOURCE_BYTES + 1 if oversized else 3))

    def build_opener(handler: object) -> Opener:
        del handler
        return Opener()

    monkeypatch.setattr(urllib.request, "build_opener", build_opener)
    if oversized:
        with pytest.raises(ValueError, match="exceeds"):
            audit.download("https://raw.githubusercontent.com/a")
    else:
        assert audit.download("https://raw.githubusercontent.com/a") == b"aaa"


@pytest.mark.parametrize("token", ["NaN", "Infinity", "-Infinity"])
def test_nonstandard_json_fails_before_network_audit(
    manifest: dict[str, object],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
    capsys: pytest.CaptureFixture[str],
    token: str,
) -> None:
    path = tmp_path / "nonstandard.json"
    path.write_text(json.dumps(manifest)[:-1] + f', "ignored": {token}' + "}", encoding="utf-8")

    def unexpected_audit(_entries: list[audit.Entry]) -> audit.Report:
        pytest.fail("Invalid JSON must be rejected before starting any source audit.")

    monkeypatch.setattr(audit, "audit_entries", unexpected_audit)
    assert audit.main(["--manifest", str(path)]) == 1
    assert f"Invalid JSON constant: {token}" in capsys.readouterr().err
