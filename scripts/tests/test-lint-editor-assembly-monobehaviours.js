#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Contract tests for scripts/lint-editor-assembly-monobehaviours.js.
//
// The red half is the point (#556): a gate over a corpus that is clean by construction reports
// exactly what a gate scanning nothing reports. So every rule here is driven both ways, and the
// end-to-end case runs the real command line over a fixture tree rather than calling into the
// module, because the wiring between the two is where a gate stops firing without saying so.
//
// The cases beyond the two halves are the ones an adversarial read found this gate could get
// wrong. A false negative is the whole #677 defect class returning; a false positive sends the next
// reader to move a double that was fine where it was.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-editor-assembly-monobehaviours.js");
const {
  analyze,
  baseTypeNames,
  buildTypeIndex,
  parseSource,
  scan,
  vacuityFailures,
  EDITOR_ASSEMBLY_BY_DESIGN
} = require(linterPath);

console.log("Testing scripts/lint-editor-assembly-monobehaviours.js...\n");

let passed = 0;
let failed = 0;
const failedTests = [];

function runTest(name, body) {
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

const FIXTURE_ROOT = "/fixture";

/**
 * The asmdef map `collectAsmdefs` produces, built by hand so a case can state exactly which
 * directory is Editor-only.
 *
 * @param {Record<string, {name: string, includePlatforms?: string[]}>} directories Owning directories.
 * @returns {Map<string, object>} The map `ownerOf` walks.
 */
function asmdefsFor(directories) {
  return new Map(
    Object.entries(directories).map(([directory, asmdef]) => [
      path.join(FIXTURE_ROOT, directory).split(path.sep).join("/"),
      { includePlatforms: [], ...asmdef }
    ])
  );
}

/** @param {Record<string, string>} files Repo-relative path to source text. */
function sourcesFor(files) {
  return Object.entries(files).map(([file, text]) => ({ path: file, text }));
}

function analyzeFixture(files, directories, allowlist) {
  return analyze(
    sourcesFor(files),
    asmdefsFor(directories),
    new Map(Object.entries(allowlist ?? {})),
    FIXTURE_ROOT
  );
}

const EDITOR_ONLY = { name: "Fixture.Tests.Editor", includePlatforms: ["Editor"] };
const RUNTIME_CAPABLE = { name: "Fixture.Tests.Core", includePlatforms: [] };

function double(namespace, name, baseType) {
  return `namespace ${namespace}\n{\n    using UnityEngine;\n\n    public sealed class ${name} : ${baseType} { }\n}\n`;
}

runTest("a MonoBehaviour in an Editor-only assembly is reported, naming the assembly", () => {
  const { failures, subjects } = analyzeFixture(
    { "Tests/Editor/TestTypes/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour") },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.strictEqual(subjects.length, 1, "the double must be counted as a subject");
  assert.strictEqual(
    failures.length,
    1,
    `expected exactly one finding, got ${failures.join(" | ")}`
  );
  assert.match(failures[0], /Tests\/Editor\/TestTypes\/Double\.cs:\d+: Double is a MonoBehaviour/);
  assert.match(failures[0], /Fixture\.Tests\.Editor, which is Editor-only/);
  assert.match(failures[0], /refuse AddComponent/);
});

runTest("the same MonoBehaviour in a runtime-capable assembly is not reported", () => {
  // The green half of the rule above, and the only thing that shows the gate is reading
  // includePlatforms rather than the word "Editor" in the path.
  const { failures, subjects } = analyzeFixture(
    { "Tests/Editor/Targets/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour") },
    { "Tests/Editor/Targets": RUNTIME_CAPABLE, "Tests/Editor": EDITOR_ONLY }
  );
  assert.deepStrictEqual(failures, [], "a double under a nested runtime-capable asmdef is fine");
  assert.strictEqual(subjects.length, 0);
});

runTest("a file with no owning asmdef at all is not reported", () => {
  const { failures } = analyzeFixture(
    { "Tests/Loose/Double.cs": double("Fixture.Loose", "Double", "MonoBehaviour") },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.deepStrictEqual(failures, []);
});

runTest("an allowlisted double with the site count it claims is not reported", () => {
  const { failures, allowed } = analyzeFixture(
    { "Tests/Editor/TestTypes/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour") },
    { "Tests/Editor": EDITOR_ONLY },
    {
      "Tests/Editor/TestTypes/Double.cs::Double": {
        addComponentSites: 0,
        reason: "reached only through typeof"
      }
    }
  );
  assert.deepStrictEqual(failures, []);
  assert.strictEqual(allowed.length, 1, "the entry must be recorded as used");
});

runTest("an allowlisted double that has grown an AddComponent site is reported", () => {
  // The half that makes the allowlist a work list rather than an excuse: the reason stays true on
  // the day the fact it was written about stops being true, and only the count notices.
  const { failures } = analyzeFixture(
    {
      "Tests/Editor/TestTypes/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour"),
      "Tests/Editor/DoubleTests.cs":
        "namespace Fixture.Editor\n{\n    using UnityEngine;\n\n    public sealed class DoubleTests\n    {\n        public void Add(GameObject go) { go.AddComponent<Double>(); }\n    }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY },
    {
      "Tests/Editor/TestTypes/Double.cs::Double": {
        addComponentSites: 0,
        reason: "reached only through typeof"
      }
    }
  );
  assert.strictEqual(failures.length, 1, `expected one finding, got ${failures.join(" | ")}`);
  assert.match(failures[0], /claim of 0 AddComponent site\(s\), and there are now 1/);
  assert.match(failures[0], /Tests\/Editor\/DoubleTests\.cs:\d+/);
});

runTest("Undo.AddComponent and AddComponent(typeof(T)) are both counted", () => {
  const { failures } = analyzeFixture(
    {
      "Tests/Editor/TestTypes/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour"),
      "Tests/Editor/DoubleTests.cs":
        "namespace Fixture.Editor\n{\n    using UnityEditor;\n    using UnityEngine;\n\n    public sealed class DoubleTests\n    {\n        public void Add(GameObject go)\n        {\n            Undo.AddComponent<Double>(go);\n            go.AddComponent(typeof(Double));\n        }\n    }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY },
    {
      "Tests/Editor/TestTypes/Double.cs::Double": {
        addComponentSites: 0,
        reason: "reached only through typeof"
      }
    }
  );
  assert.strictEqual(failures.length, 1);
  assert.match(failures[0], /there are now 2/);
});

runTest("an allowlist entry whose subject is gone is reported as stale", () => {
  const { failures } = analyzeFixture(
    { "Tests/Editor/TestTypes/Double.cs": double("Fixture.Editor", "Double", "MonoBehaviour") },
    { "Tests/Editor": EDITOR_ONLY },
    {
      "Tests/Editor/TestTypes/Double.cs::Double": { addComponentSites: 0, reason: "still here" },
      "Tests/Editor/TestTypes/Moved.cs::Moved": { addComponentSites: 0, reason: "moved away" }
    }
  );
  assert.strictEqual(failures.length, 1, `expected one finding, got ${failures.join(" | ")}`);
  assert.match(failures[0], /names Tests\/Editor\/TestTypes\/Moved\.cs::Moved/);
  assert.match(failures[0], /Remove the entry/);
});

runTest("an abstract MonoBehaviour is exempt and its concrete subclass is not", () => {
  const { failures, subjects } = analyzeFixture(
    {
      "Tests/Editor/TestTypes/BaseDouble.cs":
        "namespace Fixture.Editor\n{\n    using UnityEngine;\n\n    public abstract class BaseDouble : MonoBehaviour { }\n}\n",
      "Tests/Editor/TestTypes/Concrete.cs": double("Fixture.Editor", "Concrete", "BaseDouble")
    },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.strictEqual(subjects.length, 1, "only the concrete subclass is addable");
  assert.strictEqual(failures.length, 1);
  assert.match(failures[0], /Concrete is a MonoBehaviour/);
});

runTest("inheritance is followed across files and namespaces", () => {
  // The base lives in the runtime tree, as the package's own MonoBehaviour bases do. A resolver
  // that only matched the literal token MonoBehaviour would pass every other case here and miss
  // every double that derives from one of ours.
  const { failures } = analyzeFixture(
    {
      "Runtime/Tags/AttributesComponent.cs":
        "namespace Fixture.Runtime.Tags\n{\n    using UnityEngine;\n\n    public class AttributesComponent : MonoBehaviour { }\n}\n",
      "Tests/Editor/TestTypes/Double.cs":
        "namespace Fixture.Editor\n{\n    using Fixture.Runtime.Tags;\n\n    public sealed class Double : AttributesComponent { }\n}\n"
    },
    { Runtime: RUNTIME_CAPABLE, "Tests/Editor": EDITOR_ONLY }
  );
  assert.strictEqual(failures.length, 1, `expected one finding, got ${failures.join(" | ")}`);
  assert.match(failures[0], /Double is a MonoBehaviour/);
});

runTest("two types of the same name are told apart by namespace", () => {
  // Measured on the real tree: Tests.WButton.TestComponent is a ScriptableObject and
  // Tests.Integrations.VContainer.TestComponent is a MonoBehaviour. A resolver keyed by simple name
  // merged their base lists and reported the ScriptableObject one, with ten AddComponent sites
  // belonging to the other.
  const { failures, subjects } = analyzeFixture(
    {
      "Tests/Editor/WButton/TestTypes/TestComponent.cs": double(
        "Fixture.Editor.WButton",
        "TestComponent",
        "ScriptableObject"
      ),
      "Tests/Core/TestTypes/TestComponent.cs": double(
        "Fixture.Core",
        "TestComponent",
        "MonoBehaviour"
      ),
      "Tests/Core/TestComponentTests.cs":
        "namespace Fixture.Core\n{\n    using UnityEngine;\n\n    public sealed class TestComponentTests\n    {\n        public void Add(GameObject go) { go.AddComponent<TestComponent>(); }\n    }\n}\n"
    },
    { "Tests/Editor/WButton": EDITOR_ONLY, "Tests/Core": RUNTIME_CAPABLE }
  );
  assert.deepStrictEqual(
    failures,
    [],
    "the ScriptableObject in the Editor-only assembly is not a MonoBehaviour"
  );
  assert.strictEqual(subjects.length, 0);
});

runTest("a nested MonoBehaviour is reported, and said to have no MonoScript", () => {
  // It escapes Unity's refusal today, which is exactly why it is a landmine: #666 gave eleven such
  // types a correctly-named file and turned them all red at once.
  const { failures } = analyzeFixture(
    {
      "Tests/Editor/DoubleTests.cs":
        "namespace Fixture.Editor\n{\n    using UnityEngine;\n\n    public sealed class DoubleTests\n    {\n        internal sealed class Nested : MonoBehaviour { }\n    }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.strictEqual(failures.length, 1, `expected one finding, got ${failures.join(" | ")}`);
  assert.match(failures[0], /Nested is a MonoBehaviour/);
  assert.match(failures[0], /NO MonoScript -- it is nested/);
});

runTest("a MonoBehaviour sharing a file named for another type is reported the same way", () => {
  const { failures } = analyzeFixture(
    {
      "Tests/Editor/Companion.cs":
        "namespace Fixture.Editor\n{\n    using UnityEngine;\n\n    public sealed class Companion { }\n\n    public sealed class Passenger : MonoBehaviour { }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.strictEqual(failures.length, 1, `expected one finding, got ${failures.join(" | ")}`);
  assert.match(failures[0], /Passenger is a MonoBehaviour/);
  assert.match(failures[0], /sharing a file named for another type/);
});

runTest("a doc comment showing a MonoBehaviour is prose, not a declaration", () => {
  // The first draft reported Editor/AnimationEventEditor.cs, whose <example> block shows
  // `class EnemyEvents : MonoBehaviour`.
  const { failures, subjects } = analyzeFixture(
    {
      "Tests/Editor/DoubleTests.cs":
        "namespace Fixture.Editor\n{\n    /// <example>\n    /// public sealed class EnemyEvents : MonoBehaviour { }\n    /// </example>\n    public sealed class DoubleTests { }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.deepStrictEqual(failures, []);
  assert.strictEqual(subjects.length, 0);
});

runTest("a generic constraint is not read as a base type", () => {
  const { failures } = analyzeFixture(
    {
      "Tests/Editor/Holder.cs":
        "namespace Fixture.Editor\n{\n    public sealed class Holder<TValue>\n        where TValue : class, new()\n    {\n    }\n}\n"
    },
    { "Tests/Editor": EDITOR_ONLY }
  );
  assert.deepStrictEqual(failures, []);
});

runTest("baseTypeNames splits at top-level commas and keeps the qualifier", () => {
  assert.deepStrictEqual(baseTypeNames(" UnityEngine.MonoBehaviour, IThing<int, string> "), [
    "UnityEngine.MonoBehaviour",
    "IThing"
  ]);
  assert.deepStrictEqual(baseTypeNames(""), []);
});

runTest("parseSource records the namespace, usings and nesting a resolver needs", () => {
  const parsed = parseSource({
    path: "Tests/Editor/Outer.cs",
    text: "namespace Fixture.Editor\n{\n    using UnityEngine;\n\n    public class Outer\n    {\n        public sealed class Inner : MonoBehaviour { }\n    }\n}\n"
  });
  assert.strictEqual(parsed.namespace, "Fixture.Editor");
  assert.ok(parsed.usings.has("UnityEngine"), "using directives must be captured");
  const inner = parsed.declarations.find((declaration) => declaration.name === "Inner");
  assert.ok(inner, "the nested type must be parsed");
  assert.strictEqual(inner.qualified, "Fixture.Editor.Outer.Inner");
  assert.strictEqual(inner.isNested, true);
  assert.strictEqual(inner.hasMonoScript, false);
  const index = buildTypeIndex(parsed.declarations);
  assert.strictEqual(index.isMonoBehaviour(inner), true);
});

runTest("every empty subject set produces its own vacuity failure", () => {
  // The half #556 is about, and the half the sibling gates get wrong: a count that survives on the
  // strength of another subject set is not a control.
  const full = {
    asmdefs: 1,
    editorOnlyAssemblies: 1,
    sourcesByRoot: new Map([["Tests", 1]]),
    monoBehaviours: 1,
    addComponentSites: 1,
    editorAssemblySubjects: 1
  };
  assert.deepStrictEqual(vacuityFailures(full), [], "a populated scan reports no vacuity");

  const empties = {
    asmdefs: /found no \.asmdef/,
    editorOnlyAssemblies: /includePlatforms \["Editor"\]/,
    monoBehaviours: /no class in the package resolved to a MonoBehaviour/,
    addComponentSites: /no AddComponent call site was resolved/,
    editorAssemblySubjects: /no MonoBehaviour was found in any Editor-only assembly/
  };
  for (const [field, pattern] of Object.entries(empties)) {
    const messages = vacuityFailures({ ...full, [field]: 0 });
    assert.strictEqual(messages.length, 1, `${field} must report exactly one vacuity failure`);
    assert.match(messages[0], pattern);
  }

  const rootDropped = vacuityFailures({ ...full, sourcesByRoot: new Map([["Tests", 0]]) });
  assert.strictEqual(rootDropped.length, 1);
  assert.match(rootDropped[0], /Tests\/ contributed no \.cs file/);
});

/**
 * A miniature of the real layout on disk: a runtime tree, an Editor-only test assembly holding one
 * double, and a fixture that adds a runtime component so the AddComponent scan is not empty.
 *
 * @param {{offender: boolean}} options Whether to plant an editor-assembly double nobody allowed.
 * @returns {string} The fixture root.
 */
function writeTree(options) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "editor-assembly-monobehaviours-"));
  const write = (relative, text) => {
    const full = path.join(root, relative);
    fs.mkdirSync(path.dirname(full), { recursive: true });
    fs.writeFileSync(full, text, "utf8");
  };

  write(
    "Runtime/Fixture.asmdef",
    JSON.stringify({ name: "Fixture.Runtime", includePlatforms: [] })
  );
  write("Runtime/RuntimeThing.cs", double("Fixture.Runtime", "RuntimeThing", "MonoBehaviour"));
  write(
    "Tests/Editor/Fixture.Tests.Editor.asmdef",
    JSON.stringify({ name: "Fixture.Tests.Editor", includePlatforms: ["Editor"] })
  );
  write(
    "Tests/Editor/TestTypes/AllowedDouble.cs",
    double("Fixture.Editor", "AllowedDouble", "MonoBehaviour")
  );
  write(
    "Tests/Editor/RuntimeThingTests.cs",
    "namespace Fixture.Editor\n{\n    using Fixture.Runtime;\n    using UnityEngine;\n\n    public sealed class RuntimeThingTests\n    {\n        public void Add(GameObject go) { go.AddComponent<RuntimeThing>(); }\n    }\n}\n"
  );
  if (options.offender) {
    write(
      "Tests/Editor/TestTypes/Offender.cs",
      double("Fixture.Editor", "Offender", "MonoBehaviour")
    );
  }

  fs.writeFileSync(
    path.join(root, "allowlist.json"),
    JSON.stringify({
      "Tests/Editor/TestTypes/AllowedDouble.cs::AllowedDouble": {
        addComponentSites: 0,
        reason: "reached only through typeof"
      }
    }),
    "utf8"
  );
  return root;
}

function runCommandLine(root) {
  return spawnSync(process.execPath, [linterPath, "--verbose"], {
    cwd: repoRoot,
    encoding: "utf8",
    env: {
      ...process.env,
      EDITOR_ASSEMBLY_MONOBEHAVIOUR_SCAN_ROOT: root,
      EDITOR_ASSEMBLY_MONOBEHAVIOUR_ALLOWLIST: path.join(root, "allowlist.json")
    }
  });
}

runTest("the command line exits 0 on a tree whose only editor double is allowed", () => {
  const root = writeTree({ offender: false });
  try {
    const result = runCommandLine(root);
    assert.strictEqual(
      result.status,
      0,
      `expected a clean run, got ${result.status}: ${result.stdout}${result.stderr}`
    );
    assert.match(
      result.stdout,
      /1 MonoBehaviour\(s\) in Editor-only assemblies considered, 1 allowed by design/,
      "the clean run must still report the subject count it judged"
    );
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("the command line exits 1 the moment a double nobody allowed is planted", () => {
  const root = writeTree({ offender: true });
  try {
    const result = runCommandLine(root);
    assert.strictEqual(result.status, 1, "planting the defect must fail the gate");
    assert.match(
      result.stderr,
      /Tests\/Editor\/TestTypes\/Offender\.cs:\d+: Offender is a MonoBehaviour/
    );
    assert.match(result.stderr, /Fixture\.Tests\.Editor, which is Editor-only/);
    assert.match(result.stderr, /1 violation\(s\)/);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("the repository's own allowlist is well formed and names RegularMonoBehaviour", () => {
  // The named subject the issue asks for. A scope that narrows silently drops it, and the staleness
  // rule then turns the missing subject into a red run rather than a quiet pass.
  assert.ok(0 < EDITOR_ASSEMBLY_BY_DESIGN.size, "the allowlist must not be empty while it exists");
  for (const [key, entry] of EDITOR_ASSEMBLY_BY_DESIGN) {
    const [file, type] = key.split("::");
    assert.ok(type, `${key} must be keyed <path>::<type>`);
    assert.ok(
      fs.existsSync(path.join(repoRoot, file)),
      `${key} names a file that does not exist: ${file}`
    );
    assert.strictEqual(
      typeof entry.addComponentSites,
      "number",
      `${key} must pin its AddComponent site count`
    );
    assert.ok(
      40 < (entry.reason ?? "").length,
      `${key} must carry a reason a reader can act on, not a word`
    );
  }
  assert.ok(
    EDITOR_ASSEMBLY_BY_DESIGN.has(
      "Tests/Editor/TestTypes/RegularMonoBehaviour.cs::RegularMonoBehaviour"
    ),
    "RegularMonoBehaviour is the double whose refusal named the mechanism; it must stay listed"
  );
});

runTest("the repository is green, and says how many subjects it judged", () => {
  const { failures, summary, subjects } = scan(repoRoot, EDITOR_ASSEMBLY_BY_DESIGN);
  assert.deepStrictEqual(failures, [], `the tree must be clean:\n${failures.join("\n")}`);
  assert.ok(
    0 < subjects.length,
    "a clean run over zero subjects is what a narrowed scope prints, not a pass"
  );
  assert.match(summary, /MonoBehaviour\(s\) in Editor-only assemblies considered/);
});

if (require.main === module) {
  console.log(`\n${passed} passed, ${failed} failed`);
  if (0 < failed) {
    console.log(`Failed: ${failedTests.join(", ")}`);
    process.exit(1);
  }
  process.exit(0);
}
