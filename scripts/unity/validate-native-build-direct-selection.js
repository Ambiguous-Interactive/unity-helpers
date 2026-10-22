// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

"use strict";

function getDirectBuildSelectionError(environment) {
  if (
    environment == null ||
    environment.UH_NATIVE_DIAGNOSTICS !== "true" ||
    environment.UH_NATIVE_MODE !== "standalone" ||
    environment.UH_NATIVE_ACCEPTANCE !== "none"
  ) {
    return "Direct Standalone build control requires native-diagnostics=true, test-mode=standalone and acceptance=none.";
  }
  return null;
}

module.exports = { getDirectBuildSelectionError };

if (require.main === module) {
  const error = getDirectBuildSelectionError(process.env);
  if (error !== null) {
    console.error(error);
    process.exitCode = 1;
  }
}
