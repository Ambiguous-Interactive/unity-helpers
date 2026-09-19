# github-pages-theming - Part 1

## Split Content

**Trigger**: When customizing CSS themes for GitHub Pages or Jekyll documentation sites.

---

## When to Use

This skill applies when:

- Overriding remote Jekyll themes (like `pages-themes/minimal`)
- Creating dark/light theme switching
- Fixing "box" appearance issues in themed pages
- Adding CSS accessibility features
- Debugging CSS specificity issues with framework components

---

## CSS Variables in `:root`

**Always define default CSS variables in `:root`** to ensure themes work before JavaScript loads:

```css
:root {
  /* Define ALL theme variables with defaults */
  --bg-primary: #1e1e1e;
  --bg-secondary: #252526;
  --text-primary: #d4d4d4;
  --text-secondary: #b0b0b0;
  --border-color: #3c3c3c;
  --link-color: #569cd6;
  /* ... all other variables */
}
```

---

## Override Remote Theme Elements Comprehensively

When using a remote theme (like `pages-themes/minimal`), override ALL structural elements to prevent "box" appearance:

```css
/* Make all nested elements transparent */
.wrapper,
.wrapper > *,
.inner,
header,
header *,
section,
section *,
footer,
footer * {
  background-color: transparent !important;
  box-shadow: none !important;
}

/* Re-apply backgrounds ONLY to root containers */
body {
  background-color: var(--bg-primary) !important;
}

header {
  background-color: var(--bg-primary) !important;
  border-bottom: 1px solid var(--border-color) !important;
}

section {
  background-color: var(--bg-primary) !important;
}

footer {
  background-color: var(--bg-primary) !important;
  border-top: 1px solid var(--border-color) !important;
}
```

---

## Theming Best Practices

| Principle                    | Implementation                                                        |
| ---------------------------- | --------------------------------------------------------------------- |
| Transparent nested elements  | Apply `background: transparent` to all children                       |
| Root-only backgrounds        | Apply background colors only to `body`, `header`, `section`, `footer` |
| Consistent color family      | Use same `--bg-primary` for all containers                            |
| No box shadows on containers | Remove `box-shadow` from theme elements                               |
| Use `!important` sparingly   | Required to override remote theme styles                              |

---

## Theme Switching Support

```css
/* Dark theme (default) */
[data-theme="dark"] {
  --bg-primary: var(--dark-bg-primary);
  --text-primary: var(--dark-text-primary);
  /* ... */
}

/* Light theme */
[data-theme="light"] {
  --bg-primary: var(--light-bg-primary);
  --text-primary: var(--light-text-primary);
  /* ... */
}
```

---

## CSS Accessibility Requirements

**Always respect `prefers-reduced-motion` for animations**. This is required by WCAG 2.1 SC 2.3.3 (Animation from Interactions) for users with vestibular disorders.

```css
/* ✅ CORRECT: Completely disable animation */
@media (prefers-reduced-motion: reduce) {
  *,
  *::before,
  *::after {
    animation-duration: 0s !important;
    transition-duration: 0s !important;
    scroll-behavior: auto !important;
  }
}

/* ❌ WRONG: Near-zero values still trigger animation */
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important; /* Still plays briefly! */
  }
}
```

**Why `0s` not `0.01ms`**:

- `0s` truly disables the animation—no visual flicker, no animation callbacks
- `0.01ms` was a legacy workaround for old browser bugs that is now obsolete
- Semantically clearer: `0s` means "no animation" while `0.01ms` implies "very fast animation"

---

## CSS Selector Scoping Consistency

**CRITICAL**: When styling components in a framework (MkDocs, Jekyll themes, etc.), use consistent scoping for related rules. Inconsistent prefixes cause specificity bugs.

```css
/* ❌ WRONG: Inconsistent scoping - some rules use .md-typeset, some don't */
.md-typeset table {
  width: 100%;
}
.md-typeset th {
  background: var(--bg-secondary);
}
td {
  /* Missing .md-typeset prefix! */
  padding: 0.5rem;
}

/* ✅ CORRECT: All related rules use same scoping */
.md-typeset table {
  width: 100%;
}
.md-typeset th {
  background: var(--bg-secondary);
}
.md-typeset td {
  padding: 0.5rem;
}
```

**Why this matters:**

1. **Specificity consistency** — All rules have same weight, predictable cascade
2. **Scope isolation** — Styles only affect intended context, don't leak
3. **Maintainability** — Easy to identify which rules belong together

**Rule**: If one rule in a group uses a parent scope (`.md-typeset`, `.markdown-body`, etc.), ALL related rules must use the same scope.

---
