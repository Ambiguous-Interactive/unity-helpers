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

PACKAGE_NAME="$(jq -r '.name' "${REPO_ROOT}/package.json")"
PACKAGE_VERSION="$(jq -r '.version' "${REPO_ROOT}/package.json")"
if [[ -z "${OUTPUT_PATH}" ]]; then
    OUTPUT_PATH="${REPO_ROOT}/.artifacts/release/${PACKAGE_NAME}-${PACKAGE_VERSION}.unitypackage"
fi

ARTIFACTS_ROOT="$(realpath -m "${REPO_ROOT}/.artifacts")"
PROJECT_DIR="$(realpath -m "${PROJECT_DIR}")"
if [[ "${PROJECT_DIR}" != "${ARTIFACTS_ROOT}" && "${PROJECT_DIR}" != "${ARTIFACTS_ROOT}/"* ]]; then
    echo "ERROR: Refusing to create the export project outside ${ARTIFACTS_ROOT}: ${PROJECT_DIR}" >&2
    exit 1
fi

echo "==> [export-unitypackage] Package: ${PACKAGE_NAME}@${PACKAGE_VERSION}"
echo "==> [export-unitypackage] Unity version: ${UNITY_VERSION}"
echo "==> [export-unitypackage] Project: ${PROJECT_DIR}"
echo "==> [export-unitypackage] Output: ${OUTPUT_PATH}"

rm -rf "${PROJECT_DIR}"
mkdir -p "${PROJECT_DIR}/Assets/WallstopStudios" "${PROJECT_DIR}/Assets/Editor" "${PROJECT_DIR}/ProjectSettings" "${PROJECT_DIR}/Packages"

cat > "${PROJECT_DIR}/ProjectSettings/ProjectVersion.txt" << EOF
m_EditorVersion: ${UNITY_VERSION}
EOF

cat > "${PROJECT_DIR}/ProjectSettings/ProjectSettings.asset" << 'EOF'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  productName: UnityHelpers-PackageExport
  companyName: WallstopStudios
  runInBackground: 1
EOF

cat > "${PROJECT_DIR}/ProjectSettings/EditorSettings.asset" << 'EOF'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!159 &1
EditorSettings:
  m_DefaultBehaviorMode: 1
EOF

jq '{dependencies: ({"com.unity.test-framework": "1.1.33"} + .modules)}' \
    "${REPO_ROOT}/.github/unity-test-project-modules.json" > "${PROJECT_DIR}/Packages/manifest.json"

PACK_TEMP="$(mktemp -d)"
cleanup() {
    rm -rf "${PACK_TEMP}"
}
trap cleanup EXIT

PACK_DIR="${PACK_TEMP}/pack"
EXTRACT_DIR="${PACK_TEMP}/extract"
mkdir -p "${PACK_DIR}" "${EXTRACT_DIR}"

pushd "${REPO_ROOT}" > /dev/null
PACK_JSON="$(npm pack --json --pack-destination "${PACK_DIR}")"
popd > /dev/null

PACKAGE_FILE="$(printf '%s' "${PACK_JSON}" | jq -r '.[0].filename')"
if [[ -z "${PACKAGE_FILE}" || ! -f "${PACK_DIR}/${PACKAGE_FILE}" ]]; then
    echo "ERROR: npm pack did not produce a tarball." >&2
    exit 1
fi

tar -xzf "${PACK_DIR}/${PACKAGE_FILE}" -C "${EXTRACT_DIR}"
SOURCE_ROOT="${EXTRACT_DIR}/package"
STAGED_ROOT="${PROJECT_DIR}/Assets/WallstopStudios/UnityHelpers"
mkdir -p "${STAGED_ROOT}"

copy_package_entry() {
    local entry="$1"
    local required="$2"
    local source="${SOURCE_ROOT}/${entry}"
    local target="${STAGED_ROOT}/${entry}"

    if [[ ! -e "${source}" ]]; then
        if [[ "${required}" == "required" ]]; then
            echo "ERROR: Packed npm package is missing required Unity export entry: ${entry}" >&2
            exit 1
        fi
        return 0
    fi

    mkdir -p "$(dirname "${target}")"
    cp -a "${source}" "${target}"
}

for entry in \
    package.json \
    package.json.meta \
    README.md \
    README.md.meta \
    LICENSE \
    LICENSE.meta \
    CHANGELOG.md \
    CHANGELOG.md.meta \
    Runtime \
    Runtime.meta \
    Editor \
    Editor.meta
do
    copy_package_entry "${entry}" required
done

for entry in docs docs.meta Samples~; do
    copy_package_entry "${entry}" optional
done

if [[ -d "${STAGED_ROOT}/Samples~" ]]; then
    rm -rf "${STAGED_ROOT}/Samples"
    mv "${STAGED_ROOT}/Samples~" "${STAGED_ROOT}/Samples"
fi

cat > "${PROJECT_DIR}/Assets/Editor/UnityHelpersPackageExporter.cs" << 'EOF'
using System;
using System.IO;
using UnityEditor;

public static class UnityHelpersPackageExporter
{
    public static void Export()
    {
        string outputPath = GetArgument("-exportOutput");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new InvalidOperationException("Missing -exportOutput argument.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ExportPackage(
            "Assets/WallstopStudios/UnityHelpers",
            outputPath,
            ExportPackageOptions.Recurse
        );

        FileInfo exported = new FileInfo(outputPath);
        if (!exported.Exists || exported.Length <= 0)
        {
            throw new InvalidOperationException("Unity package export did not produce a non-empty file: " + outputPath);
        }
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length - 1; index++)
        {
            if (args[index] == name)
            {
                return args[index + 1];
            }
        }

        return string.Empty;
    }
}
EOF

if [[ "${STAGE_ONLY}" -eq 1 ]]; then
    echo "==> [export-unitypackage] Stage-only mode complete."
    exit 0
fi

INTERNAL_OUTPUT_DIR="${PROJECT_DIR}/unitypackage-output"
INTERNAL_OUTPUT="${INTERNAL_OUTPUT_DIR}/$(basename "${OUTPUT_PATH}")"
mkdir -p "${INTERNAL_OUTPUT_DIR}" "$(dirname "${OUTPUT_PATH}")"

UNITY_TEST_PROJECT_DIR="${PROJECT_DIR}" \
UNITY_VERSION="${UNITY_VERSION}" \
UNITY_TIMEOUT="${UNITY_TIMEOUT:-7200}" \
"${SCRIPT_DIR}/run-unity-docker.sh" \
    -batchmode -nographics -quit \
    -projectPath /project \
    -executeMethod UnityHelpersPackageExporter.Export \
    -exportOutput "/project/unitypackage-output/$(basename "${OUTPUT_PATH}")" \
    -logFile -

if [[ ! -s "${INTERNAL_OUTPUT}" ]]; then
    echo "ERROR: Unity package was not exported: ${INTERNAL_OUTPUT}" >&2
    exit 1
fi

cp -f "${INTERNAL_OUTPUT}" "${OUTPUT_PATH}"
(cd "$(dirname "${OUTPUT_PATH}")" && sha256sum "$(basename "${OUTPUT_PATH}")" > "$(basename "${OUTPUT_PATH}").sha256")

echo "==> [export-unitypackage] Exported ${OUTPUT_PATH}"
