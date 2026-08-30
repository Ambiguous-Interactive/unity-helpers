#!/usr/bin/env node
/**
 * Documentation may not name a namespace or an assembly this repository does not have.
 *
 * `lint:code-samples` extracts 3,061 C# blocks out of `docs/` and validates none of them, so an
 * example that names something moved or renamed reads as correct forever. The damage is silent
 * twice over: the reader copies it, and the person who moved the namespace gets no signal at all.
 *
 * TWO RULES, both chosen because they have no false positives. A general "does this API exist"
 * check needs a real parser -- a first attempt keyed on `Type.Member` reported
 * `Serializer.ProtoDeserialize` as missing, because a regex over declarations cannot see a generic
 * method -- and a gate that cries wolf is one people stop reading:
 *
 *   1. Every `using WallstopStudios.UnityHelpers...;` in a Markdown file must name a namespace some
 *      `.cs` file under the governed source roots declares. A `using` is unambiguous: it is a
 *      namespace, spelled in full, or it does not compile.
 *   2. Every `<assembly fullname="WallstopStudios...">` in a `link.xml` example must name an
 *      assembly some `.asmdef` declares. A wrong name here is worse than a compile error, because
 *      the linker silently preserves nothing and the failure arrives as a stripped player.
 *
 * Both found a real defect on the run that introduced them: `Core.Attribute` for `Core.Attributes`
 * in the enum display-name example, and `WallstopStudios.UnityHelpers.Runtime` for the runtime
 * assembly, which is named `WallstopStudios.UnityHelpers`
 * ([#441](https://github.com/Ambiguous-Interactive/unity-helpers/issues/441)).
 *
 * Exit codes: 0 = every name resolves, 1 = at least one does not.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it, so the
// default is the only path that ships.
const SCAN_ROOT = process.env.DOC_IDENTIFIER_ROOT
  ? path.resolve(process.env.DOC_IDENTIFIER_ROOT)
  : REPO_ROOT;

/** Where a namespace or an assembly may be declared. */
const SOURCE_ROOTS = ["Runtime", "Editor", "Tests", "Generator~", "Samples~"];

/** Where documentation is read from. */
const DOC_ROOTS = ["docs"];

const SKIPPED_DIRECTORIES = new Set(["obj", "bin", "node_modules", "Library", "artifacts"]);

const USING_PATTERN = /^\s*using\s+(WallstopStudios(?:\.[A-Za-z0-9_]+)*)\s*;/;
const ASSEMBLY_PATTERN = /<assembly\s+fullname="(WallstopStudios[^"]*)"/g;
const NAMESPACE_PATTERN = /^[ \t]*namespace\s+([A-Za-z0-9_.]+)/gm;

/**
 * Every file under a root whose name ends with one of the given extensions.
 *
 * @param {string} root Absolute directory to walk.
 * @param {string[]} extensions Suffixes to keep.
 * @returns {string[]} Absolute paths.
 */
function filesUnder(root, extensions) {
  if (!fs.existsSync(root)) {
    return [];
  }

  const found = [];
  const pending = [root];
  while (0 < pending.length) {
    const current = pending.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      if (entry.isDirectory()) {
        if (!SKIPPED_DIRECTORIES.has(entry.name) && !entry.name.startsWith(".")) {
          pending.push(path.join(current, entry.name));
        }

        continue;
      }

      if (extensions.some((extension) => entry.name.endsWith(extension))) {
        found.push(path.join(current, entry.name));
      }
    }
  }

  return found;
}

/**
 * The namespaces and assembly names this repository actually declares.
 *
 * @param {string} root Repository root to read.
 * @returns {{namespaces: Set<string>, assemblies: Set<string>}} What exists.
 */
function declared(root) {
  const namespaces = new Set();
  const assemblies = new Set();

  for (const sourceRoot of SOURCE_ROOTS) {
    for (const file of filesUnder(path.join(root, sourceRoot), [".cs"])) {
      const text = fs.readFileSync(file, "utf8");
      NAMESPACE_PATTERN.lastIndex = 0;
      let match;
      while ((match = NAMESPACE_PATTERN.exec(text)) !== null) {
        // Every ancestor too: `using A.B;` is legal wherever `A.B.C` is declared.
        const parts = match[1].split(".");
        for (let length = 1; length <= parts.length; length++) {
          namespaces.add(parts.slice(0, length).join("."));
        }
      }
    }

    for (const file of filesUnder(path.join(root, sourceRoot), [".asmdef"])) {
      try {
        const name = JSON.parse(fs.readFileSync(file, "utf8")).name;
        if (typeof name === "string" && 0 < name.length) {
          assemblies.add(name);
        }
      } catch {
        // An unreadable asmdef is lint-asmdef's subject, not this one's. Skipping it here can only
        // produce a false positive further down, which the report names precisely enough to see.
      }
    }
  }

  return { namespaces, assemblies };
}

/**
 * Checks every documentation file against what the sources declare.
 *
 * @param {string} root Repository root to scan.
 * @returns {{violations: string[], usings: number, assemblies: number}} What was checked and what failed.
 */
function analyze(root) {
  const { namespaces, assemblies } = declared(root);
  const violations = [];
  let usingCount = 0;
  let assemblyCount = 0;

  for (const docRoot of DOC_ROOTS) {
    for (const file of filesUnder(path.join(root, docRoot), [".md"])) {
      const relative = path.relative(root, file).split(path.sep).join("/");
      const lines = fs.readFileSync(file, "utf8").split("\n");
      lines.forEach((line, index) => {
        const usingMatch = line.match(USING_PATTERN);
        if (usingMatch !== null) {
          usingCount++;
          if (!namespaces.has(usingMatch[1])) {
            violations.push(
              `${relative}:${index + 1}: 'using ${usingMatch[1]};' names a namespace nothing declares. ` +
                `A reader copying this example gets a compile error.`
            );
          }
        }

        ASSEMBLY_PATTERN.lastIndex = 0;
        let assemblyMatch;
        while ((assemblyMatch = ASSEMBLY_PATTERN.exec(line)) !== null) {
          assemblyCount++;
          if (!assemblies.has(assemblyMatch[1])) {
            violations.push(
              `${relative}:${index + 1}: <assembly fullname="${assemblyMatch[1]}"> names an assembly ` +
                `no .asmdef declares. The linker preserves nothing under a wrong name and reports ` +
                `nothing, so this surfaces as a stripped player rather than as an error.`
            );
          }
        }
      });
    }
  }

  return { violations, usings: usingCount, assemblies: assemblyCount };
}

function main() {
  const { violations, usings, assemblies } = analyze(SCAN_ROOT);

  if (0 < violations.length) {
    console.error(
      `[lint-doc-identifiers] ${violations.length} documentation reference(s) do not resolve:`
    );
    for (const violation of violations) {
      console.error(`  ${violation}`);
    }

    process.exitCode = 1;
    return;
  }

  // The counts are the gate's own red half at a glance: a scan that checked nothing would say so
  // here rather than printing the same success line a clean corpus does.
  console.log(
    `[lint-doc-identifiers] ${usings} package using directive(s) and ${assemblies} assembly ` +
      `reference(s) in docs all resolve.`
  );
}

if (require.main === module) {
  main();
}

module.exports = { analyze, declared, filesUnder, SOURCE_ROOTS, DOC_ROOTS };
