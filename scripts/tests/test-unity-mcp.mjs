// Tests for the Unity MCP endpoint identity check (issue #333).
//
// The interesting behavior is not "can we reach a port" -- the retired scripts could do that, and
// doing only that is what pointed a whole session at the wrong Unity project. It is "does the editor
// on the other end have THIS project open", so these tests stand up a real HTTP listener that
// answers the MCP handshake and the GetProjectRoot tool call, and assert on the classification.

import assert from "node:assert/strict";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import {
  clientConfigPaths,
  configure,
  findUnityProjectRoot,
  isUnityProjectRoot,
  mergeCodexToml,
  normalizeProjectRoot,
  parseArgs,
  pinProjectRoot,
  probeEndpoint,
  runProbe,
  sameProjectRoot
} from "../mcp/unity-mcp.mjs";

const PROTOCOL_VERSION = "2025-11-25";

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

      // Unity answers tools/call with JSON encoded inside a text content block.
      const payload = omitProjectRoot
        ? { success: false, message: "unsupported" }
        : { success: true, data: { projectRoot } };
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
