#!/usr/bin/env node
/**
 * One member carries one `<summary>`.
 *
 * A doc comment block holding two `<summary>` tags is a member's documentation that outlived the
 * member. Fourteen were found across four files: ten in ReflectionHelpers where the stale half
 * merely repeated the live one, and four where it described a DIFFERENT method entirely --
 * `NestedCollectionAnalyzer.FullMetadataName` was documented as "whether Unity will inline this
 * type's own fields", which belongs to the predicate three members below it.
 *
 * Nothing catches this. `GenerateDocumentationFile` -- which Unity does not set, and which the
 * TypeCheck and EditorCheck projects turned on for `Runtime/**` and `Editor/**` in #591 and #594 --
 * validates CREF RESOLUTION, not summary arity: two `<summary>` tags in one block are well-formed
 * XML that the compiler emits without complaint. So a duplicated or orphaned summary still compiles
 * clean forever, and the wrong sentence sits above a public API until a reader trips on it.
 *
 * The rule is the narrowest one that finds the defect: one member's doc block may open
 * `<summary>` at most once. Two adjacent members' docs cannot merge into one block, because a
 * member declaration always separates them.
 *
 * Four things decide the block boundary, and each one was a hole an adversarial review found:
 *
 *   * A `///` run continues across ATTRIBUTE and PREPROCESSOR lines. A stale summary parked above
 *     an `[Obsolete]` was invisible to the first version -- the exact defect class this exists for.
 *   * `///` inside a block comment or a `@"..."` verbatim string is not a doc comment.
 *     Both were reported as violations, which would have deleted correct documentation.
 *   * `<summary>` inside a `<code>` sample is a sample, escaped or not.
 *   * Lines split on CR as well as CRLF and LF. A CR-only file was one line, and every violation
 *     in it was invisible.
 *
 * A self-closing `<summary/>` opens nothing and is not counted, spelled with a space or without.
 *
 * Exit codes: 0 = clean, 1 = at least one block carries more than one summary.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.XML_DOC_SUMMARY_ROOTS
  ? process.env.XML_DOC_SUMMARY_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

// Vendored upstream verbatim; `lint:comparison-direction` excludes it for the same reason.
const EXCLUDED_PREFIXES = ["Runtime/Utils/SevenZip"];

const SKIPPED_DIRECTORIES = new Set(["bin", "obj", "node_modules", ".git"]);

// An opening tag only: `<summary/>` and `<summary />` close themselves and introduce nothing.
const SUMMARY_OPEN = /<summary(?:\s[^>]*?)?(?<!\/)>/g;
const CODE_OPEN = /<code(?:\s[^>]*?)?(?<!\/)>/i;
const CODE_CLOSE = /<\/code\s*>/i;

/**
 * Replaces block-comment and verbatim-string content with spaces so a `///` inside either is not
 * mistaken for a doc comment. A `//` comment ends the scan with its text intact, because that text
 * is the thing being looked for.
 *
 * @param {string} line One source line.
 * @param {{inBlockComment: boolean, inVerbatimString: boolean}} state Carried across lines.
 * @returns {string} The line with masked spans blanked.
 */
function maskNoise(line, state) {
  let masked = "";
  let index = 0;
  while (index < line.length) {
    const character = line[index];
    if (state.inBlockComment) {
      if (character === "*" && line[index + 1] === "/") {
        state.inBlockComment = false;
        masked += "  ";
        index += 2;
        continue;
      }
      masked += " ";
      index++;
      continue;
    }

    if (state.inVerbatimString) {
      if (character === '"') {
        if (line[index + 1] === '"') {
          masked += "  ";
          index += 2;
          continue;
        }
        state.inVerbatimString = false;
      }
      masked += " ";
      index++;
      continue;
    }

    if (character === "/" && line[index + 1] === "/") {
      masked += line.slice(index);
      return masked;
    }

    if (character === "/" && line[index + 1] === "*") {
      state.inBlockComment = true;
      masked += "  ";
      index += 2;
      continue;
    }

    if (character === "@" && line[index + 1] === '"') {
      state.inVerbatimString = true;
      masked += "  ";
      index += 2;
      continue;
    }

    if (character === '"' || character === "'") {
      const quote = character;
      masked += " ";
      index++;
      while (index < line.length) {
        if (line[index] === "\\") {
          masked += "  ";
          index += 2;
          continue;
        }
        const closing = line[index] === quote;
        masked += " ";
        index++;
        if (closing) {
          break;
        }
      }
      continue;
    }

    masked += character;
    index++;
  }

  return masked;
}

/**
 * Reports every doc-comment block in one file that opens `<summary>` more than once.
 *
 * @param {string} source File text.
 * @returns {{line: number, count: number, first: string}[]} One entry per offending block.
 */
function analyzeFile(source) {
  const lines = source.split(/\r\n|\r|\n/);
  const state = { inBlockComment: false, inVerbatimString: false };
  const kinds = lines.map((line) => {
    const masked = maskNoise(line, state).trim();
    if (masked.startsWith("///")) {
      return { kind: "doc", text: masked };
    }
    if (masked === "") {
      return { kind: "blank", text: masked };
    }
    // An attribute or a preprocessor directive sits INSIDE one member's documentation; it does not
    // end it. A brace, a keyword or anything else does.
    if (masked.startsWith("[") || masked.startsWith("#")) {
      return { kind: "interior", text: masked };
    }
    return { kind: "code", text: masked };
  });

  const violations = [];
  let index = 0;
  while (index < lines.length) {
    if (kinds[index].kind !== "doc") {
      index++;
      continue;
    }

    const blockStart = index;
    let count = 0;
    let firstSummaryLine = "";
    let inCodeSample = false;
    let cursor = index;
    let lastDocLine = index;
    while (cursor < lines.length) {
      const entry = kinds[cursor];
      if (entry.kind === "doc") {
        lastDocLine = cursor;
        if (!inCodeSample) {
          const matches = entry.text.match(SUMMARY_OPEN);
          if (matches) {
            if (count === 0) {
              firstSummaryLine = entry.text;
            }
            count += matches.length;
          }
        }
        if (CODE_OPEN.test(entry.text)) {
          inCodeSample = true;
        }
        if (CODE_CLOSE.test(entry.text)) {
          inCodeSample = false;
        }
        cursor++;
        continue;
      }

      if (entry.kind === "interior") {
        cursor++;
        continue;
      }

      break;
    }

    if (1 < count) {
      violations.push({ line: blockStart + 1, count, first: firstSummaryLine });
    }

    index = lastDocLine + 1;
  }

  return violations;
}

function isExcluded(relativePath) {
  const normalized = relativePath.split(path.sep).join("/");
  return EXCLUDED_PREFIXES.some((prefix) => normalized.startsWith(prefix));
}

function collectFiles(root, collected) {
  const absolute = path.isAbsolute(root) ? root : path.join(REPO_ROOT, root);
  if (!fs.existsSync(absolute)) {
    return collected;
  }

  const entries = fs.readdirSync(absolute, { withFileTypes: true });
  for (const entry of entries) {
    const entryPath = path.join(absolute, entry.name);
    if (entry.isDirectory()) {
      if (!SKIPPED_DIRECTORIES.has(entry.name)) {
        collectFiles(entryPath, collected);
      }

      continue;
    }

    if (entry.name.endsWith(".cs")) {
      collected.push(entryPath);
    }
  }

  return collected;
}

function main() {
  const verbose = process.argv.includes("--verbose");
  const files = [];
  for (const root of SCAN_ROOTS) {
    collectFiles(root, files);
  }

  let offending = 0;
  const reports = [];
  for (const file of files) {
    const relative = path.relative(REPO_ROOT, file).split(path.sep).join("/");
    if (isExcluded(relative)) {
      continue;
    }

    const violations = analyzeFile(fs.readFileSync(file, "utf8"));
    for (const violation of violations) {
      offending++;
      reports.push(
        `  ${relative}:${violation.line}: doc block opens <summary> ${violation.count} times; ` +
          `the first is "${violation.first}"`
      );
    }
  }

  if (0 < offending) {
    console.error(
      `[xml-doc-summaries] ${offending} doc comment block(s) carry more than one <summary>. ` +
        "Delete the stale one, or move it onto the member it actually documents."
    );
    for (const report of reports) {
      console.error(report);
    }

    process.exitCode = 1;
    return;
  }

  if (files.length === 0) {
    // A walk that matched nothing is the absence of a measurement rather than a pass: a renamed
    // source root, a moved tree, or a walk that stopped descending all reach this line otherwise
    // (#556). Reported whatever the verbosity, because a silent zero is the whole defect.
    console.error(
      `[xml-doc-summaries] no C# files were found under ${SCAN_ROOTS.join(", ")}, so this run ` +
        `checked nothing.`
    );
    process.exitCode = 1;
    return;
  }

  if (verbose) {
    console.log(`[xml-doc-summaries] ${files.length} file(s) clean.`);
  }
}

module.exports = { analyzeFile, maskNoise };

if (require.main === module) {
  main();
}
