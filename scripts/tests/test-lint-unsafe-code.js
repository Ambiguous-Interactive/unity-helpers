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
//
// The stackalloc cases pin the second rule: a span sized from an argument dies with an
// StackOverflowException that no catch intercepts, so a length must be a constant or guarded against one in
// the same statement. Both shipped offenders are reproduced verbatim as red cases.

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

/*
    A length no constant bounds. Both entries are the shipped defects: PointPolygonCheck projected
    a caller-sized polygon and WButtonGUI hashed the Inspector's whole multi-selection.
*/
const UNBOUNDED_STACKALLOC = [
  [
    "a caller-sized span length",
    "class Sample { void Project(System.ReadOnlySpan<int> polygon) {\n" +
      "    System.Span<int> projected = stackalloc int[polygon.Length];\n} }\n"
  ],
  [
    "an array length taken from a parameter",
    "class Sample { void Hash(object[] targets) {\n" +
      "    System.Span<long> ids = stackalloc long[targets.Length];\n} }\n"
  ],
  [
    "a local whose guard is in an enclosing block rather than the statement",
    "class Sample { const int Max = 8; void Take(int count) {\n" +
      "    if (count <= Max) { System.Span<int> buffer = stackalloc int[count]; }\n} }\n"
  ]
];

for (const [label, text] of UNBOUNDED_STACKALLOC) {
  runTest(`${label} is reported`, () => {
    const found = sources(text);
    assert.strictEqual(found.length, 1, `expected one violation, got ${JSON.stringify(found)}`);
    assert.ok(/stackalloc of/.test(found[0]), found[0]);
  });
}

/** Lengths the compiler already bounds. Every one must stay silent. */
const BOUNDED_STACKALLOC = [
  [
    "an integer literal",
    "class Sample { void M() { System.Span<int> b = stackalloc int[16]; } }\n"
  ],
  [
    "a const declared in the same file",
    "class Sample { const int Size = 16;\n" +
      "    void M() { System.Span<int> b = stackalloc int[Size]; } }\n"
  ],
  [
    "a const reached through its declaring type",
    "class Sample { const int Size = 16;\n" +
      "    void M() { System.Span<int> b = stackalloc int[Other.Size]; } }\n"
  ],
  [
    "a sizeof",
    "class Sample { void M() { System.Span<byte> b = stackalloc byte[sizeof(int)]; } }\n"
  ],
  [
    "an initializer that states its own length",
    "class Sample { void M() { System.Span<int> b = stackalloc int[] { 1, 2, 3 }; } }\n"
  ],
  [
    "a length guarded against a const in the same statement",
    "class Sample { const int Max = 128;\n" +
      "    void M(int count) {\n" +
      "        System.Span<int> b = count <= Max ? stackalloc int[count] : default; } }\n"
  ],
  [
    "a guard written with the constant on the left",
    "class Sample { const int Max = 128;\n" +
      "    void M(int count) {\n" +
      "        System.Span<int> b = Max < count ? default : stackalloc int[count]; } }\n"
  ],
  [
    "a stackalloc inside a comment",
    "class Sample { /* System.Span<int> b = stackalloc int[n]; */ }\n"
  ]
];

for (const [label, text] of BOUNDED_STACKALLOC) {
  runTest(`${label} is not a violation`, () => {
    assert.deepStrictEqual(sources(text), []);
  });
}

runTest("the shipped trees carry stackalloc sites for the rule to judge", () => {
  const { findStackAllocations } = require(linterPath);
  assert.strictEqual(typeof findStackAllocations, "function");
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
  const { readFileSync } = require("fs");
  const corpus = listed.stdout
    .split("\n")
    .filter((entry) => entry.endsWith(".cs"))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
  const { failures: found, inspected } = findStackAllocations(corpus);
  assert.ok(0 < inspected, "the rule judged no stackalloc site, so a clean run means nothing");
  assert.deepStrictEqual(found, []);
});

// The third rule: memory-access `Unsafe` members compile without the unsafe flag, so the asmdef
// check above cannot see them. The baseline is a ratchet, so BOTH directions are red -- a new site
// in a file with no baseline entry, and a baselined file that has fewer than its frozen count.
const { findUnsafeAccess, findStaleUnsafeBaselines } = require(linterPath);

function unsafeAccess(text, filePath) {
  return findUnsafeAccess([{ path: filePath ?? "Runtime/Sample.cs", text }]).failures;
}

runTest("a memory-access Unsafe member in a file with no baseline entry is a violation", () => {
  const found = unsafeAccess(
    "class Sample { static int Read(ref byte b) => Unsafe.As<byte, int>(ref b); }\n"
  );
  assert.strictEqual(found.length, 1, `expected one violation, got ${JSON.stringify(found)}`);
  assert.ok(found[0].includes("Runtime/Sample.cs:1"), found[0]);
});

runTest("Unsafe.SizeOf is a size query, not memory access", () => {
  assert.deepStrictEqual(
    unsafeAccess("class Sample { static int Size() => Unsafe.SizeOf<int>(); }\n"),
    []
  );
});

runTest("the word appears in a comment without being a violation", () => {
  assert.deepStrictEqual(unsafeAccess("class Sample { /* Unsafe.As was here once. */ }\n"), []);
});

runTest("a baselined file that drops below its frozen count is a violation", () => {
  const found = findStaleUnsafeBaselines(new Map());
  assert.ok(0 < found.length, "an empty tally must report every baselined file as retired");
  assert.ok(
    found.every((message) => message.includes("Lower the baseline")),
    JSON.stringify(found)
  );
});

runTest("a baseline matching what the scan found is silent", () => {
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    {
      encoding: "utf8",
      maxBuffer: 64 * 1024 * 1024
    }
  );
  const { readFileSync } = require("fs");
  const corpus = listed.stdout
    .split("\n")
    .filter((entry) => entry.endsWith(".cs"))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
  const { counted } = findUnsafeAccess(corpus);
  assert.ok(0 < counted.size, "the tally is empty, so a clean staleness run means nothing");
  assert.deepStrictEqual(findStaleUnsafeBaselines(counted), []);
});

runTest("the shipped trees carry memory-access Unsafe members for the rule to judge", () => {
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
  const { readFileSync } = require("fs");
  const corpus = listed.stdout
    .split("\n")
    .filter((entry) => entry.endsWith(".cs"))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
  const { failures: found, inspected } = findUnsafeAccess(corpus);
  assert.ok(0 < inspected, "the rule judged no site, so a clean run means nothing");
  assert.deepStrictEqual(found, []);
});

// The fourth rule: [Il2CppSetOption(Option.NullChecks, false)] deletes the runtime checks that
// make a wrong index or a null field a catchable exception instead of undefined behaviour, and it
// needs neither the `unsafe` keyword nor allowUnsafeCode -- so no rule above can see it. Zero such
// sites exist, which is why the RED cases carry the whole burden of proving the matcher is alive:
// a repository scan of an empty subject set looks identical whether the pattern works or not.
const { findIl2CppSafetyDisables } = require(linterPath);

function il2cpp(text) {
  return findIl2CppSafetyDisables([{ path: "Runtime/Sample.cs", text }]).failures;
}

/** Every spelling that leaves a check switched off. Each must be reported exactly once. */
const IL2CPP_DISABLED = [
  ["the attribute on a type", "[Il2CppSetOption(Option.NullChecks, false)]\nclass Sample { }\n"],
  [
    "the attribute on a method",
    "class Sample { [Il2CppSetOption(Option.ArrayBoundsChecks, false)] void M() { } }\n"
  ],
  [
    "the attribute on a property",
    "class Sample { [Il2CppSetOption(Option.DivideByZeroChecks, false)] int P => 1; }\n"
  ],
  [
    "named arguments in the reverse order",
    "[Il2CppSetOption(value: false, option: Option.NullChecks)]\nclass Sample { }\n"
  ],
  [
    "the fully qualified attribute",
    "[Unity.IL2CPP.CompilerServices.Il2CppSetOption(Option.NullChecks, false)]\nclass Sample { }\n"
  ],
  [
    "the constructor spelled with its Attribute suffix",
    "class Sample { void M() { object a = new Il2CppSetOptionAttribute(Option.NullChecks, false); } }\n"
  ],
  [
    "an array of options",
    "[Il2CppSetOption(new[] { Option.NullChecks, Option.ArrayBoundsChecks }, false)]\nclass Sample { }\n"
  ],
  [
    "an option name reached through using static",
    "[Il2CppSetOption(NullChecks, false)]\nclass Sample { }\n"
  ],
  [
    "an option name in a different case",
    "[Il2CppSetOption(Option.nullchecks, false)]\nclass Sample { }\n"
  ],
  [
    "an option this gate does not know by name",
    "[Il2CppSetOption(Option.Whatever, false)]\nclass Sample { }\n"
  ],
  [
    "a value routed through a named constant",
    "class Sample { const bool Fast = false;\n" +
      "    [Il2CppSetOption(Option.NullChecks, Fast)] void M() { } }\n"
  ],
  [
    "a helper that sets the option instead of the attribute",
    "class Sample { void M() { Configure(Option.NullChecks, false); } }\n"
  ]
];

for (const [label, text] of IL2CPP_DISABLED) {
  runTest(`${label} is reported`, () => {
    const found = il2cpp(text);
    assert.strictEqual(found.length, 1, `expected one violation, got ${JSON.stringify(found)}`);
    assert.ok(found[0].startsWith("Runtime/Sample.cs:"), `expected file and line, got ${found[0]}`);
    assert.ok(/switches off/.test(found[0]), found[0]);
    assert.ok(found[0].includes("#637"), "the report must name the issue that explains the rule");
  });
}

/*
    Shapes that name the options and leave every check ON. The attribute with `true` -- or with no
    value at all -- is harmless and was historically written to record that a hot path had been
    considered, so only the disabling forms are red. The prose cases are the ones that matter most:
    issue #637's own text names all three options, and a gate that flagged its own rationale would
    be uninstallable.
*/
const IL2CPP_ENABLED = [
  ["the attribute with true", "[Il2CppSetOption(Option.NullChecks, true)]\nclass Sample { }\n"],
  [
    "named arguments in the reverse order with true",
    "[Il2CppSetOption(value: true, option: Option.NullChecks)]\nclass Sample { }\n"
  ],
  ["the attribute with no value", "[Il2CppSetOption(Option.NullChecks)]\nclass Sample { }\n"],
  [
    "an array of options with true",
    "[Il2CppSetOption(new[] { Option.NullChecks, Option.ArrayBoundsChecks }, true)]\nclass Sample { }\n"
  ],
  [
    "a line comment quoting the refused form",
    "// [Il2CppSetOption(Option.NullChecks, false)] is refused by lint:unsafe-code.\nclass Sample { }\n"
  ],
  [
    "an XML doc comment naming the options",
    "/// <summary>Option.ArrayBoundsChecks must never be set to false.</summary>\nclass Sample { }\n"
  ],
  [
    "a block comment quoting the refused form",
    "/*\n    Il2CppSetOption(Option.DivideByZeroChecks, false)\n*/\nclass Sample { }\n"
  ],
  [
    "a string literal quoting the refused form",
    'class Sample { const string N = "Il2CppSetOption(Option.NullChecks, false)"; }\n'
  ],
  [
    "a different attribute whose argument merely reads alike",
    "[Diagnostics(Mode.NullChecksReport, false)]\nclass Sample { }\n"
  ],
  [
    "an option named outside every argument list",
    "class Sample { Option o = Option.NullChecks; bool disabled = false; }\n"
  ],
  ["a different IL2CPP attribute", "[Il2CppEagerStaticClassConstruction]\nclass Sample { }\n"]
];

for (const [label, text] of IL2CPP_ENABLED) {
  runTest(`${label} is not a violation`, () => {
    assert.deepStrictEqual(il2cpp(text), []);
  });
}

runTest("a trailing comment cannot make an enabling attribute read as disabling", () => {
  assert.deepStrictEqual(
    il2cpp("[Il2CppSetOption(Option.NullChecks, true)] // never false\nclass Sample { }\n"),
    []
  );
});

runTest("a trailing comment cannot hide a disabling attribute either", () => {
  const found = il2cpp(
    "[Il2CppSetOption(Option.NullChecks, false)] // Option.NullChecks, true\nclass Sample { }\n"
  );
  assert.strictEqual(found.length, 1, `expected one violation, got ${JSON.stringify(found)}`);
});

runTest("the vendored tree is excluded from the IL2CPP rule too", () => {
  const vendored = findIl2CppSafetyDisables([
    {
      path: "Runtime/Utils/SevenZip/Vendored.cs",
      text: "[Il2CppSetOption(Option.NullChecks, false)]\nclass Sample { }\n"
    }
  ]);
  assert.deepStrictEqual(vendored.failures, []);
  assert.strictEqual(vendored.inspected, 0, "a vendored file must not even be judged");
});

runTest("the IL2CPP rule rides the same entry point as the rest", () => {
  const found = findViolations(
    [],
    [
      {
        path: "Runtime/Sample.cs",
        text: "[Il2CppSetOption(Option.NullChecks, false)]\nclass Sample { }\n"
      }
    ],
    []
  );
  assert.strictEqual(found.length, 1, `expected one violation, got ${JSON.stringify(found)}`);
  assert.ok(/switches off/.test(found[0]), found[0]);
});

runTest("the package sets no IL2CPP check option at all, which is the point of the rule", () => {
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
  const { readFileSync } = require("fs");
  const corpus = listed.stdout
    .split("\n")
    .filter((entry) => entry.endsWith(".cs"))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
  const { failures: found, inspected } = findIl2CppSafetyDisables(corpus);
  assert.deepStrictEqual(found, []);
  assert.strictEqual(inspected, 0, "a site appeared; the rule needs a decision, not a baseline");

  // The subject set is empty by design, so the control cannot be a subject count (honest-gates).
  // It is the matcher itself: handed a real site, over the same corpus shape, it must red.
  assert.strictEqual(
    findIl2CppSafetyDisables([
      ...corpus,
      {
        path: "Runtime/Control.cs",
        text: "[Il2CppSetOption(Option.NullChecks, false)]\nclass Control { }\n"
      }
    ]).failures.length,
    1,
    "the scan cannot see a disabling attribute, so its clean run over the package means nothing"
  );
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
