import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const root = fs.mkdtempSync(path.join(os.tmpdir(), "agent-skill-migration-"));

function run(script, mode, success = true) {
  const result = spawnSync(process.execPath, [path.join(root, "scripts", script), mode], {
    cwd: root,
    encoding: "utf8"
  });
  assert.equal(result.status === 0, success, `${script} ${mode}: ${result.stderr}`);
}

try {
  for (const directory of ["scripts", ".llm/skills", ".llm/references"]) {
    fs.mkdirSync(path.join(root, directory), { recursive: true });
  }
  fs.symlinkSync(path.join(repo, "node_modules"), path.join(root, "node_modules"), "dir");
  fs.copyFileSync(path.join(repo, ".prettierrc.json"), path.join(root, ".prettierrc.json"));
  for (const script of ["migrate-agent-skills.mjs", "generate-agent-skill-stubs.mjs"]) {
    fs.copyFileSync(path.join(repo, "scripts", script), path.join(root, "scripts", script));
  }

  const source = [
    "# Skill: Demo",
    "",
    "<!-- trigger: demo | Demo description | Core -->",
    "",
    "## First Section",
    "",
    ...Array.from({ length: 135 }, (_, index) => `Instruction ${index + 1}.`),
    "",
    "### Follow-up",
    "",
    ...Array.from({ length: 70 }, (_, index) => `Follow-up ${index + 1}.`),
    "",
    "## Second Section",
    "",
    "Last instruction.",
    ""
  ].join("\n");
  const skill = path.join(root, ".llm/skills/demo.md");
  fs.writeFileSync(skill, source);

  run("migrate-agent-skills.mjs", "--write");
  run("migrate-agent-skills.mjs", "--check");
  let router = fs.readFileSync(skill, "utf8");
  assert.match(router, /\[Read section\]\(\.\.\/references\/demo-part-1\.md#first-section\)/);
  assert.match(router, /\[Read section\]\(\.\.\/references\/demo-part-2\.md#second-section\)/);
  assert.match(router, /### \[Follow-up\]\(\.\.\/references\/demo-part-2\.md#follow-up\)/);
  const partTwo = path.join(root, ".llm/references/demo-part-2.md");
  assert.match(fs.readFileSync(partTwo, "utf8"), /^# demo - Part 2\n\n## Split Content\n\n### Follow-up/m);

  const partOne = path.join(root, ".llm/references/demo-part-1.md");
  fs.appendFileSync(partOne, "\nAdditional instruction.\n");
  run("migrate-agent-skills.mjs", "--check", false);
  run("migrate-agent-skills.mjs", "--refresh");
  run("migrate-agent-skills.mjs", "--check");
  const manifest = JSON.parse(fs.readFileSync(path.join(root, ".llm/skill-splits.json"), "utf8"));
  assert.ok(manifest.skills[0].routerSha256);
  router = fs.readFileSync(skill, "utf8");
  fs.appendFileSync(skill, "\nUntracked router instruction.\n");
  run("migrate-agent-skills.mjs", "--refresh", false);
  fs.writeFileSync(skill, router.replace("Demo description", "Updated demo description"));
  run("migrate-agent-skills.mjs", "--refresh");
  run("migrate-agent-skills.mjs", "--check");

  run("generate-agent-skill-stubs.mjs", "--write");
  run("generate-agent-skill-stubs.mjs", "--check");
  fs.renameSync(skill, path.join(root, ".llm/skills/renamed.md"));
  run("generate-agent-skill-stubs.mjs", "--check", false);
  run("generate-agent-skill-stubs.mjs", "--write");
  run("generate-agent-skill-stubs.mjs", "--check");
  for (const directory of [".agents", ".claude"]) {
    assert.equal(fs.existsSync(path.join(root, directory, "skills/demo")), false);
    assert.equal(fs.existsSync(path.join(root, directory, "skills/renamed/SKILL.md")), true);
  }
  process.stdout.write("Agent skill migration and discovery regression checks passed.\n");
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
