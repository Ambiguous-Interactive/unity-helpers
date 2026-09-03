#!/usr/bin/env node

import { spawn, spawnSync } from "node:child_process";
import os from "node:os";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

import { readLocalEnv } from "./unity-mcp.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const DEFAULT_IMAGE = "ghcr.io/github/github-mcp-server:latest";

function cleanCredential(value) {
  return typeof value === "string" ? value.trim() : "";
}

function readCachedGithubToken(repoRoot) {
  const result = spawnSync("bash", [path.join(repoRoot, "scripts", "github-token.sh")], {
    encoding: "utf8",
    env: process.env
  });
  return result.status === 0 ? cleanCredential(result.stdout) : "";
}

export function resolveGithubToken(
  repoRoot = REPO_ROOT,
  environment = process.env,
  readCache = () => readCachedGithubToken(repoRoot)
) {
  const fromEnvironment = cleanCredential(environment.GITHUB_PERSONAL_ACCESS_TOKEN);
  if (fromEnvironment) {
    return fromEnvironment;
  }

  const fromLocalEnv = cleanCredential(readLocalEnv(repoRoot).GITHUB_PERSONAL_ACCESS_TOKEN);
  if (fromLocalEnv) {
    return fromLocalEnv;
  }

  return cleanCredential(readCache());
}

export async function main(argv = process.argv.slice(2)) {
  const token = resolveGithubToken();
  if (!token) {
    console.error(
      "github-mcp: GITHUB_PERSONAL_ACCESS_TOKEN is not configured; refusing to start unauthenticated."
    );
    console.error(
      "github-mcp: add it to .env.local, or run `npm run github:token:store`, then restart the MCP client."
    );
    return 3;
  }

  const image = cleanCredential(process.env.GITHUB_MCP_IMAGE) || DEFAULT_IMAGE;
  const child = spawn(
    "docker",
    ["run", "-i", "--rm", "-e", "GITHUB_PERSONAL_ACCESS_TOKEN", image, ...argv],
    {
      env: { ...process.env, GITHUB_PERSONAL_ACCESS_TOKEN: token },
      stdio: "inherit"
    }
  );
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
  }
}

const entry = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : "";
if (import.meta.url === entry) {
  main()
    .then((status) => {
      process.exitCode = status;
    })
    .catch((error) => {
      console.error(
        error.code === "ENOENT"
          ? "github-mcp: docker is not installed, so the GitHub MCP server cannot start."
          : `github-mcp: ${error.message}`
      );
      process.exitCode = error.code === "ENOENT" ? 127 : 1;
    });
}
