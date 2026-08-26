#!/usr/bin/env node
// Contract tests for scripts/random-quality/evaluate-testu01.mjs.
//
// The red half is the recorded control's real summary; the green half is a real clean summary and a
// real marginal one. If the marginal case ever starts reading as a failure this workflow goes
// permanently red, and if the control ever stops reading as one the battery is decorative.

import assert from "node:assert";
import { DECISIVE, extremities, extremity, verdict } from "../random-quality/evaluate-testu01.mjs";

let passed = 0;
let failed = 0;
const failures = [];

function runTest(name, body) {
  try {
    body();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (error) {
    console.log(`  [FAIL] ${name}\n         ${error.message}`);
    failed++;
    failures.push(name);
  }
}

// Verbatim from a real run: XorShiftRandom, SmallCrush, seed 00010203-...
const CONTROL = `========= Summary results of SmallCrush =========

 Version:          TestU01 1.2.3
 Generator:        wallstop-stream
 Number of statistics:  15
 Total CPU time:   00:00:06.33
 The following tests gave p-values outside [0.001, 0.9990]:
 (eps  means a value < 1.0e-300):
 (eps1 means a value < 1.0e-15):

       Test                          p-value
 ----------------------------------------------
  1  BirthdaySpacings                 eps
  2  Collision                      1 - eps1
  6  MaxOft                         5.6e-16
  8  MatrixRank                       eps
 10  RandomWalk1 H                  1.4e-11
 10  RandomWalk1 M                   1.8e-5
 ----------------------------------------------
 All other tests were passed
`;

// Verbatim from a real run: IllusionFlow at the manifest seed. One statistic outside the interval
// at 7.2e-4, which two other seeds did not reproduce.
const MARGINAL = `========= Summary results of SmallCrush =========

 Number of statistics:  15
 The following tests gave p-values outside [0.001, 0.9990]:

       Test                          p-value
 ----------------------------------------------
  3  Gap                             7.2e-4
 ----------------------------------------------
 All other tests were passed
`;

const CLEAN = `========= Summary results of SmallCrush =========

 Number of statistics:  15
 Total CPU time:   00:00:06.21

 All tests were passed
`;

runTest("the recorded control reads as a failure", () => {
  const result = verdict(CONTROL);
  assert.ok(result.ranBattery);
  assert.ok(result.failed, "XorShiftRandom must fail, or the battery is decorative");
  const names = result.decisive.map((row) => row.test);
  assert.deepStrictEqual(names, [
    "BirthdaySpacings",
    "Collision",
    "MaxOft",
    "MatrixRank",
    "RandomWalk1 H"
  ]);
  assert.deepStrictEqual(
    result.marginal.map((row) => row.test),
    ["RandomWalk1 M"],
    "1.8e-5 is the one row in the control that is not decisive"
  );
});

runTest("a marginal p-value does not", () => {
  const result = verdict(MARGINAL);
  assert.ok(result.ranBattery);
  assert.strictEqual(result.failed, false, "7.2e-4 in 15 statistics is noise, not a regression");
  assert.strictEqual(result.marginal.length, 1);
  assert.strictEqual(result.marginal[0].test, "Gap");
});

runTest("RandomWalk1 H at 1.4e-11 is decisive and 1.8e-5 is not", () => {
  const rows = extremities(CONTROL).filter((row) => row.test.startsWith("RandomWalk1"));
  assert.strictEqual(rows.length, 2);
  assert.ok(rows[0].extremity < DECISIVE, "1.4e-11 must be decisive");
  assert.ok(DECISIVE <= rows[1].extremity, "1.8e-5 must not be");
});

runTest("a clean summary is a pass that actually ran", () => {
  const result = verdict(CLEAN);
  assert.strictEqual(result.ranBattery, true);
  assert.strictEqual(result.failed, false);
  assert.strictEqual(result.decisive.length, 0);
});

runTest("a report with no summary is not a pass", () => {
  for (const empty of ["", null, undefined, "wallstop-testu01: input stream exhausted after 12"]) {
    const result = verdict(empty);
    assert.strictEqual(result.ranBattery, false, `expected a harness fault for: ${empty}`);
  }
});

runTest("a decisive failure at the TOP of the interval is not dropped", () => {
  // TestU01 prints these as `1 - <value>`. Reading only the `1 - eps` spellings drops every
  // high-side NUMERIC row, and a dropped row is indistinguishable from a passing one.
  const report = `
       Test                          p-value
 ----------------------------------------------
  4  Something                      1 - 1.4e-11
 ----------------------------------------------
`;
  const result = verdict("The following tests gave p-values outside [0.001, 0.9990]:" + report);
  assert.strictEqual(result.failed, true, "1 - 1.4e-11 is as decisive as 1.4e-11");
  assert.deepStrictEqual(
    result.decisive.map((row) => row.raw),
    ["1 - 1.4e-11"]
  );
});

runTest("a marginal at the top of the interval is still marginal", () => {
  assert.strictEqual(extremity("1 - 7.2e-4"), 7.2e-4);
  assert.ok(DECISIVE <= extremity("1 - 7.2e-4"));
});

runTest("text that is not a p-value is refused rather than read as zero", () => {
  for (const raw of ["nonsense", "", "1 - nonsense", "-0.5", "2.0"]) {
    assert.strictEqual(extremity(raw), null, `expected null for ${JSON.stringify(raw)}`);
  }
});

runTest("both ends of the interval count", () => {
  assert.strictEqual(
    extremities("  1  Whatever                       1 - eps1")[0].extremity,
    1e-16
  );
  // 1 - 0.9999 is not exactly 1e-4 in binary floating point, and the threshold comparison does not
  // care; the assertion should not pretend otherwise.
  assert.ok(
    Math.abs(extremities("  1  Whatever                       0.9999")[0].extremity - 1e-4) < 1e-12
  );
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
