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
const os = require("node:os");
const { execFileSync } = require("node:child_process");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");
// Every DLL the package ships as a compiler input: WallstopProto, general-purpose WUH### analyzers,
// and Odin migration bindings. All are subject to every contract
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

const diagnosticsSource = fs.readFileSync(
  path.join(
    root,
    "Generator~",
    "WallstopStudios.UnityHelpers.Analyzers",
    "UnityHelpersDiagnostics.cs"
  ),
  "utf8"
);
const policyCatalogSource = fs.readFileSync(
  path.join(root, "Editor", "Tools", "AnalyzerPolicyAPI.cs"),
  "utf8"
);
const descriptorIds = [...diagnosticsSource.matchAll(/^\s+"(WUH\d{3})",\r?$/gm)].map(
  (match) => match[1]
);
const catalogIds = [...policyCatalogSource.matchAll(/new\(\s*"(WUH\d{3})",/g)].map(
  (match) => match[1]
);
check(
  "the analyzer-policy editor catalog matches every shipped WUH descriptor",
  JSON.stringify(catalogIds) === JSON.stringify(descriptorIds),
  `descriptors=${descriptorIds.join(",")} catalog=${catalogIds.join(",")}`
);

const checkRuleset = fs.readFileSync(
  path.join(root, "Generator~", "CheckProjects.ruleset"),
  "utf8"
);
const enabledOptInIds = [...checkRuleset.matchAll(/<Rule Id="(WUH\d{3})" Action="Warning" \/>/g)]
  .map((match) => match[1])
  .sort();
check(
  "shared opt-in WUH policies are enabled for package-owned check projects",
  JSON.stringify(enabledOptInIds) === JSON.stringify(["WUH010", "WUH013", "WUH018"]),
  enabledOptInIds.join(",")
);

const generatorBuildPolicy = fs.readFileSync(
  path.join(root, "Generator~", "Directory.Build.props"),
  "utf8"
);
for (const analyzerDll of analyzerDlls) {
  const analyzerFileName = path.basename(analyzerDll);
  check(
    `every owned .NET project loads ${analyzerFileName}`,
    generatorBuildPolicy.includes(`Runtime/Analyzers/${analyzerFileName}`),
    "Generator~/Directory.Build.props must load the shipped binary"
  );
}
const productionRuleset = fs.readFileSync(
  path.join(root, "Generator~", "ProductionCheckProjects.ruleset"),
  "utf8"
);
check(
  "the production policy imports shared policies and enables WUH019, WUH021 and WUH023",
  productionRuleset.includes('<Include Path="CheckProjects.ruleset" Action="Default" />') &&
    ["WUH019", "WUH021", "WUH023"].every((id) =>
      productionRuleset.includes(`<Rule Id="${id}" Action="Warning" />`)
    ),
  "WUH019, WUH021 and WUH023 are production-only opt-ins; tests and tooling retain the shared ruleset"
);
check(
  "shared projects explicitly disable WUH019, WUH021 and WUH023",
  ["WUH019", "WUH021", "WUH023"].every((id) =>
    checkRuleset.includes(`<Rule Id="${id}" Action="None" />`)
  ),
  "The shared ruleset retains the default-disabled WUH019, WUH021 and WUH023 policies"
);
for (const projectName of ["TypeCheck", "EditorCheck", "IntegrationCheck"]) {
  check(
    `production ${projectName} enables loop-bound caching`,
    generatorBuildPolicy.includes(
      `'$(MSBuildProjectName)' == 'WallstopStudios.UnityHelpers.${projectName}'`
    ),
    projectName
  );
}
const warningPromotions = [
  ...generatorBuildPolicy.matchAll(/<WarningsAsErrors([^>]*)>([^<]+)<\/WarningsAsErrors>/g)
];
check(
  "WUH019, WUH021 and WUH023 promotion is limited to the three production projects",
  warningPromotions.some(
    (match) =>
      match[1] === "" && ["WUH019", "WUH021", "WUH023"].every((id) => !match[2].includes(id))
  ) &&
    warningPromotions.some(
      (match) =>
        ["WUH019", "WUH021", "WUH023"].every((id) => match[2].includes(id)) &&
        ["TypeCheck", "EditorCheck", "IntegrationCheck"].every((name) =>
          match[1].includes(`WallstopStudios.UnityHelpers.${name}`)
        ) &&
        !match[1].includes("TestCheck") &&
        !match[1].includes("EditorTestCheck")
    ),
  "Explicit WarningsAsErrors IDs override ruleset None; only production may list WUH019, WUH021 or WUH023"
);
const generatorProjectFiles = fs
  .readdirSync(path.join(root, "Generator~"), { withFileTypes: true })
  .filter((entry) => entry.isDirectory())
  .flatMap((entry) =>
    fs
      .readdirSync(path.join(root, "Generator~", entry.name))
      .filter((fileName) => fileName.endsWith(".csproj"))
      .map((fileName) => path.join(root, "Generator~", entry.name, fileName))
  );
function evaluatedAnalyzers(projectFile, oracle) {
  const output = execFileSync(
    "dotnet",
    ["msbuild", projectFile, "--nologo", "-getItem:Analyzer", `-p:ProtobufNetOracle=${oracle}`],
    { cwd: root, encoding: "utf8", timeout: 30000, maxBuffer: 1024 * 1024 }
  );
  const items = JSON.parse(output).Items.Analyzer;
  assert.ok(Array.isArray(items), `MSBuild did not return analyzer items for ${projectFile}`);
  return items;
}

function validAnalyzerPlacement(items) {
  const identity = (value) => {
    const fullPath = path.resolve(value);
    return process.platform === "win32" ? fullPath.toLowerCase() : fullPath;
  };
  return analyzerDlls.every((analyzerDll) => {
    const matches = items.filter(
      (item) => path.basename(identity(item.FullPath)) === path.basename(identity(analyzerDll))
    );
    return matches.length === 1 && identity(matches[0].FullPath) === identity(analyzerDll);
  });
}

check("owned project discovery is nonempty", generatorProjectFiles.length > 0, "Generator~");
check(
  "compiler compatibility project remains in the placement check",
  generatorProjectFiles.some(
    (file) =>
      path.basename(file) === "WallstopStudios.UnityHelpers.Proto.CompilerFloor.Tests.csproj"
  )
);
for (const projectFile of generatorProjectFiles) {
  for (const oracle of ["v3", "v2"]) {
    check(
      `owned project inherits each shipped analyzer once: ${path.relative(root, projectFile)} (${oracle})`,
      validAnalyzerPlacement(evaluatedAnalyzers(projectFile, oracle)),
      "MSBuild must evaluate exactly one compiler input at each shipped path"
    );
  }
}

// Exercise real MSBuild item semantics: a normal Reference is data, while a
// duplicate Analyzer (including a property alias) or removed compiler input fails.
const fixtureRoot = fs.mkdtempSync(path.join(os.tmpdir(), "wuh analyzer placement-"));
try {
  const projectFile = path.join(fixtureRoot, "Fixture.proj");
  const attribute = (value) => value.replaceAll("&", "&amp;").replaceAll('"', "&quot;");
  const inherited = `<Import Project="${attribute(path.join(root, "Generator~/Directory.Build.props"))}" />`;
  const probe = (body, oracle = "v3") => {
    fs.writeFileSync(projectFile, `<Project>${body}</Project>`);
    return validAnalyzerPlacement(evaluatedAnalyzers(projectFile, oracle));
  };
  check(
    "ordinary assembly references do not duplicate compiler inputs",
    probe(
      inherited +
        analyzerDlls
          .map(
            (file) =>
              `<ItemGroup><Reference Include="Fixture"><HintPath>${attribute(file)}</HintPath></Reference></ItemGroup>`
          )
          .join("")
    )
  );
  for (const analyzerDll of analyzerDlls) {
    const file = attribute(analyzerDll);
    check(
      `duplicate analyzer is rejected: ${path.basename(analyzerDll)}`,
      !probe(`${inherited}<ItemGroup><Analyzer Include="${file}" /></ItemGroup>`)
    );
    check(
      `property-aliased duplicate is rejected: ${path.basename(analyzerDll)}`,
      !probe(
        `${inherited}<PropertyGroup><OwnedCompiler>${file}</OwnedCompiler></PropertyGroup><ItemGroup><Analyzer Include="$(OwnedCompiler)" /></ItemGroup>`
      )
    );
    check(
      `v2-only duplicate is rejected: ${path.basename(analyzerDll)}`,
      !probe(
        `${inherited}<ItemGroup Condition="'$(ProtobufNetOracle)' == 'v2'"><Analyzer Include="${file}" /></ItemGroup>`,
        "v2"
      )
    );
    check(
      `an analyzer from a different directory is rejected: ${path.basename(analyzerDll)}`,
      !probe(
        `${inherited}<ItemGroup><Analyzer Remove="${file}" /><Analyzer Include="${attribute(path.join(fixtureRoot, path.basename(analyzerDll)))}" /></ItemGroup>`
      )
    );
    check(
      `missing analyzer is rejected: ${path.basename(analyzerDll)}`,
      !probe(`${inherited}<ItemGroup><Analyzer Remove="${file}" /></ItemGroup>`)
    );
  }
  check("an empty compiler item set is rejected", !probe(""));
} finally {
  fs.rmSync(fixtureRoot, { recursive: true, force: true });
}
const promotedPolicyIds = [...generatorBuildPolicy.matchAll(/(?:^|;)(WUH\d{3})(?=;|<)/g)].map(
  (match) => match[1]
);
check(
  "shared WUH policies are promoted globally and WUH019, WUH021 and WUH023 only in production",
  JSON.stringify([...promotedPolicyIds].sort()) ===
    JSON.stringify(descriptorIds.filter((id) => id !== "WUH024").sort()),
  `descriptors=${descriptorIds.join(",")} promoted=${promotedPolicyIds.join(",")}`
);

check(
  "enum cache advice stays an explicit per-project opt-in",
  checkRuleset.includes('<Rule Id="WUH024" Action="None" />') &&
    !promotedPolicyIds.includes("WUH024"),
  "WUH024 can retain enum names; the package must not turn every naming call into a cache"
);

process.stdout.write(`Analyzer placement contract passed (${passed} checks).\n`);
