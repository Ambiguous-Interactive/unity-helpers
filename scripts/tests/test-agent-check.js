"use strict";
const test = require("node:test");
const assert = require("node:assert/strict");
const { select, changedFiles } = require("../agent-check");
const { execFileSync } = require("node:child_process");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
test("PR native tiers preserve full compatibility for risky or unknown changes", () => {
  const { resolvePullRequestMatrix } = require("../unity/resolve-test-matrix");
  const versions = ["2021.3.45f1", "6000.6.0f1"];
  const latest = resolvePullRequestMatrix(versions, ["Runtime/Utils/WallMath.cs"]);
  assert.deepEqual(latest["unity-versions"], versions);
  assert.deepEqual(latest["test-modes"], ["editmode", "playmode"]);
  assert.equal(latest.coverage, "editors");
  for (const files of [["new.config"], ["Runtime/Core/Serialization/Serializer.cs"], []]) {
    const full = resolvePullRequestMatrix(versions, files);
    assert.deepEqual(full["unity-versions"], versions);
    assert.ok(full["test-modes"].includes("standalone"));
    assert.equal(full.coverage, "full");
  }
  const documentation = resolvePullRequestMatrix(versions, ["docs/guide.md"]);
  assert.equal(documentation.coverage, "editors");
  assert.match(documentation.reason, /enrollment/);
});
test("native CLI requires a PR base and shares the committed change inventory", () => {
  const { spawnSync } = require("node:child_process");
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "native-selection-"));
  const git = (...args) => execFileSync("git", args, { cwd: root, encoding: "utf8" });
  try {
    for (const file of [
      "scripts/agent-check.js",
      "scripts/unity/resolve-test-matrix.js",
      ".github/unity-versions.json"
    ]) {
      const target = path.join(root, file);
      fs.mkdirSync(path.dirname(target), { recursive: true });
      fs.copyFileSync(path.join(__dirname, "../..", file), target);
    }
    git("init", "-q");
    git("add", ".");
    git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-qm", "base");
    const base = git("rev-parse", "HEAD").trim();
    fs.mkdirSync(path.join(root, "Runtime"));
    fs.writeFileSync(path.join(root, "Runtime/Calculation.cs"), "class Calculation {}");
    git("add", ".");
    git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-qm", "task");
    const checkout = path.join(root, "checkout");
    git("clone", "--quiet", "--no-local", "--depth", "2", root, checkout);
    assert.ok(fs.existsSync(path.join(checkout, ".git/shallow")));
    const output = path.join(root, "outputs");
    const env = {
      ...process.env,
      GITHUB_EVENT_NAME: "pull_request",
      AGENT_CHECK_BASE: base,
      GITHUB_OUTPUT: output,
      GITHUB_STEP_SUMMARY: ""
    };
    const result = spawnSync(
      process.execPath,
      [path.join(checkout, "scripts/unity/resolve-test-matrix.js")],
      { env, encoding: "utf8" }
    );
    assert.equal(result.status, 0, result.stderr);
    assert.match(fs.readFileSync(output, "utf8"), /test-modes=\["editmode","playmode"\]/);
    const missing = spawnSync(
      process.execPath,
      [path.join(checkout, "scripts/unity/resolve-test-matrix.js")],
      { env: { ...env, AGENT_CHECK_BASE: "" }, encoding: "utf8" }
    );
    assert.notEqual(missing.status, 0);
    assert.match(missing.stderr, /immutable PR base/);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});
for (const [file, expected] of [
  ["docs/guide.md", "none"],
  ["docs/guide.md.meta", "none"],
  ["docs/run.sh", "full"],
  [".agents/run.js", "full"],
  ["Runtime/Utils/WallMath.cs", "latest"],
  ["Runtime/Core/Serialization/Serializer.cs", "full"],
  ["Runtime/Core/Threading/Worker.cs", "full"],
  ["Runtime/Utils/Buffers.cs", "full"],
  ["Editor/CustomDrawers/Odin/Drawer.cs", "full"],
  ["Tests/Runtime/Thing.cs", "latest"],
  ["Runtime/Core/Thing.cs.meta", "full"],
  ["unknown.config", "full"],
  [".github/workflows/unity-tests.yml", "full"]
]) {
  test(`coverage for ${file}`, () => assert.equal(select([file], "pr").unity, expected));
}
test("unknown input dominates documentation", () =>
  assert.equal(select(["docs/a.md", "new.config"], "pr").unity, "full"));
test("empty input is explicitly inapplicable", () =>
  assert.equal(select([], "iterate").status, "not-applicable"));
test("full tier always includes compatibility", () =>
  assert.equal(select([], "full").unity, "full"));
test("iteration selects compiler scope", () =>
  assert.ok(select(["Editor/Thing.cs"], "iterate").commands.some((c) => c.includes("editor"))));
test("native compatibility risk does not force every compiler profile during source iteration", () => {
  for (const file of [
    "Runtime/Core/Serialization/Serializer.cs",
    "Runtime/Core/Threading/Worker.cs"
  ]) {
    const plan = select([file], "iterate");
    assert.equal(plan.unity, "full");
    assert.ok(
      plan.commands.some(
        (command) => command.includes("typecheck:iterate") && command.includes("runtime")
      )
    );
    assert.ok(!plan.commands.some((command) => command.includes("typecheck:unity")));
    assert.ok(select([file], "pr").commands.some((command) => command.includes("typecheck:unity")));
  }
  for (const files of [
    ["Generator~/Generator.cs"],
    ["Runtime/Core/Thing.cs", "Runtime/Assembly.asmdef"],
    ["Runtime/Analyzers/Analyzer.dll"]
  ]) {
    assert.ok(
      select(files, "iterate").commands.some((command) => command.includes("typecheck:unity"))
    );
  }
});
test("tooling iteration does not repeat PR aggregates", () => {
  const plan = select(["scripts/agent-check.js"], "iterate");
  assert.equal(plan.commands.length, 1);
  assert.ok(plan.commands[0].includes("scripts/agent-preflight.ps1"));
  assert.ok(plan.omitted.some((item) => item.includes("focused red/green")));
  assert.ok(
    select(["scripts/agent-check.js"], "pr").commands.some((c) => c.includes("validate:tests"))
  );
});
test("invalid tiers and escaping paths fail", () => {
  assert.throws(() => select([], "bad"));
  assert.throws(() => select(["../outside"], "pr"));
});
test("committed, deleted, renamed and untracked files remain selected", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-check-"));
  const git = (...args) => execFileSync("git", args, { cwd: root, encoding: "utf8" });
  try {
    git("init", "-q");
    git("config", "user.email", "test@example.com");
    git("config", "user.name", "Test");
    fs.writeFileSync(path.join(root, "old.cs"), "old");
    git("add", ".");
    git("commit", "-qm", "base");
    const base = git("rev-parse", "HEAD").trim();
    git("mv", "old.cs", "new.cs");
    git("commit", "-qm", "rename");
    fs.unlinkSync(path.join(root, "new.cs"));
    fs.writeFileSync(path.join(root, "untracked.cs"), "new");
    const files = changedFiles(base, root);
    for (const file of ["old.cs", "new.cs", "untracked.cs"]) assert.ok(files.includes(file), file);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("receipts invalidate on source, configuration, command, and tool changes", () => {
  const { fingerprint, reusable } = require("../agent-check");
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-receipt-"));
  try {
    execFileSync("git", ["init", "-q"], { cwd: root });
    fs.writeFileSync(path.join(root, "source.cs"), "before");
    execFileSync("git", ["add", "."], { cwd: root });
    execFileSync(
      "git",
      ["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-qm", "base"],
      { cwd: root }
    );
    const first = fingerprint(root, ["node", "check"], "tool-v1");
    assert.ok(reusable({ fingerprint: first, status: 0, stable: true }, first));
    assert.ok(!reusable({ fingerprint: first, status: 1, stable: true }, first));
    assert.ok(!reusable({ fingerprint: first, status: 0, stable: false }, first));
    fs.writeFileSync(path.join(root, "source.cs"), "after");
    assert.notEqual(fingerprint(root, ["node", "check"], "tool-v1"), first);
    fs.writeFileSync(path.join(root, "source.cs"), "before");
    fs.writeFileSync(path.join(root, "config.json"), "{}");
    assert.notEqual(fingerprint(root, ["node", "check"], "tool-v1"), first);
    fs.unlinkSync(path.join(root, "config.json"));
    assert.notEqual(fingerprint(root, ["node", "other"], "tool-v1"), first);
    assert.notEqual(fingerprint(root, ["node", "check"], "tool-v2"), first);
    execFileSync("git", ["update-index", "--assume-unchanged", "source.cs"], { cwd: root });
    assert.throws(() => fingerprint(root, ["node", "check"], "tool-v1"), /assume-unchanged/);
    execFileSync("git", ["update-index", "--no-assume-unchanged", "source.cs"], { cwd: root });
    fs.writeFileSync(path.join(root, "link"), "source.cs");
    const linkBlob = execFileSync("git", ["hash-object", "-w", "link"], {
      cwd: root,
      encoding: "utf8"
    }).trim();
    execFileSync("git", ["update-index", "--add", "--cacheinfo", `120000,${linkBlob},link`], {
      cwd: root
    });
    assert.throws(() => fingerprint(root, ["node", "check"], "tool-v1"), /tracked symlinks/);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("real command receipts reuse success and reject input mutation", () => {
  const { executeChecks } = require("../agent-check");
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-execution-"));
  const previousActions = process.env.GITHUB_ACTIONS;
  try {
    process.env.GITHUB_ACTIONS = "true";
    assert.throws(() => executeChecks({ commands: [] }, root, true), /CI must independently/);
    process.env.GITHUB_ACTIONS = "false";
    execFileSync("git", ["init", "-q"], { cwd: root });
    fs.writeFileSync(path.join(root, ".gitignore"), "progress/\n");
    const command = [process.execPath, "-e", "require('fs').appendFileSync('progress/count', 'x')"];
    const selection = { commands: [command] };
    assert.equal(executeChecks(selection, root, true), 0);
    assert.equal(executeChecks(selection, root, true), 0);
    assert.equal(fs.readFileSync(path.join(root, "progress/count"), "utf8"), "x");
    fs.writeFileSync(path.join(root, "source.cs"), "changed");
    assert.equal(executeChecks(selection, root, true), 0);
    assert.equal(fs.readFileSync(path.join(root, "progress/count"), "utf8"), "xx");
    assert.equal(
      executeChecks({ commands: [[process.execPath, "-e", "process.exit(7)"]] }, root, true),
      7
    );
    assert.throws(
      () =>
        executeChecks(
          {
            commands: [
              [process.execPath, "-e", "require('fs').writeFileSync('source.cs', 'mutated')"]
            ]
          },
          root,
          true
        ),
      /inputs changed/
    );
  } finally {
    if (previousActions === undefined) delete process.env.GITHUB_ACTIONS;
    else process.env.GITHUB_ACTIONS = previousActions;
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("receipt identity includes raw bytes hidden by Git line-ending normalization", () => {
  const { fingerprint } = require("../agent-check");
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-raw-input-"));
  const git = (...args) => execFileSync("git", args, { cwd: root, encoding: "utf8" });
  try {
    git("init", "-q");
    fs.writeFileSync(path.join(root, ".gitattributes"), "*.cs text eol=crlf\n");
    fs.writeFileSync(path.join(root, "source.cs"), "before\r\n");
    git("add", ".");
    git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-qm", "base");
    const before = fingerprint(root, ["check"], "tool");
    fs.writeFileSync(path.join(root, "source.cs"), "before\n");
    assert.equal(git("diff", "HEAD", "--"), "");
    assert.notEqual(fingerprint(root, ["check"], "tool"), before);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test("missing CLI values fail before repository discovery", () => {
  const { main } = require("../agent-check");
  for (const args of [["--base"], ["--tier"], ["--files"], ["--base", "--plan"]]) {
    assert.throws(() => main(args), /Missing/);
  }
});
