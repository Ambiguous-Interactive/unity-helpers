#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Executable controls for Docker and PowerShell stream redaction.
//
// All credential-shaped values below are synthetic.

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const yaml = require("yaml");

const repoRoot = path.resolve(__dirname, "..", "..");
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

const temporaryRoots = [];

function temporaryDirectory() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "redact-unity-artifacts-test-"));
  temporaryRoots.push(root);
  return root;
}

/** A miniature artifact tree: one clean log, two nested leaking logs, and a binary blob. */
const ACCESS_TOKEN_CASES = Object.freeze([
  [
    "overlapping serial shape",
    "[Licensing::Module] Access token: SYNTHETIC-SC-FAKE-FAKE-FAKE-FAKE-FAKE-private-tail",
    true
  ],
  [
    "full token",
    "[Licensing::Module] Successfully updated the access token SYNTHETIC-FULL.TOKEN_0123456789+/=",
    true
  ],
  [
    "preview",
    "[Licensing::Module] Successfully updated the access token SYNTHETIC-PREVIEW...",
    true
  ],
  ["short preview", "[Licensing::Module] Successfully updated the access token SYN...", true],
  ["colon label", "[Licensing::Module] Access token: SYNTHETIC-PREVIEW...", true],
  ["quoted assignment", "[Licensing::Module] access token = 'SYNTHETIC-FULL.TOKEN'", true],
  [
    "mixed case",
    "[Licensing::Module] Successfully Updated The Access Token SYNTHETIC-PREVIEW...",
    true
  ],
  [
    "trailing diagnostic",
    "[Licensing::Module] Successfully updated the access token SYNTHETIC-PREVIEW... (expires in 60 seconds)",
    true
  ],
  [
    "redacted value",
    "[Licensing::Module] Successfully updated the access token <redacted:unity-access-token>",
    false
  ],
  [
    "Docker redacted value",
    "[Licensing::Module] Successfully updated the access token [REDACTED-UNITY-ACCESS-TOKEN]",
    false
  ],
  ["masked value", "[Licensing::Module] Successfully updated the access token ***", false],
  ["missing value", "[Licensing::Module] Successfully updated the access token", false],
  ["empty colon value", "[Licensing::Module] Access token: \t", false],
  [
    "newline after empty value",
    "[Licensing::Module] Successfully updated the access token \r\nerror CS1234: failure preserved",
    false
  ],
  [
    "failure diagnostic",
    "[Licensing::Module] Error: Access token is unavailable; Code 20111",
    false
  ]
]);

function assertTokenCases(redact, runtime) {
  for (const [label, input, sensitive] of ACCESS_TOKEN_CASES) {
    const output = redact(input);
    assert.ok(
      !output.includes("SYNTHETIC") && !output.includes("SYN..."),
      `${runtime}/${label}: token bytes survived`
    );
    assert.ok(
      output.startsWith("[Licensing::Module]"),
      `${runtime}/${label}: diagnostic module lost`
    );
    assert.ok(redact(output) === output, `${runtime}/${label}: second pass changed output`);
    if (sensitive) {
      const expected = input.replace(
        /(?:SYNTHETIC|SYN)[A-Za-z0-9._~+/=-]*/g,
        "<redacted:unity-access-token>"
      );
      assert.ok(output === expected, `${runtime}/${label}: credential tail or label changed`);
    }
    if (!sensitive) {
      assert.ok(output === input, `${runtime}/${label}: clean diagnostic changed`);
    }
    if (input.includes("(expires")) {
      assert.ok(
        output.endsWith("(expires in 60 seconds)"),
        `${runtime}/${label}: expiry diagnostic lost`
      );
    }
  }
}

runTest("Docker stream redaction destroys Unity token bytes and preserves diagnostics", () => {
  const source = fs.readFileSync(path.join(repoRoot, "scripts/unity/run-unity-docker.sh"), "utf8");
  const match = source.match(/redact_unity_license_output\(\) \{[\s\S]*?\n\}/);
  assert.ok(match, "Docker redactor function must exist");
  const script = match[0].replaceAll("'\"'\"'", "'") + "\nredact_unity_license_output\n";
  assertTokenCases((input) => {
    const result = spawnSync("bash", ["-c", script], { input, encoding: "utf8" });
    assert.ok(result.status === 0, "Docker redactor process failed");
    assert.ok(result.stderr === "", "Docker redactor must not echo values to stderr");
    return result.stdout;
  }, "Docker");
});

runTest("PowerShell stream redaction destroys Unity token bytes and preserves diagnostics", () => {
  const helper = path.join(repoRoot, "scripts/unity/lib/credential-redaction.ps1");
  assert.ok(fs.existsSync(helper), "PowerShell redaction helper must exist");
  const script = `. '${helper.replaceAll("'", "''")}'; $lines = [Console]::In.ReadToEnd() | ConvertFrom-Json; @($lines | ForEach-Object { $once = ConvertTo-UnitySafeLogText -Text $_; [pscustomobject]@{ once = $once; twice = (ConvertTo-UnitySafeLogText -Text $once) } }) | ConvertTo-Json -Compress`;
  const inputs = ACCESS_TOKEN_CASES.map(([, input]) => input);
  const result = spawnSync("pwsh", ["-NoProfile", "-Command", script], {
    input: JSON.stringify(inputs),
    encoding: "utf8"
  });
  assert.ok(result.status === 0, "PowerShell redactor process failed");
  assert.ok(result.stderr === "", "PowerShell redactor must not echo values to stderr");
  assert.ok(
    !result.stdout.includes("SYNTHETIC") && !result.stdout.includes("SYN..."),
    "PowerShell CLI output contains token bytes"
  );
  const outputs = JSON.parse(result.stdout);
  let index = 0;
  assertTokenCases((input) => {
    const originalIndex = inputs.indexOf(input);
    if (originalIndex !== -1) {
      index = originalIndex;
      return outputs[index].once;
    }
    return outputs[index].twice;
  }, "PowerShell");
});

for (const actionName of ["dump-unity-log-tail", "verify-unity-results"]) {
  runTest(`${actionName} hides credentials in tails and catastrophic annotations`, () => {
    const root = temporaryDirectory();
    const input =
      ACCESS_TOKEN_CASES.map(([, line]) => line).join("\n") +
      "\nerror CS1234: [Licensing::Module] Access token: SYNTHETIC-PREVIEW... failure preserved\n";
    for (const file of ["unity.log", "player.log"]) {
      fs.writeFileSync(path.join(root, file), input);
    }
    const action = yaml.parse(
      fs.readFileSync(path.join(repoRoot, ".github/actions", actionName, "action.yml"), "utf8")
    );
    const script = action.runs.steps
      .find((step) => step.run)
      .run.replaceAll("${{ inputs.results-dir }}", root.replaceAll("\\", "/"))
      .replaceAll("${{ inputs.label }}", "redaction regression")
      .replaceAll("${{ inputs.tail-lines }}", "200");
    const scriptPath = path.join(root, "action.ps1");
    fs.writeFileSync(scriptPath, script);
    const result = spawnSync("pwsh", ["-NoProfile", "-File", scriptPath], {
      encoding: "utf8",
      cwd: repoRoot,
      env: {
        ...process.env,
        GITHUB_WORKSPACE: repoRoot,
        UH_RESULTS_DIR: root,
        UH_LABEL: "redaction regression",
        UH_EXPECTED_EMPTY: "false"
      }
    });
    const output = `${result.stdout}${result.stderr}`;
    assert.ok(
      result.status === (actionName === "dump-unity-log-tail" ? 0 : 1),
      "diagnostic action returned an unexpected status"
    );
    assert.ok(
      !output.includes("SYNTHETIC") && !output.includes("SYN..."),
      "diagnostic action leaked credential bytes"
    );
    assert.ok(
      output.includes("failure preserved") && output.includes("error CS1234"),
      "catastrophic diagnostic disappeared"
    );
    assert.ok(
      output.includes("redacted:unity-access-token"),
      "diagnostic action never exercised a redaction"
    );
  });
}

runTest(
  "PowerShell process streams and standalone log tails redact credentials without hiding failures",
  () => {
    const root = temporaryDirectory();
    const child = path.join(root, "child.ps1");
    fs.writeFileSync(
      child,
      "[Console]::Out.WriteLine('[Licensing::Module] Access token: SYNTHETIC-STDOUT...'); [Console]::Error.WriteLine('[Licensing::Module] Access token: SYNTHETIC-STDERR...'); [Console]::Out.WriteLine('error CS1234: failure preserved'); exit 42"
    );
    const script = `
$ErrorActionPreference = 'Stop'
. (Join-Path $env:GITHUB_WORKSPACE 'scripts/unity/lib/credential-redaction.ps1')
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $env:GITHUB_WORKSPACE 'scripts/unity/run-ci-tests.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'runner parse failure' }
foreach ($definition in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
  Invoke-Expression $definition.Extent.Text
}
. (Join-Path $env:GITHUB_WORKSPACE 'scripts/unity/lib/catastrophic-patterns.ps1')
$script:CatastrophicPatterns = @(Get-CatastrophicPatterns)
$nativeCodes = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$script:NativeExitCodeDescriptions' }, $false)
Invoke-Expression $nativeCodes.Extent.Text
function FakeUnityEditor {
    '[Licensing::Module] Access token: SYNTHETIC-NATIVE...'
    'native diagnostic preserved'
    $global:LASTEXITCODE = 0
}
$nativeLog = Join-Path $env:REDACTION_ROOT 'native.log'
$nativeResult = Invoke-UnityEditor -EditorPath 'FakeUnityEditor' -Arguments @('-quit') -LogPath $nativeLog -Label 'native regression'
if ($nativeResult -ne 0) { throw 'native result changed' }
Invoke-UnityLicenseActivate -EditorPath 'FakeUnityEditor' -Serial 'unused' -Email 'unused' -Password 'unused' -LogPath $nativeLog -RetryBudgetSeconds 0
Invoke-UnityNativeStartupProbe -EditorPath 'FakeUnityEditor' -LogPath $nativeLog
$provisioning = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $env:GITHUB_WORKSPACE 'scripts/unity/ensure-editor.ps1'), [ref]$tokens, [ref]$errors)
$probe = $provisioning.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-UnityNativeStartup' }, $false)
Invoke-Expression $probe.Extent.Text
$probeResult = Test-UnityNativeStartup -EditorPath 'FakeUnityEditor' -LogPath $nativeLog
if (-not $probeResult.Success) { throw 'provisioning probe success changed' }
$setup = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $env:GITHUB_WORKSPACE 'scripts/unity/setup-license.ps1'), [ref]$tokens, [ref]$errors)
foreach ($definition in $setup.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
  Invoke-Expression $definition.Extent.Text
}
$VerboseOutput = $true
$UnityImage = 'unused'
function docker { '[Licensing::Module] Access token: SYNTHETIC-SETUP...'; 'setup failure preserved'; $global:LASTEXITCODE = 1 }
if (Test-ProActivation -Serial 'unused' -Email 'unused' -Password 'unused') { throw 'setup activation failure changed' }
$result = Invoke-ProcessWithTreeKillTimeout -FilePath (Get-Command pwsh).Source -Arguments @('-NoProfile', '-File', $env:REDACTION_CHILD) -TimeoutSeconds 20 -LogPath $env:REDACTION_LOG -Label 'redaction regression'
if ($result.ExitCode -ne 42 -or $result.TimedOut) { throw 'process failure status changed' }
Write-StandaloneBuildOutputDiagnostics -Project $env:REDACTION_ROOT -ExpectedExe (Join-Path $env:REDACTION_ROOT 'missing.exe') -LogPath $env:REDACTION_LOG -BuildStartedUtc ([datetime]::UtcNow)
`;
    const scriptPath = path.join(root, "runner.ps1");
    fs.writeFileSync(scriptPath, script);
    const result = spawnSync("pwsh", ["-NoProfile", "-File", scriptPath], {
      encoding: "utf8",
      cwd: repoRoot,
      env: {
        ...process.env,
        GITHUB_WORKSPACE: repoRoot,
        REDACTION_ROOT: root,
        REDACTION_CHILD: child,
        REDACTION_LOG: path.join(root, "unity.log")
      }
    });
    const output = `${result.stdout}${result.stderr}`;
    assert.ok(result.status === 0, "PowerShell process/tail harness failed");
    assert.ok(!output.includes("SYNTHETIC"), "PowerShell process/tail output leaked token bytes");
    assert.ok(
      output.includes("Build log tail:") && output.includes("failure preserved"),
      "PowerShell process/tail diagnostics were lost"
    );
    assert.ok(
      (output.match(/redacted:unity-access-token/g) ?? []).length === 9,
      "native editor, activation, both probes, setup, stdout, stderr and both tail entries must be scrubbed"
    );
  }
);

runTest("standalone PowerShell launchers load the redactor at top level", () => {
  const script = `
$ErrorActionPreference = 'Stop'
foreach ($name in @('run-ci-tests.ps1', 'ensure-editor.ps1', 'setup-license.ps1')) {
  $tokens = $null; $errors = $null
  $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $env:GITHUB_WORKSPACE "scripts/unity/$name"), [ref]$tokens, [ref]$errors)
  if ($errors.Count) { throw 'launcher parse failed' }
  $imports = @($ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.PipelineAst] -and $_.Extent.Text.StartsWith('. ') -and $_.Extent.Text.Contains('lib/credential-redaction.ps1') })
  if ($imports.Count -ne 1) { throw 'launcher lacks unconditional redactor import' }
}
`;
  const result = spawnSync("pwsh", ["-NoProfile", "-Command", script], {
    encoding: "utf8",
    env: { ...process.env, GITHUB_WORKSPACE: repoRoot }
  });
  assert.ok(result.status === 0, "each launcher must parse and unconditionally load the redactor");
});

for (const root of temporaryRoots) {
  fs.rmSync(root, { recursive: true, force: true });
}

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
