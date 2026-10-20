// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

"use strict";

function getAttachSelectionError(environment) {
  if (
    environment == null ||
    environment.UH_NATIVE_DIAGNOSTICS !== "true" ||
    environment.UH_NATIVE_MODE !== "standalone" ||
    environment.UH_NATIVE_ACCEPTANCE !== "none" ||
    environment.UH_NATIVE_DIRECT_CONTROL !== "false" ||
    environment.UH_NATIVE_STANDARD_HEAP !== "false"
  ) {
    return "Direct attachment requires native Standalone diagnostics, acceptance=none, Direct disabled and standard heap disabled.";
  }
  return null;
}

module.exports = { getAttachSelectionError };

function getStartupAttachSelectionError(environment) {
  if (
    getAttachSelectionError(environment) !== null ||
    environment.UH_NATIVE_ATTACH_CONTROL !== "true" ||
    environment.UH_NATIVE_ATTACH_AFTER_STARTUP !== "true" ||
    (environment.UH_NATIVE_ATTACH_AFTER_SHUTDOWN !== undefined &&
      environment.UH_NATIVE_ATTACH_AFTER_SHUTDOWN !== "false")
  ) {
    return "Startup-delayed attachment requires the admitted Direct attachment arm.";
  }
  return null;
}

module.exports.getStartupAttachSelectionError = getStartupAttachSelectionError;

function getShutdownAttachSelectionError(environment) {
  if (
    getAttachSelectionError(environment) !== null ||
    environment.UH_NATIVE_ATTACH_CONTROL !== "true" ||
    environment.UH_NATIVE_ATTACH_AFTER_STARTUP !== "false" ||
    environment.UH_NATIVE_ATTACH_AFTER_SHUTDOWN !== "true"
  ) {
    return "Shutdown-delayed attachment requires the admitted Direct attachment arm without startup selection.";
  }
  return null;
}

module.exports.getShutdownAttachSelectionError = getShutdownAttachSelectionError;

if (require.main === module) {
  const boundary = process.env.UH_NATIVE_ATTACH_AFTER_STARTUP;
  const shutdown = process.env.UH_NATIVE_ATTACH_AFTER_SHUTDOWN;
  const error =
    shutdown === "true"
      ? getShutdownAttachSelectionError(process.env)
      : shutdown !== undefined && shutdown !== "false"
        ? "Unknown attachment shutdown selection."
        : boundary === "true"
          ? getStartupAttachSelectionError(process.env)
          : boundary !== undefined && boundary !== "false"
            ? "Unknown attachment startup selection."
            : getAttachSelectionError(process.env);
  if (error !== null) {
    console.error(error);
    process.exitCode = 1;
  }
}
