"""Validate devcontainer formatter entries and retained image/publication contracts."""

from __future__ import annotations

import argparse
import codecs
import re
import sys
from pathlib import Path

from cli_encoding import configure_output

ROOT = Path(__file__).resolve().parents[1]
IMAGE_NAME = "ambiguous-interactive/unity-helpers/devcontainer"
IMAGE = "ghcr.io/" + IMAGE_NAME
LEGACY_IMAGE = "ghcr.io/wallstop/unity-helpers/devcontainer"
PUBLISH = ".github/workflows/build-publish-devcontainer.yml"
SELF_VALIDATION = "./scripts/validate-devcontainer-config.ps1 -VerboseOutput"
LANGUAGES = (
    ("csharp", "*.cs", "csharpier.csharpier-vscode"),
    ("json", "*.json", "esbenp.prettier-vscode"),
    ("jsonc", "*.jsonc", "esbenp.prettier-vscode"),
    ("yaml", "*.yml/*.yaml", "esbenp.prettier-vscode"),
    ("markdown", "*.md", "esbenp.prettier-vscode"),
    ("javascript", "*.js", "esbenp.prettier-vscode"),
    ("xml", "*.xml", "(formatOnSave: false)"),
    ("shellscript", "*.sh", "(formatOnSave: false)"),
    ("powershell", "*.ps1", "(formatOnSave: false)"),
    ("hlsl", "*.hlsl", "(formatOnSave: false)"),
    ("shaderlab", "*.shader", "(formatOnSave: false)"),
)
REGISTRY_CALL = (
    r"(?m)^\s*(?:run:\s*)?uv run --project scripts/python~ --locked --no-dev "
    r"python scripts/check_registries\.py lint(?: --direct)?(?: --ci)?"
)
REGISTRY = re.compile(REGISTRY_CALL + r"\s*$")
SHARDED_REGISTRY = re.compile(REGISTRY_CALL + r' --shard "\$LINT_SHARD"\s*$')
WORKFLOW_JOBS = re.compile(r"(?ms)^jobs:[ \t]*\n(.*?)(?=^\S|\Z)")
LINT_MATRIX = re.compile(r"(?m)^ {6}matrix:[ \t]*\n {8}shard: \[0,\s*1,\s*2\][ \t]*$")
LINT_SHARD_ENV = re.compile(
    r"(?m)^(?: {8})?env:[ \t]*\n(?: {10}[^\n]*\n)*?"
    r" {10}LINT_SHARD: \$\{\{ matrix\.shard \}\}/\$\{\{ strategy\.job-total \}\}[ \t]*$"
)


def read_text(path: Path) -> str:
    if not path.is_file():
        raise ValueError(f"Expected a regular input file: {path}")
    data = path.read_bytes()
    if data.startswith((codecs.BOM_UTF32_LE, codecs.BOM_UTF32_BE)):
        return data.decode("utf-32")
    if data.startswith((codecs.BOM_UTF16_LE, codecs.BOM_UTF16_BE)):
        return data.decode("utf-16")
    return data.decode("utf-8-sig")


def sharded_lint_runs(content: str) -> bool:
    jobs = WORKFLOW_JOBS.search(content)
    if jobs is None:
        return False
    for job in re.split(r"(?m)^ {2}[\w-]+:[ \t]*$", jobs[1]):
        preamble, _, steps = job.partition("\n    steps:\n")
        if LINT_MATRIX.search(preamble) is None or re.search(
            r"(?m)^(?: {4}if:| {8}exclude:)", preamble
        ):
            continue
        for step in re.split(r"(?m)^ {6}- ", steps):
            if (
                SHARDED_REGISTRY.search(step)
                and LINT_SHARD_ENV.search(step)
                and not re.search(r"(?m)^(?: {8})?if:", step)
            ):
                return True
    return False


def workflow_covers_publish(content: str) -> bool:
    content = "\n".join(re.sub(r"#.*$", "", line) for line in content.splitlines())
    invokes = (
        "validate-devcontainer-config.ps1" in content
        or "run-repo-lint.js" in content
        or REGISTRY.search(content) is not None
        or sharded_lint_runs(content)
    )
    changes = re.search(r"(?m)^\s*(?:push|pull_request):\s*$", content) is not None
    filtered = re.search(r"(?m)^\s+paths:\s*$", content) is not None
    return invokes and changes and (not filtered or PUBLISH in content)


def validate(root: Path, *, verbose: bool = False) -> int:
    config = root / ".devcontainer/devcontainer.json"
    hook = root / ".githooks/pre-commit"
    publish = root / PUBLISH
    for path, label in [
        (config, "devcontainer.json"),
        (hook, "pre-commit hook"),
        (publish, "Devcontainer publish workflow"),
    ]:
        if not path.is_file():
            raise ValueError(f"{label} not found at: {path}")
    configuration = read_text(config)
    publication = read_text(publish)
    if LEGACY_IMAGE in configuration or LEGACY_IMAGE in publication:
        raise ValueError(f"Devcontainer image references must use {IMAGE}, not {LEGACY_IMAGE}.")
    if not all(IMAGE + ":" + tag in configuration for tag in ("buildcache", "latest")):
        raise ValueError(f"devcontainer.json must cache from {IMAGE} buildcache and latest tags.")
    for token, message in [
        ("IMAGE_NAME: " + IMAGE_NAME, f"must publish IMAGE_NAME: {IMAGE_NAME}."),
        ("packages: write", "must grant packages: write for GHCR publishing."),
        (
            "org.opencontainers.image.source=https://github.com/${{ github.repository }}",
            "must publish an org.opencontainers.image.source label so GHCR links the image to this repository.",
        ),
        (
            SELF_VALIDATION,
            "must run validate-devcontainer-config.ps1 before publishing to GHCR.",
        ),
    ]:
        if token not in publication:
            raise ValueError("build-publish-devcontainer.yml " + message)
    wired = False
    for path in sorted(publish.parent.iterdir(), key=lambda item: item.name.casefold()):
        if path == publish or path.suffix.casefold() != ".yml" or not path.is_file():
            continue
        if workflow_covers_publish(read_text(path)):
            wired = True
            break
    if not wired:
        raise ValueError(
            "A workflow must run validate-devcontainer-config.ps1 (directly or via the lint registry) "
            "on changes to build-publish-devcontainer.yml."
        )
    if verbose:
        print("Devcontainer image namespace and GHCR publish metadata are valid.")
    entries: dict[str, str] = {}
    for language in re.findall(r'"\[(\w+)\]"', configuration):
        # Expected identifiers are ASCII; Unicode case folding accepts aliases that
        # the original OrdinalIgnoreCase set rejects (for example, a long s).
        key = language.lower() if language.isascii() else language
        entries.setdefault(key, language)
    if verbose:
        print("Found devcontainer formatter entries for: " + ", ".join(entries.values()))
    missing = [entry for entry in LANGUAGES if entry[0] not in entries]
    if missing:
        print(
            "\nWARNING: The following languages are handled by pre-commit but have no explicit "
            "devcontainer.json formatter entry:",
        )
        for language, pattern, formatter in missing:
            print(f"WARNING:   [{language}] ({pattern}) - expected formatter: {formatter}")
        print()
        raise ValueError(
            f"devcontainer.json is missing {len(missing)} explicit formatter assignment(s). See warnings above."
        )
    if verbose:
        print("All expected formatter assignments are present in devcontainer.json.")
    return 0


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", default=str(ROOT))
    parser.add_argument("--verbose", action="store_true")
    args = parser.parse_args(arguments)
    root = Path(args.repo_root) if args.repo_root.strip() else ROOT
    try:
        return validate(root, verbose=args.verbose)
    except (OSError, ValueError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
