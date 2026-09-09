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
const {
  maskNoise,
  regionKeys,
  analyzeFile,
  applyEdits,
  ORDER_TIERS
} = require(linterPath);

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

/** Shapes that are NOT a nested type declaration between members, and shapes the #672 member
 * ordering already accepts. Every one must stay silent. */
const SILENT = [
  ["a nested type already at the end", "class A { int _x; class B { } }"],
  ["the only member", "class A { class B { } }"],
  ["two nested types at the end", "class A { int _x; class B { } class C { } }"],
  [
    "a class constraint, which is not a declaration",
    "class A { int _x; void M<T>() where T : class where U : new() { } }"
  ],
  [
    "a struct constraint",
    "class A { int _x; void M<T>() where T : struct, System.IComparable { } }"
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
  ["a top-level type after another top-level type", "namespace N { class A { } class B { } }"],
  // --- #672 member ordering, accepted shapes -----------------------------------------
  [
    "the canonical tier order",
    [
      "class A",
      "{",
      "    public const int PublicConstant = 1;",
      "    private const int Constant = 2;",
      "    public static int PublicStaticProperty { get; set; }",
      "    private static int StaticProperty { get; set; }",
      "    public static readonly int PublicStaticField = 3;",
      "    private static int _staticField;",
      "    public int PublicProperty { get; set; }",
      "    private int Property => 4;",
      "    public readonly int PublicField = 5;",
      "    private int _field;",
      "    public A() { }",
      "    private A(int value) { _field = value; }",
      "    public static void PublicStaticMethod() { }",
      "    private static void StaticMethod() { }",
      "    public void PublicMethod() { }",
      "    private void Method() { }",
      "}"
    ].join("\n")
  ],
  [
    "an expression-bodied property before a field",
    "class A { int Count => 5; int _y; }"
  ],
  [
    "a tuple-typed field and a tuple-typed property",
    "class A { (int X, int Y) Point { get; set; } (int X, int Y) _point; }"
  ],
  ["an indexer is a property", "class A { int this[int i] => i; int _y; }"],
  [
    "an operator overload is a static method",
    "class A { public static A operator +(A left, A right) => left; }"
  ],
  ["a static constructor sorts with the constructors", "class A { static A() { } A(int v) { } }"],
  [
    "a destructor sorts with the constructors",
    "class A { ~A() { } public void Method() { } }"
  ],
  [
    "an event and a delegate sort after const, before static properties",
    "class A { private const int C = 1; private event System.Action Happened; private delegate void Handler(int value); private static int P { get; set; } }"
  ],
  [
    "an accessor-less auto-property before its field",
    "class A { public int Count { get; set; } private int _count; }"
  ],
  [
    "an object-initializer chain that continues past its closing brace",
    [
      "class A",
      "{",
      "    private const char C = 'x';",
      "    private static readonly ImmutableHashSet<char> WordSeparators = new HashSet<char>",
      "    {",
      "        '_',",
      "    }.ToImmutableHashSet();",
      "    private int _z;",
      "}"
    ].join("\n")
  ],
  [
    "an initializer inside a call argument that ends past its brace",
    [
      "class A",
      "{",
      "    private const char C = 'x';",
      "    private static readonly Foo X = Build(new Foo",
      "    {",
      "        A = 1,",
      "    });",
      "    private int _z;",
      "}"
    ].join("\n")
  ],
  [
    "a nested interpolated string inside an interpolation hole",
    [
      "class A",
      "{",
      "    private const char C = 'x';",
      "    private int _z;",
      '    private static string Describe(string platformName, string v) =>',
      "        TestContext.WriteLine(",
      '            $"Input: {(platformName == null ? "(null)" : $"\\u0022{platformName}\\u0022")}, "',
      '                + $"Serialized: \\u0022{v}\\u0022"',
      "        );",
      "}"
    ].join("\n")
  ]
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

/** Shapes the #672 member ordering MUST report. */
const ORDER_REPORTED = [
  [
    "a static property after a static field",
    "class A { private static int _f; private static int P { get; set; } }",
    "static property",
    "P"
  ],
  [
    "a property after a field",
    "class A { private int _f; private int P => 1; }",
    "property",
    "P"
  ],
  [
    "a field after a method",
    "class A { private void M() { } private int _f; }",
    "field",
    "_f"
  ],
  [
    "an internal field after a private field of the same tier",
    "class A { private int _f; internal int _g; }",
    "field",
    "_g"
  ],
  [
    "a static method after a method",
    "class A { private void M() { } private static void S() { } }",
    "static method",
    "S"
  ],
  [
    "a constructor after a method",
    "class A { private void M() { } private A() { } }",
    "constructor",
    "A"
  ],
  [
    "a const after a static property",
    "class A { private static int P { get; set; } private const int C = 1; }",
    "const",
    "C"
  ],
  [
    "a public method after a private method of the same tier",
    "class A { private void M() { } public void N() { } }",
    "method",
    "N"
  ]
];

for (const [name, source, expectedKind, expectedName] of ORDER_REPORTED) {
  runTest(`ordering reported: ${name}`, () => {
    const violations = violationsIn(source);
    assert.strictEqual(violations.length, 1, `expected exactly one report for: ${source}`);
    assert.strictEqual(violations[0].kind, expectedKind);
    assert.strictEqual(violations[0].name, expectedName);
  });
}

runTest("ordering: the canonical tier order passes and the same body scrambled fails", () => {
  const ordered = [
    "private const int C = 1;",
    "private static int P { get; set; }",
    "private static int _sf;",
    "private int Prop => 2;",
    "private int _f;",
    "private A() { }",
    "private static void S() { }",
    "private void M() { }"
  ];
  const ok = `class A { ${ordered.join(" ")} }`;
  assert.deepStrictEqual(violationsIn(ok), [], `expected the ordered body to pass: ${ok}`);
  const reversed = `class A { ${ordered.reverse().join(" ")} }`;
  assert.strictEqual(
    violationsIn(reversed).length,
    ordered.length - 1,
    `every adjacent descent in the reversed body is reported: ${reversed}`
  );
});

runTest("ordering: members on opposite sides of a conditional are never compared", () => {
  const source = [
    "class A",
    "{",
    "#if UNITY_EDITOR",
    "    private void EditorMethod() { }",
    "#else",
    "    private int _field;",
    "#endif",
    "}"
  ].join("\n");
  assert.deepStrictEqual(
    violationsIn(source).filter((violation) => ORDER_TIERS.includes(violation.kind)),
    [],
    "an #if branch and its #else branch are independent orderings"
  );
});

runTest("ordering: a conditional inside a method does not reset the comparison", () => {
  const source = [
    "class A",
    "{",
    "    private void M()",
    "    {",
    "#if UNITY_EDITOR",
    "        Log();",
    "#endif",
    "    }",
    "",
    "    private int _lateField;",
    "}"
  ].join("\n");
  const violations = violationsIn(source);
  assert.strictEqual(violations.length, 1);
  assert.strictEqual(violations[0].kind, "field");
  assert.strictEqual(violations[0].name, "_lateField");
});

runTest("ordering: --fix reorders a scrambled body and changes nothing else", () => {
  const source = [
    "class A",
    "{",
    "    private void Method() { }",
    "",
    "    // keeps its comment",
    "    private int _field = 1;",
    "",
    "    private const int Constant = 2;",
    "}"
  ].join("\n");
  const result = fixedText(source);
  const expected = [
    "class A",
    "{",
    "    private const int Constant = 2;",
    "",
    "    // keeps its comment",
    "    private int _field = 1;",
    "",
    "    private void Method() { }",
    "}"
  ].join("\n");
  assert.strictEqual(result, expected, `the fix is an exact permutation:\n${result}`);
  assert.strictEqual(result.length, source.length, "the rewrite must be a permutation of slices");
});

runTest("ordering: --fix orders access within a tier and keeps nested types last", () => {
  const source = "class A { private int _p; public int P; private void M() { } class B { } }";
  const result = fixedText(source);
  assert.strictEqual(
    result,
    "class A { public int P; private int _p; private void M() { } class B { } }"
  );
  assert.strictEqual(result.length, source.length);
});

runTest("ordering: --fix slots an event between const and static properties", () => {
  const source =
    "class A { private void M() { } private event System.Action E; private const int C = 1; }";
  const violations = violationsIn(source);
  assert.ok(0 < violations.length, "the descent is reported");
  const result = fixedText(source);
  assert.strictEqual(
    result,
    "class A { private const int C = 1; private event System.Action E; private void M() { } }",
    `events take the tier after const:\n${result}`
  );
  assert.strictEqual(result.length, source.length);
});

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
