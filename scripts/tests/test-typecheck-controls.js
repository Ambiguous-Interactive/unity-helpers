#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Self-test for scripts/typecheck-controls.js.
//
// That script exists because a gate which reports by failing cannot be trusted until it has been
// seen to fail. The same is true of the script itself: it decides the verdict from a build's text
// output, and a verdict function that stopped discriminating would report every gate as healthy.
// So the decision is a pure function here, driven over synthetic build output, and the parts that
// have to match the repository -- the anchors, the property every check project must declare --
// are asserted against the tracked files rather than remembered.
//
// The full run compiles four projects twice each and takes minutes. Nothing here spawns a build.

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..", "..");
const {
  CHECK_PROJECTS,
  CONTROLS,
  analyzerControl,
  classify,
  compilerControl,
  diagnosticsIn
} = require(path.join(repoRoot, "scripts", "typecheck-controls.js"));

let passed = 0;
let failed = 0;
const failedTests = [];

function runTest(name, fn) {
  try {
    fn();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (err) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${err.message}`);
    failed++;
    failedTests.push(name);
  }
}

const project = CHECK_PROJECTS[0];
const analyzers = CONTROLS.find((control) => control.id === "analyzers");
const compiler = CONTROLS.find((control) => control.id === "compiler");

/** A build that reported exactly `ids`, as MSBuild renders them, and failed. */
function attempt(ids, exitCode = 1) {
  const lines = ids.map((id) => `Control.cs(9,20): error ${id}: something the gate must name.`);
  return { exitCode, output: `${lines.join("\n")}\n` };
}

console.log("Testing scripts/typecheck-controls.js...\n");

runTest("a build that reports exactly the expected diagnostics is a pass", () => {
  assert.equal(
    classify(project, analyzers, attempt(["WPROTO001", "WUH003"])),
    null,
    "the analyzers control fired and nothing else did"
  );
  assert.equal(
    classify(project, compiler, attempt(["CS0246"])),
    null,
    "the compiler control fired and nothing else did"
  );
});

runTest("a build that succeeds with a control defect in it is the finding", () => {
  const verdict = classify(project, analyzers, { exitCode: 0, output: "Build succeeded.\n" });
  assert.match(
    verdict ?? "",
    /the build SUCCEEDED with a control defect in it/,
    "a gate that compiles a known defect clean is exactly what this script exists to catch"
  );
});

runTest("a missing diagnostic names the one that did not fire", () => {
  // The measured case: a compiler error in the compilation silences the WUH### analyzer, so this
  // is what a combined control would print on every run.
  const verdict = classify(project, analyzers, attempt(["WPROTO001"]));
  assert.match(verdict ?? "", /never reported WUH003/, "the missing id must be named");
  assert.match(verdict ?? "", /Reported: WPROTO001\./, "what did fire must be named beside it");
});

runTest("a diagnostic that fired but reported nothing at all still fails", () => {
  const verdict = classify(project, compiler, { exitCode: 1, output: "Build FAILED.\n" });
  assert.match(verdict ?? "", /Reported: nothing\./, "an empty report must say so");
});

runTest("an extra diagnostic is reported as a tree that does not type-check", () => {
  const verdict = classify(project, analyzers, attempt(["WPROTO001", "WUH003", "CS0234"]));
  assert.match(verdict ?? "", /also reported CS0234/, "the unexpected id must be named");
  assert.match(
    verdict ?? "",
    /anchor .* no longer resolves/,
    "the message must offer the anchor as the other explanation, because that is the likelier one"
  );
});

runTest("diagnosticsIn reads every family the check projects can emit, and nothing else", () => {
  assert.deepEqual(
    [...diagnosticsIn("error CS0246: x\nwarning WUH003: y\nerror WPROTO001: z\n")].sort(),
    ["CS0246", "WPROTO001", "WUH003"],
    "all three families must be recognized regardless of severity"
  );
  assert.deepEqual(
    [...diagnosticsIn("Restored /x.csproj (in 1.2 sec).\nBuild succeeded.\n")],
    [],
    "ordinary build chatter must not read as a diagnostic"
  );
});

runTest("every control body carries its anchor, so an empty source tree cannot pass", () => {
  assert.ok(0 < CONTROLS.length, "the control table must not be empty");
  for (const checkProject of CHECK_PROJECTS) {
    for (const control of CONTROLS) {
      const source = control.render(checkProject.anchor);
      assert.ok(
        source.includes(`typeof(${checkProject.anchor})`),
        `${checkProject.id} ${control.id}: the control must name the anchor`
      );
    }
  }
  /*
      The anchor must be bound BEFORE the unresolvable name is reached. As a signature element the
      unresolvable name is a declaration error, and Roslyn stops there without ever evaluating the
      anchor -- measured, and it left the anchor inert. Asserting the anchor's `typeof` precedes the
      unresolvable name says that, where matching one exact spelling of the method signature does
      not survive a rename or a csharpier rewrap.
  */
  const compilerBody = compilerControl("Some.Anchor");
  assert.ok(
    compilerBody.indexOf("typeof(Some.Anchor)") <
      compilerBody.indexOf("ControlTypeThisGateMustNotResolve"),
    "the anchor must bind before the control's unresolvable name is reached"
  );
  assert.ok(
    analyzerControl("X").includes("target?.name"),
    "the analyzers control must keep the null-propagation WUH003 fires on"
  );
});

runTest("every check project exists, declares the control hook, and has a live anchor", () => {
  assert.ok(0 < CHECK_PROJECTS.length, "the project table must not be empty");
  for (const checkProject of CHECK_PROJECTS) {
    const projectPath = path.join(repoRoot, checkProject.project);
    assert.ok(fs.existsSync(projectPath), `${checkProject.id}: ${checkProject.project} is missing`);
    const body = fs.readFileSync(projectPath, "utf8");
    assert.match(
      body,
      /Condition="'\$\(WallstopCheckControl\)' != ''"[\s\S]*?<Compile Include="\$\(WallstopCheckControl\)" \/>/,
      `${checkProject.id}: the project must compile the control the runner injects, and only when ` +
        `the property is set`
    );
    const anchorPath = path.join(repoRoot, checkProject.anchorFile);
    assert.ok(
      fs.existsSync(anchorPath),
      `${checkProject.id}: the anchor file ${checkProject.anchorFile} is gone`
    );
    const anchorType = checkProject.anchor.slice(checkProject.anchor.lastIndexOf(".") + 1);
    assert.match(
      fs.readFileSync(anchorPath, "utf8"),
      new RegExp(`(?:class|struct|interface)\\s+${anchorType}\\b`),
      `${checkProject.id}: ${checkProject.anchorFile} no longer declares ${anchorType}`
    );
  }
});

console.log("");
console.log(`Tests passed: ${passed}`);
console.log(`Tests failed: ${failed}`);
if (failedTests.length > 0) {
  console.log("Failed tests:");
  for (const name of failedTests) {
    console.log(`  - ${name}`);
  }
}

process.exit(failed === 0 ? 0 : 1);
