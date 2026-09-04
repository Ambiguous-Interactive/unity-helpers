"use strict";

const assert = require("node:assert/strict");
const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { TEST_MODES, resolveTestMatrix } = require("../unity/resolve-test-matrix.js");

const repoRoot = path.resolve(__dirname, "..", "..");
const versions = JSON.parse(
  fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8")
).all;
let cases = 0;

for (const requestedVersion of ["", ...versions]) {
  for (const requestedMode of ["", "all", ...TEST_MODES]) {
    const result = resolveTestMatrix(versions, requestedVersion, requestedMode);
    const selectedVersions = requestedVersion ? [requestedVersion] : versions;
    const selectedModes = TEST_MODES.includes(requestedMode) ? [requestedMode] : TEST_MODES;
    assert.deepEqual(result["unity-versions"], selectedVersions);
    assert.deepEqual(result["test-modes"], selectedModes);
    assert.deepEqual(
      result["matrix-exclude"],
      versions
        .filter((version) => !selectedVersions.includes(version))
        .map((version) => ({
          "unity-version": version
        }))
    );
    cases++;
  }
}
for (const invalid of ["unknown", "editmode,playmode", "editmode\nplaymode", "PLAYMODE"]) {
  assert.throws(() => resolveTestMatrix(versions, "", invalid), /Unsupported Unity test mode/);
  cases++;
}
assert.throws(() => resolveTestMatrix(versions, "9999.1.1f1"), /Unsupported Unity version/);
for (const invalid of [[], null, [""], ["version", "version"], [123]]) {
  assert.throws(() => resolveTestMatrix(invalid), /Unity versions must/);
  cases++;
}
const matrix = resolveTestMatrix(versions);
matrix["unity-versions"].pop();
assert.deepEqual(resolveTestMatrix(versions)["unity-versions"], versions);
assert.equal(resolveTestMatrix(versions)["test-modes"].length * versions.length, 12);

const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "unity-grouped-modes-"));
try {
  const output = path.join(scratch, "outputs");
  const result = spawnSync(
    process.execPath,
    [path.join(repoRoot, "scripts/unity/resolve-test-matrix.js")],
    {
      cwd: scratch,
      encoding: "utf8",
      env: {
        ...process.env,
        INPUT_UNITY_VERSION: versions[0],
        INPUT_TEST_MODE: "standalone",
        GITHUB_OUTPUT: output
      }
    }
  );
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stderr);
  const published = Object.fromEntries(
    fs
      .readFileSync(output, "utf8")
      .trim()
      .split("\n")
      .map((line) => {
        const separator = line.indexOf("=");
        return [line.slice(0, separator), JSON.parse(line.slice(separator + 1))];
      })
  );
  assert.deepEqual(published, resolveTestMatrix(versions, versions[0], "standalone"));
  cases++;
} finally {
  fs.rmSync(scratch, { recursive: true, force: true });
}

const workflow = fs
  .readFileSync(path.join(repoRoot, ".github/workflows/unity-tests.yml"), "utf8")
  .replace(/\r\n/g, "\n");
function jobText(id) {
  const start = workflow.indexOf(`\n  ${id}:\n`);
  assert.notEqual(start, -1, `${id}: missing job`);
  const rest = workflow.slice(start + 1);
  const next = rest.slice(1).search(/^  [A-Za-z0-9_-]+:\s*$/m);
  return next === -1 ? rest : rest.slice(0, next + 1);
}
function stepText(job, id) {
  const steps = job
    .split(/(?=^      - name:)/m)
    .filter((step) => new RegExp(`^        id: ${id}$`, "m").test(step));
  assert.equal(steps.length, 1, `${id}: expected exactly one step`);
  return steps[0];
}
function runBody(step) {
  const body = step.match(/^        run: \|\n((?:          [^\n]*\n|\n)+)/m)?.[1];
  assert.ok(body, "expected an executable workflow body");
  return body.replace(/^ {10}/gm, "");
}
function runPowerShell(body, env) {
  const result = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", body], {
    cwd: repoRoot,
    encoding: "utf8",
    env: { ...process.env, ...env }
  });
  assert.ifError(result.error);
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
  return result.stdout;
}

const guardSource = fs.readFileSync(
  path.join(repoRoot, "scripts/unity/assert-no-active-unity-editor.ps1"),
  "utf8"
);
assert.doesNotMatch(guardSource, /Stop-Process|\.Kill\(|taskkill/i);
const guardCases = [
  { processes: [], reject: false },
  { processes: [{ ProcessName: "Unity", Id: 42 }], reject: true },
  { processes: [{ ProcessName: "unity", Id: 43 }], reject: true },
  { processes: [{ ProcessName: "UnityShaderCompiler", Id: 44 }], reject: false },
  { processes: [], inspectionFailure: true, reject: true }
];
runPowerShell(
  `
$ErrorActionPreference = 'Stop'
$guard = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:GUARD_BODY)))
function Get-Process {
    [CmdletBinding()]
    param()
    if ($script:inspectionFailure) { throw 'Process inspection failed.' }
    $script:processes
}
foreach ($case in ($env:GUARD_CASES | ConvertFrom-Json)) {
    $script:inspectionFailure = [bool]$case.inspectionFailure
    $script:processes = $case.processes
    $rejected = $false
    try { & $guard } catch { $rejected = $true }
    if ($rejected -ne $case.reject) { throw "Wrong editor boundary verdict: $($case | ConvertTo-Json -Compress)" }
}
`,
  {
    GUARD_BODY: Buffer.from(guardSource).toString("base64"),
    GUARD_CASES: JSON.stringify(guardCases.map((entry) => ({ inspectionFailure: false, ...entry })))
  }
);
cases += guardCases.length;

for (const [jobId, selectedModes] of [
  ["unity-tests", TEST_MODES],
  ["unity-tests-single-threaded", ["editmode", "playmode"]]
]) {
  const job = jobText(jobId);
  const acquiredBody = runBody(stepText(job, "require_unity_lock"));
  runPowerShell(
    `
$ErrorActionPreference = 'Stop'
$gate = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:ACQUIRE_GATE_BODY)))
foreach ($state in @('true', 'false', '', 'unknown')) {
    $env:ACQUIRED = $state
    $rejected = $false
    try { & $gate } catch { $rejected = $true }
    if ($rejected -ne ($state -ne 'true')) { throw "Wrong lock acquisition verdict: $state" }
}
`,
    { ACQUIRE_GATE_BODY: Buffer.from(acquiredBody).toString("base64") }
  );
  cases += 4;
  const gateStep = stepText(job, "require_selected_modes");
  const gateBody = runBody(gateStep);
  assert.match(gateStep, /if:.*always\(\)/);
  assert.doesNotMatch(gateStep, /continue-on-error:/);
  const outcomes = {};
  for (const mode of TEST_MODES) {
    for (const phase of ["RUN", "VERIFY", "REDACT", "UPLOAD"]) {
      outcomes[`${mode.toUpperCase()}_${phase}`] = selectedModes.includes(mode)
        ? "success"
        : "skipped";
    }
  }
  const gateCases = [
    { name: "all selected modes pass", selection: selectedModes, outcomes, reject: false }
  ];
  for (const mode of selectedModes) {
    for (const phase of ["RUN", "VERIFY", "REDACT", "UPLOAD"]) {
      assert.ok(
        gateStep.includes(
          `${mode.toUpperCase()}_${phase}: \${{ steps.${phase.toLowerCase()}_${mode}.outcome }}`
        ),
        `${jobId}/${mode}/${phase}: the gate must inspect the matching unsuppressed step outcome`
      );
    }
    const runStep = stepText(job, `run_${mode}`);
    const body = runBody(runStep);
    const guardPosition = body.indexOf("./scripts/unity/assert-no-active-unity-editor.ps1");
    assert.ok(
      guardPosition !== -1 && guardPosition < body.indexOf("./scripts/unity/run-ci-tests.ps1"),
      `${jobId}/${mode}: guard must run before Unity`
    );
    const stopPosition = body.indexOf("$ErrorActionPreference = 'Stop'");
    assert.ok(
      0 <= stopPosition && stopPosition < guardPosition,
      "guard errors must stop the caller"
    );
    assert.match(runStep, /continue-on-error: true/);
    assert.ok(runStep.includes("!cancelled()"));
    assert.ok(runStep.includes("steps.unity_lock.outputs.acquired == 'true'"));
    if (mode !== "editmode") {
      const headStep = stepText(job, `${mode}_head`);
      assert.ok(headStep.includes("/.github/actions/require-current-pr-head@"));
      assert.ok(headStep.includes("expected-head-sha: ${{ github.event.pull_request.head.sha }}"));
      assert.ok(runStep.includes(`steps.${mode}_head.outcome == 'success'`));
      assert.ok(job.indexOf(headStep) < job.indexOf(runStep));
    }
    if (jobId === "unity-tests") {
      assert.ok(
        runStep.includes(`contains(fromJSON(needs.matrix-config.outputs.test-modes), '${mode}')`)
      );
    }
    for (const phase of ["RUN", "VERIFY", "REDACT", "UPLOAD"]) {
      for (const failure of ["failure", "cancelled", "skipped", ""]) {
        gateCases.push({
          name: `${mode}/${phase}/${failure}`,
          selection: selectedModes,
          outcomes: { ...outcomes, [`${mode.toUpperCase()}_${phase}`]: failure },
          reject: true
        });
      }
    }
  }
  for (const mode of selectedModes) {
    const single = Object.fromEntries(
      Object.keys(outcomes).map((key) => [
        key,
        key.startsWith(mode.toUpperCase() + "_") ? "success" : "skipped"
      ])
    );
    gateCases.push({
      name: `${mode} dispatch`,
      selection: [mode],
      outcomes: single,
      reject: false
    });
    const unexpected = selectedModes.find((entry) => entry !== mode);
    gateCases.push({
      name: "unselected mode ran",
      selection: [mode],
      outcomes: { ...single, [`${unexpected.toUpperCase()}_RUN`]: "success" },
      reject: true
    });
  }
  for (const selection of [[], ["unknown"], ["editmode", "editmode"]]) {
    gateCases.push({ name: "invalid selection", selection, outcomes, reject: true });
  }
  runPowerShell(
    `
$ErrorActionPreference = 'Stop'
$gate = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:MODE_GATE_BODY)))
foreach ($case in ($env:MODE_GATE_CASES | ConvertFrom-Json)) {
    $env:SELECTED_MODES = ConvertTo-Json -InputObject @($case.selection) -Compress
    foreach ($property in $case.outcomes.PSObject.Properties) {
        [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
    }
    $rejected = $false
    try { & $gate } catch { $rejected = $true }
    if ($rejected -ne $case.reject) { throw "Wrong mode gate verdict: $($case.name)" }
}
`,
    {
      MODE_GATE_BODY: Buffer.from(gateBody).toString("base64"),
      MODE_GATE_CASES: JSON.stringify(gateCases)
    }
  );
  cases += gateCases.length;
}

console.log(`Grouped Unity mode controls passed: ${cases}`);
