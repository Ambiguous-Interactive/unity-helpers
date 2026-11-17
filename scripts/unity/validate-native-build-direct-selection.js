// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

"use strict";

const path = require("node:path");
const { spawnSync } = require("node:child_process");
if (require.main === module) {
  const result = spawnSync(
    "uv",
    [
      "run",
      "--quiet",
      "--project",
      path.join(__dirname, "../python~"),
      "--locked",
      "--no-dev",
      "python",
      path.join(__dirname, "native_build_selection.py"),
      "direct"
    ],
    { stdio: "inherit" }
  );
  if (result.error) console.error(`Python tooling could not start: ${result.error.message}`);
  process.exitCode = result.status ?? 1;
}
