#!/usr/bin/env node

/*
    Negative controls for the four local type-check gates.

    Those gates report a defect by failing, so a clean run and a gate that has stopped working are
    the same output. `typecheck:tests` has already been measured exiting 0 on a tree the same
    command found four WPROTO044 errors on once `obj/` was deleted (session 238), and a project
    that silently lost an `<Analyzer Include>` line, an excluded source root, or a severity would
    read exactly as green. This injects a file carrying a known diagnostic into each project and
    asserts the build names it, so the green run beside it is a measurement rather than the absence
    of one (#636).

    TWO control files rather than one, and this is measured, not stylistic: with a `CS0246` in the
    same compilation the `WUH###` analyzer reported nothing at all, while `WPROTO001` still fired
    from the generator. A combined control would therefore have reported the analyzer as broken on
    every run. The compiler control is built on its own for that reason.

    Each control also names an ANCHOR type from the tree that project is the only local compiler
    of. Without one the whole run is satisfied by the control file alone: a project whose
    `Runtime/**` glob had gone empty still loads the analyzers, still reports both diagnostics
    against the control, and still reads as a working gate over nothing. The anchor makes an empty
    tree a `CS0246` on a name that must resolve, which the exact-set match below turns into a
    failure that says so.

    The reported diagnostics must match the expected set EXACTLY. `Generator~/Directory.Build.props`
    makes warnings errors, so a tree with any other finding is a tree whose ordinary type-check is
    already red -- and reporting that here, rather than tolerating it, is what keeps a control from
    passing on a build that was going to fail anyway.

    The controls reach the compilation through the `WallstopCheckControl` property, which every
    check project declares and nothing else sets. An ordinary build adds no file.
*/

"use strict";

const { spawnSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");

/**
 * The four gates, the source tree each one is the only local compiler of, and a long-lived public
 * type from that tree. `anchorFile` is checked before any build so a moved anchor is reported as a
 * stale control rather than as a broken gate.
 */
const CHECK_PROJECTS = Object.freeze([
  {
    id: "runtime",
    tree: "Runtime/",
    project:
      "Generator~/WallstopStudios.UnityHelpers.TypeCheck/WallstopStudios.UnityHelpers.TypeCheck.csproj",
    anchor: "WallstopStudios.UnityHelpers.Core.DataStructure.Circle",
    anchorFile: "Runtime/Core/DataStructure/Circle.cs"
  },
  {
    id: "editor",
    tree: "Editor/",
    project:
      "Generator~/WallstopStudios.UnityHelpers.EditorCheck/WallstopStudios.UnityHelpers.EditorCheck.csproj",
    anchor: "WallstopStudios.UnityHelpers.Editor.Validation.SerializableDictionaryAssetValidator",
    anchorFile: "Editor/Validation/SerializableDictionaryAssetValidator.cs"
  },
  {
    id: "tests",
    tree: "Tests/Runtime/",
    project:
      "Generator~/WallstopStudios.UnityHelpers.TestCheck/WallstopStudios.UnityHelpers.TestCheck.csproj",
    anchor: "WallstopStudios.UnityHelpers.Tests.Tags.TagHandlerTests",
    anchorFile: "Tests/Runtime/Tags/TagHandlerTests.cs"
  },
  {
    id: "editor-tests",
    tree: "Tests/Editor/",
    project:
      "Generator~/WallstopStudios.UnityHelpers.EditorTestCheck/WallstopStudios.UnityHelpers.EditorTestCheck.csproj",
    anchor: "WallstopStudios.UnityHelpers.Tests.Windows.PrefabCheckerTests",
    anchorFile: "Tests/Editor/Windows/PrefabCheckerTests.cs"
  }
]);

function analyzerControl(anchor) {
  return `namespace WallstopStudios.UnityHelpers.CheckControls
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    internal sealed class ControlContractIsNotPartial
    {
        [WProtoMember(1)]
        public int value;
    }

    internal static class ControlNullPropagation
    {
        internal static readonly Type Anchor = typeof(${anchor});

        internal static string NameOf(Transform target)
        {
            return target?.name;
        }
    }
}
`;
}

function compilerControl(anchor) {
  /*
      The unresolvable name sits in a method BODY, not in a signature. Measured: with it as the
      return type, the anchor's own `CS0234` never appeared -- a declaration error stops Roslyn
      before it binds the rest of the type, so the anchor was inert and the control could not have
      told a compiling tree from an empty one.
  */
  return `namespace WallstopStudios.UnityHelpers.CheckControls
{
    using System;

    internal static class ControlMissingType
    {
        internal static readonly Type Anchor = typeof(${anchor});

        internal static object Missing()
        {
            return new ControlTypeThisGateMustNotResolve();
        }
    }
}
`;
}

/**
 * Each control names the diagnostics the build must report, and says what a miss means. A control
 * that reports nothing is the finding: the gate compiled a defect and called it clean.
 */
const CONTROLS = Object.freeze([
  {
    id: "analyzers",
    fileName: "WallstopCheckControlAnalyzers.cs",
    render: analyzerControl,
    expected: ["WPROTO001", "WUH003"],
    meaning:
      "the shipped WallstopProto generator and the WUH### analyzer are both loaded and reporting"
  },
  {
    id: "compiler",
    fileName: "WallstopCheckControlCompiler.cs",
    render: compilerControl,
    expected: ["CS0246"],
    meaning: "the compiler itself is reporting, and the project is not silencing its errors"
  }
]);

const DIAGNOSTIC_PATTERN = /\b(CS\d{4}|WPROTO\d{3}|WUH\d{3})\b/g;

function parseArguments(argv) {
  const requested = [];
  let verbose = false;
  for (const argument of argv.slice(2)) {
    if (argument === "--verbose") {
      verbose = true;
      continue;
    }
    if (argument.startsWith("--only=")) {
      requested.push(...argument.slice("--only=".length).split(","));
      continue;
    }
    throw new Error(`Unknown option ${argument}. Use --only=<id>[,<id>] or --verbose.`);
  }
  return { requested: requested.filter((entry) => entry.length > 0), verbose };
}

function build(project, controlPath) {
  const args = [
    "build",
    path.join(repoRoot, project),
    "--nologo",
    "-v",
    "minimal",
    /*
        The shared Roslyn compiler server has served a stale file snapshot to this repository
        before (session 239), and a control that a stale snapshot answered would be worthless.
    */
    "-p:UseSharedCompilation=false"
  ];
  if (controlPath !== null) {
    args.push(`-p:WallstopCheckControl=${controlPath}`);
  }
  const result = spawnSync("dotnet", args, { cwd: repoRoot, encoding: "utf8" });
  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  return { exitCode: result.status ?? 1, output };
}

function diagnosticsIn(output) {
  return new Set(output.match(DIAGNOSTIC_PATTERN) ?? []);
}

/**
 * The verdict on one control build: null when it behaved, otherwise the sentence explaining what
 * the gate failed to say. Pure, so its own self-test can drive every branch without a compiler.
 */
function classify(project, control, attempt) {
  const reported = diagnosticsIn(attempt.output);
  const missing = control.expected.filter((id) => !reported.has(id));
  const unexpected = [...reported].filter((id) => !control.expected.includes(id)).sort();
  if (attempt.exitCode === 0) {
    return (
      `${project.id} ${control.id}: the build SUCCEEDED with a control defect in it. ` +
      `Expected ${control.expected.join(" and ")} so that ${control.meaning}.`
    );
  }
  if (missing.length !== 0) {
    return (
      `${project.id} ${control.id}: the build failed but never reported ${missing.join(", ")}. ` +
      `Expected ${control.expected.join(" and ")} so that ${control.meaning}. ` +
      `Reported: ${[...reported].sort().join(", ") || "nothing"}.`
    );
  }
  if (unexpected.length !== 0) {
    return (
      `${project.id} ${control.id}: the control fired, but the build also reported ` +
      `${unexpected.join(", ")}. Either ${project.tree} does not type-check on its own -- run ` +
      `the ordinary gate and fix that first -- or the anchor ${project.anchor} no longer ` +
      `resolves, which means this project has stopped compiling that tree.`
    );
  }
  return null;
}

function main() {
  const { requested, verbose } = parseArguments(process.argv);
  const projects =
    requested.length === 0
      ? [...CHECK_PROJECTS]
      : CHECK_PROJECTS.filter((project) => requested.includes(project.id));
  if (projects.length === 0) {
    throw new Error(
      `No check project matched ${requested.join(", ")}. Known ids: ` +
        CHECK_PROJECTS.map((project) => project.id).join(", ")
    );
  }

  for (const project of projects) {
    const anchorPath = path.join(repoRoot, project.anchorFile);
    if (!fs.existsSync(anchorPath)) {
      throw new Error(
        `${project.id}: the anchor file ${project.anchorFile} is gone, so its control would ` +
          `report a moved type as a broken gate. Point ${project.id} at another long-lived ` +
          `public type in ${project.tree}.`
      );
    }
  }

  const controlRoot = fs.mkdtempSync(path.join(os.tmpdir(), "unity-helpers-check-controls-"));
  const failures = [];
  try {
    for (const project of projects) {
      for (const control of CONTROLS) {
        const controlPath = path.join(controlRoot, `${project.id}-${control.fileName}`);
        fs.writeFileSync(controlPath, control.render(project.anchor), "utf8");
        const attempt = build(project.project, controlPath);
        if (verbose) {
          console.log(attempt.output);
        }
        const failure = classify(project, control, attempt);
        if (failure !== null) {
          failures.push(failure);
          continue;
        }
        console.log(
          `  [PASS] ${project.id}: the ${control.id} control over ${project.tree} reported ` +
            `${control.expected.join(", ")} and nothing else`
        );
      }
    }
  } finally {
    fs.rmSync(controlRoot, { recursive: true, force: true });
  }

  console.log("");
  if (failures.length !== 0) {
    console.error(`[typecheck-controls] ${failures.length} control(s) failed:`);
    for (const failure of failures) {
      console.error(`  - ${failure}`);
    }
    console.error(
      "[typecheck-controls] A control that does not fire means the gate beside it is not " +
        "measuring what its green run claims."
    );
    return 1;
  }
  console.log(
    `[typecheck-controls] OK: ${projects.length} check project(s), ${CONTROLS.length} control(s) ` +
      `each, every expected diagnostic reported and nothing else.`
  );
  return 0;
}

try {
  process.exitCode = main();
} catch (error) {
  console.error(`[typecheck-controls] ${error.message}`);
  process.exitCode = 2;
}
