// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--quiet",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(root, "scripts/tests/test_lint_markdown.py"),
    "-q"
  ],
  { cwd: root, stdio: "inherit", env: { ...process.env, UH_NODE_EXECUTABLE: process.execPath } }
);
if (result.error) console.error(result.error.message);
process.exitCode = result.status ?? 1;
