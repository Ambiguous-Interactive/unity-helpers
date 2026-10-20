"use strict";

// cspell:ignore ine

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const workflow = fs
  .readFileSync(path.join(__dirname, "../../.github/workflows/unity-tests.yml"), "utf8")
  .replace(/\r\n/g, "\n");
const directOption = workflow.match(
  /^      native-build-direct-control:\n((?:        [^\n]*\n|\n)+)/m
)?.[1];
assert.ok(directOption, "Manual Direct build control input must exist");
assert.match(directOption, /^        default: false$/m);
assert.match(directOption, /^        type: boolean$/m);
const heapOption = workflow.match(
  /^      native-debugger-standard-heap:\n((?:        [^\n]*\n|\n)+)/m
)?.[1];
assert.ok(heapOption, "Manual standard heap control input must exist");
assert.match(heapOption, /^        default: false$/m);
assert.match(heapOption, /^        type: boolean$/m);
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
const acceptanceNode = identified("acceptance_node");
assert.ok(steps.indexOf(acceptanceNode) < steps.indexOf(run));
assert.match(acceptanceNode, /uses: actions\/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1/);
assert.match(acceptanceNode, /node-version: "22\.18\.0"/);
assert.doesNotMatch(acceptanceNode, /continue-on-error: true/);
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
                acceptance_node: { outcome: "success" },
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
            assert.equal(
              Boolean(predicate(acceptanceNode)(context)),
              requested && acceptance === "wproto" && !cancelled && acquired === "true"
            );
            for (const outcome of ["failure", "skipped", "cancelled"]) {
              const failedSetup = {
                ...context,
                steps: { ...context.steps, acceptance_node: { outcome } }
              };
              assert.equal(
                Boolean(predicate(run)(failedSetup)),
                expected[0] && acceptance !== "wproto"
              );
            }
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
            controls += predicates.length + 5;
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
    .length <= 25
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

const nativeBuild = identified("native_build_capture");
const nativeBuildPython = identified("native_build_python");
assert.ok(steps.indexOf(acceptanceNode) < steps.indexOf(nativeBuildPython));
assert.ok(steps.indexOf(nativeBuildPython) < steps.indexOf(nativeBuild));
assert.ok(steps.indexOf(identified("run_standalone")) < steps.indexOf(nativeBuild));
assert.match(nativeBuild, /-StandaloneBuildCapture \$env:UH_NATIVE_BUILD_CAPTURE_MODE/);
assert.match(nativeBuild, /-StandaloneBuildCaptureBackend 'IL2CPP'/);
assert.match(
  nativeBuild,
  /UH_NATIVE_DEBUGGER_STANDARD_HEAP: \$\{\{ inputs.native-debugger-standard-heap \}\}/
);
assert.match(
  nativeBuild,
  /-StandaloneBuildDebuggerStandardHeap:\(\$env:UH_NATIVE_DEBUGGER_STANDARD_HEAP -eq 'true'\)/
);
assert.match(nativeBuild, /-Acceptance 'wproto' -UnityVersion '6000\.6\.0f1'/);
assert.match(
  nativeBuild,
  /ArtifactsPath '\.artifacts\/unity\/6000\.6\.0f1-standalone\/build-only-diagnostics'/
);
assert.match(nativeBuild, /UH_CENTRAL_LICENSE_RETURN: "true"/);
assert.doesNotMatch(nativeBuild, /continue-on-error|CompareLoaderSnaps/);
assert.match(
  workflow,
  /Standalone native diagnostics require acceptance=none; no timing campaign may run/
);
const captureBuildWhen = predicate(nativeBuild);
const pythonBuildWhen = predicate(nativeBuildPython);
let nativeBuildControls = 0;
for (const event of ["workflow_dispatch", "push", "pull_request"]) {
  for (const mode of ["standalone", "editmode", "playmode", "all"]) {
    for (const acceptance of ["none", "wproto", "all"]) {
      for (const version of ["2021.3.45f1", "2022.3.45f1", "6000.5.2f1", "6000.6.0f1"]) {
        for (const requested of [undefined, false, true]) {
          for (const cancelled of [false, true]) {
            for (const acquired of ["false", "true"]) {
              for (const standalone of ["skipped", "failure", "success"]) {
                for (const setup of ["skipped", "failure", "success"]) {
                  const context = {
                    github: { event_name: event },
                    inputs: { "native-diagnostics": requested, "test-mode": mode, acceptance },
                    matrix: { "unity-version": version },
                    cancelled,
                    steps: {
                      unity_lock: { outputs: { acquired } },
                      run_standalone: { outcome: standalone },
                      native_build_python: { outcome: setup },
                      acceptance_node: { outcome: setup }
                    }
                  };
                  const selected =
                    event === "workflow_dispatch" &&
                    requested === true &&
                    mode === "standalone" &&
                    acceptance === "none" &&
                    version === "6000.6.0f1" &&
                    !cancelled &&
                    acquired === "true" &&
                    standalone === "success";
                  assert.equal(Boolean(pythonBuildWhen(context)), selected);
                  assert.equal(Boolean(captureBuildWhen(context)), selected && setup === "success");
                  nativeBuildControls += 2;
                }
              }
            }
          }
        }
      }
    }
  }
}
console.log(
  `${nativeBuildControls} build-only native diagnostic predicate and wiring controls passed.`
);

const { getDirectBuildSelectionError } = require("../unity/validate-native-build-direct-selection");
const vm = require("node:vm");
const directHelperPath = path.join(__dirname, "../unity/validate-native-build-direct-selection.js");
const directHelperSource = fs.readFileSync(directHelperPath, "utf8");
const matrixConfig = workflow.match(/^  matrix-config:\n([\s\S]*?)(?=^  [\w-]+:\n)/m)?.[1];
assert.ok(matrixConfig);
const directSelection = matrixConfig
  .split(/(?=^      - name:)/m)
  .find((step) => step.startsWith("      - name: Validate Standalone Direct control selection\n"));
assert.ok(directSelection, "Hosted Direct selection must precede licensed jobs");
assert.match(
  directSelection,
  /run: node scripts\/unity\/validate-native-build-direct-selection\.js/
);
assert.ok(
  matrixConfig.indexOf("uses: actions/setup-node@") < matrixConfig.indexOf(directSelection)
);
assert.match(directSelection, /UH_NATIVE_DIAGNOSTICS: \$\{\{ inputs.native-diagnostics \}\}/);
assert.match(directSelection, /UH_NATIVE_MODE: \$\{\{ inputs.test-mode \}\}/);
assert.match(directSelection, /UH_NATIVE_ACCEPTANCE: \$\{\{ inputs.acceptance \}\}/);
for (const event of ["workflow_dispatch", "pull_request", "push", "schedule"]) {
  for (const direct of [false, true]) {
    for (const cancelled of [false, true]) {
      assert.equal(
        predicate(directSelection)({
          cancelled,
          github: { event_name: event },
          inputs: { "native-build-direct-control": direct }
        }),
        !cancelled && event === "workflow_dispatch" && direct
      );
    }
  }
}
const captureModeExpression = nativeBuild.match(
  /^          UH_NATIVE_BUILD_CAPTURE_MODE: (.+)$/m
)?.[1];
assert.ok(captureModeExpression);
for (const direct of [undefined, false, true]) {
  assert.equal(
    expression(captureModeExpression)({ inputs: { "native-build-direct-control": direct } }),
    direct === true ? "Direct" : "Debugger"
  );
}
for (const absent of [null, undefined]) {
  assert.notEqual(getDirectBuildSelectionError(absent), null);
}
const directSelections = [];
for (const diagnostics of ["", "false", "true", "invalid"]) {
  for (const mode of ["", "editmode", "standalone", "all"]) {
    for (const acceptance of ["", "none", "wproto", "all"]) {
      const environment = {
        UH_NATIVE_DIAGNOSTICS: diagnostics,
        UH_NATIVE_MODE: mode,
        UH_NATIVE_ACCEPTANCE: acceptance
      };
      const permitted = diagnostics === "true" && mode === "standalone" && acceptance === "none";
      assert.equal(getDirectBuildSelectionError(environment) === null, permitted);
      directSelections.push({ environment, permitted });
    }
  }
}
for (const field of ["UH_NATIVE_DIAGNOSTICS", "UH_NATIVE_MODE", "UH_NATIVE_ACCEPTANCE"]) {
  const patterns = {
    UH_NATIVE_DIAGNOSTICS: /environment\.UH_NATIVE_DIAGNOSTICS !== "true"\s*\|\|\s*/,
    UH_NATIVE_MODE: /environment\.UH_NATIVE_MODE !== "standalone"\s*\|\|\s*/,
    UH_NATIVE_ACCEPTANCE: /\|\|\s*environment\.UH_NATIVE_ACCEPTANCE !== "none"/
  };
  const pattern = patterns[field];
  const removed = directHelperSource.replace(pattern, "");
  assert.notEqual(
    removed,
    directHelperSource,
    `The ${field} control must change the actual helper`
  );
  const sandbox = { module: { exports: {} }, require, console, process };
  vm.runInNewContext(removed, sandbox, { filename: directHelperPath });
  const mismatches = directSelections.filter(
    ({ environment, permitted }) =>
      (sandbox.module.exports.getDirectBuildSelectionError(environment) === null) !== permitted
  );
  assert.equal(
    mismatches.length,
    3,
    `Removing ${field} must fail its three exact selection controls`
  );
}
for (const [diagnostics, mode, acceptance, exitCode] of [
  ["true", "standalone", "none", 0],
  ["false", "standalone", "none", 1],
  ["true", "editmode", "none", 1],
  ["true", "standalone", "wproto", 1]
]) {
  const environment = {
    UH_NATIVE_DIAGNOSTICS: diagnostics,
    UH_NATIVE_MODE: mode,
    UH_NATIVE_ACCEPTANCE: acceptance
  };
  const result = spawnSync(process.execPath, [directHelperPath], {
    env: { ...process.env, ...environment },
    encoding: "utf8"
  });
  assert.equal(result.status, exitCode, result.stderr);
}
console.log(
  `${directSelections.length} Direct build selection controls, three removed-guard controls, CLI and workflow wiring passed.`
);

const heapSelection = matrixConfig.match(
  /- name: Validate Standalone debugger heap selection\n[\s\S]*?(?=\n      - name:)/
)?.[0];
assert.ok(heapSelection);
assert.match(heapSelection, /run: node scripts\/unity\/validate-native-build-heap-selection\.js/);
for (const [field, input] of [
  ["UH_NATIVE_DIAGNOSTICS", "native-diagnostics"],
  ["UH_NATIVE_MODE", "test-mode"],
  ["UH_NATIVE_ACCEPTANCE", "acceptance"],
  ["UH_NATIVE_DIRECT_CONTROL", "native-build-direct-control"]
]) {
  assert.ok(heapSelection.includes(field + ": ${{ inputs." + input + " }}"));
}
for (const event of ["workflow_dispatch", "pull_request", "push", "schedule"]) {
  for (const heap of [false, true]) {
    for (const cancelled of [false, true]) {
      assert.equal(
        predicate(heapSelection)({
          cancelled,
          github: { event_name: event },
          inputs: { "native-debugger-standard-heap": heap }
        }),
        !cancelled && event === "workflow_dispatch" && heap
      );
    }
  }
}
const heapHelperPath = path.join(__dirname, "../unity/validate-native-build-heap-selection.js");
const heapHelperSource = fs.readFileSync(heapHelperPath, "utf8");
const { getStandardHeapSelectionError } = require(heapHelperPath);
const heapSelections = [];
for (const diagnostics of ["", "false", "true", "invalid"]) {
  for (const mode of ["", "editmode", "standalone", "all"]) {
    for (const acceptance of ["", "none", "wproto", "all"]) {
      for (const direct of ["", "false", "true", "invalid"]) {
        const environment = {
          UH_NATIVE_DIAGNOSTICS: diagnostics,
          UH_NATIVE_MODE: mode,
          UH_NATIVE_ACCEPTANCE: acceptance,
          UH_NATIVE_DIRECT_CONTROL: direct
        };
        const permitted =
          diagnostics === "true" &&
          mode === "standalone" &&
          acceptance === "none" &&
          direct === "false";
        assert.equal(getStandardHeapSelectionError(environment) === null, permitted);
        heapSelections.push({ environment, permitted });
      }
    }
  }
}
for (const missing of [null, undefined, {}]) {
  assert.notEqual(getStandardHeapSelectionError(missing), null);
}
for (const [field, value] of [
  ["UH_NATIVE_DIAGNOSTICS", "true"],
  ["UH_NATIVE_MODE", "standalone"],
  ["UH_NATIVE_ACCEPTANCE", "none"],
  ["UH_NATIVE_DIRECT_CONTROL", "false"]
]) {
  const withoutGuard = heapHelperSource.replace(`environment.${field} !== "${value}"`, "false");
  assert.notEqual(withoutGuard, heapHelperSource);
  const sandbox = { module: { exports: {} }, require, console, process };
  vm.runInNewContext(withoutGuard, sandbox, { filename: heapHelperPath });
  assert.equal(
    heapSelections.filter(
      ({ environment, permitted }) =>
        (sandbox.module.exports.getStandardHeapSelectionError(environment) === null) !== permitted
    ).length,
    3
  );
}
for (const [direct, expected] of [
  ["false", 0],
  ["true", 1],
  ["", 1]
]) {
  const result = spawnSync(process.execPath, [heapHelperPath], {
    env: {
      ...process.env,
      UH_NATIVE_DIAGNOSTICS: "true",
      UH_NATIVE_MODE: "standalone",
      UH_NATIVE_ACCEPTANCE: "none",
      UH_NATIVE_DIRECT_CONTROL: direct
    },
    encoding: "utf8"
  });
  assert.equal(result.status, expected, result.stderr);
}
console.log(
  `${heapSelections.length} standard heap admission controls and four removed-guard controls passed.`
);

const attachOption = workflow.match(
  /^      native-build-attach-control:\n((?:        [^\n]*\n|\n)+)/m
)[1];
assert.match(attachOption, /^        default: false$/m);
assert.match(attachOption, /^        type: boolean$/m);
const attachSelection = matrixConfig.match(
  /- name: Validate Standalone debugger attachment selection\n[\s\S]*?(?=\n      - name:)/
)[0];
const attachFields = [
  ["UH_NATIVE_DIAGNOSTICS", "native-diagnostics", "true"],
  ["UH_NATIVE_MODE", "test-mode", "standalone"],
  ["UH_NATIVE_ACCEPTANCE", "acceptance", "none"],
  ["UH_NATIVE_DIRECT_CONTROL", "native-build-direct-control", "false"],
  ["UH_NATIVE_STANDARD_HEAP", "native-debugger-standard-heap", "false"]
];
for (const [field, input] of attachFields) {
  assert.ok(attachSelection.includes(`${field}: \u0024{{ inputs.${input} }}`));
}
assert.match(
  attachSelection,
  /run: node scripts\/unity\/validate-native-build-attach-selection\.js/
);
assert.match(
  nativeBuild,
  /UH_NATIVE_ATTACH_CONTROL: \$\{\{ inputs.native-build-attach-control \}\}/
);
assert.match(
  nativeBuild,
  /-StandaloneBuildAttachToDirect:\(\$env:UH_NATIVE_ATTACH_CONTROL -eq 'true'\)/
);
for (const event of ["workflow_dispatch", "pull_request", "push", "schedule"]) {
  for (const attach of [false, true]) {
    for (const cancelled of [false, true]) {
      for (const startup of [false, true]) {
        assert.equal(
          predicate(attachSelection)({
            cancelled,
            github: { event_name: event },
            inputs: {
              "native-build-attach-control": attach,
              "native-build-attach-after-startup": startup,
              "native-build-attach-after-shutdown": false
            }
          }),
          !cancelled && event === "workflow_dispatch" && (attach || startup)
        );
      }
    }
  }
}
const attachHelperPath = path.join(__dirname, "../unity/validate-native-build-attach-selection.js");
const attachHelperSource = fs.readFileSync(attachHelperPath, "utf8");
const { getAttachSelectionError } = require(attachHelperPath);
const attachSelections = [];
for (let mask = 0; mask < 1024; ++mask) {
  const environment = {};
  let cursor = mask;
  for (const [field, , valid] of attachFields) {
    environment[field] = [valid, "", "invalid", valid === "true" ? "false" : "true"][cursor % 4];
    cursor = Math.floor(cursor / 4);
  }
  const permitted = mask === 0;
  assert.equal(getAttachSelectionError(environment) === null, permitted);
  attachSelections.push({ environment, permitted });
}
for (const missing of [null, undefined, {}]) {
  assert.notEqual(getAttachSelectionError(missing), null);
}
for (const [field, , value] of attachFields) {
  const withoutGuard = attachHelperSource.replace(`environment.${field} !== "${value}"`, "false");
  assert.notEqual(withoutGuard, attachHelperSource);
  const sandbox = { module: { exports: {} }, require, console, process };
  vm.runInNewContext(withoutGuard, sandbox, { filename: attachHelperPath });
  assert.equal(
    attachSelections.filter(
      ({ environment, permitted }) =>
        (sandbox.module.exports.getAttachSelectionError(environment) === null) !== permitted
    ).length,
    3
  );
}
for (const [selection, code] of [
  [attachSelections[0], 0],
  [attachSelections[1], 1],
  [attachSelections[256], 1]
]) {
  const result = spawnSync(process.execPath, [attachHelperPath], {
    env: { ...process.env, ...selection.environment },
    encoding: "utf8"
  });
  assert.equal(result.status, code, result.stderr);
}
console.log(
  `${attachSelections.length} attachment admission controls and five removed-guard controls passed.`
);

const startupOption = workflow.match(
  /^      native-build-attach-after-startup:\n((?:        [^\n]*\n|\n)+)/m
)[1];
assert.match(startupOption, /^        default: false$/m);
assert.match(startupOption, /^        type: boolean$/m);
for (const [field, input] of [
  ["UH_NATIVE_ATTACH_CONTROL", "native-build-attach-control"],
  ["UH_NATIVE_ATTACH_AFTER_STARTUP", "native-build-attach-after-startup"]
]) {
  assert.ok(attachSelection.includes(`${field}: \u0024{{ inputs.${input} }}`));
}
assert.match(
  nativeBuild,
  /UH_NATIVE_ATTACH_AFTER_STARTUP: \$\{\{ inputs.native-build-attach-after-startup \}\}/
);
assert.match(
  nativeBuild,
  /-StandaloneBuildAttachAfterStartup:\(\$env:UH_NATIVE_ATTACH_AFTER_STARTUP -eq 'true'\)/
);
const { getStartupAttachSelectionError } = require(attachHelperPath);
const startupSelections = [];
for (const selection of attachSelections) {
  for (const attach of ["true", "false", "", "invalid"]) {
    for (const startup of ["true", "false", "", "invalid"]) {
      const environment = {
        ...selection.environment,
        UH_NATIVE_ATTACH_CONTROL: attach,
        UH_NATIVE_ATTACH_AFTER_STARTUP: startup
      };
      const permitted = selection.permitted && attach === "true" && startup === "true";
      assert.equal(getStartupAttachSelectionError(environment) === null, permitted);
      startupSelections.push({ environment, permitted });
    }
  }
}
for (const [field, value] of [
  ["UH_NATIVE_ATTACH_CONTROL", "true"],
  ["UH_NATIVE_ATTACH_AFTER_STARTUP", "true"]
]) {
  const mutant = attachHelperSource.replace(`environment.${field} !== "${value}"`, "false");
  assert.notEqual(mutant, attachHelperSource);
  const sandbox = { module: { exports: {} }, require, console, process };
  vm.runInNewContext(mutant, sandbox, { filename: attachHelperPath });
  assert.equal(
    startupSelections.filter(
      ({ environment, permitted }) =>
        (sandbox.module.exports.getStartupAttachSelectionError(environment) === null) !== permitted
    ).length,
    3
  );
}
const delegateMutant = attachHelperSource.replace(
  "getAttachSelectionError(environment) !== null",
  "false"
);
assert.notEqual(delegateMutant, attachHelperSource);
const delegateSandbox = { module: { exports: {} }, require, console, process };
vm.runInNewContext(delegateMutant, delegateSandbox, { filename: attachHelperPath });
assert.equal(
  startupSelections.filter(
    ({ environment, permitted }) =>
      (delegateSandbox.module.exports.getStartupAttachSelectionError(environment) === null) !==
      permitted
  ).length,
  1023
);
for (const [attach, startup, expected] of [
  ["true", "true", 0],
  ["false", "true", 1],
  ["true", "false", 0],
  ["true", "invalid", 1]
]) {
  const result = spawnSync(process.execPath, [attachHelperPath], {
    env: {
      ...process.env,
      ...attachSelections[0].environment,
      UH_NATIVE_ATTACH_CONTROL: attach,
      UH_NATIVE_ATTACH_AFTER_STARTUP: startup
    },
    encoding: "utf8"
  });
  assert.equal(result.status, expected, result.stderr);
}
console.log(
  `${startupSelections.length} startup attachment admissions and three removed-guard controls passed.`
);

const shutdownOption = workflow.match(
  /^      native-build-attach-after-shutdown:\n((?:        [^\n]*\n|\n)+)/m
)[1];
assert.match(shutdownOption, /^        default: false$/m);
assert.match(shutdownOption, /^        type: boolean$/m);
assert.ok(
  attachSelection.includes(
    "UH_NATIVE_ATTACH_AFTER_SHUTDOWN: ${{ inputs.native-build-attach-after-shutdown }}"
  )
);
assert.ok(
  nativeBuild.includes(
    "UH_NATIVE_ATTACH_AFTER_SHUTDOWN: ${{ inputs.native-build-attach-after-shutdown }}"
  )
);
assert.ok(
  nativeBuild.includes(
    "-StandaloneBuildAttachAfterShutdown:($env:UH_NATIVE_ATTACH_AFTER_SHUTDOWN -eq 'true')"
  )
);
const { getShutdownAttachSelectionError } = require(attachHelperPath);
const shutdownSelections = [];
for (const selection of startupSelections) {
  for (const shutdown of ["true", "false", "", "invalid"]) {
    const environment = { ...selection.environment, UH_NATIVE_ATTACH_AFTER_SHUTDOWN: shutdown };
    const permitted =
      getAttachSelectionError(environment) === null &&
      environment.UH_NATIVE_ATTACH_CONTROL === "true" &&
      environment.UH_NATIVE_ATTACH_AFTER_STARTUP === "false" &&
      shutdown === "true";
    assert.equal(getShutdownAttachSelectionError(environment) === null, permitted);
    assert.equal(
      getStartupAttachSelectionError(environment) === null,
      selection.permitted && shutdown === "false"
    );
    shutdownSelections.push({ environment, permitted });
  }
}
const shutdownFunctionStart = attachHelperSource.indexOf(
  "function getShutdownAttachSelectionError"
);
for (const condition of [
  "getAttachSelectionError(environment) !== null",
  'environment.UH_NATIVE_ATTACH_CONTROL !== "true"',
  'environment.UH_NATIVE_ATTACH_AFTER_STARTUP !== "false"',
  'environment.UH_NATIVE_ATTACH_AFTER_SHUTDOWN !== "true"'
]) {
  const before = attachHelperSource.slice(0, shutdownFunctionStart);
  const after = attachHelperSource.slice(shutdownFunctionStart);
  const removed = after.replace(condition, "false");
  assert.notEqual(after, removed);
  const sandbox = { module: { exports: {} }, require, console, process };
  vm.runInNewContext(before + removed, sandbox, { filename: attachHelperPath });
  assert.ok(
    shutdownSelections.some(
      ({ environment, permitted }) =>
        (sandbox.module.exports.getShutdownAttachSelectionError(environment) === null) !== permitted
    )
  );
}
for (const event of ["workflow_dispatch", "push", "pull_request"]) {
  for (const cancelled of [true, false]) {
    for (const attach of [true, false]) {
      for (const startup of [true, false]) {
        for (const shutdown of [true, false]) {
          assert.equal(
            predicate(attachSelection)({
              cancelled,
              github: { event_name: event },
              inputs: {
                "native-build-attach-control": attach,
                "native-build-attach-after-startup": startup,
                "native-build-attach-after-shutdown": shutdown
              }
            }),
            !cancelled && event === "workflow_dispatch" && (attach || startup || shutdown)
          );
        }
      }
    }
  }
}
for (const [startup, shutdown, expected] of [
  ["false", "true", 0],
  ["true", "true", 1],
  ["false", "invalid", 1],
  ["true", "false", 0]
]) {
  const result = spawnSync(process.execPath, [attachHelperPath], {
    env: {
      ...process.env,
      ...attachSelections[0].environment,
      UH_NATIVE_ATTACH_CONTROL: "true",
      UH_NATIVE_ATTACH_AFTER_STARTUP: startup,
      UH_NATIVE_ATTACH_AFTER_SHUTDOWN: shutdown
    },
    encoding: "utf8"
  });
  assert.equal(result.status, expected, result.stderr);
}
console.log(
  `${shutdownSelections.length} final telemetry attachment admissions and four removed guards passed.`
);
