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
const { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } = require("fs");
const { tmpdir } = require("os");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-unsafe-code.js");
const { codeOnly, createSarif, findViolations, isVendored, scanRepository } = require(linterPath);

let passed = 0;
let failed = 0;
let skipped = 0;
const failures = [];
const testArgs = process.argv.slice(2);
if (
  testArgs.length !== 0 &&
  (testArgs.length !== 2 || testArgs[0] !== "--filter" || testArgs[1].length === 0)
) {
  console.error("Usage: test-lint-unsafe-code.js [--filter text]");
  process.exit(2);
}
const filter = testArgs.length === 2 ? testArgs[1] : null;

function runTest(name, body) {
  if (filter !== null && !name.includes(filter)) {
    skipped++;
    return;
  }
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

for (const [label, args, status, expected] of [
  [
    "positive selection",
    ["--filter", "a compiler-evaluated integer expression is bounded: 2 * (3 + 1)"],
    0,
    /1 passed, 0 failed/
  ],
  ["empty selection", ["--filter", "no-fixture-has-this-name"], 1, /No fixture matched/],
  ["invalid selection", ["--filter"], 2, /Usage:/]
]) {
  runTest(`fixture selection remains explicit and nonempty: ${label}`, () => {
    const result = spawnSync(process.execPath, [__filename, ...args], { encoding: "utf8" });
    assert.strictEqual(result.status, status, result.stderr);
    assert.match(result.stdout + result.stderr, expected);
  });
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
  const scratch = mkdtempSync(path.join(tmpdir(), "unity-unsafe-report-"));
  let report;
  try {
    const output = path.join(scratch, "reports", "unsafe.sarif");
    const green = spawnSync(process.execPath, [linterPath, "--verbose", "--sarif", output], {
      cwd: repoRoot,
      encoding: "utf8"
    });
    assert.strictEqual(green.status, 0, `expected a clean repository, got: ${green.stderr}`);
    report = JSON.parse(readFileSync(output, "utf8")).runs[0];
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
  assert.deepStrictEqual(report.results, []);
  assert.strictEqual(report.invocations[0].executionSuccessful, true);
  assert.strictEqual(report.invocations[0].exitCode, 0);
  const { subjects } = report.invocations[0].properties;
  for (const root of ["Runtime", "Editor", "Tests"]) {
    assert.ok(0 < subjects.sourceFiles[root]);
    assert.ok(0 < subjects.assemblyDefinitions[root]);
  }
  assert.ok(0 < subjects.checkProjects);
  assert.ok(0 < subjects.stackAllocations);
  assert.strictEqual(subjects.unsafeControlReported, true);
  assert.strictEqual(subjects.stackControlReported, true);
  assert.strictEqual(subjects.il2cppControlReported, true);

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

runTest("SARIF findings normalize paths, encode URI characters and preserve source lines", () => {
  const failures = findViolations(
    [{ path: "Runtime\\Odd #名.asmdef", text: '{"allowUnsafeCode":true}' }],
    [{ path: "Editor\\Odd #名.cs", text: "// ignored\nunsafe class Sample {}" }],
    [{ path: "Generator~/Check.csproj", text: "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>" }]
  );
  const report = createSarif({ failures, subjects: {} });
  assert.strictEqual(report.version, "2.1.0");
  const run = report.runs[0];
  assert.strictEqual(run.results.length, 3);
  assert.strictEqual(run.invocations[0].executionSuccessful, true);
  assert.strictEqual(run.invocations[0].exitCode, 1);
  const byPath = new Map(
    run.results.map((result) => {
      assert.strictEqual(result.ruleId, run.tool.driver.rules[0].id);
      assert.strictEqual(result.level, "error");
      assert.match(result.partialFingerprints["unsafeCode/v1"], /^[a-f0-9]{64}$/);
      return [result.locations[0].physicalLocation.artifactLocation.uri, result];
    })
  );
  const code = byPath.get("Editor/Odd%20%23%E5%90%8D.cs").locations[0].physicalLocation;
  assert.deepStrictEqual(code.region, { startLine: 2 });
  assert.strictEqual(
    byPath.get("Runtime/Odd%20%23%E5%90%8D.asmdef").locations[0].physicalLocation.region,
    undefined
  );
  assert.ok(byPath.has("Generator~/Check.csproj"));
  assert.deepStrictEqual(report, createSarif({ failures: [...failures].reverse(), subjects: {} }));
});

runTest("SARIF carries location-free gate failures without inventing a source location", () => {
  const run = createSarif({ failures: ["Runtime: missing source subjects"], subjects: {} }).runs[0];
  assert.strictEqual(run.results[0].message.text, "Runtime: missing source subjects");
  assert.strictEqual(run.results[0].locations, undefined);
  assert.strictEqual(run.invocations[0].exitCode, 1);
});

runTest("a failed scan is recorded as failed execution even when no findings were produced", () => {
  const run = createSarif({ failures: [], subjects: {}, error: "unreadable source" }).runs[0];
  assert.deepStrictEqual(run.results, []);
  assert.strictEqual(run.invocations[0].executionSuccessful, false);
  assert.strictEqual(run.invocations[0].exitCode, 2);
  assert.strictEqual(
    run.invocations[0].toolExecutionNotifications[0].message.text,
    "unreadable source"
  );
});

for (const file of ["/outside/Sample.cs", "../Sample.cs", "C:\\outside\\Sample.cs"]) {
  runTest(`SARIF refuses a finding outside the repository: ${file}`, () => {
    assert.throws(
      () => createSarif({ failures: [`${file}:1: unsafe`], subjects: {} }),
      /repository-relative/
    );
  });
}

for (const args of [
  ["--sarif"],
  ["--sarif", "--verbose"],
  ["--root"],
  ["--root", "--verbose"],
  ["--root", ""],
  ["--root", repoRoot, "--root", repoRoot],
  ["--unknown"]
]) {
  runTest(`invalid report arguments fail before scanning: ${args.join(" ")}`, () => {
    const result = spawnSync(process.execPath, [linterPath, ...args], { encoding: "utf8" });
    assert.strictEqual(result.status, 2);
    assert.match(result.stderr, /Unexpected or incomplete argument/);
  });
}

runTest("a report write failure cannot report a clean gate", () => {
  const scratch = mkdtempSync(path.join(tmpdir(), "unity-unsafe-write-"));
  try {
    const parent = path.join(scratch, "file");
    writeFileSync(parent, "not a directory");
    const result = spawnSync(
      process.execPath,
      [linterPath, "--sarif", path.join(parent, "out.sarif")],
      {
        encoding: "utf8"
      }
    );
    assert.strictEqual(result.status, 2);
    assert.match(result.stderr, /Could not write SARIF/);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

runTest("a repository discovery failure is surfaced instead of becoming an empty scan", () => {
  const scratch = mkdtempSync(path.join(tmpdir(), "unity-unsafe-discovery-"));
  try {
    mkdirSync(path.join(scratch, "Runtime"));
    assert.throws(() => scanRepository(scratch), /git ls-files failed/);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

runTest("missing source roots fail even when discovery has no files to list", () => {
  const scratch = mkdtempSync(path.join(tmpdir(), "unity-unsafe-empty-"));
  try {
    const scan = scanRepository(scratch);
    for (const root of ["Runtime", "Editor", "Tests"]) {
      assert.strictEqual(scan.subjects.sourceFiles[root], 0);
      assert.strictEqual(scan.subjects.assemblyDefinitions[root], 0);
      assert.ok(scan.failures.some((failure) => failure.startsWith(`${root}: missing`)));
    }
    assert.strictEqual(createSarif(scan).runs[0].invocations[0].exitCode, 1);
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

runTest("CLI reports agree with unsafe, corrected, missing-root and unreadable sources", () => {
  const scratch = mkdtempSync(path.join(tmpdir(), "unity-unsafe-cli-"));
  try {
    const fixtureEnvironment = Object.fromEntries(
      Object.entries(process.env).filter(([name]) => !name.startsWith("GIT_"))
    );
    const inputs = {
      "Runtime/WallstopStudios.UnityHelpers.asmdef": '{"allowUnsafeCode":false}',
      "Editor/Sample.asmdef": '{"allowUnsafeCode":false}',
      "Tests/Sample.asmdef": '{"allowUnsafeCode":false}',
      "Generator~/Check.csproj": "<Project><AllowUnsafeBlocks>false</AllowUnsafeBlocks></Project>",
      "Runtime/Sample.cs":
        "unsafe class Sample { void M() { System.Span<int> b = stackalloc int[1]; } }",
      "Editor/Sample.cs": "class Sample {}",
      "Tests/Sample.cs": "class Sample {}"
    };
    for (const [file, text] of Object.entries(inputs)) {
      mkdirSync(path.dirname(path.join(scratch, file)), { recursive: true });
      writeFileSync(path.join(scratch, file), text);
    }
    const initialized = spawnSync("git", ["init", "--quiet", scratch], {
      encoding: "utf8",
      env: fixtureEnvironment
    });
    assert.strictEqual(initialized.status, 0, initialized.stderr);
    const staged = spawnSync(
      "bash",
      [
        "-c",
        'source "$UNSAFE_STAGING_HELPER"\ngit_add_with_retry "$@"',
        "unsafe-fixture",
        ...Object.keys(inputs)
      ],
      {
        cwd: scratch,
        encoding: "utf8",
        env: {
          ...fixtureEnvironment,
          UNSAFE_STAGING_HELPER: path
            .join(repoRoot, "scripts/git-staging-helpers.sh")
            .replace(/\\/g, "/")
        }
      }
    );
    assert.strictEqual(staged.status, 0, staged.stderr);
    const output = path.join(scratch, "unsafe.sarif");
    const run = (args = ["--root", scratch, "--sarif", output]) => {
      const result = spawnSync(process.execPath, [linterPath, ...args], {
        encoding: "utf8",
        env: fixtureEnvironment
      });
      return { result, report: JSON.parse(readFileSync(output, "utf8")).runs[0] };
    };
    const unsafe = run();
    assert.strictEqual(unsafe.result.status, 1, unsafe.result.stderr);
    assert.strictEqual(unsafe.report.results.length, 1);
    assert.strictEqual(unsafe.report.invocations[0].exitCode, 1);
    assert.strictEqual(
      unsafe.report.results[0].locations[0].physicalLocation.artifactLocation.uri,
      "Runtime/Sample.cs"
    );

    writeFileSync(
      path.join(scratch, "Runtime/Sample.cs"),
      inputs["Runtime/Sample.cs"].replace("unsafe ", "")
    );
    const corrected = run();
    assert.strictEqual(corrected.result.status, 0, corrected.result.stderr);
    assert.deepStrictEqual(corrected.report.results, []);
    assert.strictEqual(corrected.report.invocations[0].executionSuccessful, true);
    const reordered = run(["--sarif", "unsafe.sarif", "--root", scratch]);
    assert.strictEqual(reordered.result.status, 0, reordered.result.stderr);
    assert.deepStrictEqual(reordered.report, corrected.report);

    rmSync(path.join(scratch, "Tests"), { recursive: true });
    const missingRoot = run();
    assert.strictEqual(missingRoot.result.status, 1, missingRoot.result.stderr);
    assert.ok(
      missingRoot.report.results.some((result) => result.message.text.startsWith("Tests: missing"))
    );
    assert.strictEqual(missingRoot.report.invocations[0].properties.subjects.sourceFiles.Tests, 0);
    assert.ok(0 < missingRoot.report.invocations[0].properties.subjects.sourceFiles.Runtime);
    assert.ok(0 < missingRoot.report.invocations[0].properties.subjects.sourceFiles.Editor);
    mkdirSync(path.join(scratch, "Tests"));
    for (const file of ["Tests/Sample.cs", "Tests/Sample.asmdef"]) {
      writeFileSync(path.join(scratch, file), inputs[file]);
    }

    rmSync(path.join(scratch, "Editor/Sample.cs"));
    const unreadable = run();
    assert.strictEqual(unreadable.result.status, 2, unreadable.result.stderr);
    assert.strictEqual(unreadable.report.invocations[0].executionSuccessful, false);
    assert.strictEqual(unreadable.report.invocations[0].exitCode, 2);
    assert.match(
      unreadable.report.invocations[0].toolExecutionNotifications[0].message.text,
      /ENOENT/
    );
  } finally {
    rmSync(scratch, { recursive: true, force: true });
  }
});

/*
    A length no constant bounds. Both entries are the shipped defects: PointPolygonCheck projected
    a caller-sized polygon and WButtonGUI hashed the Inspector's whole multi-selection.
*/
const UNBOUNDED_STACKALLOC = [
  [
    "a parameter shadowing a constant allocation length",
    "class Sample { const int Max = 8; void M(int Max) { System.Span<int> b = stackalloc int[Max]; } }"
  ],
  [
    "a parameter shadowing a constant ternary bound",
    "class Sample { const int Max = 8; void M(int count, int Max) { System.Span<int> b = count <= Max ? stackalloc int[count] : default; } }"
  ],
  [
    "a local shadowing a constant allocation length",
    "class Sample { const int Max = 8; void M(int count) { int Max = count; System.Span<int> b = stackalloc int[Max]; } }"
  ],
  [
    "a readonly member whose name matches an unrelated constant",
    "class Other { public static readonly int Max = 8; } class Sample { const int Max = 8; void M() { System.Span<int> b = stackalloc int[Other.Max]; } }"
  ],
  [
    "a property whose name matches an unrelated constant",
    "class Other { public static int Max => 8; } class Sample { const int Max = 8; void M() { System.Span<int> b = stackalloc int[Other.Max]; } }"
  ],
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
    "class Other { public const int Size = 16; } class Sample {\n" +
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

for (const length of ["2 * (3 + 1)", "((int)8)", "sizeof(long) + sizeof(int)"]) {
  runTest(`a compiler-evaluated integer expression is bounded: ${length}`, () => {
    assert.deepStrictEqual(
      sources(`class Sample { void M() { System.Span<int> b = stackalloc int[${length}]; } }`),
      []
    );
  });
}

for (const [label, limits, expected] of [
  ["shared qualified constant", "public const int Max = 8;", 0],
  ["shared readonly member", "public static readonly int Max = 8;", 1],
  [
    "conditional const and readonly alternatives",
    "\n#if VARIABLE\npublic static readonly int Max = 8;\n#else\npublic const int Max = 8;\n#endif\n",
    1
  ],
  [
    "conditional constant values",
    "\n#if VARIABLE\npublic const int Max = 8;\n#else\npublic const int Max = 16;\n#endif\n",
    0
  ]
]) {
  runTest(`semantic binding handles ${label} across files`, () => {
    const result = require(linterPath).findStackAllocations([
      {
        path: "Runtime/Limits.cs",
        text: `namespace External { public static class Limits { ${limits} } }`
      },
      {
        path: "Runtime/Subject.cs",
        text: "using Bound = External.Limits; class Subject { void M(int count) { System.Span<int> b = count <= Bound.Max ? stackalloc int[count] : default; } }"
      }
    ]);
    assert.strictEqual(result.failures.length, expected);
    assert.strictEqual(result.inspected, 1);
  });
}

runTest("conditional inherited bindings include intermediate base-type declarations", () => {
  const result = require(linterPath).findStackAllocations([
    {
      path: "Runtime/Bases.cs",
      text: "class FixedBase { protected const int Max = 8; } class VariableBase { protected readonly int Max = 8; }"
    },
    {
      path: "Runtime/Intermediate.cs",
      text: "\n#if VARIABLE\nclass Intermediate : VariableBase {}\n#else\nclass Intermediate : FixedBase {}\n#endif\n"
    },
    {
      path: "Runtime/Subject.cs",
      text: "class Subject : Intermediate { void M(int count) { System.Span<int> b = count <= Max ? stackalloc int[count] : default; } }"
    }
  ]);
  assert.strictEqual(result.failures.length, 1);
  assert.strictEqual(result.inspected, 1);
});

for (const bound of ["Subject.Max", "Limit.Max"]) {
  runTest(`qualified inherited bounds track intermediate conditional types: ${bound}`, () => {
    const result = require(linterPath).findStackAllocations([
      {
        path: "Runtime/Bases.cs",
        text: "class FixedBase { protected const int Max = 8; } class VariableBase { protected static readonly int Max = 8; }"
      },
      {
        path: "Runtime/Intermediate.cs",
        text: "\n#if VARIABLE\nclass Intermediate : VariableBase {}\n#else\nclass Intermediate : FixedBase {}\n#endif\n"
      },
      {
        path: "Runtime/Subject.cs",
        text: `using Limit = Subject; class Subject : Intermediate { void M(int count) { System.Span<int> b = count <= ${bound} ? stackalloc int[count] : default; } }`
      }
    ]);
    assert.strictEqual(result.failures.length, 1);
    assert.strictEqual(result.inspected, 1);
  });
}

runTest("both branches of a conditional allocation are inspected", () => {
  const found = sources(
    "class Sample { const int Max = 8; void M(int count, int Max) {\n#if VARIABLE\nSystem.Span<int> b = stackalloc int[Max];\n#else\nSystem.Span<int> b = stackalloc int[8];\n#endif\n} }"
  );
  assert.strictEqual(found.length, 1);
});

for (const declaration of ["ref int count", "out int count"]) {
  runTest(`an externally mutable length cannot establish a stable bound: ${declaration}`, () => {
    assert.strictEqual(
      sources(
        `class Sample { const int Max = 8; void M(${declaration}) { count = 0; System.Span<int> b = count <= Max ? stackalloc int[count] : default; } }`
      ).length,
      1
    );
  });
}

for (const [condition, trueBranchBounded] of [
  ["count < Max", true],
  ["count <= Max", true],
  ["count > Max", false],
  ["count >= Max", false],
  ["Max < count", false],
  ["Max <= count", false],
  ["Max > count", true],
  ["Max >= count", true]
]) {
  for (const allocationInTrueBranch of [true, false]) {
    const expression = allocationInTrueBranch
      ? `${condition} ? stackalloc int[count] : default`
      : `${condition} ? default : stackalloc int[count]`;
    runTest(`stack bounds respect the selected branch: ${expression}`, () => {
      const found = sources(
        `class Sample { const int Max = 128; void M(int count) { ` +
          `System.Span<int> buffer = ${expression}; } }`
      );
      assert.strictEqual(found.length, allocationInTrueBranch === trueBranchBounded ? 0 : 1);
    });
  }
}

for (const condition of [
  "bypass || count <= Max",
  "count <= Max || bypass",
  "(count <= Max) == false",
  "!(count <= Max)",
  "count <= Max | bypass"
]) {
  runTest(`a comparison that does not bound the selected branch is refused: ${condition}`, () => {
    const found = sources(
      `class Sample { const int Max = 128; void M(int count, bool bypass) { ` +
        `System.Span<int> buffer = ${condition} ? stackalloc int[count] : default; } }`
    );
    assert.strictEqual(found.length, 1);
  });
}

for (const expression of [
  "count > Max ? Foo(seed: stackalloc int[count]) : default",
  "count >= Max ? Foo(first: 0, seed: stackalloc int[count]) : default",
  "Max < count ? Foo(seed: stackalloc int[count]) : default",
  "count <= Max ? Foo(seed: stackalloc int[count]) : default"
]) {
  runTest(`a nested argument cannot impersonate a direct bounded branch: ${expression}`, () => {
    assert.strictEqual(
      sources(
        `class Sample { const int Max = 128; void M(int count) { ` +
          `System.Span<int> buffer = ${expression}; } }`
      ).length,
      1
    );
  });
}

for (const fallback of [
  "Foo(seed: 0)",
  "Foo(first: 0, second: 1)",
  "global::System.Span<int>.Empty"
]) {
  runTest(`a false-branch upper bound permits an unused named fallback: ${fallback}`, () => {
    assert.deepStrictEqual(
      sources(
        `class Sample { const int Max = 128; void M(int count) { ` +
          `System.Span<int> buffer = count > Max ? ${fallback} : stackalloc int[count]; } }`
      ),
      []
    );
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

// Memory access is forbidden in every owned file, including the former exceptions.
const { findUnsafeAccess } = require(linterPath);

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

for (const imported of [
  "using Raw = System.Runtime.CompilerServices.Unsafe;",
  "using Raw = global::System.Runtime.CompilerServices.Unsafe;",
  "using Raw = Unsafe;",
  "using @Raw = @System.@Runtime.@CompilerServices.@Unsafe;",
  "using Raw = External::System.Runtime.CompilerServices.Unsafe;",
  "using static System.Runtime.CompilerServices.Unsafe;",
  "using static global::System.Runtime.CompilerServices.Unsafe;"
]) {
  runTest(`hidden Unsafe import is rejected: ${imported}`, () => {
    assert.equal(unsafeAccess(imported + " class Sample {}").length, 1);
    assert.deepEqual(unsafeAccess("// " + imported + "\nclass Sample {}"), []);
  });
}

runTest("Unsafe.SizeOf is a size query, not memory access", () => {
  assert.deepStrictEqual(
    unsafeAccess("class Sample { static int Size() => Unsafe.SizeOf<int>(); }\n"),
    []
  );
});

runTest("the word appears in a comment without being a violation", () => {
  assert.deepStrictEqual(unsafeAccess("class Sample { /* Unsafe.As was here once. */ }\n"), []);
});

for (const filePath of [
  "Runtime/Core/Extension/EnumExtensions.cs",
  "Runtime/Core/Serialization/WallstopProto/WProtoScalarFormatters.cs"
]) {
  runTest(`former exception ${filePath} rejects its first access`, () => {
    assert.strictEqual(unsafeAccess("Unsafe.As<int, long>(ref value);", filePath).length, 1);
  });
}

for (const member of [
  "As",
  "AsRef",
  "AsPointer",
  "Add",
  "Subtract",
  "AddByteOffset",
  "SubtractByteOffset",
  "Read",
  "ReadUnaligned",
  "Write",
  "WriteUnaligned",
  "Copy",
  "CopyBlock",
  "CopyBlockUnaligned",
  "InitBlock",
  "InitBlockUnaligned",
  "SkipInit"
]) {
  runTest(`Unsafe.${member} is rejected`, () => {
    assert.strictEqual(unsafeAccess(`Unsafe.${member}(ref value);`).length, 1);
  });
}

runTest("the shipped trees have no memory-access Unsafe members", () => {
  const listed = spawnSync(
    "git",
    ["-C", repoRoot, "ls-files", "--", "Runtime", "Editor", "Tests"],
    { encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
  assert.strictEqual(listed.status, 0, listed.stderr);
  const { readFileSync } = require("fs");
  const corpus = listed.stdout
    .split("\n")
    .filter((entry) => entry.endsWith(".cs"))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
  assert.ok(0 < corpus.length, "the source scan must have subjects");
  const { failures: found, inspected } = findUnsafeAccess(corpus);
  assert.strictEqual(inspected, 0);
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

console.log(`\n${passed} passed, ${failed} failed, ${skipped} not selected`);
if (passed + failed === 0) {
  console.error("No fixture matched the selected scope.");
  process.exit(1);
}
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
