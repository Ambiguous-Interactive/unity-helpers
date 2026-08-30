#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Contract tests for scripts/extract-doc-samples.js.
//
// The red half is the point (#556), and this gate has three rules that can report, so it has three:
// a marker that claims nothing, a marked sample carrying an elision it cannot compile with, and a
// tree where no sample is marked at all -- which is the "checked nothing" shape that looks exactly
// like a clean run unless something refuses it.
//
// The fourth thing worth pinning is the wrapper CHOICE. Documentation for an attribute is written
// as the member it decorates, and wrapping that in a namespace reported CS0116 -- the author's
// sample blamed for the gate's mistake. That decision is a pure function of the block's text, so it
// is asserted here rather than left to a compile.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const scriptPath = path.join(repoRoot, "scripts", "extract-doc-samples.js");

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

/**
 * Writes a fixture documentation tree and runs the extractor over it.
 *
 * @param {string} markdown The single documentation page's contents.
 * @returns {{status: number, stdout: string, stderr: string, files: string[]}} What happened.
 */
function extract(markdown) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "doc-samples-"));
  const docs = path.join(root, "docs");
  fs.mkdirSync(docs, { recursive: true });
  fs.writeFileSync(path.join(docs, "page.md"), markdown, "utf8");
  const out = path.join(root, "out");

  const result = spawnSync(process.execPath, [scriptPath], {
    cwd: repoRoot,
    encoding: "utf8",
    env: { ...process.env, DOC_SAMPLES_ROOT: root, DOC_SAMPLES_OUT: out }
  });

  const files = fs.existsSync(out)
    ? fs.readdirSync(out).map((name) => fs.readFileSync(path.join(out, name), "utf8"))
    : [];
  fs.rmSync(root, { recursive: true, force: true });
  return { status: result.status, stdout: result.stdout, stderr: result.stderr, files };
}

const TYPE_SAMPLE = [
  "# Page",
  "",
  "<!-- doc-sample: compiles -->",
  "```csharp",
  "public sealed class Example",
  "{",
  "    public int Value;",
  "}",
  "```",
  ""
].join("\n");

runTest("a marked type sample is extracted into its own namespace", () => {
  const result = extract(TYPE_SAMPLE);
  assert.strictEqual(result.status, 0, result.stderr);
  assert.strictEqual(result.files.length, 1, "exactly one sample should be extracted");
  assert.ok(
    result.files[0].includes("namespace WallstopStudios.UnityHelpers.DocSamples."),
    "a type sample is wrapped in a namespace of its own"
  );
  assert.ok(
    !result.files[0].includes("class DocSample : UnityEngine.MonoBehaviour"),
    "a type sample must not also get the member wrapper"
  );
  assert.ok(
    /Samples compiled: 1\b/.test(result.stdout),
    `the checked count has to be reported: ${result.stdout}`
  );
});

runTest("an unmarked sample is counted but not extracted", () => {
  const result = extract(TYPE_SAMPLE.replace("<!-- doc-sample: compiles -->\n", ""));
  assert.strictEqual(result.status, 1, "a tree with nothing marked has checked nothing");
  assert.strictEqual(result.files.length, 0);
  assert.ok(
    /1 declaration-shaped/.test(result.stdout),
    `an unmarked declaration-shaped block is still counted: ${result.stdout}`
  );
});

runTest("a member sample is wrapped in a MonoBehaviour rather than a bare namespace", () => {
  // The rule that decides the wrapper. A namespace cannot directly contain a field, so getting
  // this wrong reports CS0116 against documentation that is correct.
  const result = extract(
    [
      "<!-- doc-sample: compiles -->",
      "```csharp",
      "[SerializeField]",
      "private int _value;",
      "```",
      ""
    ].join("\n")
  );
  assert.strictEqual(result.status, 0, result.stderr);
  assert.strictEqual(result.files.length, 1);
  assert.ok(
    result.files[0].includes("class DocSample : UnityEngine.MonoBehaviour"),
    "a member sample needs a type to live on"
  );
});

runTest("a type declared inside a method body does not change the wrapper", () => {
  // `declaresType` walks brace depth for exactly this: a local function or a nested type inside a
  // member block is not the sample declaring a type of its own.
  const result = extract(
    [
      "<!-- doc-sample: compiles -->",
      "```csharp",
      "private void Start()",
      "{",
      "    Debug.Log(nameof(Start));",
      "}",
      "```",
      ""
    ].join("\n")
  );
  assert.strictEqual(result.status, 0, result.stderr);
  assert.ok(
    result.files[0].includes("class DocSample : UnityEngine.MonoBehaviour"),
    "a method is a member, whatever it contains"
  );
});

runTest("a sample's own using directives are hoisted above its declarations", () => {
  // C# refuses a `using` after a type declaration, so a sample that opens with a type and imports
  // something later would be a syntax error the gate blamed on the author.
  const result = extract(
    [
      "<!-- doc-sample: compiles -->",
      "```csharp",
      "public sealed class Example { }",
      "",
      "using System.Text;",
      "```",
      ""
    ].join("\n")
  );
  assert.strictEqual(result.status, 0, result.stderr);
  const text = result.files[0];
  assert.ok(
    text.indexOf("using System.Text;") < text.indexOf("public sealed class Example"),
    "the hoisted using has to precede the declaration"
  );
});

runTest("RED: a marker that is not followed by a fence is reported", () => {
  const result = extract(
    ["<!-- doc-sample: compiles -->", "", "Some prose, and no code block.", ""].join("\n")
  );
  assert.strictEqual(result.status, 1, "a marker claiming nothing must fail the gate");
  assert.ok(
    result.stderr.includes("adds nothing to the checked corpus"),
    `the reason has to name the misplaced marker: ${result.stderr}`
  );
});

runTest("RED: a marked sample containing an elision is reported", () => {
  const result = extract(
    [
      "<!-- doc-sample: compiles -->",
      "```csharp",
      "public sealed class Example",
      "{",
      "    // ...",
      "}",
      "```",
      ""
    ].join("\n")
  );
  assert.strictEqual(result.status, 1, "an elided sample cannot compile, so the claim is wrong");
  assert.ok(
    result.stderr.includes("contains an elision"),
    `the reason has to name the elision: ${result.stderr}`
  );
});

runTest("RED: a marked sample carrying an assembly attribute is reported", () => {
  const result = extract(
    [
      "<!-- doc-sample: compiles -->",
      "```csharp",
      '[assembly: System.Reflection.AssemblyMetadata("k", "v")]',
      "public sealed class Example { }",
      "```",
      ""
    ].join("\n")
  );
  assert.strictEqual(result.status, 1);
  assert.ok(
    result.stderr.includes("[assembly: ...]"),
    `the reason has to name the attribute: ${result.stderr}`
  );
});

runTest(
  "RED: a documentation tree with nothing marked fails rather than reporting a clean sweep",
  () => {
    const result = extract("# Page\n\nNo code at all.\n");
    assert.strictEqual(result.status, 1, "checking nothing is not passing");
    assert.ok(
      result.stderr.includes("checked nothing"),
      `the reason has to say the corpus was empty: ${result.stderr}`
    );
  }
);

runTest("the repository's own corpus is not empty", () => {
  // The count this gate reports is its own coverage. If it ever reaches zero the compile step
  // succeeds trivially, so the number is asserted here rather than only printed.
  const result = spawnSync(process.execPath, [scriptPath], { cwd: repoRoot, encoding: "utf8" });
  assert.strictEqual(result.status, 0, result.stderr);
  const match = /Samples compiled: (\d+)/.exec(result.stdout);
  assert.ok(match, `the run has to report a count: ${result.stdout}`);
  assert.ok(
    50 <= Number(match[1]),
    `the corpus shrank to ${match[1]} samples; a gate that checks a handful is not the gate #611 asked for`
  );
});

console.log(`\n[test-extract-doc-samples] ${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.log(`[test-extract-doc-samples] failing: ${failures.join(", ")}`);
  process.exit(1);
}
