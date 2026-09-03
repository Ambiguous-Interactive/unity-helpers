#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# generate-activation.sh
#
# Generates a Unity manual activation file (.alf) for this machine.
# This workflow is only valid for serial-based Unity licenses.
#
# After running, follow the printed instructions to complete licensing.
#
# Environment variables (same as run-unity-docker.sh):
#   UNITY_VERSION          - Unity Editor version (default: 2021.3.45f1)
#   UNITY_IMAGE_VERSION    - GameCI image version (default: 3)
#   UNITY_SERIAL           - Required serial key for manual activation workflows
#   UNITY_EMAIL            - Unity account email (optional, only used for log context)
#   UNITY_PASSWORD         - Unity account password (optional, not required for .alf generation)
#   UNITY_TEST_PROJECT_DIR - Path to test project (default: /home/vscode/.unity-test-project)
###############################################################################

UNITY_VERSION="${UNITY_VERSION:-2021.3.45f1}"
UNITY_IMAGE_VERSION="${UNITY_IMAGE_VERSION:-3}"
UNITY_TEST_PROJECT_DIR="${UNITY_TEST_PROJECT_DIR:-/home/vscode/.unity-test-project}"
UNITY_TIMEOUT="${UNITY_TIMEOUT:-1800}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

# shellcheck source=scripts/unity/lib/license-cache-dir.sh
source "${SCRIPT_DIR}/lib/license-cache-dir.sh"

UNITY_LICENSE_CACHE_DIR="$(resolve_unity_license_cache_dir \
    "${UNITY_TEST_PROJECT_DIR}" "${WORKSPACE_DIR}" "${UNITY_LICENSE_CACHE_DIR:-}")"
UNITY_LICENSE_CACHE_LOCAL_DIR="${UNITY_LICENSE_CACHE_DIR}/local-share-unity3d"
UNITY_LICENSE_CACHE_CONFIG_DIR="${UNITY_LICENSE_CACHE_DIR}/config-unity3d"
SECRETS_DIR="${WORKSPACE_DIR}/.unity-secrets"

UNITY_IMAGE="unityci/editor:ubuntu-${UNITY_VERSION}-base-${UNITY_IMAGE_VERSION}"

# ── Auto-load credentials from .unity-secrets/ ──────────────────────────────
if [[ -f "${SECRETS_DIR}/credentials.env" ]]; then
    while IFS='=' read -r key value || [[ -n "${key}" ]]; do
        [[ -z "${key}" || "${key}" =~ ^[[:space:]]*# ]] && continue
        key="$(printf '%s' "${key}" | tr -d '[:space:]')"
        value="$(printf '%s' "${value}" | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
        case "${key}" in
            UNITY_SERIAL)
                if [[ -z "${UNITY_SERIAL:-}" ]]; then
                    UNITY_SERIAL="${value}"
                    export UNITY_SERIAL
                fi
                ;;
            UNITY_EMAIL)
                if [[ -z "${UNITY_EMAIL:-}" ]]; then
                    UNITY_EMAIL="${value}"
                    export UNITY_EMAIL
                fi
                ;;
            UNITY_PASSWORD)
                if [[ -z "${UNITY_PASSWORD:-}" ]]; then
                    UNITY_PASSWORD="${value}"
                    export UNITY_PASSWORD
                fi
                ;;
        esac
    done < "${SECRETS_DIR}/credentials.env"
fi

if [[ -z "${UNITY_SERIAL:-}" ]]; then
    echo "ERROR: [generate-activation] Manual activation requires UNITY_SERIAL."
    echo "ERROR: Unity Personal does not support manual activation via .alf/.ulf upload."
    echo "ERROR: Set UNITY_SERIAL (or add it to .unity-secrets/credentials.env) and retry,"
    echo "ERROR: or use Unity Hub for a Personal license."
    exit 1
fi

echo "==> [generate-activation] Unity image: ${UNITY_IMAGE}"
echo "==> [generate-activation] Workspace: ${WORKSPACE_DIR}"
echo "==> [generate-activation] Test project: ${UNITY_TEST_PROJECT_DIR}"
echo "==> [generate-activation] Unity cache: ${UNITY_LICENSE_CACHE_DIR}"
echo "==> [generate-activation] Generating .alf for a serial-based manual activation workflow."

mkdir -p "${UNITY_TEST_PROJECT_DIR}"
mkdir -p "${UNITY_LICENSE_CACHE_LOCAL_DIR}" "${UNITY_LICENSE_CACHE_CONFIG_DIR}"
rm -f "${UNITY_LICENSE_CACHE_CONFIG_DIR}/.alf-generated-this-run"

echo "==> [generate-activation] Starting Docker container to generate .alf..."

INNER_SCRIPT='# Inner script runs inside Docker container
set -euo pipefail

mkdir -p /root/.local/share/unity3d /root/.config/unity3d 2>/dev/null || true

echo "==> Generating manual activation file (.alf)..."
unity-editor -batchmode -nographics -createManualActivationFile -logFile /dev/stdout 2>&1 || true

ALF_FILE=$(find /project /root -maxdepth 2 -name "Unity_v*.alf" 2>/dev/null | head -1 || true)

if [[ -n "${ALF_FILE}" ]]; then
    cp "${ALF_FILE}" "/root/.config/unity3d/manual-activation.alf"
    touch /root/.config/unity3d/.alf-generated-this-run
    echo "==> Saved .alf to /root/.config/unity3d/manual-activation.alf"
else
    echo "ERROR: Unity did not produce a .alf file."
    exit 1
fi
'

DOCKER_EXIT=0
timeout "${UNITY_TIMEOUT}" docker run --rm \
    -v "${UNITY_TEST_PROJECT_DIR}:/project" \
    -v "${UNITY_LICENSE_CACHE_LOCAL_DIR}:/root/.local/share/unity3d" \
    -v "${UNITY_LICENSE_CACHE_CONFIG_DIR}:/root/.config/unity3d" \
    -w /project \
    "${UNITY_IMAGE}" \
    bash -c "${INNER_SCRIPT}" || DOCKER_EXIT=$?

if [[ "${DOCKER_EXIT}" -ne 0 ]]; then
    echo "ERROR: [generate-activation] Docker container exited with code ${DOCKER_EXIT}."
    exit "${DOCKER_EXIT}"
fi

if [[ ! -f "${UNITY_LICENSE_CACHE_CONFIG_DIR}/manual-activation.alf" || ! -f "${UNITY_LICENSE_CACHE_CONFIG_DIR}/.alf-generated-this-run" ]]; then
    echo "ERROR: [generate-activation] .alf file was not found after Docker run."
    echo "ERROR: Check the output above for Unity errors."
    rm -f "${UNITY_LICENSE_CACHE_CONFIG_DIR}/.alf-generated-this-run"
    exit 1
fi

mkdir -p "${SECRETS_DIR}"
cp "${UNITY_LICENSE_CACHE_CONFIG_DIR}/manual-activation.alf" "${SECRETS_DIR}/manual-activation.alf"
rm -f "${UNITY_LICENSE_CACHE_CONFIG_DIR}/.alf-generated-this-run"

echo ""
echo "==> [generate-activation] Manual activation file generated successfully."
echo "==> [generate-activation] To activate this machine, follow these steps:"
echo "==>   1. The activation file is at: .unity-secrets/manual-activation.alf"
echo "==>   2. Upload it at https://license.unity3d.com/manual"
echo "==>   3. Complete the serial-based manual activation flow and save the .ulf as: .unity-secrets/license.ulf"
echo "==>   4. Run: npm run unity:retry-license"
exit "${DOCKER_EXIT}"
