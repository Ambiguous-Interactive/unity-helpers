#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const result = spawnSync(
  "uv",
  [
    "run",
    "--project",
    path.join(root, "scripts/python~"),
    "--locked",
    "pytest",
    path.join(root, "scripts/tests/test_random_quality_stream.py"),
    "-q",
    ...process.argv.slice(2)
  ],
  { cwd: root, stdio: "inherit" }
);
if (result.error) console.error(`Python tooling could not start: ${result.error.message}`);
process.exit(result.status === null ? 1 : result.status);
