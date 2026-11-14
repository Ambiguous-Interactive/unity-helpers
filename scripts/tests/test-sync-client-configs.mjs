import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";

const script = path.resolve("scripts/mcp/sync-client-configs.mjs");

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, encoding: "utf8" });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout;
}

function write(filePath, contents) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, contents);
}

test("worktree config sync copies private configs and preserves other global settings", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "mcp-sync-"));
  const repo = path.join(root, "repo");
  const home = path.join(root, "home");
  const worktree = path.join(home, "worktree");
  try {
    fs.mkdirSync(repo, { recursive: true });
    run("git", ["init", "-q"], repo);
    run("git", ["config", "user.email", "test@example.invalid"], repo);
    run("git", ["config", "user.name", "Test"], repo);
    write(path.join(repo, "README.md"), "test\n");
    run("git", ["add", "README.md"], repo);
    run("git", ["commit", "-qm", "initial"], repo);
    run("git", ["worktree", "add", "-q", worktree, "-b", "worktree"], repo);

    write(
      path.join(repo, ".mcp.json"),
      '{"mcpServers":{"fixture":{"type":"http","url":"http://example.invalid/mcp"}}}\n'
    );
    write(path.join(repo, ".env.local"), "FIXTURE_TOKEN=fake-secret-value\nSOURCE_ONLY=kept\n");
    write(path.join(worktree, ".env.local"), "CUSTOM=keep\nFIXTURE_TOKEN=stale\n");
    write(
      path.join(repo, "opencode.json"),
      '{"$schema":"https://opencode.ai/config.json","mcp":{"fixture":{"type":"remote","url":"http://example.invalid/mcp","enabled":true}}}\n'
    );
    write(
      path.join(home, ".config/opencode/opencode.jsonc"),
      '{"$schema":"https://opencode.ai/config.json","theme":"Keep ,] unchanged","mcp":{"personal":{"type":"local"}},}\n'
    );

    const output = run(process.execPath, [script, "--repo-root", repo, "--home", home], repo);
    assert.doesNotMatch(output, /fake-secret-value/);
    assert.match(output, /1 persistent worktree/);
    assert.match(output, /1 server/);

    const copiedConfig = path.join(worktree, ".mcp.json");
    const copiedEnv = path.join(worktree, ".env.local");
    assert.equal(
      fs.readFileSync(copiedConfig, "utf8"),
      fs.readFileSync(path.join(repo, ".mcp.json"), "utf8")
    );
    const envText = fs.readFileSync(copiedEnv, "utf8");
    assert.match(envText, /CUSTOM=keep/);
    assert.match(envText, /FIXTURE_TOKEN=fake-secret-value/);
    assert.match(envText, /SOURCE_ONLY=kept/);
    assert.doesNotMatch(envText, /FIXTURE_TOKEN=stale/);
    assert.equal(fs.statSync(copiedConfig).mode & 0o777, 0o600);
    assert.equal(fs.statSync(copiedEnv).mode & 0o777, 0o600);

    const global = JSON.parse(
      fs.readFileSync(path.join(home, ".config/opencode/opencode.jsonc"), "utf8")
    );
    assert.equal(global.theme, "Keep ,] unchanged");
    assert.deepEqual(Object.keys(global.mcp).sort(), ["fixture", "personal"]);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
