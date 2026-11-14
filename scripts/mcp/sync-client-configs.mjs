#!/usr/bin/env node

// Mirrors the generated MCP client configs into every persistent git worktree and merges the
// OpenCode server map into the user's global OpenCode config. Secrets never leave the generated
// files: each copy retains mode 0600 and this script prints counts only.

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const SCRIPT_DIR = path.dirname(fileURLToPath(import.meta.url));
const DEFAULT_REPO_ROOT = path.resolve(SCRIPT_DIR, "..", "..");
const CONFIG_FILES = [
  ".mcp.json",
  ".cursor/mcp.json",
  ".vscode/mcp.json",
  ".codex/config.toml",
  "opencode.json",
  ".nanocoder/mcp.json",
  ".copilot/mcp-config.json"
];

function parseArguments(argv) {
  const options = {
    repoRoot: process.env.MCP_SYNC_REPO_ROOT || DEFAULT_REPO_ROOT,
    home: process.env.MCP_SYNC_HOME || os.homedir()
  };
  for (let index = 0; index < argv.length; ++index) {
    if (argv[index] === "--repo-root" && index + 1 < argv.length) {
      options.repoRoot = path.resolve(argv[++index]);
    } else if (argv[index] === "--home" && index + 1 < argv.length) {
      options.home = path.resolve(argv[++index]);
    }
  }
  return options;
}

function stripJsonComments(text) {
  let result = "";
  let inString = false;
  let escaped = false;
  for (let index = 0; index < text.length; ++index) {
    const character = text[index];
    const next = text[index + 1];
    if (inString) {
      result += character;
      if (escaped) {
        escaped = false;
      } else if (character === "\\") {
        escaped = true;
      } else if (character === '"') {
        inString = false;
      }
      continue;
    }
    if (character === '"') {
      inString = true;
      result += character;
      continue;
    }
    if (character === "/" && next === "/") {
      while (index < text.length && text[index] !== "\n") {
        ++index;
      }
      result += "\n";
      continue;
    }
    if (character === "/" && next === "*") {
      index += 2;
      while (index < text.length && !(text[index] === "*" && text[index + 1] === "/")) {
        ++index;
      }
      ++index;
      continue;
    }
    result += character;
  }
  return result.replace(/,\s*([}\]])/g, "$1");
}

function readJsonc(filePath) {
  try {
    return JSON.parse(stripJsonComments(fs.readFileSync(filePath, "utf8")));
  } catch {
    return null;
  }
}

function writeJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true, mode: 0o700 });
  const temporary = `${filePath}.tmp-${process.pid}`;
  fs.writeFileSync(temporary, `${JSON.stringify(value, null, 2)}\n`, { mode: 0o600 });
  fs.chmodSync(temporary, 0o600);
  fs.renameSync(temporary, filePath);
  fs.chmodSync(filePath, 0o600);
}

function copyConfig(source, target) {
  fs.mkdirSync(path.dirname(target), { recursive: true, mode: 0o700 });
  fs.writeFileSync(target, fs.readFileSync(source), { mode: 0o600 });
  fs.chmodSync(target, 0o600);
}

function isPersistentWorktree(worktree, repoRoot, home) {
  const resolved = path.resolve(worktree);
  return (
    resolved !== path.resolve(repoRoot) &&
    (resolved.startsWith(`${home}${path.sep}`) || resolved.startsWith(`${repoRoot}${path.sep}`))
  );
}

function listWorktrees(repoRoot) {
  const result = spawnSync("git", ["-C", repoRoot, "worktree", "list", "--porcelain"], {
    encoding: "utf8"
  });
  if (result.status !== 0) {
    return [];
  }
  const worktrees = [];
  for (const line of result.stdout.split("\n")) {
    if (line.startsWith("worktree ")) {
      worktrees.push(line.slice("worktree ".length));
    }
  }
  return worktrees;
}

function syncWorktrees(repoRoot, home) {
  let copiedWorktrees = 0;
  for (const worktree of listWorktrees(repoRoot)) {
    if (!fs.existsSync(worktree) || !isPersistentWorktree(worktree, repoRoot, home)) {
      continue;
    }
    let copied = 0;
    for (const relative of CONFIG_FILES) {
      const source = path.join(repoRoot, relative);
      if (!fs.existsSync(source)) {
        continue;
      }
      copyConfig(source, path.join(worktree, relative));
      ++copied;
    }
    const envSource = path.join(repoRoot, ".env.local");
    if (fs.existsSync(envSource)) {
      copyConfig(envSource, path.join(worktree, ".env.local"));
      ++copied;
    }
    if (copied !== 0) {
      ++copiedWorktrees;
    }
  }
  return copiedWorktrees;
}

function syncGlobalOpenCode(repoRoot, home) {
  const sourcePath = path.join(repoRoot, "opencode.json");
  if (!fs.existsSync(sourcePath)) {
    return 0;
  }
  const source = readJsonc(sourcePath);
  if (!source || !source.mcp || typeof source.mcp !== "object") {
    return 0;
  }
  const configDirectory = path.join(home, ".config", "opencode");
  const candidates = ["opencode.jsonc", "opencode.json"];
  let globalPath = path.join(configDirectory, candidates[0]);
  for (const candidate of candidates) {
    const candidatePath = path.join(configDirectory, candidate);
    if (fs.existsSync(candidatePath)) {
      globalPath = candidatePath;
      break;
    }
  }
  const existing = fs.existsSync(globalPath) ? readJsonc(globalPath) : null;
  if (fs.existsSync(globalPath) && (!existing || typeof existing !== "object")) {
    // Never replace a config this script cannot parse; its other settings are the user's.
    return 0;
  }
  const merged = existing && typeof existing === "object" ? existing : {};
  if (typeof source.$schema === "string") {
    merged.$schema = source.$schema;
  }
  merged.mcp = source.mcp;
  writeJson(globalPath, merged);
  return Object.keys(source.mcp).length;
}

const options = parseArguments(process.argv.slice(2));
const worktrees = syncWorktrees(options.repoRoot, options.home);
const globalServers = syncGlobalOpenCode(options.repoRoot, options.home);
console.log(
  `Synced MCP configs to ${worktrees} persistent worktree(s); global OpenCode config has ${globalServers} server(s).`
);
