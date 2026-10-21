#!/usr/bin/env node
"use strict";

const fs = require("node:fs");
const { verifyStatistics, quantile, closeToLogged } = require("./lib/calibrated-statistics");
const manifest = require("./wproto-corpus.v1.json");
const OPERATIONS = [
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
const CONTROL_NAMES = ["reference-same-code", "subject-same-code"];
const TYPES = new Set([
  "RUNTIME",
  "CLOCK",
  "PLAN",
  "INPUT",
  "CALIBRATION",
  "CONTROL",
  "SAMPLES",
  "RESULT"
]);
const cases = new Map(manifest.cases.map((entry) => [entry.id, entry]));

function requireEvidence(condition, message) {
  if (!condition) throw new Error(`WProto corpus: ${message}`);
}

function object(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function strictJson(text) {
  requireEvidence(text.length <= 1048576, "record exceeds 1 MiB.");
  const value = JSON.parse(text);
  const tokens = text.match(/"(?:[^"\\]|\\.)*"|[{}\[\]:]/g) || [];
  const stack = [];
  for (let index = 0; index < tokens.length; index++) {
    const token = tokens[index];
    if (token === "{" || token === "[") stack.push(token === "{" ? new Set() : null);
    else if (token === "}" || token === "]") stack.pop();
    else if (token.startsWith('"') && tokens[index + 1] === ":") {
      const keys = stack[stack.length - 1];
      const key = JSON.parse(token);
      requireEvidence(keys instanceof Set && !keys.has(key), `duplicate JSON member ${key}.`);
      keys.add(key);
    }
    requireEvidence(stack.length <= 32, "record nesting exceeds 32.");
  }
  requireEvidence(object(value), "record must be a JSON object.");
  return value;
}

function identities(operation) {
  return manifest.families.map((family) =>
    manifest.sizes.flatMap((size) => {
      const ids = [`${family}/${size}`];
      if (operation.startsWith("read-") && family === "repeated-numeric")
        ids.push(`${family}/${size}-unpacked`);
      return ids;
    })
  );
}

function verifyCalibration(record, key) {
  requireEvidence(
    record.SchemaVersion === 1 &&
      Array.isArray(record.Pairs) &&
      record.Pairs.length >= 1 &&
      record.Pairs.length <= 21,
    `${key}: invalid calibration chain.`
  );
  record.Pairs.forEach((pair, index) => {
    requireEvidence(
      object(pair) &&
        pair.Iterations === 2 ** index &&
        [pair.ReferenceMilliseconds, pair.SubjectMilliseconds].every(
          (value) => typeof value === "number" && Number.isFinite(value) && value >= 0
        ),
      `${key}: calibration must start at one and double with finite nonnegative durations.`
    );
    requireEvidence(
      index === record.Pairs.length - 1 ||
        Math.min(pair.ReferenceMilliseconds, pair.SubjectMilliseconds) < 50,
      `${key}: calibration continued after reaching its target.`
    );
  });
}

function verifySample(text, key, options, calibration) {
  requireEvidence(calibration, `${key}: missing calibration record.`);
  const final = calibration.Pairs[calibration.Pairs.length - 1];
  const reachedTarget = Math.min(final.ReferenceMilliseconds, final.SubjectMilliseconds) >= 50;
  if (text === "inconclusive: calibration failed") {
    requireEvidence(
      calibration.Pairs.length === 21 && !reachedTarget,
      `${key}: calibration failure requires exhausted attempts.`
    );
    return null;
  }
  const evidence = strictJson(text);
  requireEvidence(
    reachedTarget && final.Iterations === evidence.Iterations,
    `${key}: samples do not join the first target-reaching calibration pair.`
  );
  requireEvidence(
    !Object.hasOwn(evidence, "SlotDiagnostics"),
    `${key}: diagnostic timings cannot qualify.`
  );
  for (const name of ["ReferenceMilliseconds", "SubjectMilliseconds"]) {
    requireEvidence(
      Array.isArray(evidence[name]) &&
        evidence[name].length === 32 &&
        evidence[name].every((x) => typeof x === "number" && Number.isFinite(x) && x > 0),
      `${key}: exactly 32 positive finite samples required.`
    );
  }
  requireEvidence(
    evidence.Seed === 343 &&
      Number.isInteger(evidence.Iterations) &&
      evidence.Iterations >= 1 &&
      evidence.Iterations <= 1048576 &&
      (evidence.Iterations & (evidence.Iterations - 1)) === 0,
    `${key}: changed seed or calibration iteration plan.`
  );
  const metadata = evidence.EnvironmentMetadata;
  requireEvidence(
    object(metadata) &&
      metadata.commit === options.commit &&
      metadata.unityVersion === options.unityVersion &&
      metadata.backend === "Mono" &&
      metadata.isEditor === "False" &&
      metadata.buildConfiguration === "Release" &&
      typeof metadata.frameworkDescription === "string" &&
      metadata.frameworkDescription.length > 0,
    `${key}: wrong Release Mono runtime identity.`
  );
  const stats = verifyStatistics(evidence, key, "WProto");
  const improvement = stats.stable && stats.ratio >= 1 / 0.95 && stats.lower95 > 1;
  const nonInferior = stats.stable && stats.lower95 >= 1 / 1.05;
  requireEvidence(
    typeof evidence.HasTimingImprovement === "boolean" &&
      evidence.HasTimingImprovement === improvement &&
      typeof evidence.HasTimingNonInferiority === "boolean" &&
      evidence.HasTimingNonInferiority === nonInferior,
    `${key}: forged timing decision.`
  );
  return {
    ...stats,
    nonInferior,
    improvement,
    logs: evidence.ReferenceMilliseconds.map(
      (value, index) => Math.log(value) - Math.log(evidence.SubjectMilliseconds[index])
    ),
    metadata
  };
}

function combine(families) {
  const ratio = Math.exp(
    families.reduce(
      (sum, family) =>
        sum +
        family.reduce((total, row) => total + row.logs.reduce((a, b) => a + b, 0) / 32, 0) /
          family.length,
      0
    ) / 7
  );
  let state = 343;
  const bootstrap = [];
  for (let repetition = 0; repetition < 2000; repetition++) {
    let aggregate = 0;
    for (const family of families) {
      let familySum = 0;
      for (const row of family) {
        let sum = 0;
        for (let batch = 0; batch < 8; batch++) {
          state ^= state << 13;
          state ^= state >>> 17;
          state ^= state << 5;
          const offset = ((state >>> 0) % 8) * 4;
          for (let cycle = 0; cycle < 4; cycle++) sum += row.logs[offset + cycle];
        }
        familySum += sum / 32;
      }
      aggregate += familySum / family.length;
    }
    bootstrap.push(Math.exp(aggregate / 7));
  }
  bootstrap.sort((a, b) => a - b);
  const lower95 = quantile(bootstrap, 0.025);
  const upper95 = quantile(bootstrap, 0.975);
  const conclusive = families.every((family) => family.every((row) => row.stable));
  const hasTimingNonInferiority = families.every((family) =>
    family.every((row) => row.nonInferior)
  );
  return {
    ratio,
    lower95,
    upper95,
    conclusive,
    hasTimingNonInferiority,
    meetsTwofoldTimingTarget: conclusive && hasTimingNonInferiority && lower95 >= 2
  };
}

function stableObject(value) {
  if (Array.isArray(value)) return value.map(stableObject);
  if (!object(value)) return value;
  return Object.fromEntries(
    Object.keys(value)
      .sort()
      .map((key) => [key, stableObject(value[key])])
  );
}

function publicStatistics(value) {
  if (value === null) return null;
  const { logs, metadata, ...statistics } = value;
  return statistics;
}

function verifyRuntime(record, options, operation) {
  requireEvidence(
    record.SchemaVersion === 1 &&
      record.SourceCommit === options.commit &&
      typeof record.FrozenRunId === "string" &&
      /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(record.FrozenRunId),
    `${operation}: malformed runtime anchor.`
  );
  for (const name of ["FrozenDeclarationSha256", "FrozenCorpusSha256"])
    requireEvidence(
      typeof record[name] === "string" && /^[0-9a-f]{64}$/.test(record[name]),
      `${operation}: malformed ${name}.`
    );
  const runtime = record.RuntimeIdentity;
  requireEvidence(
    object(runtime) &&
      runtime.UnityVersion === options.unityVersion &&
      runtime.Backend === "Mono" &&
      typeof runtime.Platform === "string" &&
      runtime.Platform.endsWith("Player") &&
      (runtime.ProcessBits === 32 || runtime.ProcessBits === 64) &&
      runtime.IsEditor === false &&
      runtime.DevelopmentBuild === false &&
      runtime.WallstopProto === true,
    `${operation}: runtime is not a generated-first Release Mono player.`
  );
  requireEvidence(
    Array.isArray(record.OracleAssemblies) && record.OracleAssemblies.length === 2,
    `${operation}: both oracle assemblies are required.`
  );
  const names = new Set();
  for (const oracle of record.OracleAssemblies) {
    requireEvidence(
      object(oracle) &&
        ["protobuf-net", "protobuf-net.Core"].includes(oracle.Name) &&
        !names.has(oracle.Name) &&
        typeof oracle.Version === "string" &&
        /^3\.[0-9]+\.[0-9]+\.[0-9]+$/.test(oracle.Version) &&
        typeof oracle.Sha256 === "string" &&
        /^[0-9a-f]{64}$/.test(oracle.Sha256),
      `${operation}: malformed or duplicate v3 oracle identity.`
    );
    names.add(oracle.Name);
  }
}

function verifyWProtoCorpus(log, options) {
  requireEvidence(
    typeof log === "string" && Buffer.byteLength(log, "utf8") <= 67108864,
    "log must be at most 64 MiB."
  );
  requireEvidence(
    object(options) &&
      /^[0-9a-f]{40}$/.test(options.commit) &&
      typeof options.unityVersion === "string" &&
      options.unityVersion.length > 0,
    "expected commit and Unity version required."
  );
  requireEvidence(
    cases.size === 32 && manifest.families.length === 7,
    "frozen manifest is incomplete."
  );
  const states = new Map(
    OPERATIONS.map((op) => [
      op,
      {
        inputs: new Map(),
        samples: new Map(),
        controls: new Map(),
        calibrations: new Map(),
        pendingCalibration: null
      }
    ])
  );
  let pendingCalibrationOperation = null;
  for (const line of log.split(/\r?\n/)) {
    if (!line.includes("WPROTO_CORPUS_")) continue;
    const match = /^\s*WPROTO_CORPUS_([A-Z]+) (\S+) (\S+) (.+)$/.exec(line);
    requireEvidence(
      match && TYPES.has(match[1]) && match[2] === manifest.corpusVersion && states.has(match[3]),
      "malformed, unknown, or wrong-version corpus marker."
    );
    const [, kind, , op, payload] = match;
    const state = states.get(op);
    requireEvidence(!state.result, `${op}: record after terminal result.`);
    requireEvidence(
      pendingCalibrationOperation === null ||
        (pendingCalibrationOperation === op && (kind === "CONTROL" || kind === "SAMPLES")),
      `${op}: calibration must immediately precede its measurement record.`
    );
    if (["RUNTIME", "CLOCK", "PLAN"].includes(kind)) {
      const property = kind.toLowerCase();
      requireEvidence(
        !state[property] && state.samples.size === 0,
        `${op}: duplicate or late ${kind}.`
      );
      state[property] = strictJson(payload);
      continue;
    }
    requireEvidence(
      state.plan && state.runtime && state.clock,
      `${op}: records require plan, runtime, and clock first.`
    );
    const groups = identities(op);
    const expected = new Set(groups.flat());
    if (kind === "CALIBRATION") {
      const parsed = /^(\S+) (.+)$/.exec(payload);
      requireEvidence(
        parsed && !state.calibrations.has(parsed[1]),
        `${op}: duplicate or malformed calibration.`
      );
      const id = parsed[1];
      const control = CONTROL_NAMES.includes(id);
      requireEvidence(
        control
          ? state.inputs.has("scalar-heavy/0") &&
              state.samples.size === 0 &&
              !state.controls.has(id)
          : expected.has(id) &&
              state.inputs.has(id) &&
              !state.samples.has(id) &&
              state.controls.size === 2,
        `${op}/${id}: unknown, premature, or late calibration.`
      );
      const calibration = strictJson(parsed[2]);
      verifyCalibration(calibration, `${op}/${id}`);
      state.calibrations.set(id, calibration);
      state.pendingCalibration = id;
      pendingCalibrationOperation = op;
    } else if (kind === "INPUT" || kind === "SAMPLES") {
      const parsed =
        kind === "INPUT"
          ? /^(\S+) bytes=([0-9]+) sha256=(\S+)$/.exec(payload)
          : /^(\S+) (\S+) bytes=([0-9]+) sha256=(\S+) (.+)$/.exec(payload);
      requireEvidence(parsed, `${op}: malformed ${kind}.`);
      const id = kind === "INPUT" ? parsed[1] : `${parsed[1]}/${parsed[2]}`;
      const length = Number(parsed[kind === "INPUT" ? 2 : 3]);
      const hash = parsed[kind === "INPUT" ? 3 : 4];
      const frozen = cases.get(id);
      requireEvidence(
        expected.has(id) && frozen && length === frozen.length && hash === frozen.sha256,
        `${op}/${id}: changed identity, length, or digest.`
      );
      if (kind === "INPUT") {
        requireEvidence(!state.inputs.has(id), `${op}/${id}: duplicate input.`);
        state.inputs.set(id, true);
      } else {
        requireEvidence(
          state.inputs.has(id) &&
            !state.samples.has(id) &&
            state.controls.size === 2 &&
            state.pendingCalibration === id,
          `${op}/${id}: missing input/control or duplicate sample.`
        );
        state.samples.set(
          id,
          verifySample(parsed[5], `${op}/${id}`, options, state.calibrations.get(id))
        );
        state.pendingCalibration = null;
        pendingCalibrationOperation = null;
      }
    } else if (kind === "CONTROL") {
      const parsed = /^(\S+) (.+)$/.exec(payload);
      requireEvidence(
        parsed &&
          CONTROL_NAMES.includes(parsed[1]) &&
          !state.controls.has(parsed[1]) &&
          state.pendingCalibration === parsed[1] &&
          state.samples.size === 0 &&
          state.inputs.has("scalar-heavy/0"),
        `${op}: invalid or late same-code control.`
      );
      state.controls.set(
        parsed[1],
        verifySample(parsed[2], `${op}/${parsed[1]}`, options, state.calibrations.get(parsed[1]))
      );
      state.pendingCalibration = null;
      pendingCalibrationOperation = null;
    } else {
      requireEvidence(
        state.inputs.size === expected.size &&
          state.samples.size === expected.size &&
          state.controls.size === 2 &&
          state.calibrations.size === expected.size + 2,
        `${op}: incomplete terminal corpus.`
      );
      state.result = payload;
    }
  }
  const operations = {};
  const runtimeRecords = {};
  let anchor;
  for (const op of OPERATIONS) {
    const state = states.get(op);
    requireEvidence(
      state.plan && state.runtime && state.clock && state.result,
      `${op}: missing required operation records.`
    );
    const count = op.startsWith("read-") ? 32 : 28;
    const plan = state.plan;
    requireEvidence(
      plan.SchemaVersion === 1 &&
        plan.Seed === 343 &&
        plan.CalibrationTargetMilliseconds === 50 &&
        plan.Batches === 8 &&
        plan.CyclesPerBatch === 4 &&
        plan.BatchOrder === "ABBABAAB" &&
        plan.ExpectedWorkloads === count,
      `${op}: changed fixed plan.`
    );
    verifyRuntime(state.runtime, options, op);
    const identity = JSON.stringify(
      stableObject({
        ...state.runtime,
        OracleAssemblies: [...state.runtime.OracleAssemblies].sort((a, b) =>
          a.Name.localeCompare(b.Name)
        )
      })
    );
    if (anchor === undefined) anchor = identity;
    requireEvidence(identity === anchor, `${op}: mixed runtime anchors.`);
    runtimeRecords[op] = state.runtime;
    const clock = state.clock;
    requireEvidence(
      clock.RequestedMilliseconds === 20 &&
        Number.isFinite(clock.ElapsedMilliseconds) &&
        clock.ElapsedMilliseconds >= 10 &&
        Number.isSafeInteger(clock.Frequency) &&
        clock.Frequency > 0,
      `${op}: failed observable clock control.`
    );
    for (const measurement of [...state.controls.values(), ...state.samples.values()]) {
      if (!measurement) continue;
      requireEvidence(
        measurement.metadata.platform === state.runtime.RuntimeIdentity.Platform &&
          measurement.metadata.processBits === String(state.runtime.RuntimeIdentity.ProcessBits),
        `${op}: sample/runtime identity mismatch.`
      );
    }
    const controlsQualified = [...state.controls.values()].every(
      (s) => s && s.stable && s.lower95 <= 1 && s.upper95 >= 1
    );
    const families = identities(op).map((ids) => ids.map((id) => state.samples.get(id)));
    let aggregate = null;
    if (families.some((family) => family.some((row) => row === null))) {
      requireEvidence(
        state.result === "inconclusive: incomplete calibrated corpus",
        `${op}: calibration failure concealed by result.`
      );
    } else {
      aggregate = combine(families);
      const result =
        /^families=7 workloads=(28|32) ratio=(\S+) lower95=(\S+) upper95=(\S+) conclusive=(True|False) nonInferior=(True|False) twofoldTimingTarget=(True|False)\. Timing only; platform, allocation, retention, size and fresh-process qualification remain separate gates\.$/.exec(
          state.result
        );
      requireEvidence(result && Number(result[1]) === count, `${op}: malformed family result.`);
      for (const [index, key] of [
        [2, "ratio"],
        [3, "lower95"],
        [4, "upper95"]
      ])
        closeToLogged(Number(result[index]), aggregate[key], `${op} family ${key}`);
      requireEvidence(
        (result[5] === "True") === aggregate.conclusive &&
          (result[6] === "True") === aggregate.hasTimingNonInferiority &&
          (result[7] === "True") === aggregate.meetsTwofoldTimingTarget,
        `${op}: forged family decision.`
      );
    }
    const acceptedEvidence = controlsQualified && aggregate !== null && aggregate.conclusive;
    operations[op] = {
      ...manifest.operations[op],
      calibrations: Object.fromEntries(state.calibrations),
      controls: Object.fromEntries(
        [...state.controls].map(([name, value]) => [name, publicStatistics(value)])
      ),
      workloads: Object.fromEntries(
        [...state.samples].map(([name, value]) => [name, publicStatistics(value)])
      ),
      expectedWorkloads: count,
      controlsQualified,
      acceptedEvidence,
      ...aggregate,
      meetsTwofoldTimingTarget: acceptedEvidence && aggregate.meetsTwofoldTimingTarget,
      workloadCount: state.samples.size
    };
  }
  const acceptedEvidence = Object.values(operations).every((op) => op.acceptedEvidence);
  return {
    schemaVersion: 1,
    corpusVersion: manifest.corpusVersion,
    scope: "synthetic-timing-only",
    strictRecordValidity: true,
    acceptedEvidence,
    timingQualified: acceptedEvidence,
    targetSummary: Object.fromEntries(
      ["read", "write"].map((direction) => {
        const entries = Object.entries(operations).filter(([name]) =>
          name.startsWith(direction + "-")
        );
        return [
          direction,
          {
            acceptedEvidence: entries.every(([, value]) => value.acceptedEvidence),
            twofoldTimingTarget: entries.every(([, value]) => value.meetsTwofoldTimingTarget),
            operationCount: entries.length
          }
        ];
      })
    ),
    timingTwofoldTarget: Object.values(operations).every((op) => op.meetsTwofoldTimingTarget),
    operations,
    runtimeRecords
  };
}

if (require.main === module) {
  let output;
  try {
    const args = process.argv.slice(2);
    const options = {};
    for (let i = 0; i < args.length; i += 2) {
      requireEvidence(
        ["--log", "--commit", "--unity-version", "--output"].includes(args[i]) &&
          args[i + 1] &&
          !Object.hasOwn(options, args[i]),
        "invalid CLI options."
      );
      options[args[i]] = args[i + 1];
    }
    output = options["--output"];
    requireEvidence(output && options["--log"], "log/output paths required.");
    fs.rmSync(output, { force: true });
    const result = verifyWProtoCorpus(fs.readFileSync(options["--log"], "utf8"), {
      commit: options["--commit"],
      unityVersion: options["--unity-version"]
    });
    fs.writeFileSync(output, `${JSON.stringify(result, null, 2)}\n`);
    process.stdout.write(`${JSON.stringify(result)}\n`);
  } catch (error) {
    if (output) fs.rmSync(output, { force: true });
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}

module.exports = { verifyWProtoCorpus };
