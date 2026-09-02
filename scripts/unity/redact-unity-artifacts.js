"use strict";

/**
 * Remove credential material from Unity build output before it is uploaded as a CI artifact.
 *
 * Unity writes its license identity into `unity.log` and `configure.log` during activation. GitHub
 * masks registered secrets in rendered job logs, but it does not touch the bytes of an uploaded
 * artifact, so on a public repository those logs publish the serial to anyone who can download the
 * artifact. This rewrites every credential value in place and reports what it removed by kind.
 *
 * Run it over the artifact root before every upload step. It is idempotent: no placeholder it
 * writes can match a credential pattern, so a second pass over redacted output changes nothing.
 */

const fs = require("fs");
const path = require("path");
const { looksBinary, redactCredentials } = require("./credential-patterns.js");

/** Larger than any Unity log this repository produces, and small enough to read into memory. */
const MAXIMUM_FILE_BYTES = 256 * 1024 * 1024;

function fail(message) {
  throw new Error(message);
}

function toPosixPath(value) {
  return value.split(path.sep).join("/");
}

function listFiles(root) {
  const found = [];
  const walk = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true }).sort()) {
      const absolute = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        walk(absolute);
      } else if (entry.isFile()) {
        found.push(absolute);
      }
    }
  };
  walk(root);
  return found.sort();
}

/**
 * Redact every file under `root`. Skipped files are reported rather than silently ignored, because
 * a file this cannot read is a file whose contents were never checked.
 */
function redactDirectory(root) {
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    fail(`${root} is not a directory.`);
  }
  const changed = [];
  const skipped = [];
  const totals = new Map();
  for (const absolute of listFiles(root)) {
    const relative = toPosixPath(path.relative(root, absolute));
    const size = fs.statSync(absolute).size;
    if (size > MAXIMUM_FILE_BYTES) {
      skipped.push({
        path: relative,
        reason: `is ${size} bytes, over the ${MAXIMUM_FILE_BYTES} cap`
      });
      continue;
    }
    let bytes;
    try {
      bytes = fs.readFileSync(absolute);
    } catch (error) {
      skipped.push({ path: relative, reason: `could not be read: ${error.message}` });
      continue;
    }
    if (looksBinary(bytes)) {
      continue;
    }
    const text = bytes.toString("utf8");
    const { redacted, counts } = redactCredentials(text);
    if (counts.size === 0) {
      continue;
    }
    try {
      fs.writeFileSync(absolute, redacted, "utf8");
    } catch (error) {
      fail(`${relative} contains credential material but could not be rewritten: ${error.message}`);
    }
    for (const [id, count] of counts) {
      totals.set(id, (totals.get(id) ?? 0) + count);
    }
    changed.push({ path: relative, counts: [...counts.keys()].sort() });
  }
  return { changed, skipped, totals };
}

function formatSummary(root, result) {
  const lines = [];
  if (result.changed.length === 0) {
    lines.push(`No credential material found under ${root}.`);
  } else {
    const byKind = [...result.totals.entries()]
      .sort((left, right) => (left[0] < right[0] ? -1 : 1))
      .map(([id, count]) => `${id} x${count}`)
      .join(", ");
    lines.push(`Redacted ${result.changed.length} file(s) under ${root}: ${byKind}.`);
    for (const file of result.changed) {
      lines.push(`  ${file.path}: ${file.counts.join(", ")}`);
    }
  }
  for (const file of result.skipped) {
    lines.push(`  WARNING: ${file.path} was not scanned because it ${file.reason}.`);
  }
  return `${lines.join("\n")}\n`;
}

function usage() {
  return `Usage: node scripts/unity/redact-unity-artifacts.js <directory> [<directory>...]

Rewrites credential values in every text file under each directory before the tree is uploaded as a
CI artifact. Missing directories are skipped so one call can cover every test mode of a run.
`;
}

function parseArgs(argv) {
  const roots = [];
  for (let index = 2; index < argv.length; index++) {
    const argument = argv[index];
    if (argument === "--help" || argument === "-h") {
      return { roots, help: true };
    }
    if (argument.startsWith("-")) {
      fail(`Unknown option ${argument}.`);
    }
    roots.push(argument);
  }
  return { roots, help: false };
}

function runCli(argv, write = (text) => process.stdout.write(text)) {
  const { roots, help } = parseArgs(argv);
  if (help || roots.length === 0) {
    write(usage());
    return help ? 0 : 1;
  }
  for (const root of roots) {
    const resolved = path.resolve(root);
    if (!fs.existsSync(resolved)) {
      write(`Skipping ${root}; it does not exist.\n`);
      continue;
    }
    write(formatSummary(root, redactDirectory(resolved)));
  }
  return 0;
}

if (require.main === module) {
  try {
    process.exitCode = runCli(process.argv);
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { formatSummary, parseArgs, redactDirectory, runCli, usage };
