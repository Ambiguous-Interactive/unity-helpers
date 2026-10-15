#!/usr/bin/env node

"use strict";

const { spawnSync } = require("node:child_process");
const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { CHECK_PROJECTS } = require("./typecheck-controls.js");

const NOTICE =
  "Iteration feedback only: compiler and source generators run; WUH analyzers are omitted. " +
  "Run the normal typecheck gates before publication.";
const VARIANTS = Object.freeze({
  default: [],
  legacy: ["-p:WallstopProto=false"],
  odin: ["-p:WallstopOdin=true"],
  singlethreaded: ["-p:WallstopSingleThreaded=true"],
  "proto-only": ["-p:WallstopProtoOnly=true"]
});

function compilerIdentity(projectId, variant, payloads) {
  const hash = crypto.createHash("sha256").update(projectId + "\n" + variant + "\n");
  for (const payload of payloads) {
    hash.update(crypto.createHash("sha256").update(payload).digest());
  }
  return "unity-helpers-iterate-" + hash.digest("hex");
}

function buildArguments(args) {
  const [projectId, ...remaining] = args;
  const serverIndex = remaining.indexOf("--server");
  const shared = serverIndex !== -1;
  if (shared) {
    remaining.splice(serverIndex, 1);
  }
  const variant = remaining.length && remaining[0] !== "--files" ? remaining.shift() : "default";
  const project = CHECK_PROJECTS.find((candidate) => candidate.id === projectId);
  if (
    !project ||
    !Object.hasOwn(VARIANTS, variant) ||
    (remaining.length &&
      (projectId !== "editor-tests" || remaining[0] !== "--files" || remaining.length < 2))
  ) {
    throw new Error(
      "Usage: npm run typecheck:iterate -- " +
        "<runtime|editor|tests|editor-tests|integrations> " +
        "[default|legacy|odin|singlethreaded|proto-only] " +
        "[--server] [--files Tests/Editor/Fixture.cs ...]"
    );
  }
  const sources = remaining.length
    ? remaining.slice(1).map((file) => {
        const root = path.resolve(__dirname, "..");
        const resolved = path.resolve(root, file);
        const relative = path.relative(root, resolved).split(path.sep).join("/");
        if (
          !relative.startsWith("Tests/Editor/") ||
          !relative.endsWith(".cs") ||
          /[%;,*?\[\]\r\n]/.test(file) ||
          !fs.existsSync(resolved) ||
          !fs.statSync(resolved).isFile() ||
          !fs
            .realpathSync(resolved)
            .startsWith(fs.realpathSync(path.join(root, "Tests/Editor")) + path.sep)
        ) {
          throw new Error(
            "Scoped iteration requires existing C# files under Tests/Editor: " + file
          );
        }
        return resolved;
      })
    : [];
  return [
    "build",
    project.project,
    "--no-restore",
    "--nologo",
    "-v",
    "quiet",
    "-p:RunAnalyzers=false",
    "-p:UseSharedCompilation=" + shared,
    ...VARIANTS[variant],
    "-p:OutputPath=bin/iteration/" + variant + "/",
    "-p:IntermediateOutputPath=obj/iteration/" + variant + "/",
    ...(sources.length
      ? ["-p:WallstopIterationTestSources=" + [...new Set(sources)].join("%3B")]
      : []),
    ...(shared
      ? [
          "-p:SharedCompilationId=" +
            compilerIdentity(
              projectId,
              variant,
              ["Proto.Generator", "Analyzers", "OdinMigration.Generator"].map((name) =>
                fs.readFileSync(
                  path.resolve(
                    __dirname,
                    "../Runtime/Analyzers/WallstopStudios.UnityHelpers." + name + ".dll"
                  )
                )
              )
            )
        ]
      : [])
  ];
}

function run(args, launch = spawnSync, report = console.error) {
  let build;
  try {
    build = buildArguments(args);
  } catch (error) {
    report(error.message);
    return 2;
  }
  report(NOTICE);
  if (build.includes("-p:UseSharedCompilation=true")) {
    report(
      "Compiler server identity includes all shipped analyzer payloads and the selected project and variant."
    );
  }
  if (build.some((argument) => argument.startsWith("-p:WallstopIterationTestSources="))) {
    report(
      "Scoped editor compilation: only the named editor fixtures are included. Production and shared test sources remain included."
    );
  }
  const result = launch("dotnet", build, {
    cwd: path.resolve(__dirname, ".."),
    stdio: "inherit",
    shell: false
  });
  if (result.error) {
    report(result.error.message);
  }
  return Number.isInteger(result.status) ? result.status : 1;
}

module.exports = { NOTICE, VARIANTS, compilerIdentity, buildArguments, run };
if (require.main === module) {
  process.exitCode = run(process.argv.slice(2));
}
