"use strict";

// Fixture coverage for the two parsers behind `npm run test:build-lock-action-inputs`.
//
// That gate needs a checkout of the central policy repository, so it SKIPS on a developer machine
// and only really runs in CI. Its parsers therefore had no local coverage at all, and their failure
// mode is silence: a shape they do not recognize is dropped, the call count quietly falls, and the
// gate still exits zero. Three real bugs were found this way rather than by a failing run --
// `uses:` lines with a trailing `# v1.9.1` comment (15 of 31 calls missed), a sequence item's key
// column being two greater than its indentation, and a step with no `with:` adopting the NEXT
// step's block.
//
// These cases are the shapes, not the repository's current content: they must keep passing when the
// workflows are rewritten.

const assert = require("node:assert/strict");

const { collectWithKeys, parseActionInputs } = require("../validate-build-lock-action-inputs.js");

const ACTION =
  "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/example@0123456789abcdef0123456789abcdef01234567";

let passed = 0;

/**
 * Runs collectWithKeys over a fixture, deriving the step column the way the caller does.
 *
 * @param {string[]} lines Fixture lines.
 * @returns {string[]} Supplied input names.
 */
function suppliedKeys(lines) {
  const index = lines.findIndex((line) => line.includes(ACTION));
  assert.notEqual(index, -1, "fixture must contain the action reference");
  const indent = lines[index].length - lines[index].trimStart().length;
  const column = /^-\s/.test(lines[index].trim()) ? indent + 2 : indent;
  return collectWithKeys(lines, index, column);
}

/**
 * @param {string} description Case name.
 * @param {string[]} lines Fixture lines.
 * @param {string[]} expected Expected input names.
 */
function checkKeys(description, lines, expected) {
  assert.deepEqual(suppliedKeys(lines), expected, description);
  passed += 1;
}

checkKeys(
  "the ordinary shape: uses, then with",
  [
    "      - name: Step",
    `        uses: ${ACTION}`,
    "        with:",
    "          a: 1",
    "          b: 2",
    "",
    "      - name: Next"
  ],
  ["a", "b"]
);

// YAML mapping keys are unordered, so this is legal and a forward-only scan reports every required
// input missing -- a false failure that blocks a bump which works.
checkKeys(
  "with declared BEFORE uses in the same step",
  [
    "      - name: Step",
    "        with:",
    "          a: 1",
    "          b: 2",
    `        uses: ${ACTION}`,
    "",
    "      - name: Next"
  ],
  ["a", "b"]
);

checkKeys(
  "uses on the sequence item's own line",
  [`      - uses: ${ACTION}`, "        with:", "          a: 1", "      - name: Next"],
  ["a"]
);

checkKeys(
  "a list-valued input contributes its own name and not its items",
  [
    `      - uses: ${ACTION}`,
    "        with:",
    "          a: 1",
    "          paths:",
    "            - one",
    "            - two",
    "          b: 2",
    "      - name: Next"
  ],
  ["a", "paths", "b"]
);

checkKeys(
  "a block scalar's content is not mistaken for input names",
  [
    `      - uses: ${ACTION}`,
    "        with:",
    "          script: |",
    "            key: not-an-input",
    "            other: also-not",
    "          b: 2",
    "      - name: Next"
  ],
  ["script", "b"]
);

checkKeys(
  "a comment inside with is not an input",
  [
    `      - uses: ${ACTION}`,
    "        with:",
    "          # note: this is not a key",
    "          a: 1",
    "      - name: Next"
  ],
  ["a"]
);

// The step supplies nothing; the NEXT step's block must not be adopted as this call's.
checkKeys(
  "a step with no with block supplies nothing",
  [`      - uses: ${ACTION}`, "      - name: Next", "        with:", "          leaked: 1"],
  []
);

checkKeys(
  "env after with ends the input block",
  [
    `      - uses: ${ACTION}`,
    "        with:",
    "          a: 1",
    "        env:",
    "          NOT_AN_INPUT: 1",
    "      - name: Next"
  ],
  ["a"]
);

checkKeys(
  "env before uses is walked through rather than ending the step",
  [
    "      - name: Step",
    "        env:",
    "          X: 1",
    `        uses: ${ACTION}`,
    "        with:",
    "          a: 1",
    "      - name: Next"
  ],
  ["a"]
);

checkKeys(
  "the last step in a file needs no terminator",
  [`      - uses: ${ACTION}`, "        with:", "          a: 1"],
  ["a"]
);

const declared = parseActionInputs(
  [
    "name: Example",
    "inputs:",
    "  plain:",
    "    description: required with no default",
    "    required: true",
    "  defaulted-after:",
    "    description: required, but the runner substitutes",
    "    required: true",
    '    default: "d"',
    "  defaulted-before:",
    '    default: "d"',
    "    required: true",
    "  optional:",
    "    description: not required",
    "    required: false",
    "  folded:",
    "    description: >-",
    "      a folded description whose text mentions",
    "      required: true",
    "    required: false",
    "outputs:",
    "  ignored:",
    "    description: outputs are not inputs"
  ].join("\n"),
  "fixture"
);

assert.equal(declared.get("plain"), true, "required with no default must be demanded");
assert.equal(
  declared.get("defaulted-after"),
  false,
  "a default satisfies the input, declared after"
);
assert.equal(
  declared.get("defaulted-before"),
  false,
  "a default satisfies the input, declared before"
);
assert.equal(declared.get("optional"), false, "required: false must not be demanded");
assert.equal(
  declared.get("folded"),
  false,
  "a folded description mentioning required: must not count"
);
assert.equal(declared.has("ignored"), false, "outputs must not be read as inputs");
passed += 6;

assert.equal(
  parseActionInputs("name: Example\nruns:\n  using: node24\n", "fixture").size,
  0,
  "an action with no inputs declares none"
);
passed += 1;

console.log(`[test-build-lock-action-input-parser] ${passed} assertions passed.`);
