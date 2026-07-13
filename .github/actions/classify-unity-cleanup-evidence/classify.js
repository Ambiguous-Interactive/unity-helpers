"use strict";

const fs = require("node:fs");

const TERMINATION_EXIT_CODES = new Set([
  137, 143, -1073741510, -1073740791, 3221225786, 3221226505
]);
const ENTITLEMENT_MARKERS = new Set([
  "Successfully returned the entitlement license",
  "[Licensing::Module] Successfully returned the entitlement license"
]);
const ULF_RETURN =
  /^\[Licensing::Client\] Successfully returned ULF license with serial number\s*:\s*\S+$/;

function classifyCleanupEvidence({ commandCompleted, logText }) {
  if (!commandCompleted || typeof logText !== "string") {
    return false;
  }

  let entitlementReturned = false;
  let ulfReturned = false;
  let recordedExitCode = null;
  for (const rawLine of logText.split(/\r?\n/u)) {
    const line = rawLine.trim();
    const status = /^exit_return_rc=(-?\d+)$/u.exec(line);
    if (status) {
      recordedExitCode = Number.parseInt(status[1], 10);
    }
    if (ENTITLEMENT_MARKERS.has(line)) {
      entitlementReturned = true;
    }
    if (line === "Serial number unavailable for ULF return" || ULF_RETURN.test(line)) {
      ulfReturned = true;
    }
  }

  return (
    recordedExitCode !== null &&
    !TERMINATION_EXIT_CODES.has(recordedExitCode) &&
    entitlementReturned &&
    ulfReturned
  );
}

function appendOutputs(outputPath, outputs) {
  const body = Object.entries(outputs)
    .map(([name, value]) => `${name}=${value}`)
    .join("\n");
  fs.appendFileSync(outputPath, `${body}\n`, { encoding: "utf8" });
}

function main() {
  let logText = null;
  try {
    logText = fs.readFileSync(process.env.RETURN_LOG_PATH || "", "utf8");
  } catch {
    logText = null;
  }
  const confirmed = classifyCleanupEvidence({
    commandCompleted: process.env.RETURN_COMMAND_COMPLETED === "true",
    logText
  });
  appendOutputs(process.env.GITHUB_OUTPUT, {
    "resource-safe": confirmed ? "true" : "false",
    "resource-cleanup-status": confirmed ? "confirmed" : "unknown",
    "resource-health": "healthy",
    "resource-reason": confirmed ? "cleanup-confirmed" : "return-missing-positive-evidence"
  });
  if (confirmed) {
    process.stdout.write("Exact positive Unity cleanup evidence confirmed.\n");
  } else {
    process.stdout.write(
      "::warning::Exact positive Unity cleanup evidence was not confirmed; cleanup remains unknown.\n"
    );
  }
}

module.exports = { classifyCleanupEvidence, main };
