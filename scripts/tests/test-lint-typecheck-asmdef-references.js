#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// Contract tests for scripts/lint-typecheck-asmdef-references.js.
//
// The red half is the point (#556). Both of the linter's rules have one, and both are driven the
// way they run for real: the static rule from the command line over a fixture tree, and the probe's
// attribution over compiler output recorded from the actual failing build (#598) rather than
// invented, so a change to the parser is measured against the text Roslyn really emits.
//
// The cases beyond those two are the ones an adversarial read found this gate could get wrong. A
// false negative here is the whole defect class returning; a false positive sends the next reader
// to add a precompiledReference no asmdef needs.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const linterPath = path.join(repoRoot, "scripts", "lint-typecheck-asmdef-references.js");
const {
  analyze,
  collectAsmdefs,
  discoverProjects,
  globToRegExp,
  governedAsmdefs,
  ownerOf,
  violationsFromBuildOutput,
  PROBE_PROPERTY
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

/**
 * A miniature of the real layout: one typecheck project compiling a runtime tree whose asmdef
 * auto-references everything, plus a test tree whose asmdef does not.
 */
function writeFixture(options) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "typecheck-asmdef-references-"));
  const project = path.join(root, "Generator~", "Fixture.Check");
  fs.mkdirSync(project, { recursive: true });
  fs.mkdirSync(path.join(root, "Runtime"), { recursive: true });
  fs.mkdirSync(path.join(root, "Runtime", "Binaries"), { recursive: true });
  fs.mkdirSync(path.join(root, "Tests", "Runtime"), { recursive: true });

  fs.writeFileSync(path.join(root, "Runtime", "Thing.cs"), "public class Thing { }", "utf8");
  fs.writeFileSync(
    path.join(root, "Runtime", "Binaries", "System.Text.Encodings.Web.dll"),
    "",
    "utf8"
  );
  fs.writeFileSync(
    path.join(root, "Runtime", "WallstopStudios.Fixture.asmdef"),
    JSON.stringify({ name: "WallstopStudios.Fixture", overrideReferences: false }),
    "utf8"
  );
  fs.writeFileSync(path.join(root, "Tests", "Runtime", "ThingTests.cs"), "// fixture", "utf8");
  fs.writeFileSync(
    path.join(root, "Tests", "Runtime", "WallstopStudios.Fixture.Tests.asmdef"),
    JSON.stringify({
      name: "WallstopStudios.Fixture.Tests",
      overrideReferences: true,
      precompiledReferences: options.precompiledReferences
    }),
    "utf8"
  );

  const condition = options.switchable ? ` Condition="'$(${PROBE_PROPERTY})' == 'true'"` : "";
  fs.writeFileSync(
    path.join(project, "Fixture.Check.csproj"),
    [
      '<Project Sdk="Microsoft.NET.Sdk">',
      "  <PropertyGroup>",
      "    <RepoRoot>$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)/../..'))</RepoRoot>",
      "  </PropertyGroup>",
      "  <ItemGroup>",
      `    <Reference Include="System.Text.Encodings.Web"${condition}>`,
      "      <HintPath>$(RepoRoot)/Runtime/Binaries/System.Text.Encodings.Web.dll</HintPath>",
      "    </Reference>",
      "  </ItemGroup>",
      "  <ItemGroup>",
      '    <Compile Include="$(RepoRoot)/Runtime/**/*.cs" />',
      '    <Compile Include="$(RepoRoot)/Tests/Runtime/**/*.cs" />',
      "  </ItemGroup>",
      "</Project>"
    ].join("\n"),
    "utf8"
  );
  return root;
}

function analyzeFixture(root, table) {
  const projects = discoverProjects(root);
  return analyze(projects, collectAsmdefs(root), table ?? new Map(), root);
}

runTest("a reference no governed test asmdef declares is reported, naming the asmdef", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const { failures: found } = analyzeFixture(root);
    assert.strictEqual(found.length, 1, `one failure expected, got ${JSON.stringify(found)}`);
    assert.ok(
      found[0].includes("WallstopStudios.Fixture.Tests"),
      `the asmdef must be named: ${found[0]}`
    );
    assert.ok(
      found[0].includes("System.Text.Encodings.Web"),
      `the assembly must be named: ${found[0]}`
    );
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("declaring the assembly in the test asmdef clears the report", () => {
  const root = writeFixture({
    precompiledReferences: ["nunit.framework.dll", "System.Text.Encodings.Web.dll"],
    switchable: true
  });
  try {
    assert.deepStrictEqual(analyzeFixture(root).failures, []);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("a recorded reason clears the report, and only for the project it names", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    assert.deepStrictEqual(
      analyzeFixture(
        root,
        new Map([["Fixture.Check::System.Text.Encodings.Web", "Runtime/** needs it"]])
      ).failures,
      []
    );
    const wrongProject = analyzeFixture(
      root,
      new Map([["Other.Check::System.Text.Encodings.Web", "Runtime/** needs it"]])
    ).failures;
    assert.strictEqual(wrongProject.length, 2, JSON.stringify(wrongProject));
    assert.ok(
      wrongProject.some((failure) => failure.includes("Other.Check")),
      `the stale entry must be reported: ${JSON.stringify(wrongProject)}`
    );
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("an excuse that is no longer necessary is itself a failure", () => {
  // The rule the missing-red-half list in test-run-repo-lint.js learned the hard way: an allowlist
  // whose entries may outlive their reason stops being a work list and becomes folklore.
  const root = writeFixture({
    precompiledReferences: ["System.Text.Encodings.Web.dll"],
    switchable: true
  });
  try {
    const found = analyzeFixture(
      root,
      new Map([["Fixture.Check::System.Text.Encodings.Web", "Runtime/** needs it"]])
    ).failures;
    assert.strictEqual(found.length, 1, JSON.stringify(found));
    assert.ok(found[0].includes("Remove the entry"), found[0]);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("a Runtime-only reference the probe cannot drop is reported", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: false });
  try {
    const found = analyzeFixture(
      root,
      new Map([["Fixture.Check::System.Text.Encodings.Web", "Runtime/** needs it"]])
    ).failures;
    assert.strictEqual(found.length, 1, JSON.stringify(found));
    assert.ok(found[0].includes(PROBE_PROPERTY), found[0]);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("an asmdef with overrideReferences false constrains nothing", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const asmdefPath = path.join(root, "Tests", "Runtime", "WallstopStudios.Fixture.Tests.asmdef");
    fs.writeFileSync(
      asmdefPath,
      JSON.stringify({ name: "WallstopStudios.Fixture.Tests", overrideReferences: false }),
      "utf8"
    );
    const { failures: found, checked } = analyzeFixture(root);
    assert.deepStrictEqual(found, []);
    assert.ok(
      checked.some((line) => line.includes("no overrideReferences asmdef in scope")),
      `the empty scope must be stated rather than passed silently: ${JSON.stringify(checked)}`
    );
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("a file the project excludes does not drag its asmdef into scope", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const csproj = path.join(root, "Generator~", "Fixture.Check", "Fixture.Check.csproj");
    fs.writeFileSync(
      csproj,
      fs
        .readFileSync(csproj, "utf8")
        .replace(
          '<Compile Include="$(RepoRoot)/Tests/Runtime/**/*.cs" />',
          '<Compile Include="$(RepoRoot)/Tests/Runtime/**/*.cs" Exclude="$(RepoRoot)/Tests/Runtime/**/*.cs" />'
        ),
      "utf8"
    );
    assert.deepStrictEqual(analyzeFixture(root).failures, []);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("a reference resolved outside the repository is not an asmdef's to declare", () => {
  // EditorCheck's UnityEditor reference points into the NuGet cache. No asmdef ever names it, and
  // reporting it would be a demand nobody can satisfy.
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const csproj = path.join(root, "Generator~", "Fixture.Check", "Fixture.Check.csproj");
    fs.writeFileSync(
      csproj,
      fs
        .readFileSync(csproj, "utf8")
        .replace(
          "$(RepoRoot)/Runtime/Binaries/System.Text.Encodings.Web.dll",
          "$(PkgUnity3D_SDK)/lib/UnityEditor.dll"
        ),
      "utf8"
    );
    assert.deepStrictEqual(analyzeFixture(root).failures, []);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("the linter exits non-zero from the command line on a drifting fixture tree", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const red = spawnSync(process.execPath, [linterPath, "--verbose"], {
      encoding: "utf8",
      env: { ...process.env, TYPECHECK_ASMDEF_REFERENCE_ROOT: root }
    });
    assert.strictEqual(red.status, 1, `a drifting tree must exit 1: ${red.stdout}${red.stderr}`);
    assert.ok(
      red.stderr.includes("System.Text.Encodings.Web"),
      `the assembly must be named: ${red.stderr}`
    );

    fs.writeFileSync(
      path.join(root, "Tests", "Runtime", "WallstopStudios.Fixture.Tests.asmdef"),
      JSON.stringify({
        name: "WallstopStudios.Fixture.Tests",
        overrideReferences: true,
        precompiledReferences: ["System.Text.Encodings.Web.dll"]
      }),
      "utf8"
    );
    const green = spawnSync(process.execPath, [linterPath, "--verbose"], {
      encoding: "utf8",
      env: { ...process.env, TYPECHECK_ASMDEF_REFERENCE_ROOT: root }
    });
    assert.strictEqual(green.status, 0, `a clean tree must exit 0: ${green.stdout}${green.stderr}`);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("a tree with no typecheck project is a broken scan, not a clean repository", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "typecheck-asmdef-references-empty-"));
  try {
    const result = spawnSync(process.execPath, [linterPath], {
      encoding: "utf8",
      env: { ...process.env, TYPECHECK_ASMDEF_REFERENCE_ROOT: root }
    });
    assert.strictEqual(result.status, 1);
    assert.ok(result.stderr.includes("broken scan"), result.stderr);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

// Recorded verbatim from `dotnet build ... -p:WallstopRuntimeOnlyReferences=false` on the tree that
// reproduced #598: one error in a fixture, one in the runtime tree the drop is expected to break.
const RECORDED_PROBE_OUTPUT = [
  "/repo/Tests/Runtime/Performance/JsonConverterTests.cs(7,63): error CS0012: The type 'JavaScriptEncoder' is defined in an assembly that is not referenced. You must add a reference to assembly 'System.Text.Encodings.Web, Version=9.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. [/repo/Generator~/WallstopStudios.UnityHelpers.TestCheck/WallstopStudios.UnityHelpers.TestCheck.csproj]",
  "/repo/Runtime/Core/Serialization/JsonConverters/Vector2Converter.cs(15,60): error CS0012: The type 'JavaScriptEncoder' is defined in an assembly that is not referenced. You must add a reference to assembly 'System.Text.Encodings.Web, Version=9.0.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51'. [/repo/Generator~/WallstopStudios.UnityHelpers.TestCheck/WallstopStudios.UnityHelpers.TestCheck.csproj]",
  "",
  "Build FAILED."
].join("\n");

runTest("the probe attributes an error in a governed fixture and ignores the runtime's own", () => {
  const violations = violationsFromBuildOutput(
    RECORDED_PROBE_OUTPUT,
    (file) =>
      file.startsWith("/repo/Tests/") ? "WallstopStudios.UnityHelpers.Tests.Runtime" : null,
    "TestCheck"
  );
  assert.strictEqual(violations.length, 1, JSON.stringify(violations));
  assert.ok(violations[0].includes("JsonConverterTests.cs(7)"), violations[0]);
  assert.ok(violations[0].includes("CS0012"), violations[0]);
});

runTest("the same error reported twice by two build passes is one violation", () => {
  // MSBuild echoes every error again in its summary block, so a naive parser doubles the count and
  // the reader starts discounting the number.
  const violations = violationsFromBuildOutput(
    `${RECORDED_PROBE_OUTPUT}\n${RECORDED_PROBE_OUTPUT}`,
    (file) => (file.startsWith("/repo/Tests/") ? "Tests" : null),
    "TestCheck"
  );
  assert.strictEqual(violations.length, 1, JSON.stringify(violations));
});

runTest("a probe build that reported no compiler error at all is not evidence of anything", () => {
  const violations = violationsFromBuildOutput("Build succeeded.", () => "Tests", "TestCheck");
  assert.deepStrictEqual(violations, []);
});

runTest("a warning is not an error", () => {
  const violations = violationsFromBuildOutput(
    "/repo/Tests/Runtime/A.cs(1,1): warning CS0219: The variable is assigned but never used [/repo/x.csproj]",
    () => "Tests",
    "TestCheck"
  );
  assert.deepStrictEqual(violations, []);
});

runTest("globToRegExp gives ** the zero-directory case MSBuild gives it", () => {
  const matcher = globToRegExp("/repo/Tests/**/*.cs");
  assert.ok(matcher.test("/repo/Tests/A.cs"), "zero directories must match");
  assert.ok(matcher.test("/repo/Tests/Runtime/Deep/A.cs"), "many directories must match");
  assert.ok(!matcher.test("/repo/Tests/A.txt"), "the extension still constrains");
  assert.ok(!globToRegExp("/repo/Tests/*.cs").test("/repo/Tests/Runtime/A.cs"), "* stops at /");
});

runTest("ownerOf attributes a file to its NEAREST-ancestor asmdef", () => {
  const root = writeFixture({ precompiledReferences: ["nunit.framework.dll"], switchable: true });
  try {
    const nested = path.join(root, "Tests", "Runtime", "Performance");
    fs.mkdirSync(nested, { recursive: true });
    fs.writeFileSync(
      path.join(nested, "WallstopStudios.Fixture.Tests.Performance.asmdef"),
      JSON.stringify({
        name: "WallstopStudios.Fixture.Tests.Performance",
        overrideReferences: true,
        precompiledReferences: []
      }),
      "utf8"
    );
    fs.writeFileSync(path.join(nested, "PerfTests.cs"), "// fixture", "utf8");
    const asmdefs = collectAsmdefs(root);
    assert.strictEqual(
      ownerOf(path.join(nested, "PerfTests.cs").replace(/\\/g, "/"), asmdefs).name,
      "WallstopStudios.Fixture.Tests.Performance"
    );
    const projects = discoverProjects(root);
    assert.deepStrictEqual([...governedAsmdefs(projects[0], asmdefs, root).keys()].sort(), [
      "WallstopStudios.Fixture.Tests",
      "WallstopStudios.Fixture.Tests.Performance"
    ]);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

runTest("this repository's own typecheck projects are parsed, not silently skipped", () => {
  // The fixtures above prove the rules. This proves the rules are pointed at something: a parser
  // that returned an empty reference list would pass every assertion above and check nothing here.
  const projects = discoverProjects(repoRoot);
  const names = projects.map((project) => project.name).sort();
  assert.deepStrictEqual(names, [
    // Compiles generated documentation samples rather than any asmdef's sources (#611), so the
    // static rule has nothing to govern here -- but it is discovered, and a project that stopped
    // being parsed would be invisible without this list.
    "WallstopStudios.UnityHelpers.DocSamplesCheck",
    "WallstopStudios.UnityHelpers.EditorCheck",
    // The EditMode gate (#616). It is the only project that compiles `Tests/Editor/**`, and it is
    // governed here for the reason the other three are: it folds 28 `overrideReferences: true`
    // asmdefs into one assembly with one reference list.
    "WallstopStudios.UnityHelpers.EditorTestCheck",
    "WallstopStudios.UnityHelpers.TestCheck",
    "WallstopStudios.UnityHelpers.TypeCheck"
  ]);
  for (const project of projects) {
    assert.ok(
      0 < project.references.length,
      `${project.name}: no bundled reference parsed, which would make this gate vacuous`
    );
  }
  // Exact name, not `endsWith`: `EditorTestCheck` also ends with "TestCheck" and sorts first, so a
  // suffix match here silently pointed the PlayMode assertions below at the EditMode project.
  const testCheck = projects.find(
    (project) => project.name === "WallstopStudios.UnityHelpers.TestCheck"
  );
  assert.ok(
    !testCheck.references.some((reference) => reference.name === "System.IO.Pipelines"),
    "System.IO.Pipelines was removed for #598 because no source in any tree binds it"
  );
  const asmdefs = collectAsmdefs(repoRoot);
  assert.deepStrictEqual(
    [...governedAsmdefs(testCheck, asmdefs, repoRoot).keys()].sort(),
    [
      "WallstopStudios.UnityHelpers.Tests.Core",
      "WallstopStudios.UnityHelpers.Tests.Runtime",
      "WallstopStudios.UnityHelpers.Tests.Runtime.Performance",
      "WallstopStudios.UnityHelpers.Tests.Runtime.Random"
    ],
    "the four PlayMode test asmdefs TestCheck compiles"
  );

  // The #616 half. TestCheck's list above stops at `Tests/Runtime`, which is exactly the hole
  // EditorTestCheck exists to close: if this list ever loses its `Tests.Editor.*` entries, the
  // EditMode tree has silently fallen out of scope again and the gate would still report green.
  const editorTestCheck = projects.find(
    (project) => project.name === "WallstopStudios.UnityHelpers.EditorTestCheck"
  );
  assert.ok(
    editorTestCheck,
    "the EditMode gate must be discovered, or Tests/Editor/** is ungoverned again"
  );
  const editorTestGoverned = [...governedAsmdefs(editorTestCheck, asmdefs, repoRoot).keys()].sort();
  assert.deepStrictEqual(
    editorTestGoverned,
    [
      "WallstopStudios.UnityHelpers.Tests.Capture.Targets",
      "WallstopStudios.UnityHelpers.Tests.Core",
      "WallstopStudios.UnityHelpers.Tests.Core.Editor",
      "WallstopStudios.UnityHelpers.Tests.Editor",
      "WallstopStudios.UnityHelpers.Tests.Editor.AssetProcessors",
      "WallstopStudios.UnityHelpers.Tests.Editor.Attributes",
      "WallstopStudios.UnityHelpers.Tests.Editor.Capture",
      "WallstopStudios.UnityHelpers.Tests.Editor.Core",
      "WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers",
      "WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors",
      "WallstopStudios.UnityHelpers.Tests.Editor.Extensions",
      "WallstopStudios.UnityHelpers.Tests.Editor.Helper",
      "WallstopStudios.UnityHelpers.Tests.Editor.Settings",
      "WallstopStudios.UnityHelpers.Tests.Editor.Sprites.Animation",
      "WallstopStudios.UnityHelpers.Tests.Editor.Sprites.Cropper",
      "WallstopStudios.UnityHelpers.Tests.Editor.Sprites.SpriteSheetExtractor",
      "WallstopStudios.UnityHelpers.Tests.Editor.Sprites.TextureSettings",
      "WallstopStudios.UnityHelpers.Tests.Editor.Sprites.TextureTools",
      "WallstopStudios.UnityHelpers.Tests.Editor.Tags",
      "WallstopStudios.UnityHelpers.Tests.Editor.Tools",
      "WallstopStudios.UnityHelpers.Tests.Editor.Utils",
      "WallstopStudios.UnityHelpers.Tests.Editor.Utils.Odin",
      "WallstopStudios.UnityHelpers.Tests.Editor.Validation",
      "WallstopStudios.UnityHelpers.Tests.Editor.WButton",
      "WallstopStudios.UnityHelpers.Tests.Editor.WGroup",
      "WallstopStudios.UnityHelpers.Tests.Editor.Windows",
      "WallstopStudios.UnityHelpers.Tests.Runtime"
    ],
    "every EditMode test asmdef, plus the shared Tests/Core pair and the Tests/Runtime doubles"
  );
  // `Tests.Editor.Sprites.PivotAdjuster` is absent on purpose: BOTH of its two fixtures set
  // `Texture2D.alphaIsTransparency`, an Editor-only UnityEngine member the player reference
  // assemblies do not carry, so the project excludes the whole asmdef and it governs nothing.
  // The three DI-integration asmdefs are out of scope by design -- their fixtures bind containers
  // with no NuGet equivalent -- so their ABSENCE is asserted rather than left to the list above.
  for (const excluded of [
    "WallstopStudios.UnityHelpers.Tests.Editor.Reflex",
    "WallstopStudios.UnityHelpers.Tests.Editor.VContainer",
    "WallstopStudios.UnityHelpers.Tests.Editor.Zenject"
  ]) {
    assert.ok(
      !editorTestGoverned.includes(excluded),
      `${excluded} is excluded from the EditMode gate and must not read as governed`
    );
  }
});

runTest("the repository passes its own static rule", () => {
  const result = spawnSync(process.execPath, [linterPath], { encoding: "utf8" });
  assert.strictEqual(result.status, 0, `${result.stdout}${result.stderr}`);
});

console.log(`\n[test-lint-typecheck-asmdef-references] ${passed} passed, ${failed} failed.`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exitCode = 1;
}
