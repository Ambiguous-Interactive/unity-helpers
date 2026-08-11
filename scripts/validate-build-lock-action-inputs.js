"use strict";

// Validates every pinned build-lock action call against the input contract that action DECLARES at
// the exact commit it is pinned to.
//
// Dependabot bumps a `uses:` SHA. It cannot bump the caller's `with:` block, and no check in this
// repository read the two together, so a bump that adds a `required: true` input merged green and
// then failed every licensed leg (#356, bf05d620). The failure was also unreadable: the central
// classifier writes its fail-closed defaults to GITHUB_OUTPUT before reading anything, so a missing
// input surfaced as `cleanup-reason=return-log-truncated` -- a real reason code, naming a cause that
// had not happened. This gate reads the contract instead of the symptom.
//
// It needs no Unity, no license and no matrix, which is the point: it runs on the Dependabot pull
// request that proposes the bump, where every licensed leg is skipped for want of secrets.

const childProcess = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

const ACTION_PATH_PREFIX =
  "Ambiguous-Interactive/ambiguous-organization-build-lock/.github/actions/";

/**
 * @param {string} line Raw line.
 * @returns {boolean} True when the line carries no YAML content.
 */
function isBlankOrComment(line) {
  const trimmed = line.trim();
  return trimmed === "" || trimmed.startsWith("#");
}

/**
 * Collects the `with:` keys a step supplies, starting from the step's `uses:` line.
 *
 * Only lines at exactly the mapping's key column count as keys, so a block scalar's content and a
 * nested mapping cannot be mistaken for one. Scanning stops at the first line indented less than
 * the step's own keys, which is what ends a step in workflow and composite-action YAML alike.
 *
 * @param {string[]} lines All lines of the file.
 * @param {number} usesLineIndex Index of the `uses:` line.
 * @param {number} stepColumn Column the step's keys share.
 * @returns {string[]} Supplied input names, in file order.
 */
function collectWithKeys(lines, usesLineIndex, stepColumn) {
  let withColumn = -1;
  let keyColumnInWith = -1;
  const supplied = [];

  for (let index = usesLineIndex + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (isBlankOrComment(line)) {
      continue;
    }
    const indent = line.length - line.trimStart().length;

    if (withColumn === -1) {
      if (indent < stepColumn) {
        return supplied;
      }
      if (indent === stepColumn && /^with:\s*$/.test(line.trim())) {
        withColumn = indent;
      }
      continue;
    }

    if (indent <= withColumn) {
      return supplied;
    }
    if (keyColumnInWith === -1) {
      keyColumnInWith = indent;
    }
    if (indent !== keyColumnInWith) {
      continue;
    }
    const match = /^([A-Za-z0-9_][A-Za-z0-9_-]*):(\s|$)/.exec(line.trim());
    if (match) {
      supplied.push(match[1]);
    }
  }
  return supplied;
}

/**
 * Finds every pinned build-lock action call in a file.
 *
 * @param {string} filePath Absolute path.
 * @param {string} relativePath Repository-relative path, for messages.
 * @returns {Array<{action: string, ref: string, file: string, line: number, supplied: string[]}>} Calls.
 */
function collectCalls(filePath, relativePath) {
  const lines = fs.readFileSync(filePath, "utf8").split(/\r?\n/);
  const calls = [];

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    if (isBlankOrComment(line)) {
      continue;
    }
    // Two-stage on purpose. The loose test decides WHETHER this line is a call; the strict one
    // decides what the call is. A line that is loosely a build-lock `uses:` but does not parse
    // strictly raises rather than being skipped, because a scanner that quietly drops the shapes it
    // does not recognize under-reports and still exits zero -- the first draft of this file missed
    // 15 of 31 calls that way, every one of them carrying a trailing `# v1.9.1` comment.
    if (!/^[ \t]*(?:-[ \t]*)?uses:/.test(line) || !line.includes(ACTION_PATH_PREFIX)) {
      continue;
    }
    const match = /^[ \t]*(?:-[ \t]*)?uses:[ \t]*(\S+)[ \t]*(?:#.*)?$/.exec(line);
    if (!match || !match[1].startsWith(ACTION_PATH_PREFIX)) {
      throw new Error(
        `${relativePath}:${index + 1}: build-lock action reference could not be parsed: ${line.trim()}`
      );
    }
    const reference = match[1];
    const [actionPath, ref] = reference.slice(ACTION_PATH_PREFIX.length).split("@");
    if (!actionPath || !ref) {
      throw new Error(
        `${relativePath}:${index + 1}: build-lock action reference is not pinned to a commit: ${reference}`
      );
    }
    calls.push({
      action: actionPath,
      ref,
      file: relativePath,
      line: index + 1,
      // The column the key itself starts at, which is the level its sibling keys share -- so
      // `- uses: x` and a `uses:` on its own line inside the same step both resolve correctly.
      supplied: collectWithKeys(lines, index, line.indexOf("uses:"))
    });
  }
  return calls;
}

/**
 * Parses the `inputs:` mapping of an action definition.
 *
 * Attribute lines are read only at the input block's own attribute column, so a folded
 * `description:` whose text happens to contain `required:` cannot change the answer.
 *
 * An input carrying a `default:` is reported as not-required however it declares `required:`. The
 * runner substitutes the default when the caller omits it, so demanding one would fail a call that
 * works -- and a false failure here reds the Dependabot pull request that proposes a perfectly good
 * bump. No build-lock action declares both today; this keeps that from becoming a trap.
 *
 * @param {string} content Contents of an `action.yml`.
 * @param {string} label Identifier for error messages.
 * @returns {Map<string, boolean>} Input name to whether the caller must supply it.
 */
function parseActionInputs(content, label) {
  const lines = content.split(/\r?\n/);
  const inputs = new Map();
  const defaulted = new Set();

  let inputsIndex = -1;
  for (let index = 0; index < lines.length; index += 1) {
    if (/^inputs:\s*$/.test(lines[index])) {
      inputsIndex = index;
      break;
    }
  }
  if (inputsIndex === -1) {
    return inputs;
  }

  let nameColumn = -1;
  let current = "";
  let attributeColumn = -1;

  for (let index = inputsIndex + 1; index < lines.length; index += 1) {
    const line = lines[index];
    if (isBlankOrComment(line)) {
      continue;
    }
    const indent = line.length - line.trimStart().length;
    if (indent === 0) {
      break;
    }
    if (nameColumn === -1) {
      nameColumn = indent;
    }
    if (indent === nameColumn) {
      const match = /^([A-Za-z0-9_][A-Za-z0-9_-]*):(\s|$)/.exec(line.trim());
      if (!match) {
        throw new Error(`${label}: unparsable input declaration on line ${index + 1}`);
      }
      current = match[1];
      attributeColumn = -1;
      inputs.set(current, false);
      continue;
    }
    if (!current) {
      continue;
    }
    if (attributeColumn === -1) {
      attributeColumn = indent;
    }
    if (indent !== attributeColumn) {
      continue;
    }
    if (/^default:/.test(line.trim())) {
      defaulted.add(current);
      continue;
    }
    const required = /^required:\s*(\S+)\s*$/.exec(line.trim());
    if (required) {
      inputs.set(current, required[1].replace(/^["']|["']$/g, "") === "true");
    }
  }

  // Applied after the walk, because `default:` may be declared either side of `required:`.
  for (const name of defaulted) {
    inputs.set(name, false);
  }
  return inputs;
}

/**
 * Reads an action definition at an exact commit of the central policy checkout.
 *
 * @param {string} policyRoot Checkout of the central build-lock repository.
 * @param {string} action Action directory name.
 * @param {string} ref Pinned commit.
 * @returns {string} Contents of the action definition.
 */
function readActionAtRef(policyRoot, action, ref) {
  const target = `${ref}:.github/actions/${action}/action.yml`;
  try {
    return childProcess.execFileSync("git", ["-C", policyRoot, "show", target], {
      encoding: "utf8",
      maxBuffer: 8 * 1024 * 1024
    });
  } catch (error) {
    throw new Error(
      `Could not read ${target} from the central policy checkout at ${policyRoot}. ` +
        "The checkout needs full history (fetch-depth: 0) because different actions can be " +
        `pinned to different commits. Underlying error: ${error.message}`
    );
  }
}

/**
 * @param {string} directory Directory to walk.
 * @returns {string[]} Sorted YAML file paths.
 */
function collectYamlFiles(directory) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      found.push(...collectYamlFiles(entryPath));
    } else if (entry.name.endsWith(".yml") || entry.name.endsWith(".yaml")) {
      found.push(entryPath);
    }
  }
  return found.sort();
}

/**
 * Runs the contract.
 *
 * @param {object} options Options.
 * @param {string} options.root Repository root to scan.
 * @param {string} options.policyRoot Checkout of the central build-lock repository.
 * @returns {{callCount: number, fileCount: number, versionCount: number, failures: string[]}} Result.
 */
function validateBuildLockActionInputs({ root, policyRoot }) {
  const githubRoot = path.join(root, ".github");
  const calls = [];
  const files = new Set();

  for (const filePath of collectYamlFiles(githubRoot)) {
    const relativePath = path.relative(root, filePath).split(path.sep).join("/");
    const found = collectCalls(filePath, relativePath);
    if (found.length > 0) {
      files.add(relativePath);
      calls.push(...found);
    }
  }

  const declarationCache = new Map();
  const failures = [];

  for (const call of calls) {
    const cacheKey = `${call.action}@${call.ref}`;
    if (!declarationCache.has(cacheKey)) {
      declarationCache.set(
        cacheKey,
        parseActionInputs(readActionAtRef(policyRoot, call.action, call.ref), cacheKey)
      );
    }
    const declared = declarationCache.get(cacheKey);
    const supplied = new Set(call.supplied);

    for (const [name, required] of declared) {
      if (required && !supplied.has(name)) {
        failures.push(
          `${call.file}:${call.line}: ${call.action}@${call.ref.slice(0, 8)} requires input ` +
            `'${name}', which this call does not pass.`
        );
      }
    }
    for (const name of supplied) {
      if (!declared.has(name)) {
        failures.push(
          `${call.file}:${call.line}: ${call.action}@${call.ref.slice(0, 8)} declares no input ` +
            `'${name}', which this call passes.`
        );
      }
    }
  }

  return {
    callCount: calls.length,
    fileCount: files.size,
    versionCount: declarationCache.size,
    failures
  };
}

module.exports = {
  collectCalls,
  collectWithKeys,
  parseActionInputs,
  validateBuildLockActionInputs
};

if (require.main === module) {
  const root = path.resolve(__dirname, "..");
  const configuredPolicyRoot = process.env.BUILD_LOCK_POLICY_ROOT || "";

  // Same rule as test-portable-cleanup-classifier.js: a developer machine has no central policy
  // checkout and `npm run validate:prepush` has to stay runnable there, but under Actions the
  // checkout's absence means the CI wiring broke. Skipping there would drop the contract silently,
  // which is exactly the shape of failure this gate exists to end.
  if (!configuredPolicyRoot) {
    if (process.env.GITHUB_ACTIONS === "true") {
      console.error(
        "BUILD_LOCK_POLICY_ROOT is unset under GitHub Actions. The central policy checkout step " +
          "must run before this gate; skipping here would drop the input contract silently."
      );
      process.exit(1);
    }
    console.log(
      "[validate-build-lock-action-inputs] SKIPPED: set BUILD_LOCK_POLICY_ROOT to a full-history " +
        "checkout of ambiguous-organization-build-lock to run the pinned input contract."
    );
    process.exit(0);
  }

  let result;
  try {
    result = validateBuildLockActionInputs({
      root,
      policyRoot: path.resolve(configuredPolicyRoot)
    });
  } catch (error) {
    console.error(`[validate-build-lock-action-inputs] ${error.message}`);
    process.exit(1);
  }

  // A gate that checked nothing once reported success in this repository; the count is the
  // assertion, not the exit code. Every workflow here calls at least one build-lock action, so
  // zero calls means the scan broke rather than that the calls went away.
  if (result.callCount === 0) {
    console.error(
      "[validate-build-lock-action-inputs] Found 0 pinned build-lock action calls under .github/. " +
        "This repository has many, so the scan is broken rather than the calls being absent."
    );
    process.exit(1);
  }

  for (const failure of result.failures) {
    console.error(`::error::${failure}`);
  }
  if (result.failures.length > 0) {
    console.error(
      `[validate-build-lock-action-inputs] ${result.failures.length} input contract violation(s). ` +
        "A pinned action's `with:` block must satisfy the inputs that action declares at its pin."
    );
    process.exit(1);
  }

  console.log(
    `[validate-build-lock-action-inputs] OK: ${result.callCount} call(s) across ` +
      `${result.fileCount} file(s) match ${result.versionCount} pinned action version(s).`
  );
}
