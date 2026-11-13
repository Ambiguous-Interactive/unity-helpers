"""Check pinned upstream reference hashes without changing local adaptations."""

from __future__ import annotations

import concurrent.futures
import hashlib
import json
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import TYPE_CHECKING, Literal, NotRequired, TypedDict, cast

if TYPE_CHECKING:
    from collections.abc import Callable
    from http.client import HTTPMessage
    from typing import IO

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
MAX_SOURCE_BYTES = 16 * 1024 * 1024


class Entry(TypedDict):
    name: str
    kind: Literal["sort", "random"]
    issue: int
    localPath: str
    upstreamUrl: str
    referenceCommit: str
    sha256: str


class Check(TypedDict):
    name: str
    kind: Literal["sort", "random"]
    issue: int
    localPath: str
    upstreamUrl: str
    referenceCommit: str
    expectedSha256: str
    actualSha256: str | None
    status: Literal["current", "drifted", "unavailable"]
    error: NotRequired[str]


class Report(TypedDict):
    checks: list[Check]
    passed: bool


def validate_url(url: str) -> None:
    parsed = urllib.parse.urlsplit(url)
    if (
        parsed.scheme != "https"
        or parsed.netloc != "raw.githubusercontent.com"
        or not parsed.path.startswith("/")
    ):
        raise ValueError("Source URLs must use raw.githubusercontent.com HTTPS.")


def validate_manifest(manifest: object, root: Path = ROOT) -> list[Entry]:
    if not isinstance(manifest, dict):
        raise ValueError("The manifest must contain an implementations array.")
    entries = cast("dict[str, object]", manifest).get("implementations")
    if not isinstance(entries, list):
        raise ValueError("The manifest must contain an implementations array.")
    raw_entries = cast("list[object]", entries)
    if len(raw_entries) < 2:
        raise ValueError("The manifest must cover at least one sort and one random implementation.")
    names: set[str] = set()
    kinds: set[str] = set()
    validated: list[Entry] = []
    for raw in raw_entries:
        if not isinstance(raw, dict):
            raise ValueError("Every implementation needs a name.")
        entry = cast("dict[str, object]", raw)
        name = entry.get("name")
        if not isinstance(name, str) or not name.strip():
            raise ValueError("Every implementation needs a name.")
        if name in names:
            raise ValueError(f"Duplicate implementation name: {name}")
        names.add(name)
        kind = entry.get("kind")
        if kind not in ("sort", "random"):
            raise ValueError(f"{name} has unsupported kind '{kind}'.")
        kinds.add(kind)
        issue = entry.get("issue")
        if type(issue) is not int or issue < 1:
            raise ValueError(f"{name} needs a positive issue number.")
        commit = entry.get("referenceCommit")
        if not isinstance(commit, str) or re.fullmatch(r"[0-9a-f]{40}", commit) is None:
            raise ValueError(f"{name} needs a full lowercase reference commit SHA.")
        checksum = entry.get("sha256")
        if not isinstance(checksum, str) or re.fullmatch(r"[0-9a-f]{64}", checksum) is None:
            raise ValueError(f"{name} needs a lowercase SHA-256.")
        url = entry.get("upstreamUrl")
        if not isinstance(url, str):
            raise ValueError(f"{name} needs a raw.githubusercontent.com HTTPS URL.")
        validate_url(url)
        local_path = entry.get("localPath")
        if not isinstance(local_path, str) or not local_path.strip():
            raise ValueError(f"{name} needs a local source path.")
        candidate = (root / local_path).resolve()
        if not candidate.is_relative_to(root.resolve()) or not candidate.is_file():
            raise ValueError(f"{name} local source does not exist: {local_path}")
        validated.append(
            Entry(
                name=name,
                kind=kind,
                issue=issue,
                localPath=local_path,
                upstreamUrl=url,
                referenceCommit=commit,
                sha256=checksum,
            )
        )
    if kinds != {"sort", "random"}:
        raise ValueError("The manifest must cover both sort and random implementations.")
    return validated


class SourceRedirectHandler(urllib.request.HTTPRedirectHandler):
    max_redirections = 5

    def redirect_request(
        self,
        req: urllib.request.Request,
        fp: IO[bytes],
        code: int,
        msg: str,
        headers: HTTPMessage,
        newurl: str,
    ) -> urllib.request.Request | None:
        validate_url(newurl)
        return super().redirect_request(req, fp, code, msg, headers, newurl)


def download(url: str) -> bytes:
    validate_url(url)
    request = urllib.request.Request(  # noqa: S310 - URL and every redirect are restricted to HTTPS.
        url, headers={"User-Agent": "unity-helpers-reference-audit"}
    )
    opener = urllib.request.build_opener(SourceRedirectHandler())
    with opener.open(request, timeout=30) as response:
        if response.status != 200:
            raise ValueError(f"HTTP {response.status} for {url}")
        content = response.read(MAX_SOURCE_BYTES + 1)
    if len(content) > MAX_SOURCE_BYTES:
        raise ValueError(f"Source exceeds {MAX_SOURCE_BYTES} bytes: {url}")
    return cast("bytes", content)


def audit_entries(entries: list[Entry], loader: Callable[[str], bytes] = download) -> Report:
    if not entries:
        raise ValueError("At least one reference source is required.")

    def audit(entry: Entry) -> Check:
        check = Check(
            name=entry["name"],
            kind=entry["kind"],
            issue=entry["issue"],
            localPath=entry["localPath"],
            upstreamUrl=entry["upstreamUrl"],
            referenceCommit=entry["referenceCommit"],
            expectedSha256=entry["sha256"],
            actualSha256=None,
            status="unavailable",
        )
        try:
            content = loader(entry["upstreamUrl"])
            checksum = hashlib.sha256(content).hexdigest()
            check["actualSha256"] = checksum
            check["status"] = "current" if checksum == entry["sha256"] else "drifted"
        except (OSError, ValueError, urllib.error.URLError) as error:
            check["error"] = str(error)
        return check

    with concurrent.futures.ThreadPoolExecutor(max_workers=min(8, len(entries))) as executor:
        checks = list(executor.map(audit, entries))
    return Report(checks=checks, passed=all(check["status"] == "current" for check in checks))


def parse_arguments(arguments: list[str]) -> tuple[Path, Path | None]:
    manifest = ROOT / "scripts/reference-upstreams.json"
    report: Path | None = None
    index = 0
    while index < len(arguments):
        flag = arguments[index]
        if flag not in ("--manifest", "--report") or index + 1 == len(arguments):
            raise ValueError(f"Unknown or incomplete argument: {flag}")
        value = Path(arguments[index + 1]).resolve()
        if flag == "--manifest":
            manifest = value
        else:
            report = value
        index += 2
    return manifest, report


def reject_json_constant(token: str) -> None:
    raise ValueError(f"Invalid JSON constant: {token}")


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    try:
        manifest_path, report_path = parse_arguments(
            sys.argv[1:] if arguments is None else arguments
        )
        manifest: object = json.loads(
            manifest_path.read_text(encoding="utf-8"), parse_constant=reject_json_constant
        )
        entries = validate_manifest(manifest)
        report = audit_entries(entries)
        for check in report["checks"]:
            prefix = f"[reference-upstreams] {check['name']}: "
            if check["status"] == "current":
                print(f"{prefix}current ({check['actualSha256']})")
            elif check["status"] == "drifted":
                print(
                    f"{prefix}upstream changed; expected {check['expectedSha256']}, "
                    f"received {check['actualSha256']}",
                    file=sys.stderr,
                )
            else:
                print(f"{prefix}{check.get('error', '')}", file=sys.stderr)
        if report_path is not None:
            report_path.write_text(
                json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
            )
        return 0 if report["passed"] else 1
    except (OSError, ValueError) as error:
        print(f"[reference-upstreams] {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
