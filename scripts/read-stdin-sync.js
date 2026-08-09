"use strict";

/**
 * Synchronously drains stdin, tolerating a non-blocking file descriptor.
 *
 * `fs.readFileSync(0)` fails with EAGAIN whenever fd 0 is a pipe that has been switched to
 * non-blocking mode and has no data buffered yet. Node switches it itself the moment anything
 * touches `process.stdin` -- the getter constructs a libuv handle, and libuv sets O_NONBLOCK on the
 * shared file description -- so a process that inspects `process.stdin` and then reads fd 0 races
 * against its own writer. The mode outlives the process too: it is a property of the open file
 * description, so a parent or sibling that touched its stdin leaves every later reader of that same
 * pipe non-blocking.
 *
 * Reading in a loop and retrying EAGAIN restores the blocking semantics the caller wanted. Callers
 * that do not need stdin at all should not read it: pass the descriptor through instead.
 */

const fs = require("node:fs");

const DEFAULT_TIMEOUT_MS = 5000;
const POLL_INTERVAL_MS = 5;
const CHUNK_BYTES = 65536;

/** Blocks the calling thread without spinning a CPU core. */
function sleepSync(milliseconds) {
  const signal = new Int32Array(new SharedArrayBuffer(4));
  Atomics.wait(signal, 0, 0, milliseconds);
}

/**
 * Reads fd 0 until end of input.
 *
 * @param {{ fd?: number, timeoutMs?: number }} [options]
 * @returns {{ data: Buffer, retries: number }} the bytes read and how many EAGAIN retries it took.
 * @throws {Error} with `code` set to `ESTDINTIMEOUT` when EAGAIN persists past the timeout.
 */
function readStdinSync(options) {
  const settings = options || {};
  const fd = typeof settings.fd === "number" ? settings.fd : 0;
  const timeoutMs =
    typeof settings.timeoutMs === "number" ? settings.timeoutMs : DEFAULT_TIMEOUT_MS;

  const chunks = [];
  const buffer = Buffer.alloc(CHUNK_BYTES);
  let retries = 0;
  let waitedMs = 0;

  for (;;) {
    let bytesRead;
    try {
      bytesRead = fs.readSync(fd, buffer, 0, buffer.length, null);
    } catch (error) {
      if (error.code === "EAGAIN") {
        if (waitedMs >= timeoutMs) {
          const timeout = new Error(
            `stdin stayed unreadable (EAGAIN) for ${timeoutMs}ms. This is an environment error, ` +
              "not a finding: fd 0 is a non-blocking pipe with no data and no writer. Re-run, or " +
              "redirect stdin from a file or /dev/null."
          );
          timeout.code = "ESTDINTIMEOUT";
          timeout.retries = retries;
          throw timeout;
        }

        retries += 1;
        waitedMs += POLL_INTERVAL_MS;
        sleepSync(POLL_INTERVAL_MS);
        continue;
      }

      // A closed pipe and a fd that was never opened both mean "no more input" here.
      if (error.code === "EOF" || error.code === "EBADF") {
        break;
      }

      throw error;
    }

    if (bytesRead === 0) {
      break;
    }

    chunks.push(Buffer.from(buffer.subarray(0, bytesRead)));
  }

  return { data: Buffer.concat(chunks), retries };
}

module.exports = { readStdinSync };
