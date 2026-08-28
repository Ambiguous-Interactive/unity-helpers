#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Contract tests for scripts/lint-xml-doc-summaries.js.
//
// The red half is the point (#556): a doc block with two <summary> tags MUST be reported, and the
// linter must be able to fail from the command line, not only from a unit call. The negative cases
// are the shapes that carry the word "summary" and are not a second one -- an escaped tag inside a
// <code> sample, two members' blocks separated by a declaration, and a summary opened and closed on
// one line -- because a false positive here deletes documentation that was already correct.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-xml-doc-summaries.js");
const { analyzeFile } = require(linterPath);

let passed = 0;
let failed = 0;
const failures = [];

function runTest(name, body) {
  try {
    body();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (error) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${error.message}`);
    failed++;
    failures.push(name);
  }
}

const orphanedBlock = [
  "namespace Sample",
  "{",
  "    public sealed class Widget",
  "    {",
  "        /// <summary>",
  "        /// Documents a method that no longer exists.",
  "        /// </summary>",
  "        /// <summary>",
  "        /// Documents this one.",
  "        /// </summary>",
  "        public void Draw() { }",
  "    }",
  "}"
].join("\n");

const separateBlocks = [
  "namespace Sample",
  "{",
  "    public sealed class Widget",
  "    {",
  "        /// <summary>First.</summary>",
  "        public void Draw() { }",
  "",
  "        /// <summary>Second.</summary>",
  "        public void Hide() { }",
  "    }",
  "}"
].join("\n");

const escapedInsideSample = [
  "namespace Sample",
  "{",
  "    public sealed class Widget",
  "    {",
  "        /// <summary>",
  "        /// Shows how a doc comment is written:",
  "        /// <code>",
  "        /// /// &lt;summary&gt;Text.&lt;/summary&gt;",
  "        /// </code>",
  "        /// </summary>",
  "        public void Draw() { }",
  "    }",
  "}"
].join("\n");

runTest("a block that opens <summary> twice is reported at the block's first line", () => {
  const violations = analyzeFile(orphanedBlock);
  assert.strictEqual(violations.length, 1, "one violation expected");
  assert.strictEqual(violations[0].count, 2);
  assert.strictEqual(violations[0].line, 5, "reported at the line the block starts on");
});

runTest("two members' doc blocks are two blocks, not one", () => {
  assert.deepStrictEqual(analyzeFile(separateBlocks), []);
});

runTest("an escaped <summary> inside a <code> sample is not a second summary", () => {
  assert.deepStrictEqual(analyzeFile(escapedInsideSample), []);
});

runTest("a summary opened and closed on one line counts once", () => {
  assert.deepStrictEqual(
    analyzeFile(["/// <summary>Only one.</summary>", "public void Draw() { }"].join("\n")),
    []
  );
});

runTest("a file with no doc comments at all is clean", () => {
  assert.deepStrictEqual(analyzeFile("public sealed class Widget { }"), []);
});

runTest("CRLF source is analyzed the same as LF", () => {
  assert.strictEqual(analyzeFile(orphanedBlock.replace(/\n/g, "\r\n")).length, 1);
});

runTest("the linter exits non-zero on a fixture tree that violates the rule", () => {
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "xml-doc-summaries-"));
  try {
    const sourceDirectory = path.join(scratch, "Runtime");
    fs.mkdirSync(sourceDirectory, { recursive: true });
    fs.writeFileSync(path.join(sourceDirectory, "Widget.cs"), orphanedBlock, "utf8");

    const red = spawnSync(process.execPath, [linterPath, "--verbose"], {
      encoding: "utf8",
      env: { ...process.env, XML_DOC_SUMMARY_ROOTS: sourceDirectory }
    });
    assert.strictEqual(red.status, 1, "a violating tree must exit 1");
    assert.ok(
      red.stderr.includes("Widget.cs"),
      `the offending file must be named, got: ${red.stderr}`
    );

    fs.writeFileSync(path.join(sourceDirectory, "Widget.cs"), separateBlocks, "utf8");
    const green = spawnSync(process.execPath, [linterPath, "--verbose"], {
      encoding: "utf8",
      env: { ...process.env, XML_DOC_SUMMARY_ROOTS: sourceDirectory }
    });
    assert.strictEqual(green.status, 0, `a clean tree must exit 0, got: ${green.stderr}`);
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
});

console.log(`\n[test-lint-xml-doc-summaries] ${passed} passed, ${failed} failed.`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exitCode = 1;
}
