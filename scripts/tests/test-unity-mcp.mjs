// Tests for the Unity MCP endpoint identity check (issue #333).
//
// The interesting behavior is not "can we reach a port" -- the retired scripts could do that, and
// doing only that is what pointed a whole session at the wrong Unity project. It is "does the editor
// on the other end have THIS project open", so these tests stand up a real HTTP listener that
// answers the MCP handshake and the GetProjectRoot tool call, and assert on the classification.

import assert from "node:assert/strict";
import http from "node:http";
import test from "node:test";

import {
  normalizeProjectRoot,
  pinProjectRoot,
  probeEndpoint,
  sameProjectRoot
} from "../mcp/unity-mcp.mjs";

const PROTOCOL_VERSION = "2025-11-25";

/** A stand-in bridge: MCP initialize, plus GetProjectRoot answering with the supplied root. */
function startFakeBridge({ projectRoot, omitProjectRoot = false }) {
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
      resolve({ server, port: server.address().port });
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
