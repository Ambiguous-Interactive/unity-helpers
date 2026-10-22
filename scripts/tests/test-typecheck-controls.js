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
// The full run compiles five projects twice each and takes minutes. Nothing here spawns a build.

"use strict";

const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const os = require("node:os");
const yaml = require("yaml");

const repoRoot = path.resolve(__dirname, "..", "..");
const {
  CHECK_PROJECTS,
  CONTROLS,
  analyzerControl,
  classify,
  compilerControl,
  defaultJobs,
  diagnosticsIn,
  isNuGetMigrationStartupRace,
  parseArguments
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
    classify(
      project,
      analyzers,
      attempt(["WPROTO001", "WUH003", "WUH013", "WUH020", "WUH019", "WUH021", "WUH023"])
    ),
    null,
    "the analyzers control fired and nothing else did"
  );
  assert.equal(
    classify(project, compiler, attempt(["CS0246"])),
    null,
    "the compiler control fired and nothing else did"
  );
});

runTest("production opt-in diagnostics apply to production and stay disabled in test gates", () => {
  for (const checkProject of CHECK_PROJECTS) {
    const production = ["runtime", "editor", "integrations"].includes(checkProject.id);
    const expected = ["WPROTO001", "WUH003", "WUH013", "WUH020"].concat(
      production ? ["WUH019", "WUH021", "WUH023"] : []
    );
    assert.equal(classify(checkProject, analyzers, attempt(expected)), null);
    for (const id of ["WUH019", "WUH021", "WUH023"]) {
      const incorrect = production
        ? expected.filter((reported) => reported !== id)
        : expected.concat(id);
      assert.match(classify(checkProject, analyzers, attempt(incorrect)), new RegExp(id));
    }
  }
});

runTest("the excluded-source audit independently requires every enabled diagnostic", () => {
  const excluded = CONTROLS.find((control) => control.id === "excluded-loops");
  const editor = CHECK_PROJECTS.find((entry) => entry.id === "editor");
  const ids = ["WUH013", "WUH019", "WUH021", "WUH023"];
  assert.deepEqual(excluded.expected, ids);
  assert.equal(classify(editor, excluded, attempt(ids)), null);
  for (const id of ids) {
    assert.match(
      classify(editor, excluded, attempt(ids.filter((reported) => reported !== id))),
      new RegExp(id)
    );
  }
  assert.match(
    excluded.render(editor.anchor),
    /Snapshot\(System\.Collections\.Generic\.ICollection<int> values\)/
  );
  assert.match(excluded.render(editor.anchor), /if \(values\.Count > 0\)/);
  assert.match(excluded.render(editor.anchor), /return values\.Count;/);
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

runTest("losing the package counting-loop opt-in fails the control", () => {
  const verdict = classify(project, analyzers, attempt(["WPROTO001", "WUH003"]));
  assert.match(verdict ?? "", /never reported WUH013/);
});

runTest("a diagnostic that fired but reported nothing at all still fails", () => {
  const verdict = classify(project, compiler, { exitCode: 1, output: "Build FAILED.\n" });
  assert.match(verdict ?? "", /Reported: nothing\./, "an empty report must say so");
});

runTest("an extra diagnostic is reported as a tree that does not type-check", () => {
  const verdict = classify(
    project,
    analyzers,
    attempt(["WPROTO001", "WUH003", "WUH013", "WUH020", "WUH019", "WUH021", "WUH023", "CS0234"])
  );
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

runTest("only a NuGet migration startup race without diagnostics is retried", () => {
  const race = {
    exitCode: 1,
    output:
      "System.IO.IOException: 'NuGet-Migrations'. mkdir('/tmp/.dotnet/session2048') errno == EEXIST"
  };
  assert.equal(isNuGetMigrationStartupRace(race), true);
  assert.equal(isNuGetMigrationStartupRace({ ...race, exitCode: 0 }), false);
  assert.equal(
    isNuGetMigrationStartupRace({ ...race, output: race.output + " error CS0246" }),
    false
  );
  assert.equal(isNuGetMigrationStartupRace({ exitCode: 1, output: "Build FAILED." }), false);

  const scriptPath = path.join(repoRoot, "scripts", "typecheck-controls.js");
  const nodeBody = `
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { CHECK_PROJECTS, runProject } = require(${JSON.stringify(scriptPath)});
const directory = fs.mkdtempSync(path.join(os.tmpdir(), "typecheck-control-retry-"));
let calls = 0;
async function build(project, controlPath) {
  calls++;
  if (calls === 1) return ${JSON.stringify(race)};
  const ids = controlPath.includes("Analyzers")
    ? ["WPROTO001", "WUH003", "WUH013", "WUH020", "WUH019", "WUH021", "WUH023"]
    : ["CS0246"];
  return { exitCode: 1, output: ids.map((id) => "error " + id).join("\\n") };
}
runProject(CHECK_PROJECTS[0], directory, false, build).then((result) => {
  console.log(JSON.stringify({ calls, failures: result.failures }));
  fs.rmSync(directory, { recursive: true, force: true });
});
`;
  const result = spawnSync(process.execPath, ["-e", nodeBody], { encoding: "utf8" });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
  assert.deepEqual(JSON.parse(result.stdout), { calls: 3, failures: [] });
});

runTest("the bounded worker count is configurable and rejects invalid values", () => {
  assert.ok(1 <= defaultJobs() && defaultJobs() <= 2);
  assert.equal(parseArguments(["node", "script", "--jobs=1"]).jobs, 1);
  for (const invalid of ["0", "-1", "1.5", "many", ""]) {
    assert.throws(
      () => parseArguments(["node", "script", `--jobs=${invalid}`]),
      /--jobs must be a positive integer/,
      invalid
    );
  }
  assert.equal(
    parseArguments(["node", "script", "--jobs=1000000"]).jobs,
    1000000,
    "the scheduler, rather than the parser, bounds valid requests by project-group count"
  );
});

runTest("project groups overlap without racing runtime and integrations", () => {
  const scriptPath = path.join(repoRoot, "scripts", "typecheck-controls.js");
  const nodeBody = `
const { runProjectGroups } = require(${JSON.stringify(scriptPath)});
let active = 0;
let maximum = 0;
const events = [];
async function run(project) {
  active++;
  maximum = Math.max(maximum, active);
  events.push("start:" + project);
  await new Promise((resolve) => setTimeout(resolve, 20));
  events.push("end:" + project);
  active--;
  return project;
}
runProjectGroups([["runtime", "integrations"], ["editor"], ["tests"]], 2, run)
  .then((results) => console.log(JSON.stringify({ events, maximum, results })));
`;
  const result = spawnSync(process.execPath, ["-e", nodeBody], { encoding: "utf8" });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
  const measured = JSON.parse(result.stdout);
  assert.equal(measured.maximum, 2, "two independent project groups must actually overlap");
  assert.ok(
    measured.events.indexOf("end:runtime") < measured.events.indexOf("start:integrations"),
    "the integration project must not overlap the runtime project it references"
  );
  assert.deepEqual(measured.results, [["runtime", "integrations"], ["editor"], ["tests"]]);
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

const workflow = fs
  .readFileSync(path.join(repoRoot, ".github", "workflows", "local-gates.yml"), "utf8")
  .replace(/\r\n/g, "\n");
const phaseJob = workflow.match(/^  typecheck-phases:\n(?:(?: {4}[^\n]*|)\n)*/m)?.[0] ?? "";
const resultJob = workflow.match(/^  typecheck:\n(?:(?: {4}[^\n]*|)\n)*/m)?.[0] ?? "";

runTest("parallel host phases retain complete commands without fail-fast cancellation", () => {
  assert.match(phaseJob, /fail-fast: false/);
  assert.match(phaseJob, /phase: \[sources, controls\]/);
  assert.doesNotMatch(phaseJob, /continue-on-error:|max-parallel: 1/);
  const phaseSteps = yaml.parse(workflow).jobs["typecheck-phases"].steps;
  const setupIndex = phaseSteps.findIndex((step) => step.uses?.startsWith("actions/setup-dotnet@"));
  assert.ok(setupIndex >= 0);
  const sdkGuard = phaseSteps[setupIndex + 1];
  const commands = phaseSteps
    .filter((step) => step !== sdkGuard && typeof step.run === "string")
    .map((step) => step.run);
  assert.deepEqual(commands, [
    "npm run typecheck:unity",
    "npm run test:enumeration",
    "npm run test:stable-assets",
    "npm run test:intmap",
    "npm run typecheck:controls -- --jobs=1"
  ]);
  assert.ok(
    phaseJob.includes(
      "if: matrix.phase == 'sources'\n        shell: bash\n        run: npm run test:enumeration"
    ),
    "Enumeration correctness must run in the sources phase"
  );
  assert.ok(
    phaseJob.includes("if: matrix.phase == 'sources'\n        run: npm run test:intmap"),
    "IntMap correctness must run in the sources phase"
  );
  for (const [phase, command] of [
    ["sources", "unity"],
    ["controls", "controls"]
  ]) {
    assert.ok(
      phaseJob.includes(
        `if: matrix.phase == '${phase}'\n        run: npm run typecheck:${command}`
      ),
      `${command} must run in its own phase without narrowing its project selection`
    );
  }
});

runTest("CI controls launch one SDK build at a time without changing local defaults", () => {
  assert.ok(phaseJob.includes("run: npm run typecheck:controls -- --jobs=1"));
  assert.equal(parseArguments(["node", "script", "--jobs=1"]).jobs, 1);
  assert.ok(1 <= defaultJobs() && defaultJobs() <= 2);
});

runTest("host phases isolate build outputs and cache only phase-specific dependencies", () => {
  assert.match(phaseJob, /uses: actions\/checkout@/);
  assert.match(phaseJob, /path: ~\/\.nuget\/packages/);
  assert.ok(phaseJob.includes("-typecheck-${{ matrix.phase }}-"));
  assert.doesNotMatch(phaseJob, /(?:upload|download)-artifact|restore-keys:|\b(?:bin|obj)\//);
});

runTest("the stable type-check gate fails for every non-success phase result", () => {
  assert.match(resultJob, /name: Type-check Runtime and tests\n/);
  assert.match(resultJob, /needs: typecheck-phases\n/);
  assert.match(resultJob, /if: always\(\)\n/);
  assert.ok(resultJob.includes("TYPECHECK_PHASE_RESULT: ${{ needs.typecheck-phases.result }}"));
  assert.doesNotMatch(resultJob, /continue-on-error:/);
  const body = resultJob.match(/        run: \|\n((?:          [^\n]*\n?)+)/)?.[1];
  assert.ok(body, "the final gate must execute a result check");
  const script = body.replace(/^ {10}/gm, "");
  const nodeBody = script.match(/^set -euo pipefail\nnode -e '([^'\n]+)'\n?$/)?.[1];
  assert.ok(nodeBody, "the final gate must propagate its Node result without shell suppression");
  for (const state of ["success", "failure", "cancelled", "skipped", ""]) {
    const result = spawnSync(process.execPath, ["-e", nodeBody], {
      encoding: "utf8",
      env: { ...process.env, TYPECHECK_PHASE_RESULT: state }
    });
    assert.ifError(result.error);
    assert.equal(result.status === 0, state === "success", `${state || "empty"}: ${result.stderr}`);
  }
});

const sdkWorkflows = [
  "wproto-generator.yml",
  "repo-lint.yml",
  "local-gates.yml",
  "random-quality.yml"
];
let pinnedSdkSteps = 0;
for (const file of sdkWorkflows) {
  const workflow = yaml.parse(
    fs.readFileSync(path.join(repoRoot, ".github/workflows", file), "utf8")
  );
  for (const [jobName, job] of Object.entries(workflow.jobs)) {
    const steps = job.steps || [];
    for (let index = 0; index < steps.length; index++) {
      const setup = steps[index];
      if (!setup.uses?.startsWith("actions/setup-dotnet@")) continue;
      pinnedSdkSteps++;
      runTest(
        `${file}/${jobName} selects its installed SDK and rejects missing or mismatched versions`,
        () => {
          const expected = setup.with["dotnet-version"];
          assert.match(expected, /^\d+\.\d+\.\d+$/);
          assert.match(
            setup.env?.DOTNET_INSTALL_DIR || "",
            /^\$\{\{ runner\.temp \}\}\/[a-z0-9-]+$/
          );
          assert.ok(setup.id);
          const guard = steps[index + 1];
          assert.equal(
            guard.env?.EXPECTED_DOTNET_SDK,
            "${{ steps." + setup.id + ".outputs.dotnet-version }}"
          );
          assert.equal(guard.shell, "bash");
          const fixture = fs.mkdtempSync(path.join(os.tmpdir(), "sdk-selection-"));
          try {
            fs.writeFileSync(
              path.join(fixture, "dotnet"),
              '#!/bin/sh\n[ "$1" = --version ] || exit 91\nprintf "%s\\n" "$STUB_SDK_VERSION"\nexit "$STUB_SDK_STATUS"\n',
              { mode: 0o755 }
            );
            const newer = `${Number(expected.split(".")[0]) + 1}.0.999`;
            for (const [selected, required, status, accepted] of [
              [expected, expected, 0, true],
              [newer, expected, 0, false],
              ["", "", 0, false],
              [expected, expected, 17, false],
              [expected, "", 0, false]
            ]) {
              const result = spawnSync("bash", ["-c", guard.run], {
                cwd: repoRoot,
                encoding: "utf8",
                env: {
                  ...process.env,
                  PATH: fixture + path.delimiter + process.env.PATH,
                  EXPECTED_DOTNET_SDK: required,
                  STUB_SDK_VERSION: selected,
                  STUB_SDK_STATUS: String(status)
                }
              });
              assert.ifError(result.error);
              assert.equal(
                result.status === 0,
                accepted,
                `selected=${selected || "empty"} required=${required || "empty"} exit=${status}: ${result.stdout} ${result.stderr}`
              );
            }
          } finally {
            fs.rmSync(fixture, { recursive: true, force: true });
          }
        }
      );
    }
  }
}
runTest("all seven pinned SDK installations have selection controls", () =>
  assert.equal(pinnedSdkSteps, 7)
);

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
