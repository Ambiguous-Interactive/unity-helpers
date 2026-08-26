#!/usr/bin/env node
// Reads a directory of TestU01 reports and fails the run on a decisive result.
//
// The threshold and its evidence live in evaluate-testu01.mjs. This file decides what to do about
// a verdict, and it has two rules beyond that:
//
//   * A report naming no battery summary at all is a HARNESS fault, never a pass. A directory of
//     empty files would otherwise be the quietest green run this repository could produce (#556).
//   * A generator the manifest already records as weak is EXPECTED to fail, so its failure is
//     evidence the battery works rather than a regression. The inventory contains several, and a
//     reporter that did not read the manifest would be red on every scheduled run.
//
// The converse is deliberately NOT a failure: a weak generator that passes SmallCrush is
// inconclusive, not a harness fault. SmallCrush draws 908 MB where the PractRand tier runs to 8GB,
// so it is the shallower instrument and can miss what the deeper one catches.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { verdict } from "./evaluate-testu01.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));
const directory = process.argv[2];
const manifestPath = process.argv[3] ?? path.join(here, "expected-outcomes.json");

if (!directory || !fs.existsSync(directory)) {
  console.error(`[testu01] No report directory at '${directory}'.`);
  process.exit(1);
}

if (!fs.existsSync(manifestPath)) {
  console.error(
    `[testu01] No manifest at '${manifestPath}'. Refusing to judge without expectations.`
  );
  process.exit(1);
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
const expectation = new Map(
  (manifest.generators ?? []).map((entry) => [entry.name, entry.widths?.["32"]?.expected])
);

const reports = fs
  .readdirSync(directory)
  .filter((name) => name.endsWith(".txt"))
  .sort();

if (reports.length === 0) {
  console.error(
    `[testu01] '${directory}' holds no reports. A scan that looked at nothing is not a pass.`
  );
  process.exit(1);
}

const broken = [];
const failing = [];
const confirmed = [];
const inconclusive = [];
const marginal = [];
const unknown = [];

for (const name of reports) {
  const generator = name.replace(/\.txt$/, "");
  const result = verdict(fs.readFileSync(path.join(directory, name), "utf8"));
  if (!result.ranBattery) {
    broken.push(generator);
    continue;
  }

  const expected = expectation.get(generator);
  if (expected === undefined) {
    unknown.push(generator);
  }
  const rows = result.decisive.map((row) => `${row.test}=${row.raw}`).join(", ");

  if (result.failed) {
    if (expected === "fail") {
      confirmed.push(`${generator}: ${rows}`);
    } else {
      failing.push(`${generator}: ${rows}`);
    }
    continue;
  }
  if (expected === "fail") {
    inconclusive.push(generator);
  }
  if (0 < result.marginal.length) {
    marginal.push(
      `${generator}: ${result.marginal.map((row) => `${row.test}=${row.raw}`).join(", ")}`
    );
  }
}

console.log(`[testu01] ${reports.length} generator(s) measured with SmallCrush.`);
if (0 < confirmed.length) {
  console.log(
    `[testu01] ${confirmed.length} recorded-weak generator(s) failed, as the manifest says they should:`
  );
  for (const line of confirmed) {
    console.log(`    ${line}`);
  }
}
if (0 < inconclusive.length) {
  console.log(
    `[testu01] ${inconclusive.length} recorded-weak generator(s) passed SmallCrush; it is the ` +
      `shallower battery, so this is inconclusive rather than a fault: ${inconclusive.join(", ")}`
  );
}
if (0 < unknown.length) {
  console.error(`::error::[testu01] Not in the manifest: ${unknown.join(", ")}`);
  console.error("[testu01] A generator with no recorded expectation cannot be judged. Add it.");
  process.exit(1);
}
if (0 < marginal.length) {
  // Reported, never failed. SmallCrush runs 15 statistics, so a clean generator lands one row
  // outside [0.001, 0.9990] roughly one run in seven; IllusionFlow did exactly that at the manifest
  // seed and was clean on two others.
  console.log(`[testu01] ${marginal.length} marginal p-value(s), which is expected noise:`);
  for (const line of marginal) {
    console.log(`    ${line}`);
  }
}

if (0 < broken.length) {
  console.error(
    `::error::[testu01] ${broken.length} report(s) contain no battery summary: ${broken.join(", ")}`
  );
  console.error(
    "[testu01] That is a harness fault -- the stream ran out, or the driver died. Not a pass."
  );
  process.exit(1);
}

if (0 < failing.length) {
  console.error(
    `::error::[testu01] ${failing.length} generator(s) produced a decisive TestU01 failure.`
  );
  for (const line of failing) {
    console.error(`    ${line}`);
  }
  console.error(
    "[testu01] A generator whose weakness is already recorded belongs in the manifest;"
  );
  console.error(
    "[testu01] one that is not is a regression. Re-run with another --seed to confirm."
  );
  process.exit(1);
}

console.log("[testu01] No decisive failures.");
