#!/usr/bin/env node

const { spawnSync } = require("node:child_process");
const { existsSync, readFileSync } = require("node:fs");
const path = require("node:path");

const toolMap = {
  prettier: { packageName: "prettier", binName: "prettier" },
  cspell: { packageName: "cspell", binName: "cspell" },
  markdownlint: { packageName: "markdownlint-cli", binName: "markdownlint" }
};

const requestedTool = process.argv[2];
if (!requestedTool || !toolMap[requestedTool]) {
  const tools = Object.keys(toolMap).join(", ");
  console.error(`Usage: node scripts/run-node-bin.js <${tools}> [args...]`);
  process.exit(2);
}

const repoRoot = path.resolve(__dirname, "..");
const { packageName, binName } = toolMap[requestedTool];
const packageJsonPath = path.join(repoRoot, "node_modules", packageName, "package.json");

if (!existsSync(packageJsonPath)) {
  console.error(
    `${requestedTool} is not installed in this repository. Run \`npm install\` on the same host that runs git hooks.`
  );
  process.exit(127);
}

let packageJson;
try {
  packageJson = JSON.parse(readFileSync(packageJsonPath, "utf8"));
} catch (error) {
  console.error(`Failed to read ${packageJsonPath}: ${error.message}`);
  process.exit(1);
}

const bin = packageJson.bin;
const relativeBinPath =
  typeof bin === "string" ? bin : bin && typeof bin === "object" ? bin[binName] : undefined;

if (!relativeBinPath) {
  console.error(`${packageName} does not declare a '${binName}' bin entry.`);
  process.exit(1);
}

const binPath = path.resolve(path.dirname(packageJsonPath), relativeBinPath);
if (!existsSync(binPath)) {
  console.error(`${requestedTool} bin entry does not exist at ${binPath}. Run \`npm install\`.`);
  process.exit(127);
}

// stdin is passed through, never read here. Every caller hands its file list to the tool in argv,
// and a tool that does want stdin gets the descriptor itself. Reading it first cost nothing and
// broke everything: touching `process.stdin` to decide whether to read makes Node set O_NONBLOCK on
// fd 0, and the `readFileSync(0)` on the next line then failed with EAGAIN whenever the data had not
// already arrived. That surfaced as a lint run reporting zero failures and exiting 1, which in a
// `&&` chain like validate:prepush means every later check silently does not run. See
// scripts/read-stdin-sync.js for the one place that genuinely has to read a non-blocking fd 0.
const result = spawnSync(process.execPath, [binPath, ...process.argv.slice(3)], {
  cwd: repoRoot,
  stdio: "inherit",
  windowsHide: true
});

if (result.error) {
  console.error(`Failed to launch ${requestedTool}: ${result.error.message}`);
  process.exit(1);
}

process.exit(result.status ?? 1);
