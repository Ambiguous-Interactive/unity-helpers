# github-pages - Part 2

## Split Content

### Quick Conversion Guide

When converting absolute GitHub Pages URLs to relative paths:

1. **Remove** the `/unity-helpers/` prefix
2. **Add** the appropriate relative prefix (`./` or `../`)
3. **Add** the `.md` extension for markdown files
4. **Adjust** `../` depth based on the source file's location

**Example conversion** (from a file in `docs/guides/`):

```text
Original (broken):  /unity-helpers/docs/overview/getting-started/
Step 1 - Remove prefix:  docs/overview/getting-started/
Step 2 - Add relative prefix:  ../overview/getting-started/
Step 3 - Add extension:  ../overview/getting-started  (add extension)
Final (correct):  ../overview/getting-started  (with extension)
```

---

## CSS Theming

For comprehensive CSS theming guidance including theme switching, accessibility requirements, and selector best practices, see [github-pages-theming](../skills/github-pages-theming.md).

---

## Testing Links Locally

### Local Jekyll Server

```bash
# Install dependencies (first time only)
bundle install

# Run local server with live reload
bundle exec jekyll serve --livereload

# Server runs at http://localhost:4000/unity-helpers/
```

### Common Issues and Fixes

| Issue                         | Cause                                | Fix                                     |
| ----------------------------- | ------------------------------------ | --------------------------------------- |
| Links show as `.md` downloads | Missing `./` prefix                  | Add `./` or `../` to all internal links |
| 404 on nested pages           | Wrong relative path depth            | Count `../` correctly from current file |
| Styles not loading            | Wrong `baseurl` in `_config.yml`     | Ensure `baseurl` matches repo name      |
| Theme not applying            | Missing `jekyll-remote-theme` plugin | Add to `_config.yml` plugins list       |
| Relative links not converting | Plugin not enabled                   | Add `relative_links: enabled: true`     |

### Testing Checklist

- [ ] All `.md` links have `./` or `../` prefix
- [ ] Click every link on every page
- [ ] Test from root AND nested directories
- [ ] Verify no raw `.md` file downloads
- [ ] Check browser console for 404 errors

---

## CI/CD Validation

### Required Validation Commands

**MANDATORY**: Run these before committing documentation changes:

```bash
# Check all documentation links (MUST PASS)
npm run lint:docs

# Check markdown formatting
npm run lint:markdown

# Check Prettier formatting
npm run format:md:check

# Or run full content validation (includes all above):
npm run validate:content
```

### Link Checking Script

The `lint-doc-links` script validates:

- All internal `.md` links resolve to existing files
- Links use proper `./` or `../` prefixes
- No broken anchor links (`#section-name`)
- No backtick-wrapped file references

```bash
# Verbose output for debugging
pwsh ./scripts/lint-doc-links.ps1 -VerboseOutput
```

### GitHub Actions Workflow

The `.github/workflows/lint-doc-links.yml` workflow runs automatically on:

- Pull requests affecting documentation
- Pushes to main branch

**PRs with broken documentation links will be blocked.**

---

## Common CI Failures

When CI fails on documentation PRs, here are the most common causes and fixes:

| Failure Type                     | Error Message (Pattern)                   | Fix                                                           |
| -------------------------------- | ----------------------------------------- | ------------------------------------------------------------- |
| Missing `./` prefix on links     | `Link missing relative prefix`            | Add `./` to all internal links → run `npm run lint:docs`      |
| Broken internal link             | `Link target does not exist`              | Fix the path or create missing file → run `npm run lint:docs` |
| Unescaped example links          | `Link target does not exist` (for demos)  | Wrap example links in code blocks → run `npm run lint:docs`   |
| Unknown words in spelling check  | `Unknown word: someWord`                  | Add word to `cspell.json` words array                         |
| Trailing spaces in YAML          | `Delete trailing whitespace`              | Run `node scripts/run-prettier.js --write -- <file>`          |
| Markdown formatting issues       | Various markdownlint errors               | Run `npm run lint:markdown`                                   |
| Backtick-wrapped file references | `File reference should not use backticks` | Use markdown links instead of backticks for file references   |

### Debugging Failed CI

```bash
# Step 1: Reproduce locally (run ALL of these)
npm run lint:docs          # Check link formats and targets
npm run lint:spelling      # Check for unknown words
npm run lint:markdown      # Check markdown formatting
npm run format:md:check    # Check Prettier formatting

# Step 2: Auto-fix what can be fixed
node scripts/run-prettier.js --write -- "**/*.md"

# Step 3: Manual fixes for remaining issues
# - Add ./  to internal links
# - Add unknown words to cspell.json
# - Fix broken link targets
```

---

## Quick Reference

### Configuration Checklist

- [ ] `jekyll-relative-links` plugin enabled
- [ ] `relative_links.enabled: true` in `_config.yml`
- [ ] `baseurl` matches repository name
- [ ] All internal links use `./` or `../` prefix
- [ ] CSS variables defined in `:root`
- [ ] Remote theme elements overridden transparently

---

## Related Skills

- [github-pages-theming](../skills/github-pages-theming.md) — CSS theming, accessibility, selectors
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, escaping, linting rules
- [update-documentation](../skills/update-documentation.md) — Documentation standards and CHANGELOG format
- [formatting](../skills/formatting.md) — CSharpier, Prettier, markdownlint workflow
