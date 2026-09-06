#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Behavior tests for scripts/unity/credential-patterns.js and
// scripts/unity/redact-unity-artifacts.js.
//
// Unity writes its license identity into unity.log and configure.log while it activates. GitHub
// masks a registered secret in the rendered job log, but it never rewrites the bytes of an uploaded
// artifact, so a Unity output tree uploaded from a public repository publishes the serial. The
// redactor closes that gap, and these assertions are what make it safe to trust:
//
//   - every declared pattern fires and its value is destroyed;
//   - a second pass over redacted output changes nothing, so the step is safe to repeat;
//   - a masked value such as TOKEN=*** is not a hit, so operators are not trained to skip the step;
//   - a binary file is left byte-identical, so a player build survives the walk;
//   - a directory walk aggregates counts per kind, so a run reports what it removed.
//
// Every credential-shaped string below is synthetic. Nothing here is, or ever was, a live secret.

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const yaml = require("yaml");

const repoRoot = path.resolve(__dirname, "..", "..");
const { CREDENTIAL_PATTERNS, findCredentials, looksBinary, redactCredentials } = require(
  path.join(repoRoot, "scripts", "unity", "credential-patterns.js")
);
const { formatSummary, parseArgs, redactDirectory, runCli, usage } = require(
  path.join(repoRoot, "scripts", "unity", "redact-unity-artifacts.js")
);

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

const FAKE_SERIAL = "SC-FAKE-FAKE-FAKE-FAKE-FAKE";
const FAKE_LICENSE_ID = "FAKE-LICENSE-0000-0000";
const FAKE_GITHUB_TOKEN = `ghp_${"FAKEfake0123456789".repeat(2)}`;
const FAKE_AWS_KEY = "AKIA0000FAKE0000FAKE";
const FAKE_BEARER = "FAKEbearerFAKEbearerFAKEbearer";
const FAKE_PASSWORD = "fake-password-value-0000";
const FAKE_KEY_BODY = "FAKEkeybodyFAKEkeybodyFAKEkeybody";
const FAKE_PEM = `-----BEGIN RSA PRIVATE KEY-----\n${FAKE_KEY_BODY}\n-----END RSA PRIVATE KEY-----`;

/** `[patternId, sampleText, theSubstringThatMustDisappear]`, one row per declared pattern. */
const LEAK_CASES = Object.freeze([
  ["pem-private-key", `key follows\n${FAKE_PEM}\ndone\n`, FAKE_KEY_BODY],
  ["unity-license-id", `<License id="${FAKE_LICENSE_ID}" version="1.0">\n`, FAKE_LICENSE_ID],
  ["unity-serial", `Activated with serial ${FAKE_SERIAL} today\n`, FAKE_SERIAL],
  ["github-token", `remote token ${FAKE_GITHUB_TOKEN} rejected\n`, FAKE_GITHUB_TOKEN],
  ["aws-access-key-id", `uploader used ${FAKE_AWS_KEY} for the bucket\n`, FAKE_AWS_KEY],
  ["http-bearer-token", `Authorization: Bearer ${FAKE_BEARER}\n`, FAKE_BEARER],
  ["credential-assignment", `UNITY_PASSWORD=${FAKE_PASSWORD}\n`, FAKE_PASSWORD],
  [
    "unity-access-token",
    "[Licensing::Module] Successfully updated the access token SYNTHETIC-PREVIEW...\n",
    "SYNTHETIC-PREVIEW"
  ]
]);

/** Text that a careless pattern would flag. A false hit trains operators to bypass the step. */
const CLEAN_LOG = "[Licensing::Client] Successfully resolved entitlements in 0.284 seconds\n";
const CLEAN_CASES = Object.freeze([
  ["a masked GitHub token", "GITHUB_TOKEN=***\n"],
  ["a masked Unity serial", "UNITY_SERIAL=***\n"],
  ["a value too short to be a key", "API_KEY=short\n"],
  ["a bearer header with a short value", "Authorization: Bearer short\n"],
  ["ordinary prose", "The build uploaded a token to the store without a password.\n"],
  ["a Unity licensing log line", CLEAN_LOG],
  ["a Unity engine banner", "Initialize engine version: 6000.5.2f1 (b9e1b8d9d3a2)\n"]
]);

/** Serial-shaped bytes next to a NUL, so a lost binary skip cannot hide behind a clean tree. */
const BINARY_BLOB = Buffer.concat([
  Buffer.from([0x00, 0x01, 0x02, 0xff]),
  Buffer.from(FAKE_SERIAL, "utf8"),
  Buffer.from([0x00])
]);

const temporaryRoots = [];

function temporaryDirectory() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "redact-unity-artifacts-test-"));
  temporaryRoots.push(root);
  return root;
}

/** A miniature artifact tree: one clean log, two nested leaking logs, and a binary blob. */
function writeArtifactTree() {
  const root = temporaryDirectory();
  fs.mkdirSync(path.join(root, "logs", "deep"), { recursive: true });
  fs.writeFileSync(path.join(root, "clean.log"), CLEAN_LOG);
  fs.writeFileSync(
    path.join(root, "logs", "unity.log"),
    `serial ${FAKE_SERIAL} accepted\nreactivated with ${FAKE_SERIAL}\n`
  );
  fs.writeFileSync(
    path.join(root, "logs", "deep", "configure.log"),
    `Authorization: Bearer ${FAKE_BEARER}\nserial ${FAKE_SERIAL}\n`
  );
  fs.writeFileSync(path.join(root, "logs", "GameAssembly.bin"), BINARY_BLOB);
  return root;
}

console.log("Testing scripts/unity/redact-unity-artifacts.js...\n");

for (const [id, text, secret] of LEAK_CASES) {
  runTest(`${id} is found and its value is destroyed`, () => {
    assert.deepEqual(
      findCredentials(text).map((entry) => entry.id),
      [id],
      `${id}: findCredentials must report exactly this kind and nothing else`
    );
    const { redacted, counts } = redactCredentials(text);
    assert.ok(!redacted.includes(secret), `${id}: the sensitive substring must be gone`);
    assert.ok(redacted.includes(`<redacted:${id}>`), `${id}: the placeholder must name the kind`);
    assert.deepEqual([...counts], [[id, 1]], `${id}: exactly one value must be counted`);
  });
}

runTest("the leak table exercises every declared credential pattern", () => {
  assert.deepEqual(
    LEAK_CASES.map(([id]) => id).sort(),
    CREDENTIAL_PATTERNS.map((entry) => entry.id).sort(),
    "a new credential pattern must arrive with a LEAK_CASES row that proves it fires"
  );
});

for (const [id, text, expected] of [
  [
    "http-bearer-token",
    `Authorization: Bearer ${FAKE_BEARER}\n`,
    "Authorization: Bearer <redacted:http-bearer-token>\n"
  ],
  [
    "credential-assignment",
    `UNITY_PASSWORD=${FAKE_PASSWORD}\n`,
    "UNITY_PASSWORD=<redacted:credential-assignment>\n"
  ],
  [
    "unity-license-id",
    `<License id="${FAKE_LICENSE_ID}" version="1.0">\n`,
    '<License id="<redacted:unity-license-id>" version="1.0">\n'
  ]
]) {
  runTest(`${id} keeps the label that says which credential was removed`, () => {
    assert.equal(
      redactCredentials(text).redacted,
      expected,
      `${id}: only the value may be destroyed, the surrounding label must survive`
    );
  });
}

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

runTest("Unity token shapes preserve diagnostics and are idempotent in artifact redaction", () => {
  assertTokenCases((input) => redactCredentials(input).redacted, "artifact");
});

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

runTest("artifact CLI removes Unity previews without printing credentials", () => {
  const root = temporaryDirectory();
  const log = path.join(root, "unity.log");
  fs.writeFileSync(log, ACCESS_TOKEN_CASES.map(([, input]) => input).join("\n"));
  const result = spawnSync(
    process.execPath,
    [path.join(repoRoot, "scripts/unity/redact-unity-artifacts.js"), root],
    { encoding: "utf8" }
  );
  assert.ok(result.status === 0, "artifact CLI must succeed");
  assert.ok(
    !`${result.stdout}${result.stderr}`.includes("SYNTHETIC"),
    "artifact CLI leaked token bytes"
  );
  assert.ok(!fs.readFileSync(log, "utf8").includes("SYNTHETIC"), "artifact contains token bytes");
  assert.ok(
    redactDirectory(root).changed.length === 0,
    "second artifact pass must make no changes"
  );
});

runTest("a PEM key is redacted as one block, not just its header", () => {
  const { redacted } = redactCredentials(`prelude\n${FAKE_PEM}\nepilogue\n`);
  assert.equal(
    redacted,
    "prelude\n<redacted:pem-private-key>\nepilogue\n",
    "PEM: the body bytes and the END line must go with the header"
  );
  assert.ok(!redacted.includes("PRIVATE KEY"), "PEM: no part of the armour may survive");
});

runTest("redacting already-redacted text is a no-op", () => {
  // The step can run over the same tree more than once, so a second pass must find nothing.
  for (const [id, text] of LEAK_CASES) {
    const once = redactCredentials(text);
    const twice = redactCredentials(once.redacted);
    assert.equal(twice.redacted, once.redacted, `${id}: a second pass must not change the text`);
    assert.equal(twice.counts.size, 0, `${id}: a second pass must report nothing removed`);
    assert.deepEqual(
      findCredentials(once.redacted).map((entry) => entry.id),
      [],
      `${id}: no placeholder may look like a credential to a later gate`
    );
  }
});

for (const [label, text] of CLEAN_CASES) {
  runTest(`${label} is left byte-identical`, () => {
    const { redacted, counts } = redactCredentials(text);
    assert.equal(redacted, text, `${label}: a false positive would train operators to skip this`);
    assert.equal(counts.size, 0, `${label}: nothing may be counted as removed`);
    assert.deepEqual(findCredentials(text), [], `${label}: nothing may be reported as found`);
  });
}

runTest("a binary file is left byte-identical", () => {
  const root = temporaryDirectory();
  const blob = path.join(root, "GameAssembly.bin");
  fs.writeFileSync(blob, BINARY_BLOB);
  assert.ok(looksBinary(BINARY_BLOB), "the blob must look binary or this proves nothing");
  const result = redactDirectory(root);
  assert.deepEqual(result.changed, [], "binary: nothing may be rewritten");
  assert.deepEqual(
    fs.readFileSync(blob),
    BINARY_BLOB,
    "binary: a player build must survive the walk byte for byte"
  );
});

runTest("a directory walk aggregates counts per kind and repeats cleanly", () => {
  const root = writeArtifactTree();
  const result = redactDirectory(root);
  assert.deepEqual(
    result.changed.map((file) => file.path),
    ["logs/deep/configure.log", "logs/unity.log"],
    "tree: only the two leaking logs may be rewritten"
  );
  assert.deepEqual(
    result.changed.map((file) => file.counts),
    [["http-bearer-token", "unity-serial"], ["unity-serial"]],
    "tree: each rewritten file reports the kinds it carried"
  );
  assert.deepEqual(
    [...result.totals].sort(),
    [
      ["http-bearer-token", 1],
      ["unity-serial", 3]
    ],
    "tree: counts are aggregated per pattern id across the whole walk"
  );
  assert.deepEqual(result.skipped, [], "tree: nothing in a readable tree may be skipped");
  assert.equal(
    fs.readFileSync(path.join(root, "clean.log"), "utf8"),
    CLEAN_LOG,
    "tree: a clean file must not be touched"
  );
  assert.equal(
    fs.readFileSync(path.join(root, "logs", "unity.log"), "utf8"),
    "serial <redacted:unity-serial> accepted\nreactivated with <redacted:unity-serial>\n",
    "tree: every occurrence in a file is replaced, not just the first"
  );
  const second = redactDirectory(root);
  assert.deepEqual(second.changed, [], "tree: a second pass must rewrite nothing");
  assert.deepEqual([...second.totals], [], "tree: a second pass must report nothing removed");
});

runTest("redactDirectory refuses a path that is not a directory", () => {
  const root = writeArtifactTree();
  assert.throws(
    () => redactDirectory(path.join(root, "clean.log")),
    /clean\.log is not a directory\./,
    "a file target must fail rather than be walked"
  );
  assert.throws(
    () => redactDirectory(path.join(root, "absent")),
    /absent is not a directory\./,
    "a missing target must fail rather than report a clean tree"
  );
});

runTest("redactDirectory reports a file it cannot read instead of ignoring it", () => {
  if (process.platform === "win32" || !process.getuid || process.getuid() === 0) {
    // Windows ignores a POSIX mode, and root reads any file, so no unreadable file can be staged.
    console.log("         (skipped: this platform cannot stage an unreadable file)");
    return;
  }
  const root = temporaryDirectory();
  const locked = path.join(root, "locked.log");
  fs.writeFileSync(locked, `serial ${FAKE_SERIAL}\n`);
  fs.chmodSync(locked, 0o000);
  try {
    const result = redactDirectory(root);
    assert.deepEqual(result.changed, [], "unreadable: nothing can be rewritten");
    assert.equal(result.skipped.length, 1, "unreadable: the file must be reported exactly once");
    assert.equal(result.skipped[0].path, "locked.log", "unreadable: the report names the file");
    assert.match(
      result.skipped[0].reason,
      /^could not be read: /,
      "unreadable: an unchecked file must never be silently treated as clean"
    );
  } finally {
    fs.chmodSync(locked, 0o600);
  }
});

runTest("the CLI skips a missing directory, demands a target, and explains itself", () => {
  const written = [];
  const write = (text) => written.push(text);
  const argv = (...rest) => ["node", "redact-unity-artifacts.js", ...rest];
  const missing = path.join(temporaryDirectory(), "absent");
  assert.equal(runCli(argv(missing), write), 0, "cli: a missing directory is not a failure");
  assert.match(
    written.join(""),
    /^Skipping .*absent; it does not exist\.\n$/,
    "cli: a missing directory is named in the output"
  );
  written.length = 0;
  assert.equal(runCli(argv(), write), 1, "cli: no target is a usage error");
  assert.equal(written.join(""), usage(), "cli: a usage error prints the usage text");
  written.length = 0;
  assert.equal(runCli(argv("--help"), write), 0, "cli: --help is not an error");
  assert.equal(written.join(""), usage(), "cli: --help prints the usage text");
  assert.throws(
    () => runCli(argv("--nope"), write),
    /Unknown option --nope\./,
    "cli: an unknown option must fail loudly rather than be read as a directory"
  );
  assert.deepEqual(
    parseArgs(argv("first", "second")),
    { roots: ["first", "second"], help: false },
    "cli: every positional argument becomes a root so one call covers a whole run"
  );
});

runTest("the CLI redacts a real tree and never echoes what it removed", () => {
  const root = writeArtifactTree();
  const written = [];
  assert.equal(
    runCli(["node", "cli", root], (text) => written.push(text)),
    0,
    "cli: a tree exits 0"
  );
  assert.match(
    written.join(""),
    /^Redacted 2 file\(s\) under .*: http-bearer-token x1, unity-serial x3\./,
    "cli: the summary reports kinds and counts"
  );
  assert.ok(!written.join("").includes(FAKE_SERIAL), "cli: output must never echo a credential");
});

runTest("formatSummary renders a clean tree, a redacted tree, and a skipped file", () => {
  assert.equal(
    formatSummary("artifacts", { changed: [], skipped: [], totals: new Map() }),
    "No credential material found under artifacts.\n",
    "summary: a clean tree says so plainly"
  );
  assert.equal(
    formatSummary("artifacts", {
      changed: [
        { path: "logs/deep/configure.log", counts: ["http-bearer-token", "unity-serial"] },
        { path: "logs/unity.log", counts: ["unity-serial"] }
      ],
      skipped: [],
      totals: new Map([
        ["unity-serial", 3],
        ["http-bearer-token", 1]
      ])
    }),
    "Redacted 2 file(s) under artifacts: http-bearer-token x1, unity-serial x3.\n" +
      "  logs/deep/configure.log: http-bearer-token, unity-serial\n" +
      "  logs/unity.log: unity-serial\n",
    "summary: per-kind totals are sorted by id and each file lists its kinds"
  );
  assert.equal(
    formatSummary("artifacts", {
      changed: [],
      skipped: [{ path: "locked.log", reason: "could not be read: EACCES" }],
      totals: new Map()
    }),
    "No credential material found under artifacts.\n" +
      "  WARNING: locked.log was not scanned because it could not be read: EACCES.\n",
    "summary: a file that was not scanned is a warning, not a silent omission"
  );
});

runTest("an unwritable file that holds credentials still fails the run closed", () => {
  // The exclusion above is by name, not by readability: a file the walk does reach and cannot
  // rewrite must still stop the upload, because its contents were never made safe.
  if (typeof process.getuid === "function" && process.getuid() === 0) {
    // Mode bits do not constrain root, so this platform cannot produce the subject.
    assert.ok(true, "fail-closed: skipped, a read-only file is still writable as root");
    return;
  }
  const root = temporaryDirectory();
  const log = path.join(root, "unity.log");
  fs.writeFileSync(log, `serial ${FAKE_SERIAL}\n`);
  fs.chmodSync(log, 0o444);
  try {
    assert.throws(
      () => redactDirectory(root),
      /unity\.log contains credential material but could not be rewritten/,
      "fail-closed: an unwritable leaking file must throw rather than be skipped"
    );
  } finally {
    fs.chmodSync(log, 0o644);
  }
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
