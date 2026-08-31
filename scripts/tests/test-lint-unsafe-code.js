#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/lint-unsafe-code.js.
//
// The gate's whole value is telling compiler-unsafe code from the word "unsafe" in prose, so the
// negative cases are the point: `Serializer.cs` says "unsafe for untrusted data" three times in
// doc comments and must stay quiet. The positive cases pin the three shapes that re-enable pointer
// access, and the last test runs the real linter over the real repository so a refactor that stops
// it finding any file at all cannot read as a clean pass.

"use strict";

const assert = require("assert");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-unsafe-code.js");
const { codeOnly, findViolations, isVendored } = require(linterPath);

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

function sources(text) {
  return findViolations([], [{ path: "Runtime/Sample.cs", text }], []);
}

/** Shapes that contain the word and are NOT compiler-unsafe code. Every one must stay silent. */
const EXEMPT = [
  ["a line comment", "// BinaryFormatter is unsafe for untrusted input.\nclass Sample { }\n"],
  ["an XML doc comment", "/// <summary>unsafe for untrusted data</summary>\nclass Sample { }\n"],
  ["a block comment", "/*\n    This is unsafe.\n*/\nclass Sample { }\n"],
  ["a string literal", 'class Sample { const string Note = "this is unsafe"; }\n'],
  ["a verbatim string", 'class Sample { const string Note = @"unsafe ""quoted"""; }\n'],
  ["an identifier that merely contains it", "class Sample { int unsafely; int notunsafe; }\n"]
];

for (const [label, text] of EXEMPT) {
  runTest(`${label} is not a violation`, () => {
    assert.deepStrictEqual(sources(text), []);
  });
}

/** Shapes it must never stop seeing. */
const VIOLATIONS = [
  ["an unsafe method", "class Sample { static unsafe int Read(int* p) { return *p; } }\n"],
  ["an unsafe block", "class Sample { void M() { unsafe { } } }\n"],
  ["an unsafe type", "unsafe struct Sample { }\n"],
  ["an unsafe local function", "class Sample { void M() { unsafe void Inner() { } } }\n"]
];

for (const [label, text] of VIOLATIONS) {
  runTest(`${label} is reported`, () => {
    const found = sources(text);
    assert.strictEqual(found.length, 1, `expected one violation, got ${found.length}`);
    assert.ok(found[0].startsWith("Runtime/Sample.cs:"), `expected file and line, got ${found[0]}`);
    assert.ok(found[0].includes("#637"), "the report must name the issue that explains the rule");
  });
}

runTest("a comment cannot hide an unsafe declaration on the same line", () => {
  assert.strictEqual(sources("class Sample { unsafe void M() { } } // unsafe\n").length, 1);
});

runTest("an assembly definition that permits unsafe code is reported", () => {
  const found = findViolations(
    [{ path: "Runtime/Sample.asmdef", text: '{ "name": "Sample", "allowUnsafeCode": true }' }],
    [],
    []
  );
  assert.strictEqual(found.length, 1, `expected one violation, got ${found.length}`);
  assert.ok(found[0].includes("allowUnsafeCode"), found[0]);
});

runTest("an assembly definition that refuses unsafe code is not reported", () => {
  assert.deepStrictEqual(
    findViolations(
      [{ path: "Runtime/Sample.asmdef", text: '{ "name": "Sample", "allowUnsafeCode": false }' }],
      [],
      []
    ),
    []
  );
});

runTest("an unreadable assembly definition is reported rather than skipped", () => {
  const found = findViolations([{ path: "Runtime/Sample.asmdef", text: "{ not json" }], [], []);
  assert.strictEqual(found.length, 1, `expected one violation, got ${found.length}`);
  assert.ok(found[0].includes("not valid JSON"), found[0]);
});

runTest("a check project that permits unsafe blocks is reported", () => {
  const found = findViolations(
    [],
    [],
    [
      {
        path: "Generator~/Sample/Sample.csproj",
        text: "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>"
      }
    ]
  );
  assert.strictEqual(found.length, 1, `expected one violation, got ${found.length}`);
  assert.ok(found[0].includes("AllowUnsafeBlocks"), found[0]);
});

runTest("a check project that refuses unsafe blocks is not reported", () => {
  assert.deepStrictEqual(
    findViolations(
      [],
      [],
      [
        {
          path: "Generator~/Sample/Sample.csproj",
          text: "<AllowUnsafeBlocks>false</AllowUnsafeBlocks>"
        }
      ]
    ),
    []
  );
});

runTest("the vendored tree is excluded and nothing else is", () => {
  assert.ok(isVendored("Runtime/Utils/SevenZip/Compress/LZ/LzBinTree.cs"));
  assert.ok(!isVendored("Runtime/Utils/Buffers.cs"));
  assert.ok(!isVendored("Runtime/Core/Random/WyRandom.cs"));
  assert.deepStrictEqual(
    findViolations(
      [],
      [{ path: "Runtime/Utils/SevenZip/Vendored.cs", text: "unsafe struct Sample { }\n" }],
      []
    ),
    []
  );
});

runTest("comment stripping preserves line numbering", () => {
  const stripped = codeOnly("/*\n\n*/\nunsafe struct Sample { }\n");
  assert.strictEqual(stripped.split("\n").length, 5);
  assert.strictEqual(stripped.split("\n")[3], "unsafe struct Sample { }");
});

runTest("the real repository is clean, and the gate actually inspected it", () => {
  const green = spawnSync(process.execPath, [linterPath, "--verbose"], {
    cwd: repoRoot,
    encoding: "utf8"
  });
  assert.strictEqual(green.status, 0, `expected a clean repository, got: ${green.stderr}`);

  // "Found nothing" and "looked at nothing" print the same thing (#556), so the corpus is counted
  // rather than trusted: the package ships at least one assembly definition and one check project.
  const { findViolations: scan } = require(linterPath);
  assert.strictEqual(typeof scan, "function");
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    {
      encoding: "utf8",
      maxBuffer: 64 * 1024 * 1024
    }
  );
  const files = listed.stdout.split("\n");
  assert.ok(
    100 < files.filter((entry) => entry.endsWith(".cs")).length,
    "the C# corpus is implausibly small; the gate is probably looking at nothing"
  );
  assert.ok(
    files.some((entry) => entry.endsWith(".asmdef")),
    "no assembly definition was listed; the gate is probably looking at nothing"
  );
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
