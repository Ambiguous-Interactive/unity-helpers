#!/usr/bin/env python3
"""
Generate GitHub Wiki footer content.

This script generates the _Footer.md file for the GitHub Wiki
with links to the main repository, documentation, issues, and license.

Usage:
    python generate-wiki-footer.py > _Footer.md
    python generate-wiki-footer.py --output _Footer.md
"""

import argparse
from pathlib import Path


def generate_footer(repo_owner: str = "wallstop", repo_name: str = "unity-helpers") -> str:
    """
    Generate the wiki footer markdown content.

    Args:
        repo_owner: GitHub repository owner
        repo_name: GitHub repository name

    Returns:
        Footer markdown content without leading/trailing whitespace issues
    """
    repo_url = f"https://github.com/{repo_owner}/{repo_name}"
    docs_url = f"https://{repo_owner}.github.io/{repo_name}/"

    # Use explicit string building to avoid any heredoc/indentation issues
    lines = [
        "---",
        f"📦 [Unity Helpers]({repo_url}) |",
        f"📖 [Documentation]({docs_url}) |",
        f"🐛 [Issues]({repo_url}/issues) |",
        f"📜 [MIT License]({repo_url}/blob/main/LICENSE)",
    ]

    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate GitHub Wiki footer content.")
    parser.add_argument(
        "--output",
        "-o",
        help="Output file path (default: stdout)",
    )
    parser.add_argument(
        "--repo-owner",
        default="wallstop",
        help="GitHub repository owner (default: wallstop)",
    )
    parser.add_argument(
        "--repo-name",
        default="unity-helpers",
        help="GitHub repository name (default: unity-helpers)",
    )

    args = parser.parse_args()

    footer = generate_footer(args.repo_owner, args.repo_name)

    if args.output:
        with Path(args.output).open("w", encoding="utf-8") as f:
            f.write(footer)
    else:
        print(footer, end="")


if __name__ == "__main__":
    main()
