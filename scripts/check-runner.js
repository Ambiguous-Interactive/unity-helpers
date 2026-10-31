"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { spawn, spawnSync } = require("node:child_process");
const root = path.resolve(__dirname, "..");
const executable = process.platform === "win32" ? "uv.exe" : "uv";
const uv =
  (process.env.PATH || "")
    .split(path.delimiter)
    .map((directory) => path.join(directory, executable))
    .find((candidate) => fs.existsSync(candidate)) || executable;
const argumentsForPython = [
  "run",
  "--quiet",
  "--project",
  path.join(root, "scripts/python~"),
  "--locked",
  "--no-dev",
  "python",
  path.join(root, "scripts/check_runner.py")
];

function invokeSync(request) {
  const result = spawnSync(uv, argumentsForPython, {
    cwd: root,
    input: JSON.stringify(request),
    encoding: "utf8"
  });
  if (result.error || result.status !== 0) {
    throw new Error(result.error ? result.error.message : result.stderr);
  }
  const response = JSON.parse(result.stdout.trim());
  return response.value;
}

function invoke(request) {
  return new Promise((resolve, reject) => {
    const child = spawn(uv, argumentsForPython, { cwd: root, stdio: ["pipe", "pipe", "pipe"] });
    let pending = "";
    let diagnostics = "";
    let received = false;
    let value;
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stderr.on("data", (chunk) => {
      diagnostics += chunk;
    });
    child.stdout.on("data", (chunk) => {
      pending += chunk;
      let newline;
      while ((newline = pending.indexOf("\n")) !== -1) {
        const line = pending.slice(0, newline);
        pending = pending.slice(newline + 1);
        try {
          const message = JSON.parse(line);
          if (message.kind === "output") {
            process.stdout.write(message.value);
          } else if (message.kind === "result" && !received) {
            received = true;
            value = message.value;
          } else {
            reject(new Error("Unexpected Python check runner response"));
          }
        } catch (error) {
          reject(error);
        }
      }
    });
    child.on("error", reject);
    child.stdin.on("error", reject);
    child.on("close", (status) => {
      if (status !== 0 || !received || pending.length > 0) {
        reject(new Error(`Python check runner failed (exit ${status}): ${diagnostics}`));
      } else {
        resolve(value);
      }
    });
    child.stdin.end(JSON.stringify(request));
  });
}

function defaultJobs(environment) {
  return invokeSync({ operation: "defaultJobs", environment });
}

function parseArguments(argv) {
  const result = invokeSync({ operation: "parseArguments", argv });
  return { ...result, only: new Set(result.only) };
}

function runChecks(checks, jobs) {
  return invoke({ operation: "runChecks", checks, jobs });
}

async function runCheck(check) {
  return (await runChecks([check], 1))[0];
}

function runRegistry(registry) {
  return invoke({ operation: "runRegistry", ...registry });
}

module.exports = { defaultJobs, parseArguments, runCheck, runChecks, runRegistry };
