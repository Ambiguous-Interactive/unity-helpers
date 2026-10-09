// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import {
  copyFileSync,
  mkdirSync,
  mkdtempSync,
  renameSync,
  rmSync,
  symlinkSync,
  writeFileSync
} from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  batchMarkdownFiles,
  discoverMarkdownFiles,
  selectMarkdownFiles
} from "../lint-markdown.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const root = mkdtempSync(path.join(os.tmpdir(), "markdown-inventory-"));
function write(relative, content = "# Valid\n") {
  const destination = path.join(root, relative);
  mkdirSync(path.dirname(destination), { recursive: true });
  writeFileSync(destination, content);
}
function git(...args) {
  const result = spawnSync("git", args, { cwd: root, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
}
function run() {
  return spawnSync(process.execPath, [path.join(root, "scripts/lint-markdown.mjs")], {
    cwd: root,
    encoding: "utf8"
  });
}

try {
  git("init", "--quiet");
  write(".gitignore", "progress/\nnode_modules/\ntracked-ignore.md\n");
  write(".markdownlintignore", "progress/\nnode_modules/\nexcluded.md\n");
  write(".markdownlint.json", '{"default":true,"MD041":false}\n');
  write("tracked-ignore.md");
  git("add", "--force", "tracked-ignore.md");
  write("tracked.md");
  git("add", "tracked.md");
  write("new document.markdown");
  write("excluded.md", "```\ninvalid\n```\n");
  write("progress/ignored.md", "```\ninvalid\n```\n");
  write(".hidden.md", "```\ninvalid\n```\n");
  write(".hidden/nested.md", "```\ninvalid\n```\n");
  write("docs/.hidden/nested.md", "```\ninvalid\n```\n");
  write("upper.MD");
  write("deleted.md");
  git("add", "deleted.md");
  rmSync(path.join(root, "deleted.md"));
  mkdirSync(path.join(root, "scripts"), { recursive: true });
  for (const file of ["lint-markdown.mjs", "run-node-bin.js"]) {
    copyFileSync(path.join(repoRoot, "scripts", file), path.join(root, "scripts", file));
  }
  symlinkSync(
    path.join(repoRoot, "node_modules"),
    path.join(root, "node_modules"),
    process.platform === "win32" ? "junction" : "dir"
  );

  const expected = ["new document.markdown", "tracked-ignore.md", "tracked.md"];
  if (process.platform === "win32" || process.platform === "darwin") {
    expected.push("upper.MD");
  }
  assert.deepEqual(discoverMarkdownFiles(root), expected.sort());
  assert.deepEqual(
    selectMarkdownFiles(
      "a.md\0a.md\0docs/a.markdown\0.hidden.md\0.hidden/b.md\0a.txt\0",
      "docs/\n"
    ),
    ["a.md"]
  );
  assert.deepEqual(selectMarkdownFiles("docs/skip.md\0docs/keep.md\0", "docs/*\n!docs/keep.md\n"), [
    "docs/keep.md"
  ]);
  assert.throws(() => batchMarkdownFiles(["a".repeat(12000) + ".md"]), /argument budget/);
  const batches = batchMarkdownFiles(
    Array.from({ length: 500 }, (_, index) => `docs/${index}-${"a".repeat(100)}.md`)
  );
  assert.ok(batches.length > 1);
  assert.equal(batches.flat().length, 500);
  assert.ok(
    batches.every(
      (batch) => batch.reduce((sum, file) => sum + Buffer.byteLength(file) + 3, 0) <= 12000
    )
  );
  let result = run();
  assert.equal(result.status, 0, result.stdout + result.stderr);
  assert.match(result.stdout, new RegExp(`Linting ${expected.length} Markdown files`));

  write("new document.markdown", "# Invalid\n\n```\nmissing language\n```\n");
  result = run();
  assert.equal(result.status, 1, result.stdout + result.stderr);
  assert.match(result.stderr, /new document\.markdown:3 error MD040/);

  for (const file of expected) {
    rmSync(path.join(root, file));
  }
  result = run();
  assert.equal(result.status, 1);
  assert.match(result.stderr, /zero subjects/);

  renameSync(path.join(root, ".git"), path.join(root, ".retired-git"));
  result = run();
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Markdown inventory failed/);
  console.log(
    "Markdown inventory, bounded batches, real lint failure, Git failure and zero-subject controls passed."
  );
} finally {
  rmSync(root, { recursive: true, force: true });
}
