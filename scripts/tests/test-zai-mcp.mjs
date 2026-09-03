import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import {
  createAuthorizationHeaderFile,
  launchDefinition,
  resolveZaiApiKey
} from "../mcp/zai-mcp.mjs";

test("Z.AI credential resolution prefers the process environment", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "zai-mcp-credential-"));
  try {
    fs.writeFileSync(path.join(repoRoot, ".env.local"), "Z_AI_API_KEY=from-file\n");
    assert.equal(
      resolveZaiApiKey(repoRoot, { Z_AI_API_KEY: "from-environment" }),
      "from-environment"
    );
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("Z.AI credential resolution falls back to the gitignored local environment", () => {
  const repoRoot = fs.mkdtempSync(path.join(os.tmpdir(), "zai-mcp-credential-"));
  try {
    fs.writeFileSync(path.join(repoRoot, ".env.local"), "Z_AI_API_KEY='from file'\n");
    assert.equal(resolveZaiApiKey(repoRoot, {}), "from file");
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("remote Z.AI servers receive credentials through a private temporary header file", () => {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "zai-mcp-header-test-"));
  try {
    const header = createAuthorizationHeaderFile("secret-key", temporaryRoot);
    assert.equal(fs.statSync(header.directory).mode & 0o777, 0o700);
    assert.equal(fs.statSync(header.filePath).mode & 0o777, 0o600);
    assert.equal(fs.readFileSync(header.filePath, "utf8"), "Authorization: Bearer secret-key\n");
    header.cleanup();
    assert.equal(fs.existsSync(header.directory), false);
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

test("the launcher maps every official Z.AI MCP service to its current transport", () => {
  const cases = [
    ["vision", "zai-mcp-server", undefined],
    ["web-search", "mcp-remote", "https://api.z.ai/api/mcp/web_search_prime/mcp"],
    ["web-reader", "mcp-remote", "https://api.z.ai/api/mcp/web_reader/mcp"],
    ["zread", "mcp-remote", "https://api.z.ai/api/mcp/zread/mcp"]
  ];

  for (const [mode, expectedCommand, expectedUrl] of cases) {
    const definition = launchDefinition(mode, "secret-key", os.tmpdir());
    try {
      assert.equal(definition.command, expectedCommand);
      if (expectedUrl) {
        assert.equal(definition.args[0], expectedUrl);
        assert.equal(definition.args[1], "--header-file");
        assert.doesNotMatch(definition.args.join(" "), /secret-key/);
      } else {
        assert.deepEqual(definition.args, []);
        assert.equal(definition.environment.Z_AI_API_KEY, "secret-key");
        assert.equal(definition.environment.Z_AI_MODE, "ZAI");
      }
    } finally {
      definition.cleanup();
    }
  }
});

test("the CLI keeps the remote credential out of argv and removes its header file", () => {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "zai-mcp-cli-test-"));
  try {
    const fakeBin = path.join(temporaryRoot, "bin");
    const capture = path.join(temporaryRoot, "capture");
    fs.mkdirSync(fakeBin);
    const fakeRemote = path.join(fakeBin, "mcp-remote");
    fs.writeFileSync(
      fakeRemote,
      [
        "#!/usr/bin/env bash",
        "set -euo pipefail",
        'printf \'%s\\n\' "$@" >"${ZAI_TEST_CAPTURE:?}"',
        'cp "$3" "${ZAI_TEST_CAPTURE}.header"'
      ].join("\n") + "\n"
    );
    fs.chmodSync(fakeRemote, 0o755);

    const script = path.resolve("scripts", "mcp", "zai-mcp.mjs");
    const result = spawnSync(process.execPath, [script, "web-search"], {
      encoding: "utf8",
      env: {
        ...process.env,
        PATH: `${fakeBin}${path.delimiter}${process.env.PATH}`,
        ZAI_TEST_CAPTURE: capture,
        Z_AI_API_KEY: "cli-secret"
      }
    });
    assert.equal(result.status, 0, result.stderr);
    const args = fs.readFileSync(capture, "utf8");
    assert.doesNotMatch(args, /cli-secret/);
    assert.equal(
      fs.readFileSync(`${capture}.header`, "utf8"),
      "Authorization: Bearer cli-secret\n"
    );
    const headerPath = args.trimEnd().split("\n")[2];
    assert.equal(fs.existsSync(headerPath), false);
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
});
