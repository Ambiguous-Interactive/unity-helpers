"use strict";

// Pins the two halves of the EAGAIN fix (#376): the runner must not read stdin at all, and the one
// place that genuinely has to read it must survive a non-blocking descriptor.
//
// The failure being pinned is deterministic once you know the trigger. Touching `process.stdin`
// makes Node put fd 0 into non-blocking mode; a `readFileSync(0)` after that returns EAGAIN instead
// of waiting whenever the writer has not produced anything yet. Both child scripts below touch
// stdin exactly the way the old runner did, and the parent deliberately writes late, so "no data
// yet" is guaranteed rather than raced for.

const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const writeDelayMs = 250;
let passed = 0;

/** Runs `node <args>` with a pipe on stdin that stays empty for a while, then closes. */
function runWithLateStdin(args, payload) {
  return new Promise((resolve) => {
    const child = childProcess.spawn(process.execPath, args, {
      cwd: root,
      stdio: ["pipe", "pipe", "pipe"]
    });

    let stdout = "";
    let stderr = "";
    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString("utf8");
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString("utf8");
    });

    const timer = setTimeout(() => {
      child.stdin.end(payload);
    }, writeDelayMs);

    child.on("close", (status) => {
      clearTimeout(timer);
      child.stdin.destroy();
      resolve({ status, stdout, stderr });
    });
  });
}

function check(description, condition, detail) {
  assert.ok(condition, `${description}${detail ? `: ${detail}` : ""}`);
  passed += 1;
}

async function main() {
  // 1. The helper reads everything, however late it arrives, even though the descriptor is
  //    non-blocking by then. Without the EAGAIN retry this throws immediately and reads nothing.
  const helperProbe = [
    "-e",
    [
      "void process.stdin.isTTY;",
      "const { readStdinSync } = require('./scripts/read-stdin-sync.js');",
      "const result = readStdinSync({ timeoutMs: 5000 });",
      "process.stdout.write('OK ' + result.data.toString('utf8').trim());"
    ].join("")
  ];
  const helper = await runWithLateStdin(helperProbe, "payload-arriving-late");
  check("helper exits cleanly on a non-blocking fd 0", helper.status === 0, helper.stderr);
  check(
    "helper returns the whole payload",
    helper.stdout.trim() === "OK payload-arriving-late",
    helper.stdout
  );

  // 2. The same probe with the old implementation, to prove the trigger is real rather than
  //    theoretical. If this ever stops failing, test 1 has stopped testing anything.
  const legacyProbe = [
    "-e",
    [
      "const fs = require('node:fs');",
      "void process.stdin.isTTY;",
      "try { fs.readFileSync(0); process.stdout.write('READ_OK'); }",
      "catch (error) { process.stdout.write('READ_FAIL ' + error.code); }"
    ].join("")
  ];
  const legacy = await runWithLateStdin(legacyProbe, "payload-arriving-late");
  const reproduced = legacy.stdout.trim() === "READ_FAIL EAGAIN";
  check(
    "the EAGAIN trigger still reproduces, or the platform never had it",
    reproduced || process.platform === "win32",
    legacy.stdout
  );

  // 3. The runner passes the descriptor through instead of draining it. This is the regression that
  //    reddened validate:prepush: the runner exited 1 while the lint it wrapped found nothing.
  const runner = await runWithLateStdin(
    [path.join("scripts", "run-node-bin.js"), "prettier", "--version"],
    ""
  );
  check("runner exits 0 with an unread stdin", runner.status === 0, runner.stderr);
  check(
    "runner reports the prettier version rather than a stdin error",
    /^\d+\.\d+\.\d+/.test(runner.stdout.trim()),
    `${runner.stdout} ${runner.stderr}`
  );
  check("runner emits no stdin diagnostic", !/stdin/i.test(runner.stderr), runner.stderr);

  // 4. Exhaustion is reported as an environment error, not as a finding, and names what to do.
  const timeoutProbe = [
    "-e",
    [
      "void process.stdin.isTTY;",
      "const { readStdinSync } = require('./scripts/read-stdin-sync.js');",
      "try { readStdinSync({ timeoutMs: 50 }); process.stdout.write('NO_TIMEOUT'); }",
      "catch (error) { process.stdout.write(error.code + '|' + error.message); }"
    ].join("")
  ];
  const timedOut = await new Promise((resolve) => {
    const child = childProcess.spawn(process.execPath, timeoutProbe, {
      cwd: root,
      stdio: ["pipe", "pipe", "pipe"]
    });
    let stdout = "";
    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString("utf8");
    });
    child.on("close", () => {
      child.stdin.destroy();
      resolve(stdout);
    });
  });

  if (timedOut.startsWith("ESTDINTIMEOUT")) {
    check(
      "the timeout says it is an environment error and how to recover",
      /environment error/.test(timedOut) && /Re-run/.test(timedOut),
      timedOut
    );
  } else {
    check(
      "a platform without EAGAIN reads an empty stdin instead of timing out",
      timedOut === "NO_TIMEOUT",
      timedOut
    );
  }

  process.stdout.write(`stdin EAGAIN contract passed (${passed} checks).\n`);
}

main().catch((error) => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});
