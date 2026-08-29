#!/usr/bin/env node
"use strict";

/**
 * @file test-asmdef-discovery.js
 *
 * Contract test for scripts/unity/lib/asmdef-discovery.js.
 *
 * The property under test is the one #570 is about: a test assembly is run in EditMode by Unity
 * ONLY when it is flagged `EditorAssembly`, which means an asmdef whose only platform is Editor.
 * An asmdef with no `includePlatforms` compiles for every platform, is not flagged, and is
 * silently dropped from an EditMode run that names it -- so a discovery list that includes one
 * reads as coverage that does not exist.
 *
 * Two halves, both asserted:
 *   1. Synthetic fixtures, where the classifier is driven in both directions.
 *   2. This repository, where the seven platform-neutral test assemblies are named so the next
 *      reader does not have to re-measure them, and so an eighth cannot appear unnoticed.
 */

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..", "..");
const discovery = require(path.join(repoRoot, "scripts", "unity", "lib", "asmdef-discovery.js"));

/**
 * Measured on 6000.4.6f1 through the MCP bridge:
 * `CompilationPipeline.GetAssemblies(AssembliesType.Editor)` flags 26 of the 33 test assemblies
 * the editor compiles, and the seven it does not are exactly these. Confirmed independently in
 * CI: the 2021.3.45f1 editmode leg's log mentions `Tests.Runtime.Random` three times -- the
 * assembly list being echoed -- against 2,375 times in the playmode leg.
 *
 * A fixture for an edit-mode branch of runtime code belongs in an editor-only assembly. Adding a
 * name here is a decision to give up EditMode coverage for everything in it.
 */
const PLAYMODE_ONLY_TEST_ASSEMBLIES = [
  "WallstopStudios.UnityHelpers.Tests.Core",
  "WallstopStudios.UnityHelpers.Tests.Runtime",
  "WallstopStudios.UnityHelpers.Tests.Runtime.Performance",
  "WallstopStudios.UnityHelpers.Tests.Runtime.Random",
  "WallstopStudios.UnityHelpers.Tests.Runtime.Reflex",
  "WallstopStudios.UnityHelpers.Tests.Runtime.VContainer",
  "WallstopStudios.UnityHelpers.Tests.Runtime.Zenject"
];

let failures = 0;

/**
 * @param {string} name
 * @param {() => void} body
 */
function test(name, body) {
  try {
    body();
    process.stdout.write(`  [PASS] ${name}\n`);
  } catch (error) {
    failures++;
    process.stdout.write(`  [FAIL] ${name}\n         ${error.message}\n`);
  }
}

/**
 * Build a throwaway repository containing only asmdefs, so the classifier is driven by the
 * fixture rather than by whatever this repository happens to hold today.
 *
 * @param {Array<{ name: string, includePlatforms?: string[], excludePlatforms?: string[] }>} asmdefs
 * @returns {string} Absolute fixture root
 */
function writeFixture(asmdefs) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "asmdef-discovery-"));
  const testsDirectory = path.join(root, "Tests");
  fs.mkdirSync(testsDirectory, { recursive: true });
  for (const asmdef of asmdefs) {
    fs.writeFileSync(
      path.join(testsDirectory, `${asmdef.name}.asmdef`),
      JSON.stringify({
        name: asmdef.name,
        includePlatforms: asmdef.includePlatforms || [],
        excludePlatforms: asmdef.excludePlatforms || [],
        // Defaults to a real test assembly, because that is what every case except the
        // hosts-no-tests ones is about. Pass references/precompiledReferences to override.
        references:
          asmdef.references === undefined ? ["UnityEngine.TestRunner"] : asmdef.references,
        precompiledReferences: asmdef.precompiledReferences || []
      }),
      "utf8"
    );
  }
  return root;
}

const fixtureRoots = [];

/**
 * @param {Array<{ name: string, includePlatforms?: string[], excludePlatforms?: string[], references?: string[], precompiledReferences?: string[] }>} asmdefs
 * @returns {string}
 */
function fixture(asmdefs) {
  const root = writeFixture(asmdefs);
  fixtureRoots.push(root);
  return root;
}

process.stdout.write("Testing asmdef-discovery.js...\n\n  Section: EditMode means editor-only\n");

const EDITOR_ONLY = "WallstopStudios.UnityHelpers.Tests.Editor.Probe";
const PLATFORM_NEUTRAL = "WallstopStudios.UnityHelpers.Tests.Runtime.Probe";

test("EditorOnlyAsmdefIsDiscoveredForEditmode", () => {
  const root = fixture([{ name: EDITOR_ONLY, includePlatforms: ["Editor"] }]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "editmode" }), [
    EDITOR_ONLY
  ]);
});

test("AnAsmdefReferencingNoTestFrameworkIsNotDiscovered", () => {
  // Unity refuses AddComponent for a MonoBehaviour in an editor-only assembly and returns null
  // without logging, so a capture or scene fixture has to park its targets in an all-platform
  // assembly under Tests/. That assembly holds no tests, and naming it Tests.* must not make the
  // runner try to run it.
  const root = fixture([{ name: PLATFORM_NEUTRAL, references: [] }]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "playmode" }), []);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "standalone" }), []);
});

test("AnAsmdefIsDiscoveredWhenNunitIsItsOnlyTestReference", () => {
  const root = fixture([
    {
      name: PLATFORM_NEUTRAL,
      references: [],
      precompiledReferences: ["nunit.framework.dll"]
    }
  ]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "playmode" }), [
    PLATFORM_NEUTRAL
  ]);
});

test("PlatformNeutralAsmdefIsNotDiscoveredForEditmode", () => {
  const root = fixture([{ name: PLATFORM_NEUTRAL }]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "editmode" }), []);
});

test("PlatformNeutralAsmdefIsDiscoveredForPlaymode", () => {
  const root = fixture([{ name: PLATFORM_NEUTRAL }]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "playmode" }), [
    PLATFORM_NEUTRAL
  ]);
});

test("EditorOnlyAsmdefIsNotDiscoveredForPlaymode", () => {
  const root = fixture([{ name: EDITOR_ONLY, includePlatforms: ["Editor"] }]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "playmode" }), []);
});

test("EditorPlusStandaloneAsmdefIsNotDiscoveredForEditmode", () => {
  // Unity flags EditorAssembly for an editor-ONLY assembly. Naming Editor alongside a player
  // platform produces an assembly the EditMode runner will not take either.
  const root = fixture([
    { name: PLATFORM_NEUTRAL, includePlatforms: ["Editor", "LinuxStandalone64"] }
  ]);
  assert.deepStrictEqual(discovery.defaultIncludeAssemblies(root, { target: "editmode" }), []);
});

process.stdout.write("\n  Section: this repository\n");

test("EveryEditmodeAssemblyIsEditorOnly", () => {
  const editmode = discovery.defaultIncludeAssemblies(repoRoot, {
    target: "editmode",
    includePerf: true,
    includeIntegrations: true
  });
  const notEditorOnly = editmode.filter((name) => PLAYMODE_ONLY_TEST_ASSEMBLIES.includes(name));
  assert.deepStrictEqual(
    notEditorOnly,
    [],
    `EditMode discovery returned assemblies Unity will not run: ${notEditorOnly.join(", ")}`
  );
});

test("PlaymodeOnlyAssembliesAreExactlyTheDeclaredSeven", () => {
  const playmode = discovery.defaultIncludeAssemblies(repoRoot, {
    target: "playmode",
    includePerf: true,
    includeIntegrations: true
  });
  assert.deepStrictEqual(
    playmode.slice().sort(),
    PLAYMODE_ONLY_TEST_ASSEMBLIES.slice().sort(),
    "The set of test assemblies Unity runs in PlayMode only has changed. A fixture for an " +
      "edit-mode branch of runtime code cannot run in any of them; update " +
      "PLAYMODE_ONLY_TEST_ASSEMBLIES and .llm/skills/unity-devcontainer-testing.md deliberately."
  );
});

test("BenchmarkAssembliesRunInPlaymode", () => {
  // unity-benchmarks.yml names both, and its matrix is playmode-only because of it (#570).
  const playmode = discovery.defaultIncludeAssemblies(repoRoot, {
    target: "playmode",
    includePerf: true
  });
  for (const required of [
    "WallstopStudios.UnityHelpers.Tests.Runtime.Performance",
    "WallstopStudios.UnityHelpers.Tests.Runtime.Random"
  ]) {
    assert.ok(playmode.includes(required), `${required} must be discoverable for playmode`);
  }
});

for (const root of fixtureRoots) {
  fs.rmSync(root, { recursive: true, force: true });
}

process.stdout.write(`\nTests failed: ${failures}\n`);
process.exit(failures === 0 ? 0 : 1);
