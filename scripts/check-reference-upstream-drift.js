#!/usr/bin/env node
"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const https = require("node:https");
const path = require("node:path");

const repoRoot = path.resolve(__dirname, "..");
const defaultManifestPath = path.join(__dirname, "reference-upstreams.json");
const hashPattern = /^[0-9a-f]{64}$/;
const commitPattern = /^[0-9a-f]{40}$/;

function parseArguments(argumentsList) {
  const parsed = { manifestPath: defaultManifestPath, reportPath: null };
  for (let index = 0; index < argumentsList.length; index += 1) {
    if (argumentsList[index] === "--manifest" && index + 1 < argumentsList.length) {
      parsed.manifestPath = path.resolve(argumentsList[(index += 1)]);
    } else if (argumentsList[index] === "--report" && index + 1 < argumentsList.length) {
      parsed.reportPath = path.resolve(argumentsList[(index += 1)]);
    } else {
      throw new Error(`Unknown or incomplete argument: ${argumentsList[index]}`);
    }
  }
  return parsed;
}

function validateManifest(manifest, root = repoRoot) {
  if (!manifest || !Array.isArray(manifest.implementations)) {
    throw new Error("The manifest must contain an implementations array.");
  }
  if (manifest.implementations.length < 2) {
    throw new Error("The manifest must cover at least one sort and one random implementation.");
  }

  const names = new Set();
  const kinds = new Set();
  for (const entry of manifest.implementations) {
    if (!entry || typeof entry.name !== "string" || entry.name.trim() === "") {
      throw new Error("Every implementation needs a name.");
    }
    if (names.has(entry.name)) {
      throw new Error(`Duplicate implementation name: ${entry.name}`);
    }
    names.add(entry.name);
    kinds.add(entry.kind);

    if (entry.kind !== "sort" && entry.kind !== "random") {
      throw new Error(`${entry.name} has unsupported kind '${entry.kind}'.`);
    }
    if (!Number.isInteger(entry.issue) || entry.issue < 1) {
      throw new Error(`${entry.name} needs a positive issue number.`);
    }
    if (!commitPattern.test(entry.auditedCommit || "")) {
      throw new Error(`${entry.name} needs a full lowercase audited commit SHA.`);
    }
    if (!hashPattern.test(entry.sha256 || "")) {
      throw new Error(`${entry.name} needs a lowercase SHA-256.`);
    }
    if (
      typeof entry.upstreamUrl !== "string" ||
      !entry.upstreamUrl.startsWith("https://raw.githubusercontent.com/")
    ) {
      throw new Error(`${entry.name} needs a raw.githubusercontent.com HTTPS URL.`);
    }
    if (typeof entry.localPath !== "string" || entry.localPath.trim() === "") {
      throw new Error(`${entry.name} needs a local source path.`);
    }
    const localPath = path.resolve(root, entry.localPath);
    if (!localPath.startsWith(root + path.sep) || !fs.existsSync(localPath)) {
      throw new Error(`${entry.name} local source does not exist: ${entry.localPath}`);
    }
  }

  if (!kinds.has("sort") || !kinds.has("random")) {
    throw new Error("The manifest must cover both sort and random implementations.");
  }
  return manifest.implementations;
}

function download(url, redirectCount = 0) {
  return new Promise((resolve, reject) => {
    const request = https.get(
      url,
      { headers: { "User-Agent": "unity-helpers-reference-audit" }, timeout: 30000 },
      (response) => {
        if (300 <= response.statusCode && response.statusCode < 400 && response.headers.location) {
          response.resume();
          if (5 <= redirectCount) {
            reject(new Error(`Too many redirects for ${url}`));
            return;
          }
          resolve(download(new URL(response.headers.location, url).toString(), redirectCount + 1));
          return;
        }
        if (response.statusCode !== 200) {
          response.resume();
          reject(new Error(`HTTP ${response.statusCode} for ${url}`));
          return;
        }

        const chunks = [];
        response.on("data", (chunk) => chunks.push(chunk));
        response.on("end", () => resolve(Buffer.concat(chunks)));
      }
    );
    request.on("timeout", () => request.destroy(new Error(`Timed out reading ${url}`)));
    request.on("error", reject);
  });
}

async function auditEntries(entries, loader = download) {
  const checks = await Promise.all(
    entries.map(async (entry) => {
      try {
        const bytes = await loader(entry.upstreamUrl, entry);
        const actualSha256 = crypto.createHash("sha256").update(bytes).digest("hex");
        return {
          name: entry.name,
          kind: entry.kind,
          issue: entry.issue,
          localPath: entry.localPath,
          upstreamUrl: entry.upstreamUrl,
          auditedCommit: entry.auditedCommit,
          expectedSha256: entry.sha256,
          actualSha256,
          status: actualSha256 === entry.sha256 ? "current" : "drifted"
        };
      } catch (error) {
        return {
          name: entry.name,
          kind: entry.kind,
          issue: entry.issue,
          localPath: entry.localPath,
          upstreamUrl: entry.upstreamUrl,
          auditedCommit: entry.auditedCommit,
          expectedSha256: entry.sha256,
          actualSha256: null,
          status: "unavailable",
          error: error instanceof Error ? error.message : String(error)
        };
      }
    })
  );
  return { checks, passed: checks.every((check) => check.status === "current") };
}

async function main() {
  const { manifestPath, reportPath } = parseArguments(process.argv.slice(2));
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  const entries = validateManifest(manifest);
  const report = await auditEntries(entries);

  for (const check of report.checks) {
    if (check.status === "current") {
      console.log(`[reference-upstreams] ${check.name}: current (${check.actualSha256})`);
    } else if (check.status === "drifted") {
      console.error(
        `[reference-upstreams] ${check.name}: upstream changed; expected ${check.expectedSha256}, received ${check.actualSha256}`
      );
    } else {
      console.error(`[reference-upstreams] ${check.name}: ${check.error}`);
    }
  }

  if (reportPath) {
    fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + "\n");
  }
  return report.passed ? 0 : 1;
}

module.exports = { auditEntries, parseArguments, validateManifest };

if (require.main === module) {
  main()
    .then((code) => {
      process.exitCode = code;
    })
    .catch((error) => {
      console.error(`[reference-upstreams] ${error instanceof Error ? error.message : error}`);
      process.exitCode = 1;
    });
}
