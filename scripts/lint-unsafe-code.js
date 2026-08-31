#!/usr/bin/env node

/*
    Refuses compiler-unsafe code and the switches that permit it.

    The shipped runtime assembly enabled allowUnsafeCode for two BMI2 branches behind
    NETCOREAPP3_0_OR_GREATER and NET7_0_OR_GREATER -- symbols no Unity player defines, so neither
    ever ran, and one of them only needed the flag because `unsafe` was on a method signature
    rather than around the dead block. A wrong pointer, layout or index assumption is undefined
    behaviour on IL2CPP, so the flag itself is the thing worth refusing
    (https://github.com/Ambiguous-Interactive/unity-helpers/issues/637).

    Scope is package-owned source only: Runtime/, Editor/ and Tests/ assembly definitions and C#
    sources, plus the Generator~ check projects, whose AllowUnsafeBlocks is what makes the local
    gate agree with Unity. Vendored trees are excluded by name below.
*/

"use strict";

const { existsSync, readFileSync } = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const SOURCE_ROOTS = ["Runtime", "Editor", "Tests"];
const VENDORED_PREFIXES = ["Runtime/Utils/SevenZip/"];
const ISSUE = "see issue #637";

/** Blanks comments and string literals so the keyword scan cannot match prose. */
function codeOnly(text) {
  let output = "";
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    const next = index + 1 < text.length ? text[index + 1] : "";
    if (character === "/" && next === "/") {
      while (index < text.length && text[index] !== "\n") {
        index++;
      }
      continue;
    }
    if (character === "/" && next === "*") {
      index += 2;
      while (index < text.length && !(text[index] === "*" && text[index + 1] === "/")) {
        output += text[index] === "\n" ? "\n" : " ";
        index++;
      }
      index += 2;
      continue;
    }
    if (character === '"' || character === "'") {
      const quote = character;
      const verbatim = 0 < index && text[index - 1] === "@";
      index++;
      while (index < text.length) {
        if (!verbatim && text[index] === "\\") {
          index += 2;
          continue;
        }
        if (text[index] === quote) {
          if (verbatim && text[index + 1] === quote) {
            index += 2;
            continue;
          }
          index++;
          break;
        }
        output += text[index] === "\n" ? "\n" : " ";
        index++;
      }
      continue;
    }
    output += character;
    index++;
  }
  return output;
}

/** Whether a path names a tree this package does not own. */
function isVendored(filePath) {
  const normalized = filePath.split(path.sep).join("/");
  return VENDORED_PREFIXES.some((prefix) => normalized.startsWith(prefix));
}

/**
 * @param {{path: string, text: string}[]} asmdefs assembly definitions to inspect
 * @param {{path: string, text: string}[]} sources C# sources to inspect
 * @param {{path: string, text: string}[]} projects check projects to inspect
 * @returns {string[]} one message per violation
 */
function findViolations(asmdefs, sources, projects) {
  const failures = [];

  for (const asmdef of asmdefs) {
    let parsed;
    try {
      parsed = JSON.parse(asmdef.text);
    } catch (error) {
      failures.push(`${asmdef.path}: is not valid JSON (${error.message})`);
      continue;
    }
    if (parsed.allowUnsafeCode === true) {
      failures.push(
        `${asmdef.path}: "allowUnsafeCode": true. A shipped assembly must not permit pointer ` +
          `code; ${ISSUE}.`
      );
    }
  }

  for (const source of sources) {
    if (isVendored(source.path)) {
      continue;
    }

    const lines = codeOnly(source.text).split("\n");
    for (let index = 0; index < lines.length; index++) {
      if (!/(^|[^\w])unsafe([^\w]|$)/.test(lines[index])) {
        continue;
      }
      failures.push(
        `${source.path}:${index + 1}: 'unsafe'. Pointer access is undefined behaviour on IL2CPP ` +
          `when a layout or index assumption is wrong; ${ISSUE}.`
      );
    }
  }

  for (const project of projects) {
    if (/<AllowUnsafeBlocks>\s*true\s*<\/AllowUnsafeBlocks>/i.test(project.text)) {
      failures.push(
        `${project.path}: <AllowUnsafeBlocks>true</AllowUnsafeBlocks>. A check project permitting ` +
          `what Unity refuses is a gate that agrees with nothing; ${ISSUE}.`
      );
    }
  }

  return failures;
}

/*
    Listed by directory and filtered here, never by a recursive-glob pathspec: git's double-star
    segment demands at least one directory level, so that shape silently skips every file sitting
    directly in the root -- `Runtime/WallstopStudios.UnityHelpers.asmdef` and
    `Runtime/AssemblyInfo.cs` among them. Measured: the first draft of this gate passed a tree
    whose runtime asmdef had allowUnsafeCode back on.
*/
function trackedWithExtension(repoRoot, roots, extension) {
  const present = roots.filter((root) => existsSync(path.join(repoRoot, root)));
  if (present.length === 0) {
    return [];
  }

  const result = spawnSync("git", ["-C", repoRoot, "ls-files", "-z", "--", ...present], {
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024
  });
  if (result.status !== 0) {
    console.error(`[unsafe-code] git ls-files failed: ${result.stderr}`);
    process.exit(2);
  }
  return result.stdout
    .split("\0")
    .filter((entry) => entry.length !== 0 && entry.endsWith(extension))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
}

function main() {
  const repoRoot = path.resolve(__dirname, "..");
  const verbose = process.argv.includes("--verbose");
  const failures = findViolations(
    trackedWithExtension(repoRoot, SOURCE_ROOTS, ".asmdef"),
    trackedWithExtension(repoRoot, SOURCE_ROOTS, ".cs"),
    trackedWithExtension(repoRoot, ["Generator~"], ".csproj")
  );

  if (0 < failures.length) {
    for (const failure of failures) {
      console.error(`[unsafe-code] ${failure}`);
    }
    console.error(`[unsafe-code] ${failures.length} violation(s).`);
    process.exit(1);
  }

  if (verbose) {
    console.log("[unsafe-code] No compiler-unsafe code or enabling switch found.");
  }
}

module.exports = { codeOnly, findViolations, isVendored };

if (require.main === module) {
  main();
}
