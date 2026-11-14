import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "--no-dev",
    "python",
    path.join(root, "scripts/generate_agent_skill_stubs.py"),
    ...process.argv.slice(2)
  ],
  { cwd: root, stdio: "inherit", windowsHide: true }
);
if (result.error) {
  console.error(`Python tooling could not start: ${result.error.message}`);
}
process.exitCode = result.status === null ? 1 : result.status;
