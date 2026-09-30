"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { indexByKey, alignTable } = require("./render-perf-deltas.js");
const { inferFromFileName } = require("./extract-perf-metrics.js");

const START = "<!-- CURRENT_BENCHMARK_RESULTS_START -->";
const END = "<!-- CURRENT_BENCHMARK_RESULTS_END -->";

function text(value) {
  return String(value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/[\\`*_{}[\]()#!|]/g, "\\$&")
    .replace(/[\r\n]+/g, " ");
}

function validateComplete(decision, metrics, provenance) {
  const expected = decision.expectedFiles;
  const actual = decision.actualFiles;
  if (
    decision.invalidSuccessfulMatrix ||
    !Array.isArray(expected) ||
    expected.length === 0 ||
    new Set(expected).size !== expected.length ||
    !Array.isArray(actual) ||
    actual.length !== expected.length ||
    [...actual].sort().some((file, index) => file !== [...expected].sort()[index])
  ) {
    throw new Error("Publication requires an exact, nonempty complete result matrix.");
  }
  if (!Array.isArray(metrics) || metrics.length === 0) {
    throw new Error("Publication requires measured metrics.");
  }
  indexByKey(metrics);
  const scopes = new Set();
  for (const file of expected) {
    if (typeof file !== "string" || path.basename(file) !== file) {
      throw new Error("Invalid benchmark result identity.");
    }
    const scope = inferFromFileName(file);
    if (!scope.unityVersion || !scope.testMode) {
      throw new Error("Result identities must name their Unity version and test mode.");
    }
    scopes.add(`${scope.unityVersion}/${scope.testMode}`);
  }
  const measuredScopes = new Set();
  for (const metric of metrics) {
    const scope = `${metric.unityVersion}/${metric.testMode}`;
    if (!scopes.has(scope)) {
      throw new Error("Measured metric scope does not match the complete result matrix.");
    }
    if (
      metric.sampleCount != null &&
      (!Number.isInteger(metric.sampleCount) || metric.sampleCount < 1)
    ) {
      throw new Error("Reported sample counts must be positive integers or unavailable.");
    }
    measuredScopes.add(scope);
  }
  if (measuredScopes.size !== scopes.size) {
    throw new Error("Every expected result scope must contain measured metrics.");
  }
  if (!/^[a-f0-9]{40,64}$/i.test(provenance.commit || "")) {
    throw new Error("Publication requires the measured commit SHA.");
  }
  const url = new URL(provenance.runUrl);
  if (
    url.protocol !== "https:" ||
    url.username ||
    url.password ||
    url.search ||
    url.hash ||
    !/^\/[\w.-]+\/[\w.-]+\/actions\/runs\/\d+$/.test(url.pathname) ||
    !/^[\w.-]+$/.test(url.hostname) ||
    !/^\d+$/.test(String(provenance.runAttempt)) ||
    Number(provenance.runAttempt) < 1 ||
    !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(provenance.generatedAt) ||
    !Number.isFinite(Date.parse(provenance.generatedAt))
  ) {
    throw new Error("Publication requires a run URL, attempt, and UTC publication timestamp.");
  }
}

function renderMeasurements(metrics, provenance, expectedFiles) {
  return [
    "## Current benchmark measurements",
    "",
    `Published: ${provenance.generatedAt}`,
    "",
    `Source: [Unity Benchmarks run](${provenance.runUrl}), attempt ${provenance.runAttempt}.`,
    `Measured commit: \`${provenance.commit}\`.`,
    "",
    "These are measured results from the latest complete successful benchmark run. Single",
    "aggregates are recorded values, not sampled medians. Distribution rows retain the reported",
    "median and sample count. Missing counters are unavailable, never zero. These measurements",
    "do not seed the acceptance baseline or establish calibrated optimization acceptance.",
    "",
    "Raw NUnit evidence: " +
      expectedFiles.map((file) => `[${text(file)}](../../perf-results/${file})`).join(", ") +
      ".",
    "",
    "<!-- cspell:disable -->",
    "",
    alignTable([
      ["Test", "Metric", "Unity", "Mode", "Recorded value", "Measurement"],
      ...[...metrics]
        .sort((left, right) =>
          [left.test, left.sampleGroup, left.unityVersion, left.testMode, left.unit]
            .join("/")
            .localeCompare(
              [right.test, right.sampleGroup, right.unityVersion, right.testMode, right.unit].join(
                "/"
              ),
              "en"
            )
        )
        .map((metric) => [
          text(metric.test),
          text(metric.sampleGroup),
          text(metric.unityVersion),
          text(metric.testMode),
          text(`${metric.median}${metric.unit === null ? " (unitless)" : " " + metric.unit}`),
          metric.sampleCount == null
            ? "Single aggregate; distribution unavailable"
            : `Reported median; ${metric.sampleCount} samples`
        ])
    ]),
    "",
    "<!-- cspell:enable -->",
    ""
  ].join("\n");
}

function publish({ decision, metrics, document, provenance }) {
  if (decision.complete !== true) {
    return {
      published: false,
      reason: "Benchmark run is incomplete or failed; current documentation is unchanged."
    };
  }
  validateComplete(decision, metrics, provenance);
  if (fs.lstatSync(document).isSymbolicLink()) {
    throw new Error("Benchmark documentation cannot be published through a symbolic link.");
  }
  const original = fs.readFileSync(document, "utf8");
  if (
    original.split(START).length !== 2 ||
    original.split(END).length !== 2 ||
    original.indexOf(START) > original.indexOf(END)
  ) {
    throw new Error("Benchmark documentation requires one ordered current-results marker pair.");
  }
  const start = original.indexOf(START) + START.length;
  const end = original.indexOf(END);
  const updated =
    original.slice(0, start) +
    "\n\n" +
    renderMeasurements(metrics, provenance, decision.expectedFiles) +
    "\n" +
    original.slice(end);
  const temporary = fs.mkdtempSync(path.join(path.dirname(document), ".benchmark-docs-"));
  try {
    const candidate = path.join(temporary, "report.md");
    fs.writeFileSync(candidate, updated, "utf8");
    fs.renameSync(candidate, document);
  } finally {
    fs.rmSync(temporary, { recursive: true, force: true });
  }
  return { published: true, metricCount: metrics.length };
}

function main(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 2) {
    const key = argv[index];
    if (
      !["--decision", "--current", "--document", "--run-url", "--run-attempt", "--commit"].includes(
        key
      ) ||
      !argv[index + 1]
    ) {
      throw new Error(`Unknown or incomplete argument: ${key}`);
    }
    options[key] = argv[index + 1];
  }
  const decision = JSON.parse(fs.readFileSync(options["--decision"], "utf8"));
  const result = publish({
    decision,
    metrics:
      decision.complete === true ? JSON.parse(fs.readFileSync(options["--current"], "utf8")) : null,
    document: options["--document"],
    provenance: {
      runUrl: options["--run-url"],
      runAttempt: options["--run-attempt"],
      commit: options["--commit"],
      generatedAt: new Date().toISOString()
    }
  });
  process.stdout.write(`${JSON.stringify(result)}\n`);
}

if (require.main === module) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { publish, renderMeasurements, START, END };
