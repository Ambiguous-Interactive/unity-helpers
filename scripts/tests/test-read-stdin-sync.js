"use strict";

// Pins both halves of #376: the runner passes stdin through, and the reader retries EAGAIN.
// Payload delivery waits for the child's first actual EAGAIN, so startup scheduling cannot hide
// the trigger. Child pipe errors belong to the probe result rather than escaping as parent errors.

const assert = require("node:assert/strict");
const childProcess = require("node:child_process");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const waitingMarker = "__STDIN_WAITING__\n";
let passed = 0;

/** Runs a bounded probe, keeping stdin empty until its requested marker arrives. */
function runProbe(args, options = {}) {
  return new Promise((resolve, reject) => {
    const child = childProcess.spawn(process.execPath, args, {
      cwd: root,
      stdio: options.acknowledgePipeError
        ? ["pipe", "pipe", "pipe", "ipc"]
        : ["pipe", "pipe", "pipe"]
    });
    let stdout = "";
    let stderr = "";
    let stdinError = null;
    let inputSent = false;
    let timedOut = false;
    let closeResult = null;
    let inputSettled = !options.writeAfterExit;
    function finish() {
      if (!closeResult || !inputSettled) {
        return;
      }
      clearTimeout(timer);
      child.stdin.destroy();
      resolve({ ...closeResult, stdout, stderr, stdinError, inputSent, timedOut });
    }
    const timer = setTimeout(() => {
      timedOut = true;
      child.kill("SIGKILL");
      if (closeResult) {
        inputSettled = true;
        finish();
      }
    }, options.timeoutMs || 10000);

    child.stdin.on("error", (error) => {
      stdinError = error.code;
      if (options.acknowledgePipeError && child.connected) {
        child.send("pipe-error-observed", (sendError) => {
          if (sendError) {
            stderr += sendError.message;
            child.kill("SIGKILL");
          }
        });
      }
    });
    child.stdout.on("data", (chunk) => {
      stdout += chunk.toString("utf8");
      if (!inputSent && options.inputMarker && stdout.includes(options.inputMarker)) {
        inputSent = true;
        child.stdin.end(options.payload || "");
      }
    });
    child.stderr.on("data", (chunk) => {
      stderr += chunk.toString("utf8");
    });
    child.on("error", (error) => {
      clearTimeout(timer);
      child.stdin.destroy();
      reject(error);
    });
    child.on("exit", () => {
      if (options.writeAfterExit) {
        inputSent = true;
        child.stdin.end(options.payload || "", (error) => {
          if (error) {
            stdinError = error.code;
          }
          inputSettled = true;
          finish();
        });
      }
    });
    child.on("close", (status, signal) => {
      closeResult = { status, signal };
      finish();
    });
  });
}

function check(description, condition, detail) {
  assert.ok(condition, `${description}${detail ? `: ${detail}` : ""}`);
  passed += 1;
}

function checkCleanProbe(description, result) {
  check(
    description,
    result.status === 0 && !result.timedOut && !result.stdinError,
    JSON.stringify(result)
  );
}

async function main() {
  const helperSource = [
    "const fs = require('node:fs');",
    "void process.stdin.isTTY;",
    "const read = fs.readSync;",
    "let signalled = false;",
    "fs.readSync = (...args) => {",
    "try { return read(...args); } catch (error) {",
    "if (error.code === 'EAGAIN' && !signalled) { signalled = true; process.stdout.write(" +
      JSON.stringify(waitingMarker) +
      "); }",
    "throw error; } };",
    "if (process.platform === 'win32') process.stdout.write(" +
      JSON.stringify(waitingMarker) +
      ");",
    "const { readStdinSync } = require('./scripts/read-stdin-sync.js');",
    "const result = readStdinSync({ timeoutMs: 5000 });",
    "process.stdout.write('RESULT ' + JSON.stringify({ payload: result.data.toString('utf8'), retries: result.retries }));"
  ].join("");

  for (const startupDelayMs of [0, 400]) {
    const helper = await runProbe(
      [
        "-e",
        `Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ${startupDelayMs});` +
          helperSource
      ],
      { inputMarker: waitingMarker, payload: "payload-arriving-late" }
    );
    checkCleanProbe(`helper exits cleanly after ${startupDelayMs}ms startup`, helper);
    check("helper receives input only after its readiness marker", helper.inputSent, helper.stdout);
    const resultLine = helper.stdout.slice(helper.stdout.indexOf("RESULT ") + "RESULT ".length);
    const result = JSON.parse(resultLine);
    check(
      "helper returns the whole payload",
      result.payload === "payload-arriving-late",
      resultLine
    );
    check(
      "the helper actually retries EAGAIN where supported",
      result.retries > 0 || process.platform === "win32",
      resultLine
    );
  }

  // The legacy probe gets no input on POSIX, including when startup is slow. It must observe EAGAIN.
  const legacy = await runProbe(
    [
      "-e",
      [
        "const fs = require('node:fs');",
        "void process.stdin.isTTY;",
        "if (process.platform === 'win32') process.stdout.write(" +
          JSON.stringify(waitingMarker) +
          ");",
        "try { fs.readFileSync(0); process.stdout.write('READ_OK'); }",
        "catch (error) { process.stdout.write('READ_FAIL ' + error.code); }"
      ].join("")
    ],
    process.platform === "win32" ? { inputMarker: waitingMarker } : {}
  );
  checkCleanProbe("legacy trigger probe finishes cleanly", legacy);
  check(
    "the legacy EAGAIN trigger remains real",
    legacy.stdout.trim() === "READ_FAIL EAGAIN" || process.platform === "win32",
    legacy.stdout
  );

  const runner = await runProbe([path.join("scripts", "run-node-bin.js"), "prettier", "--version"]);
  checkCleanProbe("runner exits 0 with unread, open stdin", runner);
  check(
    "runner reports the prettier version",
    /^\d+\.\d+\.\d+/.test(runner.stdout.trim()),
    `${runner.stdout} ${runner.stderr}`
  );
  check("runner emits no stdin diagnostic", !/stdin/i.test(runner.stderr), runner.stderr);

  const timedOut = await runProbe(
    [
      "-e",
      [
        "void process.stdin.isTTY;",
        "if (process.platform === 'win32') process.stdout.write(" +
          JSON.stringify(waitingMarker) +
          ");",
        "const { readStdinSync } = require('./scripts/read-stdin-sync.js');",
        "try { readStdinSync({ timeoutMs: 50 }); process.stdout.write('NO_TIMEOUT'); }",
        "catch (error) { process.stdout.write(error.code + '|' + error.message); }"
      ].join("")
    ],
    process.platform === "win32" ? { inputMarker: waitingMarker } : {}
  );
  checkCleanProbe("timeout probe terminates without harness errors", timedOut);
  if (timedOut.stdout.includes("ESTDINTIMEOUT")) {
    check(
      "the timeout names the environment error and recovery",
      /environment error/.test(timedOut.stdout) && /Re-run/.test(timedOut.stdout),
      timedOut.stdout
    );
  } else {
    check(
      "only a platform without the EAGAIN trigger may read EOF",
      process.platform === "win32" && timedOut.stdout.endsWith("NO_TIMEOUT"),
      timedOut.stdout
    );
  }

  if (process.platform !== "win32") {
    // Close both Node's pipe stream and the original descriptor before signalling. IPC keeps the
    // child alive until the parent observes the write error, without relying on a timed write or exit.
    const closedPipe = await runProbe(
      [
        "-e",
        [
          "process.on('message', () => process.exit(0));",
          "process.stdin.once('close', () => { try { require('node:fs').closeSync(0); } catch (error) { if (error.code !== 'EBADF') throw error; } process.stdout.write(" +
            JSON.stringify(waitingMarker) +
            "); });",
          "process.stdin.destroy();"
        ].join("")
      ],
      { inputMarker: waitingMarker, payload: "write-after-close", acknowledgePipeError: true }
    );
    check(
      "a closed child pipe is captured rather than crashing the parent",
      closedPipe.status === 0 &&
        !closedPipe.timedOut &&
        closedPipe.inputSent &&
        closedPipe.stdinError === "EPIPE",
      JSON.stringify(closedPipe)
    );
  }

  // A child's exit closes its OS pipe handles on every platform. Wait for the late write's
  // callback as well as child close, so asynchronous stream errors cannot escape the result.
  const exitedPipe = await runProbe(
    ["-e", "process.stdout.write('CHILD_EXITED'); process.exit(0);"],
    { writeAfterExit: true, payload: "write-after-exit" }
  );
  check(
    "writing after child exit reports a closed pipe or destroyed stream",
    exitedPipe.status === 0 &&
      !exitedPipe.timedOut &&
      exitedPipe.inputSent &&
      exitedPipe.stdout === "CHILD_EXITED" &&
      (exitedPipe.stdinError === "EPIPE" || exitedPipe.stdinError === "ERR_STREAM_DESTROYED"),
    JSON.stringify(exitedPipe)
  );
  process.stdout.write(
    `closed-child stdin error (${process.platform}): ${exitedPipe.stdinError}.\n`
  );

  const nonzero = await runProbe(["-e", "process.exit(37);"]);
  check(
    "a child's failure status is preserved",
    nonzero.status === 37 && !nonzero.timedOut,
    JSON.stringify(nonzero)
  );
  const stalled = await runProbe(["-e", "setInterval(() => {}, 1000);"], { timeoutMs: 100 });
  check(
    "a stalled child is bounded and reported as a timeout",
    stalled.timedOut && stalled.status !== 0,
    JSON.stringify(stalled)
  );

  process.stdout.write(`stdin EAGAIN contract passed (${passed} checks).\n`);
}

main().catch((error) => {
  process.stderr.write(`${error.stack || error}\n`);
  process.exitCode = 1;
});
