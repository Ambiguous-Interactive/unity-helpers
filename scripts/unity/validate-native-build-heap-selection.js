// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

"use strict";

function getStandardHeapSelectionError(environment) {
  if (
    environment == null ||
    environment.UH_NATIVE_DIAGNOSTICS !== "true" ||
    environment.UH_NATIVE_MODE !== "standalone" ||
    environment.UH_NATIVE_ACCEPTANCE !== "none" ||
    environment.UH_NATIVE_DIRECT_CONTROL !== "false"
  ) {
    return "Standard heap control requires native diagnostics, Standalone, acceptance=none and the Debugger arm.";
  }
  return null;
}

module.exports = { getStandardHeapSelectionError };

if (require.main === module) {
  const error = getStandardHeapSelectionError(process.env);
  if (error !== null) {
    console.error(error);
    process.exitCode = 1;
  }
}
