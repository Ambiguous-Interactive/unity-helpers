"use strict";

// cspell:ignore apos quot CDATA

/*
 * extract-perf-metrics.js
 * -----------------------
 * Pure-Node (Node 22, zero deps) extractor that reads one or more NUnit3
 * results.xml files emitted by the Unity Benchmarks CI job and normalizes the
 * perf numbers into a flat JSON array:
 *
 *   [{ test, sampleGroup, unit, median, min, max, stddev, sampleCount,
 *      unityVersion, testMode }]
 *
 * ---------------------------------------------------------------------------
 * WHAT unity-helpers ACTUALLY EMITS (verified against the source, NOT a guess)
 * ---------------------------------------------------------------------------
 * unity-helpers' perf tests under Tests/Runtime/Performance/ do NOT use Unity's
 * com.unity.test-framework.performance package. There is no `Measure.Method`,
 * no `SampleGroup`, and the perf asmdef
 * (WallstopStudios.UnityHelpers.Tests.Runtime.Performance.asmdef) does not
 * reference Unity.PerformanceTesting. Instead every perf test uses a raw
 * `System.Diagnostics.Stopwatch` and prints results with
 * `UnityEngine.Debug.Log(...)`, almost always as a GitHub-flavored Markdown
 * TABLE, emitted one row per Debug.Log call. Concrete examples from the repo:
 *
 *   ProtoEqualsPerformanceTests.CompareProtoEqualsSmallMediumLarge:
 *     | Payload | Optimized ProtoEquals (ms) | Classic ProtoEquals (ms) | Speedup |
 *     | ------- | -------------------------:| ------------------------:| -------:|
 *     | Small   |                        12 |                       45 |   3.75x |
 *
 *   ProtoSerializationPerformanceTests.CompareSerializeSmallMediumLarge:
 *     | Payload | Pooled Serialize (ms) | Classic Serialize (ms) | Speedup | Size (bytes) |
 *     | ...
 *
 *   SpatialTree2DPerformanceTests.Benchmark (per-dataset tables):
 *     | Construction | RTree | Quadtree | KDTree | ... |
 *     | ---          | ---   | ---      | ---    | ... |
 *     | Build        | 12,345 (0.003s) | ...               |
 *
 * The Unity test runner captures per-test `Debug.Log` output, and the
 * standalone player writer in scripts/unity/run-ci-tests.ps1 serializes results
 * with NUnit3's `TestResult.ToXml(recursive: true)`. NUnit3 puts captured
 * console output in an `<output>` CDATA child of each `<test-case>`. So the
 * perf numbers live in `<test-case><output><![CDATA[ ...markdown tables... ]]>`.
 *
 * Because the column schema differs per test family (Optimized/Classic/Speedup
 * vs Pooled/Classic/Size vs per-tree columns), we do NOT hardcode any family.
 * The extractor is FORMAT-AGNOSTIC over Markdown tables: for every detected
 * table it emits one metric per numeric data cell, keyed as
 *
 *     sampleGroup = "<RowLabel> / <ColumnHeader>"
 *     unit        = parsed from the column header, e.g. "(ms)" -> "ms"
 *     median      = the numeric value of the cell (min/max/stddev = null,
 *                   sampleCount = null, because Stopwatch tables report a single
 *                   aggregate number, not a Unity SampleGroup distribution)
 *
 * This is the documented, intentional shape: these are single-sample aggregate
 * timings, so only `median` is populated and the delta tool compares medians.
 *
 * ---------------------------------------------------------------------------
 * FALLBACKS (also documented + tested)
 * ---------------------------------------------------------------------------
 * 1. Unity SampleGroup JSON: IF a future test ever does adopt
 *    com.unity.test-framework.performance, that framework writes a
 *    `<property name="performanceTestResults" value="<json>"/>` (older builds
 *    used name "performanceData"/"PerformanceTestResults"). We parse that JSON
 *    shape too (SampleGroups with Median/Min/Max/StandardDeviation/Unit/
 *    SampleCount). This path is dormant today but future-proofs the extractor.
 *
 * 2. Plain "Stopwatch / WriteLine" timing lines NOT inside a Markdown table.
 *    Some logs print free-form lines such as:
 *        "Foo took 12.34 ms"
 *        "Bar: 1234 ns"
 *        "Baz elapsed 0.50s"
 *    We key on the documented regex TIMING_LINE_RE below: a label, then a
 *    number, then a recognized time unit (ns|us|µs|ms|s) at a word boundary.
 *    These become sampleGroup = "<label>" with the parsed unit + median.
 *    Markdown table rows are matched FIRST and excluded from this fallback so a
 *    cell is never double-counted.
 *
 * unityVersion + testMode are NOT in the XML body; they are taken from the
 * staged file name (results-<unityVersion>-<testMode>.xml) or from
 * --unity-version / --test-mode flags. They may be null when unknown.
 *
 * ---------------------------------------------------------------------------
 * USAGE
 *   node extract-perf-metrics.js <results.xml> [<results2.xml> ...] \
 *        [--unity-version <v>] [--test-mode <m>] [--output <path>]
 *   node extract-perf-metrics.js --self-test
 *
 * Files named results-<v>-<mode>.xml have their unityVersion/testMode inferred
 * automatically; explicit flags override the inference for ALL inputs.
 */

const fs = require("fs");
const path = require("path");

// A label, then a number (with optional thousands separators / decimals /
// sign), then a recognized time unit at a word boundary. Anchored loosely so
// it matches "Foo took 12.34 ms", "Bar: 1,234 ns", "Baz elapsed 0.5s".
const TIMING_LINE_RE =
  /^(?<label>.*?\S)\s*[:=]?\s*(?<value>[-+]?\d[\d,]*(?:\.\d+)?)\s*(?<unit>ns|us|µs|ms|s)\b/i;

// File name like results-6000.3.16f1-playmode.xml -> { version, mode }.
const RESULT_FILE_RE = /^results-(?<version>.+?)-(?<mode>editmode|playmode)\.xml$/i;

function parseArgs(argv) {
  const options = {
    inputs: [],
    unityVersion: null,
    testMode: null,
    output: null,
    selfTest: false,
    help: false
  };
  for (let index = 2; index < argv.length; index++) {
    const arg = argv[index];
    switch (arg) {
      case "--unity-version":
        options.unityVersion = requireValue(argv, ++index, arg);
        break;
      case "--test-mode":
        options.testMode = requireValue(argv, ++index, arg);
        break;
      case "--output":
        options.output = requireValue(argv, ++index, arg);
        break;
      case "--self-test":
        options.selfTest = true;
        break;
      case "--help":
      case "-h":
        options.help = true;
        break;
      default:
        if (arg.startsWith("--")) {
          throw new Error(`Unknown argument: ${arg}`);
        }
        options.inputs.push(arg);
        break;
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

function usage() {
  return [
    "Usage: node scripts/unity/lib/extract-perf-metrics.js <results.xml> [<results2.xml> ...] \\",
    "         [--unity-version <v>] [--test-mode <m>] [--output <path>]",
    "       node scripts/unity/lib/extract-perf-metrics.js --self-test",
    "",
    "Parses NUnit3 results.xml emitted by the Unity Benchmarks job and prints a",
    "normalized JSON array of perf metrics to stdout (or --output)."
  ].join("\n");
}

// --- XML helpers -----------------------------------------------------------
// Intentionally NOT a full XML parser. We only need <test-case> elements, their
// `fullname`/`name` attributes, their <output> CDATA, and any perf-result
// <property> values. A targeted scan keeps this dependency-free and resilient to
// the (large, deeply nested) Unity NUnit output.

function decodeXmlEntities(text) {
  return (
    text
      .replace(/&lt;/g, "<")
      .replace(/&gt;/g, ">")
      .replace(/&quot;/g, '"')
      .replace(/&apos;/g, "'")
      .replace(/&#x([0-9a-fA-F]+);/g, (_, hex) => String.fromCodePoint(parseInt(hex, 16)))
      .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(parseInt(dec, 10)))
      // Ampersand last so we do not double-decode the entities above.
      .replace(/&amp;/g, "&")
  );
}

// Extract the value of an attribute from a tag's attribute string.
function readAttr(attrText, name) {
  const re = new RegExp(`\\b${name}\\s*=\\s*"([^"]*)"`);
  const match = attrText.match(re);
  return match ? decodeXmlEntities(match[1]) : null;
}

// Walk every <test-case ...> ... </test-case> (or self-closing) element and
// hand the caller its attribute string + inner body (empty for self-closing).
function forEachTestCase(xml, callback) {
  const openRe = /<test-case\b([^>]*?)(\/?)>/g;
  let match;
  while ((match = openRe.exec(xml)) !== null) {
    const attrText = match[1];
    const selfClosing = match[2] === "/";
    if (selfClosing) {
      callback(attrText, "");
      continue;
    }
    // Find the matching close tag. test-case elements are not nested in NUnit3
    // output (a test-case has no child test-cases), so the next </test-case>
    // is ours.
    const closeIndex = xml.indexOf("</test-case>", openRe.lastIndex);
    const body = closeIndex === -1 ? "" : xml.slice(openRe.lastIndex, closeIndex);
    callback(attrText, body);
    if (closeIndex !== -1) {
      openRe.lastIndex = closeIndex + "</test-case>".length;
    }
  }
}

// Pull every <output>...</output> text from a test-case body, preferring CDATA
// but also handling entity-encoded output.
function readOutputs(body) {
  const outputs = [];
  const re = /<output\b[^>]*>([\s\S]*?)<\/output>/g;
  let match;
  while ((match = re.exec(body)) !== null) {
    outputs.push(extractCdataOrText(match[1]));
  }
  return outputs;
}

function extractCdataOrText(inner) {
  const cdata = inner.match(/<!\[CDATA\[([\s\S]*?)\]\]>/);
  if (cdata) {
    return cdata[1];
  }
  return decodeXmlEntities(inner);
}

// --- Markdown table parsing ------------------------------------------------

// Recognize a GitHub-flavored markdown divider row: every cell is dashes with
// optional leading/trailing colon for alignment, e.g. "---", ":--", "--:".
function isDividerRow(cells) {
  return cells.length > 0 && cells.every((cell) => /^:?-{1,}:?$/.test(cell.trim()));
}

// Split a pipe-delimited markdown row into trimmed cells, dropping the leading
// and trailing empties produced by the bounding pipes.
function splitRow(line) {
  const trimmed = line.trim();
  if (!trimmed.startsWith("|")) {
    return null;
  }
  const parts = trimmed.split("|");
  // Drop first/last (empty from bounding pipes).
  parts.shift();
  parts.pop();
  return parts.map((cell) => cell.trim());
}

// Parse a unit out of a column header. "Optimized ProtoEquals (ms)" -> "ms";
// "Size (bytes)" -> "bytes"; "Speedup" -> null.
function unitFromHeader(header) {
  const match = header.match(/\(([^)]+)\)\s*$/);
  return match ? match[1].trim() : null;
}

// Strip the trailing "(...)" unit annotation from a header to get a clean label.
function headerLabel(header) {
  return header.replace(/\s*\([^)]*\)\s*$/, "").trim();
}

// Parse the leading numeric value out of a markdown cell. Handles thousands
// separators, decimals, sign, and trailing annotations like "x" (speedup),
// "(0.003s)" (SpatialTree construction) or "B"/"ms" suffixes.
// Returns { value, suffixUnit } or null when there is no number.
function parseCell(cell) {
  const text = cell.trim();
  if (text === "" || text === "-" || /^n\/?a$/i.test(text)) {
    return null;
  }
  const numMatch = text.match(/^([-+]?\d[\d,]*(?:\.\d+)?)(.*)$/);
  if (!numMatch) {
    return null;
  }
  const value = Number.parseFloat(numMatch[1].replace(/,/g, ""));
  if (!Number.isFinite(value)) {
    return null;
  }
  // A trailing inline unit on the value itself, e.g. "12.3ms" or "1,024 B".
  const rest = numMatch[2].trim();
  let suffixUnit = null;
  const suffixMatch = rest.match(/^([a-zµ%]+)\b/i);
  if (suffixMatch && !/^x$/i.test(suffixMatch[1])) {
    // "x" denotes a speedup ratio (e.g. "3.75x"), which is dimensionless.
    suffixUnit = suffixMatch[1];
  }
  return { value, suffixUnit };
}

// Given the lines of one test's output, find markdown tables and emit metrics.
function metricsFromMarkdownTables(lines, context, consumedLineSet) {
  const metrics = [];
  for (let i = 0; i + 1 < lines.length; i++) {
    const headerCells = splitRow(lines[i]);
    if (!headerCells) {
      continue;
    }
    const dividerCells = splitRow(lines[i + 1]);
    if (!dividerCells || !isDividerRow(dividerCells)) {
      continue;
    }
    if (headerCells.length < 2) {
      continue;
    }
    consumedLineSet.add(i);
    consumedLineSet.add(i + 1);

    // First column is the row label; the rest are metric columns.
    for (let r = i + 2; r < lines.length; r++) {
      const rowCells = splitRow(lines[r]);
      if (!rowCells || isDividerRow(rowCells)) {
        break; // table ended
      }
      consumedLineSet.add(r);
      const rowLabel = rowCells[0] || `row${r}`;
      for (let c = 1; c < headerCells.length && c < rowCells.length; c++) {
        const parsed = parseCell(rowCells[c]);
        if (!parsed) {
          continue;
        }
        const header = headerCells[c];
        const unit = unitFromHeader(header) || parsed.suffixUnit;
        const columnLabel = headerLabel(header);
        metrics.push(makeMetric(context, `${rowLabel} / ${columnLabel}`, unit, parsed.value));
      }
    }
    // Continue scanning AFTER this table for additional tables in the same log.
    i = i; // (loop increments i; tables can be adjacent)
  }
  return metrics;
}

// Free-form "label ... <number> <unit>" lines not already consumed by a table.
function metricsFromTimingLines(lines, context, consumedLineSet) {
  const metrics = [];
  for (let i = 0; i < lines.length; i++) {
    if (consumedLineSet.has(i)) {
      continue;
    }
    const line = lines[i];
    // Skip lines that are clearly markdown table rows (defensive; tables are
    // handled above) so we never double-count.
    if (line.trim().startsWith("|")) {
      continue;
    }
    const match = line.match(TIMING_LINE_RE);
    if (!match || !match.groups) {
      continue;
    }
    const label = match.groups.label.trim();
    const value = Number.parseFloat(match.groups.value.replace(/,/g, ""));
    if (!Number.isFinite(value) || label === "") {
      continue;
    }
    metrics.push(makeMetric(context, label, normalizeUnit(match.groups.unit), value));
  }
  return metrics;
}

function normalizeUnit(unit) {
  if (!unit) {
    return null;
  }
  const lower = unit.toLowerCase();
  return lower === "µs" ? "us" : lower;
}

function makeMetric(context, sampleGroup, unit, median, extras = {}) {
  return {
    test: context.test,
    sampleGroup,
    unit: unit || null,
    median,
    min: extras.min ?? null,
    max: extras.max ?? null,
    stddev: extras.stddev ?? null,
    sampleCount: extras.sampleCount ?? null,
    unityVersion: context.unityVersion ?? null,
    testMode: context.testMode ?? null
  };
}

// --- Unity SampleGroup property fallback (dormant today, future-proof) -------
// If com.unity.test-framework.performance is ever adopted, parse its JSON blob.
function metricsFromPerfProperties(body, context) {
  const metrics = [];
  const propRe = /<property\b([^>]*)\/?>/g;
  let match;
  while ((match = propRe.exec(body)) !== null) {
    const attrText = match[1];
    const name = readAttr(attrText, "name");
    if (
      name !== "performanceTestResults" &&
      name !== "performanceData" &&
      name !== "PerformanceTestResults"
    ) {
      continue;
    }
    const value = readAttr(attrText, "value");
    if (!value) {
      continue;
    }
    let parsed;
    try {
      parsed = JSON.parse(value);
    } catch {
      continue;
    }
    const groups = collectSampleGroups(parsed);
    for (const group of groups) {
      metrics.push(
        makeMetric(context, group.name, group.unit, group.median, {
          min: group.min,
          max: group.max,
          stddev: group.stddev,
          sampleCount: group.sampleCount
        })
      );
    }
  }
  return metrics;
}

// Normalize Unity's SampleGroup JSON (property casing varies across versions).
function collectSampleGroups(parsed) {
  const out = [];
  const sampleGroups = parsed && (parsed.SampleGroups || parsed.sampleGroups);
  if (!Array.isArray(sampleGroups)) {
    return out;
  }
  for (const group of sampleGroups) {
    const definition = group.Definition || group.definition || {};
    const name = definition.Name || definition.name || group.Name || group.name || "Unknown";
    const unit = definition.SampleUnit || definition.sampleUnit || group.Unit || group.unit || null;
    out.push({
      name,
      unit: typeof unit === "string" ? unit : unitEnumToString(unit),
      median: numOrNull(group.Median ?? group.median),
      min: numOrNull(group.Min ?? group.min),
      max: numOrNull(group.Max ?? group.max),
      stddev: numOrNull(group.StandardDeviation ?? group.standardDeviation),
      sampleCount: numOrNull(
        group.SampleCount ?? group.sampleCount ?? (group.Samples ? group.Samples.length : null)
      )
    });
  }
  return out;
}

function unitEnumToString(value) {
  // Unity's SampleUnit enum: 0=Nanosecond,1=Microsecond,2=Millisecond,3=Second,
  // 4=Byte,5=Kilobyte,6=Megabyte,7=Gigabyte,8=Undefined.
  const map = ["ns", "us", "ms", "s", "b", "kb", "mb", "gb", null];
  return typeof value === "number" ? (map[value] ?? null) : null;
}

function numOrNull(value) {
  const num = Number(value);
  return Number.isFinite(num) ? num : null;
}

// --- Top-level extraction --------------------------------------------------

function inferFromFileName(filePath) {
  const base = path.basename(filePath);
  const match = base.match(RESULT_FILE_RE);
  if (!match || !match.groups) {
    return { unityVersion: null, testMode: null };
  }
  return {
    unityVersion: match.groups.version,
    testMode: match.groups.mode.toLowerCase()
  };
}

function extractFromXml(xml, defaults) {
  const metrics = [];
  forEachTestCase(xml, (attrText, body) => {
    const test = readAttr(attrText, "fullname") || readAttr(attrText, "name") || "Unknown";
    const context = {
      test,
      unityVersion: defaults.unityVersion,
      testMode: defaults.testMode
    };

    // 1) Unity SampleGroup properties (future-proof; usually empty today).
    const propMetrics = metricsFromPerfProperties(body, context);
    metrics.push(...propMetrics);

    // 2) Captured Debug.Log output -> markdown tables + timing lines.
    const outputs = readOutputs(body);
    for (const output of outputs) {
      const lines = output.split(/\r?\n/);
      const consumed = new Set();
      metrics.push(...metricsFromMarkdownTables(lines, context, consumed));
      metrics.push(...metricsFromTimingLines(lines, context, consumed));
    }
  });
  return metrics;
}

function extractFromFiles(inputs, flagDefaults) {
  const all = [];
  for (const input of inputs) {
    if (!fs.existsSync(input)) {
      throw new Error(`Input file not found: ${input}`);
    }
    const inferred = inferFromFileName(input);
    const defaults = {
      unityVersion: flagDefaults.unityVersion ?? inferred.unityVersion,
      testMode: flagDefaults.testMode ?? inferred.testMode
    };
    const xml = fs.readFileSync(input, "utf8");
    all.push(...extractFromXml(xml, defaults));
  }
  return all;
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
  if (options.inputs.length === 0) {
    process.stderr.write(`No input files provided.\n\n${usage()}\n`);
    return 2;
  }
  const metrics = extractFromFiles(options.inputs, {
    unityVersion: options.unityVersion,
    testMode: options.testMode
  });
  const json = JSON.stringify(metrics, null, 2);
  if (options.output) {
    fs.writeFileSync(options.output, `${json}\n`, "utf8");
  } else {
    process.stdout.write(`${json}\n`);
  }
  return 0;
}

// --- Self-test -------------------------------------------------------------

function assert(condition, message) {
  if (!condition) {
    throw new Error(`Self-test failed: ${message}`);
  }
}

function findMetric(metrics, sampleGroup) {
  return metrics.find((m) => m.sampleGroup === sampleGroup);
}

function runSelfTest() {
  // Tiny inline fixture modeled on the REAL ProtoEquals/ProtoSerialization
  // tables plus a SpatialTree-style "value (0.003s)" cell and a free-form
  // timing line, all inside <output> CDATA exactly as Unity/NUnit3 emit them.
  const fixture = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<test-run id="2" total="2" passed="2" failed="0">',
    '  <test-suite type="Assembly" name="Perf">',
    '    <test-case fullname="Perf.ProtoEquals.Compare" name="Compare" result="Passed">',
    "      <output><![CDATA[",
    "| Payload | Optimized ProtoEquals (ms) | Classic ProtoEquals (ms) | Speedup |",
    "| ------- | -------------------------:| ------------------------:| -------:|",
    "| Small   |                        12 |                       45 |   3.75x |",
    "| Large   |                     1,200 |                    4,500 |   3.75x |",
    "]]></output>",
    "    </test-case>",
    '    <test-case fullname="Perf.Spatial.Benchmark" name="Benchmark" result="Passed">',
    "      <output><![CDATA[",
    "SpatialTree2D Benchmarks - Tiny",
    "| Construction | RTree | Quadtree |",
    "| ---          | ---   | ---      |",
    "| Build        | 12,345 (0.003s) | 9,000 (0.004s) |",
    "Sort took 12.34 ms",
    "]]></output>",
    "    </test-case>",
    "  </test-suite>",
    "</test-run>"
  ].join("\n");

  const metrics = extractFromXml(fixture, { unityVersion: "6000.3.16f1", testMode: "playmode" });

  // ProtoEquals table: 2 rows x 3 numeric columns = 6 metrics
  // (Speedup "3.75x" is numeric too -> 6, not 4).
  const optSmall = findMetric(metrics, "Small / Optimized ProtoEquals");
  assert(optSmall, "expected 'Small / Optimized ProtoEquals' metric");
  assert(optSmall.median === 12, `Small optimized median should be 12, got ${optSmall.median}`);
  assert(optSmall.unit === "ms", `Small optimized unit should be ms, got ${optSmall.unit}`);
  assert(
    optSmall.unityVersion === "6000.3.16f1" && optSmall.testMode === "playmode",
    "context (unityVersion/testMode) should propagate onto metrics"
  );
  assert(optSmall.test === "Perf.ProtoEquals.Compare", "test fullname should be captured");

  const classicLarge = findMetric(metrics, "Large / Classic ProtoEquals");
  assert(classicLarge && classicLarge.median === 4500, "thousands separators must be stripped");

  const speedup = findMetric(metrics, "Small / Speedup");
  assert(speedup && speedup.median === 3.75, "speedup 3.75x should parse to 3.75");
  assert(speedup.unit === null, "speedup ratio is dimensionless (unit null)");

  // SpatialTree cell "12,345 (0.003s)" -> value 12345, no unit from a bare
  // header "RTree" (the inline (0.003s) is an annotation, not the cell number).
  const build = findMetric(metrics, "Build / RTree");
  assert(
    build && build.median === 12345,
    `Build/RTree should be 12345, got ${build && build.median}`
  );

  // Free-form timing line fallback.
  const sort = findMetric(metrics, "Sort took");
  assert(
    sort && sort.median === 12.34 && sort.unit === "ms",
    "free-form 'Sort took 12.34 ms' must parse"
  );

  // No metric should be double-counted from the table via the timing-line path.
  const buildDupes = metrics.filter((m) => m.sampleGroup === "Build / RTree");
  assert(buildDupes.length === 1, "table cells must not be double-counted as timing lines");

  // File-name inference.
  const inferred = inferFromFileName("results-2022.3.45f1-editmode.xml");
  assert(
    inferred.unityVersion === "2022.3.45f1" && inferred.testMode === "editmode",
    "file-name inference should parse version + mode"
  );

  // Unity SampleGroup JSON fallback path.
  const perfJson = JSON.stringify({
    SampleGroups: [
      {
        Definition: { Name: "Time", SampleUnit: 2 },
        Median: 1.5,
        Min: 1.0,
        Max: 2.0,
        StandardDeviation: 0.25,
        SampleCount: 9
      }
    ]
  }).replace(/"/g, "&quot;");
  const perfXml = [
    '<test-run id="2">',
    '  <test-case fullname="Perf.Unity.Measured" name="Measured" result="Passed">',
    "    <properties>",
    `      <property name="performanceTestResults" value="${perfJson}" />`,
    "    </properties>",
    "  </test-case>",
    "</test-run>"
  ].join("\n");
  const perfMetrics = extractFromXml(perfXml, { unityVersion: null, testMode: null });
  const measured = findMetric(perfMetrics, "Time");
  assert(measured, "Unity SampleGroup JSON should yield a 'Time' metric");
  assert(measured.median === 1.5 && measured.unit === "ms", "SampleGroup median/unit");
  assert(
    measured.min === 1.0 &&
      measured.max === 2.0 &&
      measured.stddev === 0.25 &&
      measured.sampleCount === 9,
    "SampleGroup min/max/stddev/sampleCount must populate"
  );

  process.stdout.write(
    `extract-perf-metrics self-test passed (${metrics.length + perfMetrics.length} metrics across fixtures).\n`
  );
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
  extractFromXml,
  extractFromFiles,
  inferFromFileName,
  parseCell,
  unitFromHeader,
  headerLabel,
  isDividerRow,
  splitRow,
  metricsFromMarkdownTables,
  metricsFromTimingLines,
  collectSampleGroups,
  TIMING_LINE_RE
};
