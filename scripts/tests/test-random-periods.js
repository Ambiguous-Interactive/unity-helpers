#!/usr/bin/env node
"use strict";

// Every generator states its period, and the documentation states the same one (#516).
//
// The owner's ask was "a test or function that ensures that we output the period of each generator
// (include in our docs) that is ran whenever docs are refreshed". This is that test. It reads the
// declared period off the type -- one source of truth, next to the algorithm -- and refuses a
// documentation table that says anything else, in either direction.
//
// A period is not measurable for a generator whose state is 2^128 wide, so the value is a claim
// with its provenance attached: a published specification, or the MEASURED live state width when
// nothing is published. That distinction is the point. Four `Excellent` ratings in this roster came
// from repositories that now 404, and an invented period would be the same failure one column over.

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..", "..");
const randomDirectory = path.join(repoRoot, "Runtime", "Core", "Random");
const docPath = path.join(repoRoot, "docs", "features", "utilities", "random-generators.md");
// Below this, the run is measuring nothing: a filter that stops matching reads exactly like a clean
// suite, which is the whole of #556. Twenty ship today; the floor moves only when one is removed on
// purpose.
const MINIMUM_GENERATORS = 20;

let passed = 0;

function check(description, condition, detail) {
  assert.ok(condition, `${description}${detail ? `: ${detail}` : ""}`);
  passed += 1;
  console.log(`  [PASS] ${description}`);
}

/** Declared periods, keyed by generator type name, read from the annotation on the type. */
function declaredPeriods() {
  const declared = new Map();
  const unannotated = [];
  for (const entry of fs.readdirSync(randomDirectory)) {
    if (!entry.endsWith(".cs")) {
      continue;
    }
    const source = fs.readFileSync(path.join(randomDirectory, entry), "utf8");
    if (!/\[RandomGeneratorMetadata\(/.test(source)) {
      continue;
    }
    const name = entry.slice(0, -3);
    // The attribute declaration itself carries the token; only a USE of it declares a period.
    if (name === "RandomGeneratorMetadata") {
      continue;
    }
    // [\s\S] rather than (?:.|\n): V8 gives up on the alternation inside a lazy quantifier at
    // this input size and returns null for an annotation that is plainly there. Measured on
    // PcgRandom.cs -- the same pattern matches a 300-character slice and not the 8 KB file.
    const match = /\[RandomGeneratorMetadata\([\s\S]*?period:\s*"([^"]*)"/.exec(source);
    if (!match || match[1].trim() === "") {
      unannotated.push(name);
      continue;
    }
    declared.set(name, match[1]);
  }
  return { declared, unannotated };
}

/** Generator rows of the documentation table, keyed by generator name. */
function documentedPeriods() {
  const lines = fs.readFileSync(docPath, "utf8").split(/\r?\n/);
  const headerIndex = lines.findIndex(
    (line) => line.startsWith("| Generator ") && line.includes("Period")
  );
  if (headerIndex < 0) {
    return { documented: new Map(), headerFound: false };
  }
  const cells = (line) =>
    line
      .trim()
      .replace(/^\||\|$/g, "")
      .split("|")
      .map((cell) => cell.trim());
  const periodColumn = cells(lines[headerIndex]).indexOf("Period");
  const documented = new Map();
  for (let index = headerIndex + 2; index < lines.length; index += 1) {
    if (!lines[index].startsWith("| `")) {
      break;
    }
    const row = cells(lines[index]);
    documented.set(row[0].replace(/`/g, ""), row[periodColumn]);
  }
  return { documented, headerFound: true };
}

console.log("Testing that every generator declares a period and the docs carry it...\n");

const { declared, unannotated } = declaredPeriods();

check(
  `at least ${MINIMUM_GENERATORS} annotated generators were found`,
  MINIMUM_GENERATORS <= declared.size + unannotated.length,
  `found ${declared.size + unannotated.length}; a scan that stops matching reads like a clean run`
);

check(
  "every annotated generator declares a period",
  unannotated.length === 0,
  `these carry [RandomGeneratorMetadata] with no period: ${unannotated.join(", ")}. ` +
    `Add period: "<published spec>" or period: "unpublished; N/M state bits live (measured)".`
);

const { documented, headerFound } = documentedPeriods();

check(
  "the documentation carries a Period column",
  headerFound,
  `${path.relative(repoRoot, docPath)} has no generator table with a Period column`
);

const missingFromDocs = [...declared.keys()].filter((name) => !documented.has(name)).sort();
check(
  "every generator appears in the documentation table",
  missingFromDocs.length === 0,
  `undocumented: ${missingFromDocs.join(", ")}`
);

const unknownInDocs = [...documented.keys()].filter((name) => !declared.has(name)).sort();
check(
  "the documentation table names no generator that does not exist",
  unknownInDocs.length === 0,
  `documented but not found in Runtime/Core/Random: ${unknownInDocs.join(", ")}`
);

const drifted = [...declared.entries()]
  .filter(([name, period]) => documented.has(name) && documented.get(name) !== period)
  .map(([name, period]) => `${name}: source "${period}" vs docs "${documented.get(name)}"`)
  .sort();
check(
  "every documented period matches the one declared on the type",
  drifted.length === 0,
  drifted.join("; ")
);

console.log(`\n${passed} passed, 0 failed.`);
