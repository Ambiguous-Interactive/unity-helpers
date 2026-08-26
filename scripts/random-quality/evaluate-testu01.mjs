#!/usr/bin/env node
// Turns a TestU01 battery report into a verdict.
//
// TestU01 prints one of two summaries: "All tests were passed", or a table of the statistics whose
// p-value fell outside [0.001, 0.9990]. Treating any row in that table as a failure would make this
// permanently red, and that is measured rather than assumed: SmallCrush runs 15 statistics, so a
// clean generator lands one row outside the interval roughly one run in seven. IllusionFlow did
// exactly that here (Gap, 7.2e-4) and produced "All tests were passed" on two other seeds.
//
// A real failure does not look like that. The recorded control, XorShiftRandom, reports
// BirthdaySpacings at `eps`, Collision at `1 - eps1`, MaxOft at 5.6e-16 and MatrixRank at `eps`.
// The threshold below sits in the two-order-of-magnitude gap between 1e-4 noise and 1e-10 signal.

export const DECISIVE = 1e-10;

// `eps` is TestU01's rendering of a p-value below 1e-300 and `eps1` of one below 1e-15.
const SENTINELS = new Map([
  ["eps", 0],
  ["eps1", 1e-16]
]);

/**
 * The distance from the nearest end of [0, 1] that a printed p-value represents, or null when the
 * text is not one.
 *
 * TestU01 renders the top of the interval as `1 - <value>`: `1 - eps1` for a sentinel, and
 * `1 - 1.4e-11` for a plain number. Handling only the sentinel spellings drops every high-side
 * NUMERIC failure on the floor, and a dropped row is indistinguishable from a passing one -- so a
 * decisive failure at the top of the interval would read as clean.
 */
export function extremity(raw) {
  const text = String(raw).trim();
  const complement = /^1\s*-\s*(\S+)$/.exec(text);
  const body = complement === null ? text : complement[1];
  if (SENTINELS.has(body)) {
    return SENTINELS.get(body);
  }
  // `Number("")` is 0, and 0 is the most extreme p-value there is -- so an empty column would read
  // as the most decisive failure this can report rather than as the absence of a reading.
  if (body === "") {
    return null;
  }
  const value = Number(body);
  if (!Number.isFinite(value) || value < 0 || 1 < value) {
    return null;
  }
  return complement === null ? Math.min(value, 1 - value) : value;
}

/** The p-values a report names, as distances from the nearest end of [0, 1]. */
export function extremities(report) {
  const found = [];
  for (const line of String(report ?? "").split("\n")) {
    const match = /^\s+\d+\s{2}(\S.*?)\s{2,}(\S.*?)\s*$/.exec(line);
    if (match === null) {
      continue;
    }
    const raw = match[2].trim();
    const distance = extremity(raw);
    if (distance === null) {
      continue;
    }
    found.push({ test: match[1].trim(), raw, extremity: distance });
  }
  return found;
}

/**
 * Whether a report is a failure worth acting on, and the rows that make it one. A report with no
 * summary at all is a harness fault rather than a pass: `ranBattery` says which.
 */
export function verdict(report) {
  const text = String(report ?? "");
  const clean = text.includes("All tests were passed");
  const listed = text.includes("The following tests gave p-values outside");
  const decisive = extremities(text).filter((row) => row.extremity < DECISIVE);
  return {
    ranBattery: clean || listed,
    failed: 0 < decisive.length,
    decisive,
    marginal: extremities(text).filter((row) => DECISIVE <= row.extremity)
  };
}
