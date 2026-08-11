"use strict";

// Two properties every [WProtoContract] in this package must have, checked without a compiler.
//
// Both were broken in one session by scripts that annotated contracts in bulk, and neither is
// visible to anything that runs locally: the Unity compile gate can wedge for a whole session, and
// a missing `using` is a SEMANTIC error, so CSharpier parses the file happily. CI was the first
// thing to notice each of them, at roughly an hour per discovery.
//
// The checks are deliberately NOT regex-shaped like the edits that caused the failures. Each one
// asserts the property that has to hold, so a pattern that missed a declaration shape --
// `internal sealed class` rather than `internal class`, say -- is caught rather than repeated.

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
const namespaceImport = "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;";
const declaringNamespace =
  "namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

// Comments are stripped before anything is matched. A file that only MENTIONS `[WProtoContract]`
// in prose -- Serializer's XML docs describe when the facade serves a type -- needs no import and
// declares no contract, and demanding one there would add an unused using directive. Only
// whole-line comments and block comments go: a `//` mid-line can sit inside a string literal
// (every file's license header carries a URL), and truncating there would delete real code.
function stripComments(text) {
  const withoutBlocks = text.replace(/\/\*[\s\S]*?\*\//g, "");
  return withoutBlocks
    .split("\n")
    .filter((line) => !line.trim().startsWith("//"))
    .join("\n");
}

function sourceFiles(directory) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      found.push(...sourceFiles(full));
    } else if (entry.name.endsWith(".cs")) {
      found.push(full);
    }
  }
  return found;
}

const files = sourceFiles(path.join(root, "Runtime"));
const notPartial = [];
const missingImport = [];

for (const file of files) {
  const text = stripComments(fs.readFileSync(file, "utf8"));
  if (!/\[(?:assembly:\s*)?WProto/.test(text)) {
    continue;
  }

  // The attributes have to resolve. A file either imports the namespace or is declared inside it.
  if (!text.includes(namespaceImport) && !text.includes(declaringNamespace)) {
    missingImport.push(path.relative(root, file));
  }

  // The formatter is emitted as a nested type, so every contract must be partial. Walk forward
  // from the attribute past further attributes and comments to the declaration it applies to,
  // rather than assuming what that line looks like.
  const lines = text.split("\n");
  let pending = null;
  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();
    if (line.startsWith("[WProtoContract")) {
      pending = index;
      continue;
    }
    if (pending === null) {
      continue;
    }
    if (line === "" || line.startsWith("[") || line.startsWith("//")) {
      continue;
    }
    if (/\b(class|struct|record)\b/.test(line) && !/\bpartial\b/.test(line)) {
      notPartial.push(`${path.relative(root, file)}:${index + 1} ${line.slice(0, 70)}`);
    }
    pending = null;
  }
}

assert.deepEqual(
  missingImport,
  [],
  `Files use WProto attributes without importing the namespace (CS0246 at compile time):\n  ${missingImport.join("\n  ")}`
);

assert.deepEqual(
  notPartial,
  [],
  `[WProtoContract] types that are not partial (WPROTO001 at compile time):\n  ${notPartial.join("\n  ")}`
);

const annotated = files.filter((file) =>
  /\[WProtoContract/.test(stripComments(fs.readFileSync(file, "utf8")))
).length;

console.log(
  `WallstopProto annotation contract passed (${annotated} annotated files, 2 properties).`
);
