"use strict";

// Every `out` parameter must be assigned immediately before a return.
//
// The rule is the owner's, and the reason is that the compiler only proves an `out` is assigned on
// every path -- it says nothing about assigning the RIGHT value. The common shape it rejects is a
// defensive `value = default;` at the top of a method: definite assignment is satisfied forever
// after, so a later path that forgets to set the real value compiles cleanly and silently returns
// the default. Assigning immediately before each return puts the value and the exit next to each
// other, where a missing one is visible.
//
// This checks the property rather than the shape: for each assignment to an out parameter, the next
// meaningful statement must be a `return`. That admits every correct spelling (including a single
// assignment in a two-line method) and rejects only the separation the rule is about.
//
// Deliberately conservative. It scans method bodies with a brace matcher rather than a real parser,
// so anything it cannot confidently attribute is skipped instead of guessed at -- a false accusation
// here would be worse than a miss, because it would train the reader to ignore it.

const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");

// Files that predate the rule, listed individually so the check is a RATCHET: a violation in any
// file not named here fails immediately, while the backlog is worked down without blocking. Shrink
// this list; never grow it. Tracked as an issue with the full inventory.
//
// A per-file baseline rather than a count, because a count lets one fix pay for one new violation
// and stay green. It is also not per-line: line numbers move on every unrelated edit, which would
// make the baseline noise rather than signal.
const BASELINE = new Set(require("./out-parameter-baseline.json"));

function sourceFiles(directory) {
  const found = [];
  if (!fs.existsSync(directory)) {
    return found;
  }
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "bin" || entry.name === "obj") {
        continue;
      }
      found.push(...sourceFiles(full));
    } else if (entry.name.endsWith(".cs")) {
      found.push(full);
    }
  }
  return found;
}

function bodyOf(text, openBraceIndex) {
  let depth = 0;
  for (let index = openBraceIndex; index < text.length; index += 1) {
    if (text[index] === "{") {
      depth += 1;
    } else if (text[index] === "}") {
      depth -= 1;
      if (depth === 0) {
        return text.slice(openBraceIndex + 1, index);
      }
    }
  }
  return null;
}

const violations = [];

for (const file of sourceFiles(path.join(root, "Runtime")).concat(
  sourceFiles(path.join(root, "Editor"))
)) {
  const relative = path.relative(root, file).split(path.sep).join("/");
  const text = fs.readFileSync(file, "utf8");
  const signature =
    /\b(?:public|private|internal|protected)[^\n;(]*\(([^)]*)\)\s*(?:where[^\n{]*)?\s*\{/g;

  let match;
  while ((match = signature.exec(text)) !== null) {
    const outNames = [...match[1].matchAll(/\bout\s+[\w<>[\],.?\s]*?(\w+)\s*(?:,|$)/g)].map(
      (m) => m[1]
    );
    if (outNames.length === 0) {
      continue;
    }

    const openBrace = text.lastIndexOf("{", signature.lastIndex);
    const body = bodyOf(text, openBrace);
    if (body === null) {
      continue;
    }

    const lines = body.split("\n");
    for (const name of outNames) {
      const assignment = new RegExp(`^\\s*${name}\\s*=[^=]`);
      for (let index = 0; index < lines.length; index += 1) {
        if (!assignment.test(lines[index])) {
          continue;
        }

        // The next meaningful line must return. Blank lines and comments are skipped, and so are
        // assignments to the method's OTHER out parameters: a method with two of them writes both
        // and then returns, which satisfies the rule jointly even though only the last assignment
        // literally touches the return.
        const siblingAssignment = new RegExp(`^\\s*(?:${outNames.join("|")})\\s*=[^=]`);

        // An assignment may span lines. Walk to the one that actually ends the statement before
        // asking what follows it, or a wrapped expression reads as "followed by its own second
        // line" and is reported forever.
        let index2 = index;
        while (index2 < lines.length && !lines[index2].trimEnd().endsWith(";")) {
          index2 += 1;
        }
        let next = index2 + 1;
        while (
          next < lines.length &&
          (lines[next].trim() === "" ||
            lines[next].trim().startsWith("//") ||
            lines[next].trim().startsWith("/*") ||
            lines[next].trim().startsWith("*") ||
            siblingAssignment.test(lines[next]))
        ) {
          next += 1;
        }

        const following = next < lines.length ? lines[next].trim() : "";
        if (following.startsWith("return")) {
          continue;
        }

        const line = text.slice(0, openBrace).split("\n").length + index + 1;
        violations.push({
          file: relative,
          line,
          name,
          statement: lines[index].trim().slice(0, 70),
          following: following.slice(0, 50)
        });
      }
    }
  }
}

const unswept = violations.filter((v) => BASELINE.has(v.file));
const swept = violations.filter((v) => !BASELINE.has(v.file));

if (swept.length > 0) {
  console.error(
    `out-parameter discipline: ${swept.length} assignment(s) not immediately before a return.\n` +
      "Assign an out parameter exactly once, on the line before each return, so the value and the\n" +
      "exit are visible together.\n"
  );
  for (const v of swept) {
    console.error(`  ${v.file}:${v.line}  ${v.statement}   (followed by: ${v.following})`);
  }
  process.exit(1);
}

// A file that was cleaned should leave the baseline, or it silently stops being protected.
const stale = [...BASELINE].filter((file) => !violations.some((v) => v.file === file));
if (stale.length > 0) {
  console.error(
    "These files are listed in out-parameter-baseline.json but no longer violate the rule.\n" +
      "Remove them from the baseline so they stay protected:\n"
  );
  for (const file of stale) {
    console.error(`  ${file}`);
  }
  process.exit(1);
}

console.log(
  `out-parameter discipline passed (${unswept.length} known violations across ` +
    `${BASELINE.size} baselined file(s); 0 outside the baseline).`
);
