import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { resolveGithubToken } from "../mcp/github-mcp.mjs";

test("GitHub MCP credential resolution prefers the process environment", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "github-mcp-credential-"));
  try {
    fs.writeFileSync(path.join(repoRoot, ".env.local"), "GITHUB_PERSONAL_ACCESS_TOKEN=from-file\n");
    assert.equal(
      resolveGithubToken(
        repoRoot,
        { GITHUB_PERSONAL_ACCESS_TOKEN: "from-environment" },
        () => "from-cache"
      ),
      "from-environment"
    );
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("GitHub MCP credential resolution reads the gitignored local environment before the cache", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "github-mcp-credential-"));
  try {
    fs.writeFileSync(
      path.join(repoRoot, ".env.local"),
      "GITHUB_PERSONAL_ACCESS_TOKEN='from local file'\n"
    );
    assert.equal(
      resolveGithubToken(repoRoot, {}, () => "from-cache"),
      "from local file"
    );
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("GitHub MCP credential resolution retains the prompt-free cache as its final fallback", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "github-mcp-credential-"));
  try {
    let cacheReads = 0;
    assert.equal(
      resolveGithubToken(repoRoot, {}, () => {
        cacheReads += 1;
        return "from-cache\n";
      }),
      "from-cache"
    );
    assert.equal(cacheReads, 1);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("the GitHub MCP CLI passes only the credential name to Docker argv", () => {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "github-mcp-cli-test-"));
  try {
    const fakeBin = path.join(temporaryRoot, "bin");
    const capture = path.join(temporaryRoot, "capture");
    fs.mkdirSync(fakeBin);
    const fakeDocker = path.join(fakeBin, "docker");
    fs.writeFileSync(
      fakeDocker,
      [
        "#!/usr/bin/env bash",
        "set -euo pipefail",
        'printf \'%s\\n\' "$@" >"${GITHUB_MCP_TEST_CAPTURE:?}.args"',
        'printf \'%s\' "${GITHUB_PERSONAL_ACCESS_TOKEN:?}" >"${GITHUB_MCP_TEST_CAPTURE}.token"'
      ].join("\n") + "\n"
    );
    fs.chmodSync(fakeDocker, 0o755);

    const script = path.resolve("scripts", "mcp", "github-mcp.mjs");
    const result = spawnSync(process.execPath, [script], {
      encoding: "utf8",
      env: {
        ...process.env,
        PATH: `${fakeBin}${path.delimiter}${process.env.PATH}`,
        GITHUB_MCP_TEST_CAPTURE: capture,
        GITHUB_PERSONAL_ACCESS_TOKEN: "github-cli-secret"
      }
    });
    assert.equal(result.status, 0, result.stderr);
    const args = fs.readFileSync(`${capture}.args`, "utf8");
    assert.deepEqual(args.trimEnd().split("\n"), [
      "run",
      "-i",
      "--rm",
      "-e",
      "GITHUB_PERSONAL_ACCESS_TOKEN",
      "ghcr.io/github/github-mcp-server:latest"
    ]);
    assert.doesNotMatch(args, /github-cli-secret/);
    assert.equal(fs.readFileSync(`${capture}.token`, "utf8"), "github-cli-secret");
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
});
