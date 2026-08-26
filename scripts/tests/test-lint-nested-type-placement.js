#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/lint-nested-type-placement.js.
//
// The red half is the point (#556): a fixture with a nested type between members MUST be reported,
// and one with it at the end must not. The negative cases are the words that look like a nested
// type and are not -- a generic constraint, a contextual `record` used as a name, a brace inside an
// attribute argument, a brace in a field initializer -- because a false positive here drives a
// rewrite of source that was already correct.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-nested-type-placement.js");
const { maskNoise, regionKeys, analyzeFile, applyEdits } = require(linterPath);

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

function violationsIn(source) {
  return analyzeFile(source).violations;
}

function fixedText(source) {
  let text = source;
  for (let round = 0; round < 12; round += 1) {
    const result = analyzeFile(text);
    if (result.violations.length === 0) {
      return text;
    }
    text = applyEdits(text, result.edits);
  }
  return text;
}

/** Shapes that are NOT a nested type declaration between members. Every one must stay silent. */
const SILENT = [
  ["a nested type already at the end", "class A { int _x; class B { } }"],
  ["the only member", "class A { class B { } }"],
  ["two nested types at the end", "class A { int _x; class B { } class C { } }"],
  [
    "a class constraint, which is not a declaration",
    "class A { void M<T>() where T : class where U : new() { } int _x; }"
  ],
  [
    "a struct constraint",
    "class A { void M<T>() where T : struct, System.IComparable { } int _x; }"
  ],
  ["`record` used as a field name", "class A { Entry record; int _x; }"],
  ["a brace inside an attribute argument", "class A { [Values(new[] { 1, 2 })] int _x; int _y; }"],
  ["a brace in a field initializer", "class A { int[] _a = { 1, 2 }; int _y; }"],
  [
    "a property with an accessor block and an initializer",
    "class A { int Count { get; } = 5; int _y; }"
  ],
  ["the word class inside a comment", "class A { // class B is elsewhere\n int _x; }"],
  ["the word class inside a string", 'class A { string _s = "class B { }"; int _x; }'],
  ["an enum body, whose members are not types", "enum E { A = 1, B = 2 }"],
  ["a top-level type after another top-level type", "namespace N { class A { } class B { } }"]
];

for (const [name, source] of SILENT) {
  runTest(`silent: ${name}`, () => {
    assert.deepStrictEqual(violationsIn(source), [], `expected no report for: ${source}`);
  });
}

/** Shapes that MUST be reported. If any of these goes quiet, the rule stops being enforced. */
const REPORTED = [
  ["a nested class before a field", "class A { class B { } int _x; }", "B"],
  ["a nested enum before a constant", "class A { enum E { X = 0 } const int C = 1; }", "E"],
  ["a nested struct before a method", "class A { struct S { } void M() { } }", "S"],
  ["a nested interface before a field", "class A { interface I { } int _x; }", "I"],
  ["a nested record before a field", "class A { record R(int X); int _x; }", "R"],
  [
    "a nested type inside a nested type",
    "class A { int _x; class B { class C { } int _y; } }",
    "C"
  ],
  [
    "a nested class between two fields inside a namespace",
    "namespace N { class A { int _x; class B { } int _y; } }",
    "B"
  ]
];

for (const [name, source, expected] of REPORTED) {
  runTest(`reported: ${name}`, () => {
    const violations = violationsIn(source);
    assert.strictEqual(violations.length, 1, `expected exactly one report for: ${source}`);
    assert.strictEqual(violations[0].name, expected);
  });
}

runTest("--fix moves the type to the end and changes nothing else", () => {
  const source = "class A { class B { } int _x; }";
  const result = fixedText(source);
  assert.strictEqual(result, "class A { int _x; class B { } }");
  assert.strictEqual(result.length, source.length, "the rewrite must be a permutation of slices");
});

runTest("--fix carries the doc comment and attributes with the type", () => {
  const source = [
    "class A",
    "{",
    "    /// <summary>Doc.</summary>",
    "    [Serializable]",
    "    private sealed class B { }",
    "",
    "    private int _x;",
    "}",
    ""
  ].join("\n");
  const result = fixedText(source);
  assert.ok(
    result.indexOf("private int _x;") < result.indexOf("/// <summary>Doc.</summary>"),
    `expected the doc comment to travel with the type:\n${result}`
  );
  assert.ok(
    result.indexOf("/// <summary>Doc.</summary>") < result.indexOf("[Serializable]"),
    "the attribute must stay under its doc comment"
  );
  assert.strictEqual(result.length, source.length);
});

runTest("--fix leaves a trailing same-line comment on the member it annotates", () => {
  const source = "class A { int _x; // the x\nclass B { }\nint _y; }";
  const result = fixedText(source);
  assert.ok(result.includes("int _x; // the x"), `the comment must stay with _x, got:\n${result}`);
  assert.strictEqual(result.length, source.length);
});

runTest("--fix keeps nested ordering stable", () => {
  const source = "class A { class B { } class C { } int _x; }";
  assert.strictEqual(fixedText(source), "class A { int _x; class B { } class C { } }");
});

runTest("masking blanks comments and literals without moving anything", () => {
  const source = 'var a = "brace{inside"; // } trailing\n';
  const masked = maskNoise(source);
  assert.strictEqual(masked.length, source.length);
  assert.ok(!masked.includes("{"), "a brace inside a string must not survive masking");
  assert.ok(!masked.includes("}"), "a brace inside a comment must not survive masking");
  assert.strictEqual(masked.split("\n").length, source.split("\n").length);
});

runTest("the linter reports, fixes and then passes over a real tree", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "nested-type-placement-"));
  try {
    const file = path.join(directory, "Sample.cs");
    fs.writeFileSync(
      file,
      "class Sample\n{\n    private class Inner { }\n\n    private int _x;\n}\n"
    );

    const environment = { ...process.env, NESTED_TYPE_PLACEMENT_ROOTS: directory };
    const red = spawnSync(process.execPath, [linterPath], { env: environment, encoding: "utf8" });
    assert.strictEqual(red.status, 1, "expected a violation to fail the linter");
    assert.ok(
      red.stderr.includes("Sample.cs:3:"),
      `expected the file and line in the report, got: ${red.stderr}`
    );

    const fix = spawnSync(process.execPath, [linterPath, "--fix"], {
      env: environment,
      encoding: "utf8"
    });
    assert.strictEqual(fix.status, 0, `--fix should succeed, got: ${fix.stderr}`);
    assert.strictEqual(
      fs.readFileSync(file, "utf8"),
      "class Sample\n{\n    private int _x;\n\n    private class Inner { }\n}\n"
    );

    const green = spawnSync(process.execPath, [linterPath], { env: environment, encoding: "utf8" });
    assert.strictEqual(green.status, 0, `expected clean after --fix, got: ${green.stderr}`);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

runTest("an empty corpus fails rather than reporting a clean scan", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "nested-type-placement-empty-"));
  try {
    const environment = { ...process.env, NESTED_TYPE_PLACEMENT_ROOTS: directory };
    const result = spawnSync(process.execPath, [linterPath], {
      env: environment,
      encoding: "utf8"
    });
    assert.strictEqual(result.status, 1, "a scan that looked at nothing must not report success");
    assert.ok(result.stderr.includes("no C# files found"), result.stderr);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});

runTest("--fix refuses a type that only compiles inside a conditional", () => {
  const source = [
    "class A",
    "{",
    "#if UNITY_EDITOR",
    "    private class B { }",
    "",
    "    private int _x;",
    "#endif",
    "}",
    ""
  ].join("\n");
  assert.strictEqual(violationsIn(source).length, 1, "the violation must still be reported");
  assert.strictEqual(
    fixedText(source),
    source,
    "moving B past the #endif would compile it into every build"
  );
});

runTest("--fix moves an unconditional type past a trailing #endif", () => {
  const source = [
    "class A",
    "{",
    "    private class B { }",
    "",
    "#if UNITY_EDITOR",
    "    private int _x;",
    "#endif",
    "}",
    ""
  ].join("\n");
  const result = fixedText(source);
  assert.ok(
    result.indexOf("#endif") < result.indexOf("private class B { }"),
    `B was unconditional and must stay so:\n${result}`
  );
  assert.strictEqual(result.length, source.length);
});

runTest("--fix still moves a type whose siblings carry a whole conditional inside them", () => {
  const source = [
    "class A",
    "{",
    "    private class B { }",
    "",
    "    private void M()",
    "    {",
    "#if UNITY_EDITOR",
    "        Log();",
    "#endif",
    "    }",
    "}",
    ""
  ].join("\n");
  const result = fixedText(source);
  assert.ok(
    result.indexOf("private void M()") < result.indexOf("private class B { }"),
    `a balanced conditional inside a member must not block the move:\n${result}`
  );
  assert.strictEqual(result.length, source.length);
});

runTest("region keys separate the two branches of one conditional", () => {
  const source = "int before;\n#if A\nint x;\n#else\nint y;\n#endif\nint after;\n";
  const keys = regionKeys(source);
  assert.notStrictEqual(
    keys[source.indexOf("int x;")],
    keys[source.indexOf("int y;")],
    "an #if branch and its #else branch are not the same region"
  );
  assert.strictEqual(keys[source.indexOf("int before;")], "", "outside every conditional");
  assert.strictEqual(
    keys[source.indexOf("int after;")],
    keys[source.indexOf("int before;")],
    "text on both sides of a balanced conditional is the same region"
  );
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
