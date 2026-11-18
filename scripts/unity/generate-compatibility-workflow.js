"use strict";

const path = require("node:path");
const { spawnSync } = require("node:child_process");

const root = path.resolve(__dirname, "../..");
const target = ".github/workflows/unity-compatibility.yml";

function callRenderer(args, input) {
  return spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(root, "scripts/python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(__dirname, "generate_compatibility_workflow.py"),
      ...args
    ],
    {
      cwd: root,
      encoding: "utf8",
      input,
      windowsHide: true,
      env: { ...process.env, UH_NODE_EXECUTABLE: process.execPath }
    }
  );
}

function capturedOutput(args, input) {
  const result = callRenderer(args, input);
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(result.stderr.trim() || "Unity compatibility workflow generation failed.");
  }
  return result.stdout;
}

function renderCompatibilityWorkflow(source) {
  return capturedOutput(["--raw"], source);
}

async function generatedWorkflow(repository) {
  return capturedOutput(["--repo-root", path.resolve(repository), "--stdout"]);
}

if (require.main === module) {
  const result = callRenderer(process.argv.includes("--check") ? ["--check"] : []);
  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status ?? 1;
}

module.exports = { target, renderCompatibilityWorkflow, generatedWorkflow };
