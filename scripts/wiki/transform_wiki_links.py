#!/usr/bin/env python3
"""
Transform relative markdown links to GitHub Wiki page references.

This script converts links like:
    (./docs/overview/roadmap.md) → (Overview-Roadmap)
    (../README.md) → (Home)
    (./docs/features/inspector/inspector-button.md) → (Features-Inspector-Inspector-Button)

Usage:
    uv run --project scripts/python~ --locked --no-dev python scripts/wiki/transform_wiki_links.py input.md > output.md
    uv run --project scripts/python~ --locked --no-dev python scripts/wiki/transform_wiki_links.py input.md --in-place
    echo "content" | python transform_wiki_links.py -
"""

import argparse
import re
import sys
from collections.abc import Callable
from pathlib import Path


def transform_path_to_wiki_page(path: str) -> str:
    """
    Transform a relative file path to a wiki page name.

    Examples:
        "../README.md" -> "Home"
        "./README.md" -> "Home"
        "README.md" -> "Home"
        "./docs/overview/roadmap.md" -> "Overview-Roadmap"
        "./docs/features/inspector.md" -> "Features-Inspector"
        "../overview/getting-started.md" -> "Overview-Getting-Started"
        "./CHANGELOG.md" -> "CHANGELOG"
        "./docs/overview/roadmap.md#future" -> "Overview-Roadmap#future"

    Args:
        path: Relative path like './docs/overview/roadmap.md'

    Returns:
        Wiki page name like 'Overview-Roadmap' or 'Home'
    """
    # Separate anchor if present
    anchor = ""
    if "#" in path:
        path, anchor = path.split("#", 1)
        anchor = "#" + anchor

    # Strip ./ and ../ prefixes
    while path.startswith(("./", "../")):
        if path.startswith("./"):
            path = path[2:]
        elif path.startswith("../"):
            path = path[3:]

    # Strip .md suffix
    if path.endswith(".md"):
        path = path[:-3]

    # Handle special case: README -> Home
    if path.upper() == "README":
        return "Home" + anchor

    # Handle special case: CHANGELOG stays as-is
    if path.upper() == "CHANGELOG":
        return "CHANGELOG" + anchor

    # Remove docs/ prefix if present (case-insensitive)
    if path.lower().startswith("docs/"):
        path = path[5:]

    # Replace / with - and capitalize each segment
    parts = path.split("/")
    wiki_parts: list[str] = []
    for part in parts:
        # Split on hyphens to capitalize each word
        words = part.split("-")
        capitalized_words = [word.capitalize() for word in words if word]
        wiki_parts.append("-".join(capitalized_words))

    return "-".join(wiki_parts) + anchor


def _transform_preserving_code_blocks(content: str, transform_fn: Callable[[str], str]) -> str:
    """
    Apply a transformation function while preserving code blocks.

    This handles both fenced code blocks (``` or ~~~) and inline code (backticks).
    The transform_fn is only applied to non-code portions of the content.

    Important: Links can contain backticks in their display text, e.g.:
        [The `Config` class](./docs/api.md)
    These links should still be transformed. Only standalone inline code
    (not part of a link) should be protected.

    Args:
        content: Full markdown content
        transform_fn: Function that takes a text segment and returns transformed text

    Returns:
        Content with transform_fn applied only to non-code portions
    """
    # First, split by fenced code blocks to preserve them exactly
    # Pattern matches fenced code blocks: ```...``` or ~~~...~~~
    # Uses non-greedy matching and DOTALL to match across lines
    fenced_pattern = r"(```[\s\S]*?```|~~~[\s\S]*?~~~)"

    # Split content by fenced code blocks, keeping the delimiters
    fenced_parts = re.split(fenced_pattern, content)

    result_parts: list[str] = []
    for i, part in enumerate(fenced_parts):
        # Odd indices are fenced code blocks (captured groups)
        if i % 2 == 1:
            # This is a fenced code block - preserve exactly
            result_parts.append(part)
        else:
            # This is regular content - need to handle inline code carefully
            # We must NOT break apart links that have backticks in link text
            # e.g., [The `method` name](./path.md) should be transformed
            #
            # Strategy:
            # 1. Find and protect standalone inline code (not inside link brackets)
            # 2. Apply transformation (links with backticks in text are transformed)
            # 3. Restore protected inline code
            result_parts.append(_transform_with_protected_inline_code(part, transform_fn))

    return "".join(result_parts)


def _transform_with_protected_inline_code(text: str, transform_fn: Callable[[str], str]) -> str:
    """
    Transform text while protecting standalone inline code.

    Inline code that is NOT part of a markdown link's display text is protected.
    Links with backticks in their display text are still transformed.

    Args:
        text: Text segment (not inside fenced code blocks)
        transform_fn: Transformation function to apply

    Returns:
        Transformed text with standalone inline code preserved
    """
    # Placeholder for protected inline code
    placeholders: list[str] = []
    placeholder_prefix = "\x00INLINE_CODE_"

    # Pattern to find inline code that is NOT inside link brackets
    # We need to identify inline code that appears outside of [...](...) patterns
    #
    # Strategy: Process the text to find inline code, but skip any inline code
    # that appears within markdown link brackets [...]
    #
    # We'll iterate through the text, tracking whether we're inside link brackets

    result: list[str] = []
    pos = 0
    in_link_text = False
    bracket_depth = 0

    while pos < len(text):
        # Check for start of link text [
        if text[pos] == "[" and not in_link_text:
            # Could be start of link text, track bracket depth
            in_link_text = True
            bracket_depth = 1
            result.append(text[pos])
            pos += 1
            continue

        # Track nested brackets within link text
        if in_link_text:
            if text[pos] == "[":
                bracket_depth += 1
            elif text[pos] == "]":
                bracket_depth -= 1
                if bracket_depth == 0:
                    # Check if this is followed by ( to confirm it's a link
                    # Look ahead for (
                    next_pos = pos + 1
                    while next_pos < len(text) and text[next_pos] in " \t":
                        next_pos += 1
                    if next_pos < len(text) and text[next_pos] == "(":
                        # This is a markdown link - content inside [...] was link text
                        # Continue to include the (...) part
                        result.append(text[pos])
                        pos += 1
                        # Skip whitespace
                        while pos < len(text) and text[pos] in " \t":
                            result.append(text[pos])
                            pos += 1
                        # Now consume the (...) part
                        if pos < len(text) and text[pos] == "(":
                            paren_depth = 1
                            result.append(text[pos])
                            pos += 1
                            while pos < len(text) and paren_depth > 0:
                                if text[pos] == "(":
                                    paren_depth += 1
                                elif text[pos] == ")":
                                    paren_depth -= 1
                                result.append(text[pos])
                                pos += 1
                        in_link_text = False
                        continue
                    # Not a link, just brackets
                    in_link_text = False
            # Inside link text - include everything as-is (including backticks)
            result.append(text[pos])
            pos += 1
            continue

        # Outside link text - check for inline code to protect
        # Match double backticks first, then single
        if text[pos : pos + 2] == "``":
            # Double backtick inline code
            end = text.find("``", pos + 2)
            if end != -1:
                code_span = text[pos : end + 2]
                idx = len(placeholders)
                placeholders.append(code_span)
                result.append(f"{placeholder_prefix}{idx}\x00")
                pos = end + 2
                continue

        if text[pos] == "`":
            # Single backtick inline code
            end = text.find("`", pos + 1)
            if end != -1:
                code_span = text[pos : end + 1]
                idx = len(placeholders)
                placeholders.append(code_span)
                result.append(f"{placeholder_prefix}{idx}\x00")
                pos = end + 1
                continue

        result.append(text[pos])
        pos += 1

    protected_text = "".join(result)

    # Apply the transformation
    transformed = transform_fn(protected_text)

    # Restore placeholders
    for idx, code in enumerate(placeholders):
        transformed = transformed.replace(f"{placeholder_prefix}{idx}\x00", code)

    return transformed


def _transform_links_in_text(text: str) -> str:
    """
    Transform relative markdown links in a text segment (not inside code blocks).

    Args:
        text: Text segment to transform

    Returns:
        Text with links transformed to wiki format
    """
    # Pattern matches: [text](./path/to/file.md) or [text](../path/to/file.md)
    # Also handles anchors: [text](./path/to/file.md#section)
    # Captures: [link_text](path_with_optional_anchor)
    pattern = r"\[([^\]]+)\]\((\.\.?/[^)]+\.md(?:#[^)]*)?)\)"

    def replace_link(match: re.Match[str]) -> str:
        link_text = match.group(1)
        path = match.group(2)
        wiki_page = transform_path_to_wiki_page(path)
        return f"[{link_text}]({wiki_page})"

    return re.sub(pattern, replace_link, text)


def transform_wiki_links(content: str) -> str:
    """
    Transform all relative markdown links in content to wiki page references.

    Only transforms links that look like local file references:
    - Start with ./ or ../
    - End with .md (optionally followed by an anchor)
    - Do not start with http:// or https://

    Preserves:
    - External links (http://, https://)
    - Pure anchor links (#section)
    - Non-markdown file links
    - Content inside fenced code blocks (``` or ~~~)
    - Content inside inline code (backticks)

    Args:
        content: Markdown content with relative links

    Returns:
        Content with links transformed to wiki format
    """
    return _transform_preserving_code_blocks(content, _transform_links_in_text)


def _transform_images_in_text(text: str) -> str:
    """
    Normalize image paths in a text segment (not inside code blocks).

    Args:
        text: Text segment to transform

    Returns:
        Text with image paths normalized
    """
    # Pattern matches: ![alt](path/containing/docs/images/or/similar/file.ext)
    # We want to normalize paths like:
    #   ./docs/images/foo.png -> images/foo.png
    #   ../docs/images/foo.png -> images/foo.png
    #   ../../docs/images/foo.png -> images/foo.png

    def normalize_image_path(match: re.Match[str]) -> str:
        alt_text = match.group(1)
        full_path = match.group(2)

        # Skip external URLs
        if full_path.startswith(("http://", "https://", "//")):
            return match.group(0)

        # Find 'images/' in the path and extract everything after it
        images_idx = full_path.lower().find("images/")
        if images_idx != -1:
            # Keep images/ and everything after it
            new_path = full_path[images_idx:]
            return f"![{alt_text}]({new_path})"

        return match.group(0)

    pattern = r"!\[([^\]]*)\]\(([^)]+)\)"
    return re.sub(pattern, normalize_image_path, text)


def transform_image_paths(content: str) -> str:
    """
    Normalize image paths to use images/ prefix for wiki.

    The wiki stores images in an 'images/' folder at the root.
    This transforms various relative paths to point there.

    Preserves:
    - Content inside fenced code blocks (``` or ~~~)
    - Content inside inline code (backticks)

    Args:
        content: Markdown content with image paths

    Returns:
        Content with normalized image paths
    """
    return _transform_preserving_code_blocks(content, _transform_images_in_text)


def process_file(input_path: str, in_place: bool = False) -> str:
    """
    Process a markdown file and transform its links.

    Args:
        input_path: Path to input file, or '-' for stdin
        in_place: If True, modify the file in place

    Returns:
        Transformed content (only if not in_place)
    """
    if input_path == "-":
        content = sys.stdin.read()
    else:
        with Path(input_path).open("r", encoding="utf-8") as f:
            content = f.read()

    transformed = transform_wiki_links(content)
    transformed = transform_image_paths(transformed)

    if in_place and input_path != "-":
        with Path(input_path).open("w", encoding="utf-8") as f:
            f.write(transformed)
        return ""
    return transformed


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Transform relative markdown links to GitHub Wiki page references."
    )
    parser.add_argument("input", help="Input markdown file path, or '-' for stdin")
    parser.add_argument(
        "--in-place",
        "-i",
        action="store_true",
        help="Modify file in place instead of printing to stdout",
    )

    args = parser.parse_args()

    result = process_file(args.input, args.in_place)
    if result:
        print(result, end="")


if __name__ == "__main__":
    main()
