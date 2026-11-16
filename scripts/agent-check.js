"use strict";
const { spawnSync } = require("node:child_process");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const python = [
  "run",
  "--quiet",
  "--project",
  path.join(root, "scripts/python~"),
  "--locked",
  "--no-dev",
  "python",
  path.join(root, "scripts/agent_check.py")
];
function invoke(request) {
  const result = spawnSync("uv", [...python, "--bridge"], {
    cwd: root,
    encoding: "utf8",
    input: JSON.stringify(request),
    maxBuffer: 16777216
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr.trim() || "Python task selection failed");
  return JSON.parse(result.stdout).value;
}
function select(files, tier) {
  return invoke({ operation: "select", files, tier });
}
function changedFiles(base, cwd = root) {
  return invoke({ operation: "changedFiles", base, cwd });
}
function executeChecks(selection, cwd = root) {
  const result = spawnSync("uv", [...python, "--bridge"], {
    cwd: root,
    input: JSON.stringify({ operation: "executeChecks", commands: selection.commands, cwd }),
    stdio: ["pipe", "inherit", "inherit"]
  });
  if (result.error) throw result.error;
  return result.status === null ? 1 : result.status;
}
function main(args) {
  const result = spawnSync("uv", [...python, "--bridge"], {
    cwd: root,
    encoding: "utf8",
    input: JSON.stringify({ operation: "main", args }),
    maxBuffer: 16777216
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(result.stderr.trim() || "Python task validation failed");
  const lines = result.stdout.trimEnd().split("\n");
  const response = JSON.parse(lines.pop());
  if (lines.length) process.stdout.write(`${lines.join("\n")}\n`);
  if (result.stderr) process.stderr.write(result.stderr);
  process.stdout.write(response.output);
  return response.value;
}
module.exports = { select, changedFiles, executeChecks, main };
if (require.main === module) {
  const result = spawnSync("uv", [...python, ...process.argv.slice(2)], {
    cwd: root,
    stdio: "inherit"
  });
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status === null ? 1 : result.status;
}
