"use strict";

const fs = require("node:fs");
const path = require("node:path");

const TEST_MODES = Object.freeze(["editmode", "playmode", "standalone"]);

function resolveTestMatrix(
  versions,
  requestedVersion = "",
  requestedMode = "all",
  requestedAcceptance = "none"
) {
  if (
    !Array.isArray(versions) ||
    versions.length === 0 ||
    versions.some(
      (version) => typeof version !== "string" || version.trim() !== version || !version
    ) ||
    new Set(versions).size !== versions.length
  ) {
    throw new Error("Unity versions must be a nonempty list of distinct version strings.");
  }
  const version = requestedVersion.trim();
  const mode = requestedMode.trim();
  if (version && !versions.includes(version)) {
    throw new Error(`Unsupported Unity version: ${version}`);
  }
  if (mode && mode !== "all" && !TEST_MODES.includes(mode)) {
    throw new Error(`Unsupported Unity test mode: ${mode}`);
  }
  const acceptance = requestedAcceptance.trim();
  const acceptanceRequested = acceptance !== "" && acceptance !== "none";
  const selectedVersions = version ? [version] : [...versions];
  const selectedModes = !mode || mode === "all" ? [...TEST_MODES] : [mode];
  if (acceptanceRequested && !selectedModes.includes("standalone")) {
    // Native acceptance builds IL2CPP standalone players (intmap, serialization),
    // so the standalone leg -- the only leg whose editor gate provisions the
    // StandaloneWindowsIl2Cpp profile -- must exist whenever acceptance runs.
    selectedModes.push("standalone");
  }
  return {
    "unity-versions": selectedVersions,
    "test-modes": selectedModes,
    "matrix-exclude": versions
      .filter((entry) => !selectedVersions.includes(entry))
      .map((entry) => ({ "unity-version": entry }))
  };
}

function main() {
  const repoRoot = path.resolve(__dirname, "..", "..");
  const versions = JSON.parse(
    fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8")
  ).all;
  const matrix = resolveTestMatrix(
    versions,
    process.env.INPUT_UNITY_VERSION || "",
    process.env.INPUT_TEST_MODE || "all",
    process.env.INPUT_ACCEPTANCE || "none"
  );
  if (!process.env.GITHUB_OUTPUT) {
    throw new Error("GITHUB_OUTPUT is required to publish the Unity test matrix.");
  }
  const outputs = Object.entries(matrix).map(([key, value]) => `${key}=${JSON.stringify(value)}`);
  fs.appendFileSync(process.env.GITHUB_OUTPUT, `${outputs.join("\n")}\n`, "utf8");
}

if (require.main === module) {
  try {
    main();
  } catch (error) {
    console.error(`[resolve-test-matrix] ${error.message}`);
    process.exitCode = 1;
  }
}

module.exports = { TEST_MODES, resolveTestMatrix };
