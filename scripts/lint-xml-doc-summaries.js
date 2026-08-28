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
 * Nothing catches this. The C# compiler validates XML doc structure only under `/doc`, which Unity
 * does not pass and neither type-check project enables, so a duplicated or orphaned summary
 * compiles clean forever and the wrong sentence sits above a public API until a reader trips on it.
 *
 * The rule is deliberately the narrowest one that finds the defect: a run of consecutive `///`
 * lines is one block, and a block may open `<summary>` at most once. Two adjacent members' docs
 * cannot merge into one block, because a member declaration always separates them.
 *
 * `<summary>` inside a `<code>` sample is escaped as `&lt;summary&gt;` and does not match, and a
 * line that both opens and closes on itself counts once, like any other.
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

const SUMMARY_OPEN = /<summary(\s[^>]*)?>/g;

/**
 * Reports every doc-comment block in one file that opens `<summary>` more than once.
 *
 * @param {string} source File text.
 * @returns {{line: number, count: number, first: string}[]} One entry per offending block.
 */
function analyzeFile(source) {
  const lines = source.split(/\r?\n/);
  const violations = [];
  let blockStart = -1;
  let count = 0;
  let firstSummaryLine = "";

  const closeBlock = () => {
    if (1 < count) {
      violations.push({
        line: blockStart + 1,
        count,
        first: firstSummaryLine
      });
    }
    blockStart = -1;
    count = 0;
    firstSummaryLine = "";
  };

  for (let index = 0; index < lines.length; index++) {
    const trimmed = lines[index].trim();
    if (trimmed.startsWith("///")) {
      if (blockStart < 0) {
        blockStart = index;
      }

      SUMMARY_OPEN.lastIndex = 0;
      if (SUMMARY_OPEN.test(trimmed)) {
        count++;
        if (count === 1) {
          firstSummaryLine = trimmed;
        }
      }

      continue;
    }

    if (0 <= blockStart) {
      closeBlock();
    }
  }

  closeBlock();
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

  if (verbose) {
    console.log(`[xml-doc-summaries] ${files.length} file(s) clean.`);
  }
}

module.exports = { analyzeFile };

if (require.main === module) {
  main();
}
