#!/usr/bin/env node
"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawnSync } = require("node:child_process");
const { verifyWProtoCorpus } = require("../unity/verify-wproto-corpus");
const manifest = require("../unity/wproto-corpus.v1.json");
const commit = "a".repeat(40);
const unityVersion = "2021.3.45f1";
const options = { commit, unityVersion };
const operations = [
  "read-memory",
  "read-stream",
  "read-finite",
  "read-direct",
  "read-public",
  "write-buffer",
  "write-stream",
  "write-array",
  "write-direct",
  "write-public"
];
const prefix = (kind, op) => `WPROTO_CORPUS_${kind} ${manifest.corpusVersion} ${op} `;
let passed = 0;

function evidence(ratio = 2.5) {
  const reference = 40 * ratio;
  return {
    EnvironmentMetadata: {
      commit,
      unityVersion,
      backend: "Mono",
      isEditor: "False",
      buildConfiguration: "Release",
      frameworkDescription: "Mono 6.12",
      platform: "WindowsPlayer",
      processBits: "64"
    },
    ReferenceMilliseconds: Array(32).fill(reference),
    SubjectMilliseconds: Array(32).fill(40),
    PairedLogRatios: Array(32).fill(Math.log(reference) - Math.log(40)),
    Comparison: { Ratio: ratio, ReferenceSpread: 0, SubjectSpread: 0, Cycles: 32 },
    ReferenceSummary: { Median: reference, P95: reference, MedianAbsoluteDeviation: 0 },
    SubjectSummary: { Median: 40, P95: 40, MedianAbsoluteDeviation: 0 },
    RatioLower95: ratio,
    RatioUpper95: ratio,
    HasSufficientTiming: true,
    HasTimingImprovement: ratio >= 1 / 0.95,
    HasTimingNonInferiority: ratio >= 1 / 1.05,
    Iterations: 1,
    Seed: 343,
    ReferenceWarmupMilliseconds: 100,
    SubjectWarmupMilliseconds: 100,
    ReferenceWarmupExecutions: 3,
    SubjectWarmupExecutions: 3
  };
}

function variedEvidence(scale = 1) {
  const value = evidence();
  value.ReferenceMilliseconds = Array.from({ length: 32 }, (_, i) => 80 + Math.floor(i / 4) * 0.3);
  value.PairedLogRatios = value.ReferenceMilliseconds.map((x) => Math.log(x) - Math.log(40));
  // Frozen from an independent Python uint32 whole-batch bootstrap, seed 343, 2000 draws.
  value.Comparison = {
    Ratio: 2.0261771250084215,
    ReferenceSpread: 0.026249999999999885,
    SubjectSpread: 0,
    Cycles: 32
  };
  value.ReferenceSummary = {
    Median: 81.05000000000001,
    P95: 82.1,
    MedianAbsoluteDeviation: 0.6000000000000014
  };
  value.RatioLower95 = 2.0149304053193013;
  value.RatioUpper95 = 2.038404920601711;
  if (scale !== 1) {
    value.ReferenceMilliseconds = value.ReferenceMilliseconds.map((x) => x * scale);
    value.PairedLogRatios = value.ReferenceMilliseconds.map((x) => Math.log(x) - Math.log(40));
    value.Comparison.Ratio *= scale;
    value.ReferenceSummary.Median *= scale;
    value.ReferenceSummary.P95 *= scale;
    value.ReferenceSummary.MedianAbsoluteDeviation *= scale;
    value.RatioLower95 *= scale;
    value.RatioUpper95 *= scale;
  }
  return value;
}

function unstableEvidence(ratio = 2.5) {
  const value = evidence(ratio);
  value.SubjectMilliseconds = Array.from({ length: 32 }, (_, index) => (index % 2 ? 44 : 40));
  value.ReferenceMilliseconds = value.SubjectMilliseconds.map((x) => x * ratio);
  value.PairedLogRatios = value.ReferenceMilliseconds.map(
    (x, index) => Math.log(x) - Math.log(value.SubjectMilliseconds[index])
  );
  value.Comparison.ReferenceSpread = 0.1;
  value.Comparison.SubjectSpread = 0.1;
  value.ReferenceSummary = {
    Median: 42 * ratio,
    P95: 44 * ratio,
    MedianAbsoluteDeviation: 2 * ratio
  };
  value.SubjectSummary = { Median: 42, P95: 44, MedianAbsoluteDeviation: 2 };
  value.HasTimingImprovement = false;
  value.HasTimingNonInferiority = false;
  return value;
}

function runtime() {
  return {
    SchemaVersion: 1,
    FrozenRunId: "fixture-run",
    FrozenDeclarationSha256: "b".repeat(64),
    FrozenCorpusSha256: "c".repeat(64),
    SourceCommit: commit,
    RuntimeIdentity: {
      UnityVersion: unityVersion,
      Backend: "Mono",
      Platform: "WindowsPlayer",
      ProcessBits: 64,
      IsEditor: false,
      DevelopmentBuild: false,
      WallstopProto: true
    },
    OracleAssemblies: ["protobuf-net", "protobuf-net.Core"].map((Name) => ({
      Name,
      Version: "3.2.0.0",
      Sha256: "d".repeat(64)
    }))
  };
}

function calibration(iterations = 1, exhausted = false) {
  const count = exhausted ? 21 : Math.log2(iterations) + 1;
  return {
    SchemaVersion: 1,
    Pairs: Array.from({ length: count }, (_, index) => ({
      Iterations: 2 ** index,
      ReferenceMilliseconds: !exhausted && index === count - 1 ? 60 : index,
      SubjectMilliseconds: !exhausted && index === count - 1 ? 50 : index
    }))
  };
}

function fixture({
  ratio = 2.5,
  varied = false,
  variedScale = 1,
  unstable = false,
  weighted = false,
  reverse = false,
  controls = null,
  calibrationFailure = false,
  controlCalibrationFailure = false,
  iterations = 1
} = {}) {
  const lines = [];
  for (const op of operations) {
    const reading = op.startsWith("read-");
    const rows = manifest.cases.filter((row) => reading || !row.id.endsWith("-unpacked"));
    if (reverse) rows.splice(1, rows.length - 1, ...rows.slice(1).reverse());
    lines.push(prefix("RUNTIME", op) + JSON.stringify(runtime()));
    lines.push(
      prefix("CLOCK", op) +
        JSON.stringify({ RequestedMilliseconds: 20, ElapsedMilliseconds: 21, Frequency: 10000000 })
    );
    lines.push(
      prefix("PLAN", op) +
        JSON.stringify({
          SchemaVersion: 1,
          Seed: 343,
          CalibrationTargetMilliseconds: 50,
          Batches: 8,
          CyclesPerBatch: 4,
          BatchOrder: "ABBABAAB",
          ExpectedWorkloads: reading ? 32 : 28
        })
    );
    for (const [index, row] of rows.entries()) {
      lines.push(prefix("INPUT", op) + `${row.id} bytes=${row.length} sha256=${row.sha256}`);
      if (index === 0) {
        for (const kind of ["reference-same-code", "subject-same-code"]) {
          const failed = controlCalibrationFailure && kind === "reference-same-code";
          const control = { ...(controls || evidence(1)), Iterations: iterations };
          lines.push(
            prefix("CALIBRATION", op) + kind + " " + JSON.stringify(calibration(iterations, failed))
          );
          lines.push(
            prefix("CONTROL", op) +
              kind +
              " " +
              (failed ? "inconclusive: calibration failed" : JSON.stringify(control))
          );
        }
      }
      const sample = unstable
        ? unstableEvidence(ratio)
        : varied
          ? variedEvidence(variedScale)
          : evidence(weighted ? (row.id.startsWith("repeated-numeric/") ? 4 : 2) : ratio);
      sample.Iterations = iterations;
      lines.push(
        prefix("CALIBRATION", op) +
          row.id +
          " " +
          JSON.stringify(calibration(iterations, calibrationFailure && index === 0))
      );
      lines.push(
        prefix("SAMPLES", op) +
          `${row.id.replace("/", " ")} bytes=${row.length} sha256=${row.sha256} ` +
          (calibrationFailure && index === 0
            ? "inconclusive: calibration failed"
            : JSON.stringify(sample))
      );
    }
    if (calibrationFailure) {
      lines.push(prefix("RESULT", op) + "inconclusive: incomplete calibrated corpus");
    } else {
      const average = varied
        ? 2.0261771250084215 * variedScale
        : weighted
          ? Math.pow(4 * Math.pow(2, 6), 1 / 7)
          : ratio;
      const lower = varied
        ? reading
          ? 2.0241188888404937 * variedScale
          : 2.0239899843539186 * variedScale
        : average;
      const upper = varied
        ? reading
          ? 2.0282518379620322 * variedScale
          : 2.028428355133617 * variedScale
        : average;
      const nonInferior = !unstable && (weighted || ratio >= 1 / 1.05);
      lines.push(
        prefix("RESULT", op) +
          `families=7 workloads=${reading ? 32 : 28} ratio=${average} lower95=${lower} upper95=${upper} conclusive=${unstable ? "False" : "True"} nonInferior=${nonInferior ? "True" : "False"} twofoldTimingTarget=${nonInferior && lower >= 2 ? "True" : "False"}. Timing only; platform, allocation, retention, size and fresh-process qualification remain separate gates.`
      );
    }
  }
  return lines.join("\n");
}

function mutateJson(log, kind, mutate) {
  let changed = false;
  return log
    .split("\n")
    .map((line) => {
      if (!changed && line.startsWith(prefix(kind, operations[0]))) {
        const start = line.indexOf("{");
        const value = JSON.parse(line.slice(start));
        mutate(value);
        changed = true;
        return line.slice(0, start) + JSON.stringify(value);
      }
      return line;
    })
    .join("\n");
}

function test(name, action) {
  action();
  ++passed;
  process.stdout.write(`PASS ${name}\n`);
}

const good = fixture();
test("complete stable baseline meets synthetic timing targets only", () => {
  const result = verifyWProtoCorpus(good, options);
  assert.equal(result.strictRecordValidity, true);
  assert.equal(result.acceptedEvidence, true);
  assert.equal(result.timingTwofoldTarget, true);
  assert.equal(result.scope, "synthetic-timing-only");
  assert.equal(Object.keys(result.runtimeRecords).length, 10);
  assert.equal(
    Object.values(result.operations).reduce((sum, op) => sum + op.workloadCount, 0),
    300
  );
  assert.equal(
    Object.values(result.operations).reduce(
      (sum, op) => sum + Object.keys(op.calibrations).length,
      0
    ),
    320
  );
});
test("stable below target remains accepted baseline evidence", () => {
  const result = verifyWProtoCorpus(fixture({ ratio: 1.2 }), options);
  assert.equal(result.acceptedEvidence, true);
  assert.equal(result.timingTwofoldTarget, false);
});
test("stable adverse baseline is valid evidence without non-inferiority", () => {
  const result = verifyWProtoCorpus(fixture({ ratio: 0.8 }), options);
  assert.equal(result.acceptedEvidence, true);
  assert.equal(result.operations["read-memory"].hasTimingNonInferiority, false);
});
test("complete calibration failures remain explicit inconclusive evidence", () => {
  const result = verifyWProtoCorpus(fixture({ calibrationFailure: true }), options);
  assert.equal(result.strictRecordValidity, true);
  assert.equal(result.acceptedEvidence, false);
});
for (const iterations of [4, 1048576]) {
  test(`calibration doubles from zero-duration early probes through ${iterations}`, () => {
    const result = verifyWProtoCorpus(fixture({ iterations }), options);
    assert.equal(result.acceptedEvidence, true);
    assert.equal(
      result.operations["read-memory"].calibrations["scalar-heavy/0"].Pairs.at(-1).Iterations,
      iterations
    );
  });
}
test("exhausted same-code calibration preserves capture but prevents timing acceptance", () => {
  const result = verifyWProtoCorpus(fixture({ controlCalibrationFailure: true }), options);
  assert.equal(result.strictRecordValidity, true);
  assert.equal(result.acceptedEvidence, false);
  assert.equal(result.operations["read-memory"].controls["reference-same-code"], null);
});
test("same-code interval excluding one prevents acceptance", () => {
  const result = verifyWProtoCorpus(fixture({ controls: evidence(1.01) }), options);
  assert.equal(result.acceptedEvidence, false);
  assert.equal(result.timingTwofoldTarget, false);
});
test("unequal family sizes retain equal family weights", () => {
  const result = verifyWProtoCorpus(fixture({ weighted: true }), options);
  assert.ok(
    Math.abs(result.operations["read-memory"].ratio - result.operations["write-buffer"].ratio) <
      1e-12
  );
  assert.ok(
    Math.abs(result.operations["read-memory"].ratio - Math.pow(4 * Math.pow(2, 6), 1 / 7)) < 1e-12
  );
});
test("independent varied family golden and log ordering agree", () => {
  const normal = verifyWProtoCorpus(fixture({ varied: true }), options);
  const reversed = verifyWProtoCorpus(fixture({ varied: true, reverse: true }), options);
  assert.deepEqual(normal.operations, reversed.operations);
  assert.ok(Math.abs(normal.operations["read-memory"].lower95 - 2.0241188888404937) < 1e-12);
  assert.ok(Math.abs(normal.operations["write-buffer"].upper95 - 2.028428355133617) < 1e-12);
});
test("operation blocks can arrive in NUnit discovery order", () => {
  const lines = good.split("\n");
  const reversed = [...operations]
    .reverse()
    .flatMap((op) => lines.filter((line) => line.split(" ")[2] === op))
    .join("\n");
  assert.deepEqual(verifyWProtoCorpus(reversed, options), verifyWProtoCorpus(good, options));
});

for (const [name, kind, mutate] of [
  [
    "calibration schema string",
    "CALIBRATION",
    (v) => {
      v.SchemaVersion = "1";
    }
  ],
  [
    "empty calibration",
    "CALIBRATION",
    (v) => {
      v.Pairs = [];
    }
  ],
  [
    "nonarray calibration",
    "CALIBRATION",
    (v) => {
      v.Pairs = v.Pairs[0];
    }
  ],
  [
    "calibration starts above one",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].Iterations = 2;
    }
  ],
  [
    "string calibration iteration",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].Iterations = "1";
    }
  ],
  [
    "negative calibration duration",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].ReferenceMilliseconds = -1;
    }
  ],
  [
    "null calibration duration",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].SubjectMilliseconds = null;
    }
  ],
  [
    "string calibration duration",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].SubjectMilliseconds = "50";
    }
  ],
  [
    "nonfinite calibration duration",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].SubjectMilliseconds = Infinity;
    }
  ],
  [
    "sample before target reached",
    "CALIBRATION",
    (v) => {
      v.Pairs[0].SubjectMilliseconds = 49.99;
    }
  ],
  [
    "continued after successful calibration",
    "CALIBRATION",
    (v) => {
      v.Pairs.push({ ...v.Pairs[0], Iterations: 2 });
    }
  ],
  [
    "calibration iteration mismatch",
    "CONTROL",
    (v) => {
      v.Iterations = 2;
    }
  ],
  [
    "workload calibration iteration mismatch",
    "SAMPLES",
    (v) => {
      v.Iterations = 2;
    }
  ],
  [
    "wrong seed",
    "PLAN",
    (v) => {
      v.Seed = 344;
    }
  ],
  [
    "changed calibration target",
    "PLAN",
    (v) => {
      v.CalibrationTargetMilliseconds = 500;
    }
  ],
  [
    "changed batch order",
    "PLAN",
    (v) => {
      v.BatchOrder = "A".repeat(4) + "B".repeat(4);
    }
  ],
  [
    "string batch count",
    "PLAN",
    (v) => {
      v.Batches = "8";
    }
  ],
  [
    "wrong row count",
    "PLAN",
    (v) => {
      v.ExpectedWorkloads = 28;
    }
  ],
  [
    "missed sleep",
    "CLOCK",
    (v) => {
      v.ElapsedMilliseconds = 0;
    }
  ],
  [
    "fractional frequency",
    "CLOCK",
    (v) => {
      v.Frequency = 1.5;
    }
  ],
  [
    "wrong commit",
    "RUNTIME",
    (v) => {
      v.SourceCommit = "e".repeat(40);
    }
  ],
  [
    "wrong backend",
    "RUNTIME",
    (v) => {
      v.RuntimeIdentity.Backend = "IL2CPP";
    }
  ],
  [
    "editor result",
    "RUNTIME",
    (v) => {
      v.RuntimeIdentity.IsEditor = true;
    }
  ],
  [
    "development player",
    "RUNTIME",
    (v) => {
      v.RuntimeIdentity.DevelopmentBuild = true;
    }
  ],
  [
    "missing generated-first symbol",
    "RUNTIME",
    (v) => {
      v.RuntimeIdentity.WallstopProto = false;
    }
  ],
  [
    "string generated-first symbol",
    "RUNTIME",
    (v) => {
      v.RuntimeIdentity.WallstopProto = "true";
    }
  ],
  [
    "missing oracle",
    "RUNTIME",
    (v) => {
      v.OracleAssemblies.pop();
    }
  ],
  [
    "duplicate oracle",
    "RUNTIME",
    (v) => {
      v.OracleAssemblies[1] = v.OracleAssemblies[0];
    }
  ],
  [
    "wrong oracle major",
    "RUNTIME",
    (v) => {
      v.OracleAssemblies[0].Version = "2.4.0.0";
    }
  ],
  [
    "mixed declaration",
    "RUNTIME",
    (v) => {
      v.FrozenDeclarationSha256 = "f".repeat(64);
    }
  ],
  [
    "missing raw sample",
    "SAMPLES",
    (v) => {
      v.ReferenceMilliseconds.pop();
    }
  ],
  [
    "nonfinite JSON null sample",
    "SAMPLES",
    (v) => {
      v.SubjectMilliseconds[0] = null;
    }
  ],
  [
    "string sample",
    "SAMPLES",
    (v) => {
      v.SubjectMilliseconds[0] = "40";
    }
  ],
  [
    "forged timing boolean",
    "SAMPLES",
    (v) => {
      v.HasTimingNonInferiority = false;
    }
  ],
  [
    "string timing boolean",
    "SAMPLES",
    (v) => {
      v.HasTimingImprovement = "true";
    }
  ],
  [
    "diagnostics even null",
    "SAMPLES",
    (v) => {
      v.SlotDiagnostics = null;
    }
  ],
  [
    "raw ratio forgery",
    "SAMPLES",
    (v) => {
      v.Comparison.Ratio = 9;
    }
  ],
  [
    "raw interval forgery",
    "SAMPLES",
    (v) => {
      v.RatioLower95 = 9;
    }
  ],
  [
    "raw sample forgery",
    "SAMPLES",
    (v) => {
      v.ReferenceMilliseconds[0] = 200;
    }
  ],
  [
    "non-power calibration count",
    "SAMPLES",
    (v) => {
      v.Iterations = 3;
    }
  ],
  [
    "changed sample seed",
    "SAMPLES",
    (v) => {
      v.Seed = 1;
    }
  ],
  [
    "sample metadata disagreement",
    "SAMPLES",
    (v) => {
      v.EnvironmentMetadata.platform = "LinuxPlayer";
    }
  ],
  [
    "control raw decision forgery",
    "CONTROL",
    (v) => {
      v.HasTimingImprovement = true;
    }
  ]
])
  test(name, () =>
    assert.throws(() => verifyWProtoCorpus(mutateJson(good, kind, mutate), options))
  );

for (const kind of [
  "RUNTIME",
  "CLOCK",
  "PLAN",
  "INPUT",
  "CALIBRATION",
  "CONTROL",
  "SAMPLES",
  "RESULT"
]) {
  test(`missing ${kind}`, () => {
    let removed = false;
    const log = good
      .split("\n")
      .filter((line) => {
        if (!removed && line.startsWith(prefix(kind, operations[0]))) {
          removed = true;
          return false;
        }
        return true;
      })
      .join("\n");
    assert.throws(() => verifyWProtoCorpus(log, options));
  });
  test(`duplicate ${kind}`, () => {
    const log = good.split("\n");
    const i = log.findIndex((line) => line.startsWith(prefix(kind, operations[0])));
    log.splice(i, 0, log[i]);
    assert.throws(() => verifyWProtoCorpus(log.join("\n"), options));
  });
}
for (const [name, change] of [
  [
    "missing operation",
    (s) =>
      s
        .split("\n")
        .filter((line) => line.split(" ")[2] !== "write-public")
        .join("\n")
  ],
  ["unknown operation", (s) => s.replace("read-memory", "read-invented")],
  ["unknown marker", (s) => s + "\nWPROTO_CORPUS_UNKNOWN v op {}"],
  ["embedded malformed marker", (s) => s + "\nprefix WPROTO_CORPUS_BAD v op {}"],
  [
    "nonfinite raw calibration number",
    (s) => s.replace('"SubjectMilliseconds":50', '"SubjectMilliseconds":1e999')
  ],
  ["wrong version", (s) => s.replace("wproto-seven-families-v1", "wproto-seven-families-v2")],
  [
    "wrong digest",
    (s) => {
      const digest = Buffer.from(manifest.cases[0].sha256, "base64");
      digest[0] ^= 1;
      return s.replace(`sha256=${manifest.cases[0].sha256}`, `sha256=${digest.toString("base64")}`);
    }
  ],
  ["wrong delivered bytes", (s) => s.replace("bytes=3", "bytes=4")],
  ["changed identity", (s) => s.replace("scalar-heavy/0", "scalar-heavy/2")],
  ["forged family ratio", (s) => s.replace("ratio=2.5", "ratio=9")],
  [
    "forged family target",
    (s) => s.replace("twofoldTimingTarget=True", "twofoldTimingTarget=False")
  ],
  ["duplicate JSON key", (s) => s.replace('"Seed":343', '"Seed":343,"Seed":343')],
  [
    "escaped duplicate JSON key",
    (s) => s.replace('"Seed":343', '"Seed":343,"\\u0053\\u0065\\u0065\\u0064":343')
  ],
  ["empty log", () => ""]
])
  test(name, () => assert.throws(() => verifyWProtoCorpus(change(good), options)));

for (const [name, base, mutate] of [
  [
    "skipped calibration iteration",
    fixture({ iterations: 4 }),
    (v) => {
      v.Pairs.splice(1, 1);
    }
  ],
  [
    "repeated calibration iteration",
    fixture({ iterations: 4 }),
    (v) => {
      v.Pairs[1].Iterations = 1;
    }
  ],
  [
    "truncated exhaustion",
    fixture({ controlCalibrationFailure: true }),
    (v) => {
      v.Pairs.pop();
    }
  ],
  [
    "exhaustion exceeds iteration cap",
    fixture({ controlCalibrationFailure: true }),
    (v) => {
      v.Pairs.push({ Iterations: 2097152, ReferenceMilliseconds: 1, SubjectMilliseconds: 1 });
    }
  ],
  [
    "successful chain reported as exhausted",
    fixture({ controlCalibrationFailure: true }),
    (v) => {
      v.Pairs.at(-1).ReferenceMilliseconds = 50;
      v.Pairs.at(-1).SubjectMilliseconds = 50;
    }
  ]
]) {
  test(name, () =>
    assert.throws(() => verifyWProtoCorpus(mutateJson(base, "CALIBRATION", mutate), options))
  );
}
for (const [name, alter] of [
  [
    "unknown calibration subject",
    (lines, index) => {
      lines[index] = lines[index].replace("reference-same-code", "unknown-subject");
    }
  ],
  [
    "calibration after its control",
    (lines, index) => {
      [lines[index], lines[index + 1]] = [lines[index + 1], lines[index]];
    }
  ],
  [
    "another operation interrupts calibration join",
    (lines, index) => {
      const other = lines.findIndex((line) => line.startsWith(prefix("RUNTIME", "read-stream")));
      const [moved] = lines.splice(other, 1);
      lines.splice(index + 1, 0, moved);
    }
  ],
  [
    "calibration attributed to another control",
    (lines, index) => {
      lines[index] = lines[index].replace("reference-same-code", "subject-same-code");
    }
  ],
  [
    "calibration separated from its result",
    (lines, index) => {
      lines.splice(
        index + 1,
        0,
        lines.find((line) => line.startsWith(prefix("INPUT", operations[0])))
      );
    }
  ]
]) {
  test(name, () => {
    const lines = good.split("\n");
    const index = lines.findIndex((line) => line.startsWith(prefix("CALIBRATION", operations[0])));
    alter(lines, index);
    assert.throws(() => verifyWProtoCorpus(lines.join("\n"), options));
  });
}

test("consistent unstable raw samples are valid but inconclusive", () => {
  const result = verifyWProtoCorpus(fixture({ unstable: true }), options);
  assert.equal(result.strictRecordValidity, true);
  assert.equal(result.acceptedEvidence, false);
  assert.equal(result.operations["read-memory"].conclusive, false);
});
test("unstable same-code observations cannot qualify", () => {
  const result = verifyWProtoCorpus(fixture({ controls: unstableEvidence(1) }), options);
  assert.equal(result.acceptedEvidence, false);
});
test("twofold timing target uses lower bound rather than point estimate", () => {
  const result = verifyWProtoCorpus(fixture({ varied: true, variedScale: 0.9877 }), options);
  assert.equal(result.acceptedEvidence, true);
  assert.ok(result.operations["read-memory"].ratio > 2);
  assert.ok(result.operations["read-memory"].lower95 < 2);
  assert.equal(result.timingTwofoldTarget, false);
});
test("runtime JSON member and oracle ordering are immaterial", () => {
  const changed = mutateJson(good, "RUNTIME", (record) => {
    const fields = Object.entries(record.RuntimeIdentity).reverse();
    record.RuntimeIdentity = Object.fromEntries(fields);
    record.OracleAssemblies.reverse();
  });
  assert.equal(verifyWProtoCorpus(changed, options).acceptedEvidence, true);
});

test("frozen manifest matches every source identity exactly", () => {
  const source = fs.readFileSync(
    path.join(__dirname, "../../Tests/Runtime/Serialization/ProtoBenchmarkCorpusIdentity.cs"),
    "utf8"
  );
  const matches = [
    ...source.matchAll(/\["([^"]+)"\]\s*=\s*\(\s*(\d+),\s*"([A-Za-z0-9+/=]+)"\s*\)/g)
  ];
  assert.equal(matches.length, 32);
  assert.deepEqual(
    matches.map((m) => ({ id: m[1], length: Number(m[2]), sha256: m[3] })),
    manifest.cases
  );
});
test("invalid CLI removes stale summary", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "wproto-corpus-"));
  try {
    const log = path.join(directory, "player.log");
    const output = path.join(directory, "summary.json");
    fs.writeFileSync(log, "");
    fs.writeFileSync(output, '{"acceptedEvidence":true}');
    const result = spawnSync(
      process.execPath,
      [
        path.join(__dirname, "../unity/verify-wproto-corpus.js"),
        "--log",
        log,
        "--commit",
        commit,
        "--unity-version",
        unityVersion,
        "--output",
        output
      ],
      { encoding: "utf8" }
    );
    assert.ifError(result.error);
    assert.equal(result.status, 1);
    assert.equal(fs.existsSync(output), false);
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
process.stdout.write(`${passed} WProto corpus controls passed.\n`);
