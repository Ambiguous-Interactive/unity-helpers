#!/usr/bin/env node
// Contract tests for scripts/random-quality/evaluate-testu01.mjs.
//
// The red half is the recorded control's real summary; the green half is a real clean summary and a
// real marginal one. If the marginal case ever starts reading as a failure this workflow goes
// permanently red, and if the control ever stops reading as one the battery is decorative.

import assert from "node:assert";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
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

// WDoomRandom summary with trailing spaces removed: TestU01 omits the footer with 14 or 15 listed statistics.
const ALL_FAILING = `========= Summary results of SmallCrush =========

 Version:          TestU01 1.2.3
 Generator:        wallstop-stream
 Number of statistics:  15
 Total CPU time:   00:00:05.05
 The following tests gave p-values outside [0.001, 0.9990]:
 (eps  means a value < 1.0e-300):
 (eps1 means a value < 1.0e-15):

       Test                          p-value
 ----------------------------------------------
  1  BirthdaySpacings                 eps
  2  Collision                        eps
  3  Gap                              eps
  4  SimpPoker                        eps
  5  CouponCollector                  eps
  6  MaxOft                           eps
  6  MaxOft AD                      1 - eps1
  7  WeightDistrib                    eps
  8  MatrixRank                       eps
  9  HammingIndep                     eps
 10  RandomWalk1 H                    eps
 10  RandomWalk1 M                    eps
 10  RandomWalk1 J                    eps
 10  RandomWalk1 R                    eps
 10  RandomWalk1 C                    eps
 ----------------------------------------------
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
  const result = verdict(
    "========= Summary results of SmallCrush =========\n Number of statistics: 15\n" +
      "The following tests gave p-values outside [0.001, 0.9990]:" +
      report +
      " All other tests were passed\n"
  );
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

runTest("malformed or unfinished summaries cannot pass", () => {
  const malformed = [
    "All tests were passed",
    CLEAN.replace("Number of statistics:  15", "Number of statistics:  0"),
    CLEAN.replace("Number of statistics:  15", ""),
    CONTROL.replace("All other tests were passed", ""),
    CONTROL.replace(
      "MaxOft                         5.6e-16",
      "MaxOft                         nonsense"
    ),
    CONTROL.replace("5.6e-16", "2"),
    CONTROL.replace("5.6e-16", "1e999"),
    CONTROL.replace("5.6e-16", "0x0"),
    CONTROL.replace("5.6e-16", "1 - 2"),
    CONTROL.replace("p-values outside", "p-valuesdriver diagnostic\n outside"),
    CONTROL.replace("RandomWalk1 M                   1.8e-5", "RandomWalk1 M"),
    CONTROL.replace("All other tests were passed", "All tests were passed"),
    CLEAN + CONTROL,
    CLEAN + "wallstop-testu01: input stream exhausted after 12 words\n",
    CLEAN + "unfinished trailing output\n"
  ];
  for (const report of malformed) assert.strictEqual(verdict(report).ranBattery, false, report);
});

runTest("completed nearly-all-failing tables need no optional footer", () => {
  const nearly = ALL_FAILING.replace(/^.*MatrixRank.*\n/m, "");
  for (const [report, count] of [
    [ALL_FAILING, 15],
    [nearly, 14]
  ]) {
    const result = verdict(report);
    assert.strictEqual(result.ranBattery, true);
    assert.strictEqual(result.failed, true);
    assert.strictEqual(result.decisive.length, count);
  }
  assert.strictEqual(verdict(nearly.replace(/^.*HammingIndep.*\n/m, "")).ranBattery, false);
  assert.strictEqual(verdict(ALL_FAILING.trim().replace(/-+$/, "")).ranBattery, false);
});

runTest("complement probabilities use the nearest endpoint", () => {
  assert.ok(Math.abs(extremity("1 - 0.9999") - 1e-4) < 1e-12);
  assert.strictEqual(extremity("0x0"), null);
});

runTest("cached-driver self-test separates streams and refuses invalid evidence", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "testu01-control-"));
  const source = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../random-quality");
  const scriptDirectory = path.join(directory, "scripts/random-quality");
  const build = path.join(directory, "build");
  const host = path.join(
    directory,
    "Generator~/WallstopStudios.UnityHelpers.RandomQuality/bin/Release/net9.0/WallstopStudios.UnityHelpers.RandomQuality"
  );
  const quote = (value) => "'" + value.replace(/'/g, "'\\''") + "'";
  try {
    fs.mkdirSync(scriptDirectory, { recursive: true });
    fs.mkdirSync(build);
    fs.mkdirSync(path.dirname(host), { recursive: true });
    for (const name of ["build-testu01.sh", "evaluate-testu01.mjs"])
      fs.copyFileSync(path.join(source, name), path.join(scriptDirectory, name));
    fs.writeFileSync(host, "#!/usr/bin/env bash\nexit 0\n", { mode: 0o755 });
    const split = CONTROL.indexOf("p-values outside") + "p-values".length;
    const cases = [
      {
        name: "interleaved decisive control",
        first: CONTROL.slice(0, split),
        last: CONTROL.slice(split),
        diagnostic: "driver diagnostic\n",
        status: 0,
        success: true
      },
      { name: "clean control", first: CLEAN, last: "", diagnostic: "", status: 0, success: false },
      {
        name: "marginal control",
        first: MARGINAL,
        last: "",
        diagnostic: "",
        status: 0,
        success: false
      },
      { name: "empty report", first: "", last: "", diagnostic: "", status: 0, success: false },
      {
        name: "failed driver with valid report",
        first: CONTROL,
        last: "",
        diagnostic: "driver failure\n",
        status: 2,
        success: false
      },
      {
        name: "exhausted stream",
        first: CONTROL,
        last: "",
        diagnostic: "input stream exhausted\n",
        status: 3,
        success: false
      }
    ];
    for (const fixture of cases) {
      const driver = `#!/usr/bin/env bash\nprintf '%s' ${quote(fixture.first)}\nprintf '%s' ${quote(fixture.diagnostic)} >&2\nprintf '%s' ${quote(fixture.last)}\nexit ${fixture.status}\n`;
      fs.writeFileSync(path.join(build, "wallstop-testu01"), driver, { mode: 0o755 });
      const result = spawnSync("bash", [path.join(scriptDirectory, "build-testu01.sh")], {
        encoding: "utf8",
        timeout: 30000,
        env: {
          ...process.env,
          TESTU01_BUILD_ROOT: build,
          TESTU01_CONTROL_REPORT_DIRECTORY: path.join(build, "self-test")
        }
      });
      assert.ifError(result.error);
      assert.strictEqual(
        result.status === 0,
        fixture.success,
        fixture.name + "\n" + result.stdout + result.stderr
      );
      assert.strictEqual(
        fs.readFileSync(path.join(build, "self-test/report.txt"), "utf8"),
        fixture.first + fixture.last
      );
      assert.strictEqual(
        fs.readFileSync(path.join(build, "self-test/driver.stderr.log"), "utf8"),
        fixture.diagnostic
      );
    }
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
