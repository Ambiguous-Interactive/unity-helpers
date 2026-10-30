#!/usr/bin/env python3
"""
Prepare documentation files for GitHub Wiki deployment.

This script handles the complete wiki preparation process:
1. Copy and rename documentation files to wiki-friendly names
2. Transform relative links to wiki page references
3. Generate sidebar and footer

Usage:
    uv run --project scripts/python~ --locked --no-dev python scripts/wiki/prepare_wiki.py --source /path/to/repo --dest /path/to/wiki
"""

import argparse
import shutil
import sys
from pathlib import Path

# Import sibling modules
sys.path.insert(0, str(Path(__file__).parent))

from transform_wiki_links import transform_image_paths, transform_wiki_links


def path_to_wiki_name(relative_path: str) -> str:
    """
    Convert a relative file path to a wiki page name.

    Examples:
        "overview/getting-started.md" -> "Overview-Getting-Started.md"
        "features/inspector/inspector-button.md" -> "Features-Inspector-Inspector-Button.md"

    Args:
        relative_path: Path relative to docs directory

    Returns:
        Wiki-friendly filename
    """
    # Remove .md extension
    name = relative_path.rsplit(".", 1)[0] if relative_path.endswith(".md") else relative_path

    # Replace path separators (both / and \) with -
    name = name.replace("\\", "-").replace("/", "-")

    # Capitalize each segment
    parts = name.split("-")
    capitalized = [part[0].upper() + part[1:] if part else part for part in parts]
    name = "-".join(capitalized)

    return name + ".md"


def copy_and_transform_file(src: Path, dest: Path) -> None:
    """
    Copy a markdown file, transforming its links for wiki format.

    Args:
        src: Source file path
        dest: Destination file path
    """
    with src.open("r", encoding="utf-8") as f:
        content = f.read()

    # Transform links
    content = transform_wiki_links(content)
    content = transform_image_paths(content)

    with dest.open("w", encoding="utf-8") as f:
        f.write(content)


def prepare_wiki(source_dir: Path, dest_dir: Path, verbose: bool = False) -> tuple[int, int]:
    """
    Prepare wiki content from source repository.

    Args:
        source_dir: Path to source repository root
        dest_dir: Path to wiki destination directory
        verbose: Print progress messages

    Returns:
        Tuple of (pages_created, images_copied)
    """
    pages_created = 0
    images_copied = 0

    # Ensure destination exists
    dest_dir.mkdir(parents=True, exist_ok=True)

    # Copy README as Home page
    readme_path = source_dir / "README.md"
    if readme_path.exists():
        if verbose:
            print("Creating Home.md from README.md...")
        copy_and_transform_file(readme_path, dest_dir / "Home.md")
        pages_created += 1

    # Copy CHANGELOG
    changelog_path = source_dir / "CHANGELOG.md"
    if changelog_path.exists():
        if verbose:
            print("Creating CHANGELOG.md...")
        copy_and_transform_file(changelog_path, dest_dir / "CHANGELOG.md")
        pages_created += 1

    # Copy index.md if exists
    index_path = source_dir / "index.md"
    if index_path.exists():
        if verbose:
            print("Creating Index.md...")
        copy_and_transform_file(index_path, dest_dir / "Index.md")
        pages_created += 1

    # Process docs directory
    docs_dir = source_dir / "docs"
    if docs_dir.exists():
        for md_file in sorted(docs_dir.rglob("*.md")):
            # Skip .meta files
            if md_file.suffix == ".meta":
                continue

            # Get relative path from docs dir
            relative_path = md_file.relative_to(docs_dir)
            wiki_name = path_to_wiki_name(str(relative_path))

            if verbose:
                print(f"  📄 Creating: {wiki_name} (from {relative_path})")

            copy_and_transform_file(md_file, dest_dir / wiki_name)
            pages_created += 1

    # Copy images
    images_dir = docs_dir / "images" if docs_dir.exists() else None
    if images_dir and images_dir.exists():
        wiki_images_dir = dest_dir / "images"
        wiki_images_dir.mkdir(parents=True, exist_ok=True)

        for img_file in images_dir.rglob("*"):
            if img_file.is_file() and not img_file.name.endswith(".meta"):
                # Preserve directory structure within images
                relative_path = img_file.relative_to(images_dir)
                dest_path = wiki_images_dir / relative_path
                dest_path.parent.mkdir(parents=True, exist_ok=True)

                if verbose:
                    print(f"  🖼️  Copying: images/{relative_path}")

                shutil.copy2(img_file, dest_path)
                images_copied += 1

    return pages_created, images_copied


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Prepare documentation files for GitHub Wiki deployment."
    )
    parser.add_argument(
        "--source",
        "-s",
        required=True,
        help="Path to source repository root",
    )
    parser.add_argument(
        "--dest",
        "-d",
        required=True,
        help="Path to wiki destination directory",
    )
    parser.add_argument(
        "--verbose",
        "-v",
        action="store_true",
        help="Print progress messages",
    )
    parser.add_argument(
        "--clean",
        "-c",
        action="store_true",
        help="Clean destination directory before copying (preserves .git)",
    )

    args = parser.parse_args()

    source_dir = Path(args.source).resolve()
    dest_dir = Path(args.dest).resolve()

    if not source_dir.is_dir():
        print(f"Error: Source directory does not exist: {source_dir}", file=sys.stderr)
        sys.exit(1)

    # Clean destination if requested
    if args.clean and dest_dir.exists():
        if args.verbose:
            print("Cleaning destination directory...")
        for item in dest_dir.iterdir():
            if item.name != ".git":
                if item.is_dir():
                    shutil.rmtree(item)
                else:
                    item.unlink()

    pages, images = prepare_wiki(source_dir, dest_dir, args.verbose)

    if args.verbose:
        print("\n✅ Wiki preparation complete")
        print(f"   Pages: {pages}")
        print(f"   Images: {images}")


if __name__ == "__main__":
    main()
