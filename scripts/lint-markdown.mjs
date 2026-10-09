// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import { spawnSync } from "node:child_process";
import { readFileSync, statSync } from "node:fs";
import { createRequire } from "node:module";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const cliRequire = createRequire(require.resolve("markdownlint-cli/package.json"));
const ignore = cliRequire("ignore");
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export function selectMarkdownFiles(inventory, ignoreText) {
  const ignored = ignore().add(ignoreText);
  const insensitive = process.platform === "win32" || process.platform === "darwin";
  return [...new Set(inventory.split("\0").filter(Boolean))]
    .filter((file) => {
      const extension = insensitive
        ? path.posix.extname(file).toLowerCase()
        : path.posix.extname(file);
      return (
        (extension === ".md" || extension === ".markdown") &&
        !file.split("/").some((part) => part.startsWith(".")) &&
        !ignored.ignores(file)
      );
    })
    .sort();
}

export function discoverMarkdownFiles(root) {
  const result = spawnSync("git", ["ls-files", "-co", "--exclude-standard", "-z"], {
    cwd: root,
    encoding: "utf8",
    maxBuffer: 32 * 1024 * 1024,
    windowsHide: true
  });
  if (result.error || result.status !== 0) {
    throw new Error(`Markdown inventory failed: ${result.error?.message || result.stderr.trim()}`);
  }
  const files = selectMarkdownFiles(
    result.stdout,
    readFileSync(path.join(root, ".markdownlintignore"), "utf8")
  );
  return files.filter((file) => {
    try {
      return statSync(path.join(root, file)).isFile();
    } catch (error) {
      if (error.code === "ENOENT") {
        return false;
      }
      throw error;
    }
  });
}

export function batchMarkdownFiles(files) {
  const batches = [];
  let batch = [];
  let bytes = 0;
  for (const file of files) {
    const size = Buffer.byteLength(file) + 3;
    if (size > 12000) {
      throw new Error(`Markdown path exceeds the command argument budget: ${file}`);
    }
    if (batch.length > 0 && bytes + size > 12000) {
      batches.push(batch);
      batch = [];
      bytes = 0;
    }
    batch.push(file);
    bytes += size;
  }
  if (batch.length > 0) {
    batches.push(batch);
  }
  return batches;
}

function main() {
  const files = discoverMarkdownFiles(repoRoot);
  if (files.length === 0) {
    throw new Error("Markdown lint found zero subjects.");
  }
  console.log(`Linting ${files.length} Markdown files from the Git inventory.`);
  let status = 0;
  for (const batch of batchMarkdownFiles(files)) {
    const result = spawnSync(
      process.execPath,
      [
        path.join(repoRoot, "scripts", "run-node-bin.js"),
        "markdownlint",
        "--config",
        ".markdownlint.json",
        "--ignore-path",
        ".markdownlintignore",
        "--",
        ...batch
      ],
      { cwd: repoRoot, stdio: "inherit", windowsHide: true }
    );
    if (result.error) {
      throw result.error;
    }
    if (result.status !== 0) {
      status = result.status || 1;
    }
  }
  return status;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main();
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
