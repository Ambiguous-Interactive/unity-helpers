"use strict";
const { execFileSync, spawnSync } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");
const crypto = require("node:crypto");
const root = path.resolve(__dirname, "..");
function fingerprint(cwd, command, tools) {
  const hash = crypto.createHash("sha256");
  hash.update(JSON.stringify({ command, tools }));
  const git = (...args) => execFileSync("git", args, { cwd, maxBuffer: 67108864 });
  const flags = git("ls-files", "-v", "-z").toString("utf8").split("\0");
  if (flags.some((entry) => /^[a-zS]/.test(entry)))
    throw new Error("Reuse refuses assume-unchanged or sparse inputs");
  if (
    git("ls-files", "--stage", "-z")
      .toString("utf8")
      .split("\0")
      .some((entry) => /^(120000|160000) /.test(entry))
  )
    throw new Error("Reuse requires a repository without tracked symlinks or submodules");
  const head = spawnSync("git", ["rev-parse", "--verify", "HEAD^{tree}"], {
    cwd,
    encoding: "utf8"
  });
  hash.update(head.status === 0 ? head.stdout : "unborn");
  hash.update(
    git(
      "diff",
      "--binary",
      "--no-ext-diff",
      "--no-textconv",
      ...(head.status === 0 ? ["HEAD"] : []),
      "--"
    )
  );
  if (head.status !== 0)
    hash.update(git("diff", "--cached", "--binary", "--no-ext-diff", "--no-textconv", "--"));
  const deleted = new Set(git("ls-files", "--deleted", "-z").toString("utf8").split("\0"));
  const tracked = git("ls-files", "-z")
    .toString("utf8")
    .split("\0")
    .filter((file) => file && !deleted.has(file));
  if (tracked.length)
    hash.update(
      execFileSync("git", ["hash-object", "--no-filters", "--stdin-paths"], {
        cwd,
        input: `${tracked.map((file) => JSON.stringify(file)).join("\n")}\n`,
        maxBuffer: 67108864
      })
    );
  const files = git("ls-files", "--others", "--exclude-standard", "-z").toString("utf8");
  for (const file of [...new Set(files.split("\0").filter(Boolean))].sort()) {
    hash.update(JSON.stringify(file));
    const full = path.join(cwd, file);
    if (!fs.lstatSync(full).isFile()) throw new Error(`Cannot fingerprint non-file input: ${file}`);
    hash.update(crypto.createHash("sha256").update(fs.readFileSync(full)).digest());
  }
  return hash.digest("hex");
}
function reusable(receipt, identity) {
  return receipt?.fingerprint === identity && receipt.status === 0 && receipt.stable === true;
}
function toolIdentity() {
  const versions = {};
  for (const tool of ["dotnet", "uv", "pwsh", "git"]) {
    const result = spawnSync(tool, ["--version"], { encoding: "utf8" });
    versions[tool] = { status: result.status, output: result.stdout, error: result.error?.code };
  }
  return JSON.stringify({
    versions,
    node: process.versions,
    environment: process.env,
    platform: process.platform,
    architecture: process.arch
  });
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
          else if (file.startsWith("Runtime/")) scopes.add("runtime");
        }
        if (!scopes.size || unity === "full") commands.push(npm("typecheck:unity"));
        else
          for (const scope of scopes)
            commands.push(["npm", "run", "typecheck:iterate", "--", scope]);
      }
    }
    if (tier === "pr") {
      for (const name of ["lint:repo", "validate:tests"])
        commands.push(["npm", "run", name, "--", "--files", ...files]);
    } else if (files.some((f) => f.endsWith(".md"))) commands.push(npm("lint:docs"));
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
              ? ["contract suites; run the task's focused red/green witness separately"]
              : []),
            "full native compatibility matrix unless escalated"
          ]
  };
}
function main(args) {
  let tier = "iterate",
    base,
    plan = false,
    reuse = false,
    json = false,
    files = [];
  while (args.length) {
    const arg = args.shift();
    if (arg === "--tier" || arg === "--base") {
      if (!args.length || args[0].startsWith("--")) throw new Error(`Missing value for ${arg}`);
      if (arg === "--tier") tier = args.shift();
      else base = args.shift();
    } else if (arg === "--plan") plan = true;
    else if (arg === "--reuse") reuse = true;
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
  return executeChecks(selection, root, reuse);
}
function executeChecks(selection, cwd = root, reuse = false) {
  if (reuse && process.env.GITHUB_ACTIONS === "true")
    throw new Error("CI must independently execute validation; --reuse is local only");
  const tools = reuse ? toolIdentity() : null;
  const receiptRoot = path.join(cwd, "progress", ".artifacts", "agent-check");
  fs.mkdirSync(receiptRoot, { recursive: true });
  for (const command of selection.commands) {
    const identity = reuse ? fingerprint(cwd, command, tools) : null;
    const receiptPath = path.join(receiptRoot, `${identity}.json`);
    if (reuse && fs.existsSync(receiptPath)) {
      let receipt;
      try {
        receipt = JSON.parse(fs.readFileSync(receiptPath, "utf8"));
      } catch {
        receipt = null;
      }
      if (reusable(receipt, identity)) {
        console.log(`Reused local completion: ${JSON.stringify(command)}`);
        continue;
      }
    }
    const started = performance.now();
    let [executable, ...argv] = command;
    if (executable === "npm" && process.env.npm_execpath) {
      executable = process.execPath;
      argv = [process.env.npm_execpath, ...argv];
    }
    const result = spawnSync(executable, argv, { cwd, stdio: "inherit" });
    if (result.error) throw result.error;
    const stable = reuse && fingerprint(cwd, command, tools) === identity;
    const receipt = {
      command,
      fingerprint: identity,
      status: result.status,
      signal: result.signal,
      stable,
      seconds: (performance.now() - started) / 1000,
      completed: new Date().toISOString()
    };
    const temporary = `${receiptPath}.${process.pid}.tmp`;
    if (reuse) {
      fs.writeFileSync(temporary, JSON.stringify(receipt, null, 2));
      fs.renameSync(temporary, receiptPath);
    }
    if (result.status !== 0) return result.status || 1;
    if (reuse && !stable)
      throw new Error(
        "Validation inputs changed during execution; result cannot qualify this tree"
      );
  }
  return 0;
}
module.exports = { select, changedFiles, fingerprint, reusable, executeChecks, main };
if (require.main === module) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
