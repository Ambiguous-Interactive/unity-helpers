// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Compares a directory of PractRand reports against the checked-in expectations
// manifest. PractRand's RNG_test exits 0 whether or not it found a failure, so
// the verdict has to come from the report text rather than from a status code.

import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";

const UNIT_BYTES = {
  KB: 1024,
  MB: 1024 * 1024,
  GB: 1024 * 1024 * 1024,
  TB: 1024 * 1024 * 1024 * 1024
};

export function parseLength(token) {
  const match = /^([0-9]+)(KB|MB|GB|TB)$/.exec(String(token).trim());
  if (!match) {
    throw new Error(`Unrecognized PractRand length '${token}'. Expected e.g. 1GB, 16MB, 64KB.`);
  }
  return Number(match[1]) * UNIT_BYTES[match[2]];
}

// A definitive failure is the literal "FAIL" evaluation column. PractRand also
// emits "unusual", "mildly suspicious" and "VERY SUSPICIOUS", none of which are
// failures; matching on the word boundary keeps those out.
export function classifyReport(text) {
  const failures = [];
  let lastLength = "";
  for (const line of text.split(/\r?\n/)) {
    const lengthMatch = /^length=\s*(.+?),\s*time=/.exec(line.trim());
    if (lengthMatch) {
      lastLength = lengthMatch[1].trim();
      continue;
    }
    if (/\bFAIL\b/.test(line)) {
      failures.push({
        test: line.trim().split(/\s{2,}/)[0],
        length: lastLength,
        line: line.trim()
      });
    }
  }
  return { observed: failures.length > 0 ? "fail" : "pass", failures, lastLength };
}

function readArg(argv, name, fallback) {
  const index = argv.indexOf(name);
  if (index === -1 || index + 1 >= argv.length) {
    if (fallback !== undefined) {
      return fallback;
    }
    throw new Error(`Missing required argument ${name}.`);
  }
  return argv[index + 1];
}

/**
 * The manifest records one outcome per generator PER WIDTH. `NextUlong` is not `NextUint`
 * rearranged: five generators answer it from one raw word whose high half appears in no 32-bit
 * draw, and even the ones that do build it from two `NextUint` draws are packed high-word-first
 * and written little-endian, so the 64-bit stream is the 32-bit one with each adjacent word pair
 * swapped. `SystemRandom` is the proof -- it fails 32-bit at exactly 8GB and is clean through 8GB
 * at 64-bit -- which is why a shared `expected` would be wrong (#544).
 */
export function outcomeFor(generator, width) {
  const outcome = generator.widths ? generator.widths[String(width)] : undefined;
  if (outcome === undefined) {
    throw new Error(
      `${generator.name} has no recorded outcome for width ${width}. ` +
        `The manifest must carry every width the workflow runs.`
    );
  }
  return outcome;
}

export function evaluate(manifest, reports, budgetToken, width) {
  const budgetBytes = parseLength(budgetToken);
  const results = [];
  for (const generator of manifest.generators) {
    const outcome = outcomeFor(generator, width);
    const report = reports.get(generator.name);
    if (report === undefined) {
      results.push({
        name: generator.name,
        width,
        expected: outcome.expected,
        observed: "missing",
        status: "error",
        detail: "No PractRand report was produced for this generator."
      });
      continue;
    }
    const { observed, failures, lastLength } = classifyReport(report);
    const base = {
      name: generator.name,
      width,
      quality: generator.quality,
      expected: outcome.expected,
      observed,
      reachedLength: lastLength,
      failures: failures.slice(0, 5)
    };
    if (observed === outcome.expected) {
      results.push({ ...base, status: "ok", detail: "Outcome matches the manifest." });
      continue;
    }
    if (outcome.expected === "pass") {
      results.push({
        ...base,
        status: "error",
        detail:
          `${generator.name} is expected to pass ${manifest.battery.name} ${manifest.battery.version} ` +
          `on the ${width}-bit stream ` +
          `but failed at ${lastLength || "an unrecorded length"}. This is a statistical regression in the generator.`
      });
      continue;
    }
    // An expected-failure control that passed. That is only evidence of a broken
    // harness when the run was long enough to have reached the length where the
    // failure is known to appear; otherwise the run simply could not see it.
    // A control with no measured failsBy has never been caught by this battery,
    // so no budget can turn its pass into evidence of a broken harness.
    const failsByBytes = outcome.failsBy ? parseLength(outcome.failsBy) : Number.POSITIVE_INFINITY;
    if (budgetBytes >= failsByBytes) {
      results.push({
        ...base,
        status: "error",
        detail:
          `${generator.name} is a deliberate expected-failure control that must fail by ${outcome.failsBy} ` +
          `on the ${width}-bit stream, ` +
          `but it PASSED a ${budgetToken} run. A weak generator passing is evidence the harness is broken, ` +
          `not that the generator improved.`
      });
      continue;
    }
    results.push({
      ...base,
      status: "inconclusive",
      detail: outcome.failsBy
        ? `${generator.name} only fails at ${outcome.failsBy}; a ${budgetToken} budget is too short to ` +
          `discriminate it. Raise the byte budget to assert this control.`
        : `${generator.name} has no measured failing length, so this battery cannot discriminate it at any ` +
          `budget. Recorded as inconclusive by design; see the manifest reason.`
    });
  }
  return results;
}

export function renderMarkdown(manifest, results, context) {
  const icons = { ok: "OK", error: "MISMATCH", inconclusive: "INCONCLUSIVE" };
  const lines = [
    `# Random quality battery report (${context.width}-bit stream)`,
    "",
    `- Battery: **${manifest.battery.name} ${manifest.battery.version}** (\`${manifest.battery.sha256}\`)`,
    `- Source: ${manifest.battery.url}`,
    `- Seed: \`${context.seed}\``,
    `- Stream width: **${context.width}-bit**`,
    `- Byte budget per generator: **${context.budget}**`,
    `- Command: \`${context.command}\``,
    `- Run: ${context.runUrl || "(local)"}`,
    "",
    "| Generator | Quality | Expected | Observed | Reached | Result |",
    "| --- | --- | --- | --- | --- | --- |"
  ];
  for (const result of results) {
    lines.push(
      `| ${result.name} | ${result.quality || "-"} | ${result.expected} | ${result.observed} | ` +
        `${result.reachedLength || "-"} | ${icons[result.status] || result.status} |`
    );
  }
  const problems = results.filter((result) => result.status === "error");
  if (problems.length > 0) {
    lines.push("", "## Mismatches", "");
    for (const problem of problems) {
      lines.push(`### ${problem.name}`, "", problem.detail, "");
      for (const failure of problem.failures || []) {
        lines.push(`- \`${failure.line}\` at ${failure.length}`);
      }
      lines.push("");
    }
  }
  return lines.join("\n");
}

function main() {
  const argv = process.argv.slice(2);
  const manifestPath = readArg(argv, "--manifest", "scripts/random-quality/expected-outcomes.json");
  const reportsDir = readArg(argv, "--reports");
  const budget = readArg(argv, "--budget");
  const seed = readArg(argv, "--seed");
  const command = readArg(argv, "--command", "RNG_test stdin32");
  const width = readArg(argv, "--width", "32");
  const runUrl = readArg(argv, "--run-url", "");
  const outMarkdown = readArg(argv, "--out-md", "");
  const outJson = readArg(argv, "--out-json", "");

  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  const reports = new Map();
  if (fs.existsSync(reportsDir)) {
    for (const entry of fs.readdirSync(reportsDir)) {
      if (entry.endsWith(".txt")) {
        reports.set(
          path.basename(entry, ".txt"),
          fs.readFileSync(path.join(reportsDir, entry), "utf8")
        );
      }
    }
  }

  // Drift guard: a generator added to the host but not the manifest would
  // otherwise be silently untested.
  const unlisted = [...reports.keys()].filter(
    (name) => !manifest.generators.some((generator) => generator.name === name)
  );
  const results = evaluate(manifest, reports, budget, width);
  for (const name of unlisted) {
    results.push({
      name,
      expected: "(unlisted)",
      observed: classifyReport(reports.get(name)).observed,
      status: "error",
      failures: [],
      detail: `${name} produced a report but is absent from the expectations manifest. Add it to ${manifestPath}.`
    });
  }

  const markdown = renderMarkdown(manifest, results, { seed, budget, command, runUrl, width });
  if (outMarkdown) {
    fs.writeFileSync(outMarkdown, `${markdown}\n`, "utf8");
  }
  if (outJson) {
    fs.writeFileSync(
      outJson,
      `${JSON.stringify({ manifest: manifest.battery, seed, budget, width, results }, null, 2)}\n`,
      "utf8"
    );
  }
  process.stdout.write(`${markdown}\n`);

  const mismatches = results.filter((result) => result.status === "error");
  process.stdout.write(`mismatch-count=${mismatches.length}\n`);
  if (mismatches.length > 0) {
    process.exitCode = 1;
  }
}

// `file://` + a raw path is not a file URL: import.meta.url percent-encodes, so a repository
// checked out under a path containing a space silently fails this comparison, main() never runs,
// and the step emits no verdict at all -- which reads exactly like a clean battery. pathToFileURL
// does the encoding, matching the idiom already used in scripts/mcp/unity-mcp.mjs.
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main();
}
