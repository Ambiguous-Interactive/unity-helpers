"use strict";

// cspell:ignore ine

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
  ["none", "sentinel", "intmap", "serialization", "nullableconsumer", "borrowed", "wproto", "all"]
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
  const body = text.match(/\$\{\{\s*([\s\S]*?)\s*\}\}/)?.[1] ?? text.trim();
  assert.ok(body, "GitHub expression required");
  const evaluate = new Function(
    "github",
    "inputs",
    "steps",
    "needs",
    "matrix",
    "always",
    "cancelled",
    "success",
    "failure",
    "contains",
    "fromJSON",
    "format",
    `return (${body.replace(/\.([A-Za-z_][A-Za-z0-9_]*-[A-Za-z0-9_-]+)/g, '["$1"]')});`
  );
  return (context) =>
    evaluate(
      context.github,
      context.inputs,
      context.steps,
      context.needs,
      context.matrix,
      () => true,
      () => context.cancelled,
      () => context.success,
      () => context.failure,
      (values, value) => values.includes(value),
      JSON.parse,
      (template, ...values) => template.replace(/\{(\d+)\}/g, (_, index) => values[Number(index)])
    );
}
function predicate(step) {
  // Long conditions are wrapped in the repo's folded `if: >-` style; keep the
  // extraction aligned with the diagnostic sweep below.
  const line = step
    .match(/^        if: ([^\n]*(?:\n          [^\n]*)*)/m)?.[1]
    ?.replace(/^(?:>-?|\|-?)\s*/, "")
    .trim();
  assert.ok(line, "Explicit acceptance predicate required");
  assert.match(
    line,
    /always\(\)|cancelled\(\)/,
    "Prior failed steps must not silently suppress required evidence"
  );
  return expression(line);
}

const run = identified("run_acceptance");
assert.match(run, /timeout-minutes: \$\{\{ inputs\.acceptance == 'wproto' && 150 \|\| 120 \}\}/);
const diagnostics = workflow.match(/^      intmap-diagnostics:\n((?:        [^\n]*\n|\n)+)/m)?.[1];
assert.ok(diagnostics, "Manual diagnostic input must exist");
assert.match(diagnostics, /^        default: false$/m);
assert.match(diagnostics, /^        type: boolean$/m);
const diagnosticEnvironment = run.match(/^          UH_PERF_DIAGNOSTICS: (.+)$/m)?.[1];
assert.ok(diagnosticEnvironment, "Diagnostic mode must be scoped to the additional player");
assert.equal((workflow.match(/UH_PERF_DIAGNOSTICS:/g) || []).length, 1);
const diagnosticMode = expression(diagnosticEnvironment);
let diagnosticControls = 0;
for (const event of ["pull_request", "push", "workflow_dispatch"]) {
  for (const acceptance of [
    "none",
    "sentinel",
    "intmap",
    "serialization",
    "nullableconsumer",
    "borrowed",
    "wproto",
    "all"
  ]) {
    for (const requested of [undefined, false, true]) {
      assert.equal(
        diagnosticMode({
          github: { event_name: event },
          inputs: { acceptance, "intmap-diagnostics": requested }
        }),
        event === "workflow_dispatch" && acceptance === "intmap" && requested === true ? "1" : ""
      );
      diagnosticControls++;
    }
  }
}
const verify = identified("verify_acceptance");
const redact = identified("redact_acceptance");
const reverify = identified("reverify_wproto_acceptance");
const upload = identified("upload_acceptance");
const uploadPaths = upload
  .match(/^          path: \|\n((?:            [^\n]*\n)+)/m)?.[1]
  ?.trim()
  .split("\n")
  .map((line) => line.trim());
assert.equal(
  uploadPaths?.length,
  2,
  "Acceptance upload requires its directory and conditional summary exclusion"
);
assert.equal(uploadPaths[0], ".artifacts/unity/${{ matrix.unity-version }}-acceptance");
assert.match(upload, /^          name: unity-\$\{\{ matrix\.unity-version \}\}-acceptance$/m);
const summaryExclusion = expression(uploadPaths[1]);
const gate = named("Require requested native acceptance");
const selectedSteps = [run, verify, redact, reverify, upload, gate];
const predicates = selectedSteps.map(predicate);
let controls = diagnosticControls;
for (const event of ["pull_request", "push", "schedule", "workflow_dispatch"]) {
  for (const acceptance of [
    "",
    "none",
    "sentinel",
    "intmap",
    "serialization",
    "nullableconsumer",
    "borrowed",
    "wproto",
    "all"
  ]) {
    for (const unityVersion of ["2021.3.45f1", "2022.3.45f1", "6000.5.2f1", "6000.6.0f1"]) {
      for (const cancelled of [false, true]) {
        for (const acquired of ["true", "false", ""]) {
          for (const { redaction, reverification } of ["success", "failure", "skipped"].flatMap(
            (redaction) =>
              ["success", "failure", "skipped", "cancelled", ""].map((reverification) => ({
                redaction,
                reverification
              }))
          )) {
            const context = {
              github: { event_name: event },
              inputs: { acceptance },
              // Acceptance runs once in each selected version job after its
              // standard modes; the editor gate verifies StandaloneWindowsIl2Cpp.
              matrix: { "unity-version": unityVersion },
              cancelled,
              success: false,
              steps: {
                checkout: { outcome: "success" },
                unity_lock: { outputs: { acquired } },
                redact_acceptance: { outcome: redaction },
                reverify_wproto_acceptance: { outcome: reverification }
              }
            };
            const requested =
              event === "workflow_dispatch" &&
              [
                "sentinel",
                "intmap",
                "serialization",
                "nullableconsumer",
                "borrowed",
                "wproto",
                "all"
              ].includes(acceptance) &&
              (!["nullableconsumer", "wproto"].includes(acceptance) ||
                ["2021.3.45f1", "6000.6.0f1"].includes(unityVersion));
            const expected = [
              requested && !cancelled && acquired === "true",
              requested && !cancelled,
              requested,
              requested && acceptance === "wproto" && !cancelled && redaction === "success",
              requested && redaction === "success",
              requested && !cancelled
            ];
            predicates.forEach((test, index) =>
              assert.equal(
                Boolean(test(context)),
                expected[index],
                `Wrong predicate for ${["run", "verify", "redact", "reverify", "upload", "gate"][index]}: ${JSON.stringify(context)}`
              )
            );
            assert.equal(
              summaryExclusion(context),
              acceptance === "wproto" && reverification !== "success"
                ? "!**/corpus-summary.json"
                : "",
              `Wrong rejected-capture summary exclusion: ${JSON.stringify(context)}`
            );
            controls += predicates.length + 1;
          }
        }
      }
    }
  }
}

// Every version job can execute standalone coverage, so the profile is a
// bounded literal and verifies the IL2CPP module once before all selected modes.
assert.match(
  named("Require manually installed Unity editor"),
  /^          provisioning-profile: StandaloneWindowsIl2Cpp$/m
);
controls++;

// Standalone coverage must be selected whenever acceptance is requested.
assert.match(
  workflow,
  /INPUT_ACCEPTANCE: \$\{\{ inputs\.acceptance \}\}/,
  "Dispatch acceptance must feed resolve-test-matrix.js"
);
const { resolveTestMatrix } = require("../unity/resolve-test-matrix.js");
assert.deepEqual(resolveTestMatrix(["2022.3.45f1"], "", "all", "none")["test-modes"], [
  "editmode",
  "playmode",
  "standalone"
]);
assert.deepEqual(resolveTestMatrix(["2022.3.45f1"], "", "editmode", "sentinel")["test-modes"], [
  "editmode",
  "standalone"
]);
assert.deepEqual(resolveTestMatrix(["2022.3.45f1"], "", "playmode", "intmap")["test-modes"], [
  "playmode",
  "standalone"
]);
controls += 3;

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
assert.ok(job.indexOf(redact) < job.indexOf(reverify));
assert.ok(job.indexOf(reverify) < job.indexOf(upload));
assert.ok(reverify.includes("./scripts/unity/verify-acceptance.ps1"));
assert.ok(reverify.includes("-Acceptance 'wproto'"));
assert.match(reverify, /timeout-minutes: 5/);
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
  ["REVERIFY", "reverify_wproto_acceptance"],
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
const gateCases = [
  {
    acceptance: "intmap",
    reverification: "skipped",
    outcomes: ["success", "success", "success", "success"],
    reject: false
  }
];
for (const reverification of ["success", "failure", "cancelled", "skipped", "", "unknown"]) {
  gateCases.push({
    acceptance: "wproto",
    reverification,
    outcomes: ["success", "success", "success", "success"],
    reject: reverification !== "success"
  });
}
for (let stage = 0; stage < 4; stage++) {
  for (const failure of ["failure", "cancelled", "skipped", "", "unknown"]) {
    const outcomes = ["success", "success", "success", "success"];
    outcomes[stage] = failure;
    gateCases.push({ acceptance: "intmap", reverification: "skipped", outcomes, reject: true });
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
    [Environment]::SetEnvironmentVariable('UH_ACCEPTANCE', $case.acceptance, 'Process')
    [Environment]::SetEnvironmentVariable('ACCEPTANCE_REVERIFY', $case.reverification, 'Process')
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
const workflowDirectory = path.join(__dirname, "../../.github/workflows");
let diagnosticSubjects = 0;
let declaredDiagnostics = 0;
for (const file of fs.readdirSync(workflowDirectory).filter((name) => /\.ya?ml$/.test(name))) {
  const source = fs.readFileSync(path.join(workflowDirectory, file), "utf8").replace(/\r\n/g, "\n");
  declaredDiagnostics += [
    ...source.matchAll(
      /^        uses: \.\/\.github\/actions\/(dump-unity-log-tail|redact-unity-artifacts)$/gm
    )
  ].length;
  for (const match of source.matchAll(/^  ([\w-]+):\n([\s\S]*?)(?=^  [\w-]+:\n|(?![\s\S]))/gm)) {
    const jobSteps = match[2].split(/(?=^      - name:)/m);
    const checkout = jobSteps.filter((step) => /^        uses: actions\/checkout@/m.test(step));
    for (const step of jobSteps) {
      const action = step.match(
        /^        uses: \.\/\.github\/actions\/(dump-unity-log-tail|redact-unity-artifacts)$/m
      )?.[1];
      if (!action) continue;
      const condition = step
        .match(/^        if: ([^\n]*(?:\n          [^\n]*)*)/m)?.[1]
        .replace(/^(?:>-?|\|-?)\s*/, "")
        .trim();
      assert.ok(condition, `${file}/${match[1]} diagnostic predicate missing`);
      const evaluate = expression(condition);
      for (const checkoutOutcome of ["skipped", "failure", "cancelled", "success"]) {
        for (const status of ["success", "failure", "cancelled"]) {
          const context = {
            github: { event_name: "workflow_dispatch" },
            inputs: { acceptance: "all" },
            needs: {
              "matrix-config": {
                outputs: {
                  "test-modes": '["editmode","playmode","standalone"]'
                }
              }
            },
            matrix: {},
            success: status === "success",
            failure: status === "failure",
            cancelled: status === "cancelled",
            steps: new Proxy(
              {},
              {
                get: (_, id) => ({
                  outcome: id === "checkout" ? checkoutOutcome : status
                })
              }
            )
          };
          const expected =
            checkoutOutcome === "success" &&
            (action === "redact-unity-artifacts" || status !== "success");
          assert.equal(
            Boolean(evaluate(context)),
            expected,
            `${file}/${match[1]}/${action}: checkout=${checkoutOutcome}, job=${status}`
          );
          controls++;
        }
      }
      assert.equal(checkout.length, 1, `${file}/${match[1]} needs one current-job checkout`);
      assert.match(checkout[0], /^        id: checkout$/m);
      assert.ok(jobSteps.indexOf(checkout[0]) < jobSteps.indexOf(step));
      diagnosticSubjects++;
    }
  }
}
assert.equal(
  diagnosticSubjects,
  declaredDiagnostics,
  "Every declared diagnostic action must be exercised"
);
assert.ok(
  diagnosticSubjects >= 15,
  "The sweep must include all existing local diagnostic/redaction actions"
);
console.log(`${diagnosticSubjects} local action checkout dependencies checked.`);
console.log(
  `${controls} optional acceptance workflow predicate, provisioning and outcome controls passed.`
);

const onlyRun = identified("run_proto_only");
const onlyVerify = identified("verify_proto_only");
const onlyRedact = identified("redact_proto_only");
const onlyUpload = identified("upload_proto_only");
const onlyGate = named("Require WProto-only Standalone acceptance");
const onlyPredicates = [onlyRun, onlyVerify, onlyRedact, onlyUpload, onlyGate].map(predicate);
let onlyControls = 0;
for (const modes of [
  ["editmode"],
  ["playmode"],
  ["standalone"],
  ["editmode", "playmode", "standalone"]
]) {
  for (const cancelled of [false, true]) {
    for (const acquired of ["true", "false"]) {
      for (const head of ["success", "failure"]) {
        for (const runOutcome of ["success", "failure", "skipped", "cancelled"]) {
          for (const redaction of ["success", "failure"]) {
            const context = {
              cancelled,
              needs: {
                "matrix-config": {
                  outputs: { "test-modes": JSON.stringify(modes) }
                }
              },
              steps: {
                checkout: { outcome: "success" },
                unity_lock: { outputs: { acquired } },
                proto_only_head: { outcome: head },
                run_proto_only: { outcome: runOutcome },
                redact_proto_only: { outcome: redaction }
              }
            };
            const requested = modes.includes("standalone");
            assert.deepEqual(
              onlyPredicates.map((test) => test(context)),
              [
                requested && !cancelled && acquired === "true" && head === "success",
                requested && !cancelled && runOutcome !== "skipped",
                requested,
                requested && redaction === "success",
                requested && !cancelled
              ]
            );
            onlyControls++;
          }
        }
      }
    }
  }
}
assert.match(onlyRun, /-ProjectScope 'proto-only'/);
assert.match(onlyRun, /-AdditionalScriptingDefines WALLSTOP_PROTO_ONLY/);
assert.match(onlyRun, /-AssemblyNames 'WallstopStudios\.UnityHelpers\.Tests\.ProtoOnly'/);
assert.match(onlyRun, /-ReleasePlayerBuild/);
assert.match(onlyRun, /-ManagedStrippingLevel 'High'/);
assert.match(onlyRun, /-Il2CppCompilerConfiguration 'Release'/);
assert.match(
  onlyVerify,
  /required-test-name: WallstopStudios\.UnityHelpers\.Tests\.Serialization\.WProtoOnlyModeTests\.MissingFormatterCannotReachTheLegacyBackend/
);
assert.match(onlyUpload, /if-no-files-found: error/);
assert.match(onlyGate, /assert-test-mode-outcomes\.ps1/);
for (const mode of ["EDITMODE", "PLAYMODE"]) {
  for (const stage of ["RUN", "VERIFY", "REDACT", "UPLOAD"]) {
    assert.match(onlyGate, new RegExp(`^          ${mode}_${stage}: "skipped"$`, "m"));
  }
}
for (const stage of ["RUN", "VERIFY", "REDACT", "UPLOAD"]) {
  assert.match(
    onlyGate,
    new RegExp(
      `^          STANDALONE_${stage}: \\$\\{\\{ steps\\.\\w+_proto_only\\.outcome \\}\\}$`,
      "m"
    )
  );
}
console.log(`${onlyControls} WProto-only player workflow controls passed.`);

const readbackOriginalJobPredicates = {
  "matrix-config":
    "(github.event_name != 'pull_request' ||\n          github.event.pull_request.head.repo.full_name == github.repository) &&\n        (github.event_name != 'push' || github.ref_protected)",
  "runner-preflight":
    "github.event.pull_request.user.login != 'dependabot[bot]' && (github.event_name != 'pull_request' || github.event.pull_request.head.repo.full_name == github.repository)",
  "unity-tests":
    "!cancelled() &&\n        (github.event_name != 'pull_request' ||\n          github.event.pull_request.head.repo.full_name == github.repository) &&\n        (github.event_name != 'push' || github.ref_protected) &&\n        needs.matrix-config.result == 'success' &&\n        needs.runner-preflight.result == 'success' &&\n        needs.matrix-config.outputs.test-modes != '[]' &&\n        needs.matrix-config.outputs.superseded != 'true'",
  "unity-tests-single-threaded":
    "!cancelled() &&\n        (github.event_name != 'pull_request' ||\n          github.event.pull_request.head.repo.full_name == github.repository) &&\n        (github.event_name != 'push' || github.ref_protected) &&\n        needs.matrix-config.result == 'success' &&\n        needs.runner-preflight.result == 'success' &&\n        needs.matrix-config.outputs.superseded != 'true'",
  "unitypackage-smoke":
    "!cancelled() &&\n        (github.event_name != 'pull_request' ||\n          github.event.pull_request.head.repo.full_name == github.repository) &&\n        (github.event_name != 'push' || github.ref_protected) &&\n        needs.matrix-config.result == 'success' &&\n        needs.runner-preflight.result == 'success' &&\n        needs.matrix-config.outputs.superseded != 'true'",
  "unity-tests-aggregate": "always()",
  "unity-tests-single-threaded-aggregate": "always()",
  "unitypackage-smoke-aggregate": "always()",
  "unity-ci-success": "always()"
};

const historicalJob = workflow.match(
  /^  native-historical-readback:\n([\s\S]*?)(?=^  [\w-]+:\n)/m
)?.[1];
assert.ok(historicalJob);
assert.match(historicalJob, /^    needs: runner-preflight$/m);
assert.match(historicalJob, /^    runs-on: \[self-hosted, Windows, RAM-64GB, fast\]$/m);
assert.match(historicalJob, /^      actions: read$/m);
assert.match(historicalJob, /^      contents: read$/m);
assert.doesNotMatch(
  historicalJob,
  /UNITY_SERIAL|UNITY_EMAIL|UNITY_PASSWORD|ensure-unity-editor|run-ci-tests\.ps1|run-acceptance\.ps1|-EditorPath|-ProjectPath|CompareLoaderSnaps/
);
const readbackOption = workflow.match(
  /^      native-readback-run-id:\n((?:        [^\n]*\n|\n)+)/m
)?.[1];
assert.match(readbackOption, /^        default: ""$/m);
assert.match(readbackOption, /^        type: string$/m);
assert.ok(
  [...workflow.slice(0, workflow.indexOf("\npermissions:")).matchAll(/^      [\w-]+:\n/gm)]
    .length <= 10
);
function jobGate(block) {
  return expression(block.match(/^    if: (?:>-\n\s*)?\$\{\{([\s\S]*?)\}\}/m)?.[1]);
}
let readbackJobControls = 0;
for (const event of ["push", "pull_request", "workflow_dispatch"]) {
  for (const request of ["", "39132725629"]) {
    for (const cancelled of [false, true]) {
      for (const fork of [false, true]) {
        for (const protectedRef of [false, true]) {
          for (const result of ["success", "failure", "skipped"]) {
            const context = {
              github: {
                event_name: event,
                repository: "Org/Repo",
                ref_protected: protectedRef,
                ref: "refs/heads/feature",
                sha: "abc",
                run_id: "100",
                workflow: "Unity Tests",
                event: {
                  pull_request: {
                    number: 42,
                    head: { repo: { full_name: fork ? "Other/Repo" : "Org/Repo" } },
                    user: { login: "developer" }
                  }
                }
              },
              inputs: { "native-readback-run-id": request },
              cancelled,
              steps: {},
              needs: {
                "matrix-config": {
                  result,
                  outputs: { "test-modes": '["standalone"]', superseded: "false" }
                },
                "runner-preflight": { result }
              }
            };
            const requested = event === "workflow_dispatch" && request !== "";
            for (const [name, previous] of Object.entries(readbackOriginalJobPredicates)) {
              const block = workflow.match(
                new RegExp(`^  ${name}:\\n([\\s\\S]*?)(?=^  [\\w-]+:\\n|(?![\\s\\S]))`, "m")
              )?.[1];
              assert.ok(block, name);
              const expected =
                Boolean(expression(previous)(context)) &&
                (name === "runner-preflight" || !requested);
              assert.equal(
                Boolean(jobGate(block)(context)),
                expected,
                `${name} preserves its prior predicate outside explicit readback`
              );
            }
            assert.equal(
              Boolean(jobGate(historicalJob)(context)),
              requested && !cancelled && result === "success"
            );
            readbackJobControls++;
          }
        }
      }
    }
  }
}
function foldedGroup(block, indentation) {
  const header = " ".repeat(indentation) + "group: >-\n";
  const start = block.indexOf(header);
  assert.ok(start >= 0, "Concurrency group uses a folded scalar");
  const prefix = " ".repeat(indentation + 2);
  const lines = block.slice(start + header.length).split("\n");
  const content = [];
  for (const line of lines) {
    if (!line.startsWith(prefix)) break;
    assert.ok(
      line.length > prefix.length && line[prefix.length] !== " ",
      "Folded group has no blank or extra-indented lines"
    );
    content.push(line.slice(prefix.length));
  }
  assert.ok(content.length > 1);
  return content.join(" ");
}
const concurrencyGroup = foldedGroup(workflow, 2);
function renderGroup(template, context) {
  return template.replace(/\$\{\{([\s\S]*?)\}\}/g, (_, value) => expression(value)(context));
}
const groupContext = {
  github: {
    event_name: "workflow_dispatch",
    sha: "abc",
    ref: "refs/heads/feature",
    run_id: "100",
    event: { pull_request: {} }
  },
  inputs: { "native-readback-run-id": "" }
};
const ordinaryGroup = renderGroup(concurrencyGroup, groupContext);
assert.equal(ordinaryGroup, "unity-tests-refs/heads/feature");
groupContext.inputs["native-readback-run-id"] = "39132725629";
const firstReadbackGroup = renderGroup(concurrencyGroup, groupContext);
groupContext.github.run_id = "101";
assert.notEqual(firstReadbackGroup, ordinaryGroup);
assert.notEqual(renderGroup(concurrencyGroup, groupContext), firstReadbackGroup);
const historicalSteps = historicalJob.split(/(?=^      - name:)/m);
function historicalStep(name) {
  const matches = historicalSteps.filter((step) => step.startsWith(`      - name: ${name}\n`));
  assert.equal(matches.length, 1);
  return matches[0];
}
for (const [name, id] of [
  ["Download original latest acceptance evidence", "acceptance-id"],
  ["Download original latest standalone evidence", "standalone-id"]
]) {
  const step = historicalStep(name);
  assert.match(step, /actions\/download-artifact@9000827ccba6bdab643e8b6fd33ac0654aef8333/);
  assert.match(step, /repository: \$\{\{ github.repository \}\}/);
  assert.match(step, /run-id: \$\{\{ inputs.native-readback-run-id \}\}/);
  assert.ok(step.includes("artifact-ids: ${{ steps.readback_selection.outputs." + id + " }}"));
  assert.doesNotMatch(step, /^          (?:name|pattern):/m);
  assert.equal(
    predicate(step)({ cancelled: false, steps: { readback_selection: { outcome: "success" } } }),
    true
  );
  assert.equal(
    predicate(step)({ cancelled: false, steps: { readback_selection: { outcome: "failure" } } }),
    false
  );
}
const uploadHistorical = historicalStep("Upload historical readback diagnostics");
for (const outcome of ["success", "failure", "skipped", "cancelled"]) {
  for (const redaction of ["success", "failure", "skipped"]) {
    assert.equal(
      predicate(uploadHistorical)({
        steps: {
          redact_native_readback: { outcome: redaction },
          readback_standalone_input: { outcome }
        }
      }),
      redaction === "success"
    );
  }
}
assert.match(uploadHistorical, /path: .artifacts\/native-readback\/output/);
assert.match(
  historicalStep("Read historical standalone process and package sources"),
  /-TestFrameworkLogPath '.artifacts\/native-readback\/input\/standalone\/unity.log'/
);
assert.match(
  historicalStep("Resolve exact historical artifact identities"),
  /-SourceRunId \$env:UH_READBACK_RUN_ID -Repository \$env:UH_READBACK_REPOSITORY/
);
const preflightBlock = workflow.match(/^  runner-preflight:\n([\s\S]*?)(?=^  [\w-]+:\n)/m)?.[1];
const requiredLabels = expression(
  preflightBlock.match(/^          required-label-sets: (.+)$/m)?.[1]
);
for (const event of ["push", "pull_request", "workflow_dispatch"]) {
  for (const request of ["", "39132725629"]) {
    const labels = JSON.parse(
      requiredLabels({
        github: { event_name: event },
        inputs: { "native-readback-run-id": request }
      })
    );
    assert.deepEqual(labels, [
      [
        "self-hosted",
        "Windows",
        "RAM-64GB",
        ...(event === "workflow_dispatch" && request !== "" ? ["fast"] : [])
      ]
    ]);
  }
}
const registeredWindowsRunners = [
  { name: "DAD-MACHINE", labels: ["self-hosted", "X64", "Windows", "RAM-64GB"] },
  { name: "ELI-MACHINE", labels: ["self-hosted", "X64", "Windows", "RAM-64GB", "fast"] }
];
const historicalLabels = JSON.parse(
  requiredLabels({
    github: { event_name: "workflow_dispatch" },
    inputs: { "native-readback-run-id": "39132725629" }
  })
)[0];
assert.deepEqual(
  registeredWindowsRunners
    .filter((runner) => historicalLabels.every((label) => runner.labels.includes(label)))
    .map((runner) => runner.name),
  ["ELI-MACHINE"]
);
assert.match(
  historicalStep("Validate historical readback selection"),
  /if \(\[Environment\]::MachineName -ine 'ELI-MACHINE'\) \{ throw 'Historical readback must run on ELI-MACHINE\.' \}/
);
const preflightGroup = foldedGroup(preflightBlock, 6);
groupContext.github.workflow = "Unity Tests";
groupContext.inputs["native-readback-run-id"] = "";
const normalPreflightGroup = renderGroup(preflightGroup, groupContext);
groupContext.inputs["native-readback-run-id"] = "39132725629";
assert.notEqual(renderGroup(preflightGroup, groupContext), normalPreflightGroup);
assert.equal(normalPreflightGroup, "Unity Tests-runner-preflight-refs/heads/feature");
for (const event of ["push", "pull_request", "workflow_dispatch"]) {
  for (const request of ["", "39132725629"]) {
    const context = {
      github: {
        event_name: event,
        workflow: "Unity Tests",
        sha: "source-sha",
        ref: "refs/heads/feature",
        run_id: "456",
        event: { pull_request: event === "pull_request" ? { number: 42 } : {} }
      },
      inputs: { "native-readback-run-id": request }
    };
    const suffix = event === "workflow_dispatch" && request !== "" ? "-native-readback-456" : "";
    assert.equal(
      renderGroup(concurrencyGroup, context),
      "unity-tests-" +
        (event === "push" ? "source-sha" : event === "pull_request" ? "42" : "refs/heads/feature") +
        suffix
    );
    assert.equal(
      renderGroup(preflightGroup, context),
      "Unity Tests-runner-preflight-" +
        (event === "push" ? "source-sha" : "refs/heads/feature") +
        suffix
    );
  }
}
assert.doesNotMatch(historicalJob, /continue-on-error:/);
for (const [name, input] of [
  ["Read historical acceptance process evidence", "readback_acceptance_input"],
  ["Read historical standalone process and package sources", "readback_standalone_input"]
]) {
  const step = historicalStep(name);
  assert.match(step, /-ExpectedMachine 'ELI-MACHINE'/);
  for (const cancelled of [false, true]) {
    for (const download of ["success", "failure", "skipped", "cancelled"]) {
      for (const idle of ["success", "failure", "skipped"]) {
        assert.equal(
          predicate(step)({
            cancelled,
            steps: { [input]: { outcome: download }, readback_host_idle: { outcome: idle } }
          }),
          !cancelled && download === "success" && idle === "success"
        );
      }
    }
  }
}
console.log(`${readbackJobControls} historical readback job predicate controls passed.`);
