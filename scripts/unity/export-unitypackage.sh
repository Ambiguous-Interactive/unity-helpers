#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
UNITY_VERSION="${UNITY_VERSION:-$(jq -r '.release' "${REPO_ROOT}/.github/unity-versions.json")}"
PROJECT_DIR="${UNITY_PACKAGE_PROJECT_DIR:-${REPO_ROOT}/.artifacts/unity/unitypackage-project}"
OUTPUT_PATH=""
STAGE_ONLY=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output)
            OUTPUT_PATH="$2"
            shift 2
            ;;
        --project-dir)
            PROJECT_DIR="$2"
            shift 2
            ;;
        --stage-only)
            STAGE_ONLY=1
            shift
            ;;
        *)
            echo "ERROR: Unknown argument: $1" >&2
            exit 1
            ;;
    esac
done

PACKAGE_JSON="${REPO_ROOT}/package.json"
PACKAGE_NAME="$(jq -r '(.name // empty) | strings | select(test("\\S"))' "${PACKAGE_JSON}")"
PACKAGE_VERSION="$(jq -r '(.version // empty) | strings | select(test("\\S"))' "${PACKAGE_JSON}")"
if [[ -z "${PACKAGE_NAME}" || -z "${PACKAGE_VERSION}" ]]; then
    echo "ERROR: ${PACKAGE_JSON} must define non-empty string name and version fields." >&2
    exit 1
fi

if [[ -z "${OUTPUT_PATH}" ]]; then
    OUTPUT_PATH="${REPO_ROOT}/.artifacts/release/${PACKAGE_NAME}-${PACKAGE_VERSION}.unitypackage"
fi

UNITY_VERSION="${UNITY_VERSION}" node "${SCRIPT_DIR}/stage-unitypackage.js" --project-dir "${PROJECT_DIR}"
PROJECT_DIR="$(realpath -m "${PROJECT_DIR}")"

if [[ "${STAGE_ONLY}" -eq 1 ]]; then
    echo "==> [export-unitypackage] Stage-only mode complete."
    exit 0
fi

INTERNAL_OUTPUT_DIR="${PROJECT_DIR}/unitypackage-output"
INTERNAL_OUTPUT="${INTERNAL_OUTPUT_DIR}/$(basename "${OUTPUT_PATH}")"
UNITY_LOG="${INTERNAL_OUTPUT_DIR}/unity.log"
mkdir -p "${INTERNAL_OUTPUT_DIR}" "$(dirname "${OUTPUT_PATH}")"

UNITY_EXIT_CODE=0
TEE_EXIT_CODE=0
UNITY_EXPORT_PIPE_STATUS=()
set +e
UNITY_TEST_PROJECT_DIR="${PROJECT_DIR}" \
UNITY_VERSION="${UNITY_VERSION}" \
UNITY_TIMEOUT="${UNITY_TIMEOUT:-7200}" \
"${SCRIPT_DIR}/run-unity-docker.sh" \
    -batchmode -nographics -quit \
    -releaseCodeOptimization \
    -projectPath /project \
    -executeMethod UnityHelpersPackageExporter.Export \
    -exportOutput "/project/unitypackage-output/$(basename "${OUTPUT_PATH}")" \
    -logFile - 2>&1 | tee "${UNITY_LOG}"
UNITY_EXPORT_PIPE_STATUS=("${PIPESTATUS[@]}")
UNITY_EXIT_CODE="${UNITY_EXPORT_PIPE_STATUS[0]}"
TEE_EXIT_CODE="${UNITY_EXPORT_PIPE_STATUS[1]}"
set -e

if [[ "${TEE_EXIT_CODE}" -ne 0 ]]; then
    echo "ERROR: Failed to persist Unity package export log with tee exit code ${TEE_EXIT_CODE}: ${UNITY_LOG}" >&2
    exit "${TEE_EXIT_CODE}"
fi

if [[ "${UNITY_EXIT_CODE}" -ne 0 ]]; then
    echo "ERROR: Unity package export failed with exit code ${UNITY_EXIT_CODE}. Log: ${UNITY_LOG}" >&2
    exit "${UNITY_EXIT_CODE}"
fi

if [[ ! -s "${INTERNAL_OUTPUT}" ]]; then
    echo "ERROR: Unity package was not exported: ${INTERNAL_OUTPUT}" >&2
    exit 1
fi

cp -f "${INTERNAL_OUTPUT}" "${OUTPUT_PATH}"
(cd "$(dirname "${OUTPUT_PATH}")" && sha256sum "$(basename "${OUTPUT_PATH}")" > "$(basename "${OUTPUT_PATH}").sha256")

echo "==> [export-unitypackage] Exported ${OUTPUT_PATH}"
