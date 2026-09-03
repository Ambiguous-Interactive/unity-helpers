#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# retry-license.sh
#
# Retries Unity compilation after the user has obtained a .ulf license file
# via the serial-based manual activation workflow.
#
# Prerequisites:
#   - .unity-secrets/license.ulf must exist
#   - If .alf has not been generated yet, run: npm run unity:generate-activation
#   - Unity Personal is not supported by this manual activation flow
###############################################################################

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SECRETS_DIR="${WORKSPACE_DIR}/.unity-secrets"
UNITY_TEST_PROJECT_DIR="${UNITY_TEST_PROJECT_DIR:-/home/vscode/.unity-test-project}"

# shellcheck source=scripts/unity/lib/license-cache-dir.sh
source "${SCRIPT_DIR}/lib/license-cache-dir.sh"

UNITY_LICENSE_CACHE_DIR="$(resolve_unity_license_cache_dir \
    "${UNITY_TEST_PROJECT_DIR}" "${WORKSPACE_DIR}" "${UNITY_LICENSE_CACHE_DIR:-}")"

if [[ ! -f "${SECRETS_DIR}/license.ulf" ]]; then
    echo "ERROR: [retry-license] .unity-secrets/license.ulf not found."
    if [[ -f "${SECRETS_DIR}/manual-activation.alf" ]]; then
        echo "ERROR: An activation file (.alf) exists at .unity-secrets/manual-activation.alf."
        echo "ERROR: To complete serial-based manual activation:"
        echo "ERROR:   1. Upload .unity-secrets/manual-activation.alf to https://license.unity3d.com/manual"
        echo "ERROR:   2. Enter your Unity serial in the manual activation flow"
        echo "ERROR:   3. Download the .ulf file and save it as: .unity-secrets/license.ulf"
        echo "ERROR:   4. Re-run: npm run unity:retry-license"
        echo "ERROR: Unity Personal cannot use this workflow."
    else
        echo "ERROR: No .alf file found either. If you have a serial-based paid license, run:"
        echo "ERROR:   1. npm run unity:generate-activation"
        echo "ERROR: Unity Personal cannot use this workflow."
    fi
    exit 1
fi

echo "==> [retry-license] Found .unity-secrets/license.ulf — clearing stale cache artifacts..."

# Clear stale license artifacts from the cache (not activation logs)
for artifact in \
    "${UNITY_LICENSE_CACHE_DIR}/local-share-unity3d/Unity/Unity_lic.ulf" \
    "${UNITY_LICENSE_CACHE_DIR}/config-unity3d/Unity/Unity_lic.ulf" \
    "${UNITY_LICENSE_CACHE_DIR}/local-share-unity3d/Unity/UnityEntitlementLicense.xml" \
    "${UNITY_LICENSE_CACHE_DIR}/config-unity3d/Unity/UnityEntitlementLicense.xml"
do
    if [[ -f "${artifact}" ]]; then
        rm -f "${artifact}"
        echo "==> [retry-license] Removed: ${artifact}"
    fi
done

# Belt-and-suspenders: run-unity-docker.sh also auto-loads this, but explicit export ensures consistency
UNITY_LICENSE="$(cat "${SECRETS_DIR}/license.ulf")"
export UNITY_LICENSE

echo "==> [retry-license] Retrying compile with new license..."

COMPILE_EXIT=0
bash "${WORKSPACE_DIR}/scripts/unity/compile.sh" || COMPILE_EXIT=$?

exit "${COMPILE_EXIT}"
