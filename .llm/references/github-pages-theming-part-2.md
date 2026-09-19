# github-pages-theming - Part 2

## Split Content

## Avoid Overly Broad Structural Selectors

When targeting specific page elements (banners, hero sections, featured content), use pseudo-classes to prevent unintended side effects on similar elements:

```css
/* ❌ WRONG: Affects ALL centered paragraphs including badges */
section p[align="center"] {
  margin: 1rem auto;
  max-width: 800px;
}

/* ✅ CORRECT: Only affects the first centered paragraph (banner) */
section p[align="center"]:first-of-type {
  margin: 1rem auto;
  max-width: 800px;
}
```

**Why this matters:**

1. **Unintended cascade** — Multiple elements may match broad selectors
2. **Structural precision** — `:first-of-type`, `:last-of-type`, `:nth-of-type()` target specific positions
3. **Future-proofing** — Prevents CSS conflicts when page content structure changes

---

## Use ARIA Attributes for JavaScript-Controlled States

```css
/* ❌ WRONG: CSS class never applied by JavaScript */
table[data-sortable] th.sort-indicator-active::after {
  display: none;
}

/* ✅ CORRECT: Use ARIA attributes that JavaScript already manages */
table[data-sortable] th[aria-sort]:not([aria-sort="none"])::after {
  display: none;
}
```

**Why ARIA over custom classes:**

1. **Single source of truth** — JavaScript sets ARIA for accessibility; CSS uses same attributes
2. **No orphaned classes** — Can't have CSS targeting a class that JavaScript never applies
3. **Accessibility alignment** — Visual presentation matches what screen readers see

---

## Preserve Semantic Sort Order with `data-sort-value`

For categorical table values (for example `Fastest`, `Very Fast`, `Very Slow`), lexical sorting is usually wrong.
Emit an explicit semantic key in markup and prefer it in the sorter:

```html
<td data-sort-value="6">Fastest</td>
<td data-sort-value="1">Very Slow</td>
```

```js
const valueA = (cellA.getAttribute("data-sort-value") || cellA.textContent).trim();
```

This keeps display labels user-friendly while ensuring GitHub Pages sorting remains numerically meaningful.

---

## Related Skills

- [github-pages](../skills/github-pages.md) — Jekyll configuration, markdown links, CI/CD validation
- [markdown-reference](../skills/markdown-reference.md) — Link formatting, escaping, linting rules
