"use strict";
const { execFileSync, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
function focusedControlIds(files) {
  const checks = JSON.parse(
    fs.readFileSync(path.join(root, "scripts/contracts-checks.json"), "utf8")
  );
  if (!Array.isArray(checks)) throw new Error("Contract registry must be a list");
  const byId = new Map();
  const selected = new Set();
  for (const check of checks) {
    if (
      typeof check.id !== "string" ||
      !Array.isArray(check.inputs) ||
      check.inputs.some((input) => typeof input !== "string")
    )
      throw new Error("Invalid contract ownership metadata");
    byId.set(check.id, check);
    if (check.inputs.some((input) => !input.startsWith("@") && files.includes(input)))
      selected.add(check.id);
  }
  for (const id of selected) {
    const check = byId.get(id);
    if (!check || !Array.isArray(check.dependencies || []))
      throw new Error(`Invalid focused control dependency: ${id}`);
    for (const dependency of check.dependencies || []) selected.add(dependency);
  }
  return [...selected];
}
function changedFiles(base, cwd = root) {
  if (!base || base.startsWith("-")) throw new Error("An explicit base revision is required");
  const git = (...args) =>
    execFileSync("git", args, { cwd, encoding: "utf8", maxBuffer: 16777216 })
      .split("\0")
      .filter(Boolean);
  const revision = execFileSync("git", ["rev-parse", "--verify", `${base}^{commit}`], {
    cwd,
    encoding: "utf8"
  }).trim();
  return [
    ...new Set([
      ...git("diff", "--name-only", "--no-renames", "-z", `${revision}...HEAD`, "--"),
      ...git("diff", "--name-only", "--no-renames", "-z", "HEAD", "--"),
      ...git("ls-files", "--others", "--exclude-standard", "-z")
    ])
  ].sort();
}
function select(files, tier) {
  if (!["iterate", "pr", "full"].includes(tier)) throw new Error("Unknown validation tier");
  files = [
    ...new Set(
      files.map((file) => {
        const normalized = file.replaceAll("\\", "/").replace(/^\.\//, "");
        if (
          !normalized ||
          normalized.startsWith("/") ||
          normalized.split("/").some((p) => p === ".." || p === ".") ||
          /^[A-Za-z]:/.test(normalized) ||
          normalized.includes("\0")
        )
          throw new Error("Expected repository-relative paths");
        return normalized;
      })
    )
  ].sort();
  let unity = tier === "full" ? "full" : "none";
  const reasons = [];
  for (const file of files) {
    const docs = /^(docs\/|\.llm\/|\.agents\/)/.test(file)
      ? /\.(md|png|jpg|jpeg|svg)(\.meta)?$/.test(file)
      : /^(README|CHANGELOG|CONTRIBUTING|AGENTS)\.md$/.test(file);
    const code = /^(Runtime|Editor|Tests)\/.*\.cs$/.test(file);
    const risk = /serial|thread|pool|buffer|singleton|lifecycle|odin|integrations/i.test(file);
    const coverage =
      docs && !/\.(cs|dll|asmdef|props|targets)$/.test(file)
        ? "none"
        : code && !risk
          ? "latest"
          : "full";
    if (coverage === "full" || (coverage === "latest" && unity === "none")) unity = coverage;
    reasons.push({
      file,
      coverage,
      reason:
        coverage === "none"
          ? "documentation or instructions"
          : coverage === "latest"
            ? "ordinary C#"
            : "compatibility-sensitive or unknown input"
    });
  }
  const npm = (name) => ["npm", "run", name];
  let commands = [];
  const tooling = (file) =>
    /^(scripts|\.github|\.githooks|\.devcontainer|\.llm|\.agents)\//.test(file) ||
    file === "package.json" ||
    file === "cspell.json";
  const compiler =
    tier === "full" ||
    files.some(
      (file) =>
        /^(Runtime|Editor|Tests|Generator~|Samples~|Styles)\//.test(file) ||
        /^scripts\/(typecheck|verify-shipped-analyzers)/.test(file) ||
        (!tooling(file) && !/\.(md|png|jpg|svg)(\.meta)?$/.test(file))
    );
  if (tier === "full") {
    commands = [
      npm("lint:repo"),
      npm("validate:tests"),
      npm("typecheck:unity"),
      npm("typecheck:controls")
    ];
  } else if (files.length) {
    commands.push([
      "pwsh",
      "-NoProfile",
      "-File",
      "scripts/agent-preflight.ps1",
      "-Paths",
      ...files
    ]);
    if (compiler) {
      if (tier === "pr") commands.push(npm("typecheck:unity"));
      else {
        const scopes = new Set();
        for (const file of files) {
          if (file.startsWith("Tests/Editor/")) scopes.add("editor-tests");
          else if (file.startsWith("Tests/Runtime/") || file.startsWith("Tests/Core/"))
            scopes.add("tests");
          else if (file.startsWith("Editor/")) scopes.add("editor");
          else if (file.startsWith("Runtime/Integrations/")) scopes.add("integrations");
          else if (file.startsWith("Runtime/")) scopes.add("runtime");
        }
        const sourceOnly = files.every((file) =>
          /^(Runtime|Editor|Tests)\/.*\.cs(?:\.meta)?$/.test(file)
        );
        if (!scopes.size || (unity === "full" && !sourceOnly))
          commands.push(npm("typecheck:unity"));
        else
          for (const scope of scopes)
            commands.push(["npm", "run", "typecheck:iterate", "--", scope]);
      }
    }
    if (tier === "pr") {
      for (const name of ["lint:repo", "validate:tests"])
        commands.push(["npm", "run", name, "--", "--files", ...files]);
    } else {
      if (files.some((f) => f.endsWith(".md"))) commands.push(npm("lint:docs"));
      const controls = focusedControlIds(files);
      if (controls.length)
        commands.push(["node", "scripts/run-contract-tests.js", "--only", controls.join(",")]);
    }
  }
  return {
    tier,
    status: commands.length ? "selected" : "not-applicable",
    files,
    unity,
    reasons,
    commands,
    nativeExecution: "not performed by this runner; use the real Unity fixture runner or CI",
    omitted:
      tier === "full"
        ? []
        : [
            "unaffected checks",
            ...(tier === "iterate"
              ? ["unselected contract suites; retain the task's focused behavior witness"]
              : []),
            "full native compatibility matrix unless escalated"
          ]
  };
}
function main(args) {
  let tier = "iterate",
    base,
    plan = false,
    json = false,
    files = [];
  while (args.length) {
    const arg = args.shift();
    if (arg === "--tier" || arg === "--base") {
      if (!args.length || args[0].startsWith("--")) throw new Error(`Missing value for ${arg}`);
      if (arg === "--tier") tier = args.shift();
      else base = args.shift();
    } else if (arg === "--plan") plan = true;
    else if (arg === "--json") json = true;
    else if (arg === "--files") {
      if (!args.length || args[0].startsWith("--")) throw new Error("Missing paths for --files");
      while (args.length && !args[0].startsWith("--")) files.push(args.shift());
    } else throw new Error(`Unknown argument: ${arg}`);
  }
  if (!files.length) files = changedFiles(base || "origin/main");
  const selection = select(files, tier);
  console.log(json ? JSON.stringify(selection) : JSON.stringify(selection, null, 2));
  if (plan || selection.status === "not-applicable") return 0;
  return executeChecks(selection, root);
}
function executeChecks(selection, cwd = root) {
  for (const command of selection.commands) {
    const started = performance.now();
    let [executable, ...argv] = command;
    if (executable === "npm" && process.env.npm_execpath) {
      executable = process.execPath;
      argv = [process.env.npm_execpath, ...argv];
    }
    const result = spawnSync(executable, argv, { cwd, stdio: "inherit" });
    if (result.error) throw result.error;
    console.log(
      JSON.stringify({
        command,
        status: result.status,
        signal: result.signal,
        seconds: (performance.now() - started) / 1000
      })
    );
    if (result.status !== 0) return result.status || 1;
  }
  return 0;
}
module.exports = { select, changedFiles, executeChecks, main };
if (require.main === module) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
