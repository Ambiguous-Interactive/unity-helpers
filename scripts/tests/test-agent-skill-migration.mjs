import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
const directory = path.dirname(fileURLToPath(import.meta.url));
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(directory, "../python~"),
    "--locked",
    ...["pytest", "-q"],
    path.join(directory, "test_skills_tooling.py"),
    ...process.argv.slice(2)
  ],
  { cwd: path.resolve(directory, "../.."), stdio: "inherit" }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status === null ? 1 : result.status;
