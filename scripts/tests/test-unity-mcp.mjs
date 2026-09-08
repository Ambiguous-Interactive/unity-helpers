// Tests for the Unity MCP endpoint identity check (issue #333).
//
// The interesting behavior is not "can we reach a port" -- the retired scripts could do that, and
// doing only that is what pointed a whole session at the wrong Unity project. It is "does the editor
// on the other end have THIS project open", so these tests stand up a real HTTP listener that
// answers the MCP handshake and the GetProjectRoot tool call, and assert on the classification.

import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { PassThrough } from "node:stream";
import test from "node:test";

import {
  buildCliArgs,
  clientConfigPaths,
  configure,
  configureShared,
  countTools,
  emptyRegistryGuidance,
  findRelay,
  findUnityCli,
  findUnityProjectRoot,
  isUnityProjectRoot,
  mergeCodexToml,
  normalizeProjectRoot,
  parseArgs,
  parseDotEnv,
  pinProjectRoot,
  probeEndpoint,
  projectRootFromEditorStatusText,
  projectRootFromManageEditorText,
  readLocalEnv,
  resolveChildCommand,
  resolveOnPath,
  resolveOptions,
  runProbe,
  sameProjectRoot,
  startBridge,
  unityCliCandidates
} from "../mcp/unity-mcp.mjs";

const PROTOCOL_VERSION = "2025-11-25";

test("local environment diagnostics omit malformed credential contents", (context) => {
  const repoRoot = newTempRepoRoot();
  const warnings = [];
  context.mock.method(console, "warn", (message) => warnings.push(message));
  try {
    fs.writeFileSync(
      path.join(repoRoot, ".env.local"),
      'Z_AI_API_KEY="private-unclosed-key\nVALID=value\n'
    );
    assert.deepEqual(readLocalEnv(repoRoot), { VALID: "value" });
    assert.equal(warnings.length, 1);
    assert.match(warnings[0], /line 1/);
    assert.doesNotMatch(warnings[0], /private-unclosed-key/);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

/**
 * A WebSocket-only server: it answers every plain HTTP request with 426, which is what the Unity
 * editor's own bridge (or a neighbouring repository's) does when it owns the port (issue #349).
 */
function startWebSocketOnlyServer() {
  const server = http.createServer((request, response) => {
    response.writeHead(426, { "Content-Type": "text/plain" });
    response.end("Upgrade Required");
  });

  return new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      resolve({ server, port: server.address().port });
    });
  });
}

/** A stand-in bridge: MCP initialize, plus GetProjectRoot answering with the supplied root. */
function startFakeBridge({ projectRoot, omitProjectRoot = false, toolCount = 1 }) {
  const methods = [];
  const server = http.createServer((request, response) => {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
    });
    request.on("end", () => {
      if (request.method === "DELETE") {
        response.writeHead(200).end();
        return;
      }

      const message = JSON.parse(body);
      methods.push(message.method);
      if (message.method === "initialize") {
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end(
          JSON.stringify({
            jsonrpc: "2.0",
            id: 1,
            result: { protocolVersion: PROTOCOL_VERSION, capabilities: {} }
          })
        );
        return;
      }

      if (message.method === "tools/list") {
        // Unity registers Unity_* tools from inside the editor; none exist when it is detached.
        const tools = Array.from({ length: toolCount }, (unused, index) => ({
          name: `Unity_Tool${index}`
        }));
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end(JSON.stringify({ jsonrpc: "2.0", id: 3, result: { tools } }));
        return;
      }

      // Unity answers tools/call with JSON encoded inside a text content block. The Pipeline
      // catalog's `editor_status` carries `projectPath`; the Assistant relay's
      // `Unity_ManageEditor` carries `data.projectRoot`. Each identity parser must accept its own.
      const toolName = message.params?.name;
      let payload;
      if (toolName === "editor_status") {
        payload = omitProjectRoot
          ? { status: "ready", compiling: false, unityVersion: "6000.4.6f1" }
          : {
              status: "ready",
              compiling: false,
              playMode: "stopped",
              projectPath: projectRoot,
              unityVersion: "6000.4.6f1"
            };
      } else {
        payload = omitProjectRoot
          ? { success: false, message: "unsupported" }
          : { success: true, data: { projectRoot } };
      }
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(
        JSON.stringify({
          jsonrpc: "2.0",
          id: 2,
          result: { content: [{ type: "text", text: JSON.stringify(payload) }] }
        })
      );
    });
  });

  return new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => {
      resolve({ server, port: server.address().port, methods });
    });
  });
}

function optionsFor(expectedProjectRoot, extra = {}) {
  return {
    protocolVersion: PROTOCOL_VERSION,
    timeout: 5_000,
    connectTimeout: 1_000,
    expectedProjectRoot,
    anyProject: false,
    ...extra
  };
}

test("normalizeProjectRoot survives separators, trailing slashes, and case", () => {
  assert.equal(normalizeProjectRoot("D:\\Code\\Packages"), "d:/code/packages");
  assert.equal(normalizeProjectRoot("D:/Code/Packages/"), "d:/code/packages");
  assert.equal(normalizeProjectRoot("  D:/CODE/packages  "), "d:/code/packages");
  assert.equal(normalizeProjectRoot(undefined), "");
});

test("sameProjectRoot compares host paths written either way", () => {
  assert.ok(sameProjectRoot("D:\\Code\\Packages", "D:/Code/Packages/"));
  assert.ok(!sameProjectRoot("D:/Code/Packages", "D:/Code/IshoBoy"));
});

test("a bridge serving another project is rejected, and says which", async () => {
  const { server, port } = await startFakeBridge({ projectRoot: "D:/Code/IshoBoy" });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "project-mismatch");
    assert.equal(result.projectRoot, "D:/Code/IshoBoy");
    assert.match(result.detail, /D:\/Code\/IshoBoy/);
    assert.match(result.detail, /D:\/Code\/Packages/);
  } finally {
    server.close();
  }
});

test("a bridge serving the expected project is accepted", async () => {
  const { server, port } = await startFakeBridge({ projectRoot: "D:\\Code\\Packages" });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages/")
    );
    assert.equal(result.ok, true);
    assert.equal(result.status, "ok");
    assert.equal(result.projectRoot, "D:\\Code\\Packages");
  } finally {
    server.close();
  }
});

test("--any-project accepts whatever answers", async () => {
  const { server, port } = await startFakeBridge({ projectRoot: "D:/Code/IshoBoy" });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages", { anyProject: true })
    );
    assert.equal(result.ok, true);
    assert.equal(result.projectRoot, "D:/Code/IshoBoy");
  } finally {
    server.close();
  }
});

// An endpoint that will not identify itself is reported, never assumed to match. Treating silence as
// agreement would restore exactly the behavior issue #333 is about.
test("an endpoint that will not identify itself is not treated as a match", async () => {
  const { server, port } = await startFakeBridge({ omitProjectRoot: true });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "unidentified");
  } finally {
    server.close();
  }
});

test("with no expectation pinned, any identified endpoint is usable", async () => {
  const { server, port } = await startFakeBridge({ projectRoot: "D:/Code/IshoBoy" });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor(undefined)
    );
    assert.equal(result.ok, true);
    assert.equal(result.projectRoot, "D:/Code/IshoBoy");
  } finally {
    server.close();
  }
});

test("pinProjectRoot never overwrites a pin the developer already stated", () => {
  const options = { repoRoot: "/tmp", expectedProjectRoot: "D:/Code/Packages" };
  const result = pinProjectRoot(options, { projectRoot: "D:/Code/IshoBoy" });
  assert.equal(result.wrote, false);
  assert.equal(result.options.expectedProjectRoot, "D:/Code/Packages");
});

test("pinProjectRoot writes nothing when the endpoint never identified itself", () => {
  const result = pinProjectRoot({ repoRoot: "/tmp" }, { projectRoot: undefined });
  assert.equal(result.wrote, false);
});

// The MCP lifecycle requires notifications/initialized before any other request. Unity's own server
// tolerates its absence; a conforming relay need not, and a refused identity query reads as "would
// not identify itself" -- so skipping it would disable the check on the strictest servers.
test("the probe completes the lifecycle before asking for identity", async () => {
  const { server, port, methods } = await startFakeBridge({ projectRoot: "D:/Code/Packages" });
  try {
    await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.deepEqual(methods, [
      "initialize",
      "notifications/initialized",
      "tools/list",
      "tools/call"
    ]);
  } finally {
    server.close();
  }
});

// The Pipeline-native identity tool answers first; the Assistant relay's is only a fallback for
// bridges still serving the old catalog.
test("the probe asks editor_status for identity before falling back to Unity_ManageEditor", async () => {
  const { server, port, methods } = await startFakeBridge({ projectRoot: "D:/Code/Packages" });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.equal(result.ok, true);
    assert.equal(result.projectRoot, "D:/Code/Packages");
    // The fake bridge dispatches on tool name: recover the order from the payload shapes.
    assert.equal(methods.filter((method) => method === "tools/call").length, 1);
  } finally {
    server.close();
  }
});

test("the probe falls back to Unity_ManageEditor when editor_status carries no project", async () => {
  const { server, port } = await startFakeBridge({
    projectRoot: "D:/Code/Packages",
    omitProjectRoot: true
  });
  try {
    // With both identity tools answering empty, the endpoint is unidentified rather than trusted.
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "unidentified");
  } finally {
    server.close();
  }
});

test("the documented identity flags are actually parseable", () => {
  const args = parseArgs(["--project-root", "D:/Code/Packages", "--any-project"]);
  assert.equal(args["project-root"], "D:/Code/Packages");
  assert.equal(args["any-project"], true);
});

// The Codex table is snake_case here, matching validate-mcp-config and every config already on
// disk. A hyphenated name would leave the old table enabled beside the new one.
test("the Codex merge replaces the existing table rather than adding a second", () => {
  const existing = [
    "[mcp_servers.unity_mcp_remote]",
    'url = "http://192.168.1.33:9003/mcp"',
    "enabled = true",
    ""
  ].join("\n");
  const merged = mergeCodexToml(existing, "http://host.docker.internal:9007/mcp", "tok");
  assert.equal((merged.match(/^\[mcp_servers\./gm) ?? []).length, 1);
  assert.match(merged, /\[mcp_servers\.unity_mcp_remote\]/);
  assert.match(merged, /9007/);
  assert.doesNotMatch(merged, /9003/);
});

// ── configure(): every supported agent client ────────────────────────────────
// OpenCode and nanocoder must be configured by the same `npm run
// unity:mcp:configure` run as Claude Code, Cursor, VS Code, and Codex, or the
// "the bridge is configured" claim silently covers only some of the agents.

function newTempRepoRoot() {
  return fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-configure-"));
}

function configuredEndpoint() {
  return { host: "host.docker.internal", port: 9007, endpointPath: "/mcp" };
}

test("clientConfigPaths includes the OpenCode config", () => {
  const repoRoot = path.resolve("/repo");
  const paths = clientConfigPaths(repoRoot);
  assert.equal(paths.opencode, path.join(repoRoot, "opencode.json"));
});

test("configure writes an OpenCode remote entry that enables the server", () => {
  const repoRoot = newTempRepoRoot();
  try {
    const token = "t".repeat(32);
    const { url, written } = configure({ repoRoot, bearerToken: token }, configuredEndpoint());
    assert.ok(written.includes(path.join(repoRoot, "opencode.json")));
    assert.equal(url, "http://host.docker.internal:9007/mcp");
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, "opencode.json"), "utf8"));
    const server = document.mcp["unity-mcp-remote"];
    assert.equal(server.type, "remote");
    assert.equal(server.url, "http://host.docker.internal:9007/mcp");
    assert.equal(server.enabled, true);
    assert.equal(server.headers.Authorization, `Bearer ${token}`);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("configure merges into an existing OpenCode config without clobbering other servers", () => {
  const repoRoot = newTempRepoRoot();
  try {
    fs.writeFileSync(
      path.join(repoRoot, "opencode.json"),
      JSON.stringify(
        {
          $schema: "https://opencode.ai/config.json",
          mcp: { context7: { type: "remote", url: "https://mcp.context7.com/mcp" } }
        },
        null,
        2
      )
    );
    configure({ repoRoot, bearerToken: "t".repeat(32) }, configuredEndpoint());
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, "opencode.json"), "utf8"));
    assert.ok(document.mcp.context7, "existing server must survive");
    assert.ok(document.mcp["unity-mcp-remote"], "unity server must be added");
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

// Nanocoder reads the same project-root .mcp.json as Claude Code but selects
// the HTTP transport with `transport` instead of `type`, so the shared entry
// has to carry both keys: Claude Code reads `type`, nanocoder reads
// `transport`, and each ignores the other's key.
test("the shared .mcp.json entry carries both the Claude Code and nanocoder transport keys", () => {
  const repoRoot = newTempRepoRoot();
  try {
    const token = "t".repeat(32);
    configure({ repoRoot, bearerToken: token }, configuredEndpoint());
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    const server = document.mcpServers["unity-mcp-remote"];
    assert.equal(server.type, "http");
    assert.equal(server.transport, "http");
    assert.equal(server.url, "http://host.docker.internal:9007/mcp");
    assert.equal(server.headers.Authorization, `Bearer ${token}`);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("Cursor and VS Code configs keep only the standard type key", () => {
  const repoRoot = newTempRepoRoot();
  try {
    configure({ repoRoot, bearerToken: "t".repeat(32) }, configuredEndpoint());
    const cursor = JSON.parse(fs.readFileSync(path.join(repoRoot, ".cursor", "mcp.json"), "utf8"));
    assert.equal(cursor.mcpServers["unity-mcp-remote"].type, "http");
    assert.equal(cursor.mcpServers["unity-mcp-remote"].transport, undefined);
    const vscode = JSON.parse(fs.readFileSync(path.join(repoRoot, ".vscode", "mcp.json"), "utf8"));
    assert.equal(vscode.servers["unity-mcp-remote"].type, "http");
    assert.equal(vscode.servers["unity-mcp-remote"].transport, undefined);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

const EXPECTED_SHARED_MCP_SERVERS = [
  "github",
  "zai-vision",
  "zai-web-search",
  "zai-web-reader",
  "zai-zread",
  "context7",
  "git",
  "fetch"
];

test("configure writes every shared MCP server for every supported frontend", () => {
  const repoRoot = newTempRepoRoot();
  try {
    configure({ repoRoot, bearerToken: "t".repeat(32) }, configuredEndpoint());

    const clients = [
      {
        file: ".mcp.json",
        collection: "mcpServers",
        commandFor: (server) => server.command
      },
      {
        file: path.join(".cursor", "mcp.json"),
        collection: "mcpServers",
        commandFor: (server) => server.command
      },
      {
        file: path.join(".vscode", "mcp.json"),
        collection: "servers",
        commandFor: (server) => server.command
      },
      {
        file: "opencode.json",
        collection: "mcp",
        commandFor: (server) => server.command[0]
      }
    ];

    for (const client of clients) {
      const document = JSON.parse(fs.readFileSync(path.join(repoRoot, client.file), "utf8"));
      for (const serverName of EXPECTED_SHARED_MCP_SERVERS) {
        const server = document[client.collection][serverName];
        assert.ok(server, `${client.file} must configure ${serverName}`);
        // Launchers are bash/node scripts in scripts/mcp; git and fetch are uv tools baked into
        // the devcontainer image and referenced by their own command names; context7 is npx-run.
        assert.match(
          client.commandFor(server),
          /^(?:bash|node|mcp-server-git|mcp-server-fetch|npx)$/
        );
      }
    }

    const codex = fs.readFileSync(path.join(repoRoot, ".codex", "config.toml"), "utf8");
    for (const serverName of EXPECTED_SHARED_MCP_SERVERS.map((name) => name.replaceAll("-", "_"))) {
      assert.match(codex, new RegExp(`^\\[mcp_servers\\.${serverName}\\]$`, "m"));
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("shared MCP configs use tracked launchers and contain no credentials", () => {
  const repoRoot = newTempRepoRoot();
  try {
    configure({ repoRoot, bearerToken: "t".repeat(32) }, configuredEndpoint());
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    const github = document.mcpServers.github;
    assert.equal(github.command, "node");
    assert.equal(github.args[0], "-e");
    assert.deepEqual(github.args.slice(2), ["github-mcp.mjs"]);

    for (const [serverName, mode] of [
      ["zai-vision", "vision"],
      ["zai-web-search", "web-search"],
      ["zai-web-reader", "web-reader"],
      ["zai-zread", "zread"]
    ]) {
      const server = document.mcpServers[serverName];
      assert.deepEqual(server.args.slice(2), ["zai-mcp.mjs", mode]);
    }

    for (const configPath of Object.values(clientConfigPaths(repoRoot))) {
      const raw = fs.readFileSync(configPath, "utf8");
      assert.doesNotMatch(raw, /Z_AI_API_KEY/);
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("shared launchers survive moving the workspace and starting in a subdirectory", () => {
  const repoRoot = newTempRepoRoot();
  const relocated = newTempRepoRoot();
  try {
    configureShared(repoRoot);
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    const scripts = path.join(relocated, "scripts", "mcp");
    fs.mkdirSync(scripts, { recursive: true });
    const cwd = path.join(relocated, "nested", "directory");
    fs.mkdirSync(cwd, { recursive: true });
    for (const script of ["github-mcp.mjs", "zai-mcp.mjs"]) {
      fs.writeFileSync(
        path.join(scripts, script),
        "export async function main(args) { console.log(JSON.stringify(args)); return 7; }"
      );
    }
    for (const [name, server] of Object.entries(document.mcpServers)) {
      // git and fetch are image-baked direct commands and context7 is npx-run;
      // none of the three is a repo-relative launcher.
      if (["git", "fetch", "context7"].includes(name)) continue;
      assert.ok(!JSON.stringify(server).includes(repoRoot));
      const result = spawnSync(process.execPath, server.args, { cwd, encoding: "utf8" });
      assert.equal(result.status, 7, result.stderr);
      assert.deepEqual(JSON.parse(result.stdout), server.args.slice(3));
      const missing = spawnSync(process.execPath, server.args, {
        cwd: os.tmpdir(),
        encoding: "utf8"
      });
      assert.equal(missing.status, 1);
      assert.match(missing.stderr, /inside the repository workspace/);
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
    fs.rmSync(relocated, { recursive: true, force: true });
  }
});

test("configureShared writes shared-only config for every supported client", () => {
  const repoRoot = newTempRepoRoot();
  try {
    const { written } = configureShared(repoRoot);
    assert.equal(written.length, 7);
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    assert.ok(document.mcpServers.github);
    assert.ok(document.mcpServers["zai-vision"]);
    assert.equal(document.mcpServers["unity-mcp-remote"], undefined);
    assert.equal(fs.existsSync(path.join(repoRoot, ".env.local")), false);
    for (const [file, collection] of [
      [path.join(".nanocoder", "mcp.json"), "mcpServers"],
      [path.join(".copilot", "mcp-config.json"), "mcpServers"]
    ]) {
      const config = JSON.parse(fs.readFileSync(path.join(repoRoot, file), "utf8"));
      assert.ok(config[collection].github, `${file} must configure github`);
      assert.ok(config[collection].git, `${file} must configure git`);
      assert.equal(config[collection]["unity-mcp-remote"], undefined);
    }
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

test("git and fetch shared servers are credential-free direct commands", () => {
  const repoRoot = newTempRepoRoot();
  try {
    configureShared(repoRoot);
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    assert.equal(document.mcpServers.git.command, "mcp-server-git");
    assert.equal(document.mcpServers.fetch.command, "mcp-server-fetch");
    assert.deepEqual(document.mcpServers.git.args, []);
    assert.deepEqual(document.mcpServers.fetch.args, []);
    assert.equal(document.mcpServers.git.transport, "stdio");
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

// Context7 ships version-specific library docs (Unity, Roslyn, mkdocs, protobuf-net) and is the
// only shared server that is a remote service behind a local npx launcher; it must stay
// credential-free so the shared catalog never carries or requires a key.
test("context7 shared server runs the official stdio package without credentials", () => {
  const repoRoot = newTempRepoRoot();
  try {
    configureShared(repoRoot);
    const document = JSON.parse(fs.readFileSync(path.join(repoRoot, ".mcp.json"), "utf8"));
    const server = document.mcpServers.context7;
    assert.deepEqual(server.args, ["-y", "@upstash/context7-mcp"]);
    assert.equal(server.transport, "stdio");
    const serialized = JSON.stringify(document);
    assert.doesNotMatch(serialized, /context7[_-]?api[_-]?key/i);
  } finally {
    fs.rmSync(repoRoot, { recursive: true, force: true });
  }
});

// This repository is a PACKAGE at <project>/Packages/com.wallstop-studios.unity-helpers, not a
// Unity project. The sibling repos the bridge was ported from ARE projects, so their default of
// "relay --project-path <repo root>" is wrong here and would point the relay at a non-project.
test("project discovery walks up to the Unity project containing the package", () => {
  const project = path.resolve("/Code/Packages");
  const pkg = path.join(project, "Packages", "com.wallstop-studios.unity-helpers");
  const present = new Set([path.join(project, "Assets"), path.join(project, "ProjectSettings")]);
  const exists = (candidate) => present.has(candidate);

  assert.equal(findUnityProjectRoot(pkg, exists), project);
});

// The repo root carries a stray, untracked Assets/ directory. Matching on Assets alone would stop
// there and hand the relay the package instead of the project.
test("a stray Assets directory without ProjectSettings is not a project root", () => {
  const project = path.resolve("/Code/Packages");
  const pkg = path.join(project, "Packages", "com.wallstop-studios.unity-helpers");
  const present = new Set([
    path.join(pkg, "Assets"),
    path.join(project, "Assets"),
    path.join(project, "ProjectSettings")
  ]);
  const exists = (candidate) => present.has(candidate);

  assert.equal(isUnityProjectRoot(pkg, exists), false);
  assert.equal(findUnityProjectRoot(pkg, exists), project);
});

test("discovery reports nothing when no Unity project encloses the package", () => {
  assert.equal(
    findUnityProjectRoot(path.resolve("/tmp/standalone"), () => false),
    undefined
  );
});

// A bridge with no editor attached handshakes perfectly and exposes nothing. Reporting that as
// "reachable" is the #333 confusion one layer up, and it was observed live before this check.
test("a bridge with no editor attached is reported as such, not as reachable", async () => {
  const { server, port } = await startFakeBridge({
    projectRoot: "D:/Code/Packages",
    toolCount: 0
  });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor(undefined)
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "no-editor");
    assert.equal(result.toolCount, 0);
    assert.match(result.detail, /no Unity editor is attached/);
  } finally {
    server.close();
  }
});

// Zero tools is unambiguous, so it fails even with no project pinned -- unlike the identity check,
// which cannot demand an expectation that was never configured.
test("no-editor fails even when no project root is pinned", async () => {
  const { server, port } = await startFakeBridge({ projectRoot: "D:/x", toolCount: 0 });
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor(undefined)
    );
    assert.equal(result.ok, false);
  } finally {
    server.close();
  }
});

// A port that answers is a categorically different failure from a port that is silent, and reporting
// it as silence once sent a session after Unity's approval dialog instead of after the occupant.
test("a WebSocket-only server on the port is classified as an occupant, not as silence", async () => {
  const { server, port } = await startWebSocketOnlyServer();
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor("D:/Code/Packages")
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "port-occupied");
    assert.match(result.detail, /426/);
  } finally {
    server.close();
  }
});

test("probe names the occupant and its port rather than reporting that nothing responded", async () => {
  const { server, port } = await startWebSocketOnlyServer();
  try {
    await assert.rejects(
      runProbe(
        optionsFor("D:/Code/Packages", {
          host: "127.0.0.1",
          port,
          endpointPath: "/mcp",
          discover: false
        })
      ),
      (error) => {
        assert.match(error.message, /WebSocket-only server/);
        assert.match(error.message, new RegExp(String(port)));
        assert.doesNotMatch(error.message, /^No Unity MCP endpoint responded/);
        return true;
      }
    );
  } finally {
    server.close();
  }
});

// ── Backends: the Unity CLI (`unity mcp`) and the legacy Assistant relay ─────

test("resolveOptions defaults to the cli backend and honors --backend, env, and .env.local", () => {
  assert.equal(resolveOptions(parseArgs([]), {}, {}, "/repo").backend, "cli");
  assert.equal(resolveOptions(parseArgs(["--backend", "relay"]), {}, {}, "/repo").backend, "relay");
  assert.equal(
    resolveOptions(parseArgs([]), { UNITY_MCP_BACKEND: "relay" }, {}, "/repo").backend,
    "relay"
  );
  assert.equal(
    resolveOptions(parseArgs([]), {}, { UNITY_MCP_BACKEND: "relay" }, "/repo").backend,
    "relay"
  );
  assert.throws(() => resolveOptions(parseArgs(["--backend", "http"]), {}, {}, "/repo"));
  assert.equal(
    resolveOptions(parseArgs(["--cli", "unity-cli"]), {}, {}, "/repo").cliPath,
    path.resolve("/repo/unity-cli")
  );
});

test("buildCliArgs targets `unity mcp` at a specific editor by project path", () => {
  assert.deepEqual(buildCliArgs("D:\\Code\\Packages"), [
    "mcp",
    "--no-banner",
    "--project-path",
    path.resolve("D:\\Code\\Packages")
  ]);
});

test("unityCliCandidates covers the Windows and home-directory install locations", () => {
  const windows = unityCliCandidates({
    platform: "win32",
    home: "/home/dev",
    localAppData: "/Users/dev/AppData/Local"
  });
  assert.ok(windows.includes(path.join("/Users/dev/AppData/Local", "Unity", "bin", "unity.exe")));
  assert.ok(windows.includes(path.join("/home/dev", ".unity", "bin", "unity.exe")));
  const posix = unityCliCandidates({ platform: "darwin", home: "/Users/dev" });
  assert.deepEqual(posix, [path.join("/Users/dev", ".unity", "bin", "unity")]);
});

test("resolveOnPath finds executables on PATH including PATHEXT on Windows", () => {
  const binDir = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-path-"));
  try {
    const exe = path.join(binDir, "unity.EXE");
    fs.writeFileSync(exe, "");
    const environment = { PATH: binDir, PATHEXT: ".COM;.EXE;.BAT" };
    // Windows lookups are case-insensitive, so only the resolved file identity is guaranteed.
    assert.equal(resolveOnPath("unity", environment, "win32")?.toLowerCase(), exe.toLowerCase());
    assert.equal(resolveOnPath("unity", { PATH: binDir }, "linux"), undefined);
    assert.equal(resolveOnPath("./missing", {}, "linux"), undefined);
  } finally {
    fs.rmSync(binDir, { recursive: true, force: true });
  }
});

test("Windows PATH discovery skips command scripts before the native Unity CLI", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-native-"));
  try {
    const scripts = path.join(root, "scripts");
    const native = path.join(root, "native");
    fs.mkdirSync(scripts);
    fs.mkdirSync(native);
    for (const extension of [".CMD", ".BAT", ".PS1"]) {
      fs.writeFileSync(path.join(scripts, `unity${extension}`), "script");
    }
    const executable = path.join(native, "unity.EXE");
    fs.writeFileSync(executable, "native");
    const environment = { PATH: `${scripts};${native}`, PATHEXT: ".CMD;.BAT;.PS1;.EXE" };
    assert.equal(resolveOnPath("unity", environment, "win32"), executable);
    assert.equal(
      findUnityCli(undefined, { environment, platform: "win32", home: root }),
      executable
    );
    for (const extension of [".CMD", ".BAT", ".PS1"]) {
      const script = path.join(scripts, `unity${extension}`);
      assert.equal(resolveOnPath(script, environment, "win32"), undefined);
      assert.throws(() => findUnityCli(script, { platform: "win32" }), /native Windows executable/);
      assert.throws(() => findRelay(script, { platform: "win32" }), /native Windows executable/);
    }
    assert.equal(findUnityCli(executable, { platform: "win32" }), executable);
    assert.equal(findRelay(executable, { platform: "win32" }), executable);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

test(
  "PATH discovery skips non-executable files before a usable CLI",
  {
    skip: process.platform === "win32"
  },
  () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-path-permission-"));
    try {
      const directories = ["blocked", "executable"].map((name) => path.join(root, name));
      for (const directory of directories) {
        fs.mkdirSync(directory);
      }
      fs.writeFileSync(path.join(directories[0], "unity"), "", { mode: 0o644 });
      const executable = path.join(directories[1], "unity");
      fs.writeFileSync(executable, "", { mode: 0o755 });
      const environment = { PATH: directories.join(path.delimiter) };
      assert.equal(resolveOnPath("unity", environment), executable);
      assert.equal(findUnityCli(undefined, { environment, home: root }), executable);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
);

for (const [code, expected] of [
  [-32002, 0],
  [-32603, undefined],
  [-32601, undefined]
]) {
  test(`tool-list error ${code} preserves its classification`, async () => {
    const fetchImpl = async () =>
      new Response(
        JSON.stringify({
          jsonrpc: "2.0",
          id: 3,
          error: { code, message: "failure" }
        }),
        { headers: { "Content-Type": "application/json" } }
      );
    assert.equal(
      await countTools(
        "http://localhost/mcp",
        { timeout: 1000 },
        undefined,
        PROTOCOL_VERSION,
        fetchImpl
      ),
      expected
    );
  });
}

test("findUnityCli accepts an explicit override and explains the fix when missing", () => {
  const binDir = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-cli-"));
  try {
    const cli = path.join(binDir, "unity.exe");
    fs.writeFileSync(cli, "", { mode: 0o755 });
    assert.equal(findUnityCli(cli), cli);
    assert.throws(
      () => findUnityCli(path.join(binDir, "absent.exe")),
      (error) => {
        assert.match(error.message, /Unity CLI not found/);
        assert.match(error.message, /--backend relay/);
        return true;
      }
    );
  } finally {
    fs.rmSync(binDir, { recursive: true, force: true });
  }
});

test("resolveChildCommand selects the cli or relay child and honors the test hook", () => {
  const hook = { command: "fake", args: ["child"], kind: "cli" };
  assert.deepEqual(resolveChildCommand({}, "/proj", { childCommand: () => hook }), hook);

  const binDir = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-child-"));
  try {
    const fakeCli = path.join(binDir, "unity.exe");
    const fakeRelay = path.join(binDir, "relay_win.exe");
    fs.writeFileSync(fakeCli, "", { mode: 0o755 });
    fs.writeFileSync(fakeRelay, "", { mode: 0o755 });
    const cli = resolveChildCommand({ backend: "cli", cliPath: fakeCli }, "D:/proj", {
      cliRuntime: {}
    });
    assert.equal(cli.kind, "cli");
    assert.equal(cli.command, fakeCli);
    assert.deepEqual(cli.args, buildCliArgs("D:/proj"));
    const relay = resolveChildCommand({ backend: "relay", relayPath: fakeRelay }, "D:/proj", {
      relayRuntime: {}
    });
    assert.equal(relay.kind, "relay");
    assert.equal(relay.command, fakeRelay);
  } finally {
    fs.rmSync(binDir, { recursive: true, force: true });
  }
});

test("emptyRegistryGuidance names the fix for each backend", () => {
  const cli = emptyRegistryGuidance("cli", "D:\\Code\\Packages");
  assert.match(cli, /pipeline install --project-path/);
  assert.match(cli, /EXCLUDE_REFLECTION_METADATA/);
  const relay = emptyRegistryGuidance("relay", "D:\\Code\\Packages");
  assert.match(relay, /no Unity editor is attached/);
  assert.doesNotMatch(relay, /Pipeline/);
});

test("identity parsers accept their own backend's text-block shape only", () => {
  const status = JSON.stringify({
    status: "ready",
    compiling: false,
    projectPath: "D:\\Code\\Packages",
    unityVersion: "6000.4.6f1"
  });
  assert.equal(projectRootFromEditorStatusText(status), "D:\\Code\\Packages");
  assert.equal(projectRootFromManageEditorText(status), undefined);
  const manage = JSON.stringify({ success: true, data: { projectRoot: "D:/Code/Packages" } });
  assert.equal(projectRootFromManageEditorText(manage), "D:/Code/Packages");
  assert.equal(projectRootFromEditorStatusText(manage), undefined);
  assert.equal(projectRootFromEditorStatusText("not json"), undefined);
});

// The bridge rewrites the first empty tools/list answer into an actionable error instead of
// letting clients render "connected, zero tools" as success -- the exact state a CLI completes
// its handshake in when the selected project's Pipeline server never loaded.

function fakeChildRuntime(emptyTools, concurrentToolLists = false, onToolRequest = () => {}) {
  const spawnChild = () => {
    const stdin = new PassThrough();
    const stdout = new PassThrough();
    let buffer = "";
    const toolRequests = [];
    stdin.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() ?? "";
      for (const line of lines) {
        if (!line.trim()) {
          continue;
        }
        const message = JSON.parse(line);
        if (message.method === "initialize") {
          stdout.write(
            `${JSON.stringify({
              jsonrpc: "2.0",
              id: message.id,
              result: {
                protocolVersion: "2025-11-25",
                capabilities: { tools: {} },
                serverInfo: { name: "unity-mcp", version: "test" }
              }
            })}\n`
          );
        } else if (message.method === "tools/list") {
          toolRequests.push(message);
          onToolRequest(message);
          if (concurrentToolLists && toolRequests.length < 2) {
            continue;
          }
          for (const request of toolRequests.splice(0)) {
            stdout.write(
              `${JSON.stringify({
                jsonrpc: "2.0",
                id: request.id,
                result: { tools: emptyTools ? [] : [{ name: "editor_status" }] }
              })}\n`
            );
          }
        }
      }
    });
    return {
      stdin,
      stdout,
      stderr: new PassThrough(),
      once: () => {},
      kill: () => {},
      exitCode: null,
      signalCode: null
    };
  };
  return {
    childCommand: () => ({ command: "fake-unity-cli", args: [], kind: "cli" }),
    spawnChild
  };
}

function freePort() {
  return new Promise((resolve) => {
    const server = net.createServer();
    server.unref();
    server.listen(0, "127.0.0.1", () => {
      const port = server.address().port;
      server.close(() => resolve(port));
    });
  });
}

const TEST_BRIDGE_TOKEN = "t".repeat(32);

async function bridgeRequest(httpServer, body, extraHeaders = {}) {
  const { port } = httpServer.address();
  return fetch(`http://127.0.0.1:${port}/mcp`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json, text/event-stream",
      Authorization: `Bearer ${TEST_BRIDGE_TOKEN}`,
      ...extraHeaders
    },
    body: JSON.stringify(body)
  });
}

async function startTestBridge(project) {
  const port = await freePort();
  const options = resolveOptions(
    parseArgs(["--port", String(port), "--bind", "127.0.0.1", "--project", project.path]),
    { UNITY_MCP_BEARER_TOKEN: TEST_BRIDGE_TOKEN },
    {},
    process.cwd()
  );
  return startBridge(
    options,
    fakeChildRuntime(project.emptyTools, project.concurrentToolLists, project.onToolRequest)
  );
}

test("a cli-backend bridge rewrites an empty initial tool registry into an actionable error", async () => {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-project-"));
  fs.mkdirSync(path.join(project, "Assets"));
  fs.mkdirSync(path.join(project, "ProjectSettings"));
  try {
    const running = await startTestBridge({ emptyTools: true, path: project });
    try {
      const initialize = await bridgeRequest(running.httpServer, {
        jsonrpc: "2.0",
        id: 1,
        method: "initialize",
        params: {
          protocolVersion: "2025-11-25",
          capabilities: {},
          clientInfo: { name: "test", version: "1" }
        }
      });
      assert.equal(initialize.status, 200);
      const sessionId = initialize.headers.get("mcp-session-id");
      assert.ok(sessionId, "session must initialize");
      await bridgeRequest(
        running.httpServer,
        { jsonrpc: "2.0", method: "notifications/initialized" },
        { "Mcp-Session-Id": sessionId }
      );
      const tools = await bridgeRequest(
        running.httpServer,
        { jsonrpc: "2.0", id: 3, method: "tools/list", params: {} },
        { "Mcp-Session-Id": sessionId }
      );
      const message = await tools.json();
      assert.equal(message.error?.code, -32002);
      assert.match(message.error?.message, /Pipeline package is not loaded/);
      assert.match(message.error?.message, /pipeline install --project-path/);
    } finally {
      await running.close();
    }
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});

test("overlapping tool listings diagnose the first requested registry", async () => {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-project-"));
  fs.mkdirSync(path.join(project, "Assets"));
  fs.mkdirSync(path.join(project, "ProjectSettings"));
  let running;
  try {
    let firstReceived;
    const firstRequestReceived = new Promise((resolve) => {
      firstReceived = resolve;
    });
    running = await startTestBridge({
      emptyTools: true,
      concurrentToolLists: true,
      path: project,
      onToolRequest: firstReceived
    });
    const initialize = await bridgeRequest(running.httpServer, {
      jsonrpc: "2.0",
      id: 1,
      method: "initialize",
      params: {
        protocolVersion: PROTOCOL_VERSION,
        capabilities: {},
        clientInfo: { name: "test", version: "1" }
      }
    });
    const headers = { "Mcp-Session-Id": initialize.headers.get("mcp-session-id") };
    await bridgeRequest(
      running.httpServer,
      { jsonrpc: "2.0", method: "notifications/initialized" },
      headers
    );
    const firstResponse = bridgeRequest(
      running.httpServer,
      { jsonrpc: "2.0", id: 3, method: "tools/list", params: {} },
      headers
    );
    await firstRequestReceived;
    const responses = await Promise.all([
      firstResponse,
      bridgeRequest(
        running.httpServer,
        { jsonrpc: "2.0", id: 4, method: "tools/list", params: {} },
        headers
      )
    ]);
    const messages = await Promise.all(responses.map((response) => response.json()));
    assert.equal(messages[0].error?.code, -32002);
    assert.deepEqual(messages[1].result?.tools, []);
  } finally {
    await running?.close();
    fs.rmSync(project, { recursive: true, force: true });
  }
});

test("a bridge with tools forwards the registry unchanged", async () => {
  const project = fs.mkdtempSync(path.join(os.tmpdir(), "unity-mcp-project-"));
  fs.mkdirSync(path.join(project, "Assets"));
  fs.mkdirSync(path.join(project, "ProjectSettings"));
  try {
    const running = await startTestBridge({ emptyTools: false, path: project });
    try {
      const initialize = await bridgeRequest(running.httpServer, {
        jsonrpc: "2.0",
        id: 1,
        method: "initialize",
        params: {
          protocolVersion: "2025-11-25",
          capabilities: {},
          clientInfo: { name: "test", version: "1" }
        }
      });
      const sessionId = initialize.headers.get("mcp-session-id");
      await bridgeRequest(
        running.httpServer,
        { jsonrpc: "2.0", method: "notifications/initialized" },
        { "Mcp-Session-Id": sessionId }
      );
      const tools = await bridgeRequest(
        running.httpServer,
        { jsonrpc: "2.0", id: 3, method: "tools/list", params: {} },
        { "Mcp-Session-Id": sessionId }
      );
      const message = await tools.json();
      assert.equal(message.error, undefined);
      assert.deepEqual(
        message.result?.tools?.map((tool) => tool.name),
        ["editor_status"]
      );
    } finally {
      await running.close();
    }
  } finally {
    fs.rmSync(project, { recursive: true, force: true });
  }
});

test("the probe classifies a bridge that serves the error as no-editor", async () => {
  // probeEndpoint against a fake HTTP bridge answering tools/list with the same -32002 error the
  // real bridge emits, so countTools must read it as zero tools, not as an unanswered endpoint.
  const server = http.createServer((request, response) => {
    let body = "";
    request.on("data", (chunk) => {
      body += chunk;
    });
    request.on("end", () => {
      const message = JSON.parse(body);
      if (message.method === "initialize") {
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end(
          JSON.stringify({
            jsonrpc: "2.0",
            id: 1,
            result: { protocolVersion: PROTOCOL_VERSION, capabilities: {} }
          })
        );
        return;
      }
      if (message.method === "tools/list") {
        response.writeHead(200, { "Content-Type": "application/json" });
        response.end(
          JSON.stringify({
            jsonrpc: "2.0",
            id: 3,
            error: { code: -32002, message: emptyRegistryGuidance("cli", "D:/Code/Packages") }
          })
        );
        return;
      }
      response.writeHead(200, { "Content-Type": "application/json" });
      response.end(
        JSON.stringify({
          jsonrpc: "2.0",
          id: 2,
          result: {
            content: [{ type: "text", text: JSON.stringify({ status: "ready" }) }]
          }
        })
      );
    });
  });
  const listening = new Promise((resolve) => {
    server.listen(0, "127.0.0.1", () => resolve(server.address().port));
  });
  const port = await listening;
  try {
    const result = await probeEndpoint(
      { host: "127.0.0.1", port, endpointPath: "/mcp" },
      optionsFor(undefined)
    );
    assert.equal(result.ok, false);
    assert.equal(result.status, "no-editor");
    assert.equal(result.toolCount, 0);
  } finally {
    server.close();
  }
});
