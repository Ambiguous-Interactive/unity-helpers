#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// postinstall hook: ensure git hooks are installed for local contributors so
// `npm install` is sufficient to get pre-commit / pre-push safety nets.
//
// Idempotent: if core.hooksPath resolves to `.githooks` (including common
// equivalent forms like trailing slash or Windows separators), does nothing.
//
// Safe in CI / non-repo checkouts. Skip triggers (each logs a clear reason):
//   - CI=true or CI=1 (CI images install hooks explicitly if needed)
//   - SKIP_POSTINSTALL_HOOKS=1 (opt-out for local workflows)
//   - NPM_CONFIG_IGNORE_SCRIPTS=true|1 (npm's --ignore-scripts, the standard
//     way to opt out of postinstall logic)
//   - HUSKY=0 (industry-standard opt-out popularized by Husky)
//   - pwsh missing (tracked hooks delegate to `.ps1` implementations)
//   - Not inside a git work tree
//   - Never fails npm install: any error is logged and we exit 0.
//
// Cross-platform: the chmod is best-effort; on Windows, git-bash/cmd do not
// require chmod for hook execution (git invokes hooks via the shebang).

"use strict";

const { execSync, spawnSync } = require("child_process");
const fs = require("fs");
const path = require("path");

function skipReason() {
  if (process.env.CI === "true" || process.env.CI === "1") {
    return "CI=true — skipping postinstall hook install";
  }
  if (process.env.SKIP_POSTINSTALL_HOOKS === "1") {
    return "SKIP_POSTINSTALL_HOOKS=1 — skipping";
  }
  // Honor `npm install --ignore-scripts`. npm exports the config as the
  // lower-case form `npm_config_ignore_scripts` when it spawns lifecycle
  // scripts (so `npm install --ignore-scripts` skips postinstall entirely
  // via that path). A contributor re-running the node script directly after
  // `export NPM_CONFIG_IGNORE_SCRIPTS=1` only has the upper-case form
  // visible. Check both so either manual spelling is honored.
  // Accept both common boolean-ish spellings.
  const ignoreScripts =
    process.env.NPM_CONFIG_IGNORE_SCRIPTS || process.env.npm_config_ignore_scripts;
  if (ignoreScripts === "true" || ignoreScripts === "1") {
    return `npm_config_ignore_scripts=${ignoreScripts} — skipping`;
  }
  // Industry-standard opt-out popularized by Husky. Respecting it lets
  // contributors who never want hook install (Docker images, ephemeral
  // CI checkouts) keep using the familiar flag.
  if (process.env.HUSKY === "0") {
    return "HUSKY=0 — skipping";
  }
  return null;
}

function isGitRepo(cwd) {
  const result = spawnSync("git", ["rev-parse", "--is-inside-work-tree"], {
    cwd,
    stdio: ["ignore", "pipe", "ignore"]
  });
  return result.status === 0 && String(result.stdout).trim() === "true";
}

function commandExists(command) {
  const result = spawnSync(command, ["--version"], {
    stdio: ["ignore", "ignore", "ignore"]
  });
  return result.status === 0;
}

function currentHooksPath(cwd) {
  const result = spawnSync("git", ["config", "--get", "core.hooksPath"], {
    cwd,
    stdio: ["ignore", "pipe", "ignore"]
  });
  if (result.status !== 0) {
    return "";
  }
  return String(result.stdout).trim();
}

function normalizeHooksPath(value) {
  if (!value) {
    return "";
  }

  let normalized = String(value).replace(/\r/g, "").trim();
  normalized = normalized.replace(/\\/g, "/");

  while (normalized.startsWith("./")) {
    normalized = normalized.slice(2);
  }

  while (normalized.endsWith("/")) {
    normalized = normalized.slice(0, -1);
  }

  return normalized;
}

function hookEntryPoints(hooksDir) {
  return fs
    .readdirSync(hooksDir, { withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name)
    .filter((name) => !name.includes("."))
    .sort();
}

function main() {
  const reason = skipReason();
  if (reason) {
    console.log(`[postinstall-hooks] ${reason}`);
    return;
  }

  const repoRoot = path.resolve(__dirname, "..");
  const hooksDir = path.join(repoRoot, ".githooks");

  if (!fs.existsSync(hooksDir)) {
    console.log("[postinstall-hooks] .githooks directory not present — skipping");
    return;
  }

  if (!isGitRepo(repoRoot)) {
    console.log("[postinstall-hooks] not a git work tree — skipping");
    return;
  }

  const pwshCommand = process.env.POSTINSTALL_HOOKS_PWSH_COMMAND || "pwsh";
  if (!commandExists(pwshCommand)) {
    console.log(
      `[postinstall-hooks] ${pwshCommand} not found — skipping hook install. ` +
        "Install PowerShell 7+ and run 'npm run hooks:install' to enable hooks."
    );
    return;
  }

  const existing = currentHooksPath(repoRoot);
  const normalizedExisting = normalizeHooksPath(existing);
  const expectedHooksPath = normalizeHooksPath(".githooks");
  if (normalizedExisting === expectedHooksPath) {
    // Already configured — idempotent no-op.
    return;
  }

  if (normalizedExisting !== "") {
    // A custom hooksPath is already set (power-user config) — do NOT overwrite.
    // Surface a hint so the contributor can opt in explicitly if they want this
    // repo's hooks.
    console.log(
      `[postinstall-hooks] core.hooksPath is "${existing}" — leaving unchanged. ` +
        "Run 'npm run hooks:install' if you want this repo's hooks."
    );
    return;
  }

  try {
    execSync("git config core.hooksPath .githooks", { cwd: repoRoot, stdio: "inherit" });
    // Best-effort chmod on Unix; ignored on Windows.
    if (process.platform !== "win32") {
      for (const hook of hookEntryPoints(hooksDir)) {
        const full = path.join(hooksDir, hook);
        try {
          fs.chmodSync(full, 0o755);
        } catch (_err) {
          // Non-fatal: user may already have correct permissions.
        }
      }
    }
    console.log("[postinstall-hooks] git hooks installed (core.hooksPath=.githooks)");
  } catch (err) {
    // Never fail npm install over hook setup — log and move on.
    console.log(
      `[postinstall-hooks] warning: could not configure hooks (${err.message}). Run 'npm run hooks:install' manually.`
    );
  }
}

try {
  main();
} catch (err) {
  console.log(`[postinstall-hooks] warning: ${err.message}`);
}
