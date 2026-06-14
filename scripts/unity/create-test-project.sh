#!/usr/bin/env bash
set -euo pipefail

###############################################################################
# create-test-project.sh
#
# Creates a minimal Unity project structure suitable for testing a Unity package.
# Since this repo is a Unity PACKAGE (not a project), we scaffold a temporary
# Unity project that references this package via local path.
#
# Environment variables:
#   UNITY_VERSION          - Unity Editor version (default: 2021.3.45f1)
#   UNITY_TEST_PROJECT_DIR - Path to test project (default: /home/vscode/.unity-test-project)
#
# Flags:
#   --force  Recreate the project from scratch even if it already exists
#
# Usage:
#   ./create-test-project.sh
#   ./create-test-project.sh --force
###############################################################################

UNITY_VERSION="${UNITY_VERSION:-2021.3.45f1}"
UNITY_TEST_PROJECT_DIR="${UNITY_TEST_PROJECT_DIR:-/home/vscode/.unity-test-project}"

FORCE=0
for arg in "$@"; do
    case "${arg}" in
        --force)
            FORCE=1
            ;;
        *)
            echo "WARNING: Unknown argument: ${arg}"
            ;;
    esac
done

# Check if project already exists
if [[ -d "${UNITY_TEST_PROJECT_DIR}/Assets" && -f "${UNITY_TEST_PROJECT_DIR}/Packages/manifest.json" && "${FORCE}" -eq 0 ]]; then
    echo "==> [create-test-project] Test project already exists at ${UNITY_TEST_PROJECT_DIR}. Use --force to recreate."
    exit 0
fi

if [[ "${FORCE}" -eq 1 && -d "${UNITY_TEST_PROJECT_DIR}" ]]; then
    echo "==> [create-test-project] Removing existing test project (--force)..."
    rm -rf "${UNITY_TEST_PROJECT_DIR}"
fi

echo "==> [create-test-project] Creating test project at ${UNITY_TEST_PROJECT_DIR}..."

# Step 1: Create directory structure
echo "    [1/4] Creating directory structure..."
mkdir -p "${UNITY_TEST_PROJECT_DIR}/Assets"
mkdir -p "${UNITY_TEST_PROJECT_DIR}/ProjectSettings"
mkdir -p "${UNITY_TEST_PROJECT_DIR}/Packages"

# Step 2: Create ProjectVersion.txt
echo "    [2/4] Writing ProjectSettings/ProjectVersion.txt..."
cat > "${UNITY_TEST_PROJECT_DIR}/ProjectSettings/ProjectVersion.txt" << EOF
m_EditorVersion: ${UNITY_VERSION}
EOF

# Step 3: Create ProjectSettings.asset
echo "    [3/4] Writing ProjectSettings/ProjectSettings.asset..."
cat > "${UNITY_TEST_PROJECT_DIR}/ProjectSettings/ProjectSettings.asset" << 'EOF'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  productName: UnityHelpers-TestProject
  companyName: WallstopStudios
  defaultScreenWidth: 1024
  defaultScreenHeight: 768
  runInBackground: 1
EOF

# Step 4: Create Packages/manifest.json
echo "    [4/4] Writing Packages/manifest.json..."
# The UnityEngine built-in modules + com.unity.ugui that the package's Runtime/
# Editor code AND its test fixtures need to COMPILE come from the SHARED single
# source .github/unity-test-project-modules.json -- the SAME file the CI generator
# scripts/unity/run-ci-tests.ps1 (New-ManifestJson) reads -- so this local/
# devcontainer manifest's MODULE LIST can never drift from the CI generator's.
# (That drift -- the two generators declaring different/incomplete module sets --
# is exactly what made every Unity test leg fail to compile.) The test-framework
# version and the package reference below stay independently pinned per generator.
# These modules cannot live in package.json: it is a dual npm+UPM file and
# `npm ci` would fail to resolve com.unity.* from the npm registry.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODULES_SOURCE="${REPO_ROOT}/.github/unity-test-project-modules.json"
if [[ ! -f "${MODULES_SOURCE}" ]]; then
    echo "ERROR: missing Unity module single source: ${MODULES_SOURCE}" >&2
    exit 1
fi
if ! command -v jq >/dev/null 2>&1; then
    echo "ERROR: jq is required to generate the test-project manifest from ${MODULES_SOURCE}" >&2
    exit 1
fi

# Validate the source BEFORE writing, so an empty/malformed modules object fails
# loudly instead of leaving a stale, module-less manifest.json on disk.
if ! jq -e '(.modules | length) >= 1' "${MODULES_SOURCE}" >/dev/null; then
    echo "ERROR: ${MODULES_SOURCE} declares no modules; the test project would fail to compile." >&2
    exit 1
fi

# dependencies = test-framework + (shared modules, in file order) + the local
# package. jq object '+' is last-key-wins and preserves insertion order.
jq '{dependencies: ({"com.unity.test-framework": "1.1.33"} + .modules + {"com.wallstop-studios.unity-helpers": "file:/workspace"})}' \
    "${MODULES_SOURCE}" > "${UNITY_TEST_PROJECT_DIR}/Packages/manifest.json"

# Note: packages-lock.json is intentionally NOT created.
# Unity generates it on first project open during dependency resolution.
# An empty lock file would cause resolution failures.

echo "==> [create-test-project] Test project created successfully."
echo "    Project dir: ${UNITY_TEST_PROJECT_DIR}"
echo "    Unity version: ${UNITY_VERSION}"
echo "    Package reference: file:/workspace"
