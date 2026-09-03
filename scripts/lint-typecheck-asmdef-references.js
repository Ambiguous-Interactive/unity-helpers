#!/usr/bin/env node
/**
 * The typecheck projects may not compile a governed source tree against an assembly Unity would
 * refuse it.
 *
 * `Generator~/*Check` compiles `Runtime/**`, `Editor/**` and the PlayMode and EditMode test trees
 * into ONE assembly each, with one flat reference list. Unity does not: it compiles each `.asmdef` on its
 * own, and a test asmdef sets `overrideReferences: true`, which means the assemblies it names in
 * `precompiledReferences` are the ONLY precompiled assemblies its sources may bind. A reference the
 * project holds and the asmdef does not is therefore a compilation nobody ships -- the local gate
 * goes green and the editor reports `CS0012` for a type in an unreferenced assembly.
 *
 * That is not hypothetical. A hand-written `JsonConverter` under `Tests/Runtime/Performance` used
 * `JsonEncodedText.Encode`, whose optional parameter is a `JavaScriptEncoder` from
 * `System.Text.Encodings.Web`. All four `typecheck:tests` configurations compiled it clean, because
 * `TestCheck.csproj` references that assembly; Unity failed it 25 times over
 * ([#598](https://github.com/Ambiguous-Interactive/unity-helpers/issues/598)).
 *
 * THREE RULES, because neither of the first two can be answered the same way:
 *
 *   1. STATIC (default). Every repo-bundled `<Reference>` a project holds is checked against every
 *      `overrideReferences: true` asmdef whose sources that project compiles. An assembly some
 *      governed asmdef does not declare must be named in UNDECLARED_BY_DESIGN below, with the
 *      reason. Each entry is checked for being both TRUE and NECESSARY, so the table is a work
 *      list rather than an excuse.
 *   2. PROBE (`--probe`). The static rule cannot see USAGE: an assembly kept for `Runtime/**` is
 *      still on the test sources' reference list, so a fixture reaching for it still compiles.
 *      The probe rebuilds the project with exactly those assemblies dropped -- via the csproj's
 *      own `WallstopRuntimeOnlyReferences` switch -- and reports any compiler error raised in a
 *      file a governed asmdef owns. Errors inside `Runtime/**` are the point of the drop and are
 *      ignored. It runs the DEFAULT define configuration only; a fixture that reaches for one of
 *      these assemblies solely under `WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR` or `SINGLE_THREADED`
 *      is still Unity's to find.
 *   3. UNGOVERNED. Four of the six projects compile only trees whose asmdefs are
 *      `overrideReferences: false`, so rule 1 constrains nothing about them and rule 2 has nothing
 *      to drop. That is correct and it is also what a project whose `<Compile>` globs had gone
 *      EMPTY would report, so each one is recorded in UNGOVERNED_BY_DESIGN below with its reason
 *      and checked both ways. A gate reporting zero findings has to be able to say it had subjects
 *      ([honest-gates](../.llm/skills/honest-gates.md)), and `Runtime/Integrations/**` spent its
 *      whole life outside every project's globs
 *      ([#687](https://github.com/Ambiguous-Interactive/unity-helpers/issues/687)) -- so "nothing
 *      in scope" is exactly the sentence that must never pass unread here.
 *
 * Exit codes: 0 = every governed asmdef can see what its sources are compiled against, 1 = drift.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it, so the
// default is the only path that ships.
const SCAN_ROOT = process.env.TYPECHECK_ASMDEF_REFERENCE_ROOT
  ? path.resolve(process.env.TYPECHECK_ASMDEF_REFERENCE_ROOT)
  : REPO_ROOT;

/** The MSBuild switch a project must expose so `--probe` can drop its Runtime-only references. */
const PROBE_PROPERTY = "WallstopRuntimeOnlyReferences";

/**
 * Assemblies a typecheck project references that some governed asmdef does not declare, with the
 * reason that is correct rather than drift. Keyed `<project>::<assembly>`.
 *
 * Every entry here is a claim that the assembly is needed by a tree whose asmdef sets
 * `overrideReferences: false` -- `Runtime/**` and `Editor/**`, which auto-reference every bundled
 * plugin and so constrain nothing. Measured for #598 by dropping each reference and reading which
 * files the compiler then failed: all 330 errors landed in `Runtime/`, none in `Tests/`.
 */
const UNDECLARED_BY_DESIGN = new Map([
  [
    "WallstopStudios.UnityHelpers.TestCheck::System.Text.Encodings.Web",
    "Runtime/**: the JsonConverters call JsonEncodedText.Encode, whose optional parameter is a JavaScriptEncoder"
  ],
  [
    "WallstopStudios.UnityHelpers.TestCheck::Microsoft.Bcl.AsyncInterfaces",
    "Runtime/**: Serializer.cs binds IAsyncDisposable"
  ],
  [
    "WallstopStudios.UnityHelpers.TestCheck::System.Runtime.CompilerServices.Unsafe",
    "Runtime/**: WProtoScalarFormatters, EnumExtensions, the R-trees and RuntimeSingleton call Unsafe.As/SizeOf"
  ],
  [
    "WallstopStudios.UnityHelpers.TestCheck::protobuf-net",
    "Runtime/**, and the three Tests/Runtime asmdefs declare it; only Tests/Core does not, and no fixture there names ProtoBuf"
  ],
  [
    "WallstopStudios.UnityHelpers.TestCheck::protobuf-net.Core",
    "Runtime/**, and the three Tests/Runtime asmdefs declare it; only Tests/Core does not, and no fixture there names ProtoBuf"
  ],
  [
    "WallstopStudios.UnityHelpers.TestCheck::System.Collections.Immutable",
    "Runtime/**, and the three Tests/Runtime asmdefs declare it; only Tests/Core does not, and no fixture there names ImmutableArray"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::System.Text.Json",
    "Runtime/**, and six of the 28 governed asmdefs declare it; measured across Tests/Editor and Tests/Core, no fixture in the other 22 names a System.Text.Json type"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::protobuf-net",
    "Runtime/**, and Tests.Runtime declares it; no editmode fixture names ProtoBuf"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::protobuf-net.Core",
    "Runtime/**, and Tests.Runtime declares it; no editmode fixture names ProtoBuf"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::System.Collections.Immutable",
    "Runtime/**, and Tests.Runtime declares it; no editmode fixture names ImmutableArray"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::System.Runtime.CompilerServices.Unsafe",
    "Runtime/**: WProtoScalarFormatters, EnumExtensions, the R-trees and RuntimeSingleton call Unsafe.As/SizeOf"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::System.Text.Encodings.Web",
    "Runtime/**: the JsonConverters call JsonEncodedText.Encode, whose optional parameter is a JavaScriptEncoder"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorTestCheck::Microsoft.Bcl.AsyncInterfaces",
    "Runtime/**: Serializer.cs binds IAsyncDisposable"
  ]
]);

/**
 * Check projects for which NO `overrideReferences: true` asmdef owns a compiled source, keyed by
 * project name, with the reason that makes it correct rather than a scan that found nothing.
 *
 * Checked both ways, like the table above: a project that reaches this state without an entry is a
 * failure, and an entry for a project that has since taken a governed tree is stale. Rule 1 and
 * rule 2 both go quiet here, and a project whose `<Compile>` globs had gone empty would go quiet in
 * exactly the same words -- which is the shape #687 turned out to be over a tree
 * every other project named in an `Exclude`.
 */
const UNGOVERNED_BY_DESIGN = new Map([
  [
    "WallstopStudios.UnityHelpers.DocSamplesCheck",
    "compiles the extracted samples under artifacts/doc-samples, which no asmdef owns (#611)"
  ],
  [
    "WallstopStudios.UnityHelpers.EditorCheck",
    "compiles Runtime/** and Editor/**, and both asmdefs are overrideReferences: false, so Unity auto-references every bundled plugin into them"
  ],
  [
    "WallstopStudios.UnityHelpers.IntegrationCheck",
    "compiles Runtime/Integrations/**, and all three integration asmdefs are overrideReferences: false; they name Reflex/VContainer/Zenject as asmdef references rather than precompiled ones, so there is no precompiledReferences list to be more permissive than (#687)"
  ],
  [
    "WallstopStudios.UnityHelpers.TypeCheck",
    "compiles Runtime/** alone, and the runtime asmdef is overrideReferences: false"
  ]
]);

/** Reads a csproj into the pieces this gate reasons about. */
function parseProject(csprojPath, scanRoot) {
  const text = fs.readFileSync(csprojPath, "utf8");
  const projectDirectory = path.dirname(csprojPath);
  const resolve = (value) =>
    path.resolve(projectDirectory, value.replace(/\$\(RepoRoot\)/g, scanRoot).replace(/\\/g, "/"));

  const references = [];
  const referencePattern =
    /<Reference\s+Include="([^"]+)"([^>]*)>([\s\S]*?)<\/Reference>|<Reference\s+Include="([^"]+)"([^>]*)\/>/g;
  let match;
  while ((match = referencePattern.exec(text)) !== null) {
    const name = match[1] ?? match[4];
    const attributes = match[2] ?? match[5] ?? "";
    const body = match[3] ?? "";
    const hint = /<HintPath>([^<]+)<\/HintPath>/.exec(body);
    if (hint === null) {
      continue;
    }
    const raw = hint[1].trim();
    // Only assemblies vendored in this repository are ones Unity resolves through an asmdef's
    // precompiledReferences. The Unity reference assemblies come from NuGet, under a package path
    // MSBuild resolves and this parser does not -- and a `$(PkgUnity3D_SDK)/...` left unexpanded
    // resolves RELATIVE to the project directory, which lands inside the repository and reads as
    // vendored. Any property other than $(RepoRoot) is therefore treated as foreign, which is what
    // it is: no asmdef can name the editor's own assemblies.
    if (/\$\((?!RepoRoot\))/.test(raw)) {
      continue;
    }
    const hintPath = resolve(raw);
    if (!hintPath.startsWith(scanRoot + path.sep)) {
      continue;
    }
    references.push({
      name,
      hintPath,
      // A reference the probe can drop must be switchable from the command line, or `--probe` is a
      // promise the project cannot keep.
      probeSwitchable:
        new RegExp(`'\\$\\(${PROBE_PROPERTY}\\)'`).test(attributes) ||
        new RegExp(`'\\$\\(${PROBE_PROPERTY}\\)'`).test(body)
    });
  }

  const includes = [];
  const excludes = [];
  const compilePattern = /<Compile\b([\s\S]*?)\/?>/g;
  while ((match = compilePattern.exec(text)) !== null) {
    const attributes = match[1];
    const include = /\bInclude="([^"]+)"/.exec(attributes);
    const exclude = /\bExclude="([^"]+)"/.exec(attributes);
    const remove = /\bRemove="([^"]+)"/.exec(attributes);
    for (const value of include ? include[1].split(";") : []) {
      includes.push(resolve(value));
    }
    for (const value of exclude ? exclude[1].split(";") : []) {
      excludes.push(resolve(value));
    }
    for (const value of remove ? remove[1].split(";") : []) {
      excludes.push(resolve(value));
    }
  }

  return {
    name: path.basename(csprojPath, ".csproj"),
    csprojPath,
    references,
    includes,
    excludes
  };
}

/** MSBuild's globbing, narrowed to the two wildcards these projects use. */
function globToRegExp(pattern) {
  let source = "";
  for (let index = 0; index < pattern.length; index += 1) {
    const character = pattern[index];
    if (character === "*" && pattern[index + 1] === "*") {
      // `**/` matches zero or more directories, which is why the separator is optional here.
      source += "(?:.*)";
      index += 1;
      if (pattern[index + 1] === "/") {
        index += 1;
      }
      continue;
    }
    if (character === "*") {
      source += "[^/]*";
      continue;
    }
    source += character.replace(/[.+?^${}()|[\]\\]/g, "\\$&");
  }
  return new RegExp(`^${source}$`);
}

/** Every `.cs` file under `directory`, as absolute paths with forward slashes. */
function collectSources(directory, out) {
  let entries;
  try {
    entries = fs.readdirSync(directory, { withFileTypes: true });
  } catch {
    return out;
  }
  for (const entry of entries) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (isSkippableDirectory(entry.name)) {
        continue;
      }
      collectSources(full, out);
    } else if (entry.isFile() && entry.name.endsWith(".cs")) {
      out.push(full.replace(/\\/g, "/"));
    }
  }
  return out;
}

/**
 * Directories no scan needs to enter. `.git` alone holds tens of thousands of files on this
 * repository, and walking it cost 13 of the 14 seconds this gate took before it was skipped.
 */
function isSkippableDirectory(name) {
  return (
    name.startsWith(".") ||
    name === "obj" ||
    name === "bin" ||
    name === "node_modules" ||
    name === "Library" ||
    name === "Temp"
  );
}

/** Every `.asmdef` under `root`, keyed by the directory that owns it. */
function collectAsmdefs(root) {
  const found = new Map();
  const walk = (directory) => {
    let entries;
    try {
      entries = fs.readdirSync(directory, { withFileTypes: true });
    } catch {
      return;
    }
    for (const entry of entries) {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        if (isSkippableDirectory(entry.name)) {
          continue;
        }
        walk(full);
      } else if (entry.isFile() && entry.name.endsWith(".asmdef")) {
        let parsed;
        try {
          parsed = JSON.parse(fs.readFileSync(full, "utf8"));
        } catch (error) {
          throw new Error(`${full} is not valid JSON: ${error.message}`);
        }
        found.set(directory.replace(/\\/g, "/"), {
          file: full,
          name: parsed.name ?? path.basename(full, ".asmdef"),
          overrideReferences: parsed.overrideReferences === true,
          precompiledReferences: new Set(parsed.precompiledReferences ?? []),
          // Read here rather than by a second asmdef walker: lint-editor-assembly-monobehaviours.js
          // asks the same "which asmdef owns this file" question of the same tree, and two helpers
          // answering one question answer it differently (see .llm/skills/honest-gates.md).
          includePlatforms: parsed.includePlatforms ?? []
        });
      }
    }
  };
  walk(root);
  return found;
}

/** The asmdef that owns `file`: the nearest ancestor directory holding one. */
function ownerOf(file, asmdefs) {
  let directory = path.dirname(file).replace(/\\/g, "/");
  for (;;) {
    const owner = asmdefs.get(directory);
    if (owner) {
      return owner;
    }
    const parent = path.dirname(directory);
    if (parent === directory) {
      return null;
    }
    directory = parent;
  }
}

/** The `overrideReferences: true` asmdefs whose sources `project` compiles. */
function governedAsmdefs(project, asmdefs, scanRoot) {
  const includeMatchers = project.includes.map(globToRegExp);
  const excludeMatchers = project.excludes.map(globToRegExp);
  const roots = new Set();
  for (const include of project.includes) {
    const wildcard = include.indexOf("*");
    roots.add(wildcard < 0 ? path.dirname(include) : path.dirname(include.slice(0, wildcard)));
  }

  const governed = new Map();
  for (const root of roots) {
    if (!root.startsWith(scanRoot)) {
      continue;
    }
    for (const file of collectSources(root, [])) {
      if (!includeMatchers.some((matcher) => matcher.test(file))) {
        continue;
      }
      if (excludeMatchers.some((matcher) => matcher.test(file))) {
        continue;
      }
      const owner = ownerOf(file, asmdefs);
      if (owner && owner.overrideReferences) {
        governed.set(owner.name, owner);
      }
    }
  }
  return governed;
}

/**
 * The static rule, plus the ungoverned rule. Returns `{ failures, checked, runtimeOnly }`, where
 * `runtimeOnly` names the references no governed asmdef declares -- the set `--probe` exists to
 * interrogate.
 *
 * `ungoverned` is the UNGOVERNED_BY_DESIGN table, or null to skip rule 3 entirely. Null is what a
 * FIXTURE tree passes: the table describes this repository's own projects, so against a fixture
 * every entry would read as stale and every fixture project as unrecorded.
 */
function analyze(projects, asmdefs, table, scanRoot, ungoverned = null) {
  const failures = [];
  const checked = [];
  const runtimeOnly = new Map();
  const justified = new Set();
  const ungovernedSeen = new Set();

  for (const project of projects) {
    const governed = governedAsmdefs(project, asmdefs, scanRoot);
    if (governed.size === 0) {
      // `Runtime/**`, `Editor/**` and `Runtime/Integrations/**` are `overrideReferences: false`:
      // Unity auto-references every bundled plugin into them, so those projects cannot be more
      // permissive than the editor and there is nothing here to check. Said out loud rather than
      // passed silently, and rule 3 makes the silence a recorded decision rather than a scan that
      // may simply have found no sources.
      const reason = ungoverned === null ? null : ungoverned.get(project.name);
      if (ungoverned !== null && !reason) {
        failures.push(
          `${project.name} compiles no source owned by an overrideReferences asmdef, so neither ` +
            `the static rule nor --probe constrains it. That is how a project whose <Compile> ` +
            `globs have gone empty reads too. Record why in UNGOVERNED_BY_DESIGN, or point the ` +
            `project at the tree it is supposed to gate.`
        );
        continue;
      }
      ungovernedSeen.add(project.name);
      checked.push(
        `${project.name}: no overrideReferences asmdef in scope` +
          (reason ? ` (ungoverned by design: ${reason})` : "")
      );
      continue;
    }
    if (ungoverned !== null && ungoverned.has(project.name)) {
      // Seen, so the tail sweep below reports this once and as the right thing: the entry is
      // stale because the project GAINED a governed tree, not because the project is gone.
      ungovernedSeen.add(project.name);
      failures.push(
        `UNGOVERNED_BY_DESIGN names ${project.name}, but ${governed.size} overrideReferences ` +
          `asmdef(s) now own sources it compiles: ${[...governed.keys()].sort().join(", ")}. ` +
          `Remove the entry so the static rule and --probe start judging it.`
      );
    }

    const undeclaredHere = [];
    for (const reference of project.references) {
      const declaration = `${reference.name}.dll`;
      const missing = [...governed.values()]
        .filter((asmdef) => !asmdef.precompiledReferences.has(declaration))
        .map((asmdef) => asmdef.name)
        .sort();
      const key = `${project.name}::${reference.name}`;
      if (missing.length === 0) {
        checked.push(`${project.name}: ${reference.name} declared by all ${governed.size}`);
        continue;
      }
      const reason = table.get(key);
      if (!reason) {
        failures.push(
          `${project.name} references ${reference.name}, which ${missing.length} of ${governed.size} ` +
            `governed asmdef(s) do not declare: ${missing.join(", ")}. Unity compiles those sources ` +
            `without it, so this gate is more permissive than the editor. Declare it in each asmdef's ` +
            `precompiledReferences, drop the reference, or record why in UNDECLARED_BY_DESIGN.`
        );
        continue;
      }
      justified.add(key);
      undeclaredHere.push({ reference, missing, reason });
      checked.push(`${project.name}: ${reference.name} undeclared by design (${reason})`);
    }

    const nobodyDeclares = undeclaredHere.filter((entry) => entry.missing.length === governed.size);
    if (0 < nobodyDeclares.length) {
      runtimeOnly.set(
        project,
        nobodyDeclares.map((entry) => entry.reference)
      );
    }
    for (const entry of nobodyDeclares) {
      if (!entry.reference.probeSwitchable) {
        failures.push(
          `${project.name} references ${entry.reference.name}, which NO governed asmdef declares, ` +
            `but the reference is not conditioned on '$(${PROBE_PROPERTY})'. Without that switch ` +
            `--probe cannot drop it, and nothing local can tell whether a fixture has started using it.`
        );
      }
    }
  }

  // An excuse that names a project or a reference that is gone stops being read and starts being
  // believed. The same shape as the missing-red-half list in test-run-repo-lint.js, for the same
  // reason.
  for (const key of table.keys()) {
    if (!justified.has(key)) {
      failures.push(
        `UNDECLARED_BY_DESIGN names ${key}, but that project no longer holds that reference or ` +
          `every governed asmdef now declares it. Remove the entry.`
      );
    }
  }

  if (ungoverned !== null) {
    for (const name of ungoverned.keys()) {
      if (!ungovernedSeen.has(name)) {
        failures.push(
          `UNGOVERNED_BY_DESIGN names ${name}, which is not a check project any more. Remove the ` +
            `entry.`
        );
      }
    }
  }

  return { failures, checked, runtimeOnly };
}

/**
 * The probe rule. Rebuilds `project` without its Runtime-only references and reports every compiler
 * error raised in a file a governed asmdef owns.
 */
function probeProject(project, asmdefs, scanRoot, log) {
  const build = spawnSync(
    "dotnet",
    [
      "build",
      project.csprojPath,
      "--nologo",
      "-v",
      "minimal",
      `-p:${PROBE_PROPERTY}=false`,
      // A separate intermediate directory: the probe build is EXPECTED to fail, and sharing `obj/`
      // would hand that failed state to the next real typecheck run.
      "-p:BaseIntermediateOutputPath=obj/probe/",
      "-p:BaseOutputPath=bin/probe/"
    ],
    { encoding: "utf8", cwd: scanRoot }
  );
  if (build.error) {
    return [`${project.name}: could not run dotnet build (${build.error.message})`];
  }
  const output = `${build.stdout ?? ""}\n${build.stderr ?? ""}`;
  const governed = governedAsmdefs(project, asmdefs, scanRoot);
  log(`${project.name}: probe build finished, ${governed.size} governed asmdef(s) in scope`);

  // A probe that reports nothing has two very different causes and they print the same thing, which
  // is the failure #556 is about. Dropping assemblies `Runtime/**` genuinely needs MUST break the
  // compile; if it did not, either the build never reached the compiler (a malformed csproj exits
  // with MSB####, whose lines this parser rightly ignores) or the references are dead weight.
  // Measured: an XML comment containing `--` made this probe report a clean tree while compiling
  // nothing at all.
  if (!/\berror CS\d+/.test(output)) {
    return [
      `${project.name}: the probe build dropped every Runtime-only reference and the compiler ` +
        `reported no error at all. Either the build failed before compiling (exit ${build.status}, ` +
        `last line: ${lastMeaningfulLine(output)}) or nothing needs those references and they ` +
        `should be removed from the project outright.`
    ];
  }

  return violationsFromBuildOutput(
    output,
    (file) => {
      const owner = ownerOf(file, asmdefs);
      return owner && governed.has(owner.name) ? owner.name : null;
    },
    project.name
  );
}

/** The last non-blank line of build output, for a diagnostic that would otherwise say nothing. */
function lastMeaningfulLine(output) {
  const lines = output
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== "");
  return lines.length === 0 ? "(no output)" : lines[lines.length - 1];
}

/**
 * Compiler errors the probe build raised inside a governed asmdef. `attribute` maps an absolute
 * source path to the governed asmdef that owns it, or null.
 *
 * Errors in `Runtime/**` are the whole point of dropping the references and are dropped here.
 * Split out so the self-test can drive it against recorded compiler output instead of a build.
 */
function violationsFromBuildOutput(output, attribute, projectName) {
  const violations = new Map();
  const pattern = /^(.*?)\((\d+),\d+\):\s*error\s+(CS\d+):\s*(.*?)(?:\s*\[[^[\]]*\])?$/gm;
  let match;
  while ((match = pattern.exec(output)) !== null) {
    const file = match[1].trim().replace(/\\/g, "/");
    const owner = attribute(file);
    if (!owner) {
      continue;
    }
    const key = `${file}(${match[2]}) ${match[3]}`;
    if (!violations.has(key)) {
      violations.set(
        key,
        `${projectName}: ${owner} owns ${file}(${match[2]}), which fails as ${match[3]}: ` +
          `${match[4].trim()} -- that assembly is referenced by the typecheck project and by no ` +
          `test asmdef, so Unity will refuse this file.`
      );
    }
  }
  return [...violations.values()];
}

function discoverProjects(scanRoot) {
  const generator = path.join(scanRoot, "Generator~");
  let entries;
  try {
    entries = fs.readdirSync(generator, { withFileTypes: true });
  } catch {
    return [];
  }
  const projects = [];
  for (const entry of entries) {
    if (!entry.isDirectory() || !entry.name.endsWith("Check")) {
      continue;
    }
    const csproj = path.join(generator, entry.name, `${entry.name}.csproj`);
    if (fs.existsSync(csproj)) {
      projects.push(parseProject(csproj, scanRoot));
    }
  }
  return projects.sort((left, right) => left.name.localeCompare(right.name));
}

function main() {
  const verbose = process.argv.includes("--verbose");
  const probe = process.argv.includes("--probe");
  const log = (message) => {
    if (verbose) {
      console.log(`  ..   ${message}`);
    }
  };

  const projects = discoverProjects(SCAN_ROOT);
  if (projects.length === 0) {
    console.error(
      "[lint-typecheck-asmdef-references] Found no Generator~/*Check project at all. " +
        "That is a broken scan rather than a clean repository."
    );
    process.exit(1);
  }

  const asmdefs = collectAsmdefs(SCAN_ROOT);
  if (asmdefs.size === 0) {
    console.error(
      "[lint-typecheck-asmdef-references] Found no .asmdef anywhere under the scan root. " +
        "That is a broken scan rather than a clean repository."
    );
    process.exit(1);
  }

  // Both tables describe THIS repository's own check projects. Pointed at a fixture tree the first
  // would report every entry as stale and the second every fixture project as unrecorded, which
  // would make the self-test's green half unreachable and the red half pass for the wrong reason.
  const ownRepository = SCAN_ROOT === REPO_ROOT;
  const table = ownRepository ? UNDECLARED_BY_DESIGN : new Map();
  const ungoverned = ownRepository ? UNGOVERNED_BY_DESIGN : null;
  const { failures, checked, runtimeOnly } = analyze(
    projects,
    asmdefs,
    table,
    SCAN_ROOT,
    ungoverned
  );
  for (const line of checked) {
    log(line);
  }

  if (probe) {
    for (const project of projects) {
      const references = runtimeOnly.get(project);
      if (!references) {
        // Named rather than skipped: a probe that quietly walks past a project is the same output
        // as a probe that has lost it, and rule 3 above is what makes this sentence checkable.
        log(
          `${project.name}: nothing to probe (${
            ungoverned?.get(project.name) ??
            "every reference it holds is declared by every governed asmdef"
          })`
        );
        continue;
      }
      log(
        `${project.name}: probing without ${references.map((reference) => reference.name).join(", ")}`
      );
      failures.push(...probeProject(project, asmdefs, SCAN_ROOT, log));
    }
  }

  if (0 < failures.length) {
    console.error("");
    for (const failure of failures) {
      console.error(`  FAIL ${failure}`);
    }
    console.error(
      `\n[lint-typecheck-asmdef-references] ${failures.length} reference(s) drift from what Unity compiles.`
    );
    process.exit(1);
  }

  console.log(
    `[lint-typecheck-asmdef-references] ${projects.length} typecheck project(s) reference only what ` +
      `their governed asmdefs declare${probe ? ", and no fixture uses a Runtime-only assembly" : ""}.`
  );
}

if (require.main === module) {
  main();
}

module.exports = {
  analyze,
  collectAsmdefs,
  discoverProjects,
  globToRegExp,
  governedAsmdefs,
  ownerOf,
  parseProject,
  lastMeaningfulLine,
  violationsFromBuildOutput,
  PROBE_PROPERTY,
  UNDECLARED_BY_DESIGN,
  UNGOVERNED_BY_DESIGN
};
