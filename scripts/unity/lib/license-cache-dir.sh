#!/usr/bin/env bash
###############################################################################
# license-cache-dir.sh
#
# The one derivation of the Unity license cache directory.
#
# The cache is license identity by definition: retry-license.sh and
# validate-license-setup.sh both name Unity_lic.ulf and
# UnityEntitlementLicense.xml inside it, and the container's licensing client
# also writes Unity.Entitlements.Audit.log there. It must therefore never sit
# under a directory CI uploads as an artifact.
# scripts/unity/run-ci-tests.ps1 states exactly that rule for the Windows path
# (the SECURITY block above Invoke-UnityLicenseActivate): the activation log
# lives under RUNNER_TEMP or the system temp directory, never under the
# artifacts path. This is the Docker path saying the same thing.
#
# The default still prefers the test-project volume, because that is what keeps
# the cache out of the workspace -- no ownership and no commit risk -- for the
# devcontainer, whose project directory is /home/vscode/.unity-test-project and
# is already outside the checkout. Only when the project directory is itself
# inside .artifacts does the cache move: export-unitypackage.sh puts its export
# project there deliberately, and .artifacts is the tree the release and
# unitypackage-smoke jobs upload from.
#
# Sourced by run-unity-docker.sh, generate-activation.sh, retry-license.sh and
# validate-license-setup.sh so the four cannot drift apart; exercised directly
# by scripts/tests/test-unity-license-cache-location.js.
###############################################################################

###############################################################################
# unity_license_cache_artifacts_root: Prints the uploaded artifacts tree root.
#
# Arguments:
#   $1 - Repository workspace directory
###############################################################################
unity_license_cache_artifacts_root() {
    local workspace_dir="$1"
    realpath -m "${workspace_dir}/.artifacts"
}

###############################################################################
# unity_license_cache_is_under: Path containment test for already-resolved paths.
#
# Arguments:
#   $1 - Candidate path
#   $2 - Root path
#
# Returns:
#   0 - the candidate is the root or lives under it
#   1 - otherwise
###############################################################################
unity_license_cache_is_under() {
    local candidate="$1"
    local root="$2"
    [[ "${candidate}" == "${root}" || "${candidate}" == "${root}/"* ]]
}

###############################################################################
# resolve_unity_license_cache_dir: Prints the license cache directory to use.
#
# Arguments:
#   $1 - Unity test project directory
#   $2 - Repository workspace directory
#   $3 - Explicit UNITY_LICENSE_CACHE_DIR override (may be empty)
#
# Returns:
#   0 - the resolved directory was printed to stdout
#   1 - the resolved directory is inside the uploaded artifacts tree
###############################################################################
resolve_unity_license_cache_dir() {
    local test_project_dir="$1"
    local workspace_dir="$2"
    local explicit="${3-}"

    local artifacts_root
    artifacts_root="$(unity_license_cache_artifacts_root "${workspace_dir}")"

    local resolved
    if [[ -n "${explicit}" ]]; then
        resolved="$(realpath -m "${explicit}")"
    else
        local project_realpath
        project_realpath="$(realpath -m "${test_project_dir}")"
        if unity_license_cache_is_under "${project_realpath}" "${artifacts_root}"; then
            # RUNNER_TEMP is per-job on GitHub Actions and is never uploaded; the
            # system temp directory is the local equivalent. The project directory's
            # own name keeps the release export and the smoke export apart, which is
            # all the separation they need -- the two never run in one job.
            local temp_root
            temp_root="$(realpath -m "${RUNNER_TEMP:-${TMPDIR:-/tmp}}")"
            resolved="${temp_root}/unity-license-cache/$(basename "${project_realpath}")"
        else
            resolved="${project_realpath}/.unity-license-cache"
        fi
    fi

    if unity_license_cache_is_under "${resolved}" "${artifacts_root}"; then
        echo "ERROR: Refusing to place the Unity license cache inside the uploaded artifacts tree: ${resolved}" >&2
        echo "ERROR: ${artifacts_root} is what CI uploads, and the cache holds Unity license identity." >&2
        echo "ERROR: Set UNITY_LICENSE_CACHE_DIR to a directory outside it (RUNNER_TEMP or the system temp directory)." >&2
        return 1
    fi

    printf '%s\n' "${resolved}"
}
