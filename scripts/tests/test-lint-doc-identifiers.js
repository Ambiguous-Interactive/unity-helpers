#!/usr/bin/env node
/**
 * Self-test for scripts/lint-doc-identifiers.js.
 *
 * Run against the repository the linter prints a success line, which is evidence about `docs/` and
 * no evidence that it still reports (#556). Every case below drives it over a fixture tree through
 * `DOC_IDENTIFIER_ROOT`, so each rule has a red half that fails the suite if the rule stops firing.
 *
 * Green halves also matter here more than usual, because the whole design claim is "no false
 * positives": a `using` of a parent namespace, of a namespace declared only under `Generator~`, and
 * a non-package `using` all have to pass.
 */

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const LINTER = path.join(REPO_ROOT, "scripts", "lint-doc-identifiers.js");

let passed = 0;
let failed = 0;
const failedTests = [];

function test(name, body) {
  try {
    body();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (error) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${error.message}`);
    failed++;
    failedTests.push(name);
  }
}

/**
 * Builds a scratch repository and runs the linter over it.
 *
 * @param {{sources: Record<string, string>, docs: Record<string, string>}} tree What to write.
 * @returns {{status: number, output: string}} The linter's exit code and combined output.
 */
function run(tree) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "lint-doc-identifiers-"));
  try {
    for (const [relative, contents] of Object.entries(tree.sources || {})) {
      const full = path.join(root, relative);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, contents);
    }

    for (const [relative, contents] of Object.entries(tree.docs || {})) {
      const full = path.join(root, "docs", relative);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, contents);
    }

    const result = spawnSync(process.execPath, [LINTER], {
      encoding: "utf8",
      env: { ...process.env, DOC_IDENTIFIER_ROOT: root }
    });

    return {
      status: result.status,
      output: `${result.stdout || ""}${result.stderr || ""}`
    };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

const NAMESPACE_SOURCE = {
  "Runtime/Core/Attributes/Thing.cs":
    "namespace WallstopStudios.UnityHelpers.Core.Attributes\n{\n    public sealed class Thing { }\n}\n",
  "Runtime/WallstopStudios.UnityHelpers.asmdef": '{\n  "name": "WallstopStudios.UnityHelpers"\n}\n'
};

console.log("\nTesting scripts/lint-doc-identifiers.js...\n");

// -- Green half ---------------------------------------------------------------
test("a using that names a declared namespace passes", () => {
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: {
      "guide.md": "```csharp\nusing WallstopStudios.UnityHelpers.Core.Attributes;\n```\n"
    }
  });

  assert.strictEqual(status, 0, output);
  assert.ok(/1 package using directive/.test(output), output);
});

test("a using of a PARENT namespace passes", () => {
  // `using A.B;` is legal wherever `A.B.C` is declared, so the index has to carry the ancestors.
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: { "guide.md": "```csharp\nusing WallstopStudios.UnityHelpers.Core;\n```\n" }
  });

  assert.strictEqual(status, 0, output);
});

test("a namespace declared only under Generator~ passes", () => {
  const { status, output } = run({
    sources: {
      ...NAMESPACE_SOURCE,
      "Generator~/Gen/Emitter.cs":
        "namespace WallstopStudios.UnityHelpers.Proto.Generator\n{\n    internal sealed class Emitter { }\n}\n"
    },
    docs: {
      "guide.md": "```csharp\nusing WallstopStudios.UnityHelpers.Proto.Generator;\n```\n"
    }
  });

  assert.strictEqual(status, 0, output);
});

test("a using from another vendor is not this linter's business", () => {
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: { "guide.md": "```csharp\nusing UnityEngine.Rendering.Universal;\n```\n" }
  });

  assert.strictEqual(status, 0, output);
  assert.ok(/0 package using directive/.test(output), output);
});

test("an assembly reference that names a real asmdef passes", () => {
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: {
      "guide.md": '```xml\n<assembly fullname="WallstopStudios.UnityHelpers">\n```\n'
    }
  });

  assert.strictEqual(status, 0, output);
  assert.ok(/1 assembly reference/.test(output), output);
});

// -- Red halves ---------------------------------------------------------------
test("a using that names no declared namespace is reported", () => {
  // The real defect this was written for: Core.Attribute for Core.Attributes.
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: { "guide.md": "```csharp\nusing WallstopStudios.UnityHelpers.Core.Attribute;\n```\n" }
  });

  assert.strictEqual(status, 1, output);
  assert.ok(/Core\.Attribute;/.test(output), output);
  assert.ok(/guide\.md:2/.test(output), output);
});

test("an assembly reference that names no asmdef is reported", () => {
  // The other real defect: the runtime assembly is WallstopStudios.UnityHelpers, and a link.xml
  // naming .Runtime preserves nothing while reporting nothing.
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: {
      "guide.md": '```xml\n<assembly fullname="WallstopStudios.UnityHelpers.Runtime">\n```\n'
    }
  });

  assert.strictEqual(status, 1, output);
  assert.ok(/WallstopStudios\.UnityHelpers\.Runtime/.test(output), output);
});

test("a nested documentation file is scanned", () => {
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: {
      "features/deep/guide.md": "```csharp\nusing WallstopStudios.UnityHelpers.Nowhere;\n```\n"
    }
  });

  assert.strictEqual(status, 1, output);
  assert.ok(/features\/deep\/guide\.md/.test(output), output);
});

test("every violation is reported, not just the first", () => {
  const { status, output } = run({
    sources: NAMESPACE_SOURCE,
    docs: {
      "a.md": "```csharp\nusing WallstopStudios.UnityHelpers.Nowhere;\n```\n",
      "b.md": "```csharp\nusing WallstopStudios.UnityHelpers.AlsoNowhere;\n```\n"
    }
  });

  assert.strictEqual(status, 1, output);
  assert.ok(/2 documentation reference\(s\)/.test(output), output);
});

test("the repository itself passes", () => {
  const result = spawnSync(process.execPath, [LINTER], { encoding: "utf8" });
  assert.strictEqual(result.status, 0, `${result.stdout || ""}${result.stderr || ""}`);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.log(`Failed: ${failedTests.join(", ")}`);
  process.exit(1);
}

process.exit(0);
