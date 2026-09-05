"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const workflow = fs
  .readFileSync(path.join(__dirname, "../../.github/workflows/unity-tests.yml"), "utf8")
  .replace(/\r\n/g, "\n");
const option = workflow.match(/^      acceptance:\n((?:        [^\n]*\n|\n)+)/m)?.[1];
assert.ok(option, "Manual acceptance input must exist");
assert.match(option, /^        required: false$/m);
assert.match(option, /^        default: "none"$/m);
assert.match(option, /^        type: choice$/m);
assert.deepEqual(
  [...option.matchAll(/^          - (\w+)$/gm)].map((match) => match[1]),
  ["none", "sentinel", "intmap", "all"]
);
const job = workflow.match(/^  unity-tests:\n([\s\S]*?)(?=^  [\w-]+:\n)/m)?.[1];
assert.ok(job, "Default licensed job must exist");
const steps = job.split(/(?=^      - name:)/m);
function named(name) {
  const matches = steps.filter((step) => step.startsWith(`      - name: ${name}\n`));
  assert.equal(matches.length, 1, `Exactly one ${name} step required`);
  return matches[0];
}
function identified(id) {
  const matches = steps.filter((step) => new RegExp(`^        id: ${id}$`, "m").test(step));
  assert.equal(matches.length, 1, `Exactly one ${id} step required`);
  return matches[0];
}
function expression(text) {
  const body = text.match(/\$\{\{\s*([\s\S]*?)\s*\}\}/)?.[1];
  assert.ok(body, "GitHub expression required");
  const evaluate = new Function(
    "github",
    "inputs",
    "steps",
    "needs",
    "always",
    "cancelled",
    "success",
    "contains",
    "fromJSON",
    `return (${body.replace(/\.([A-Za-z_][A-Za-z0-9_]*-[A-Za-z0-9_-]+)/g, '["$1"]')});`
  );
  return (context) =>
    evaluate(
      context.github,
      context.inputs,
      context.steps,
      context.needs,
      () => true,
      () => context.cancelled,
      () => context.success,
      (values, value) => values.includes(value),
      JSON.parse
    );
}
function predicate(step) {
  const line = step.match(/^        if: (.*)$/m)?.[1];
  assert.ok(line, "Explicit acceptance predicate required");
  assert.match(
    line,
    /always\(\)|cancelled\(\)/,
    "Prior failed steps must not silently suppress required evidence"
  );
  return expression(line);
}

const run = identified("run_acceptance");
const verify = identified("verify_acceptance");
const redact = identified("redact_acceptance");
const upload = identified("upload_acceptance");
const gate = named("Require requested native acceptance");
const selectedSteps = [run, verify, redact, upload, gate];
const predicates = selectedSteps.map(predicate);
let controls = 0;
for (const event of ["pull_request", "push", "schedule", "workflow_dispatch"]) {
  for (const acceptance of ["", "none", "sentinel", "intmap", "all"]) {
    for (const cancelled of [false, true]) {
      for (const acquired of ["true", "false", ""]) {
        for (const redaction of ["success", "failure", "skipped"]) {
          const context = {
            github: { event_name: event },
            inputs: { acceptance },
            cancelled,
            success: false,
            steps: {
              unity_lock: { outputs: { acquired } },
              redact_acceptance: { outcome: redaction }
            }
          };
          const requested =
            event === "workflow_dispatch" && ["sentinel", "intmap", "all"].includes(acceptance);
          const expected = [
            requested && !cancelled && acquired === "true",
            requested && !cancelled,
            requested,
            requested && redaction === "success",
            requested && !cancelled
          ];
          predicates.forEach((test, index) =>
            assert.equal(
              Boolean(test(context)),
              expected[index],
              `Wrong predicate for ${["run", "verify", "redact", "upload", "gate"][index]}: ${JSON.stringify(context)}`
            )
          );
          controls += predicates.length;
        }
      }
    }
  }
}

const profileLine = named("Require manually installed Unity editor").match(
  /^          provisioning-profile:\s*(?:>-\s*)?(\$\{\{[\s\S]*?\}\})/m
)?.[1];
const profile = expression(profileLine);
for (const modes of [
  ["editmode"],
  ["playmode"],
  ["standalone"],
  ["editmode", "playmode", "standalone"]
]) {
  for (const acceptance of ["", "none", "sentinel", "intmap", "all"]) {
    assert.equal(
      profile({
        inputs: { acceptance },
        needs: { "matrix-config": { outputs: { "test-modes": JSON.stringify(modes) } } }
      }),
      modes.includes("standalone") || ["intmap", "all"].includes(acceptance)
        ? "StandaloneWindowsIl2Cpp"
        : "EditorOnly"
    );
    controls++;
  }
}

assert.ok(
  job.indexOf(identified("unity_lock")) < job.indexOf(run),
  "Acceptance requires the existing lease"
);
assert.ok(
  job.indexOf(identified("run_standalone")) < job.indexOf(run),
  "Acceptance must retain standard correctness attempts"
);
assert.ok(
  job.indexOf(run) < job.indexOf(identified("return_unity_license")),
  "Acceptance must finish before returning its license"
);
assert.equal(
  (job.match(/\.github\/actions\/acquire-build-lock@/g) || []).length,
  1,
  "No second lease acquisition"
);
assert.ok(run.includes('UH_CENTRAL_LICENSE_RETURN: "true"'));
assert.ok(run.includes("UH_ACCEPTANCE: ${{ inputs.acceptance }}"));
assert.ok(verify.includes("UH_ACCEPTANCE: ${{ inputs.acceptance }}"));
assert.ok(run.includes("./scripts/unity/run-acceptance.ps1"));
assert.ok(verify.includes("./scripts/unity/verify-acceptance.ps1"));
assert.ok(run.includes("UNITY_EDITOR_PATH: ${{ steps.ensure_unity_editor.outputs.editor-path }}"));
for (const step of [run, verify, redact, upload]) {
  assert.match(step, /continue-on-error: true/);
}
assert.doesNotMatch(gate, /continue-on-error:/);
assert.match(upload, /if-no-files-found: error/);
const nodeSetup = named("Setup Node.js for credential redaction");
assert.ok(
  job.indexOf(nodeSetup) < job.indexOf(verify),
  "Node must be installed before the raw evidence verifier"
);
assert.match(nodeSetup, /if:.*always\(\)/);
assert.match(nodeSetup, /uses: actions\/setup-node@/);
for (const [phase, id] of [
  ["RUN", "run_acceptance"],
  ["VERIFY", "verify_acceptance"],
  ["REDACT", "redact_acceptance"],
  ["UPLOAD", "upload_acceptance"]
]) {
  assert.ok(
    gate.includes(`ACCEPTANCE_${phase}: \${{ steps.${id}.outcome }}`),
    `Gate must inspect raw ${phase} outcome`
  );
}
const body = gate
  .match(/^        run: \|\n((?:          [^\n]*\n|\n)+)/m)?.[1]
  ?.replace(/^ {10}/gm, "");
assert.ok(body, "Executable outcome gate required");
const gateCases = [{ outcomes: ["success", "success", "success", "success"], reject: false }];
for (let stage = 0; stage < 4; stage++) {
  for (const failure of ["failure", "cancelled", "skipped", "", "unknown"]) {
    const outcomes = ["success", "success", "success", "success"];
    outcomes[stage] = failure;
    gateCases.push({ outcomes, reject: true });
  }
}
const result = spawnSync(
  "pwsh",
  [
    "-NoProfile",
    "-NonInteractive",
    "-Command",
    `
$ErrorActionPreference = 'Stop'
$gate = [scriptblock]::Create([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:ACCEPTANCE_GATE_BODY)))
foreach ($case in ($env:ACCEPTANCE_GATE_CASES | ConvertFrom-Json)) {
    $stages = @('RUN', 'VERIFY', 'REDACT', 'UPLOAD')
    for ($index = 0; $index -lt $stages.Count; $index++) {
        [Environment]::SetEnvironmentVariable('ACCEPTANCE_' + $stages[$index], $case.outcomes[$index], 'Process')
    }
    $rejected = $false
    try { & $gate } catch { $rejected = $true }
    if ($rejected -ne $case.reject) { throw "Wrong acceptance gate verdict: $($case.outcomes -join ',')" }
}
`
  ],
  {
    encoding: "utf8",
    env: {
      ...process.env,
      ACCEPTANCE_GATE_BODY: Buffer.from(body).toString("base64"),
      ACCEPTANCE_GATE_CASES: JSON.stringify(gateCases)
    }
  }
);
assert.ifError(result.error);
assert.equal(result.status, 0, result.stdout + result.stderr);
controls += gateCases.length;
console.log(
  `${controls} optional acceptance workflow predicate, provisioning and outcome controls passed.`
);
