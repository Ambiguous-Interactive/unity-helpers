#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
"use strict";

const fs = require("fs");
const path = require("path");
const repoRoot = path.resolve(__dirname, "..");
const scanRoots = ["Runtime", "Editor", "Generator~", "Samples~", "Styles"];
const skipped = new Set(["bin", "obj", ".git", "node_modules"]);
const hostProjects = new Set([
  "ProtobufNetV2Oracle",
  "WallstopStudios.UnityHelpers.CountingLoopAudit",
  "WallstopStudios.UnityHelpers.DocSamplesCheck",
  "WallstopStudios.UnityHelpers.EditorCheck",
  "WallstopStudios.UnityHelpers.EditorTestCheck",
  "WallstopStudios.UnityHelpers.IntegrationCheck",
  "WallstopStudios.UnityHelpers.RandomQuality",
  "WallstopStudios.UnityHelpers.SyntaxPolicy",
  "WallstopStudios.UnityHelpers.TestCheck",
  "WallstopStudios.UnityHelpers.TypeCheck"
]);

function isProductionPath(relative) {
  const parts = relative.replace(/\\/g, "/").split("/");
  if (!scanRoots.includes(parts[0])) return false;
  // Only generator project boundaries may exclude test/host projects. A production
  // subdirectory named Tests must not bypass the policy.
  return parts[0] !== "Generator~" || !(parts[1]?.endsWith(".Tests") || hostProjects.has(parts[1]));
}

// This naming/marker gate is not a C# parser or semantic proof. Interpolation
// expressions inside strings are masked; ordinary member declarations remain visible.
// Keep comments/strings separate: ordinary messages can describe tests without defining hooks.
// Generator literal contents are checked separately because they become shipped source.
function tokenize(source) {
  let code = "";
  const literals = [];
  const literalRanges = [];
  const comments = [];
  for (let i = 0; i < source.length;) {
    const start = i;
    if (source.startsWith("//", i) || source.startsWith("/*", i)) {
      const block = source.startsWith("/*", i);
      const end = block ? source.indexOf("*/", i + 2) : source.slice(i).search(/[\r\n]/);
      i = end < 0 ? source.length : block ? end + 2 : i + end;
      comments.push(source.slice(start, i));
    } else if (source[i] === '"' || source[i] === "'") {
      const quote = source[i++];
      const verbatim = quote === '"' && source[start - 1] === "@";
      const rawCount = quote === '"' ? source.slice(start).match(/^"+/)[0].length : 0;
      let value = "";
      if (rawCount >= 3) {
        const delimiter = '"'.repeat(rawCount);
        const end = source.indexOf(delimiter, start + rawCount);
        i = end < 0 ? source.length : end + rawCount;
        value = source.slice(start + rawCount, end < 0 ? source.length : end);
      } else {
        while (i < source.length) {
          if (source[i] === quote) {
            if (verbatim && source[i + 1] === quote) {
              value += quote;
              i += 2;
              continue;
            }
            i++;
            break;
          }
          if (!verbatim && source[i] === "\\") {
            const escape = source
              .slice(i)
              .match(/^\\(?:u[0-9a-fA-F]{4}|U[0-9a-fA-F]{8}|x[0-9a-fA-F]{1,4}|.)/);
            if (escape) {
              const text = escape[0];
              value += /^\\[uUx]/.test(text)
                ? String.fromCodePoint(parseInt(text.slice(2), 16))
                : ({ n: "\n", r: "\r", t: "\t" }[text[1]] ?? text[1]);
              i += text.length;
              continue;
            }
          }
          value += source[i++];
        }
      }
      if (quote === '"') {
        literals.push(value);
        literalRanges.push({ start, end: i });
      }
    } else {
      code += source[i++];
      continue;
    }
    code += source.slice(start, i).replace(/[^\r\n]/g, " ");
  }
  return { code, literals, literalRanges, comments };
}

function forbiddenIdentifiers(code) {
  const decoded = code.replace(/\\(?:u([0-9a-fA-F]{4})|U([0-9a-fA-F]{8}))/g, (_, short, long) =>
    String.fromCodePoint(parseInt(short || long, 16))
  );
  return [...decoded.matchAll(/\b[A-Za-z_][A-Za-z0-9_]*\b/g)]
    .filter(([name]) => /ForTest|TestHook|TestOnly/i.test(name))
    .map(([name]) => name);
}

function analyzeFile(source, generated = false) {
  const { code, literals, literalRanges, comments } = tokenize(source);
  const violations = forbiddenIdentifiers(code).map(
    (name) => `forbidden production identifier ${name}`
  );
  for (const comment of comments) {
    if (
      /\btest[- ]only\s+(?:hook|callback|seam|API|member|method|field|instrumentation|code|override|latch)\b|\bexists for tests\b/i.test(
        comment
      )
    ) {
      violations.push("explicit test-only production marker");
    }
  }
  if (generated) {
    const emitted = [];
    for (let i = 0; i < literals.length; i++) {
      const previous = literalRanges[i - 1];
      if (previous && source.slice(previous.end, literalRanges[i].start).trim() === "+") {
        emitted[emitted.length - 1] += literals[i];
      } else emitted.push(literals[i]);
    }
    for (const literal of emitted) {
      violations.push(
        ...forbiddenIdentifiers(tokenize(literal).code).map(
          (name) => `forbidden emitted identifier ${name}`
        )
      );
      if (/^\s*#if\s+UNITY_INCLUDE_TESTS\s*$/m.test(literal))
        violations.push("emitted test-only conditional code");
    }
  }
  return [...new Set(violations)];
}

function main(root = repoRoot) {
  const files = [];
  function visit(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (skipped.has(entry.name)) continue;
      const absolute = path.join(directory, entry.name);
      const relative = path.relative(root, absolute).replace(/\\/g, "/");
      if (entry.isDirectory()) {
        if (isProductionPath(relative)) visit(absolute);
      } else if (entry.isFile() && entry.name.endsWith(".cs") && isProductionPath(relative))
        files.push({ absolute, relative });
    }
  }
  for (const relative of scanRoots) {
    const directory = path.join(root, relative);
    if (!fs.existsSync(directory) || !fs.statSync(directory).isDirectory()) {
      console.error(
        `[production-test-hooks] missing production root directory ${relative}; refusing partial acceptance.`
      );
      return 1;
    }
    visit(directory);
  }
  if (!files.length) {
    console.error(
      "[production-test-hooks] no production C# files found; refusing empty acceptance."
    );
    return 1;
  }
  let failed = 0;
  for (const { absolute, relative } of files) {
    for (const finding of analyzeFile(
      fs.readFileSync(absolute, "utf8"),
      relative.startsWith("Generator~/")
    )) {
      console.error(`[production-test-hooks] ${relative}: ${finding}`);
      failed++;
    }
  }
  console.log(
    `[production-test-hooks] ${files.length} production files scanned, ${failed} findings.`
  );
  return failed ? 1 : 0;
}
module.exports = { analyzeFile, isProductionPath, tokenize, main };
if (require.main === module)
  process.exitCode = main(process.env.PRODUCTION_TEST_HOOKS_ROOT || repoRoot);
