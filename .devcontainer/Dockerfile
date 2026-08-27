# Use the .NET devcontainer base image
FROM mcr.microsoft.com/devcontainers/dotnet:1-9.0-bookworm

# Architecture for downloading binaries (supports amd64 and arm64)
ARG TARGETARCH=amd64

# Set non-interactive frontend for apt to avoid prompts
ENV DEBIAN_FRONTEND=noninteractive

# Fix: Remove the Yarn repository with expired GPG key
# The base image includes yarn repo, but the GPG key (23E7166788B63E1E) expired.
# This causes apt-get update to fail with "EXPKEYSIG" error.
# We don't need Yarn for this project, so simply remove the problematic source.
# If Yarn is needed later, use corepack (bundled with Node.js) or npm instead.
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && rm -f /etc/apt/keyrings/yarn.gpg \
    && rm -f /usr/share/keyrings/yarn-archive-keyring.gpg

# Install additional tools from apt repositories
# - ripgrep: Fast regex search (10x faster than grep)
# - jq: JSON processor
# - htop: Interactive process viewer (better than top)
# - ncdu: Disk usage analyzer with ncurses interface
# - tree: Directory listing in tree format
# - tldr: Simplified man pages with practical examples
# - silversearcher-ag: Fast code search (ag command)
# - moreutils: Additional Unix utilities (sponge, parallel, etc.)
# - unzip: Required for extracting some binaries
# - wget: Alternative to curl for downloads
# - ca-certificates: Ensure SSL certificates are available
# - shellcheck: Shell script linter (used by actionlint for enhanced checks)
#
# Additional tools installed from GitHub releases:
# - fd: Fast file finder (better find)
# - bat: Cat with syntax highlighting
# - fzf: Fuzzy finder
# - delta: Better git diffs
# - eza: Modern ls replacement
# - zoxide: Smarter cd command
# - yq: YAML processor
# - duf: Better df
# - sd: Intuitive find-and-replace (better sed)
# - dust: Visual disk usage (better du)
# - procs: Modern process viewer (better ps)
# - tokei: Code statistics
# - actionlint: GitHub Actions workflow linter
RUN apt-get update && apt-get install -y --no-install-recommends \
    ripgrep \
    jq \
    htop \
    ncdu \
    tree \
    tldr \
    silversearcher-ag \
    moreutils \
    unzip \
    wget \
    ca-certificates \
    shellcheck \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Install fd-find (faster alternative to find) from GitHub releases
# fd respects .gitignore by default and has intuitive syntax
RUN FD_VERSION="10.4.2" \
    && case "${TARGETARCH}" in \
        amd64) FD_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) FD_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/sharkdp/fd/releases/download/v${FD_VERSION}/fd-v${FD_VERSION}-${FD_ARCH}.tar.gz" \
    | tar -xz --strip-components=1 -C /usr/local/bin "fd-v${FD_VERSION}-${FD_ARCH}/fd" \
    && chmod +x /usr/local/bin/fd

# Install bat (better cat with syntax highlighting) from GitHub releases
RUN BAT_VERSION="0.26.1" \
    && case "${TARGETARCH}" in \
        amd64) BAT_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) BAT_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/sharkdp/bat/releases/download/v${BAT_VERSION}/bat-v${BAT_VERSION}-${BAT_ARCH}.tar.gz" \
    | tar -xz --strip-components=1 -C /usr/local/bin "bat-v${BAT_VERSION}-${BAT_ARCH}/bat" \
    && chmod +x /usr/local/bin/bat

# Install fzf (fuzzy finder) from GitHub releases
RUN FZF_VERSION="0.70.0" \
    && case "${TARGETARCH}" in \
        amd64) FZF_ARCH="linux_amd64" ;; \
        arm64) FZF_ARCH="linux_arm64" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/junegunn/fzf/releases/download/v${FZF_VERSION}/fzf-${FZF_VERSION}-${FZF_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin fzf \
    && chmod +x /usr/local/bin/fzf

# Install delta (better git diff viewer) from GitHub releases
RUN DELTA_VERSION="0.18.2" \
    && case "${TARGETARCH}" in \
        amd64) DELTA_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) DELTA_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/dandavison/delta/releases/download/${DELTA_VERSION}/delta-${DELTA_VERSION}-${DELTA_ARCH}.tar.gz" \
    | tar -xz --strip-components=1 -C /usr/local/bin "delta-${DELTA_VERSION}-${DELTA_ARCH}/delta" \
    && chmod +x /usr/local/bin/delta

# Install eza (modern replacement for ls, successor to exa) from GitHub releases
RUN EZA_VERSION="0.23.4" \
    && case "${TARGETARCH}" in \
        amd64) EZA_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) EZA_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/eza-community/eza/releases/download/v${EZA_VERSION}/eza_${EZA_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin \
    && chmod +x /usr/local/bin/eza

# Install zoxide (smarter cd command) from GitHub releases
RUN ZOXIDE_VERSION="0.9.9" \
    && case "${TARGETARCH}" in \
        amd64) ZOXIDE_ARCH="x86_64-unknown-linux-musl" ;; \
        arm64) ZOXIDE_ARCH="aarch64-unknown-linux-musl" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/ajeetdsouza/zoxide/releases/download/v${ZOXIDE_VERSION}/zoxide-${ZOXIDE_VERSION}-${ZOXIDE_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin zoxide \
    && chmod +x /usr/local/bin/zoxide

# Install yq (YAML processor, like jq for YAML) from GitHub releases
RUN YQ_VERSION="4.52.4" \
    && case "${TARGETARCH}" in \
        amd64) YQ_ARCH="linux_amd64" ;; \
        arm64) YQ_ARCH="linux_arm64" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/mikefarah/yq/releases/download/v${YQ_VERSION}/yq_${YQ_ARCH}" -o /usr/local/bin/yq \
    && chmod +x /usr/local/bin/yq

# Install duf (better df) from GitHub releases
RUN DUF_VERSION="0.9.1" \
    && case "${TARGETARCH}" in \
        amd64) DUF_ARCH="linux_x86_64" ;; \
        arm64) DUF_ARCH="linux_arm64" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/muesli/duf/releases/download/v${DUF_VERSION}/duf_${DUF_VERSION}_${DUF_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin duf \
    && chmod +x /usr/local/bin/duf

# Install sd (intuitive find-and-replace, better sed) from GitHub releases
# Note: arm64 uses musl (no gnu aarch64 builds published)
RUN SD_VERSION="1.1.0" \
    && case "${TARGETARCH}" in \
        amd64) SD_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) SD_ARCH="aarch64-unknown-linux-musl" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/chmln/sd/releases/download/v${SD_VERSION}/sd-v${SD_VERSION}-${SD_ARCH}.tar.gz" \
    | tar -xz --strip-components=1 -C /usr/local/bin "sd-v${SD_VERSION}-${SD_ARCH}/sd" \
    && chmod +x /usr/local/bin/sd

# Install dust (intuitive disk usage, better du) from GitHub releases
RUN DUST_VERSION="1.2.4" \
    && case "${TARGETARCH}" in \
        amd64) DUST_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) DUST_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/bootandy/dust/releases/download/v${DUST_VERSION}/dust-v${DUST_VERSION}-${DUST_ARCH}.tar.gz" \
    | tar -xz --strip-components=1 -C /usr/local/bin "dust-v${DUST_VERSION}-${DUST_ARCH}/dust" \
    && chmod +x /usr/local/bin/dust

# Install procs (modern replacement for ps) from GitHub releases
RUN PROCS_VERSION="0.14.11" \
    && case "${TARGETARCH}" in \
        amd64) PROCS_ARCH="x86_64-linux" ;; \
        arm64) PROCS_ARCH="aarch64-linux" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/dalance/procs/releases/download/v${PROCS_VERSION}/procs-v${PROCS_VERSION}-${PROCS_ARCH}.zip" -o /tmp/procs.zip \
    && unzip -q /tmp/procs.zip -d /tmp/procs \
    && PROCS_BIN="$(find /tmp/procs -type f -name procs | head -n 1)" \
    && test -n "${PROCS_BIN}" \
    && install -m 0755 "${PROCS_BIN}" /usr/local/bin/procs \
    && rm -rf /tmp/procs /tmp/procs.zip

# Install tokei (code statistics) from GitHub releases
RUN TOKEI_VERSION="12.1.2" \
    && case "${TARGETARCH}" in \
        amd64) TOKEI_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) TOKEI_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/XAMPPRocky/tokei/releases/download/v${TOKEI_VERSION}/tokei-${TOKEI_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin tokei \
    && chmod +x /usr/local/bin/tokei

# Install lychee (fast link checker) from GitHub releases
# Version should match lychee-action in .github/workflows/lint-doc-links.yml
RUN LYCHEE_VERSION="0.23.0" \
    && case "${TARGETARCH}" in \
        amd64) LYCHEE_ARCH="x86_64-unknown-linux-gnu" ;; \
        arm64) LYCHEE_ARCH="aarch64-unknown-linux-gnu" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/lycheeverse/lychee/releases/download/lychee-v${LYCHEE_VERSION}/lychee-${LYCHEE_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin lychee \
    && chmod +x /usr/local/bin/lychee

# Install actionlint (GitHub Actions workflow linter) from GitHub releases
# https://github.com/rhysd/actionlint - validates workflow syntax and detects common errors
RUN ACTIONLINT_VERSION="1.7.11" \
    && case "${TARGETARCH}" in \
        amd64) ACTIONLINT_ARCH="linux_amd64" ;; \
        arm64) ACTIONLINT_ARCH="linux_arm64" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_${ACTIONLINT_ARCH}.tar.gz" \
    | tar -xz -C /usr/local/bin actionlint \
    && chmod +x /usr/local/bin/actionlint

# Verify GitHub-release tools are available and executable.
RUN command -v fd >/dev/null \
    && command -v bat >/dev/null \
    && command -v fzf >/dev/null \
    && command -v delta >/dev/null \
    && command -v eza >/dev/null \
    && command -v zoxide >/dev/null \
    && command -v yq >/dev/null \
    && command -v duf >/dev/null \
    && command -v sd >/dev/null \
    && command -v dust >/dev/null \
    && command -v procs >/dev/null \
    && command -v tokei >/dev/null \
    && command -v lychee >/dev/null \
    && command -v actionlint >/dev/null

# Install cross-tool Python requirements for container tooling.
COPY .devcontainer/requirements-tools.txt /tmp/requirements-tools.txt

# Install PowerShell from GitHub releases to support both amd64 and arm64.
RUN POWERSHELL_VERSION="7.5.3" \
    && case "${TARGETARCH}" in \
        amd64) POWERSHELL_ARCH="x64" ; POWERSHELL_SHA256="5C74E0BBAFD8BE59E72267F05700FD6615344D9CF4D30460D9B6D12CD4A88A8C" ;; \
        arm64) POWERSHELL_ARCH="arm64" ; POWERSHELL_SHA256="4A6B656DD0E751E82C5DF9B9A4245BD1EC7D21DE5334AFBBD802DFD009595599" ;; \
        *) echo "Unsupported TARGETARCH: ${TARGETARCH}" >&2; exit 1 ;; \
    esac \
    && curl -fsSL "https://github.com/PowerShell/PowerShell/releases/download/v${POWERSHELL_VERSION}/powershell-${POWERSHELL_VERSION}-linux-${POWERSHELL_ARCH}.tar.gz" -o /tmp/powershell.tar.gz \
    && echo "${POWERSHELL_SHA256}  /tmp/powershell.tar.gz" | sha256sum -c - \
    && mkdir -p /opt/microsoft/powershell/7 \
    && tar -xzf /tmp/powershell.tar.gz -C /opt/microsoft/powershell/7 \
    && chmod +x /opt/microsoft/powershell/7/pwsh \
    && ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/pwsh \
    && ln -sf /opt/microsoft/powershell/7/pwsh /usr/local/bin/powershell \
    && rm -f /tmp/powershell.tar.gz \
    && pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()'

# Install Git LFS, Python, yamllint and Node.js in a consolidated cycle.
RUN curl -fsSL https://deb.nodesource.com/setup_22.x -o /tmp/nodesource_setup.sh \
    && bash /tmp/nodesource_setup.sh \
    && rm -f /tmp/nodesource_setup.sh \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
    git-lfs \
    nodejs \
    python3 \
    python3-pip \
    python3-venv \
    && python3 -m venv /opt/python-tools \
    && /opt/python-tools/bin/pip install --no-cache-dir --requirement /tmp/requirements-tools.txt \
    && ln -sf /opt/python-tools/bin/yamllint /usr/local/bin/yamllint \
    && git lfs install \
    && npm install -g npm@10.9.2 \
    && rm -f /tmp/requirements-tools.txt \
    && apt-get clean \
    && rm -rf /var/lib/apt/lists/*

# Pre-create volume mount target directories with correct ownership.
# Docker named volumes copy image-layer permissions into empty volumes on
# first mount, so this ensures new volumes start with vscode ownership.
# For existing volumes, post-create.sh runs sudo chown as a safety net.
# See: https://github.com/microsoft/vscode-remote-release/issues/9931
#
# Note: /var/lib/docker is managed by the Docker-in-Docker feature and must
# remain root-owned. It is NOT pre-created here (the DinD feature handles it).
RUN mkdir -p /home/vscode/.npm \
    && mkdir -p /home/vscode/.nuget/packages \
    && mkdir -p /home/vscode/.cache/pip \
    && mkdir -p /home/vscode/.unity-test-project \
    && chown -R vscode:vscode /home/vscode/.npm /home/vscode/.nuget /home/vscode/.cache /home/vscode/.unity-test-project

# Install CSharpier as a global .NET tool for the vscode user
USER vscode
RUN dotnet tool install -g csharpier --version 1.2.6

# Point npm's global prefix at a user-owned directory so `npm install -g` works
# without sudo for the vscode user (npm docs: "resolving EACCES permissions
# errors when installing packages globally"). The system prefix (/usr, root-owned)
# is what forced `sudo npm install` before. PATH entries for ~/.local/bin are
# added below (ENV for processes, profile.d for login shells).
RUN npm config set prefix "${HOME}/.local"

# Pre-install the AI coding agent CLIs so a freshly built/rebuilt image has them
# even before post-create runs. post-create.sh/post-start.sh refresh these to the
# latest versions (via .devcontainer/install-agent-clis.sh), so this layer is a
# fast offline-safe baseline rather than the source of truth.
RUN npm install -g --no-fund --no-audit \
    "@openai/codex@latest" \
    "opencode-ai@latest" \
    "@nanocollective/nanocoder@latest" \
    && "$(npm prefix -g)/bin/codex" --version \
    && "$(npm prefix -g)/bin/opencode" --version \
    && "$(npm prefix -g)/bin/nanocoder" --version

# Switch back to root for remaining setup
USER root

# Put user-installed npm binaries on PATH for login shells (ENV PATH below covers
# non-login processes). $HOME expands per-user, so root keeps a working PATH too.
RUN printf '%s\n' \
    '# User-global npm/bin tools (npm prefix ~/.local, dotnet tools, pip --user)' \
    'export PATH="$HOME/.local/bin:$HOME/.dotnet/tools:$PATH"' \
    > /etc/profile.d/90-user-binaries.sh \
    && chmod 0644 /etc/profile.d/90-user-binaries.sh

# Configure delta as the default git pager for better diffs
RUN git config --system core.pager delta \
    && git config --system interactive.diffFilter "delta --color-only" \
    && git config --system delta.navigate true \
    && git config --system delta.light false \
    && git config --system delta.line-numbers true \
    && git config --system delta.side-by-side false \
    && git config --system merge.conflictstyle diff3 \
    && git config --system diff.colorMoved default

# Initialize zoxide for bash (add to bashrc for interactive shells)
RUN echo 'eval "$(zoxide init bash)"' >> /etc/bash.bashrc

# Add helpful aliases to bashrc
# IMPORTANT: Do NOT alias standard POSIX tools (find, grep, sed, cat, etc.)
# Modern replacements (fd, rg, sd, bat) have DIFFERENT syntax and break scripts.
# They are installed and available by their real names: fd, rg, sd, bat, dust, procs, duf, etc.
# Only alias commands that don't shadow standard tools.
RUN { \
    echo ''; \
    echo '# Modern CLI tool aliases (list/tree only - never shadow POSIX tools)'; \
    echo '# Modern tools available by real name: fd, rg, sd, bat, dust, procs, duf, eza, z, zi'; \
    echo 'alias ls="eza --icons --group-directories-first"'; \
    echo 'alias ll="eza -la --icons --group-directories-first"'; \
    echo 'alias lt="eza --tree --icons --group-directories-first"'; \
    } >> /etc/bash.bashrc

# Enable VS Code shell integration
# VS Code automatically injects shell integration when terminal.integrated.shellIntegration.enabled is true.
# This manual fallback ensures shell integration works in edge cases (sub-shells, reconnections, etc.)
# See: https://code.visualstudio.com/docs/terminal/shell-integration
RUN { \
    echo ''; \
    echo '# VS Code shell integration (manual fallback)'; \
    echo '# Automatic injection is enabled via devcontainer.json settings.'; \
    echo '# This provides a fallback for sub-shells and edge cases.'; \
    echo 'if [[ "$TERM_PROGRAM" == "vscode" ]]; then'; \
    echo '    # Check if VS Code has injected the integration script path'; \
    echo '    if [[ -n "$VSCODE_SHELL_INTEGRATION" && -f "$VSCODE_SHELL_INTEGRATION" ]]; then'; \
    echo '        builtin source "$VSCODE_SHELL_INTEGRATION"'; \
    echo '    elif command -v code &> /dev/null; then'; \
    echo '        # Fallback: use code CLI to locate the script'; \
    echo '        _vscode_shell_integration="$(code --locate-shell-integration-path bash 2>/dev/null)"'; \
    echo '        if [[ -n "$_vscode_shell_integration" && -f "$_vscode_shell_integration" ]]; then'; \
    echo '            builtin source "$_vscode_shell_integration"'; \
    echo '        fi'; \
    echo '        unset _vscode_shell_integration'; \
    echo '    fi'; \
    echo 'fi'; \
    } >> /etc/bash.bashrc \
    && { \
    echo ''; \
    echo '# VS Code shell integration (manual fallback)'; \
    echo ''; \
    echo 'if [[ "$TERM_PROGRAM" == "vscode" ]]; then'; \
    echo '    if [[ -n "$VSCODE_SHELL_INTEGRATION" && -f "$VSCODE_SHELL_INTEGRATION" ]]; then'; \
    echo '        builtin source "$VSCODE_SHELL_INTEGRATION"'; \
    echo '    elif command -v code &> /dev/null; then'; \
    echo '        _vscode_shell_integration="$(code --locate-shell-integration-path zsh 2>/dev/null)"'; \
    echo '        if [[ -n "$_vscode_shell_integration" && -f "$_vscode_shell_integration" ]]; then'; \
    echo '            builtin source "$_vscode_shell_integration"'; \
    echo '        fi'; \
    echo '        unset _vscode_shell_integration'; \
    echo '    fi'; \
    echo 'fi'; \
    } >> /etc/zsh/zshrc

# Enable VS Code shell integration for PowerShell
# See: https://code.visualstudio.com/docs/terminal/shell-integration
RUN mkdir -p /opt/microsoft/powershell/7 \
    && { \
    echo '# VS Code shell integration (manual fallback)'; \
    echo 'if ($env:TERM_PROGRAM -eq "vscode") {'; \
    echo '    if ($env:VSCODE_SHELL_INTEGRATION -and (Test-Path $env:VSCODE_SHELL_INTEGRATION)) {'; \
    echo '        . $env:VSCODE_SHELL_INTEGRATION'; \
    echo '    } elseif (Get-Command code -ErrorAction SilentlyContinue) {'; \
    echo '        $script = code --locate-shell-integration-path pwsh 2>$null'; \
    echo '        if ($script -and (Test-Path $script)) { . $script }'; \
    echo '    }'; \
    echo '}'; \
    } >> /opt/microsoft/powershell/7/profile.ps1

# Ensure dotnet tools and user-global npm binaries are on the PATH
ENV PATH="${PATH}:/root/.dotnet/tools:/home/vscode/.dotnet/tools:/home/vscode/.local/bin"

# Reset to non-root user
USER vscode
