#!/usr/bin/env node

/**
 * Unity MCP endpoint discovery, client auto-configuration, and streamable-HTTP bridge.
 *
 *   node scripts/mcp/unity-mcp.mjs probe       Find a live Unity MCP endpoint and handshake with it.
 *   node scripts/mcp/unity-mcp.mjs configure   Discover, then write every MCP client config.
 *   node scripts/mcp/unity-mcp.mjs bridge      Serve the Unity relay over authenticated HTTP.
 *
 * Topology: the Unity editor and its relay binary run on the host; agents run in the devcontainer.
 * `bridge` runs beside Unity, `probe` and `configure` run beside the agent. Only `bridge` needs the
 * Unity project directory, which is why `--project` is validated for that command alone -- the
 * project path names a host filesystem location that does not exist inside the container.
 */

import { spawn } from "node:child_process";
import { randomBytes, randomUUID, timingSafeEqual } from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import process from "node:process";
import { fileURLToPath, pathToFileURL } from "node:url";

import { Server } from "@modelcontextprotocol/sdk/server/index.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { isInitializeRequest } from "@modelcontextprotocol/sdk/types.js";
import { parse as parseToml } from "smol-toml";

export const REPO_ROOT = path.resolve(fileURLToPath(new URL("../..", import.meta.url)));

export const DEFAULTS = Object.freeze({
  bindHost: "0.0.0.0",
  host: "host.docker.internal",
  port: 9007,
  endpointPath: "/mcp",
  protocolVersion: "2025-11-25",
  probeTimeout: 5_000,
  connectTimeout: 750,
  requestTimeout: 300_000,
  sessionTimeout: 60_000,
  bodyLimitBytes: 1_048_576,
  // Upper bound on how long the bridge waits for a request body. Capped by the session timeout so a
  // client that sends headers and then stalls cannot hold a socket (and shutdown) open.
  bodyTimeout: 15_000,
  maxSessions: 8
});

// Ports tried during discovery, after any explicitly configured one. This list is deliberately just
// this repository's own port (9007): every sibling studio project owns a distinct one (DxMessaging 9003,
// IshoBoy 9004, DoxReloaded 9010, qora-redux 9020), and probing theirs is precisely how a config
// ends up pointed at another project's editor -- issue #333. The identity check would now catch it,
// but not probing a neighbour at all is the cheaper guarantee.
export const FALLBACK_PORTS = Object.freeze([9007]);

// Hosts tried during discovery, after any explicitly configured one. host.docker.internal is the
// Docker Desktop bridge; the resolv.conf nameserver and default gateway cover WSL2 and plain Linux
// bridge networking respectively; localhost covers running the agent on the same box as Unity.
export const FALLBACK_HOSTS = Object.freeze(["host.docker.internal", "127.0.0.1"]);

const OPTION_NAMES = new Set([
  "bind",
  "host",
  "port",
  "path",
  "project",
  "relay",
  "request-timeout",
  "session-timeout",
  "timeout",
  "connect-timeout",
  "max-sessions",
  "protocol-version",
  "log-level",
  "token",
  "project-root",
  "no-discover",
  "any-project"
]);

const FLAG_NAMES = new Set(["no-discover", "any-project"]);

const ENV_KEYS = Object.freeze({
  bindHost: "UNITY_MCP_BIND_HOST",
  host: "UNITY_MCP_BRIDGE_HOST",
  port: "UNITY_MCP_BRIDGE_PORT",
  endpointPath: "UNITY_MCP_BRIDGE_PATH",
  projectPath: "UNITY_PROJECT_PATH",
  relayPath: "UNITY_MCP_RELAY_PATH",
  requestTimeout: "UNITY_MCP_REQUEST_TIMEOUT",
  sessionTimeout: "UNITY_MCP_SESSION_TIMEOUT",
  timeout: "UNITY_MCP_PROBE_TIMEOUT",
  connectTimeout: "UNITY_MCP_CONNECT_TIMEOUT",
  maxSessions: "UNITY_MCP_MAX_SESSIONS",
  protocolVersion: "UNITY_MCP_PROTOCOL_VERSION",
  logLevel: "UNITY_MCP_LOG_LEVEL",
  bearerToken: "UNITY_MCP_BEARER_TOKEN",
  projectRoot: "UNITY_MCP_PROJECT_ROOT"
});

function fail(message) {
  throw new Error(message);
}

function first(...values) {
  return values.find((value) => value !== undefined && value !== "");
}

// ---------------------------------------------------------------------------
// Argument and .env.local parsing
// ---------------------------------------------------------------------------

export function parseArgs(argv) {
  const result = { _: [] };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) {
      result._.push(token);
      continue;
    }
    const separator = token.indexOf("=");
    const name = token.slice(2, separator === -1 ? undefined : separator);
    if (!OPTION_NAMES.has(name)) {
      fail(`Unknown option: --${name}`);
    }
    if (FLAG_NAMES.has(name)) {
      if (separator !== -1) {
        fail(`--${name} does not take a value`);
      }
      result[name] = true;
      continue;
    }
    const value = separator === -1 ? argv[++index] : token.slice(separator + 1);
    if (value === undefined || value.startsWith("--")) {
      fail(`Missing value for --${name}`);
    }
    if (value === "") {
      fail(`--${name} requires a non-empty value`);
    }
    result[name] = value;
  }
  return result;
}

// A quoted value runs to the last quote that leaves only whitespace or a comment behind. The
// alternation lets the engine backtrack over a trailing backslash, so a Windows path written as
// "D:\Program Files\Proj\" parses while an escaped quote inside the value ("say \"hi\"") still does.
const QUOTED_VALUE = Object.freeze({
  '"': /^"((?:\\"|[^"])*)"\s*(?:#.*)?$/,
  "'": /^'((?:\\'|[^'])*)'\s*(?:#.*)?$/
});

export function parseDotEnv(raw, source = ".env.local") {
  const values = {};
  for (const [index, original] of raw.split(/\r?\n/).entries()) {
    const line = original.trim();
    if (!line || line.startsWith("#")) {
      continue;
    }
    const match = /^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$/.exec(line);
    if (!match) {
      fail(`Invalid ${source} entry on line ${index + 1}`);
    }
    let value = match[2].trim();
    const quote = QUOTED_VALUE[value[0]] ? value[0] : undefined;
    if (quote) {
      const quoted = QUOTED_VALUE[quote].exec(value);
      if (!quoted) {
        fail(`Invalid quoted value in ${source} on line ${index + 1}`);
      }
      // Only double quotes carry escapes, matching POSIX shell and dotenv semantics.
      value = quote === '"' ? quoted[1].replace(/\\(["\\])/g, "$1") : quoted[1];
    } else {
      const comment = value.search(/\s+#/);
      if (comment !== -1) {
        value = value.slice(0, comment).trimEnd();
      }
    }
    values[match[1]] = value;
  }
  return values;
}

/**
 * `.env.local` is shared with unrelated tooling, so one line this parser cannot read must not abort
 * `probe`, `configure`, or `bridge`. Each line is parsed on its own and a bad one is warned about and
 * skipped; `parseDotEnv` itself stays strict.
 */
export function readLocalEnv(repoRoot) {
  const envPath = path.join(repoRoot, ".env.local");
  if (!fs.existsSync(envPath)) {
    return {};
  }
  const values = {};
  for (const [index, line] of fs.readFileSync(envPath, "utf8").split(/\r?\n/).entries()) {
    try {
      Object.assign(values, parseDotEnv(line, envPath));
    } catch {
      console.warn(`unity-mcp: ignoring unparsable ${envPath} line ${index + 1}: ${line.trim()}`);
    }
  }
  return values;
}

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

function integer(value, name, minimum, maximum) {
  if (!/^\d+$/.test(String(value))) {
    fail(`${name} must be an integer`);
  }
  const parsed = Number(value);
  if (!Number.isSafeInteger(parsed) || parsed < minimum || parsed > maximum) {
    fail(`${name} must be between ${minimum} and ${maximum}`);
  }
  return parsed;
}

function validateText(value, name) {
  if (/[\0\r\n]/.test(value)) {
    fail(`${name} contains an invalid control character`);
  }
  return value;
}

export function validateHost(value, name = "Host") {
  validateText(value, name);
  if (net.isIP(value)) {
    return value;
  }
  const candidate = value.endsWith(".") ? value.slice(0, -1) : value;
  const labels = candidate.split(".");
  const labelPattern = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/;
  if (
    candidate.length === 0 ||
    candidate.length > 253 ||
    !labels.every((label) => labelPattern.test(label))
  ) {
    fail(`Invalid ${name.toLowerCase()}: ${value}`);
  }
  return value;
}

export function validateEndpointPath(value) {
  const normalized = value.startsWith("/") ? value : `/${value}`;
  if (
    !/^\/[A-Za-z0-9._~!$&'()*+,;=:@%/-]*$/.test(normalized) ||
    normalized.includes("//") ||
    /%(?![0-9A-Fa-f]{2})/.test(normalized)
  ) {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  let decoded;
  try {
    // Syntactically valid escapes can still be invalid UTF-8 (for example /%FF), which throws.
    decoded = decodeURIComponent(normalized);
  } catch {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  if (decoded.includes("//") || decoded.split("/").some((part) => part === "." || part === "..")) {
    fail(`Invalid MCP endpoint path: ${value}`);
  }
  return normalized;
}

function validateToken(value) {
  if (value === undefined) {
    return undefined;
  }
  if (!/^[A-Za-z0-9._~-]{32,256}$/.test(value)) {
    fail("Bearer token must be 32-256 URL-safe characters");
  }
  return value;
}

// ---------------------------------------------------------------------------
// Option resolution
// ---------------------------------------------------------------------------

/**
 * `repoRoot` is where MCP client configs and `.env.local` live; it is always this repository.
 * `projectPath` is the Unity project the relay opens and is only meaningful on the host, so it is
 * resolved lazily and validated by `requireProjectPath` from the `bridge` command alone.
 */
export function resolveOptions(args, environment = process.env, localValues, repoRoot = REPO_ROOT) {
  const local = localValues ?? readLocalEnv(repoRoot);
  const get = (argName, key, fallback) =>
    first(args[argName], environment[ENV_KEYS[key]], local[ENV_KEYS[key]], fallback);

  const explicitHost = first(args.host, environment[ENV_KEYS.host], local[ENV_KEYS.host]);
  const explicitPort = first(args.port, environment[ENV_KEYS.port], local[ENV_KEYS.port]);
  const projectPath = first(
    args.project,
    environment[ENV_KEYS.projectPath],
    local[ENV_KEYS.projectPath]
  );

  const options = {
    repoRoot,
    bindHost: validateHost(get("bind", "bindHost", DEFAULTS.bindHost), "Bind host"),
    host: validateHost(explicitHost ?? DEFAULTS.host),
    explicitHost: explicitHost === undefined ? undefined : validateHost(explicitHost),
    port: integer(explicitPort ?? DEFAULTS.port, "Port", 1, 65_535),
    explicitPort: explicitPort === undefined ? undefined : integer(explicitPort, "Port", 1, 65_535),
    endpointPath: validateEndpointPath(get("path", "endpointPath", DEFAULTS.endpointPath)),
    projectPath: projectPath === undefined ? undefined : path.resolve(projectPath),
    relayPath: first(args.relay, environment[ENV_KEYS.relayPath], local[ENV_KEYS.relayPath]),
    requestTimeout: integer(
      get("request-timeout", "requestTimeout", DEFAULTS.requestTimeout),
      "Request timeout",
      1,
      86_400_000
    ),
    sessionTimeout: integer(
      get("session-timeout", "sessionTimeout", DEFAULTS.sessionTimeout),
      "Session timeout",
      1,
      86_400_000
    ),
    timeout: integer(get("timeout", "timeout", DEFAULTS.probeTimeout), "Probe timeout", 1, 300_000),
    connectTimeout: integer(
      get("connect-timeout", "connectTimeout", DEFAULTS.connectTimeout),
      "Connect timeout",
      1,
      60_000
    ),
    maxSessions: integer(
      get("max-sessions", "maxSessions", DEFAULTS.maxSessions),
      "Max sessions",
      1,
      1_024
    ),
    protocolVersion: validateText(
      get("protocol-version", "protocolVersion", DEFAULTS.protocolVersion),
      "Protocol version"
    ),
    logLevel: get("log-level", "logLevel", "info"),
    bearerToken: validateToken(get("token", "bearerToken", undefined)),
    // The project this repo's editor is expected to have open. Unset on a first run; `configure`
    // pins whatever it discovers so every later run is checked rather than trusted.
    expectedProjectRoot: first(
      args["project-root"],
      environment[ENV_KEYS.projectRoot],
      local[ENV_KEYS.projectRoot]
    ),
    anyProject: args["any-project"] === true,
    discover: args["no-discover"] !== true
  };

  if (options.relayPath) {
    options.relayPath = path.resolve(options.repoRoot, options.relayPath);
  }
  if (!/^(?:debug|info|none)$/.test(options.logLevel)) {
    fail("Log level must be debug, info, or none");
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(options.protocolVersion)) {
    fail("Protocol version must use YYYY-MM-DD format");
  }
  return options;
}

/** A Unity project root is the directory holding both Assets and ProjectSettings. */
export function isUnityProjectRoot(directory, exists = fs.existsSync) {
  return exists(path.join(directory, "Assets")) && exists(path.join(directory, "ProjectSettings"));
}

/**
 * Walk up from a starting directory to the Unity project that contains it.
 *
 * The sibling studio repos this script came from ARE Unity projects, so they default the relay's
 * --project-path to their own repo root. This repository is a PACKAGE: it lives at
 * <project>/Packages/com.wallstop-studios.unity-helpers and has no Assets/ProjectSettings of its
 * own. Inheriting that default hands the relay a directory that is not a project at all, and the
 * editor it then attaches to is anyone's guess -- the #333 failure one level down.
 *
 * Requiring BOTH marker directories is what makes this correct here: the repo root happens to
 * carry a stray Assets/, and matching on that alone would stop at the package.
 */
export function findUnityProjectRoot(start, exists = fs.existsSync) {
  let current = path.resolve(start);
  for (;;) {
    if (isUnityProjectRoot(current, exists)) {
      return current;
    }
    const parent = path.dirname(current);
    if (parent === current) {
      return undefined;
    }
    current = parent;
  }
}

export function requireProjectPath(options, runtime = {}) {
  const exists = runtime.exists ?? fs.existsSync;
  if (!options.projectPath) {
    const discovered = findUnityProjectRoot(options.repoRoot, exists);
    if (!discovered) {
      fail(
        `Could not find a Unity project above ${options.repoRoot}. This repository is a package, ` +
          `not a project, so the bridge walks up looking for a directory with both Assets and ` +
          `ProjectSettings. Pass --project <unity project root> or set ${ENV_KEYS.projectPath}.`
      );
    }
    options = { ...options, projectPath: discovered };
  }
  if (!fs.existsSync(options.projectPath) || !fs.statSync(options.projectPath).isDirectory()) {
    fail(`Unity project directory does not exist: ${options.projectPath}`);
  }
  return options.projectPath;
}

export function endpointUrl({ host, port, endpointPath }) {
  const formatted = net.isIP(host) === 6 ? `[${host}]` : host;
  return `http://${formatted}:${port}${endpointPath}`;
}

// ---------------------------------------------------------------------------
// Endpoint discovery
// ---------------------------------------------------------------------------

/** Nameserver entries in /etc/resolv.conf. Under WSL2 this is the Windows host. */
export function resolvConfHosts(raw) {
  if (!raw) {
    return [];
  }
  return raw
    .split(/\r?\n/)
    .map((line) => /^\s*nameserver\s+(\S+)\s*$/.exec(line))
    .filter(Boolean)
    .map((match) => match[1])
    .filter((address) => net.isIP(address) === 4);
}

/** Default-route gateways from /proc/net/route (little-endian hex IPv4). */
export function procNetRouteGateways(raw) {
  if (!raw) {
    return [];
  }
  const gateways = [];
  for (const line of raw.split(/\r?\n/).slice(1)) {
    const fields = line.trim().split(/\s+/);
    if (fields.length < 3 || fields[1] !== "00000000" || !/^[0-9A-Fa-f]{8}$/.test(fields[2])) {
      continue;
    }
    const value = Number.parseInt(fields[2], 16);
    if (value === 0) {
      continue;
    }
    const octets = [
      value & 0xff,
      (value >>> 8) & 0xff,
      (value >>> 16) & 0xff,
      (value >>> 24) & 0xff
    ];
    gateways.push(octets.join("."));
  }
  return gateways;
}

function readTextOrEmpty(filePath) {
  try {
    return fs.readFileSync(filePath, "utf8");
  } catch {
    return "";
  }
}

/**
 * Candidate endpoints in priority order, de-duplicated. An explicitly configured host or port is the
 * ONLY candidate on that axis, so discovery can never override a deliberate setting: `--host X`
 * probes X against the fallback ports, and `--host X --port Y` yields exactly one candidate.
 */
export function endpointCandidates(options, runtime = {}) {
  const readFile = runtime.readFile ?? readTextOrEmpty;
  const hosts = options.explicitHost
    ? [options.explicitHost]
    : [
        ...FALLBACK_HOSTS,
        ...resolvConfHosts(readFile("/etc/resolv.conf")),
        ...procNetRouteGateways(readFile("/proc/net/route"))
      ].filter(Boolean);
  const ports = options.explicitPort ? [options.explicitPort] : [...FALLBACK_PORTS];

  const seen = new Set();
  const candidates = [];
  for (const port of ports) {
    for (const host of hosts) {
      const key = `${host}:${port}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      candidates.push({ host, port, endpointPath: options.endpointPath });
    }
  }
  return candidates;
}

export function tcpReachable(host, port, timeout) {
  return new Promise((resolve) => {
    const socket = new net.Socket();
    const settle = (value) => {
      socket.removeAllListeners();
      socket.destroy();
      resolve(value);
    };
    socket.setTimeout(timeout);
    socket.once("connect", () => settle(true));
    socket.once("timeout", () => settle(false));
    socket.once("error", () => settle(false));
    socket.connect(port, host);
  });
}

function parseProbePayload(contentType, body, expectedId = 1) {
  const candidates = contentType.includes("text/event-stream")
    ? body
        .split(/\r?\n/)
        .filter((line) => line.startsWith("data:"))
        .map((line) => line.slice(5).trim())
        .filter((line) => line && line !== "[DONE]")
    : [body];
  for (const candidate of candidates) {
    try {
      const message = JSON.parse(candidate);
      if (message?.jsonrpc === "2.0" && message.id === expectedId) {
        return message;
      }
    } catch {
      /* Continue to the next server-sent event. */
    }
  }
  return undefined;
}

/**
 * Compare two Unity project roots. The editor reports a host path, so the comparison has to survive
 * separator direction, a trailing separator, and Windows' case-insensitive filesystem.
 */
export function sameProjectRoot(left, right) {
  return normalizeProjectRoot(left) === normalizeProjectRoot(right);
}

export function normalizeProjectRoot(value) {
  if (typeof value !== "string") {
    return "";
  }
  return value.trim().replace(/\\/g, "/").replace(/\/+$/, "").toLowerCase();
}

/**
 * Complete the MCP lifecycle handshake. Best-effort: a server that does not want the notification
 * should not cost us the identity query, so a failure here is not fatal on its own.
 */
export async function notifyInitialized(
  url,
  options,
  sessionId,
  protocolVersion,
  fetchImpl = fetch
) {
  const headers = {
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
    "MCP-Protocol-Version": protocolVersion
  };
  if (options.bearerToken) {
    headers.Authorization = `Bearer ${options.bearerToken}`;
  }
  if (sessionId) {
    headers["Mcp-Session-Id"] = sessionId;
  }

  try {
    await fetchImpl(url, {
      method: "POST",
      headers,
      body: JSON.stringify({ jsonrpc: "2.0", method: "notifications/initialized" }),
      signal: AbortSignal.timeout(options.timeout)
    });
  } catch {
    /* The identity query below reports the real outcome. */
  }
}

/**
 * Count the tools an endpoint exposes.
 *
 * Unity registers the `Unity_*` tools from inside the editor, so a bridge with no editor attached
 * completes the handshake perfectly and exposes nothing. That state is indistinguishable from a
 * working setup unless you ask -- observed live: a healthy, authenticating bridge on 9007 with
 * `tools/list` returning zero, while `probe` happily reported "reachable".
 *
 * Returns `undefined` when the endpoint will not answer at all, which is NOT treated as empty:
 * only a definitive zero is worth failing on.
 */
export async function countTools(url, options, sessionId, protocolVersion, fetchImpl = fetch) {
  const headers = {
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
    "MCP-Protocol-Version": protocolVersion
  };
  if (options.bearerToken) {
    headers.Authorization = `Bearer ${options.bearerToken}`;
  }
  if (sessionId) {
    headers["Mcp-Session-Id"] = sessionId;
  }

  try {
    const response = await fetchImpl(url, {
      method: "POST",
      headers,
      body: JSON.stringify({ jsonrpc: "2.0", id: 3, method: "tools/list", params: {} }),
      signal: AbortSignal.timeout(options.timeout)
    });
    if (!response.ok) {
      return undefined;
    }
    const message = parseProbePayload(
      response.headers.get("content-type") ?? "",
      await response.text(),
      3
    );
    const tools = message?.result?.tools;
    return Array.isArray(tools) ? tools.length : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Ask an endpoint which Unity project it has open.
 *
 * This is the question the old bash/PowerShell tooling never asked, and not asking it is the whole
 * of issue #333: an endpoint is a host:port, but a bridge is bound to one editor, and the editor is
 * whichever one happened to claim that port. A probe that only checks "does something here speak
 * MCP" reports success against a completely unrelated project. `Unity_ManageEditor GetProjectRoot`
 * answers in a single POST with no code compilation, so there is no reason not to ask.
 *
 * Returns `undefined` when the endpoint cannot answer -- an older bridge, or a server that is not
 * Unity's. That is reported, never treated as a match.
 */
export async function queryProjectRoot(
  url,
  options,
  sessionId,
  protocolVersion,
  fetchImpl = fetch
) {
  const headers = {
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
    "MCP-Protocol-Version": protocolVersion
  };
  if (options.bearerToken) {
    headers.Authorization = `Bearer ${options.bearerToken}`;
  }
  if (sessionId) {
    headers["Mcp-Session-Id"] = sessionId;
  }

  try {
    const response = await fetchImpl(url, {
      method: "POST",
      headers,
      body: JSON.stringify({
        jsonrpc: "2.0",
        id: 2,
        method: "tools/call",
        params: { name: "Unity_ManageEditor", arguments: { Action: "GetProjectRoot" } }
      }),
      signal: AbortSignal.timeout(options.timeout)
    });
    if (!response.ok) {
      return undefined;
    }
    const message = parseProbePayload(
      response.headers.get("content-type") ?? "",
      await response.text(),
      2
    );
    const text = message?.result?.content?.find((entry) => entry?.type === "text")?.text;
    if (typeof text !== "string") {
      return undefined;
    }
    // The tool answers with JSON encoded inside a text block.
    const payload = JSON.parse(text);
    const projectRoot = payload?.data?.projectRoot;
    return typeof projectRoot === "string" && projectRoot.length > 0 ? projectRoot : undefined;
  } catch {
    return undefined;
  }
}

/**
 * Complete an MCP `initialize` handshake against one endpoint. Returns a classified result rather
 * than throwing so discovery can report every attempt; `status: "unauthorized"` in particular means
 * a bridge is running but the local bearer token does not match it, which needs different advice
 * from "nothing is listening".
 */
export async function probeEndpoint(candidate, options, fetchImpl = fetch) {
  const url = endpointUrl(candidate);
  // Failures carry the candidate too, so callers can act on the endpoint that produced them (an
  // `unauthorized` attempt names the bridge whose token needs copying).
  const classify = (status, detail) => ({ ...candidate, url, ok: false, status, detail });
  if (!(await tcpReachable(candidate.host, candidate.port, options.connectTimeout))) {
    return classify("unreachable", "no TCP listener");
  }

  const headers = {
    Accept: "application/json, text/event-stream",
    "Content-Type": "application/json",
    "MCP-Protocol-Version": options.protocolVersion
  };
  if (options.bearerToken) {
    headers.Authorization = `Bearer ${options.bearerToken}`;
  }

  let response;
  let body;
  try {
    response = await fetchImpl(url, {
      method: "POST",
      headers,
      body: JSON.stringify({
        jsonrpc: "2.0",
        id: 1,
        method: "initialize",
        params: {
          protocolVersion: options.protocolVersion,
          capabilities: {},
          clientInfo: { name: "unity-mcp-probe", version: "1.0.0" }
        }
      }),
      signal: AbortSignal.timeout(options.timeout)
    });
    body = await response.text();
  } catch (error) {
    return classify("unreachable", error.message);
  }

  if (response.status === 401 || response.status === 403) {
    return classify("unauthorized", `HTTP ${response.status}`);
  }
  // A WebSocket-only server answers every plain HTTP request with 426. This bridge never emits one,
  // so a 426 is proof the responder is not it: the port belongs to some other process. That is a
  // different failure from silence and needs different advice, and folding it into "nothing
  // responded" once cost a session's time chasing Unity's approval dialog. A 101 is checked beside
  // it for completeness only -- fetch never surfaces a completed upgrade as a Response, so such a
  // peer arrives through the catch above as `unreachable`.
  if (response.status === 426 || response.status === 101) {
    return classify(
      "port-occupied",
      `HTTP ${response.status} ${response.statusText || "Upgrade Required"}`
    );
  }
  if (!response.ok) {
    return classify("http-error", `HTTP ${response.status} ${body.slice(0, 120)}`);
  }

  const message = parseProbePayload(response.headers.get("content-type") ?? "", body);
  if (!message || message.error || typeof message.result?.protocolVersion !== "string") {
    return classify("malformed", body.slice(0, 120) || "no JSON-RPC result");
  }

  const sessionId = response.headers.get("mcp-session-id");
  const negotiated = message.result.protocolVersion;

  // The spec requires `notifications/initialized` before any other request. Unity's own server is
  // lenient, but a conforming relay may refuse the tool call -- and a refused identity query reads
  // as "would not identify itself", which fails the endpoint. Skipping it would defeat the check on
  // exactly the servers that follow the protocol most carefully.
  await notifyInitialized(url, options, sessionId, negotiated, fetchImpl);

  // A bridge with no editor attached handshakes perfectly and exposes nothing, so establish that
  // an editor is actually there before asking which project it has open -- otherwise the useful
  // diagnosis ("no editor") is reported as the vaguer "would not identify itself".
  const toolCount = await countTools(url, options, sessionId, negotiated, fetchImpl);

  // Ask WHICH Unity this is before treating the endpoint as usable (issue #333).
  const projectRoot = await queryProjectRoot(url, options, sessionId, negotiated, fetchImpl);

  if (sessionId) {
    // Close the session we just opened so probing does not leak relay child processes on the host.
    await fetchImpl(url, {
      method: "DELETE",
      headers: {
        Accept: "application/json, text/event-stream",
        ...(options.bearerToken ? { Authorization: `Bearer ${options.bearerToken}` } : {}),
        "Mcp-Session-Id": sessionId,
        "MCP-Protocol-Version": negotiated
      },
      signal: AbortSignal.timeout(options.timeout)
    }).catch(() => {});
  }

  const identified = {
    url,
    sessionId,
    protocolVersion: negotiated,
    projectRoot,
    toolCount,
    ...candidate
  };

  // Zero tools is unambiguous and worth failing on regardless of whether a project is pinned:
  // the endpoint cannot do anything an agent wants.
  if (toolCount === 0) {
    return {
      ...identified,
      ok: false,
      status: "no-editor",
      detail: "bridge is up but no Unity editor is attached (tools/list returned 0 tools)"
    };
  }

  if (options.expectedProjectRoot && !options.anyProject) {
    if (projectRoot === undefined) {
      return {
        ...identified,
        ok: false,
        status: "unidentified",
        detail: "endpoint did not report a project root"
      };
    }
    if (!sameProjectRoot(projectRoot, options.expectedProjectRoot)) {
      return {
        ...identified,
        ok: false,
        status: "project-mismatch",
        detail: `serves ${projectRoot}, expected ${options.expectedProjectRoot}`
      };
    }
  }

  return { ...identified, ok: true, status: "ok" };
}

/** Probe every candidate in order and return the first that completes a handshake. */
export async function discoverEndpoint(options, runtime = {}) {
  const fetchImpl = runtime.fetchImpl ?? fetch;
  const candidates = runtime.candidates ?? endpointCandidates(options, runtime);
  const attempts = [];
  for (const candidate of candidates) {
    log(options, "debug", `Probing ${endpointUrl(candidate)}`);
    const result = await probeEndpoint(candidate, options, fetchImpl);
    log(options, "debug", `  ${result.status}: ${result.detail ?? "ok"}`);
    attempts.push(result);
    if (result.ok) {
      return { found: result, attempts };
    }
  }
  return { found: undefined, attempts };
}

export function describeAttempts(attempts) {
  const interesting = attempts.filter((attempt) => attempt.status !== "unreachable");
  // A project-mismatch attempt is the single most useful line in this list: it names a bridge that
  // is alive and healthy and simply belongs to another editor.
  const shown = interesting.length > 0 ? interesting : attempts;
  return shown
    .map((attempt) => `  ${attempt.url} - ${attempt.status} (${attempt.detail ?? "no detail"})`)
    .join("\n");
}

// ---------------------------------------------------------------------------
// Client configuration
// ---------------------------------------------------------------------------

function stageFile(filePath, content) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  const temporary = `${filePath}.${process.pid}.${randomBytes(8).toString("hex")}.tmp`;
  fs.writeFileSync(temporary, content, { encoding: "utf8", mode: 0o600, flag: "wx" });
  return temporary;
}

function atomicWrite(filePath, content, mode) {
  const temporary = stageFile(filePath, content);
  try {
    if (mode !== undefined) {
      // Rollback must restore the permissions the file had, not the 0600 staging default.
      fs.chmodSync(temporary, mode);
    }
    fs.renameSync(temporary, filePath);
  } finally {
    fs.rmSync(temporary, { force: true });
  }
}

/**
 * Write several files as one unit. Every file is staged before any is committed, and a failure part
 * way through rolls back the files already renamed, so a crash cannot leave one agent pointed at a
 * new endpoint while another still holds the old one.
 *
 * Rollback is itself failure-safe: every restore is attempted even when an earlier one fails (Windows
 * `rename` returns EPERM whenever an editor holds the destination open, which is exactly these four
 * config files), and the ORIGINAL error is rethrown with the rollback failures attached as `cause`.
 */
export function transactionalWrite(writes, beforeCommit = () => {}) {
  const changed = writes.filter(
    ([filePath, content]) =>
      !fs.existsSync(filePath) || fs.readFileSync(filePath, "utf8") !== content
  );
  const staged = [];
  const committed = [];
  try {
    for (const [filePath, content] of changed) {
      const existed = fs.existsSync(filePath);
      staged.push({
        filePath,
        existed,
        original: existed ? fs.readFileSync(filePath, "utf8") : undefined,
        mode: existed ? fs.statSync(filePath).mode & 0o777 : undefined,
        temporary: stageFile(filePath, content)
      });
    }
    for (let index = 0; index < staged.length; index += 1) {
      beforeCommit(index, staged[index].filePath);
      fs.renameSync(staged[index].temporary, staged[index].filePath);
      committed.push(staged[index]);
    }
  } catch (error) {
    const suppressed = [];
    for (const item of committed.reverse()) {
      try {
        if (item.existed) {
          atomicWrite(item.filePath, item.original, item.mode);
        } else {
          fs.rmSync(item.filePath, { force: true });
        }
      } catch (rollbackError) {
        suppressed.push(rollbackError);
      }
    }
    if (suppressed.length > 0) {
      error.cause = new AggregateError(
        suppressed,
        `Rollback failed for ${suppressed.length} file(s)`
      );
    }
    throw error;
  } finally {
    // Staging can throw part way through, so only the temporaries actually created are removed.
    for (const item of staged) {
      fs.rmSync(item.temporary, { force: true });
    }
  }
  return changed.map(([filePath]) => filePath);
}

function ensureBearerToken(options) {
  if (options.bearerToken) {
    return options;
  }
  const bearerToken = randomBytes(32).toString("hex");
  const envPath = path.join(options.repoRoot, ".env.local");
  const current = fs.existsSync(envPath) ? fs.readFileSync(envPath, "utf8") : "";
  const prefix = current && !current.endsWith("\n") ? "\n" : "";
  atomicWrite(envPath, `${current}${prefix}${ENV_KEYS.bearerToken}=${bearerToken}\n`);
  return { ...options, bearerToken };
}

/**
 * Record the project root the endpoint reported, so subsequent runs verify instead of trusting.
 *
 * The first run is the one a human watches; after it, an editor swap or a port collision turns into
 * a loud `project-mismatch` rather than silent work against the wrong project. Never overwrites an
 * existing pin -- that value is the developer's stated intent.
 */
export function pinProjectRoot(options, found) {
  if (options.expectedProjectRoot || !found?.projectRoot) {
    return { options, wrote: false };
  }
  const envPath = path.join(options.repoRoot, ".env.local");
  const current = fs.existsSync(envPath) ? fs.readFileSync(envPath, "utf8") : "";
  const prefix = current && !current.endsWith("\n") ? "\n" : "";
  atomicWrite(envPath, `${current}${prefix}${ENV_KEYS.projectRoot}=${found.projectRoot}\n`);
  return { options: { ...options, expectedProjectRoot: found.projectRoot }, wrote: true };
}

/**
 * Strip `//` and block comments plus trailing commas so JSONC parses. `.vscode/mcp.json` is JSONC and
 * VS Code's own "MCP: Add Server" scaffolding writes a comment into it, so refusing JSONC means
 * `configure` cannot run at all for those users. String contents are tracked so a `//` inside a URL
 * or a comma inside a string value is never mistaken for syntax.
 */
export function stripJsonComments(raw) {
  let out = "";
  let inString = false;
  let escaped = false;
  // Index of the last non-whitespace character already in `out`. Re-scanning `out` with a regex on
  // every closing bracket flattens the rope V8 builds from `out += char`, which makes the pass
  // quadratic in the number of closing brackets: a 364 KB config took 9 s, a 1.5 MB one took 150 s.
  let lastNonSpace = -1;
  // The character at `lastNonSpace`, carried separately: `out[lastNonSpace]` would flatten the same
  // rope the regex did, which is most of the remaining cost.
  let lastNonSpaceChar = "";
  const append = (text) => {
    if (text.trim() !== "") {
      lastNonSpace = out.length + text.length - 1;
      lastNonSpaceChar = text[text.length - 1];
    }
    out += text;
  };
  for (let index = 0; index < raw.length; index += 1) {
    const char = raw[index];
    if (inString) {
      append(char);
      if (escaped) {
        escaped = false;
      } else if (char === "\\") {
        escaped = true;
      } else if (char === '"') {
        inString = false;
      }
      continue;
    }
    if (char === '"') {
      inString = true;
    } else if (char === "/" && raw[index + 1] === "/") {
      const end = raw.indexOf("\n", index + 2);
      index = end === -1 ? raw.length : end - 1;
      continue;
    } else if (char === "/" && raw[index + 1] === "*") {
      const end = raw.indexOf("*/", index + 2);
      index = end === -1 ? raw.length : end + 1;
      continue;
    } else if ((char === "}" || char === "]") && lastNonSpaceChar === ",") {
      // Drop a trailing comma in place. Valid JSON never takes this branch. `append` below restores
      // `lastNonSpace` to the closing bracket, so a nested `[1,],}` still sees its own comma.
      out = out.slice(0, lastNonSpace) + out.slice(lastNonSpace + 1);
    }
    append(char);
  }
  return out;
}

function readJsonObject(filePath) {
  if (!fs.existsSync(filePath) || !fs.readFileSync(filePath, "utf8").trim()) {
    return {};
  }
  let parsed;
  try {
    parsed = JSON.parse(stripJsonComments(fs.readFileSync(filePath, "utf8")));
  } catch (error) {
    fail(`Invalid JSON in ${filePath}: ${error.message}`);
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    fail(`Expected a JSON object in ${filePath}`);
  }
  return parsed;
}

export function prepareJsonServer(filePath, collection, server) {
  return prepareJsonServers(filePath, collection, { "unity-mcp-remote": server });
}

export function prepareJsonServers(filePath, collection, servers) {
  const document = readJsonObject(filePath);
  const existing = document[collection];
  if (
    existing !== undefined &&
    (!existing || typeof existing !== "object" || Array.isArray(existing))
  ) {
    fail(`Expected ${collection} to be an object in ${filePath}`);
  }
  document[collection] = { ...(existing ?? {}), ...servers };
  return `${JSON.stringify(document, null, 2)}\n`;
}

function tomlString(value) {
  return `"${value.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`;
}

/**
 * Decide whether a TOML header line opens the table this tool owns. Parsing the single line with a
 * sentinel key is how a `[mcp_servers.unity_mcp_remote]` header is told apart from any other header without
 * hand-writing a TOML grammar.
 */
function classifyTomlHeader(line, serverName) {
  if (!line.trimStart().startsWith("[")) {
    return undefined;
  }
  const marker = "__dxm_mcp_table_marker_7d0f__";
  try {
    const parsed = parseToml(`${line}\n${marker} = true\n`);
    const owned = parsed.mcp_servers?.[serverName]?.[marker] === true;
    const hasMarker = JSON.stringify(parsed).includes(`"${marker}":true`);
    return hasMarker ? { owned } : undefined;
  } catch {
    return undefined;
  }
}

const CODEX_AMBIGUOUS_MESSAGE = (reason, serverName) =>
  `${reason} in .codex/config.toml, so this tool cannot tell which lines it owns. ` +
  `Delete the [mcp_servers.${serverName}] table from .codex/config.toml (or move it to the end of the ` +
  "file, after every multi-line value) and re-run configure.";

function mergeCodexServerTable(raw, serverName, block) {
  let parsed;
  try {
    parsed = raw.trim() ? parseToml(raw) : {};
  } catch (error) {
    fail(`Invalid TOML in Codex config: ${error.message}`);
  }

  const normalized = raw.replace(/\r\n/g, "\n");
  const lines = normalized.split("\n");
  const owned = lines
    .map((line, index) => ({ index, header: classifyTomlHeader(line, serverName) }))
    .filter((item) => item.header?.owned)
    .map((item) => item.index);
  if (owned.length > 1) {
    fail(CODEX_AMBIGUOUS_MESSAGE(`Duplicate ${serverName} table`, serverName));
  }
  if (owned.length === 0) {
    if (parsed.mcp_servers?.[serverName] !== undefined) {
      fail(`Unsupported inline or dotted ${serverName} definition in Codex config`);
    }
    return `${normalized.trimEnd()}${normalized.trim() ? "\n\n" : ""}${block}`;
  }

  const start = owned[0];
  try {
    parseToml(lines.slice(0, start).join("\n"));
  } catch {
    fail(
      CODEX_AMBIGUOUS_MESSAGE(
        `A ${serverName} header line appears inside a multi-line value`,
        serverName
      )
    );
  }
  let end = lines.length;
  for (let index = start + 1; index < lines.length; index += 1) {
    if (classifyTomlHeader(lines[index], serverName)) {
      end = index;
      break;
    }
  }
  lines.splice(start, end - start, ...block.trimEnd().split("\n"), "");
  const result = lines.join("\n").replace(/\n*$/, "\n");
  try {
    parseToml(result);
  } catch {
    fail(
      CODEX_AMBIGUOUS_MESSAGE(`Rewriting the ${serverName} table produced invalid TOML`, serverName)
    );
  }
  return result;
}

export function mergeCodexToml(raw, url, bearerToken) {
  const block = [
    "[mcp_servers.unity_mcp_remote]",
    `url = ${tomlString(url)}`,
    `http_headers = { Authorization = ${tomlString(`Bearer ${bearerToken}`)} }`,
    "startup_timeout_sec = 20",
    "tool_timeout_sec = 120",
    "enabled = true",
    ""
  ].join("\n");
  return mergeCodexServerTable(raw, "unity_mcp_remote", block);
}

function codexStdioBlock(serverName, command, args) {
  return [
    `[mcp_servers.${serverName}]`,
    `command = ${tomlString(command)}`,
    `args = [${args.map(tomlString).join(", ")}]`,
    "startup_timeout_sec = 60",
    "tool_timeout_sec = 120",
    "enabled = true",
    ""
  ].join("\n");
}

function sharedMcpDefinitions(repoRoot) {
  const githubLauncher = path.join(repoRoot, "scripts", "mcp", "github-mcp.sh");
  const zaiLauncher = path.join(repoRoot, "scripts", "mcp", "zai-mcp.mjs");
  const definitions = [
    { name: "github", command: "bash", args: [githubLauncher] },
    { name: "zai-vision", command: "node", args: [zaiLauncher, "vision"] },
    { name: "zai-web-search", command: "node", args: [zaiLauncher, "web-search"] },
    { name: "zai-web-reader", command: "node", args: [zaiLauncher, "web-reader"] },
    { name: "zai-zread", command: "node", args: [zaiLauncher, "zread"] }
  ];
  const json = Object.fromEntries(
    definitions.map(({ name, command, args }) => [name, { type: "stdio", command, args }])
  );
  const sharedJson = Object.fromEntries(
    Object.entries(json).map(([name, server]) => [name, { ...server, transport: "stdio" }])
  );
  const opencode = Object.fromEntries(
    definitions.map(({ name, command, args }) => [
      name,
      { type: "local", command: [command, ...args], enabled: true }
    ])
  );
  return { definitions, json, sharedJson, opencode };
}

function mergeSharedCodexServers(raw, definitions) {
  return definitions.reduce((current, { name, command, args }) => {
    const serverName = name.replaceAll("-", "_");
    return mergeCodexServerTable(current, serverName, codexStdioBlock(serverName, command, args));
  }, raw);
}

export function configureShared(repoRoot, beforeCommit) {
  const paths = clientConfigPaths(repoRoot);
  const shared = sharedMcpDefinitions(repoRoot);
  const codexRaw = fs.existsSync(paths.codex) ? fs.readFileSync(paths.codex, "utf8") : "";
  const written = transactionalWrite(
    [
      [paths.claudeCode, prepareJsonServers(paths.claudeCode, "mcpServers", shared.sharedJson)],
      [paths.cursor, prepareJsonServers(paths.cursor, "mcpServers", shared.json)],
      [paths.vscode, prepareJsonServers(paths.vscode, "servers", shared.json)],
      [paths.codex, mergeSharedCodexServers(codexRaw, shared.definitions)],
      [paths.opencode, prepareJsonServers(paths.opencode, "mcp", shared.opencode)]
    ],
    beforeCommit
  );
  return { written };
}

/** Every MCP client config this repository owns, keyed by the schema each client expects. */
export function clientConfigPaths(repoRoot) {
  return {
    claudeCode: path.join(repoRoot, ".mcp.json"),
    cursor: path.join(repoRoot, ".cursor", "mcp.json"),
    vscode: path.join(repoRoot, ".vscode", "mcp.json"),
    codex: path.join(repoRoot, ".codex", "config.toml"),
    opencode: path.join(repoRoot, "opencode.json")
  };
}

export function configure(inputOptions, endpoint, beforeCommit) {
  const options = ensureBearerToken(inputOptions);
  const url = endpointUrl(endpoint);
  const server = { type: "http", url, headers: { Authorization: `Bearer ${options.bearerToken}` } };
  // Nanocoder loads the same project-root .mcp.json as Claude Code but selects
  // the HTTP transport with `transport` rather than `type` (its loader reads
  // `transport` and ignores unknown keys; Claude Code mirrors that for
  // `type`). One entry carrying both keys configures both agents.
  const sharedFileServer = { type: "http", transport: "http", url, headers: server.headers };
  // OpenCode's schema names the collection `mcp`, its type is `remote`, and a
  // server only registers when `enabled` is true.
  const opencodeServer = { type: "remote", url, enabled: true, headers: server.headers };
  const paths = clientConfigPaths(options.repoRoot);
  const codexRaw = fs.existsSync(paths.codex) ? fs.readFileSync(paths.codex, "utf8") : "";
  const shared = sharedMcpDefinitions(options.repoRoot);
  const codex = mergeSharedCodexServers(
    mergeCodexToml(codexRaw, url, options.bearerToken),
    shared.definitions
  );

  const written = transactionalWrite(
    [
      [
        paths.claudeCode,
        prepareJsonServers(paths.claudeCode, "mcpServers", {
          "unity-mcp-remote": sharedFileServer,
          ...shared.sharedJson
        })
      ],
      [
        paths.cursor,
        prepareJsonServers(paths.cursor, "mcpServers", {
          "unity-mcp-remote": server,
          ...shared.json
        })
      ],
      [
        paths.vscode,
        prepareJsonServers(paths.vscode, "servers", {
          "unity-mcp-remote": server,
          ...shared.json
        })
      ],
      [paths.codex, codex],
      [
        paths.opencode,
        prepareJsonServers(paths.opencode, "mcp", {
          "unity-mcp-remote": opencodeServer,
          ...shared.opencode
        })
      ]
    ],
    beforeCommit
  );
  return { url, written };
}

// ---------------------------------------------------------------------------
// Relay discovery and the bridge server
// ---------------------------------------------------------------------------

export function relayCandidates({
  platform = process.platform,
  arch = process.arch,
  home = os.homedir()
} = {}) {
  const root = path.join(home, ".unity", "relay");
  const names =
    platform === "win32"
      ? ["relay_win.exe", "relay_windows.exe", "relay.exe"]
      : platform === "darwin"
        ? [
            `relay_mac_${arch}.app/Contents/MacOS/relay_mac_${arch}`,
            `relay_macos_${arch}.app/Contents/MacOS/relay_macos_${arch}`,
            `relay_mac_${arch}`,
            "relay_mac",
            "relay"
          ]
        : platform === "linux"
          ? [`relay_linux_${arch}`, "relay_linux", "relay"]
          : [];
  return names.map((name) => path.join(root, ...name.split("/")));
}

export function findRelay(override, runtime = {}) {
  const candidates = override ? [path.resolve(override)] : relayCandidates(runtime);
  const found = candidates.find((candidate) => {
    if (!fs.existsSync(candidate) || !fs.statSync(candidate).isFile()) {
      return false;
    }
    if ((runtime.platform ?? process.platform) !== "win32") {
      try {
        fs.accessSync(candidate, fs.constants.X_OK);
      } catch {
        return false;
      }
    }
    return true;
  });
  if (!found) {
    fail(
      `Unity MCP relay not found or not executable. ${
        override ? `Checked: ${candidates[0]}` : `Searched: ${candidates.join(", ")}`
      }`
    );
  }
  return found;
}

export function buildRelayArgs(projectPath) {
  return ["--mcp", "--project-path", path.resolve(projectPath)];
}

export async function assertPortAvailable(port, host = DEFAULTS.bindHost) {
  await new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.once("error", (error) =>
      reject(new Error(`Port ${port} is unavailable on ${host}: ${error.message}`))
    );
    server.listen({ port, host, exclusive: true }, () => server.close(resolve));
  });
}

function authorized(request, token) {
  const received = Buffer.from(request.headers.authorization ?? "");
  const expected = Buffer.from(`Bearer ${token}`);
  return received.length === expected.length && timingSafeEqual(received, expected);
}

function log(options, level, message) {
  if (options.logLevel === "none" || (level === "debug" && options.logLevel !== "debug")) {
    return;
  }
  (level === "error" ? console.error : console.log)(message);
}

/**
 * Client-caused body failures carry the HTTP status and JSON-RPC error to report. Without this a
 * malformed body came back as HTTP 500 / -32603 "internal error", which clients retry forever.
 */
function bodyError(message, httpStatus, code, rpcMessage) {
  return Object.assign(new Error(message), { httpStatus, rpc: { code, message: rpcMessage } });
}

function readJsonBody(request, limitBytes, timeoutMs) {
  return new Promise((resolve, reject) => {
    let size = 0;
    const chunks = [];
    const timer = setTimeout(() => {
      // Without this a client that sends Content-Length headers and no body pins the socket (and
      // therefore shutdown) until Node's request timeout, which defaults to five minutes.
      request.pause();
      reject(bodyError("Request body timed out", 408, -32001, "Request body timed out"));
    }, timeoutMs);
    timer.unref();
    const settle = (action, value) => {
      clearTimeout(timer);
      action(value);
    };
    request.on("data", (chunk) => {
      size += chunk.length;
      if (size > limitBytes) {
        // Pause rather than destroy: the handler still has to write a 413, and destroying the socket
        // first is what turns an over-large body into an opaque ECONNRESET for the client.
        request.pause();
        settle(reject, bodyError("Request body too large", 413, -32600, "Request body too large"));
        return;
      }
      chunks.push(chunk);
    });
    request.once("error", (error) => settle(reject, error));
    request.once("end", () => {
      const raw = Buffer.concat(chunks).toString("utf8");
      if (!raw.trim()) {
        settle(resolve, undefined);
        return;
      }
      try {
        settle(resolve, JSON.parse(raw));
      } catch (error) {
        settle(
          reject,
          bodyError(`Invalid JSON body: ${error.message}`, 400, -32700, "Parse error")
        );
      }
    });
  });
}

function sendJson(response, statusCode, payload, closeConnection = false) {
  const body = JSON.stringify(payload);
  const headers = {
    "Content-Type": "application/json",
    "Content-Length": Buffer.byteLength(body)
  };
  if (closeConnection) {
    // The request body was never drained, so the connection cannot be reused.
    headers.Connection = "close";
  }
  response.writeHead(statusCode, headers);
  response.end(body);
}

export async function startBridge(inputOptions, runtime = {}) {
  let options = ensureBearerToken(inputOptions);
  const projectPath = requireProjectPath(options, runtime.projectRuntime);
  // Fold the discovered project back in so the startup banner and the returned options report the
  // directory the relay was actually pointed at, not the undefined the caller supplied.
  options = { ...options, projectPath };
  const relayPath = findRelay(options.relayPath, runtime.relayRuntime);
  await assertPortAvailable(options.port, options.bindHost);

  const maxSessions = options.maxSessions ?? DEFAULTS.maxSessions;
  const bodyTimeout = Math.min(options.sessionTimeout ?? Infinity, DEFAULTS.bodyTimeout);
  const sessions = new Map();
  const provisionalSessions = new Set();
  let starting = 0;

  const disposeSession = async (session) => {
    if (!session || session.disposed) {
      return;
    }
    log(options, "debug", `Disposing session ${session.sessionId ?? "(provisional)"}`);
    session.disposed = true;
    provisionalSessions.delete(session);
    if (session.sessionId) {
      sessions.delete(session.sessionId);
    }
    clearTimeout(session.timer);
    if (!session.stopping) {
      session.stopping = true;
      if (session.child.exitCode === null && session.child.signalCode === null) {
        session.child.kill("SIGTERM");
      }
      const force = setTimeout(() => {
        if (session.child.exitCode === null && session.child.signalCode === null) {
          session.child.kill("SIGKILL");
        }
      }, 3_000);
      force.unref();
    }
    await session.transport.close().catch(() => {});
  };

  const touch = (sessionId) => {
    const session = sessions.get(sessionId);
    if (!session || session.pendingRequests.size) {
      return;
    }
    clearTimeout(session.timer);
    session.timer = setTimeout(() => {
      disposeSession(session).catch(() => {});
    }, options.sessionTimeout);
    session.timer.unref();
  };

  const armRequestTimeout = (sessionId) => {
    const session = sessions.get(sessionId);
    if (!session) {
      return;
    }
    clearTimeout(session.timer);
    session.timer = setTimeout(() => {
      disposeSession(session).catch(() => {});
    }, options.requestTimeout);
    session.timer.unref();
  };

  const createSession = async () => {
    let sessionId;
    const transport = new StreamableHTTPServerTransport({
      sessionIdGenerator: () => randomUUID(),
      enableJsonResponse: true,
      onsessioninitialized: (id) => {
        sessionId = id;
        session.sessionId = id;
        provisionalSessions.delete(session);
        sessions.set(id, session);
        log(options, "debug", `Session ${id} initialized (${sessions.size}/${maxSessions})`);
        touch(id);
      }
    });
    const server = new Server(
      { name: "unity-helpers-unity-mcp-bridge", version: "1.0.0" },
      { capabilities: {} }
    );
    await server.connect(transport);
    const relayArgs = buildRelayArgs(projectPath);
    log(options, "debug", `Spawning relay: ${relayPath} ${relayArgs.join(" ")}`);
    const child = runtime.spawnRelay
      ? runtime.spawnRelay(relayPath, relayArgs)
      : spawn(relayPath, relayArgs, {
          stdio: ["pipe", "pipe", "pipe"],
          shell: false,
          windowsHide: true
        });
    const session = {
      child,
      server,
      transport,
      timer: undefined,
      stopping: false,
      disposed: false,
      sessionId: undefined,
      pendingRequests: new Set()
    };
    provisionalSessions.add(session);
    // A session that never reaches `onsessioninitialized` holds a live relay child, so it gets the
    // short idle timeout rather than the multi-minute active-request budget.
    session.timer = setTimeout(() => {
      disposeSession(session).catch(() => {});
    }, options.sessionTimeout);
    session.timer.unref();

    let buffer = "";
    child.stdout.on("data", (chunk) => {
      buffer += chunk.toString("utf8");
      const lines = buffer.split(/\r?\n/);
      buffer = lines.pop() ?? "";
      for (const line of lines) {
        if (!line.trim()) {
          continue;
        }
        try {
          const message = JSON.parse(line);
          if (message.id !== undefined && !message.method) {
            session.pendingRequests.delete(`${typeof message.id}:${message.id}`);
          }
          if (sessionId) {
            touch(sessionId);
          }
          Promise.resolve(transport.send(message)).catch((error) =>
            log(options, "error", `Relay response failed: ${error.message}`)
          );
        } catch {
          log(options, "error", `Unity relay emitted non-JSON output: ${line.slice(0, 200)}`);
        }
      }
    });
    child.stderr.on("data", (chunk) =>
      log(options, "error", `Unity relay: ${chunk.toString("utf8").trimEnd()}`)
    );
    child.stdin.on("error", (error) => {
      log(options, "error", `Unity relay input failed: ${error.message}`);
      disposeSession(session).catch(() => {});
    });
    child.once("error", (error) => {
      log(options, "error", `Unity relay failed: ${error.message}`);
      disposeSession(session).catch(() => {});
    });
    child.once("exit", () => {
      if (!session.stopping) {
        disposeSession(session).catch(() => {});
      }
    });

    transport.onmessage = (message) => {
      const startsRequest = message.id !== undefined && message.method;
      const wasIdle = session.pendingRequests.size === 0;
      if (startsRequest) {
        session.pendingRequests.add(`${typeof message.id}:${message.id}`);
      }
      child.stdin.write(`${JSON.stringify(message)}\n`);
      if (sessionId && startsRequest && wasIdle && session.pendingRequests.size) {
        armRequestTimeout(sessionId);
      } else if (sessionId) {
        touch(sessionId);
      }
    };
    transport.onclose = () => {
      disposeSession(session).catch(() => {});
    };
    transport.onerror = (error) => {
      log(options, "error", `MCP transport error: ${error.message}`);
      disposeSession(session).catch(() => {});
    };
    return transport;
  };

  const handle = async (request, response) => {
    try {
      const url = new URL(request.url ?? "/", `http://${request.headers.host ?? "localhost"}`);
      // Liveness only; it reveals nothing, so it is deliberately outside the bearer check. A probe
      // that has to hold the token is not a probe an orchestrator can run.
      if (url.pathname === "/healthz") {
        response.writeHead(200, { "Content-Type": "text/plain" });
        response.end("ok");
        return;
      }
      if (!authorized(request, options.bearerToken)) {
        response.setHeader("WWW-Authenticate", "Bearer");
        sendJson(response, 401, { error: "Unauthorized" });
        return;
      }
      if (
        url.pathname !== options.endpointPath ||
        !["POST", "GET", "DELETE"].includes(request.method ?? "")
      ) {
        sendJson(response, 404, { error: "Not found" });
        return;
      }

      const body =
        request.method === "POST"
          ? await readJsonBody(request, DEFAULTS.bodyLimitBytes, bodyTimeout)
          : undefined;
      const sessionId = request.headers["mcp-session-id"];
      let transport = sessionId ? sessions.get(sessionId)?.transport : undefined;
      if (!transport && request.method === "POST" && !sessionId && isInitializeRequest(body)) {
        // Every session owns a relay child process, so the count is capped rather than unbounded.
        // `starting` is bumped synchronously because createSession awaits before it registers.
        if (sessions.size + provisionalSessions.size + starting >= maxSessions) {
          sendJson(response, 503, {
            jsonrpc: "2.0",
            id: null,
            error: {
              code: -32000,
              message: `Too many concurrent MCP sessions (limit ${maxSessions}); close one or raise --max-sessions`
            }
          });
          return;
        }
        starting += 1;
        try {
          transport = await createSession();
        } finally {
          starting -= 1;
        }
      }
      if (!transport) {
        sendJson(response, sessionId ? 404 : 400, {
          jsonrpc: "2.0",
          id: null,
          error: {
            code: -32001,
            message: sessionId ? "Session not found" : "Initialize request required"
          }
        });
        return;
      }
      if (sessionId) {
        touch(sessionId);
      }
      await transport.handleRequest(request, response, body);
    } catch (error) {
      const status = error.httpStatus ?? 500;
      if (!response.headersSent) {
        // 413 and 408 both leave the request body undrained, so the socket cannot be reused.
        sendJson(
          response,
          status,
          {
            jsonrpc: "2.0",
            id: null,
            error: error.rpc ?? { code: -32603, message: "Bridge failure" }
          },
          status === 413 || status === 408
        );
      }
      log(options, status === 500 ? "error" : "debug", `Bridge request failed: ${error.message}`);
    }
  };

  const httpServer = http.createServer((request, response) => {
    // The catch inside `handle` can itself throw (a socket that died mid-response), and an unhandled
    // rejection is fatal to the process by default, so the outer promise is always caught.
    handle(request, response).catch((error) => {
      log(options, "error", `Bridge handler crashed: ${error.message}`);
      response.destroy();
    });
  });

  await new Promise((resolve, reject) => {
    httpServer.once("error", reject);
    httpServer.listen(options.port, options.bindHost, resolve);
  });

  let closeResolve;
  const closed = new Promise((resolve) => {
    closeResolve = resolve;
  });
  let closing = false;
  const close = async () => {
    if (closing) {
      return closed;
    }
    closing = true;
    await Promise.all(
      [...new Set([...sessions.values(), ...provisionalSessions])].map(disposeSession)
    );
    const stopped = new Promise((resolve) => httpServer.close(resolve));
    // Without this, an idle keep-alive socket or a client that stalled mid-body keeps `close()`
    // pending until Node's 300s request timeout expires.
    httpServer.closeAllConnections();
    await stopped;
    closeResolve();
    return closed;
  };
  return { close, closed, httpServer, options, bearerToken: options.bearerToken };
}

// ---------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------

/**
 * `--no-discover` narrows the candidate list to the configured endpoint; it does not skip the
 * handshake. Returning early without probing left `runProbe` with no `found` and an empty attempts
 * list, so `probe --no-discover` always failed and said nothing about why.
 */
async function resolveEndpoint(options, runtime = {}) {
  const configured = {
    host: options.host,
    port: options.port,
    endpointPath: options.endpointPath
  };
  const narrowed = options.discover
    ? runtime
    : { ...runtime, candidates: runtime.candidates ?? [configured] };

  const { found, attempts } = await discoverEndpoint(options, narrowed);
  if (found) {
    return {
      endpoint: { host: found.host, port: found.port, endpointPath: found.endpointPath },
      attempts,
      found
    };
  }
  // With discovery off the caller named the endpoint, so keep it: `configure` still writes it, and
  // `probe` reports the attempt that failed rather than an empty list.
  return { endpoint: options.discover ? undefined : configured, attempts };
}

export async function runProbe(options, runtime = {}) {
  const { found, attempts } = await resolveEndpoint(options, runtime);
  if (!found) {
    // "Nothing responded" is the wrong diagnosis when a healthy bridge answered and was rejected
    // for serving another project. Naming that distinction is most of the value of the check.
    const noEditor = attempts.find((attempt) => attempt.status === "no-editor");
    if (noEditor) {
      fail(
        `A Unity MCP bridge is running at ${noEditor.url}, but no Unity editor is attached to it, ` +
          `so it exposes no Unity_* tools. Open the editor on this repository's Unity project and ` +
          `approve the connection in its Unity MCP Server settings, then re-run.\n` +
          `Attempts:\n${describeAttempts(attempts)}`
      );
    }
    const mismatch = attempts.find((attempt) => attempt.status === "project-mismatch");
    if (mismatch) {
      fail(
        `A Unity MCP bridge answered at ${mismatch.url} but has a different project open ` +
          `(${mismatch.detail}). Point ${ENV_KEYS.projectRoot} at the project you want, start a ` +
          `bridge for it on its own port, or pass --any-project to accept this one.\n` +
          `Attempts:\n${describeAttempts(attempts)}`
      );
    }
    const occupied = attempts.find((attempt) => attempt.status === "port-occupied");
    if (occupied) {
      fail(
        `${occupied.url} answered ${occupied.detail}. That is a WebSocket-only server, not the ` +
          `unity-mcp relay, so another process owns port ${occupied.port} - most often a bridge ` +
          `belonging to a neighbouring repository, or the Unity editor's own bridge grabbing the ` +
          `port while this relay was down. Stop that process or pass --port to pick another.\n` +
          `Attempts:\n${describeAttempts(attempts)}`
      );
    }
    fail(`No Unity MCP endpoint responded. Attempts:\n${describeAttempts(attempts)}`);
  }
  console.log(`Unity MCP is reachable at ${found.url} (protocol ${found.protocolVersion}).`);
  console.log(
    found.projectRoot
      ? `Unity project: ${found.projectRoot}`
      : "Unity project: UNKNOWN - this endpoint did not answer Unity_ManageEditor GetProjectRoot."
  );
  if (!options.expectedProjectRoot) {
    console.warn(
      `No ${ENV_KEYS.projectRoot} is pinned, so nothing verified that this is the right editor. ` +
        "Run `npm run unity:mcp:configure` to pin it."
    );
  }
  return found;
}

export async function runConfigure(options, runtime = {}) {
  const { endpoint, attempts, found } = await resolveEndpoint(options, runtime);
  // `unauthorized` means a bridge IS running there and only the token is wrong. Falling back to the
  // default endpoint and minting a fresh token would guarantee a 401 and persist the bogus token
  // into .env.local and every agent config, so this refuses to write anything.
  // A live bridge serving a DIFFERENT Unity project is the failure this tooling exists to stop
  // (issue #333). Writing its endpoint would point every agent at the wrong editor while every
  // check reported success, which is exactly how the old scripts behaved.
  const noEditor = found ? undefined : attempts.find((a) => a.status === "no-editor");
  if (noEditor) {
    fail(
      `A Unity MCP bridge is running at ${noEditor.url} with no Unity editor attached, so it ` +
        `exposes no Unity_* tools and cannot be verified. Nothing was written. Open the editor on ` +
        `this repository's Unity project, approve the connection, then re-run configure.`
    );
  }
  const mismatch = found ? undefined : attempts.find((a) => a.status === "project-mismatch");
  if (mismatch) {
    fail(
      `A Unity MCP bridge is running at ${mismatch.url} but it has a different project open ` +
        `(${mismatch.detail}). Nothing was written. Start a bridge for this repo's Unity project ` +
        `on its own port, or re-run with --any-project to accept whatever answers.`
    );
  }
  const unauthorized = found ? undefined : attempts.find((a) => a.status === "unauthorized");
  if (unauthorized) {
    fail(
      `A Unity MCP bridge is running at ${unauthorized.url} but rejected the bearer token ` +
        `(${unauthorized.detail}). Nothing was written and no token was generated: copy ` +
        `${ENV_KEYS.bearerToken} from the host's .env.local into ` +
        `${path.join(options.repoRoot, ".env.local")}, or pass --token, then re-run configure.`
    );
  }
  // A foreign WebSocket server on the port is as unusable as a bridge with the wrong token, and
  // writing its endpoint into every agent config would point each agent at something that can never
  // answer. Refuse for the same reason the three cases above do.
  const occupied = found ? undefined : attempts.find((a) => a.status === "port-occupied");
  if (occupied) {
    fail(
      `${occupied.url} answered ${occupied.detail}, which is a WebSocket-only server rather than ` +
        `the unity-mcp relay: another process owns port ${occupied.port}. Nothing was written. ` +
        `Stop that process, or re-run configure with --port pointing somewhere free.`
    );
  }
  const target = endpoint ?? {
    host: options.host,
    port: options.port,
    endpointPath: options.endpointPath
  };
  if (!found) {
    console.warn(
      `No Unity MCP endpoint responded; configuring ${endpointUrl(target)} anyway. Attempts:\n${describeAttempts(attempts)}`
    );
  }
  const pinned = pinProjectRoot(options, found);
  const { url, written } = configure(pinned.options, target);
  const summary = written.length
    ? written.map((filePath) => path.relative(options.repoRoot, filePath)).join(", ")
    : "no changes";
  console.log(`Configured Unity MCP endpoint ${url} (${summary}).`);
  if (found?.projectRoot) {
    console.log(
      `Unity project: ${found.projectRoot}${pinned.wrote ? " (pinned to .env.local)" : ""}`
    );
  }
  return url;
}

export async function runBridge(options) {
  const running = await startBridge(options);
  console.log(`Unity project: ${running.options.projectPath}`);
  console.log(
    `Unity MCP bridge: http://${running.options.bindHost}:${running.options.port}${running.options.endpointPath} (bearer authentication required)`
  );
  const stop = () => {
    running.close().catch((error) => console.error(`Bridge shutdown failed: ${error.message}`));
  };
  process.once("SIGINT", stop);
  process.once("SIGTERM", stop);
  await running.closed;
  process.removeListener("SIGINT", stop);
  process.removeListener("SIGTERM", stop);
}

function usage() {
  return [
    "Usage: node scripts/mcp/unity-mcp.mjs <probe|configure|configure-shared|bridge> [options]",
    "",
    "  probe      Discover a live Unity MCP endpoint and complete an initialize handshake.",
    "  configure  Discover, then write .mcp.json, .cursor/mcp.json, .vscode/mcp.json, .codex/config.toml, opencode.json.",
    "  configure-shared  Write GitHub and Z.AI servers without requiring the Unity bridge.",
    "  bridge     Serve the Unity relay over authenticated streamable HTTP (run next to Unity).",
    "",
    "Options:",
    "  --host HOST                 Endpoint host; the only host discovery probes",
    "  --port PORT                 Endpoint port; the only port discovery probes",
    "  --path PATH                 Streamable HTTP path (default: /mcp)",
    "  --no-discover               Probe only the configured host/port, not the fallbacks",
    "  --bind HOST                 Bridge bind interface (default: 0.0.0.0)",
    "  --project PATH              Unity project directory (bridge only)",
    "  --relay PATH                Unity relay executable override",
    "  --token TOKEN               32-256 character bearer token (generated into .env.local if omitted)",
    "  --project-root PATH         Unity project the endpoint MUST have open; pinned by configure",
    "  --any-project               Accept whatever project answers (disables the identity check)",
    "  --timeout MS                Per-endpoint handshake timeout (default: 5000)",
    "  --connect-timeout MS        Per-endpoint TCP connect timeout (default: 750)",
    "  --session-timeout MS        Idle session timeout (default: 60000)",
    "  --request-timeout MS        Active-request hard limit (default: 300000)",
    "  --max-sessions COUNT        Concurrent bridge sessions, one relay each (default: 8)",
    "  --protocol-version VERSION  MCP protocol version (default: 2025-11-25)",
    "  --log-level LEVEL           debug, info, or none"
  ].join("\n");
}

export async function main(argv = process.argv.slice(2)) {
  const [command, ...rest] = argv;
  if (!command || command === "--help" || command === "-h") {
    console.log(usage());
    return;
  }
  if (!["bridge", "configure", "configure-shared", "probe"].includes(command)) {
    fail(`Unknown command: ${command}`);
  }
  if (rest.includes("--help") || rest.includes("-h")) {
    console.log(usage());
    return;
  }
  const args = parseArgs(rest);
  if (args._.length) {
    fail(`Unexpected argument: ${args._[0]}`);
  }
  const options = resolveOptions(args);
  if (command === "probe") {
    await runProbe(options);
  }
  if (command === "configure") {
    await runConfigure(options);
  }
  if (command === "configure-shared") {
    const { written } = configureShared(options.repoRoot);
    const summary = written.length
      ? written.map((filePath) => path.relative(options.repoRoot, filePath)).join(", ")
      : "no changes";
    console.log(`Configured shared MCP servers (${summary}).`);
  }
  if (command === "bridge") {
    await runBridge(options);
  }
}

const entry = process.argv[1] ? pathToFileURL(path.resolve(process.argv[1])).href : "";
if (import.meta.url === entry) {
  main().catch((error) => {
    console.error(`unity-mcp: ${error.message}`);
    process.exitCode = 1;
  });
}
