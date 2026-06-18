#!/usr/bin/env node
// =============================================================================
// Claude Code PostToolUse hook: auto-run cspell on the file Claude just touched.
// =============================================================================
// Why this exists:
//   Pre-commit and agent-preflight cspell-lint a wide set of extensions (md,
//   markdown, cs, yml, yaml, json, jsonc, asmdef, asmref, js). Previously the
//   skill guidance told the LLM to run `npm run lint:spelling` after every
//   edit, but cspell.json drift could make one validation surface scan less
//   than another. This hook turns "remember to spell-check" into
//   "spell-check happens automatically after every Edit/Write/MultiEdit/
//   NotebookEdit".
//
// Contract (see https://code.claude.com/docs/en/hooks):
//   - stdin: Claude Code PostToolUse event JSON.
//   - stdout/stderr: cspell diagnostics on failure plus a bucket-pointer note.
//   - Exit 0: success, skip, or unsupported extension.
//   - Exit 2: PostToolUse is non-blocking, but exit 2 routes stderr back to
//     Claude as model-visible feedback. The edit already happened; this
//     surfaces the spell-check failure so Claude fixes it in a follow-up
//     edit or adds the term to cspell.json. Any other non-zero exit code
//     becomes a user-visible (not model-visible) non-blocking error — use 2.
//
// Safety rails:
//   - Never hard-fail if the dev environment lacks node_modules / cspell.
//   - Never hard-fail on malformed JSON -- missing a single post-edit check
//     is still better than bricking every edit.
//   - Skip files outside this repo (e.g. Claude editing its own settings).
//   - Works on any OS Node runs on (no POSIX utility assumptions).
// =============================================================================

"use strict";

const fs = require("fs");
const path = require("path");
const os = require("os");
const { spawnSync } = require("child_process");

const SUPPORTED_EXTENSIONS = new Set([
  ".md",
  ".markdown",
  ".cs",
  ".yml",
  ".yaml",
  ".json",
  ".jsonc",
  ".asmdef",
  ".asmref",
  ".js"
]);

const CSPELL_TIMEOUT_MS = 30000;

function readStdin() {
  try {
    return fs.readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

function parseEvent(raw) {
  if (!raw) {
    return null;
  }
  try {
    return JSON.parse(raw);
  } catch {
    return null;
  }
}

function extractFilePath(event) {
  if (!event || typeof event !== "object") {
    return "";
  }
  const input = event.tool_input;
  if (!input || typeof input !== "object") {
    return "";
  }
  const candidate =
    (typeof input.file_path === "string" && input.file_path) ||
    (typeof input.notebook_path === "string" && input.notebook_path) ||
    (typeof input.path === "string" && input.path) ||
    "";
  return candidate;
}

function expandHome(p) {
  if (!p) {
    return p;
  }
  if (p === "~") {
    return os.homedir();
  }
  if (p.startsWith("~/") || p.startsWith("~\\")) {
    return path.join(os.homedir(), p.slice(2));
  }
  return p;
}

function findRepoRoot(startDir) {
  let dir = startDir;
  // Walk up until we find a .git entry or hit the filesystem root.
  // Supports plain `.git` dirs and `.git` files (worktrees).
  // eslint-disable-next-line no-constant-condition
  while (true) {
    if (fs.existsSync(path.join(dir, ".git"))) {
      return dir;
    }
    const parent = path.dirname(dir);
    if (parent === dir) {
      return null;
    }
    dir = parent;
  }
}

function isInsideRepo(absPath, repoRoot) {
  const rel = path.relative(repoRoot, absPath);
  return rel !== "" && !rel.startsWith("..") && !path.isAbsolute(rel);
}

function resolveNodeToolRunner(repoRoot) {
  const runner = path.join(repoRoot, "scripts", "run-node-bin.js");
  if (!fs.existsSync(runner)) {
    return null;
  }
  return runner;
}

function main() {
  // Any uncaught failure in the setup/detection phase must NOT block edits.
  // We wrap the whole body in try/catch and degrade to exit 0 on anything
  // unexpected.
  let repoRoot;
  let absPath;
  let nodeToolRunner;
  try {
    repoRoot = findRepoRoot(process.cwd());
    if (!repoRoot) {
      return 0;
    }

    const raw = readStdin();
    const event = parseEvent(raw);
    const rawFile = extractFilePath(event);
    if (!rawFile || typeof rawFile !== "string") {
      return 0;
    }

    const expanded = expandHome(rawFile);
    // Claude Code's Edit/Write tools enforce absolute file paths, but resolve
    // defensively in case a relative path sneaks through: relative paths are
    // resolved against the hook's cwd (Claude Code's invocation dir), NOT
    // against repoRoot, because "./foo" from a subdir means cwd-relative.
    absPath = path.isAbsolute(expanded)
      ? path.resolve(expanded)
      : path.resolve(process.cwd(), expanded);

    if (!isInsideRepo(absPath, repoRoot)) {
      return 0;
    }

    const ext = path.extname(absPath).toLowerCase();
    if (!SUPPORTED_EXTENSIONS.has(ext)) {
      return 0;
    }

    // File must exist on disk (the preceding tool may have errored or been
    // a delete).
    let stat;
    try {
      stat = fs.statSync(absPath);
    } catch {
      return 0;
    }
    if (!stat.isFile()) {
      return 0;
    }

    nodeToolRunner = resolveNodeToolRunner(repoRoot);
    if (!nodeToolRunner) {
      // Fresh clone before `npm install` — degrade silently.
      return 0;
    }
  } catch {
    return 0;
  }

  // Cspell invocation. We pass `--` before the file arg so a filename that
  // begins with `-` cannot be parsed as an option.
  let result;
  try {
    result = spawnSync(
      process.execPath,
      [
        nodeToolRunner,
        "cspell",
        "lint",
        "--no-progress",
        "--show-suggestions",
        "--no-must-find-files",
        "--",
        absPath
      ],
      {
        cwd: repoRoot,
        encoding: "utf8",
        timeout: CSPELL_TIMEOUT_MS,
        windowsHide: true
      }
    );
  } catch {
    // Spawn itself failed (OS-level) — do not block edits on infrastructure
    // failure.
    return 0;
  }

  if (result.error) {
    if (result.error.code === "ETIMEDOUT") {
      process.stderr.write(
        `cspell timed out after ${CSPELL_TIMEOUT_MS}ms while linting: ${absPath}\n`
      );
      process.stderr.write(
        "If this file is legitimately too large, skip it by adding its path to cspell.json 'ignorePaths'.\n"
      );
      return 2;
    }
    // Other spawn-layer errors (ENOENT on the binary, EACCES, etc.): do not
    // block the edit. The environment is broken, not the content.
    return 0;
  }

  if (result.status === 0) {
    return 0;
  }

  // Non-zero exit: surface diagnostics plus a bucket-pointer hint.
  if (result.stdout) {
    process.stderr.write(result.stdout);
  }
  if (result.stderr) {
    process.stderr.write(result.stderr);
  }
  process.stderr.write("\n");
  process.stderr.write("Fix typos, or add a valid term with:\n");
  process.stderr.write("  npm run lint:spelling:add -- <bucket> <word>\n");
  process.stderr.write(
    "Buckets: unity-terms | csharp-terms | package-terms | tech-terms | words (root)\n"
  );
  process.stderr.write(
    "See .llm/skills/validate-before-commit.md Rule 4 for bucket selection guidance.\n"
  );
  return 2;
}

process.exit(main());
