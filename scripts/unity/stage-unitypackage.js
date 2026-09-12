#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const REQUIRED_ENTRIES = [
  "package.json",
  "package.json.meta",
  "README.md",
  "README.md.meta",
  "LICENSE",
  "LICENSE.meta",
  "CHANGELOG.md",
  "CHANGELOG.md.meta",
  "Runtime",
  "Runtime.meta",
  "Editor",
  "Editor.meta",
  "Samples~",
  "Shaders",
  "Shaders.meta",
  "Styles",
  "Styles.meta",
  "URP",
  "URP.meta",
  "link.xml",
  "link.xml.meta"
];
const EXPORTER_SOURCE = `using System;
using System.IO;
using UnityEditor;
using UnityEditor.AssetPackage;

public static class UnityHelpersPackageExporter
{
    public static void Export()
    {
        string outputPath = GetArgument("-exportOutput");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Missing -exportOutput argument.");
        }

        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            outputDirectory = Directory.GetCurrentDirectory();
        }

        Directory.CreateDirectory(outputDirectory);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Package.Export(new ExportPackageParameters(
            "Assets/WallstopStudios/UnityHelpers",
            outputPath,
            flags: ExportPackageOptions.Recurse
        ));

        FileInfo exported = new FileInfo(outputPath);
        if (!exported.Exists || exported.Length <= 0)
        {
            throw new InvalidOperationException("Unity package export did not produce a non-empty file: " + outputPath);
        }
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }
}
`;

function canonicalPath(value) {
  const resolved = path.resolve(value);
  if (fs.existsSync(resolved)) {
    return fs.realpathSync(resolved);
  }
  const parent = path.dirname(resolved);
  return path.join(canonicalPath(parent), path.basename(resolved));
}

function isWithin(parent, candidate) {
  const relative = path.relative(parent, candidate);
  return (
    relative !== "" &&
    relative !== ".." &&
    !relative.startsWith(`..${path.sep}`) &&
    !path.isAbsolute(relative)
  );
}

function resolveProjectPath(repoRoot, requestedPath, temporaryRoot = os.tmpdir()) {
  const repository = canonicalPath(repoRoot);
  const artifacts = canonicalPath(path.join(repository, ".artifacts"));
  const temporary = canonicalPath(temporaryRoot);
  const project = canonicalPath(requestedPath);
  const lexicalArtifacts = path.join(repository, ".artifacts");
  const artifactsAreScratch =
    path.relative(lexicalArtifacts, artifacts) === "" ||
    (path.relative(repository, artifacts) !== "" &&
      !isWithin(repository, artifacts) &&
      !isWithin(artifacts, repository));
  const overlapsCheckout =
    path.relative(project, repository) === "" || isWithin(project, repository);
  if (
    overlapsCheckout ||
    path.relative(project, artifacts) === "" ||
    !(
      (artifactsAreScratch && isWithin(artifacts, project)) ||
      (isWithin(temporary, project) && !isWithin(repository, project))
    )
  ) {
    throw new Error(
      `Refusing to create the export project unless it is a subdirectory under ${artifacts}, or under ${temporary} and outside ${repository}: ${project}`
    );
  }
  return project;
}

function run(command, args, cwd) {
  const result = spawnSync(command, args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 16 * 1024 * 1024,
    windowsHide: true
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`${path.basename(command)} failed (${result.status}): ${result.stderr}`);
  }
  return result.stdout;
}

function npmCommand() {
  if (process.platform !== "win32") {
    return { command: "npm", args: [] };
  }
  const roots = [path.dirname(process.execPath), ...(process.env.PATH || "").split(path.delimiter)];
  for (const root of roots) {
    if (!root) {
      continue;
    }
    const cli = path.join(root, "node_modules", "npm", "bin", "npm-cli.js");
    if (fs.existsSync(cli)) {
      return { command: process.execPath, args: [cli] };
    }
  }
  throw new Error("Cannot locate npm-cli.js beside Node or on PATH.");
}

function stageUnityPackage({ repoRoot, projectPath, unityVersion }) {
  const metadata = JSON.parse(fs.readFileSync(path.join(repoRoot, "package.json"), "utf8"));
  if (
    ![metadata.name, metadata.version].every((value) => typeof value === "string" && value.trim())
  ) {
    throw new Error(
      `${path.join(repoRoot, "package.json")} must define non-empty string name and version fields.`
    );
  }
  if (!/^\d+\.\d+\.\d+f\d+$/.test(unityVersion)) {
    throw new Error("Invalid release Unity version.");
  }
  const project = resolveProjectPath(repoRoot, projectPath);
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "unitypackage-pack-"));
  try {
    const npm = npmCommand();
    const pack = JSON.parse(
      run(npm.command, [...npm.args, "pack", "--json", "--pack-destination", scratch], repoRoot)
    );
    if (
      pack.length !== 1 ||
      !pack[0].filename ||
      path.basename(pack[0].filename) !== pack[0].filename
    ) {
      throw new Error("npm pack did not produce exactly one package tarball.");
    }
    run("tar", ["-x", "-z", "-f", pack[0].filename], scratch);
    const packedRoot = path.join(scratch, "package");
    for (const entry of REQUIRED_ENTRIES) {
      if (!fs.existsSync(path.join(packedRoot, entry))) {
        throw new Error(`Packed npm package is missing required Unity export entry: ${entry}`);
      }
    }
    const modules = JSON.parse(
      fs.readFileSync(path.join(repoRoot, ".github", "unity-test-project-modules.json"), "utf8")
    ).modules;
    fs.rmSync(project, { recursive: true, force: true });
    const stagedRoot = path.join(project, "Assets", "WallstopStudios", "UnityHelpers");
    for (const directory of [
      stagedRoot,
      path.join(project, "Assets", "Editor"),
      path.join(project, "Packages"),
      path.join(project, "ProjectSettings")
    ]) {
      fs.mkdirSync(directory, { recursive: true });
    }
    for (const entry of [...REQUIRED_ENTRIES, "docs", "docs.meta"]) {
      const source = path.join(packedRoot, entry);
      if (fs.existsSync(source)) {
        fs.cpSync(source, path.join(stagedRoot, entry === "Samples~" ? "Samples" : entry), {
          recursive: true,
          preserveTimestamps: true
        });
      }
    }
    fs.writeFileSync(
      path.join(project, "ProjectSettings", "ProjectVersion.txt"),
      `m_EditorVersion: ${unityVersion}\n`
    );
    fs.writeFileSync(
      path.join(project, "ProjectSettings", "ProjectSettings.asset"),
      "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  productName: UnityHelpers-PackageExport\n  companyName: WallstopStudios\n  runInBackground: 1\n"
    );
    fs.writeFileSync(
      path.join(project, "ProjectSettings", "EditorSettings.asset"),
      "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!159 &1\nEditorSettings:\n  m_DefaultBehaviorMode: 1\n"
    );
    fs.writeFileSync(
      path.join(project, "Packages", "manifest.json"),
      JSON.stringify(
        { dependencies: { "com.unity.test-framework": "1.1.33", ...modules } },
        null,
        2
      ) + "\n"
    );
    fs.writeFileSync(
      path.join(project, "Assets", "Editor", "UnityHelpersPackageExporter.cs"),
      EXPORTER_SOURCE
    );
    console.log(
      `Unity package ${metadata.name}@${metadata.version} staged for ${unityVersion}: ${project}`
    );
    return project;
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
}

function main(args) {
  const repoRoot = path.resolve(__dirname, "..", "..");
  let projectPath = path.join(repoRoot, ".artifacts", "unity", "unitypackage-project");
  for (let index = 0; index < args.length; index += 2) {
    if (args[index] !== "--project-dir" || !args[index + 1]) {
      throw new Error(`Unknown or incomplete argument: ${args[index]}`);
    }
    projectPath = path.resolve(args[index + 1]);
  }
  const unityVersion =
    process.env.UNITY_VERSION ||
    JSON.parse(fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8"))
      .release;
  stageUnityPackage({ repoRoot, projectPath, unityVersion });
}

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}

module.exports = { REQUIRED_ENTRIES, EXPORTER_SOURCE, resolveProjectPath, stageUnityPackage };
