"use strict";

// Pins where the shipped Roslyn analyzer lives and how it is labelled.
//
// Unity scopes a folder-resident analyzer by the nearest enclosing assembly definition: under an
// .asmdef it applies to that assembly AND every assembly referencing it; under none it applies to
// the predefined assemblies only. The consequence that matters is not obvious — an analyzer under an
// EDITOR-ONLY asmdef reaches no consumer runtime code at all, because no runtime assembly may
// reference an editor assembly. A consumer's `[WProtoContract]` types would then get no formatter,
// and the error would surface as a missing nested type rather than as anything naming the analyzer.
//
// The same trap cost the sibling DxMessaging project a released version (their issue #229). This
// file exists so it cannot be reintroduced here silently.

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
// Every DLL the package ships as a compiler input. Two families, two assemblies: the WallstopProto
// source generator, and the general-purpose WUH### analyzers. Both are subject to every contract
// below, and a new one added to Runtime/Analyzers without being listed here would be governed by
// none of them.
const analyzerDlls = fs
  .readdirSync(path.join(root, "Runtime/Analyzers"))
  .filter((name) => name.endsWith(".dll"))
  .map((name) => path.join(root, "Runtime/Analyzers", name))
  .sort();
let passed = 0;

function check(description, condition, detail) {
  assert.ok(condition, `${description}${detail ? `: ${detail}` : ""}`);
  passed += 1;
}

/** Walks up from a file to the .asmdef that governs it, or null for the predefined assemblies. */
function governingAsmdef(filePath) {
  let directory = path.dirname(filePath);
  while (directory.startsWith(root)) {
    const asmdef = fs
      .readdirSync(directory)
      .find((entry) => entry.toLowerCase().endsWith(".asmdef"));
    if (asmdef) {
      return path.join(directory, asmdef);
    }

    const parent = path.dirname(directory);
    if (parent === directory) {
      return null;
    }

    directory = parent;
  }

  return null;
}

function isEditorOnly(asmdefPath) {
  const definition = JSON.parse(fs.readFileSync(asmdefPath, "utf8"));
  const platforms = definition.includePlatforms || [];
  return platforms.length === 1 && platforms[0] === "Editor";
}

check("at least one analyzer ships", analyzerDlls.length > 0, "Runtime/Analyzers is empty");

for (const analyzerDll of analyzerDlls) {
  const label = path.relative(root, analyzerDll);

  const metaPath = `${analyzerDll}.meta`;
  check(`${label} has a .meta sidecar`, fs.existsSync(metaPath), metaPath);

  const meta = fs.readFileSync(metaPath, "utf8");
  check(
    `${label} is labelled RoslynAnalyzer`,
    /labels:\s*\r?\n\s*-\s*RoslynAnalyzer\b/.test(meta),
    "without the label Unity loads it as a plain managed plugin, not a compiler input"
  );
  check(
    `${label} disables every platform`,
    /Any:\s*\r?\n\s*enabled:\s*0/.test(meta) && /Editor:\s*\r?\n\s*enabled:\s*0/.test(meta),
    "an analyzer enabled for a platform would be copied into player builds"
  );

  const asmdef = governingAsmdef(analyzerDll);
  check(`an .asmdef governs ${label}`, asmdef !== null, "found none");
  check(
    `the assembly governing ${label} is not editor-only`,
    !isEditorOnly(asmdef),
    `${path.relative(root, asmdef)} restricts includePlatforms to Editor, so no consumer runtime ` +
      "assembly can reference it and the analyzer would never run on consumer code"
  );

  // A second copy anywhere Unity imports would shadow the shipped one with an auto-generated .meta
  // that carries no label, and the analyzer would silently stop running.
  const strays = [];
  (function walk(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      if (entry.name === "node_modules" || entry.name.startsWith(".") || entry.name.endsWith("~")) {
        continue;
      }

      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        walk(full);
      } else if (entry.name === path.basename(analyzerDll) && full !== analyzerDll) {
        strays.push(path.relative(root, full));
      }
    }
  })(root);

  check(
    `no second copy of ${label} is anywhere Unity imports`,
    strays.length === 0,
    strays.join(", ")
  );

  // Each shipped analyzer needs a project that builds it, or the committed DLL has no sources and
  // the byte-comparison gate in CI has nothing to compare against.
  const projectName = path.basename(analyzerDll, ".dll");
  const projectFile = path.join(root, "Generator~", projectName, `${projectName}.csproj`);
  check(
    `${label} is built by a project in Generator~`,
    fs.existsSync(projectFile),
    `expected ${path.relative(root, projectFile)}`
  );
}

process.stdout.write(`Analyzer placement contract passed (${passed} checks).\n`);
