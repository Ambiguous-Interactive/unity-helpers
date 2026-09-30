"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { test } = require("node:test");
const { spawnSync } = require("node:child_process");
const { publish, START, END } = require("../unity/lib/publish-benchmark-docs.js");
const { run, requireCompleteBaseline } = require("../unity/lib/render-perf-deltas.js");

const file = "results-6000.6.0f1-playmode.xml";
const metric = {
  test: "Fixture.Benchmark",
  sampleGroup: "Small / Time",
  unit: "ms",
  median: 1.23456789,
  unityVersion: "6000.6.0f1",
  testMode: "playmode",
  sampleCount: null
};
const provenance = {
  commit: "a".repeat(40),
  runUrl: "https://github.com/wallstop/unity-helpers/actions/runs/123",
  runAttempt: "2",
  ref: "main",
  generatedAt: "2026-09-30T12:00:00.000Z"
};

function fixture(context) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "benchmark-docs-test-"));
  context.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const document = path.join(directory, "guide.md");
  const original = `# Guide\n\nHistorical results stay intact.\n\n${START}\n\nLast good measured report.\n\n${END}\n\nAcceptance policy stays intact.\n`;
  fs.writeFileSync(document, original);
  return {
    directory,
    original,
    document,
    decision: {
      complete: true,
      invalidSuccessfulMatrix: false,
      expectedFiles: [file],
      actualFiles: [file]
    },
    metrics: [metric],
    provenance
  };
}

test("complete measurements publish all values with provenance while baseline remains unseeded", (context) => {
  const input = fixture(context);
  const baseline = path.join(input.directory, "baseline.json");
  const current = path.join(input.directory, "current.json");
  fs.writeFileSync(baseline, '{"metrics":[]}\n');
  fs.writeFileSync(current, JSON.stringify(input.metrics));
  const comparison = run({ current, baseline });
  assert.equal(comparison.meta.noBaseline, true);
  assert.doesNotThrow(() => requireCompleteBaseline(comparison));
  assert.equal(publish(input).metricCount, 1);
  const result = fs.readFileSync(input.document, "utf8");
  for (const text of [
    "1.23456789 ms",
    provenance.commit,
    provenance.runUrl,
    "attempt 2",
    "6000.6.0f1",
    "playmode",
    "Single aggregate",
    "Historical results stay intact.",
    "Acceptance policy stays intact.",
    "https://github.com/wallstop/unity-helpers/blob/main/perf-results/" + file
  ]) {
    assert.ok(result.includes(text), text);
  }
  assert.equal(fs.readFileSync(baseline, "utf8"), '{"metrics":[]}\n');
});

test("a measured regression publishes current documentation without promoting the seeded baseline", (context) => {
  const input = fixture(context);
  const baseline = path.join(input.directory, "baseline.json");
  const current = path.join(input.directory, "current.json");
  const baselineBytes = JSON.stringify({ metrics: [{ ...metric, median: 0.5 }] });
  fs.writeFileSync(baseline, baselineBytes);
  fs.writeFileSync(current, JSON.stringify(input.metrics));
  const comparison = run({
    current,
    baseline,
    tolerance: 0.05,
    regressionThreshold: 0.1,
    stddevMultiplier: 1
  });
  requireCompleteBaseline(comparison);
  assert.equal(comparison.significant, true);
  assert.equal(publish(input).published, true);
  assert.ok(fs.readFileSync(input.document, "utf8").includes("1.23456789 ms"));
  assert.equal(fs.readFileSync(baseline, "utf8"), baselineBytes);
});

test("actual workflow comparison preserves published measurements when a seeded comparator is missing", (context) => {
  const input = fixture(context);
  const results = path.join(input.directory, "perf-results");
  fs.mkdirSync(results);
  const baseline = path.join(results, "baseline.json");
  const current = path.join(results, "perf-current.json");
  const baselineBytes = JSON.stringify({
    metrics: [metric, { ...metric, sampleGroup: "Missing operation" }]
  });
  fs.writeFileSync(baseline, baselineBytes);
  fs.writeFileSync(current, JSON.stringify(input.metrics));
  fs.writeFileSync(path.join(results, "perf-deltas.md"), "Old comparison must disappear.");
  fs.writeFileSync(path.join(results, "perf-deltas.json"), "{}");
  const scripts = path.join(input.directory, "scripts/unity/lib");
  fs.mkdirSync(scripts, { recursive: true });
  fs.copyFileSync(
    path.join(__dirname, "../unity/lib/render-perf-deltas.js"),
    path.join(scripts, "render-perf-deltas.js")
  );
  assert.equal(publish(input).published, true);
  const published = fs.readFileSync(input.document, "utf8");
  const workflow = fs.readFileSync(
    path.join(__dirname, "../../.github/workflows/unity-benchmarks.yml"),
    "utf8"
  );
  const start = workflow.indexOf("      - name: Compute perf deltas vs baseline");
  const end = workflow.indexOf("      # git-auto-commit-action", start);
  const comparisonStep = workflow.slice(start, end);
  assert.match(comparisonStep, /continue-on-error: true/);
  const runBlock = comparisonStep
    .slice(comparisonStep.indexOf("        run: |\n") + "        run: |\n".length)
    .split("\n")
    .map((line) => (line.startsWith("          ") ? line.slice(10) : line))
    .join("\n");
  const comparison = spawnSync("bash", ["-c", runBlock], {
    cwd: input.directory,
    env: { ...process.env, GITHUB_OUTPUT: path.join(input.directory, "outputs") },
    encoding: "utf8"
  });
  assert.equal(comparison.status, 1, comparison.stdout + comparison.stderr);
  assert.match(comparison.stderr, /omit 1 baseline metric/);
  assert.equal(fs.readFileSync(input.document, "utf8"), published);
  assert.equal(fs.readFileSync(baseline, "utf8"), baselineBytes);
  for (const transient of ["perf-current.json", "perf-deltas.md", "perf-deltas.json"]) {
    assert.equal(fs.existsSync(path.join(results, transient)), false, transient);
  }
  const commitStep = workflow.indexOf("      - name: Commit perf results");
  const failureStep = workflow.indexOf("      - name: Require valid benchmark comparison");
  assert.ok(start < commitStep && commitStep < failureStep);
  assert.match(workflow.slice(failureStep), /steps\.perf_deltas\.outcome == 'failure'/);
  assert.match(workflow.slice(failureStep), /exit 1/);
});

for (const reason of ["failure", "missing result", "cancelled"]) {
  test(`incomplete ${reason} preserves last good document without requiring current metrics`, (context) => {
    const input = fixture(context);
    input.decision.complete = false;
    input.metrics = null;
    input.provenance = null;
    assert.equal(publish(input).published, false);
    assert.equal(fs.readFileSync(input.document, "utf8"), input.original);
  });
}

for (const [name, mutate] of [
  [
    "empty metric set",
    (input) => {
      input.metrics = [];
    }
  ],
  [
    "duplicate metric",
    (input) => {
      input.metrics = [metric, metric];
    }
  ],
  [
    "invalid measurement",
    (input) => {
      input.metrics = [{ ...metric, median: NaN }];
    }
  ],
  [
    "scope mismatch",
    (input) => {
      input.metrics = [{ ...metric, testMode: "editmode" }];
    }
  ],
  [
    "missing matrix member",
    (input) => {
      input.decision.expectedFiles.push("results-2022.3.0f1-playmode.xml");
    }
  ],
  [
    "unexpected matrix member",
    (input) => {
      input.decision.actualFiles.push("results-2022.3.0f1-playmode.xml");
    }
  ],
  [
    "duplicate matrix identity",
    (input) => {
      input.decision.expectedFiles.push(file);
      input.decision.actualFiles.push(file);
    }
  ],
  [
    "invalid provenance",
    (input) => {
      input.provenance = { ...provenance, commit: "unknown" };
    }
  ],
  [
    "invalid sample count",
    (input) => {
      input.metrics = [{ ...metric, sampleCount: -1 }];
    }
  ],
  [
    "unordered document markers",
    (input) => {
      fs.writeFileSync(input.document, END + START);
      input.original = END + START;
    }
  ]
]) {
  test(`${name} refuses publication and preserves previous documentation`, (context) => {
    const input = fixture(context);
    mutate(input);
    assert.throws(() => publish(input));
    assert.equal(fs.readFileSync(input.document, "utf8"), input.original);
  });
}

test("reported distributions and literal labels do not invent aggregates or inject Markdown", (context) => {
  const input = fixture(context);
  input.metrics = [{ ...metric, sampleCount: 32, sampleGroup: "A|B <tag>\n# Heading" }];
  publish(input);
  const result = fs.readFileSync(input.document, "utf8");
  assert.ok(result.includes("Reported median; 32 samples"));
  assert.ok(result.includes("A\\|B &lt;tag&gt; \\# Heading"));
  assert.ok(!result.includes("<tag>"));
});

test("generated documentation with literal raw labels stays Prettier clean and limits spell suppression to the table", async (context) => {
  const input = fixture(context);
  const guide = path.join(__dirname, "../../docs/performance/baseline-tests-performance.md");
  fs.copyFileSync(guide, input.document);
  input.metrics = [
    { ...metric, test: "Benchmark([edge]|<value>)", sampleGroup: "A|B <tag>\n# Heading" }
  ];
  publish(input);
  const content = fs.readFileSync(input.document, "utf8");
  const prettier = await import("prettier");
  const config = await prettier.resolveConfig(guide);
  assert.equal(content, await prettier.format(content, { ...config, filepath: guide }));
  const disable = content.indexOf("<!-- cspell:disable -->");
  const table = content.indexOf("| Test ", disable);
  const enable = content.indexOf("<!-- cspell:enable -->", table);
  assert.ok(disable > content.indexOf("Raw NUnit evidence:"));
  assert.ok(disable < table && table < enable);
  assert.ok(enable < content.indexOf(END));
});

for (const ref of ["main", "benchmark/manual-results"]) {
  test(`raw evidence resolves the publication repository and ref ${ref}, not the measured candidate SHA`, (context) => {
    const input = fixture(context);
    input.provenance = {
      ...provenance,
      ref,
      runUrl: "https://github.com/Ambiguous-Interactive/unity-helpers/actions/runs/123"
    };
    publish(input);
    const content = fs.readFileSync(input.document, "utf8");
    const link =
      "https://github.com/Ambiguous-Interactive/unity-helpers/blob/" +
      encodeURIComponent(ref) +
      "/perf-results/" +
      file;
    assert.ok(content.includes(link));
    assert.ok(!content.includes("/blob/" + provenance.commit));
    assert.ok(!content.includes("](../../perf-results/"));
  });
}

test("publication links encode Markdown delimiters in branch refs and raw evidence filenames", (context) => {
  const input = fixture(context);
  const evidence = "results-6000.6.0f1+build(1)-playmode.xml";
  input.provenance = { ...provenance, ref: "benchmark/results(weekly)" };
  input.decision.expectedFiles = [evidence];
  input.decision.actualFiles = [evidence];
  input.metrics = [{ ...metric, unityVersion: "6000.6.0f1+build(1)" }];
  publish(input);
  const content = fs.readFileSync(input.document, "utf8");
  assert.ok(
    content.includes(
      // cspell:disable-next-line
      "/blob/benchmark%2Fresults%28weekly%29/perf-results/results-6000.6.0f1%2Bbuild%281%29-playmode.xml"
    )
  );
});

test("publication requires a safe explicit ref and preserves documentation when it is absent", (context) => {
  for (const ref of [undefined, "", "bad\nref"]) {
    const input = fixture(context);
    input.provenance = { ...provenance, ref };
    assert.throws(() => publish(input), /publication ref/);
    assert.equal(fs.readFileSync(input.document, "utf8"), input.original);
  }
});

test("current guide and representative generated report have no repository asset links outside the MkDocs tree", (context) => {
  const input = fixture(context);
  const guide = path.join(__dirname, "../../docs/performance/baseline-tests-performance.md");
  fs.copyFileSync(guide, input.document);
  publish(input);
  const content = fs.readFileSync(input.document, "utf8");
  for (const link of content.matchAll(/\]\(([^)]+)\)/g)) {
    const destination = link[1];
    if (/^https?:\/\//.test(destination) || destination.startsWith("#")) {
      continue;
    }
    const resolved = path.resolve(path.dirname(guide), destination.split("#")[0]);
    const docsRoot = path.resolve(__dirname, "../../docs") + path.sep;
    assert.ok(resolved.startsWith(docsRoot), destination);
    assert.ok(fs.existsSync(resolved), destination);
  }
  assert.ok(
    content.includes(
      "https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/perf-results/baseline.json"
    )
  );
});

test("workflow publishes only complete datasets and commits the generated guide", () => {
  const workflow = fs.readFileSync(
    path.join(__dirname, "../../.github/workflows/unity-benchmarks.yml"),
    "utf8"
  );
  assert.match(workflow, /publish-benchmark-docs\.js/);
  assert.match(workflow, /BENCHMARK_PUBLICATION_REF: \$\{\{ github\.ref_name \}\}/);
  assert.ok(workflow.includes('--ref "${BENCHMARK_PUBLICATION_REF}"'));
  assert.match(
    workflow,
    /file_pattern:.*perf-results\/\*\*.*docs\/performance\/baseline-tests-performance\.md/
  );
  assert.ok(
    workflow.indexOf("publish-benchmark-docs.js") <
      workflow.indexOf("rm -f perf-results/perf-current.json")
  );
  assert.ok(!/^\s+schedule:/m.test(workflow));
});
