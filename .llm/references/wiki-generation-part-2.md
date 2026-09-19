# wiki-generation - Part 2

## Split Content

## Troubleshooting

### Symptom: Sidebar links show page name instead of display text

**Cause**: Using MediaWiki wikilink syntax `[[Page|Display]]` instead of
Markdown syntax `[Display](Page)`

**Fix**: Convert all sidebar links to Markdown format:

```bash
# Before (broken)
echo "- [[Overview-Getting-Started|Getting Started]]"

# After (correct)
echo "- [Getting Started](Overview-Getting-Started)"
```

### Symptom: Links point to wrong pages

**Cause**: Wiki page naming doesn't match the generated filename

**Fix**: Verify page naming follows the conversion rules:

1. File path segments become hyphen-separated
2. Each segment is capitalized
3. `.md` extension is stripped in links

### Symptom: Sidebar validation passes but links are broken

**Cause**: Validation regex doesn't match the link format being used

**Fix**: Ensure validation regex matches the actual link format:

- For `[Display](Page)`: Use `\]\([A-Za-z0-9_.-]+\)`
- For `[[Page|Display]]`: Use `\[\[[A-Za-z0-9_.-]+` (deprecated)

---

## Testing

**TWO test suites validate wiki generation:**

### Bash Tests (`scripts/tests/test-wiki-generation.sh`)

Fast syntax validation tests that verify:

- Python scripts use Markdown link syntax (not MediaWiki)
- `get_display_name()` function produces correct display names
- Sidebar links are generated in `[Display](Page)` format
- No MediaWiki `[[Page|Display]]` syntax is present

```bash
# Run bash tests (fast, ~2 seconds)
bash scripts/tests/test-wiki-generation.sh
```

### Python Tests (`scripts/wiki/test_wiki_scripts.py`)

Comprehensive unit and integration tests (50+ test cases):

```bash
# Create and activate virtual environment (one time)
python3.11 -m venv .venv
source .venv/bin/activate
python -m pip install -r requirements-wiki.txt

# Run all wiki Python tests
python -m pytest scripts/wiki/test_wiki_scripts.py -v

# Test specific module
python -m pytest \
  scripts/wiki/test_wiki_scripts.py::TestTransformPathToWikiPage -v

# Test on actual repository
python scripts/wiki/prepare_wiki.py \
  --source . --dest /tmp/test-wiki --verbose
```

Tests cover:

- Link transformation (`transform_path_to_wiki_page`, `transform_wiki_links`)
- Image path normalization (`transform_image_paths`)
- Sidebar generation (`generate_sidebar`)
- Footer generation (`generate_footer`)
- Wiki page naming (`path_to_wiki_name`)
- Full pipeline integration (`prepare_wiki`)
- Edge cases (empty content, malformed links, external URLs)

### When Tests Run

- `npm run test:wiki-generation` runs the bash tests only.
- `python -m pytest scripts/wiki/test_wiki_scripts.py -v` runs the Python
  tests only.
- `npm run validate:tests` runs the bash tests only.
- CI runs both suites for PRs and pushes touching wiki files.

---

## Related Files

<!-- markdownlint-disable MD013 -->

| File                                        | Purpose                         |
| ------------------------------------------- | ------------------------------- |
| `.github/workflows/deploy-wiki.yml`         | Main wiki deployment workflow   |
| `.github/workflows/validate-wiki-links.yml` | PR link validation + wiki tests |
| `scripts/wiki/prepare_wiki.py`              | Main wiki preparation script    |
| `scripts/wiki/transform_wiki_links.py`      | Link transformation logic       |
| `scripts/wiki/generate_wiki_sidebar.py`     | Sidebar generation              |
| `scripts/wiki/generate_wiki_footer.py`      | Footer generation               |
| `scripts/wiki/test_wiki_scripts.py`         | Python test suite (50+ tests)   |
| `scripts/tests/test-wiki-generation.sh`     | Bash syntax validation tests    |
| `.githooks/pre-push`                        | Fast changed-file pre-push hook |
| `docs/`                                     | Source documentation files      |

<!-- markdownlint-enable MD013 -->

---

## Critical Rules

### Test Synchronization (CRITICAL)

When refactoring wiki generation scripts (e.g., moving from inline bash to Python):

1. **Update BOTH test suites** — Bash tests (`test-wiki-generation.sh`)
   AND Python tests (`test_wiki_scripts.py`)
2. **Bash tests validate Python scripts** — The bash tests grep Python
   files for correct patterns
3. **Run both locally before pushing** — use `npm run test:wiki-generation`
   for bash coverage and `python -m pytest scripts/wiki/test_wiki_scripts.py -v`
   for Python coverage. The local pre-push hook does not run wiki regression
   suites.

**Example failure scenario (what caused this issue):**

- Wiki generation was refactored from inline bash in `deploy-wiki.yml`
  to Python scripts
- Python tests were updated, but bash tests still checked for old
  inline bash patterns
- CI failed because bash tests looked for `echo "- [Home](Home)"`
  which no longer existed
- Fix: Updated bash tests to check Python scripts for correct
  Markdown link patterns

### Comment and Documentation Synchronization (CRITICAL)

When modifying wiki generation scripts, **update comments that reference
implementation details**:

1. **Remove dead code** — Delete unused functions entirely; don't leave
   them "for future use"
2. **Update implementation references** — If code moves from bash/Perl
   to Python, update comments
3. **Keep docstrings current** — Ensure module and function docstrings
   reflect actual behavior

**Common mistakes**:

- Leaving comments like "Perl-based transformation" when Python is now used
- Keeping helper functions that were part of a refactor but never integrated
- Docstrings describing an old algorithm after a rewrite

---

## Related Skills

<!-- markdownlint-disable MD013 -->

- [github-actions-script-pattern](../skills/github-actions-script-pattern.md) — Why we use scripts not embedded YAML
- [update-documentation](../skills/update-documentation.md) — Documentation standards
- [validate-before-commit](../skills/validate-before-commit.md) — Pre-commit checks
- [github-pages](../skills/github-pages.md) — GitHub Pages deployment (separate from wiki)

<!-- markdownlint-enable MD013 -->
