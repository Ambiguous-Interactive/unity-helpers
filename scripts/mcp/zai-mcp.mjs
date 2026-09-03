#!/usr/bin/env node

import { spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

import { readLocalEnv } from "./unity-mcp.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const REMOTE_SERVERS = Object.freeze({
  "web-search": "https://api.z.ai/api/mcp/web_search_prime/mcp",
  "web-reader": "https://api.z.ai/api/mcp/web_reader/mcp",
  zread: "https://api.z.ai/api/mcp/zread/mcp"
});

function cleanCredential(value) {
  return typeof value === "string" ? value.trim() : "";
}

export function resolveZaiApiKey(repoRoot = REPO_ROOT, environment = process.env) {
  const fromEnvironment = cleanCredential(environment.Z_AI_API_KEY);
  if (fromEnvironment) {
    return fromEnvironment;
  }
  return cleanCredential(readLocalEnv(repoRoot).Z_AI_API_KEY);
}

export function createAuthorizationHeaderFile(apiKey, temporaryRoot = os.tmpdir()) {
  const directory = fs.mkdtempSync(path.join(temporaryRoot, "unity-helpers-zai-mcp-"));
  fs.chmodSync(directory, 0o700);
  const filePath = path.join(directory, "headers");
  fs.writeFileSync(filePath, `Authorization: Bearer ${apiKey}\n`, {
    encoding: "utf8",
    flag: "wx",
    mode: 0o600
  });
  fs.chmodSync(filePath, 0o600);
  return {
    directory,
    filePath,
    cleanup: () => fs.rmSync(directory, { recursive: true, force: true })
  };
}

export function launchDefinition(mode, apiKey, temporaryRoot = os.tmpdir()) {
  if (mode === "vision") {
    return {
      command: "zai-mcp-server",
      args: [],
      environment: { Z_AI_API_KEY: apiKey, Z_AI_MODE: "ZAI" },
      cleanup: () => {}
    };
  }

  const url = REMOTE_SERVERS[mode];
  if (!url) {
    throw new Error(`Unknown Z.AI MCP server: ${mode}`);
  }
  const header = createAuthorizationHeaderFile(apiKey, temporaryRoot);
  return {
    command: "mcp-remote",
    args: [url, "--header-file", header.filePath],
    environment: {},
    cleanup: header.cleanup
  };
}

function usage() {
  return [
    "Usage: node scripts/mcp/zai-mcp.mjs <vision|web-search|web-reader|zread>",
    "",
    "Set Z_AI_API_KEY in the environment or the repository's gitignored .env.local file."
  ].join("\n");
}

export async function main(argv = process.argv.slice(2)) {
  const [mode, ...unexpected] = argv;
  if (!mode || mode === "--help" || mode === "-h") {
    console.log(usage());
    return 0;
  }
  if (unexpected.length > 0) {
    throw new Error(`Unexpected argument: ${unexpected[0]}`);
  }

  const apiKey = resolveZaiApiKey();
  if (!apiKey) {
    console.error("zai-mcp: Z_AI_API_KEY is not configured; refusing to start unauthenticated.");
    console.error("zai-mcp: add Z_AI_API_KEY=<key> to .env.local, then restart the MCP client.");
    return 3;
  }

  const definition = launchDefinition(mode, apiKey);
  const child = spawn(definition.command, definition.args, {
    env: { ...process.env, ...definition.environment },
    stdio: "inherit"
  });
  const forwardedSignals = new Set();
  const forwardSignal = (signal) => {
    forwardedSignals.add(signal);
    child.kill(signal);
  };
  const signals = ["SIGINT", "SIGTERM"];
  const signalHandlers = new Map();
  for (const signal of signals) {
    const handler = forwardSignal.bind(undefined, signal);
    signalHandlers.set(signal, handler);
    process.on(signal, handler);
  }

  try {
    return await new Promise((resolve, reject) => {
      child.once("error", reject);
      child.once("exit", (code, signal) => {
        if (signal && forwardedSignals.has(signal)) {
          resolve(128 + os.constants.signals[signal]);
        } else {
          resolve(code ?? 1);
        }
      });
    });
  } finally {
    for (const signal of signals) {
      process.removeListener(signal, signalHandlers.get(signal));
    }
    definition.cleanup();
  }
}

const entry = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : "";
if (import.meta.url === entry) {
  main()
    .then((status) => {
      process.exitCode = status;
    })
    .catch((error) => {
      console.error(`zai-mcp: ${error.message}`);
      process.exitCode = error.code === "ENOENT" ? 127 : 1;
    });
}
