"use strict";

// cspell:ignore stddev

/*
 * render-perf-deltas.js
 * ---------------------
 * Pure-Node (Node 22, zero deps) delta renderer. Given the CURRENT normalized
 * metrics JSON (produced by extract-perf-metrics.js) and a committed BASELINE
 * JSON, it:
 *   1. Matches metrics by composite key
 *      (test | sampleGroup | unityVersion | testMode).
 *   2. Computes per-metric delta (absolute + percent vs baseline).
 *   3. Classifies each metric as regression / improvement / stable using a
 *      tolerance, and flags a *significant* regression using a stricter
 *      threshold (the hard-gate signal).
 *   4. Renders a GitHub-flavored Markdown report AND a machine-readable JSON
 *      payload (current metrics, regressions, improvements, summary).
 *
 * ---------------------------------------------------------------------------
 * DIRECTION-OF-IMPROVEMENT
 * ---------------------------------------------------------------------------
 * unity-helpers' perf metrics are mostly raw Stopwatch timings (unit "ms"/"s"/
 * "ns"/"us") or sizes ("bytes"/"b"/"kb"/...). For those, LOWER is better, so a
 * positive delta (current > baseline) is SLOWER/larger = a regression.
 *
 * Dimensionless ratio metrics (e.g. "Speedup" cells like "3.75x", which the
 * extractor stores with unit === null) have NO well-defined regression
 * direction from the number alone, so they are always classified "stable" for
 * gating purposes and merely reported. This avoids falsely failing CI when a
 * speedup ratio merely shifts.
 *
 * ---------------------------------------------------------------------------
 * REGRESSION CLASSIFICATION
 * ---------------------------------------------------------------------------
 * tolerance (default 0.05 = 5%): |pct| <= tolerance  => "stable".
 *   pct > tolerance for a lower-is-better metric => "regression".
 *   pct < -tolerance for a lower-is-better metric => "improvement".
 *
 * A regression is "significant" (the hard-gate signal) when it is BOTH:
 *   - at least `regressionThreshold` slower (default 0.10 = 10%), AND
 *   - beyond noise. unity-helpers Stopwatch metrics carry NO stddev/sampleCount
 *     (they are single aggregate numbers), so when stddev is null the
 *     "beyond stddev" test degrades to the percent threshold alone. When a
 *     metric DOES carry a stddev (the future Unity-perf SampleGroup path), the
 *     absolute delta must also exceed `stddevMultiplier * stddev` (default 1x)
 *     for the regression to count as significant. This matches the brief's
 *     ">5% slower AND beyond stddev" intent while remaining meaningful for the
 *     stddev-less data we actually have today.
 *
 * The process exits NON-ZERO only when a significant regression is found AND
 * gating is enabled (PERF_FAIL_ON_REGRESSION=1 / --fail-on-regression). The
 * DEFAULT is report-only (exit 0) so the weekly scheduled run never blocks
 * unexpectedly; the regression list is always written into the JSON + Markdown.
 *
 * ---------------------------------------------------------------------------
 * USAGE
 *   node render-perf-deltas.js --current <metrics.json> --baseline <baseline.json> \
 *        [--out-md <report.md>] [--out-json <payload.json>] \
 *        [--update-baseline <path>] [--tolerance 0.05] \
 *        [--regression-threshold 0.10] [--stddev-multiplier 1] \
 *        [--fail-on-regression]
 *   node render-perf-deltas.js --self-test
 *
 * --update-baseline writes the CURRENT metrics back out as the new rolling
 * baseline (prettier-clean, 2-space, trailing newline) so the workflow can keep
 * a rolling baseline. The baseline file shape is { _comment?, metrics: [...] }.
 *
 * Env equivalents (flags win): PERF_TOLERANCE, PERF_REGRESSION_THRESHOLD,
 * PERF_STDDEV_MULTIPLIER, PERF_FAIL_ON_REGRESSION.
 */

const fs = require("fs");

const DEFAULT_TOLERANCE = 0.05;
const DEFAULT_REGRESSION_THRESHOLD = 0.1;
const DEFAULT_STDDEV_MULTIPLIER = 1;

// Units for which LOWER is better (a positive delta is a regression). Anything
// else (notably unit === null, e.g. dimensionless speedup ratios) is reported
// but never gated.
const LOWER_IS_BETTER_UNITS = new Set([
  "ns",
  "us",
  "µs",
  "ms",
  "s",
  "sec",
  "b",
  "byte",
  "bytes",
  "kb",
  "mb",
  "gb"
]);

function parseArgs(argv) {
  const env = process.env;
  const options = {
    current: null,
    baseline: null,
    outMd: null,
    outJson: null,
    updateBaseline: null,
    tolerance: numFromEnv(env.PERF_TOLERANCE, DEFAULT_TOLERANCE),
    regressionThreshold: numFromEnv(env.PERF_REGRESSION_THRESHOLD, DEFAULT_REGRESSION_THRESHOLD),
    stddevMultiplier: numFromEnv(env.PERF_STDDEV_MULTIPLIER, DEFAULT_STDDEV_MULTIPLIER),
    failOnRegression: boolFromEnv(env.PERF_FAIL_ON_REGRESSION),
    selfTest: false,
    help: false
  };
  for (let index = 2; index < argv.length; index++) {
    const arg = argv[index];
    switch (arg) {
      case "--current":
        options.current = requireValue(argv, ++index, arg);
        break;
      case "--baseline":
        options.baseline = requireValue(argv, ++index, arg);
        break;
      case "--out-md":
        options.outMd = requireValue(argv, ++index, arg);
        break;
      case "--out-json":
        options.outJson = requireValue(argv, ++index, arg);
        break;
      case "--update-baseline":
        options.updateBaseline = requireValue(argv, ++index, arg);
        break;
      case "--tolerance":
        options.tolerance = parseNonNegative(requireValue(argv, ++index, arg), arg);
        break;
      case "--regression-threshold":
        options.regressionThreshold = parseNonNegative(requireValue(argv, ++index, arg), arg);
        break;
      case "--stddev-multiplier":
        options.stddevMultiplier = parseNonNegative(requireValue(argv, ++index, arg), arg);
        break;
      case "--fail-on-regression":
        options.failOnRegression = true;
        break;
      case "--self-test":
        options.selfTest = true;
        break;
      case "--help":
      case "-h":
        options.help = true;
        break;
      default:
        throw new Error(`Unknown argument: ${arg}`);
    }
  }
  return options;
}

function requireValue(argv, index, flag) {
  const value = argv[index];
  if (value === undefined || value.startsWith("--")) {
    throw new Error(`${flag} requires a value.`);
  }
  return value;
}

function parseNonNegative(value, flag) {
  const parsed = Number.parseFloat(value);
  if (!Number.isFinite(parsed) || parsed < 0) {
    throw new Error(`${flag} must be a non-negative number, got: ${value}`);
  }
  return parsed;
}

function numFromEnv(value, fallback) {
  if (value === undefined || value === "") {
    return fallback;
  }
  const parsed = Number.parseFloat(value);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
}

function boolFromEnv(value) {
  if (value === undefined) {
    return false;
  }
  return /^(1|true|yes|on)$/i.test(value.trim());
}

function usage() {
  return [
    "Usage: node scripts/unity/lib/render-perf-deltas.js --current <metrics.json> \\",
    "         --baseline <baseline.json> [--out-md <report.md>] [--out-json <payload.json>] \\",
    "         [--update-baseline <path>] [--tolerance 0.05] [--regression-threshold 0.10] \\",
    "         [--stddev-multiplier 1] [--fail-on-regression]",
    "       node scripts/unity/lib/render-perf-deltas.js --self-test",
    "",
    "Compares current perf metrics with a committed baseline, renders a Markdown",
    "delta report + a machine-readable JSON payload, and (optionally) rewrites the",
    "rolling baseline. Report-only by default; exits non-zero on a significant",
    "regression only when --fail-on-regression / PERF_FAIL_ON_REGRESSION=1."
  ].join("\n");
}

// --- Loading ---------------------------------------------------------------

function loadMetrics(filePath) {
  if (!filePath || !fs.existsSync(filePath)) {
    return null;
  }
  let raw;
  try {
    raw = fs.readFileSync(filePath, "utf8");
  } catch {
    return null;
  }
  if (raw.trim() === "") {
    return [];
  }
  let parsed;
  try {
    parsed = JSON.parse(raw);
  } catch (error) {
    throw new Error(`Failed to parse JSON from ${filePath}: ${error.message}`);
  }
  // Accept either a bare array (extractor output) or { metrics: [...] }
  // (baseline file shape).
  if (Array.isArray(parsed)) {
    return parsed;
  }
  if (parsed && Array.isArray(parsed.metrics)) {
    return parsed.metrics;
  }
  return [];
}

// --- Comparison ------------------------------------------------------------

function metricKey(metric) {
  return [
    metric.test ?? "",
    metric.sampleGroup ?? "",
    metric.unityVersion ?? "",
    metric.testMode ?? ""
  ].join("");
}

function indexByKey(metrics) {
  const map = new Map();
  for (const metric of metrics) {
    const key = metricKey(metric);
    // First wins so output is deterministic if a run emits duplicates.
    if (!map.has(key)) {
      map.set(key, metric);
    }
  }
  return map;
}

function lowerIsBetter(unit) {
  return unit != null && LOWER_IS_BETTER_UNITS.has(String(unit).toLowerCase());
}

function relativeChange(current, baseline) {
  if (baseline === 0) {
    return current === 0 ? 0 : Infinity;
  }
  return (current - baseline) / baseline;
}

// Classify one matched metric. Returns null when the current value is not a
// finite number (nothing to compare).
function classify(current, baseline, options) {
  const currentVal = Number(current.median);
  const baselineVal = Number(baseline.median);
  if (!Number.isFinite(currentVal) || !Number.isFinite(baselineVal)) {
    return null;
  }

  const absDelta = currentVal - baselineVal;
  const pct = relativeChange(currentVal, baselineVal);
  const gated = lowerIsBetter(current.unit);

  let status = "stable";
  let significant = false;

  if (gated && Number.isFinite(pct)) {
    if (pct > options.tolerance) {
      status = "regression";
    } else if (pct < -options.tolerance) {
      status = "improvement";
    }

    if (status === "regression") {
      const meetsThreshold = pct >= options.regressionThreshold;
      // stddev gate: only applies when the baseline carries a stddev. For the
      // stddev-less Stopwatch metrics we have today, beyondNoise is true and the
      // percent threshold alone decides significance.
      const stddev = Number(baseline.stddev);
      const beyondNoise = Number.isFinite(stddev)
        ? Math.abs(absDelta) > options.stddevMultiplier * stddev
        : true;
      significant = meetsThreshold && beyondNoise;
    }
  }

  return {
    test: current.test ?? null,
    sampleGroup: current.sampleGroup ?? null,
    unit: current.unit ?? null,
    unityVersion: current.unityVersion ?? null,
    testMode: current.testMode ?? null,
    baseline: baselineVal,
    current: currentVal,
    absDelta,
    pct,
    gated,
    status,
    significant
  };
}

function compareMetrics(currentMetrics, baselineMetrics, options) {
  const baselineIndex = indexByKey(baselineMetrics);
  const comparisons = [];
  const newMetrics = [];

  for (const current of currentMetrics) {
    const key = metricKey(current);
    const baseline = baselineIndex.get(key);
    if (!baseline) {
      newMetrics.push(current);
      continue;
    }
    const comparison = classify(current, baseline, options);
    if (comparison) {
      comparisons.push(comparison);
    }
  }

  const currentKeys = new Set(currentMetrics.map(metricKey));
  const removedMetrics = baselineMetrics.filter((m) => !currentKeys.has(metricKey(m)));

  const regressions = comparisons.filter((c) => c.status === "regression");
  const significantRegressions = comparisons.filter((c) => c.significant);
  const improvements = comparisons.filter((c) => c.status === "improvement");

  return {
    comparisons,
    regressions,
    significantRegressions,
    improvements,
    newMetrics,
    removedMetrics
  };
}

// --- Formatting ------------------------------------------------------------

function formatNumber(value) {
  if (!Number.isFinite(value)) {
    return "n/a";
  }
  if (Number.isInteger(value)) {
    return value.toLocaleString("en-US");
  }
  return value.toLocaleString("en-US", { maximumFractionDigits: 3 });
}

function formatValueWithUnit(value, unit) {
  const formatted = formatNumber(value);
  return unit ? `${formatted} ${unit}` : formatted;
}

function formatPct(pct) {
  if (!Number.isFinite(pct)) {
    return "n/a";
  }
  const scaled = pct * 100;
  const sign = scaled > 0 ? "+" : "";
  return `${sign}${scaled.toFixed(2)}%`;
}

function statusEmoji(comparison) {
  if (comparison.status === "regression") {
    return comparison.significant ? "regression!" : "regression";
  }
  if (comparison.status === "improvement") {
    return "improvement";
  }
  return "stable";
}

// Render a left/right-padded markdown table (header + rows), all string cells.
function alignTable(rows) {
  if (rows.length === 0) {
    return "";
  }
  const columnCount = rows[0].length;
  const widths = new Array(columnCount).fill(0);
  for (const row of rows) {
    for (let c = 0; c < columnCount; c++) {
      widths[c] = Math.max(widths[c], String(row[c] ?? "").length);
    }
  }
  const renderRow = (row) =>
    `| ${row.map((cell, c) => String(cell ?? "").padEnd(widths[c])).join(" | ")} |`;
  const divider = `| ${widths.map((w) => "-".repeat(Math.max(3, w))).join(" | ")} |`;
  return [renderRow(rows[0]), divider, ...rows.slice(1).map(renderRow)].join("\n");
}

function comparisonRow(comparison) {
  const scope = [comparison.unityVersion, comparison.testMode].filter(Boolean).join(" / ") || "-";
  return [
    `${comparison.test ?? ""} :: ${comparison.sampleGroup ?? ""}`,
    scope,
    formatValueWithUnit(comparison.baseline, comparison.unit),
    formatValueWithUnit(comparison.current, comparison.unit),
    formatPct(comparison.pct),
    statusEmoji(comparison)
  ];
}

function buildMarkdown(result, options, meta) {
  const lines = [];
  lines.push("# Perf Deltas");
  lines.push("");
  lines.push(`_Generated ${meta.generatedAt}._`);
  lines.push("");

  if (meta.noBaseline) {
    lines.push(
      "_No baseline committed yet; skipping the delta comparison. The next benchmark run will seed `perf-results/baseline.json` and subsequent runs will diff against it._"
    );
    if (meta.currentCount > 0) {
      lines.push("");
      lines.push(`Captured **${meta.currentCount}** metric(s) this run (now the baseline).`);
    }
    lines.push("");
    return lines.join("\n");
  }

  const summary = [
    `- Metrics compared: **${result.comparisons.length}**`,
    `- Regressions: **${result.regressions.length}** (significant: **${result.significantRegressions.length}**)`,
    `- Improvements: **${result.improvements.length}**`,
    `- New metrics: **${result.newMetrics.length}**, removed: **${result.removedMetrics.length}**`,
    `- Tolerance: ${(options.tolerance * 100).toFixed(2)}%, regression threshold: ${(
      options.regressionThreshold * 100
    ).toFixed(2)}%`
  ];
  lines.push(...summary);
  lines.push("");

  if (result.significantRegressions.length > 0) {
    lines.push("## Significant regressions");
    lines.push("");
    lines.push(
      alignTable([
        ["Metric", "Scope", "Baseline", "Current", "Delta", "Status"],
        ...result.significantRegressions.map(comparisonRow)
      ])
    );
    lines.push("");
  }

  // Show the moved metrics (regressions + improvements) to keep the report
  // focused; stable metrics are summarized by count only.
  const moved = result.comparisons.filter((c) => c.status !== "stable");
  lines.push("## Changed metrics");
  lines.push("");
  if (moved.length === 0) {
    lines.push("_All compared metrics are within tolerance._");
  } else {
    lines.push(
      alignTable([
        ["Metric", "Scope", "Baseline", "Current", "Delta", "Status"],
        ...moved.map(comparisonRow)
      ])
    );
  }
  lines.push("");

  if (result.newMetrics.length > 0) {
    lines.push("## New metrics (no baseline yet)");
    lines.push("");
    lines.push(
      alignTable([
        ["Metric", "Scope", "Current"],
        ...result.newMetrics.map((m) => [
          `${m.test ?? ""} :: ${m.sampleGroup ?? ""}`,
          [m.unityVersion, m.testMode].filter(Boolean).join(" / ") || "-",
          formatValueWithUnit(Number(m.median), m.unit)
        ])
      ])
    );
    lines.push("");
  }

  return lines.join("\n");
}

function buildJsonPayload(result, options, meta) {
  return {
    generatedAt: meta.generatedAt,
    noBaseline: meta.noBaseline,
    tolerance: options.tolerance,
    regressionThreshold: options.regressionThreshold,
    stddevMultiplier: options.stddevMultiplier,
    summary: {
      compared: result.comparisons.length,
      regressions: result.regressions.length,
      significantRegressions: result.significantRegressions.length,
      improvements: result.improvements.length,
      newMetrics: result.newMetrics.length,
      removedMetrics: result.removedMetrics.length
    },
    significantRegressions: result.significantRegressions,
    regressions: result.regressions,
    improvements: result.improvements,
    newMetrics: result.newMetrics,
    comparisons: result.comparisons
  };
}

// --- Output ----------------------------------------------------------------

function writeFileClean(filePath, content) {
  // Always end with exactly one trailing newline (prettier-clean).
  const normalized = content.endsWith("\n") ? content : `${content}\n`;
  fs.writeFileSync(filePath, normalized, "utf8");
}

function writeBaseline(filePath, metrics, comment) {
  const payload = {
    _comment:
      comment ||
      "Rolling perf baseline for unity-helpers benchmarks. Auto-updated by the Unity Benchmarks workflow (scripts/unity/lib/render-perf-deltas.js). Do not hand-edit.",
    metrics
  };
  // JSON.stringify with 2-space indent matches prettier's default for JSON.
  writeFileClean(filePath, JSON.stringify(payload, null, 2));
}

// --- Orchestration ---------------------------------------------------------

function run(options, nowIso) {
  const generatedAt = nowIso || new Date().toISOString();
  const currentMetrics = loadMetrics(options.current) || [];
  const baselineMetrics = loadMetrics(options.baseline);

  const noBaseline = baselineMetrics === null || baselineMetrics.length === 0;
  const meta = {
    generatedAt,
    noBaseline,
    currentCount: currentMetrics.length
  };

  let result;
  if (noBaseline) {
    result = {
      comparisons: [],
      regressions: [],
      significantRegressions: [],
      improvements: [],
      newMetrics: currentMetrics,
      removedMetrics: []
    };
  } else {
    result = compareMetrics(currentMetrics, baselineMetrics, options);
  }

  const markdown = buildMarkdown(result, options, meta);
  const json = buildJsonPayload(result, options, meta);

  return {
    markdown,
    json,
    result,
    meta,
    currentMetrics,
    significant: result.significantRegressions.length > 0
  };
}

function main(argv = process.argv) {
  const options = parseArgs(argv);
  if (options.help) {
    process.stdout.write(`${usage()}\n`);
    return 0;
  }
  if (options.selfTest) {
    return runSelfTest();
  }
  if (!options.current) {
    process.stderr.write(`--current <metrics.json> is required.\n\n${usage()}\n`);
    return 2;
  }

  const outcome = run(options);

  if (options.outMd) {
    writeFileClean(options.outMd, outcome.markdown);
  } else {
    process.stdout.write(`${outcome.markdown}\n`);
  }
  if (options.outJson) {
    writeFileClean(options.outJson, JSON.stringify(outcome.json, null, 2));
  }
  if (options.updateBaseline) {
    writeBaseline(options.updateBaseline, outcome.currentMetrics);
  }

  // Emit GitHub-friendly signals on stdout regardless of output routing.
  process.stdout.write(
    `changed=${outcome.result.comparisons.some((c) => c.status !== "stable") ? "true" : "false"}\n`
  );
  process.stdout.write(`regressed=${outcome.significant ? "true" : "false"}\n`);

  if (outcome.significant && options.failOnRegression) {
    process.stderr.write(
      `Significant perf regression(s) detected: ${outcome.result.significantRegressions.length}.\n`
    );
    return 1;
  }
  return 0;
}

// --- Self-test -------------------------------------------------------------

function assert(condition, message) {
  if (!condition) {
    throw new Error(`Self-test failed: ${message}`);
  }
}

function runSelfTest() {
  const options = {
    tolerance: DEFAULT_TOLERANCE,
    regressionThreshold: DEFAULT_REGRESSION_THRESHOLD,
    stddevMultiplier: DEFAULT_STDDEV_MULTIPLIER
  };

  const baseline = [
    {
      test: "T",
      sampleGroup: "Small / Serialize",
      unit: "ms",
      median: 100,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    {
      test: "T",
      sampleGroup: "Small / Deserialize",
      unit: "ms",
      median: 100,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    {
      test: "T",
      sampleGroup: "Small / Stable",
      unit: "ms",
      median: 100,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    {
      test: "T",
      sampleGroup: "Small / Speedup",
      unit: null,
      median: 3.0,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    {
      test: "T",
      sampleGroup: "Gone",
      unit: "ms",
      median: 50,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    }
  ];
  const current = [
    // +20% slower, beyond 10% threshold => significant regression.
    {
      test: "T",
      sampleGroup: "Small / Serialize",
      unit: "ms",
      median: 120,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    // -30% faster => improvement.
    {
      test: "T",
      sampleGroup: "Small / Deserialize",
      unit: "ms",
      median: 70,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    // +2% => within tolerance => stable.
    {
      test: "T",
      sampleGroup: "Small / Stable",
      unit: "ms",
      median: 102,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    // Dimensionless ratio moved a lot but must NOT gate (unit null).
    {
      test: "T",
      sampleGroup: "Small / Speedup",
      unit: null,
      median: 1.0,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    },
    // A brand-new metric with no baseline.
    {
      test: "T",
      sampleGroup: "Small / New",
      unit: "ms",
      median: 5,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    }
  ];

  const result = compareMetrics(current, baseline, options);

  assert(
    result.comparisons.length === 4,
    `expected 4 compared metrics, got ${result.comparisons.length}`
  );
  assert(
    result.regressions.length === 1,
    `expected 1 regression, got ${result.regressions.length}`
  );
  assert(
    result.significantRegressions.length === 1,
    "the +20% ms metric should be a significant regression"
  );
  assert(
    result.significantRegressions[0].sampleGroup === "Small / Serialize",
    "significant regression should be the Serialize metric"
  );
  assert(
    result.improvements.length === 1,
    `expected 1 improvement, got ${result.improvements.length}`
  );
  assert(
    result.improvements[0].sampleGroup === "Small / Deserialize",
    "improvement should be Deserialize"
  );
  assert(
    result.newMetrics.length === 1 && result.newMetrics[0].sampleGroup === "Small / New",
    "New metric should be detected"
  );
  assert(
    result.removedMetrics.length === 1 && result.removedMetrics[0].sampleGroup === "Gone",
    "Removed metric should be detected"
  );

  const speedup = result.comparisons.find((c) => c.sampleGroup === "Small / Speedup");
  assert(
    speedup && speedup.status === "stable" && speedup.gated === false,
    "dimensionless ratio must be stable and not gated"
  );

  const stableOne = result.comparisons.find((c) => c.sampleGroup === "Small / Stable");
  assert(stableOne && stableOne.status === "stable", "+2% metric must be stable");

  // stddev gate: a +20% move that is WITHIN the stddev band must NOT be significant.
  const noisyBaseline = [
    {
      test: "T",
      sampleGroup: "Jittery",
      unit: "ms",
      median: 100,
      stddev: 40,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    }
  ];
  const noisyCurrent = [
    {
      test: "T",
      sampleGroup: "Jittery",
      unit: "ms",
      median: 120,
      unityVersion: "6000.3.16f1",
      testMode: "playmode"
    }
  ];
  const noisy = compareMetrics(noisyCurrent, noisyBaseline, options);
  assert(
    noisy.regressions.length === 1,
    "the noisy metric is still a (non-significant) regression"
  );
  assert(
    noisy.significantRegressions.length === 0,
    "a +20% move inside a 40ms stddev band must NOT be a significant regression"
  );

  // Missing-baseline path.
  const noBaselineOutcome = run(
    { current: null, baseline: null, ...options },
    "2026-06-14T00:00:00.000Z"
  );
  assert(noBaselineOutcome.meta.noBaseline === true, "null baseline path should set noBaseline");
  assert(
    /No baseline committed yet/.test(noBaselineOutcome.markdown),
    "no-baseline markdown should explain itself"
  );
  assert(
    noBaselineOutcome.significant === false,
    "no-baseline path must never report a regression"
  );

  // Markdown renders without throwing and includes the significant section.
  const md = buildMarkdown(result, options, {
    generatedAt: "2026-06-14T00:00:00.000Z",
    noBaseline: false,
    currentCount: current.length
  });
  assert(
    /Significant regressions/.test(md),
    "markdown should include a Significant regressions section"
  );
  assert(/Small \/ Serialize/.test(md), "markdown should list the regressed metric");

  // JSON payload shape.
  const payload = buildJsonPayload(result, options, {
    generatedAt: "2026-06-14T00:00:00.000Z",
    noBaseline: false,
    currentCount: current.length
  });
  assert(
    payload.summary.significantRegressions === 1,
    "JSON summary should report 1 significant regression"
  );
  assert(Array.isArray(payload.comparisons), "JSON payload should carry the comparisons array");

  process.stdout.write("render-perf-deltas self-test passed.\n");
  return 0;
}

if (require.main === module) {
  try {
    process.exitCode = main();
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = {
  loadMetrics,
  metricKey,
  indexByKey,
  lowerIsBetter,
  classify,
  compareMetrics,
  buildMarkdown,
  buildJsonPayload,
  alignTable,
  writeBaseline,
  run,
  LOWER_IS_BETTER_UNITS
};
