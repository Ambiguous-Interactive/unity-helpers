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
  ["credential-assignment", `UNITY_PASSWORD=${FAKE_PASSWORD}\n`, FAKE_PASSWORD]
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
