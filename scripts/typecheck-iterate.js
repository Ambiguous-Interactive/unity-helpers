#!/usr/bin/env node

"use strict";

const { spawnSync } = require("node:child_process");
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

function buildArguments(args) {
  const [projectId, variant = "default"] = args;
  const project = CHECK_PROJECTS.find((candidate) => candidate.id === projectId);
  if (!project || args.length > 2 || !Object.hasOwn(VARIANTS, variant)) {
    throw new Error(
      "Usage: npm run typecheck:iterate -- " +
        "<runtime|editor|tests|editor-tests|integrations> " +
        "[default|legacy|odin|singlethreaded|proto-only]"
    );
  }
  return [
    "build",
    project.project,
    "--no-restore",
    "--nologo",
    "-v",
    "quiet",
    "-p:RunAnalyzers=false",
    "-p:UseSharedCompilation=false",
    ...VARIANTS[variant]
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

module.exports = { NOTICE, VARIANTS, buildArguments, run };
if (require.main === module) {
  process.exitCode = run(process.argv.slice(2));
}
