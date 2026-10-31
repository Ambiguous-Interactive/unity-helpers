#!/usr/bin/env python3
"""
Generate GitHub Wiki sidebar from documentation files.

This script generates the _Sidebar.md file for the GitHub Wiki
by scanning the wiki directory for markdown files and organizing
them into categories.

Usage:
    python generate-wiki-sidebar.py /path/to/wiki > _Sidebar.md
    python generate-wiki-sidebar.py /path/to/wiki --output _Sidebar.md
"""

import argparse
import sys
from pathlib import Path


def get_display_name(wiki_name: str) -> str:
    """
    Convert wiki filename to human-readable display name.

    Examples:
        "Features-Inspector-Inspector-Overview" -> "Inspector Overview"
        "Features-Inspector-Utility-Components" -> "Utility Components"
        "Overview-Getting-Started" -> "Getting Started"

    Args:
        wiki_name: Wiki page name (without .md extension)

    Returns:
        Human-readable display name
    """
    display = wiki_name

    # Remove top-level category prefix
    top_level_prefixes = ["Features-", "Overview-", "Performance-", "Guides-", "Project-"]
    for prefix in top_level_prefixes:
        if display.startswith(prefix):
            display = display[len(prefix) :]
            break

    # Remove subcategory prefix if matches known subcategories
    subcategory_prefixes = [
        "Inspector-",
        "Effects-",
        "Relational-Components-",
        "Serialization-",
        "Spatial-",
        "Logging-",
        "Utilities-",
        "Editor-Tools-",
    ]
    for prefix in subcategory_prefixes:
        if display.startswith(prefix):
            display = display[len(prefix) :]
            break

    # Convert dashes to spaces
    return display.replace("-", " ")


def get_wiki_files(wiki_dir: Path, pattern: str) -> list[str]:
    """
    Get sorted list of wiki files matching a pattern.

    Args:
        wiki_dir: Path to wiki directory
        pattern: Glob pattern to match (e.g., "Overview-*.md")

    Returns:
        Sorted list of filenames (without .md extension)
    """
    return [path.stem for path in sorted(wiki_dir.glob(pattern)) if path.is_file()]


def generate_section(title: str, files: list[str]) -> list[str]:
    """
    Generate a sidebar section with links to files.

    Args:
        title: Section title
        files: List of wiki page names

    Returns:
        List of markdown lines for the section
    """
    if not files:
        return []

    lines = ["", f"### {title}"]
    for wiki_name in files:
        display = get_display_name(wiki_name)
        lines.append(f"- [{display}]({wiki_name})")

    return lines


def generate_sidebar(wiki_dir: Path) -> str:
    """
    Generate the complete wiki sidebar content.

    Args:
        wiki_dir: Path to wiki directory

    Returns:
        Complete sidebar markdown content
    """
    lines = ["## 📚 Documentation", "", "### Getting Started", "- [Home](Home)"]

    # Overview section - add directly to Getting Started section
    overview_files = get_wiki_files(wiki_dir, "Overview-*.md")
    for wiki_name in overview_files:
        display = get_display_name(wiki_name)
        lines.append(f"- [{display}]({wiki_name})")

    # Inspector Features
    inspector_files = get_wiki_files(wiki_dir, "Features-Inspector-*.md")
    lines.extend(generate_section("Inspector Features", inspector_files))

    # Effects System
    effects_files = get_wiki_files(wiki_dir, "Features-Effects-*.md")
    lines.extend(generate_section("Effects System", effects_files))

    # Relational Components
    relational_files = get_wiki_files(wiki_dir, "Features-Relational-Components-*.md")
    lines.extend(generate_section("Relational Components", relational_files))

    # Serialization
    serialization_files = get_wiki_files(wiki_dir, "Features-Serialization-*.md")
    lines.extend(generate_section("Serialization", serialization_files))

    # Spatial Trees
    spatial_files = get_wiki_files(wiki_dir, "Features-Spatial-*.md")
    lines.extend(generate_section("Spatial Trees", spatial_files))

    # Logging
    logging_files = get_wiki_files(wiki_dir, "Features-Logging-*.md")
    lines.extend(generate_section("Logging", logging_files))

    # Utilities
    utilities_files = get_wiki_files(wiki_dir, "Features-Utilities-*.md")
    lines.extend(generate_section("Utilities", utilities_files))

    # Editor Tools
    editor_tools_files = get_wiki_files(wiki_dir, "Features-Editor-Tools-*.md")
    lines.extend(generate_section("Editor Tools", editor_tools_files))

    # Guides
    guides_files = get_wiki_files(wiki_dir, "Guides-*.md")
    lines.extend(generate_section("Guides", guides_files))

    # Performance
    performance_files = get_wiki_files(wiki_dir, "Performance-*.md")
    lines.extend(generate_section("Performance", performance_files))

    # Project section (special handling for CHANGELOG)
    lines.extend(["", "### Project", "- [Changelog](CHANGELOG)"])
    project_files = get_wiki_files(wiki_dir, "Project-*.md")
    for wiki_name in project_files:
        display = get_display_name(wiki_name)
        lines.append(f"- [{display}]({wiki_name})")

    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Generate GitHub Wiki sidebar from documentation files."
    )
    parser.add_argument(
        "wiki_dir",
        help="Path to wiki directory containing markdown files",
    )
    parser.add_argument(
        "--output",
        "-o",
        help="Output file path (default: stdout)",
    )

    args = parser.parse_args()

    wiki_dir = Path(args.wiki_dir)
    if not wiki_dir.is_dir():
        print(f"Error: {wiki_dir} is not a directory", file=sys.stderr)
        sys.exit(1)

    sidebar = generate_sidebar(wiki_dir)

    if args.output:
        with Path(args.output).open("w", encoding="utf-8") as f:
            f.write(sidebar)
    else:
        print(sidebar, end="")


if __name__ == "__main__":
    main()
