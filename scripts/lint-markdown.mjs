// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

if (process.argv[2] === "--ignore-inventory") {
  // Use the same ignore dialect as the installed lint engine.
  const require = createRequire(import.meta.url);
  const cliRequire = createRequire(require.resolve("markdownlint-cli/package.json"));
  const { files, patterns } = JSON.parse(readFileSync(0, "utf8"));
  const ignored = cliRequire("ignore")().add(patterns);
  process.stdout.write(JSON.stringify(files.filter((file) => ignored.ignores(file))));
} else {
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
      path.join(root, "scripts/lint_markdown.py")
    ],
    { cwd: root, stdio: "inherit", env: { ...process.env, UH_NODE_EXECUTABLE: process.execPath } }
  );
  if (result.error) console.error(result.error.message);
  process.exitCode = result.status ?? 1;
}
