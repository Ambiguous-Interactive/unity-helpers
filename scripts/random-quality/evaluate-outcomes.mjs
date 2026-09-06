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

// PractRand terminates every result block with a blank line. A completed block has either
// positive test counts or a table of evaluated results; a header alone is not a measurement.
export function classifyReport(text) {
  const failures = [];
  let lastLength = "";
  let reachedBytes = 0;
  const invalid = (detail) => ({ observed: "invalid", failures, lastLength, reachedBytes, detail });
  if (typeof text !== "string") {
    return invalid("The report is not text.");
  }
  const normalized = text.replace(/^\uFEFF/, "").replaceAll("\r\n", "\n");
  const header =
    /^RNG_test using PractRand version (\S+)\nRNG = RNG_stdin(32|64), seed = unknown\ntest set = core, folding = standard \(\2 bit\)\n\n/.exec(
      normalized
    );
  if (!header) {
    return invalid("Missing or malformed PractRand version, stream or test-set header.");
  }
  const version = header[1];
  const width = header[2];
  const blocks = normalized.slice(header[0].length).split(/(?=^rng=)/m);
  let completedBlocks = 0;
  for (const block of blocks) {
    if (block.length === 0) {
      continue;
    }
    if (!/\n[ \t]*\n$/.test(block)) {
      return invalid("The final result block is unterminated.");
    }
    const lines = block.trimEnd().split("\n");
    if (lines[0] !== `rng=RNG_stdin${width}, seed=unknown`) {
      return invalid("A result block has a missing or inconsistent stream header.");
    }
    const length = /^length=\s*(.+?\(2\^(\d+) bytes\)),\s*time=\s*\d+(?:\.\d+)? seconds$/.exec(
      lines[1] || ""
    );
    const exponent = length ? Number(length[2]) : Number.NaN;
    if (!Number.isInteger(exponent) || exponent < 0 || exponent > 52) {
      return invalid("A result block has no valid measured byte length.");
    }
    const bytes = 2 ** exponent;
    const display =
      /^(\d+(?:\.\d+)?) (kilobyte|megabyte|gigabyte|terabyte)s? \(2\^\d+ bytes\)$/.exec(length[1]);
    const unit = display
      ? { kilobyte: "KB", megabyte: "MB", gigabyte: "GB", terabyte: "TB" }[display[2]]
      : undefined;
    if (!display || Number(display[1]) * UNIT_BYTES[unit] !== bytes) {
      return invalid("The displayed length does not agree with its measured byte exponent.");
    }
    if (bytes <= reachedBytes) {
      return invalid("Measured byte lengths must increase between result blocks.");
    }
    const body = lines.slice(2).map((line) => line.trim());
    const clean = body.length === 1 && /^no anomalies in [1-9]\d* test result\(s\)$/.test(body[0]);
    if (!clean) {
      if (!/^Test Name\s+Raw\s+Processed\s+Evaluation$/.test(body[0] || "")) {
        return invalid(
          "A result block contains neither test counts nor an evaluated result table."
        );
      }
      let evaluated = 0;
      for (let index = 1; index < body.length; index += 1) {
        const line = body[index];
        if (/^\.\.\.and \d+ test result\(s\) without anomalies$/.test(line)) {
          if (index !== body.length - 1 || evaluated === 0) {
            return invalid("An anomaly summary does not follow a complete result table.");
          }
          continue;
        }
        const result =
          /^(\S.*?)\s*R[=<>]\s*([-+]?\d+(?:\.\d+)?(?:e[-+]?\d+)?)\s+(?:(?:p(?:~=|\s*=)\s*((?:1-)?(?:\d+(?:\.\d*)?|\.\d+)(?:e[-+]?\d+)?)|"(?:pass|fail)")\s+)?(normal(?:ish)?|unusual|(?:mildly |very )?suspicious|FAIL[ !]*)$/i.exec(
            line
          );
        if (!result) {
          return invalid("A result table contains an incomplete or unrecognized evaluation row.");
        }
        const raw = Number(result[2]);
        const probability =
          result[3] === undefined ? undefined : Number(result[3].replace(/^1-/, ""));
        if (
          !Number.isFinite(raw) ||
          (probability !== undefined &&
            (!Number.isFinite(probability) || probability < 0 || probability > 1))
        ) {
          return invalid(
            "A result row contains a non-finite raw value or an out-of-range probability."
          );
        }
        evaluated += 1;
        if (/^FAIL(?:[ !]*)$/i.test(result[4])) {
          failures.push({ test: result[1], length: length[1].trim(), line });
        }
      }
      if (evaluated === 0) {
        return invalid("The result table contains no evaluated tests.");
      }
    }
    completedBlocks += 1;
    lastLength = length[1].trim();
    reachedBytes = bytes;
  }
  if (completedBlocks === 0) {
    return invalid("The report contains no completed result blocks.");
  }
  return {
    observed: failures.length > 0 ? "fail" : "pass",
    failures,
    lastLength,
    reachedBytes,
    version,
    width
  };
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
    const classified = classifyReport(report);
    const { observed, failures, lastLength, reachedBytes } = classified;
    let reportError = classified.detail;
    if (
      !reportError &&
      (classified.version !== manifest.battery.version || classified.width !== String(width))
    ) {
      reportError = "The report version or stream width does not match this campaign.";
    }
    if (!reportError && observed === "pass" && reachedBytes < budgetBytes) {
      reportError = `The clean report reached only ${lastLength}, short of the requested ${budgetToken}.`;
    }
    if (reportError) {
      results.push({
        name: generator.name,
        width,
        expected: outcome.expected,
        observed,
        reachedLength: lastLength,
        status: "error",
        detail: `Invalid or incomplete PractRand report: ${reportError}`
      });
      continue;
    }
    const base = {
      name: generator.name,
      width,
      quality: generator.quality,
      expected: outcome.expected,
      observed,
      reachedLength: lastLength,
      failures: failures.slice(0, 5)
    };
    if (
      outcome.expected === "pass" &&
      observed === "pass" &&
      (outcome.cleanThrough === null || reachedBytes < parseLength(outcome.cleanThrough))
    ) {
      results.push({
        ...base,
        status: "inconclusive",
        detail:
          outcome.cleanThrough === null
            ? `${generator.name} has no recorded clean length on the ${width}-bit stream. ` +
              `This report is an initial measurement, not agreement with an established baseline. ` +
              `Review the report before recording cleanThrough; see the manifest reason.`
            : `${generator.name} was measured only through ${lastLength}, below its recorded ` +
              `${outcome.cleanThrough} baseline. This run cannot confirm that baseline at its recorded depth.`
      });
      continue;
    }
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
          `but failed at ${lastLength || "an unrecorded length"}. ` +
          (outcome.cleanThrough === null
            ? "No passing baseline was recorded; review this failure and the generator's quality rating."
            : "This is a statistical regression in the generator.")
      });
      continue;
    }
    // An expected-failure control that passed. That is only evidence of a broken
    // harness when the run was long enough to have reached the length where the
    // failure is known to appear; otherwise the run simply could not see it.
    // A control with no measured failsBy has never been caught by this battery,
    // so no budget can turn its pass into evidence of a broken harness.
    const failsByBytes = outcome.failsBy ? parseLength(outcome.failsBy) : Number.POSITIVE_INFINITY;
    if (reachedBytes >= failsByBytes) {
      results.push({
        ...base,
        status: "error",
        detail:
          `${generator.name} is a deliberate expected-failure control that must fail by ${outcome.failsBy} ` +
          `on the ${width}-bit stream, ` +
          `but it PASSED through ${lastLength}. A weak generator passing is evidence the harness is broken, ` +
          `not that the generator improved.`
      });
      continue;
    }
    results.push({
      ...base,
      status: "inconclusive",
      detail: outcome.failsBy
        ? `${generator.name} only fails at ${outcome.failsBy}; the measured ${lastLength} is too short to ` +
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
    `- Scope: **${context.scope}** (${results.length} generators)`,
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
  const inconclusive = results.filter((result) => result.status === "inconclusive");
  if (inconclusive.length > 0) {
    lines.push("", "## Inconclusive outcomes", "");
    for (const result of inconclusive) {
      lines.push(`- **${result.name}**: ${result.detail}`);
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
  const selectedNames = argv.includes("--generators")
    ? JSON.parse(readArg(argv, "--generators"))
    : manifest.generators.map((generator) => generator.name);
  if (
    !Array.isArray(selectedNames) ||
    selectedNames.length === 0 ||
    new Set(selectedNames).size !== selectedNames.length ||
    selectedNames.some(
      (name) =>
        typeof name !== "string" ||
        !manifest.generators.some((generator) => generator.name === name)
    )
  ) {
    throw new Error(
      "--generators must be a nonempty JSON array of unique manifest generator names."
    );
  }
  const selectedManifest = {
    ...manifest,
    generators: manifest.generators.filter((generator) => selectedNames.includes(generator.name))
  };
  const scope = selectedNames.length === manifest.generators.length ? "full" : "selected";
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
  const unlisted = [...reports.keys()].filter((name) => !selectedNames.includes(name));
  const results = evaluate(selectedManifest, reports, budget, width);
  for (const name of unlisted) {
    results.push({
      name,
      expected: "(unlisted)",
      observed: classifyReport(reports.get(name)).observed,
      status: "error",
      failures: [],
      detail: `${name} produced a report but is absent from the selected inventory in ${manifestPath}.`
    });
  }

  const recoverable =
    scope === "full" &&
    seed === manifest.measurement.seed &&
    results.every((result) => result.status === "ok");
  const markdown = renderMarkdown(manifest, results, {
    seed,
    budget,
    command,
    runUrl,
    width,
    scope
  });
  if (outMarkdown) {
    fs.writeFileSync(outMarkdown, `${markdown}\n`, "utf8");
  }
  if (outJson) {
    fs.writeFileSync(
      outJson,
      `${JSON.stringify({ manifest: manifest.battery, seed, budget, width, scope, recoverable, results }, null, 2)}\n`,
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
