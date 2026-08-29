#!/usr/bin/env node
/**
 * Generates `docs/readme.md` from `README.md`.
 *
 * The two files are one document. `README.md` is what GitHub renders at the repository root and is
 * the source of truth; `docs/readme.md` is the copy MkDocs publishes, is named in `mkdocs.yml`'s
 * `not_in_nav`, and is the target of nine `../readme.md#anchor` links from
 * `docs/overview/index.md`. Before this script they were maintained by hand and had drifted 138
 * lines apart in BOTH directions (#593) -- each copy carried edits the other never received.
 *
 * They cannot simply be a copy, which is why the drift was tolerated for so long: five kinds of
 * reference mean something different from inside `docs/` than from the repository root, and one of
 * them aborts `mkdocs build --strict`. Every rewrite below exists because of a measured failure,
 * and NOTHING else is rewritten -- prose, code blocks and tables are copied byte for byte.
 *
 *   1. Links into the docs tree lose their `docs/` segment. `./docs/overview/getting-started.md`
 *      is correct from the root and resolves to `docs/docs/overview/...` from inside `docs/`.
 *
 *   2. References that leave the docs tree become absolute GitHub blob URLs. `mkdocs build
 *      --strict` aborts on a relative link with no page behind it, and `CHANGELOG.md`, `llms.txt`
 *      and everything under `Samples~/` are outside the tree MkDocs copies. This is the rewrite
 *      the strict build is the only local check for: `lint:docs` and `lint:markdown` both pass a
 *      link that walks out of `docs/`.
 *
 *   3. `#anchor` fragments into ANOTHER docs page are re-slugged. GitHub and Python-Markdown's
 *      `toc` disagree about headings containing anything that is neither a letter, a digit nor a
 *      space: GitHub deletes the character and keeps the space beside it as a separator, so
 *      `### Benchmarking & Verification` is `#benchmarking--verification` there, while `toc`
 *      collapses the run and yields `#benchmarking-verification`. Six links in the front page's
 *      own "choose your starting point" table were dead on the site for exactly this reason (see
 *      the `validation.links.anchors` comment in `mkdocs.yml`). Fragments are mapped through the
 *      TARGET document's headings, so this stays correct when a heading is renamed rather than
 *      encoding today's answers as a table.
 *
 *      A fragment into THIS document is deliberately NOT rewritten, and a heading whose two slugs
 *      disagree is reported instead. Markdownlint's MD051 validates a document's own fragments
 *      with GitHub's slugger and does not follow cross-file ones, so a mirror carrying published
 *      slugs for its own headings generates correctly and then fails `lint:markdown` -- there is
 *      no rewrite that satisfies both renderers. The fix belongs in the heading: keep a linked
 *      heading to letters, digits and spaces and both sluggers agree, which is what the site copy
 *      had arrived at by hand before this script existed.
 *
 * Anchors in a link that became a GitHub URL (rewrite 2) keep their GitHub slug, because that is
 * the renderer they now point at.
 *
 * The result is run through Prettier before it is written, because Prettier owns Markdown table
 * alignment and rewrite 3 changes a cell's width: a raw mirror is generated correctly and then
 * fails `format:md:check`, and re-aligning by hand puts the file back out of sync on the next
 * run. Generating the canonical form is the only shape in which both gates can hold at once.
 *
 * `--check` compares instead of writing and is what `lint-readme-mirror.js` runs in Repo Lint.
 *
 * Exit codes: 0 = mirror written (or already current), 1 = drift in `--check`, or a rewrite that
 * could not be made safely.
 */

"use strict";

const fs = require("fs");
const path = require("path");

// A devDependency, and every consumer of this script (Repo Lint, agent preflight, the
// self-test) already runs from an installed tree.
const prettier = require("prettier");

// Overridable so the self-test can point the generator at a fixture tree and make it REPORT
// (#556). Nothing in CI sets it.
const REPO_ROOT = path.resolve(process.env.README_MIRROR_ROOT || path.join(__dirname, ".."));
const SOURCE_FILE = "README.md";
const MIRROR_FILE = "docs/readme.md";
const DOCS_PREFIX = "docs/";
const BLOB_BASE = "https://github.com/wallstop/unity-helpers/blob/main/";
const FIX_COMMAND = "npm run sync:readme-mirror";

const BANNER =
  `<!-- Generated from ../${SOURCE_FILE} by scripts/sync-readme-mirror.js -- ` +
  `edit that file and run \`${FIX_COMMAND}\`. -->`;

/**
 * GitHub's heading slug (the `github-slugger` algorithm, which GitHub's Markdown renderer uses).
 *
 * Lowercase, then DELETE every character that is not a letter, a digit, `_`, `-` or a space, then
 * turn each remaining space into a hyphen. Deleting rather than replacing is the whole difference
 * from `mkdocsSlug`: `& ` leaves two spaces behind and therefore two hyphens.
 *
 * @param {string} heading Heading text, without the leading `#`s.
 * @returns {string} The fragment GitHub links to.
 */
function githubSlug(heading) {
  return heading
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}_\- ]/gu, "")
    .replace(/ /g, "-");
}

/**
 * Python-Markdown's `toc` slug, which is what MkDocs Material publishes.
 *
 * Mirrors `markdown.extensions.toc.slugify`: NFKD-normalize and drop everything non-ASCII (an
 * emoji leaves nothing behind), delete anything that is not a word character, whitespace or a
 * hyphen, lowercase, then COLLAPSE each run of hyphens and whitespace into a single hyphen.
 *
 * @param {string} heading Heading text, without the leading `#`s.
 * @returns {string} The fragment the published site links to.
 */
function mkdocsSlug(heading) {
  return (
    heading
      .normalize("NFKD")
      // eslint-disable-next-line no-control-regex
      .replace(/[^\x00-\x7F]/g, "")
      .replace(/[^\w\s-]/g, "")
      .trim()
      .toLowerCase()
      .replace(/[-\s]+/g, "-")
  );
}

/**
 * ATX headings of a Markdown document, with fenced code blocks skipped.
 *
 * A `# comment` inside a shell fence is not a heading, and README carries plenty of them.
 *
 * @param {string} text Markdown source.
 * @returns {string[]} Heading texts in document order.
 */
function headingsOf(text) {
  const headings = [];
  let fence = null;
  for (const line of text.split("\n")) {
    const fenceMatch = line.match(/^\s*(```+|~~~+)/);
    if (fenceMatch) {
      const marker = fenceMatch[1][0];
      if (fence === null) {
        fence = marker;
      } else if (fence === marker) {
        fence = null;
      }
      continue;
    }
    if (fence !== null) {
      continue;
    }
    const heading = line.match(/^#{1,6}\s+(.*?)\s*#*\s*$/);
    if (heading) {
      headings.push(heading[1]);
    }
  }
  return headings;
}

/**
 * Maps a document's GitHub fragments onto its published ones.
 *
 * The first heading to claim a GitHub slug wins, which is what both sluggers do before they start
 * appending disambiguating suffixes. A later heading that collides is left out rather than
 * overwriting, so a fragment whose meaning is ambiguous is never rewritten on a guess.
 *
 * @param {string} text Markdown source of the document being linked to.
 * @returns {Map<string, string>} GitHub fragment to published fragment.
 */
function fragmentMap(text) {
  const map = new Map();
  for (const heading of headingsOf(text)) {
    const from = githubSlug(heading);
    if (from && !map.has(from)) {
      map.set(from, mkdocsSlug(heading));
    }
  }
  return map;
}

/**
 * @param {string} target A link target.
 * @returns {boolean} True when it names a scheme, a protocol-relative host, or is a bare fragment.
 */
function isAbsolute(target) {
  return /^(?:[a-z][a-z0-9+.-]*:|\/\/|\/)/i.test(target);
}

/** Splits `path#fragment`, keeping any further `#` inside the fragment. */
function splitFragment(target) {
  const hash = target.indexOf("#");
  return 0 <= hash
    ? { location: target.slice(0, hash), fragment: target.slice(hash + 1) }
    : { location: target, fragment: null };
}

/** Repo-relative, percent-decoded, `./`-stripped form of a link target's path half. */
function repoRelativePath(location) {
  let decoded = location;
  try {
    decoded = decodeURIComponent(location);
  } catch {
    // A literal `%` that is not an escape. The raw text is the best available answer.
  }
  return path.posix.normalize(decoded.replace(/^\.\//, ""));
}

/**
 * Rewrites one link target for the mirror.
 *
 * @param {string} target The target as written in `README.md`.
 * @param {(file: string) => Map<string, string>} fragmentsFor Fragment map for a repo-relative doc.
 * @param {string[]} problems Collects anything that cannot be rewritten safely.
 * @returns {string} The target as it must appear in `docs/readme.md`.
 */
function rewriteTarget(target, fragmentsFor, problems) {
  const { location, fragment } = splitFragment(target);

  // A bare `#fragment` stays in this document and is copied through unchanged -- see rewrite 3.
  // Both renderers have to resolve it, so the heading, not the link, is where a mismatch is fixed.
  if (location === "") {
    const mapped = fragmentsFor(SOURCE_FILE).get(fragment);
    if (mapped === undefined) {
      problems.push(
        `${SOURCE_FILE} links to '#${fragment}', which is not the GitHub slug of any heading in it`
      );
      return target;
    }
    if (mapped !== fragment) {
      problems.push(
        `${SOURCE_FILE} links to '#${fragment}', but that heading publishes as '#${mapped}'. ` +
          "Rename the heading to letters, digits and spaces so both renderers agree: the mirror " +
          "can carry only one of the two, and markdownlint's MD051 checks it with GitHub's slugger"
      );
    }
    return target;
  }

  if (isAbsolute(location)) {
    return target;
  }

  const relative = repoRelativePath(location);

  // Leaves the docs tree: MkDocs has no page to point at, and `--strict` aborts on the dangling
  // link. GitHub can serve it, so the mirror sends the reader there -- with the GitHub fragment,
  // because GitHub is now the renderer resolving it.
  if (!relative.startsWith(DOCS_PREFIX)) {
    if (!fs.existsSync(path.join(REPO_ROOT, relative))) {
      problems.push(`${SOURCE_FILE} links to '${target}', which does not exist in the repository`);
      return target;
    }
    const encoded = location.replace(/^\.\//, "");
    return `${BLOB_BASE}${encoded}${fragment === null ? "" : `#${fragment}`}`;
  }

  // Inside the docs tree: drop the `docs/` segment the mirror already lives under, keeping the
  // author's `./` style so `lint:docs` sees the relative prefix it requires on Markdown links.
  const inner = relative.slice(DOCS_PREFIX.length);
  const prefixed = location.startsWith("./") ? `./${inner}` : inner;
  if (fragment === null) {
    return prefixed;
  }
  if (!relative.endsWith(".md") || !fs.existsSync(path.join(REPO_ROOT, relative))) {
    return `${prefixed}#${fragment}`;
  }
  const mapped = fragmentsFor(relative).get(fragment);
  return `${prefixed}#${mapped === undefined ? fragment : mapped}`;
}

/**
 * Produces the mirror's content from the source's.
 *
 * Only link targets are touched, and only outside fenced code blocks -- a fence is sample code,
 * where a path is an illustration rather than a reference.
 *
 * @param {string} source `README.md` content.
 * @param {(file: string) => string} readDoc Reads a repo-relative file (injected for the self-test).
 * @returns {{content: string, problems: string[]}} The mirror, and whatever could not be rewritten.
 */
function buildMirror(source, readDoc) {
  const problems = [];
  const cache = new Map();
  const fragmentsFor = (file) => {
    if (!cache.has(file)) {
      cache.set(file, fragmentMap(readDoc(file)));
    }
    return cache.get(file);
  };
  const rewrite = (target) => rewriteTarget(target, fragmentsFor, problems);

  let fence = null;
  const lines = source.split("\n").map((line) => {
    const fenceMatch = line.match(/^\s*(```+|~~~+)/);
    if (fenceMatch) {
      const marker = fenceMatch[1][0];
      if (fence === null) {
        fence = marker;
      } else if (fence === marker) {
        fence = null;
      }
      return line;
    }
    if (fence !== null) {
      return line;
    }
    return line
      .replace(/(\]\()([^)\s]+)(\))/g, (all, open, target, close) => open + rewrite(target) + close)
      .replace(
        /\b(src|href)=(["'])([^"']+)\2/g,
        (all, attr, quote, target) => `${attr}=${quote}${rewrite(target)}${quote}`
      );
  });

  return { content: `${BANNER}\n\n${lines.join("\n")}`, problems };
}

/** @returns {Promise<number>} Process exit code. */
async function main(argv) {
  const check = argv.includes("--check");
  const verbose = argv.includes("--verbose");
  const sourcePath = path.join(REPO_ROOT, SOURCE_FILE);
  const mirrorPath = path.join(REPO_ROOT, MIRROR_FILE);

  const source = fs.readFileSync(sourcePath, "utf8").replace(/\r\n/g, "\n");
  const readDoc = (file) =>
    fs.readFileSync(path.join(REPO_ROOT, file), "utf8").replace(/\r\n/g, "\n");
  const { content: raw, problems } = buildMirror(source, readDoc);

  if (0 < problems.length) {
    console.error(`[readme-mirror] ${problems.length} link(s) could not be rewritten:`);
    for (const problem of problems) {
      console.error(`  ${problem}`);
    }
    return 1;
  }

  const prettierOptions = await prettier.resolveConfig(mirrorPath, { editorconfig: false });
  const content = await prettier.format(raw, {
    ...prettierOptions,
    filepath: mirrorPath,
    parser: "markdown"
  });

  const existing = fs.existsSync(mirrorPath)
    ? fs.readFileSync(mirrorPath, "utf8").replace(/\r\n/g, "\n")
    : null;

  if (check) {
    if (existing === content) {
      if (verbose) {
        console.log(`[readme-mirror] ${MIRROR_FILE} matches ${SOURCE_FILE}.`);
      }
      return 0;
    }
    const expected = content.split("\n");
    const actual = (existing === null ? "" : existing).split("\n");
    const drifted = [];
    for (let index = 0; index < Math.max(expected.length, actual.length); index++) {
      if (expected[index] !== actual[index]) {
        drifted.push(index + 1);
      }
    }
    console.error(
      `[readme-mirror] ${MIRROR_FILE} is not what ${SOURCE_FILE} generates ` +
        `(${drifted.length} line(s) differ, first at line ${drifted[0]}).`
    );
    for (const line of drifted.slice(0, 10)) {
      console.error(`  ${MIRROR_FILE}:${line}`);
      console.error(
        `    expected: ${expected[line - 1] === undefined ? "(no line)" : expected[line - 1]}`
      );
      console.error(
        `    actual:   ${actual[line - 1] === undefined ? "(no line)" : actual[line - 1]}`
      );
    }
    if (10 < drifted.length) {
      console.error(`  ... and ${drifted.length - 10} more line(s).`);
    }
    console.error(
      `[readme-mirror] ${MIRROR_FILE} is generated. Put the change in ${SOURCE_FILE}, then run: ` +
        FIX_COMMAND
    );
    return 1;
  }

  if (existing === content) {
    if (verbose) {
      console.log(`[readme-mirror] ${MIRROR_FILE} already current.`);
    }
    return 0;
  }
  fs.writeFileSync(mirrorPath, content);
  console.log(`[readme-mirror] wrote ${MIRROR_FILE} from ${SOURCE_FILE}.`);
  return 0;
}

module.exports = { githubSlug, mkdocsSlug, headingsOf, fragmentMap, buildMirror, main, BANNER };

if (require.main === module) {
  main(process.argv.slice(2)).then(
    (code) => process.exit(code),
    (error) => {
      console.error(`[readme-mirror] ${error && error.stack ? error.stack : error}`);
      process.exit(1);
    }
  );
}
