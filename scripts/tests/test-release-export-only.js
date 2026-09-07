#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..", "..");
const workflow = fs.readFileSync(path.join(root, ".github/workflows/release.yml"), "utf8");
const verification = workflow.match(
  /      - name: Verify tag matches package metadata\n[\s\S]*?        run: \|\n([\s\S]*?)(?=\n  release-ready:)/
);
assert.ok(verification, "Release metadata verification must have a runnable body");
const script = verification[1].replace(/^          /gm, "");
const temporary = fs.mkdtempSync(path.join(os.tmpdir(), "release-export-control-"));
try {
  const scriptPath = path.join(temporary, "verify.sh");
  const apiMarker = path.join(temporary, "api-called");
  fs.writeFileSync(scriptPath, script);
  fs.writeFileSync(
    path.join(temporary, "gh"),
    '#!/usr/bin/env bash\nprintf called > "$API_MARKER"\nexit 77\n',
    { mode: 0o755 }
  );
  const version = JSON.parse(fs.readFileSync(path.join(root, "package.json"), "utf8")).version;
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(([key]) => !key.startsWith("GIT_"))
  );
  for (const exportOnly of ["true", "false"]) {
    fs.rmSync(apiMarker, { force: true });
    const output = path.join(temporary, `outputs-${exportOnly}`);
    const result = spawnSync("bash", [scriptPath], {
      cwd: root,
      encoding: "utf8",
      timeout: 30000,
      env: {
        ...environment,
        PATH: `${temporary}${path.delimiter}${process.env.PATH}`,
        GITHUB_OUTPUT: output,
        RUNNER_TEMP: temporary,
        GITHUB_REPOSITORY: "fixture/repository",
        GH_TOKEN: "",
        API_MARKER: apiMarker,
        INPUT_VERSION: version,
        INPUT_SOURCE_REF: "candidate-branch",
        INPUT_ALLOW_TAG_RECOVERY: "false",
        INPUT_EXPORT_ONLY: exportOnly
      }
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, exportOnly === "true" ? 0 : 77, result.stderr + result.stdout);
    assert.equal(fs.existsSync(apiMarker), exportOnly !== "true");
    const outputs = fs.readFileSync(output, "utf8");
    assert.ok(outputs.includes(`package-version=${version}`));
    if (exportOnly === "true") {
      assert.match(outputs, /^source-sha=[a-f0-9]{40}$/m);
      assert.match(outputs, /^source-ref=candidate-branch$/m);
      assert.match(outputs, /^tag-action=none$/m);
    }
  }
} finally {
  fs.rmSync(temporary, { recursive: true, force: true });
}

for (const jobName of ["prepare-tag", "publish"]) {
  const job = workflow.match(
    new RegExp(`^  ${jobName}:\\n([\\s\\S]*?)(?=^  [a-z][a-z-]+:|(?![\\s\\S]))`, "m")
  );
  assert.ok(job, `Missing ${jobName}`);
  const condition = job[1].match(/    if: (?:>-\n)?\s*\$\{\{([\s\S]*?)\}\}/);
  assert.ok(condition, `Missing ${jobName} gate`);
  for (const exportOnly of [true, false]) {
    for (const tagAction of ["none", "create"]) {
      const expression = condition[1]
        .replace(/inputs\.export_only/g, String(exportOnly))
        .replace(/always\(\)/g, "true")
        .replace(/needs\.release-ready\.outputs\.tag-action/g, JSON.stringify(tagAction))
        .replace(/needs\.[a-z-]+\.result/g, '"success"');
      const allowed = vm.runInNewContext(expression, Object.create(null), { timeout: 100 });
      assert.equal(
        allowed,
        !exportOnly && (jobName === "publish" || tagAction !== "none"),
        `${jobName}, export=${exportOnly}, tag=${tagAction}`
      );
    }
  }
}
console.log(
  "Release export-only controls passed: real verification branches and both publication gates."
);
