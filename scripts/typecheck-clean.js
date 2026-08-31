#!/usr/bin/env node

/*
    A typecheck that a changed analyzer cannot fool.

    MSBuild skips a compile whose `.cs` files have not changed, so a freshly built analyzer never
    runs and the gate prints what a pass prints -- `typecheck:tests` exited 0 on a tree the same
    command reported four WPROTO044 errors on once `obj/` was deleted (session 238). Deleting
    `obj/` is not sufficient either: a shared Roslyn compiler server served a stale file snapshot
    and reported diagnostics at pre-edit line numbers for edits already on disk (session 239).

    So this does both halves. `UseSharedCompilation` is passed through the environment, which
    MSBuild reads as a global property, so it reaches every project in the chain without editing
    twenty script lines.

    Reach for it when the change adds or widens a diagnostic -- the discriminator is whether an
    analyzer DLL is in the diff -- not on every run. It is several times slower than
    `npm run typecheck:unity`.
*/

const { spawnSync } = require("node:child_process");
const { existsSync, readdirSync, rmSync, statSync } = require("node:fs");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");
const generatorRoot = path.join(repoRoot, "Generator~");

if (!existsSync(generatorRoot)) {
  console.error(`[typecheck-clean] ${generatorRoot} does not exist.`);
  process.exit(2);
}

let removed = 0;
for (const entry of readdirSync(generatorRoot)) {
  const projectRoot = path.join(generatorRoot, entry);
  if (!statSync(projectRoot).isDirectory()) {
    continue;
  }

  for (const artifact of ["obj", "bin"]) {
    const target = path.join(projectRoot, artifact);
    if (!existsSync(target)) {
      continue;
    }

    rmSync(target, { recursive: true, force: true });
    removed++;
  }
}

console.log(
  `[typecheck-clean] Removed ${removed} build artifact ` +
    `${removed === 1 ? "directory" : "directories"}; shared compilation is off for this run.`
);

const requested = process.argv.slice(2);
const target = requested.length === 0 ? ["typecheck:unity"] : requested;
const npm = process.platform === "win32" ? "npm.cmd" : "npm";
const result = spawnSync(npm, ["run", ...target], {
  cwd: repoRoot,
  stdio: "inherit",
  env: { ...process.env, UseSharedCompilation: "false" }
});

if (result.error) {
  console.error(`[typecheck-clean] ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status === null ? 1 : result.status);
