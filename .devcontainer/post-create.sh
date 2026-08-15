#!/usr/bin/env bash
# Post-create setup script for the devcontainer.
# Runs as the remoteUser (vscode) after the container is created.
#
# This script fixes volume mount permissions and bootstraps the development
# environment. It is referenced from devcontainer.json's postCreateCommand.
#
# Usage:
#   bash .devcontainer/post-create.sh
#
# Why a script instead of an inline command?
#   - Proper error handling (set -euo pipefail)
#   - Clear step-by-step logging
#   - Testable and lintable
#   - Easier to maintain than a 200-char JSON string
#
# Volume permission background:
#   Docker named volumes are always created with root:root ownership.
#   When remoteUser is "vscode" (UID 1000), tools like npm, dotnet, and pip
#   cannot write to these directories without a chown fix.
#   See: https://github.com/microsoft/vscode-remote-release/issues/9931

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NPM_PREFIX="${NPM_CONFIG_PREFIX:-${HOME}/.local}"
CODEX_VERSION_TIMEOUT_SECONDS="${CODEX_VERSION_TIMEOUT_SECONDS:-10}"
CODEX_LOGIN_STATUS_TIMEOUT_SECONDS="${CODEX_LOGIN_STATUS_TIMEOUT_SECONDS:-5}"

export PATH="${NPM_PREFIX}/bin:${PATH}"

# ── Helpers ──────────────────────────────────────────────────────────────────

log_step() {
    echo ""
    echo "==> $1"
}

log_ok() {
    echo "    OK: $1"
}

log_warn() {
    echo "    WARN: $1" >&2
}

# ── Prerequisites ────────────────────────────────────────────────────────────

if ! command -v sudo >/dev/null 2>&1; then
    echo "ERROR: sudo is required but not found." >&2
    echo "The devcontainer base image should provide passwordless sudo." >&2
    echo "If using a custom base image, install sudo or run as root." >&2
    exit 1
fi

# ── Step 1: Fix volume mount permissions ─────────────────────────────────────
# Docker named volumes are created as root:root. The vscode user (UID 1000)
# needs ownership to write caches and packages.
#
# IMPORTANT: This MUST run before any tool that writes to these directories
# (dotnet tool restore, npm ci, pip install). Order matters.

log_step "Fixing volume mount permissions"

VOLUME_DIRS=(
    "/home/vscode/.npm"
    "/home/vscode/.nuget/packages"
    "/home/vscode/.cache/pip"
    "/home/vscode/.unity-test-project"
    "/home/vscode/.unity-test-project/.unity-license-cache/local-share-unity3d"
    "/home/vscode/.unity-test-project/.unity-license-cache/config-unity3d"
)

# System-managed volumes that must remain root-owned.
# /var/lib/docker is managed by the Docker-in-Docker feature and persists
# Docker images (including the ~3-4 GB Unity Editor image) across rebuilds.
# It must NOT be chown'd — the Docker daemon requires root ownership.
# This array is referenced by test-post-create.sh (data-driven cross-validation).
# shellcheck disable=SC2034
SYSTEM_MANAGED_VOLUMES=("/var/lib/docker")

for dir in "${VOLUME_DIRS[@]}"; do
    if [ -d "$dir" ]; then
        sudo chown -R "$(id -u):$(id -g)" "$dir"
        log_ok "Fixed ownership: $dir"
    else
        sudo mkdir -p "$dir"
        sudo chown -R "$(id -u):$(id -g)" "$dir"
        log_ok "Created and fixed ownership: $dir"
    fi
done

# ── Step 2: Restore .NET tools ───────────────────────────────────────────────
# Restores tools from .config/dotnet-tools.json (CSharpier, etc.)
# Uses || true because dotnet tool restore may fail in some environments
# (e.g., missing SDK workloads) and we want npm install to proceed regardless.

log_step "Restoring .NET tools"

if dotnet tool restore; then
    log_ok "dotnet tool restore succeeded"
else
    log_warn "dotnet tool restore failed (non-fatal, continuing)"
fi

# ── Step 3: Install npm dependencies ─────────────────────────────────────────
# Uses npm ci (clean install from lockfile) when package-lock.json exists,
# otherwise falls back to npm install.

log_step "Installing npm dependencies"

if [ -f package-lock.json ]; then
    npm ci
    log_ok "npm ci succeeded (from lockfile)"
else
    npm i --no-audit --no-fund
    log_ok "npm install succeeded (no lockfile)"
fi

# ── Step 4: Install/update OpenAI Codex CLI ──────────────────────────────────
# Ensures `codex` is installed globally and current on first container create.
# Failures are non-fatal and retried again from post-start.

log_step "Installing OpenAI Codex CLI"

if bash "$SCRIPT_DIR/install-codex.sh" --force-latest-check && command -v codex >/dev/null 2>&1 && timeout "${CODEX_VERSION_TIMEOUT_SECONDS}" codex --version >/dev/null 2>&1; then
    log_ok "OpenAI Codex CLI is available"
else
    log_warn "OpenAI Codex CLI is not currently available (non-fatal). It will retry on next container start."
fi

# ── Step 4b: Check Codex authentication state ───────────────────────────────
# Login remains browser-first. This is advisory and never blocks setup.

log_step "Checking Codex authentication status"

if command -v codex >/dev/null 2>&1 && timeout "${CODEX_LOGIN_STATUS_TIMEOUT_SECONDS}" codex login status >/dev/null 2>&1; then
    log_ok "Codex is already authenticated"
else
    log_warn "Codex is not logged in yet. Run: npm run codex:login"
fi

# ── Step 5: Install git hooks ────────────────────────────────────────────────
# Sets core.hooksPath and makes hook scripts executable.

log_step "Installing git hooks"

npm run hooks:install
log_ok "Git hooks installed"

# ── Step 5b: Configure git push defaults ─────────────────────────────────────
# Sets push.autoSetupRemote=true and push.default=simple locally so that
# `git push` on a new branch works without --set-upstream flags.

log_step "Configuring git push defaults"

if WORKSPACE_DIR_FOR_GIT="$(git rev-parse --show-toplevel 2>/dev/null)"; then
    bash "$WORKSPACE_DIR_FOR_GIT/scripts/configure-git-defaults.sh" "$WORKSPACE_DIR_FOR_GIT"
    log_ok "Git push defaults configured"
else
    log_warn "Not inside a git working tree; skipping push defaults configuration"
fi

# ── Step 6: Mark workspace as safe directory ─────────────────────────────────
# Required when the workspace is bind-mounted and may be owned by a different
# UID on the host.

log_step "Configuring git safe directory"

# Detect workspace directory. containerWorkspaceFolder is a devcontainer.json
# substitution variable and is NOT available as a shell env var, so we use
# git rev-parse as primary detection, with Codespaces fallback.
if WORKSPACE_DIR="$(git rev-parse --show-toplevel 2>/dev/null)"; then
    :
elif [ -n "${CODESPACE_VSCODE_FOLDER:-}" ]; then
    WORKSPACE_DIR="$CODESPACE_VSCODE_FOLDER"
else
    WORKSPACE_DIR="$(pwd)"
fi
git config --global --add safe.directory "$WORKSPACE_DIR"
log_ok "Marked $WORKSPACE_DIR as safe directory"

# Creating ~/.gitconfig above is what makes the next part necessary: Dev Containers copies the
# host's git config into that now-existing file at attach time, bringing a SECOND credential.helper
# (git runs every one of them, so two means two host dialogs per request) and the host's Windows
# safe.directory entries (a "not absolute" warning per entry on every git command).
bash "$WORKSPACE_DIR/scripts/normalize-container-git-config.sh" || log_warn "Could not normalize container git config"

# ── Step 7: Pre-pull Unity Docker image (background) ─────────────────────────
# Pre-pulls the GameCI Unity Editor Docker image so that unity test/compile
# scripts can run immediately without waiting for the image download.
# This runs in the background and is non-fatal (the image will be pulled
# on-demand if this step is skipped or fails).

log_step "Pre-pulling Unity Docker image (background)"

UNITY_VERSION="${UNITY_VERSION:-2021.3.45f1}"
UNITY_IMAGE_VERSION="${UNITY_IMAGE_VERSION:-3}"
UNITY_IMAGE="unityci/editor:ubuntu-${UNITY_VERSION}-base-${UNITY_IMAGE_VERSION}"

if command -v docker >/dev/null 2>&1; then
    # Pull in background so it doesn't block post-create
    (docker pull "$UNITY_IMAGE" >/dev/null 2>&1 && \
        log_ok "Unity image pulled: $UNITY_IMAGE") &
    log_ok "Unity image pull started in background: $UNITY_IMAGE"
else
    log_warn "Docker not available yet (DinD may still be starting). Unity image will be pulled on first use."
fi

# ── Done ─────────────────────────────────────────────────────────────────────

echo ""
echo "========================================"
echo "Post-create setup completed successfully"
echo "========================================"
