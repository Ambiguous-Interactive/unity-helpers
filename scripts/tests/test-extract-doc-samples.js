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
// The fourth thing worth pinning is the SCOPE each of a block's items is put in. Documentation for
// an attribute is written as the member it decorates, and wrapping that in a namespace reported
// CS0116 -- the author's sample blamed for the gate's mistake. Every rule here was paid for by a
// real sample: an expression-bodied `void Awake() => ...` read as a statement, a trailing comment
// read as one, a brace inside a string literal closing a scope that never opened, and a method
// separated from the local it closes over. That sort is a pure function of the block's text, so it
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

runTest("an unmarked block is counted by the sort that decides its wrapper", () => {
  /*
   * The report IS the gate's coverage, so the column a block lands in has to be the scope it would
   * actually be compiled into. Reading the shape off the first line instead put every block opening
   * with a `using` directive in the statement column: 448 of 646 were called usage-shaped, and 153
   * of those were not.
   */
  const result = extract(
    [
      "```csharp",
      "using UnityEngine;",
      "",
      "public sealed class Example : MonoBehaviour { }",
      "```",
      "",
      "```csharp",
      "[SerializeField]",
      "private int _value;",
      "```",
      "",
      "```csharp",
      "int index = 0;",
      "Debug.Log(index);",
      "```",
      ""
    ].join("\n")
  );
  assert.ok(
    /1 declaration-shaped, 1 member-shaped, 1 statement-shaped/.test(result.stdout),
    `each block belongs in its own column: ${result.stdout}`
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
  // The item scan walks brace depth for exactly this: a local function or a nested type inside a
  // member block is not the sample declaring a type of its own, and must not be lifted out of it.
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

/**
 * Extracts a single marked block and returns the compilation unit it produced.
 *
 * @param {string[]} body The block's lines.
 * @returns {string} The rendered file.
 */
function unitFor(body) {
  const result = extract(
    ["<!-- doc-sample: compiles -->", "```csharp", ...body, "```", ""].join("\n")
  );
  assert.strictEqual(result.status, 0, result.stderr);
  assert.strictEqual(result.files.length, 1, "exactly one sample should be extracted");
  return result.files[0];
}

runTest("a statement sample is wrapped in a method body", () => {
  // The third shape, and the largest one in the tree: statements fit in neither a namespace nor a
  // class, and wrapping them as members reported CS1519 against documentation that is correct.
  const unit = unitFor(["int index = -1;", "index = index.PositiveMod(5);"]);
  assert.ok(
    unit.includes("private void DocSampleUsage()"),
    `statements need a method body to live in:\n${unit}`
  );
});

runTest("a statement sample that yields is wrapped in an IEnumerator", () => {
  const unit = unitFor(["yield return null;", "Debug.Log(nameof(Debug));"]);
  assert.ok(
    unit.includes("System.Collections.IEnumerator DocSampleUsage()"),
    `a coroutine sample has to be given a coroutine signature:\n${unit}`
  );
});

runTest("a statement sample that awaits is wrapped in an async Task", () => {
  const unit = unitFor(["await System.Threading.Tasks.Task.Yield();"]);
  assert.ok(
    unit.includes("async System.Threading.Tasks.Task DocSampleUsage()"),
    `an awaiting sample has to be given an async signature:\n${unit}`
  );
});

runTest("an expression-bodied member is a member, not a statement", () => {
  // Measured: `void Awake() => this.AssignRelationalComponents();` reads as a statement to anything
  // that looks for `=`, and routing it to a method body reported CS0106 on two marked samples that
  // had been green.
  const unit = unitFor([
    "[SiblingComponent] private SpriteRenderer sprite;",
    "",
    "void Awake() => this.AssignRelationalComponents();"
  ]);
  assert.ok(
    !unit.includes("DocSampleUsage"),
    `a method declaration is a member however it is written:\n${unit}`
  );
});

runTest("a statement that opens with a keyword is not read as a method declaration", () => {
  // `return Compute(x);` is two identifiers and a parameter list, which is exactly the shape of a
  // method signature. The keyword is what tells them apart.
  const unit = unitFor(["int value = 1;", "if (0 < value)", "{", "    return;", "}"]);
  assert.ok(
    unit.includes("private void DocSampleUsage()"),
    `a keyword-led line is a statement:\n${unit}`
  );
});

runTest("a type declared beside a field is lifted out of the class the field needs", () => {
  // A namespace cannot hold the field and a method body cannot hold the type, so the block is
  // sorted rather than wrapped: the enum to the namespace, the field to the class.
  const unit = unitFor([
    "public enum WeaponType { Melee, Ranged }",
    "",
    "[WShowIf(nameof(weapon), WeaponType.Ranged)]",
    "public int ammo;"
  ]);
  assert.ok(
    unit.indexOf("public enum WeaponType") <
      unit.indexOf("class DocSample : UnityEngine.MonoBehaviour"),
    `the type belongs outside the class the field lives on:\n${unit}`
  );
  assert.ok(!unit.includes("DocSampleUsage"), "a field is not a statement");
});

runTest("a type declared beside statements is lifted, and the statements still see it", () => {
  // 21 blocks in the tree declare a small helper type and then use it. No single wrapper holds one:
  // the namespace refuses the statements and the method body refuses the type.
  const unit = unitFor([
    "public enum Stance { Idle, Crouched }",
    "",
    "Stance stance = Stance.Crouched;",
    "Debug.Log(stance);"
  ]);
  assert.ok(
    unit.indexOf("public enum Stance") < unit.indexOf("private void DocSampleUsage()"),
    `the type has to precede the method that uses it, outside it:\n${unit}`
  );
  assert.ok(
    unit.indexOf("private void DocSampleUsage()") < unit.indexOf("Stance stance ="),
    `the statements have to stay inside the method body:\n${unit}`
  );
});

runTest("a method is not separated from the local it closes over", () => {
  // Measured: `void OnDestroy() { proxy.OnCollisionEnter -= ...; }` beside `CollisionProxy proxy =
  // ...;` compiles ONLY as a local function beside that local. Sorting members away from statements
  // broke two green samples, so only TYPES are lifted.
  const unit = unitFor([
    "GameObject held = gameObject;",
    "",
    "void OnDestroy()",
    "{",
    "    Destroy(held);",
    "}"
  ]);
  const method = unit.indexOf("private void DocSampleUsage()");
  assert.ok(
    method < unit.indexOf("void OnDestroy()"),
    `a method that reads a local has to stay beside it, as a local function:\n${unit}`
  );
});

runTest("a trailing comment does not turn a member block into a statement block", () => {
  // The commonest last item in a documentation block is a comment. Reading it as a statement moved
  // eight member-only samples into a method body and reported CS0106 on all of them.
  const unit = unitFor([
    "[WNotNull]",
    "public GameObject target;",
    '// Inspector shows: "target must be assigned"'
  ]);
  assert.ok(!unit.includes("DocSampleUsage"), `a comment is not a statement:\n${unit}`);
});

runTest("a brace inside a string literal does not move the scope", () => {
  // The classifier counts braces, and raw text lies: `"} // done"` closes a scope that never
  // opened and truncates the line at a comment that is not one.
  const unit = unitFor([
    "public sealed class Example",
    "{",
    '    public string Text = "} // not a comment";',
    "}"
  ]);
  assert.ok(
    !unit.includes("class DocSample : UnityEngine.MonoBehaviour"),
    `the literal must not split the type into two items:\n${unit}`
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
    300 <= Number(match[1]),
    `the corpus shrank to ${match[1]} samples; a gate that checks a handful is not the gate #611 asked for`
  );
});

console.log(`\n[test-extract-doc-samples] ${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.log(`[test-extract-doc-samples] failing: ${failures.join(", ")}`);
  process.exit(1);
}
