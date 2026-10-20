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

if (require.main === module) {
  const error = getAttachSelectionError(process.env);
  if (error !== null) {
    console.error(error);
    process.exitCode = 1;
  }
}
