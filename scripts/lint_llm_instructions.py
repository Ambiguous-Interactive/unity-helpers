"""Validate skill discovery, deterministic indexes and the existing agent policies."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
import tempfile
from pathlib import Path

import generate_agent_skill_stubs
import migrate_agent_skills
from cli_encoding import configure_output
from generate_skills_index import publish, skill_sources

ROOT = Path(__file__).resolve().parents[1]
POLICY_REQUIREMENTS: dict[str, tuple[tuple[str, str], ...]] = {
    "context": (
        (
            "(?is)Disclose agent-written GitHub prose.*?DISCLOSURE: LLM-GENERATED TEXT.*?line one",
            "first-line LLM disclosure",
        ),
        (
            "(?is)Pause for outside humans.*?authenticated login.*?issue-specific user direction",
            "outside-human user-input gate",
        ),
        ("(?is)never trust `author_association`", "login comparison instead of association trust"),
        (
            "(?is)repository-trusted deterministic automation.*?unknown bots are outside",
            "trusted-automation-only exemption",
        ),
        (
            "(?is)Always act on input authored by `wallstop`.*?`cursor\\[bot\\]`.*?`copilot-pull-request-reviewer\\[bot\\]`.*?`copilot-swe-agent\\[bot\\]`",
            "trusted owner and review bots",
        ),
    ),
    "github-operations": (
        ("(?is)GitHub MCP server first", "GitHub MCP first"),
        (
            "(?is)reads and\\s+mutations.*workflow runs and logs.*releases",
            "remote operation coverage",
        ),
        (
            "(?is)plain `git`.*direct REST API or GraphQL API",
            "local Git and direct API fallback boundaries",
        ),
        ("scripts/github-token\\.sh", "prompt-free fallback credential source"),
        ("(?is)Never use.*local `gh` CLI", "local gh prohibition"),
        (
            "(?is)announce the missing\\s+capability in the same message that runs the fallback",
            "fallback announced before it runs",
        ),
        (
            "(?is)(first\\s+line.*?DISCLOSURE: LLM-GENERATED TEXT|DISCLOSURE: LLM-GENERATED TEXT.*?first\\s+line)",
            "first-line LLM disclosure",
        ),
        (
            "(?is)authenticated GitHub login.*?outside human.*?issue-specific direction",
            "outside-human user-input gate",
        ),
        (
            "(?is)author_association.*?still another person",
            "login comparison instead of association trust",
        ),
        (
            "(?is)Other\\s+deterministic automation explicitly trusted.*?unknown third-party bot as\\s+outside",
            "trusted-automation-only exemption",
        ),
        (
            "(?is)Always handle input authored by `wallstop`.*?`cursor\\[bot\\]`.*?`copilot-pull-request-reviewer\\[bot\\]`.*?`copilot-swe-agent\\[bot\\]`",
            "trusted owner and review bots",
        ),
    ),
    "ship-changes": (
        (
            "(?s)```markdown\\s+DISCLOSURE: LLM-GENERATED TEXT\\s+\\*\\*Why:",
            "agent PR body starts with disclosure",
        ),
        (
            'body\\.startswith\\("DISCLOSURE: LLM-GENERATED TEXT\\\\n\\\\n"\\)',
            "fallback body validates disclosure",
        ),
        ("(?is)outside human.*?issue-specific user direction", "outside-human shipping gate"),
        (
            "(?is)Begin an agent-written reply with `DISCLOSURE: LLM-GENERATED TEXT`",
            "review reply disclosure",
        ),
    ),
    "pr-feedback": (
        ('get_root\\("/user"\\)', "authenticated viewer lookup"),
        ("(?is)OUTSIDE OR UNKNOWN -- USER INPUT REQUIRED", "outside-author warning"),
        ("(?is)author_association is not identity", "association distrust"),
        ('get_all\\(f"/pulls/\\{number\\}/commits\\?per_page=100"', "paginated commit audit"),
        ('audit\\("commit committer"', "commit committer audit"),
        ("(?is)Co-authored-by", "co-author audit"),
        ('(?s)def get_all\\(.*?requester: Requester.*?rel="next"', "REST pagination"),
        (
            'get_all\\(f"/commits/\\{head_sha\\}/check-runs\\?per_page=100".*?"check_runs"',
            "paginated check-run audit",
        ),
        (
            '(?is)(?=.*raise FeedbackError\\(f?"HTTP)(?=.*raise FeedbackError\\(f?"invalid JSON)',
            "fail-closed HTTP and JSON handling",
        ),
        ("(?is)incomplete GitHub data.*?return 4", "nonzero incomplete-data exit"),
        ("(?is)TRUSTED_AUTOMATION.*?third-party bot", "trusted and third-party bot fixtures"),
        (
            "(?is)TRUSTED_AUTHORS.*?wallstop.*?TRUSTED_AUTOMATION.*?cursor\\[bot\\].*?copilot-pull-request-reviewer\\[bot\\].*?copilot-swe-agent\\[bot\\]",
            "trusted owner and review bot identities",
        ),
        (
            "(?is)def safe\\(value: object\\).*?terminal sanitization",
            "terminal control sanitization fixture",
        ),
        (
            "(?is)--root-comments-only.*?REST comments do not expose thread resolution",
            "truthful root-comment filter",
        ),
        ("(?is)run_self_tests.*?page failure.*?JSON failure", "offline failure fixtures"),
    ),
}

ENTRYPOINTS = (
    ("AGENTS.md", "](./.llm/context.md)", "Codex, OpenCode, and nanocoder entrypoint"),
    ("CLAUDE.md", "](./.llm/context.md)", "Claude Code entrypoint"),
    (".cursorrules", "](./.llm/context.md)", "Cursor agent entrypoint"),
    (".github/copilot-instructions.md", "](../.llm/context.md)", "Copilot agent entrypoint"),
)


def read(path: Path) -> str:
    generate_agent_skill_stubs.require_owned_path(path)
    if not path.is_file():
        raise ValueError(f"Required instruction file is missing or not regular: {path}")
    return path.read_text(encoding="utf-8-sig")


def h1_lines(content: str) -> list[tuple[int, str]]:
    result: list[tuple[int, str]] = []
    character: str | None = None
    width = 0
    for index, line in enumerate(content.split("\n")):
        if character is None:
            if match := re.match(r"^\s*(`{3,}|~{3,})", line):
                character, width = match[1][0], len(match[1])
                continue
        else:
            if re.match(rf"^\s*({re.escape(character)}{{{width},}})\s*$", line):
                character = None
            continue
        if line.startswith("# "):
            result.append((index + 1, line))
    return result


def policy_errors(root: Path) -> list[str]:
    context = read(root / ".llm/context.md")
    context_policy = context + "\n" + read(root / ".llm/references/context-agent-operations.md")
    github = read(root / ".llm/skills/github-operations.md")
    shipping = (
        read(root / ".llm/skills/ship-changes.md")
        + "\n"
        + "\n".join(
            read(path) for path in sorted((root / ".llm/references").glob("ship-changes-part-*.md"))
        )
    )
    feedback = read(root / "scripts/pr_feedback.py")
    errors = [
        f"context.md still contains a stale embedded-index marker: {marker}"
        for marker in (
            "<!-- BEGIN GENERATED SKILLS INDEX -->",
            "<!-- END GENERATED SKILLS INDEX -->",
        )
        if marker in context
    ]
    if re.search(r"\]\(\./skills/index\.md\)", context) is None:
        errors.append("context.md must link to the generated index (a link to ./skills/index.md).")
    headings = h1_lines(context)
    if len(headings) != 1:
        errors.append(f"context.md must have exactly one H1 (found {len(headings)}).")
    if (
        re.search(
            r"(?s)### GitHub Operations.*?GitHub MCP server \*\*FIRST\*\*",
            context_policy,
            re.IGNORECASE,
        )
        is None
    ):
        errors.append(
            "context.md must make the GitHub MCP server FIRST for remote GitHub operations."
        )
    if (
        re.search(
            r"(?is)### GitHub Operations.*?announce the\s+capability gap in the same message as the fallback",
            context_policy,
        )
        is None
    ):
        errors.append(
            "context.md must require announcing an MCP capability gap in the same message as the fallback."
        )
    for filename, link, label in ENTRYPOINTS:
        if link not in read(root / filename):
            errors.append(f"{label} must delegate to .llm/context.md via {link}.")
    if (
        re.search(r"\]\(\.{1,2}/(?:skills/)?github-operations\.md\)", shipping, re.IGNORECASE)
        is None
    ):
        errors.append("ship-changes.md must link to the GitHub MCP-first operations guide.")
    subjects = {
        "context": context_policy,
        "github-operations": github,
        "ship-changes": shipping,
        "pr-feedback": feedback,
    }
    labels = {
        "context": "context.md is missing required GitHub authorship policy",
        "github-operations": "github-operations.md is missing required guidance",
        "ship-changes": "ship-changes.md is missing required guidance",
        "pr-feedback": "pr_feedback.py is missing required authorship coverage",
    }
    for name, requirements in POLICY_REQUIREMENTS.items():
        for pattern, label in requirements:
            if re.search(pattern, subjects[name], re.IGNORECASE) is None:
                errors.append(f"{labels[name]}: {label}.")
    return errors


def trigger_errors(sources: list[Path]) -> list[str]:
    errors: list[str] = []
    for source in sources:
        match = re.search(r"<!--\s*trigger:\s*(.+?)\s*-->", read(source))
        if match is None:
            errors.append(f"{source.name}: missing trigger comment")
            continue
        if not match[0].isascii():
            errors.append(f"{source.name}: trigger comments must be ASCII-only")
        fields = match[1].split("|")
        if len(fields) not in {2, 3}:
            errors.append(f"{source.name}: trigger must have 2 or 3 '|'-separated fields")
        elif len(fields) == 3 and fields[2].strip().casefold() not in {
            "core",
            "performance",
            "feature",
        }:
            errors.append(f"{source.name}: unknown category '{fields[2].strip()}'")
    return errors


def generated_index(root: Path) -> bytes:
    with tempfile.TemporaryDirectory(prefix="skills-index-validation-") as temporary:
        outputs: list[bytes] = []
        for name in ("first.md", "second.md"):
            destination = Path(temporary) / name
            result = subprocess.run(  # noqa: S603 - owned generator with explicit root and output arguments.
                [
                    sys.executable,
                    str(ROOT / "scripts/generate_skills_index.py"),
                    "--repo-root",
                    str(root),
                    "--output-path",
                    str(destination),
                ],
                cwd=root,
                capture_output=True,
                encoding="utf-8",
                check=False,
                timeout=60,
            )
            if result.returncode:
                raise ValueError(
                    f"Generator failed (exit {result.returncode}) writing expected index: {result.stdout}{result.stderr}"
                )
            outputs.append(destination.read_bytes())
        if outputs[0] != outputs[1]:
            raise ValueError("Generator is NON-deterministic: two runs produced different bytes.")
        return outputs[0]


def validate(
    root: Path, *, fix: bool = False, authorship_only: bool = False, verbose: bool = False
) -> int:
    root = root.resolve(strict=True)
    required = (
        ".llm/skills",
        ".llm/context.md",
        "scripts/generate_skills_index.py",
        "scripts/generate_agent_skill_stubs.py",
        "scripts/migrate_agent_skills.py",
        ".llm/skills/github-operations.md",
        ".llm/skills/ship-changes.md",
        "scripts/pr_feedback.py",
        *(entry[0] for entry in ENTRYPOINTS),
    )
    for filename in required:
        path = root / filename
        generate_agent_skill_stubs.require_owned_path(path)
        if not path.exists():
            raise ValueError(f"Required instruction path not found: {path}")
    errors: list[str] = []
    if not authorship_only:
        sources = skill_sources(root / ".llm/skills")
        errors.extend(trigger_errors(sources))
        if errors and fix:
            for error in errors:
                print(f"[llm-lint] ERROR: {error}")
            return 1
        try:
            if generate_agent_skill_stubs.generate(root, write=fix):
                errors.append("Agent discovery skills are missing or stale.")
        except (OSError, ValueError) as discovery_error:
            errors.append(f"Agent discovery skills are missing or stale: {discovery_error}")
        try:
            if migrate_agent_skills.run("--check", root):
                errors.append("Canonical skill split reconstruction or size validation failed.")
        except (OSError, ValueError, subprocess.SubprocessError) as split_error:
            errors.append(
                f"Canonical skill split reconstruction or size validation failed: {split_error}"
            )
        expected = generated_index(root)
        index = root / ".llm/skills/index.md"
        generate_agent_skill_stubs.require_owned_path(index)
        if not index.exists():
            if fix:
                publish(index, expected.decode("utf-8"))
            else:
                errors.append(
                    "index.md does not exist. Run: pwsh -NoProfile -File scripts/generate-skills-index.ps1"
                )
        if index.exists():
            current = index.read_bytes()
            if current.decode("utf-8-sig").replace("\r\n", "\n") != expected.decode("utf-8"):
                if fix:
                    publish(index, expected.decode("utf-8"))
                    current = index.read_bytes()
                else:
                    errors.append("Skills index index.md is out of date!")
            if current.startswith(b"\xef\xbb\xbf"):
                errors.append(
                    "index.md has a UTF-8 BOM; it must be written without a BOM (cross-OS stability)."
                )
            if b"\r" in current:
                errors.append("index.md contains CR (CRLF); it must use LF line endings.")
        if verbose:
            print(
                f"[llm-lint] Checked {len(sources)} authored skills and independent generator outputs."
            )
    errors.extend(policy_errors(root))
    for error in errors:
        print(f"[llm-lint] ERROR: {error}")
    if not errors:
        print(
            "[llm-lint] Agent guidance enforces GitHub tool order, LLM disclosure, and outside-human input gates"
        )
    print(f"[llm-lint] LLM instructions validation {'failed' if errors else 'passed'}!")
    return int(bool(errors))


def main(arguments: list[str] | None = None) -> int:
    configure_output()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=ROOT)
    parser.add_argument("--fix", action="store_true")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--authorship-policy-only", action="store_true")
    args = parser.parse_args(arguments)
    try:
        return validate(
            args.repo_root,
            fix=args.fix,
            authorship_only=args.authorship_policy_only,
            verbose=args.verbose,
        )
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"[llm-lint] ERROR: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
