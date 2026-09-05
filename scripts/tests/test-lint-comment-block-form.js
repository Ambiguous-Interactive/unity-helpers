#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/lint-comment-block-form.ps1.
//
// The red half is the point (#556): a run of two or more `//` lines inside a type MUST be reported,
// and the shapes that merely look like one MUST NOT be. Each case is a whole fixture repository
// with its own Runtime/, Editor/, Tests/ and Generator~/ roots, because two of the properties under test -- the
// per-root subject counts and the refusal of a root that yielded no files -- only exist at that
// scale.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linter = path.join(repoRoot, "scripts", "lint-comment-block-form.ps1");

let passed = 0;
let failed = 0;
const failures = [];

function runTest(name, body) {
  try {
    body();
    console.log(`  [PASS] ${name}`);
    passed += 1;
  } catch (error) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${error.message}`);
    failed += 1;
    failures.push(name);
  }
}

/** Wraps a member body so the fixture has the indentation a real file has. */
function inType(body) {
  return [
    "namespace Fixture",
    "{",
    "    internal sealed class Subject",
    "    {",
    "        internal int Value()",
    "        {",
    body,
    "            return 0;",
    "        }",
    "    }",
    "}",
    ""
  ].join("\n");
}

/** Runs the linter over a throwaway repository holding `files`, and returns its result. */
function lint(files, extraArguments = []) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "comment-block-form-"));
  try {
    for (const [relative, contents] of Object.entries(files)) {
      const full = path.join(root, relative);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, contents, "utf8");
    }

    const result = spawnSync(
      "pwsh",
      ["-NoProfile", "-File", linter, "-RepoRoot", root, ...extraArguments],
      { encoding: "utf8" }
    );
    return {
      status: result.status,
      output: `${result.stdout || ""}${result.stderr || ""}`,
      root
    };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

/** A fixture repository whose four roots each hold one clean file. */
function populated(extra = {}) {
  return Object.assign(
    {
      "Runtime/Clean.cs": inType("            int total = 1;"),
      "Editor/Clean.cs": inType("            int total = 2;"),
      "Tests/Clean.cs": inType("            int total = 3;"),
      "Generator~/Clean.cs": inType("            int total = 4;")
    },
    extra
  );
}

const REPORTED = [
  [
    "two consecutive // lines inside a member",
    "            // first line of one thought\n            // second line of the same thought"
  ],
  [
    "a run separated by a blank comment line",
    "            // first\n            //\n            // third"
  ],
  [
    "a run above a member rather than inside one",
    [
      "namespace Fixture",
      "{",
      "    internal sealed class Subject",
      "    {",
      "        // why this member exists",
      "        // and what it must never do",
      "        internal int Value()",
      "        {",
      "            return 0;",
      "        }",
      "    }",
      "}",
      ""
    ].join("\n")
  ]
];

const SILENT = [
  ["one // line", "            // a single line needs no block"],
  [
    "the same thought as one block comment",
    "            /*\n                first line of one thought\n                second line\n            */"
  ],
  [
    "trailing comments on consecutive code lines",
    "            int first = 1; // one\n            int second = 2; // two"
  ],
  [
    "a run of tool directives",
    "            // UNH-SUPPRESS UNH003: the fixture owns this object\n            // cspell:ignore fixturey"
  ],
  [
    "// inside a string literal",
    '            string first = "// not a comment";\n            string second = "// still not one";'
  ],
  [
    "a namespace-level run, which is outside a type",
    [
      "namespace Fixture",
      "{",
      "    // this run sits beside the type, not inside it",
      "    // so rule 8 does not reach it",
      "    internal sealed class Subject",
      "    {",
      "        internal int Value()",
      "        {",
      "            return 0;",
      "        }",
      "    }",
      "}",
      ""
    ].join("\n")
  ],
  [
    "the file-level license header",
    "// MIT License - Copyright (c) 2026 wallstop\n// Full license text: https://example.invalid/LICENSE\n" +
      inType("            int total = 1;")
  ]
];

console.log("scripts/tests/test-lint-comment-block-form.js");

for (const [name, body] of REPORTED) {
  runTest(`reports ${name}`, () => {
    const source = body.trimStart().startsWith("namespace") ? body : inType(body);
    const result = lint(populated({ "Runtime/Subject.cs": source }));
    assert.strictEqual(result.status, 1, `expected a violation, got:\n${result.output}`);
    assert.ok(
      result.output.includes("Runtime/Subject.cs"),
      `the report did not name the file:\n${result.output}`
    );
  });
}

for (const [name, body] of SILENT) {
  runTest(`stays silent for ${name}`, () => {
    const source =
      body.trimStart().startsWith("namespace") || body.startsWith("// MIT") ? body : inType(body);
    const result = lint(populated({ "Runtime/Subject.cs": source }));
    assert.strictEqual(result.status, 0, `expected a clean run, got:\n${result.output}`);
  });
}

runTest("reports every subject count, per root", () => {
  const result = lint(populated());
  assert.strictEqual(result.status, 0, result.output);
  for (const root of ["Runtime/", "Editor/", "Tests/", "Generator~/"]) {
    assert.ok(
      result.output.includes(`${root} 1`),
      `the summary did not report a subject count for ${root}:\n${result.output}`
    );
  }
});

runTest("refuses a root that yielded no files", () => {
  const files = populated();
  delete files["Editor/Clean.cs"];
  const result = lint(files);
  assert.strictEqual(result.status, 1, `a missing root passed:\n${result.output}`);
  assert.ok(
    result.output.includes("Editor/"),
    `the refusal did not name the empty root:\n${result.output}`
  );
});

for (const root of ["Runtime", "Editor", "Tests", "Generator~"]) {
  for (const explicit of [false, true]) {
    runTest(
      `reports ${root} violations with ${explicit ? "explicit paths" : "full discovery"}`,
      () => {
        const subject = `${root}/Subject.cs`;
        const result = lint(
          populated({
            [subject]: inType("            // retained reason\n            // needs block form")
          }),
          explicit ? ["-Paths", subject] : []
        );
        assert.strictEqual(result.status, 1, result.output);
        assert.ok(result.output.includes(subject), result.output);
      }
    );
  }
  runTest(`refuses missing ${root} subjects`, () => {
    const files = populated();
    delete files[`${root}/Clean.cs`];
    const result = lint(files);
    assert.strictEqual(result.status, 1, result.output);
    assert.ok(result.output.includes(`${root}/`), result.output);
  });
}

runTest("ignores XML documentation and raw-string comment lookalikes", () => {
  const source = inType(
    [
      '            string text = """',
      "            // this is raw string content",
      "            // not a real comment",
      '            """;'
    ].join("\n")
  ).replace(
    "        internal int Value()",
    "        /// <summary>Documentation remains XML.\n        /// Its second line is also documentation.</summary>\n        internal int Value()"
  );
  const result = lint(populated({ "Generator~/Subject.cs": source }));
  assert.strictEqual(result.status, 0, result.output);
});

runTest("git discovery includes untracked sources and honors ignores", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "comment-block-form-git-"));
  try {
    const files = populated({
      "Generator~/Untracked.cs": inType(
        "            // retained reason\n            // requires block form"
      ),
      "Generator~/Ignored.cs": inType(
        "            // ignored source\n            // is outside discovery"
      ),
      ".gitignore": "Generator~/Ignored.cs\n"
    });
    for (const [relative, contents] of Object.entries(files)) {
      const full = path.join(root, relative);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, contents, "utf8");
    }
    fs.mkdirSync(path.join(root, "scripts"));
    for (const name of ["lint-comment-block-form.ps1", "comment-stripping.ps1"]) {
      fs.copyFileSync(path.join(repoRoot, "scripts", name), path.join(root, "scripts", name));
    }
    for (const args of [
      ["init", "-q"],
      ["add", "Runtime/Clean.cs", "Editor/Clean.cs", "Tests/Clean.cs", "Generator~/Clean.cs"]
    ]) {
      const result = spawnSync("git", args, { cwd: root, encoding: "utf8" });
      assert.strictEqual(result.status, 0, result.stderr);
    }
    const result = spawnSync(
      "pwsh",
      ["-NoProfile", "-File", path.join(root, "scripts", "lint-comment-block-form.ps1")],
      { encoding: "utf8" }
    );
    const output = `${result.stdout || ""}${result.stderr || ""}`;
    assert.strictEqual(result.status, 1, output);
    assert.ok(output.includes("Generator~/Untracked.cs"), output);
    assert.ok(!output.includes("Generator~/Ignored.cs"), output);
    assert.ok(output.includes("1 multi-line"), output);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("-Fix rewrites a run into one block and leaves the file clean", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "comment-block-form-fix-"));
  try {
    const files = populated({
      "Runtime/Subject.cs": inType(
        "            // first line of one thought\n            // second line of the same thought"
      )
    });
    for (const [relative, contents] of Object.entries(files)) {
      const full = path.join(root, relative);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, contents, "utf8");
    }

    const fix = spawnSync("pwsh", ["-NoProfile", "-File", linter, "-RepoRoot", root, "-Fix"], {
      encoding: "utf8"
    });
    assert.strictEqual(fix.status, 0, `${fix.stdout}${fix.stderr}`);

    const rewritten = fs.readFileSync(path.join(root, "Runtime/Subject.cs"), "utf8");
    assert.ok(rewritten.includes("/*"), `the run was not converted:\n${rewritten}`);
    assert.ok(rewritten.includes("*/"), `the block was not closed:\n${rewritten}`);
    assert.ok(
      !rewritten.includes("// first line of one thought"),
      `the original run survived:\n${rewritten}`
    );

    const after = spawnSync("pwsh", ["-NoProfile", "-File", linter, "-RepoRoot", root], {
      encoding: "utf8"
    });
    assert.strictEqual(after.status, 0, `${after.stdout}${after.stderr}`);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

console.log(`\n${passed} passed, ${failed} failed`);
if (failed > 0) {
  console.log(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
