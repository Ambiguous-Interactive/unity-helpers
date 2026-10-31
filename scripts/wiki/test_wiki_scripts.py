#!/usr/bin/env python3
"""
Tests for the wiki generation scripts.

Run with: uv run --project scripts/python~ --locked pytest scripts/wiki/test_wiki_scripts.py -v
"""

import sys
import tempfile
from collections.abc import Generator
from pathlib import Path

import pytest

# Add scripts directory to path (test file is in scripts/wiki/)
sys.path.insert(0, str(Path(__file__).parent))

from generate_wiki_footer import generate_footer
from generate_wiki_sidebar import generate_sidebar, get_display_name
from prepare_wiki import path_to_wiki_name, prepare_wiki
from transform_wiki_links import (
    transform_image_paths,
    transform_path_to_wiki_page,
    transform_wiki_links,
)


class TestTransformPathToWikiPage:
    """Tests for transform_path_to_wiki_page function."""

    def test_simple_readme_reference(self) -> None:
        assert transform_path_to_wiki_page("../README.md") == "Home"

    def test_relative_readme_reference(self) -> None:
        assert transform_path_to_wiki_page("./README.md") == "Home"

    def test_root_readme_reference(self) -> None:
        assert transform_path_to_wiki_page("README.md") == "Home"

    def test_docs_overview_file(self) -> None:
        result = transform_path_to_wiki_page("./docs/overview/roadmap.md")
        assert result == "Overview-Roadmap"

    def test_docs_feature_file(self) -> None:
        result = transform_path_to_wiki_page("./docs/features/inspector.md")
        assert result == "Features-Inspector"

    def test_relative_parent_docs_path(self) -> None:
        result = transform_path_to_wiki_page("../overview/getting-started.md")
        assert result == "Overview-Getting-Started"

    def test_hyphenated_filename(self) -> None:
        result = transform_path_to_wiki_page("./docs/guides/quick-start-guide.md")
        assert result == "Guides-Quick-Start-Guide"

    def test_deep_nested_path(self) -> None:
        result = transform_path_to_wiki_page("./docs/features/inspector/attributes/show-if.md")
        assert result == "Features-Inspector-Attributes-Show-If"

    def test_changelog_reference(self) -> None:
        assert transform_path_to_wiki_page("./CHANGELOG.md") == "CHANGELOG"

    def test_changelog_parent_reference(self) -> None:
        assert transform_path_to_wiki_page("../CHANGELOG.md") == "CHANGELOG"

    def test_preserves_anchor_links(self) -> None:
        result = transform_path_to_wiki_page("./docs/overview/roadmap.md#future")
        assert result == "Overview-Roadmap#future"

    def test_preserves_complex_anchor(self) -> None:
        result = transform_path_to_wiki_page("./README.md#installation-guide")
        assert result == "Home#installation-guide"


class TestTransformWikiLinks:
    """Tests for transform_wiki_links function."""

    def test_simple_link_transformation(self) -> None:
        content = "Check out the [roadmap](./docs/overview/roadmap.md) for details."
        result = transform_wiki_links(content)
        assert "[roadmap](Overview-Roadmap)" in result

    def test_readme_link_becomes_home(self) -> None:
        content = "See [README](./README.md) for installation."
        result = transform_wiki_links(content)
        assert "[README](Home)" in result

    def test_preserves_external_links(self) -> None:
        content = "Visit [GitHub](https://github.com) for more."
        result = transform_wiki_links(content)
        assert content == result

    def test_preserves_http_links(self) -> None:
        content = "See [docs](http://example.com/docs) online."
        result = transform_wiki_links(content)
        assert content == result

    def test_preserves_anchor_only_links(self) -> None:
        content = "Jump to [section](#installation) below."
        result = transform_wiki_links(content)
        assert content == result

    def test_multiple_links_in_content(self) -> None:
        content = """
Read the [intro](./docs/overview/intro.md) first.
Then check [features](./docs/features/index.md).
"""
        result = transform_wiki_links(content)
        assert "[intro](Overview-Intro)" in result
        assert "[features](Features-Index)" in result

    def test_mixed_external_and_internal_links(self) -> None:
        content = """
[Internal](./docs/overview/intro.md) and [External](https://example.com).
"""
        result = transform_wiki_links(content)
        assert "[Internal](Overview-Intro)" in result
        assert "[External](https://example.com)" in result

    def test_link_with_anchor(self) -> None:
        content = "See [section](./docs/overview/intro.md#getting-started)."
        result = transform_wiki_links(content)
        assert "[section](Overview-Intro#getting-started)" in result

    def test_tilde_code_block_links_preserved(self) -> None:
        """Links inside ~~~ fenced code blocks should NOT be transformed."""
        content = """Some text

~~~markdown
[link](./docs/overview/intro.md)
[another](../README.md)
~~~

After block"""
        result = transform_wiki_links(content)
        # Links inside tilde code block should be preserved exactly
        assert "[link](./docs/overview/intro.md)" in result
        assert "[another](../README.md)" in result

    def test_link_syntax_inside_inline_code_preserved(self) -> None:
        """Link syntax inside backticks should NOT be transformed."""
        content = "Use `[link](./docs/test.md)` as an example."
        result = transform_wiki_links(content)
        # The link syntax inside inline code should be preserved exactly
        assert "`[link](./docs/test.md)`" in result

    def test_link_text_with_backticks_transformed(self) -> None:
        """Links with backticks in the link text ARE transformed."""
        content = "See [the `transform` function](./docs/api.md) for details."
        result = transform_wiki_links(content)
        # Link should be transformed, backticks in text preserved
        assert "[the `transform` function](Api)" in result

    def test_double_backticks_preserved(self) -> None:
        """Content inside double backticks should be preserved."""
        content = "Use ``[link](./docs/test.md)`` in your code."
        result = transform_wiki_links(content)
        # Double backticks create inline code, so content should be preserved
        assert "``[link](./docs/test.md)``" in result

    def test_content_between_consecutive_code_blocks_transformed(self) -> None:
        """Links between consecutive code blocks should be transformed."""
        content = """```python
code1()
```

See [link](./docs/test.md) between blocks.

```python
code2()
```"""
        result = transform_wiki_links(content)
        # Link between code blocks should be transformed
        assert "[link](Test)" in result
        # Code blocks should be preserved
        assert "code1()" in result
        assert "code2()" in result


class TestTransformImagePaths:
    """Tests for transform_image_paths function."""

    def test_simple_image_path(self) -> None:
        content = "![Alt](./docs/images/screenshot.png)"
        result = transform_image_paths(content)
        assert "![Alt](images/screenshot.png)" in result

    def test_nested_image_path(self) -> None:
        content = "![Screenshot](../docs/images/features/inspector.png)"
        result = transform_image_paths(content)
        assert "![Screenshot](images/features/inspector.png)" in result

    def test_preserves_external_images(self) -> None:
        content = "![Logo](https://example.com/logo.png)"
        result = transform_image_paths(content)
        assert content == result

    def test_multiple_images(self) -> None:
        content = """
![A](./docs/images/a.png)
![B](../docs/images/b.png)
"""
        result = transform_image_paths(content)
        assert "![A](images/a.png)" in result
        assert "![B](images/b.png)" in result

    def test_preserves_images_in_fenced_code_block(self) -> None:
        """Image paths in fenced code blocks should NOT be transformed."""
        content = """Some text
```markdown
![Example](./docs/images/example.png)
```
After code block"""
        result = transform_image_paths(content)
        # The image inside the code block should be preserved exactly
        assert "![Example](./docs/images/example.png)" in result

    def test_preserves_images_in_tilde_code_block(self) -> None:
        """Image paths in tilde-fenced code blocks should NOT be transformed."""
        content = """Some text
~~~md
![Example](../docs/images/test.png)
~~~
After code block"""
        result = transform_image_paths(content)
        # The image inside the code block should be preserved exactly
        assert "![Example](../docs/images/test.png)" in result

    def test_tilde_code_block_multiple_images_preserved(self) -> None:
        """Multiple images in tilde code blocks should all be preserved."""
        content = """~~~markdown
![First](./docs/images/a.png)
![Second](../docs/images/b.jpg)
~~~

![Outside](./docs/images/outside.png)"""
        result = transform_image_paths(content)
        # Images inside tilde code block should be preserved
        assert "![First](./docs/images/a.png)" in result
        assert "![Second](../docs/images/b.jpg)" in result
        # Image outside should be transformed
        assert "![Outside](images/outside.png)" in result

    def test_transforms_images_outside_code_blocks(self) -> None:
        """Image paths outside code blocks should be transformed."""
        content = """![Before](./docs/images/before.png)

```markdown
![Inside](./docs/images/inside.png)
```

![After](./docs/images/after.png)"""
        result = transform_image_paths(content)
        # Outside code blocks should be transformed
        assert "![Before](images/before.png)" in result
        assert "![After](images/after.png)" in result
        # Inside code block should be preserved
        assert "![Inside](./docs/images/inside.png)" in result

    def test_preserves_images_in_inline_code(self) -> None:
        """Image paths in inline code should NOT be transformed."""
        content = "Use `![example](./docs/images/icon.png)` in markdown."
        result = transform_image_paths(content)
        # The image inside inline code should be preserved
        assert "`![example](./docs/images/icon.png)`" in result

    def test_transforms_images_near_inline_code(self) -> None:
        """Images near but outside inline code should be transformed."""
        content = "See `code` then ![img](./docs/images/pic.png) for help."
        result = transform_image_paths(content)
        assert "![img](images/pic.png)" in result
        assert "`code`" in result


class TestGenerateFooter:
    """Tests for generate_footer function."""

    def test_footer_contains_documentation_link(self) -> None:
        footer = generate_footer()
        assert "Documentation" in footer

    def test_footer_contains_repo_link(self) -> None:
        footer = generate_footer()
        assert "unity-helpers" in footer.lower() or "Unity Helpers" in footer

    def test_footer_contains_separator(self) -> None:
        footer = generate_footer()
        assert "---" in footer or "|" in footer

    def test_footer_no_leading_whitespace(self) -> None:
        footer = generate_footer()
        # First line shouldn't have leading spaces
        first_line = footer.split("\n")[0]
        assert first_line == first_line.lstrip()


class TestGetDisplayName:
    """Tests for get_display_name function."""

    def test_simple_name(self) -> None:
        assert get_display_name("Home") == "Home"

    def test_hyphenated_name(self) -> None:
        assert get_display_name("Getting-Started") == "Getting Started"

    def test_prefixed_name(self) -> None:
        # Should strip common prefixes
        result = get_display_name("Overview-Getting-Started")
        assert "Getting Started" in result or "Overview Getting Started" in result

    def test_changelog(self) -> None:
        assert "CHANGELOG" in get_display_name("CHANGELOG")


class TestGenerateSidebar:
    """Tests for generate_sidebar function."""

    def test_sidebar_generation_with_files(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            wiki_dir = Path(tmpdir)
            # Create some wiki files
            (wiki_dir / "Home.md").write_text("# Home")
            (wiki_dir / "Overview-Intro.md").write_text("# Intro")
            (wiki_dir / "Features-Inspector-Buttons.md").write_text("# Inspector Buttons")

            sidebar = generate_sidebar(wiki_dir)

            assert "Home" in sidebar
            assert "Overview" in sidebar or "Intro" in sidebar
            # Should have Inspector Features section when files match pattern
            assert "Inspector" in sidebar or "Buttons" in sidebar

    def test_sidebar_has_links(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            wiki_dir = Path(tmpdir)
            (wiki_dir / "Home.md").write_text("# Home")
            (wiki_dir / "Test-Page.md").write_text("# Test")

            sidebar = generate_sidebar(wiki_dir)

            # Should have markdown-style links like [text](page)
            assert "[" in sidebar
            assert "](" in sidebar


class TestPathToWikiName:
    """Tests for path_to_wiki_name function."""

    def test_simple_path(self) -> None:
        assert path_to_wiki_name("intro.md") == "Intro.md"

    def test_nested_path(self) -> None:
        result = path_to_wiki_name("overview/getting-started.md")
        assert result == "Overview-Getting-Started.md"

    def test_deeply_nested_path(self) -> None:
        result = path_to_wiki_name("features/inspector/buttons.md")
        assert result == "Features-Inspector-Buttons.md"

    def test_hyphenated_filename(self) -> None:
        result = path_to_wiki_name("guides/quick-start.md")
        assert result == "Guides-Quick-Start.md"


class TestPrepareWiki:
    """Integration tests for prepare_wiki function."""

    @pytest.fixture
    def source_repo(self) -> Generator[Path, None, None]:
        """Create a mock source repository structure."""
        with tempfile.TemporaryDirectory() as tmpdir:
            source = Path(tmpdir)

            # Create README
            (source / "README.md").write_text(
                "# My Project\n\nSee [docs](./docs/overview/intro.md)."
            )

            # Create CHANGELOG
            (source / "CHANGELOG.md").write_text("# Changelog\n\n## [1.0.0]")

            # Create docs structure
            docs = source / "docs"
            (docs / "overview").mkdir(parents=True)
            (docs / "features").mkdir(parents=True)
            (docs / "images").mkdir(parents=True)

            (docs / "overview" / "intro.md").write_text(
                "# Introduction\n\nSee [home](../../README.md)."
            )
            (docs / "features" / "buttons.md").write_text("# Buttons\n\nFeature docs.")

            # Create an image
            (docs / "images" / "screenshot.png").write_bytes(b"PNG_DATA")

            yield source

    @pytest.fixture
    def dest_wiki(self) -> Generator[Path, None, None]:
        """Create a temporary destination directory."""
        with tempfile.TemporaryDirectory() as tmpdir:
            yield Path(tmpdir)

    def test_creates_home_from_readme(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        assert (dest_wiki / "Home.md").exists()

    def test_creates_changelog(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        assert (dest_wiki / "CHANGELOG.md").exists()

    def test_creates_docs_pages(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        assert (dest_wiki / "Overview-Intro.md").exists()
        assert (dest_wiki / "Features-Buttons.md").exists()

    def test_copies_images(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        assert (dest_wiki / "images" / "screenshot.png").exists()

    def test_transforms_links_in_readme(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        home_content = (dest_wiki / "Home.md").read_text()
        # Should have transformed link, not original
        assert "(Overview-Intro)" in home_content
        assert "./docs/overview/intro.md" not in home_content

    def test_transforms_links_in_docs(self, source_repo: Path, dest_wiki: Path) -> None:
        prepare_wiki(source_repo, dest_wiki)
        intro_content = (dest_wiki / "Overview-Intro.md").read_text()
        # Should link to Home, not README
        assert "(Home)" in intro_content
        assert "../../README.md" not in intro_content

    def test_returns_correct_counts(self, source_repo: Path, dest_wiki: Path) -> None:
        pages, images = prepare_wiki(source_repo, dest_wiki)
        assert pages == 4  # Home, CHANGELOG, Overview-Intro, Features-Buttons
        assert images == 1  # screenshot.png


class TestCodeBlockPreservation:
    """Tests for preserving code block content exactly."""

    def test_fenced_code_block_content_unchanged(self) -> None:
        """Code inside fenced blocks should not be modified."""
        content = """Some text before

```python
def hello():
    print("world")
```

Some text after"""
        result = transform_wiki_links(content)
        # Code block content should be identical
        assert '```python\ndef hello():\n    print("world")\n```' in result

    def test_fenced_code_block_indentation_preserved(self) -> None:
        """Indentation inside code blocks should be preserved exactly."""
        content = """```yaml
root:
    child:
        grandchild: value
            deeply:
                nested: true
```"""
        result = transform_wiki_links(content)
        # Check exact indentation is preserved
        assert "    child:" in result
        assert "        grandchild: value" in result
        assert "            deeply:" in result
        assert "                nested: true" in result

    def test_no_leading_space_insertion(self) -> None:
        """Lines should not gain unexpected leading spaces."""
        content = """First line
Second line
Third line"""
        result = transform_wiki_links(content)
        lines = result.split("\n")
        assert lines[0] == "First line"
        assert lines[1] == "Second line"
        assert lines[2] == "Third line"

    def test_code_block_with_link_syntax_inside(self) -> None:
        """Links inside code blocks should NOT be transformed."""
        content = """Here's an example:

```markdown
See [the docs](./docs/overview/intro.md) for more info.
[Another link](../README.md)
```

Outside the block."""
        result = transform_wiki_links(content)
        # Links inside code block should be preserved exactly
        assert "[the docs](./docs/overview/intro.md)" in result
        assert "[Another link](../README.md)" in result

    def test_multiple_code_blocks_preserved(self) -> None:
        """Multiple code blocks should all be preserved correctly."""
        content = """First block:

```python
x = 1
    y = 2
```

Second block:

```javascript
const a = 1;
    const b = 2;
```

Done."""
        result = transform_wiki_links(content)
        assert "x = 1\n    y = 2" in result
        assert "const a = 1;\n    const b = 2" in result

    def test_inline_code_preserved(self) -> None:
        """Inline backtick code should be preserved."""
        content = "Use `transform_wiki_links(content)` to transform."
        result = transform_wiki_links(content)
        assert "`transform_wiki_links(content)`" in result

    def test_empty_code_blocks_preserved(self) -> None:
        """Empty code blocks should be preserved exactly."""
        content = """Before

```
```

After with [link](./docs/test.md)"""
        result = transform_wiki_links(content)
        # Empty code block should be preserved
        assert "```\n```" in result
        # Link outside should be transformed
        assert "[link](Test)" in result

    def test_code_block_at_end_of_file(self) -> None:
        """Code block at EOF (no trailing newline) should be handled correctly."""
        content = """[link](./docs/test.md)

```python
def end():
    pass
```"""
        result = transform_wiki_links(content)
        # Link should be transformed
        assert "[link](Test)" in result
        # Code block content should be preserved
        assert "def end():" in result
        assert "    pass" in result
        # Should end with closing fence
        assert result.rstrip().endswith("```")


class TestWhitespacePreservation:
    """Tests for general whitespace preservation."""

    def test_empty_lines_preserved(self) -> None:
        """Empty lines between paragraphs should be preserved."""
        content = """First paragraph.


Third paragraph after two empty lines.



Fifth paragraph after three empty lines."""
        result = transform_wiki_links(content)
        # Count empty lines
        assert "\n\n\n" in result  # Two empty lines between first and third
        assert "\n\n\n\n" in result  # Three empty lines between third and fifth

    def test_trailing_spaces_preserved(self) -> None:
        """Trailing spaces (markdown line breaks) should be preserved."""
        # Two trailing spaces create a line break in markdown
        content = "Line with trailing spaces  \nNext line"
        result = transform_wiki_links(content)
        assert "Line with trailing spaces  \n" in result

    def test_tab_characters_preserved(self) -> None:
        """Tab characters should be preserved."""
        content = "Column1\tColumn2\tColumn3"
        result = transform_wiki_links(content)
        assert "\t" in result
        assert result == content

    def test_mixed_indentation_preserved(self) -> None:
        """Mixed spaces and tabs should be preserved."""
        content = """Normal line
\tTab indented
    Space indented
\t    Mixed indent"""
        result = transform_wiki_links(content)
        assert "\tTab indented" in result
        assert "    Space indented" in result
        assert "\t    Mixed indent" in result


class TestImagePathCodeBlockPreservation:
    """Tests for image path transformation with code block preservation."""

    def test_image_path_in_code_block_preserved(self) -> None:
        """Image syntax in code blocks should NOT be transformed."""
        content = """Example of markdown image syntax:

```markdown
![My Image](./docs/images/screenshot.png)
![Another](../docs/images/photo.jpg)
```

End of example."""
        result = transform_image_paths(content)
        # Images inside code block should be preserved exactly
        assert "![My Image](./docs/images/screenshot.png)" in result
        assert "![Another](../docs/images/photo.jpg)" in result

    def test_image_path_outside_code_block_transformed(self) -> None:
        """Normal images outside code blocks ARE transformed."""
        content = """Here's an image:

![Screenshot](./docs/images/screenshot.png)

And in code:

```markdown
![Code Image](./docs/images/code-example.png)
```

Another real image:

![Photo](./docs/images/photo.jpg)"""
        result = transform_image_paths(content)
        # Outside code blocks should be transformed
        assert "![Screenshot](images/screenshot.png)" in result
        assert "![Photo](images/photo.jpg)" in result
        # Inside code block should be preserved
        assert "![Code Image](./docs/images/code-example.png)" in result


class TestRoundTripPreservation:
    """Round-trip tests ensuring content without transformable elements is unchanged."""

    def test_complex_document_without_links_unchanged(self) -> None:
        """Complex document without links should be completely unchanged."""
        content = """# Header

This is a paragraph with **bold** and *italic* text.

## Subheader

- Bullet point 1
- Bullet point 2
    - Nested bullet

1. Numbered item
2. Another item

> Blockquote text
> More quoted text

| Column 1 | Column 2 |
|----------|----------|
| Cell 1   | Cell 2   |

---

Horizontal rule above.

```python
def example():
    return "code"
```

End of document."""
        result = transform_wiki_links(content)
        assert result == content

    def test_content_with_only_code_blocks_unchanged(self) -> None:
        """Content containing only code blocks should be completely unchanged."""
        content = """```python
import os
import sys

def main():
    path = "./docs/images/test.png"
    link = "[link](./docs/overview/intro.md)"
    return path, link
```

```bash
#!/bin/bash
cd ./docs/images
ls -la
```

```markdown
# Example
[link](./README.md)
![image](./docs/images/pic.png)
```"""
        result = transform_wiki_links(content)
        # All content is in code blocks, so nothing should be transformed
        assert result == content


class TestEdgeCases:
    """Edge case tests for wiki transformation."""

    def test_empty_content(self) -> None:
        assert transform_wiki_links("") == ""
        assert transform_image_paths("") == ""

    def test_no_links(self) -> None:
        content = "Just plain text with no links."
        assert transform_wiki_links(content) == content

    def test_malformed_links_preserved(self) -> None:
        content = "Broken [link(missing bracket"
        assert transform_wiki_links(content) == content

    def test_code_block_links_preserved(self) -> None:
        """Links in code blocks should be preserved exactly."""
        content = """```markdown
[link](./docs/example.md)
```"""
        result = transform_wiki_links(content)
        # Links inside code blocks should be preserved, not transformed
        assert "[link](./docs/example.md)" in result

    def test_inline_code_links_transformed(self) -> None:
        # Links outside inline code should still transform
        content = "Use `code` then [link](./docs/test.md)."
        result = transform_wiki_links(content)
        assert "(Test)" in result

    def test_unicode_content_preserved(self) -> None:
        # Unicode characters in content should be preserved
        content = "日本語 [link](./docs/test.md) émojis 🎉"
        result = transform_wiki_links(content)
        assert "日本語" in result
        assert "émojis" in result
        assert "🎉" in result
        assert "(Test)" in result

    def test_unicode_in_link_text(self) -> None:
        # Unicode in link display text should be preserved
        content = "[日本語ドキュメント](./docs/overview/intro.md)"
        result = transform_wiki_links(content)
        assert result == "[日本語ドキュメント](Overview-Intro)"

    def test_special_characters_in_anchor(self) -> None:
        # Anchors can have various characters
        content = "[link](./docs/test.md#my-section-1.2)"
        result = transform_wiki_links(content)
        assert "(Test#my-section-1.2)" in result


class TestEmptyDirectories:
    """Tests for handling empty or missing directories."""

    def test_prepare_wiki_empty_docs(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            source = Path(tmpdir)
            dest = Path(tmpdir) / "wiki"

            # Create only README, no docs dir
            (source / "README.md").write_text("# Test")

            pages, images = prepare_wiki(source, dest)
            assert pages == 1  # Just Home.md
            assert images == 0

    def test_prepare_wiki_no_images(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            source = Path(tmpdir)
            dest = Path(tmpdir) / "wiki"

            # Create README and docs but no images
            (source / "README.md").write_text("# Test")
            (source / "docs").mkdir()
            (source / "docs" / "test.md").write_text("# Test Doc")

            pages, images = prepare_wiki(source, dest)
            assert pages == 2  # Home.md and Test.md
            assert images == 0

    def test_sidebar_empty_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            wiki_dir = Path(tmpdir)
            # Create only required Home.md
            (wiki_dir / "Home.md").write_text("# Home")

            sidebar = generate_sidebar(wiki_dir)
            # Should still generate valid sidebar structure
            assert "Home" in sidebar
            assert "## 📚 Documentation" in sidebar


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
