# github-pages - Part 1

## Split Content

**Trigger**: When creating, modifying, or troubleshooting GitHub Pages documentation with Jekyll.

---

## When to Use

This skill applies when:

- Creating new documentation pages for GitHub Pages
- Modifying Jekyll configuration (`_config.yml`)
- Adding or updating CSS themes
- Fixing broken links in documentation
- Troubleshooting GitHub Pages rendering issues

---

## Jekyll Configuration Best Practices

### Required Plugins

The following plugins MUST be configured in `_config.yml`:

| Plugin                  | Purpose                     | Critical?   |
| ----------------------- | --------------------------- | ----------- |
| `jekyll-relative-links` | Convert `.md` links to HTML | **YES**     |
| `jekyll-remote-theme`   | Use GitHub Pages themes     | Recommended |
| `jekyll-seo-tag`        | SEO metadata                | Recommended |
| `jekyll-sitemap`        | Auto-generate sitemap       | Recommended |
| `jekyll-include-cache`  | Performance optimization    | Optional    |

### Permalink Configuration

```yaml
# _config.yml
collections:
  docs:
    output: true
    permalink: /:collection/:path/

defaults:
  - scope:
      path: ""
      type: "pages"
    values:
      layout: "default"
```

### Relative Links Configuration

```yaml
# CRITICAL: Enable relative links plugin
relative_links:
  enabled: true
  collections: true
```

---

## Markdown Link Format (CRITICAL)

**This is the most common source of broken links on GitHub Pages.**

For complete link formatting rules, escaping patterns, and validation commands, see [markdown-reference](../skills/markdown-reference.md).

### Key Requirements

- **ALL internal links MUST use `./` or `../` prefix** — no bare paths
- **NEVER use backtick-wrapped file references** — use proper markdown links
- **NEVER use absolute GitHub Pages paths** — no `/unity-helpers/...` paths

### Why This Matters

The `jekyll-relative-links` plugin ONLY recognizes links with explicit relative path prefixes. Without `./` or `../`:

- Plugin ignores the link — no conversion occurs
- Link renders as raw markdown file download
- 404 errors on GitHub Pages
- Links may work locally but break in production

### Quick Validation

```bash
# Run IMMEDIATELY after any documentation change:
npm run lint:docs
```

---

## CRITICAL: Never Use Absolute GitHub Pages Paths

### The Problem

Links like `]/unity-helpers/...` are **absolute paths using the GitHub Pages baseurl**. These links:

- ✅ **Work** when viewing the deployed GitHub Pages site
- ❌ **Break** during local validation and CI link checking
- ❌ **Break** when viewing raw markdown files on GitHub

**Why they break in CI**: The `lint-doc-links.ps1` script (and CI workflow) runs on the raw repository, not the deployed site. When the linter sees `]/unity-helpers/docs/overview/getting-started/)`, it interprets `/unity-helpers/...` as a path from the repository root — which doesn't exist.

### Common Broken Patterns to Avoid

```text
❌ WRONG (Absolute GitHub Pages Path)             →  ✅ CORRECT (Relative Path)
──────────────────────────────────────────────────────────────────────────────────
](/unity-helpers/)                                →  ](./README)
](/unity-helpers/#anchor)                         →  ](./README#anchor)
](/unity-helpers/docs/overview/getting-started/)  →  ](./docs/overview/getting-started)
](/unity-helpers/docs/features/animation-events/) →  ](./docs/features/animation-events)
](/unity-helpers/LICENSE)                         →  ](./LICENSE)
](/unity-helpers/CHANGELOG/)                      →  ](./CHANGELOG)
From nested doc: ](/unity-helpers/docs/guides/x)  →  From nested doc: ](../guides/x)
```

> **Note**: All relative paths above should include the `.md` extension in actual usage.

### Why This Happens

The GitHub Pages configuration in `_config.yml` includes:

```yaml
baseurl: "/unity-helpers"
```

This `baseurl` is prepended to all site URLs when deployed. For example:

```text
Repository file:  docs/overview/getting-started      (with extension)
Deployed URL:     https://ambiguous-interactive.github.io/unity-helpers/docs/overview/getting-started/
```

When copying URLs from the live site or using Jekyll's `absolute_url` filter, you get paths starting with `/unity-helpers/`. **These paths are deployment artifacts, not source file references.**

### Detection and Enforcement

The `lint-doc-links.ps1` script automatically detects absolute GitHub Pages paths:

```bash
# This will catch /unity-helpers/ patterns and report errors
npm run lint:docs
```

**Example error output:**

```text
ERROR: index.md:15 - Link uses absolute GitHub Pages path '/unity-helpers/docs/overview/'
       Use relative path instead: './docs/overview/index.md'
```

### Workflow Reminder

⚠️ **After ANY markdown change:**

```bash
# Run IMMEDIATELY after editing any .md file
npm run lint:docs

# The linter will catch:
# - Missing ./ prefix
# - Absolute /unity-helpers/ paths
# - Broken link targets
# - Invalid anchor references
```
