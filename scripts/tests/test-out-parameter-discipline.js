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
//
// Because it is conservative, it also has the most to lose from a silent scope collapse: under-
// reporting is its designed behaviour, so a root that stopped existing looks exactly like the
// conservatism working. Every run therefore reports its SUBJECTS per root, refuses a root that
// yielded no files, and names one method per root that must be among the ones it examined
// (#683).

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

// The trees this rule covers. Named per root rather than concatenated, because one total stays
// healthy while one root drops out entirely -- the half a reviewer had to catch on #665.
//
// `anchor` is the strong form of the honest-gates control: a count only proves SOMETHING was seen,
// while a named subject cannot be satisfied by accident. Both anchors have existed since the v3.0.0
// release, so a failure here is a rename to record, not flakiness -- update the anchor to another
// long-lived `Try*` in the same tree.
const ROOTS = [
  {
    name: "Runtime",
    anchorFile: "Runtime/Core/DataStructure/Adapters/SerializableDictionary.cs",
    anchorMethod: "TryGetValue"
  },
  {
    name: "Editor",
    anchorFile: "Editor/CustomDrawers/SerializableDictionaryPropertyDrawer.cs",
    anchorMethod: "TryResolveKeyValueTypes"
  }
];

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

function scanRoot(rootName) {
  const result = {
    name: rootName,
    files: 0,
    methods: 0,
    outParameters: 0,
    anchorHits: 0,
    unreadable: [],
    violations: []
  };

  for (const file of sourceFiles(path.join(root, rootName))) {
    const relative = path.relative(root, file).split(path.sep).join("/");

    // An unreadable file leaving the scan without a trace is the same vacuum by another route, so
    // it is reported beside the findings rather than skipped silently.
    let text;
    try {
      text = fs.readFileSync(file, "utf8");
    } catch (error) {
      result.unreadable.push(`${relative}: ${error.message}`);
      continue;
    }
    result.files += 1;

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

      result.methods += 1;
      result.outParameters += outNames.length;

      const declaredName = (match[0].match(/(\w+)\s*\(/) || [])[1];
      const anchor = ROOTS.find((candidate) => candidate.name === rootName);
      if (anchor && relative === anchor.anchorFile && declaredName === anchor.anchorMethod) {
        result.anchorHits += 1;
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

          // Falling off the end of the body IS the exit, and for a `void` method carrying an `out`
          // it is the only one there is. Requiring a literal `return;` there would be code shaped
          // around this checker rather than for a reader, and it cannot hide a top-of-method
          // default: the last statement in a body is the furthest thing from the top.
          if (next >= lines.length) {
            continue;
          }

          const following = lines[next].trim();
          if (following.startsWith("return")) {
            continue;
          }

          const line = text.slice(0, openBrace).split("\n").length + index + 1;
          result.violations.push({
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

  return result;
}

const scans = ROOTS.map((entry) => scanRoot(entry.name));

// The negative control, run in process on every invocation rather than described in a comment: a
// root that does not exist must be REJECTED, not concatenated as an empty list. Renaming `Runtime/`
// or `Editor/` used to print the identical success line this gate prints when it is healthy.
const controlName = "__out-parameter-gate-control-root__";
const control = scanRoot(controlName);
if (control.files !== 0) {
  console.error(
    `out-parameter discipline: the negative control root ${controlName} exists on disk, so the\n` +
      "scope-collapse control proves nothing. Remove it."
  );
  process.exit(1);
}

const emptyRoots = scans.filter((scan) => scan.files === 0);
if (0 < emptyRoots.length) {
  console.error(
    "out-parameter discipline: a scanned root yielded no C# files, so this run examined nothing\n" +
      "there. That is a scope collapse (a rename, a restructure, or an unexpected repository root),\n" +
      "not a clean result.\n"
  );
  for (const scan of emptyRoots) {
    console.error(`  ${scan.name}/  ->  0 files under ${path.join(root, scan.name)}`);
  }
  process.exit(1);
}

const unreadable = scans.flatMap((scan) => scan.unreadable);
if (0 < unreadable.length) {
  console.error(
    "out-parameter discipline: files could not be read, so they are a hole in this measurement\n" +
      "rather than a clean result.\n"
  );
  for (const entry of unreadable) {
    console.error(`  ${entry}`);
  }
  process.exit(1);
}

const missingAnchors = scans.filter((scan) => scan.anchorHits === 0);
if (0 < missingAnchors.length) {
  console.error(
    "out-parameter discipline: a named subject was not among the methods examined. The scan\n" +
      "reached the root but not the method, so its scope is narrower than it claims.\n"
  );
  for (const scan of missingAnchors) {
    const entry = ROOTS.find((candidate) => candidate.name === scan.name);
    console.error(`  ${scan.name}/  expected ${entry.anchorFile}::${entry.anchorMethod}`);
  }
  console.error(
    "\nIf the method was legitimately renamed, point the anchor at another long-lived `Try*`\n" +
      "method with an `out` parameter in the same tree."
  );
  process.exit(1);
}

const violations = scans.flatMap((scan) => scan.violations);
const unswept = violations.filter((v) => BASELINE.has(v.file));
const swept = violations.filter((v) => !BASELINE.has(v.file));

if (0 < swept.length) {
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
if (0 < stale.length) {
  console.error(
    "These files are listed in out-parameter-baseline.json but no longer violate the rule.\n" +
      "Remove them from the baseline so they stay protected:\n"
  );
  for (const file of stale) {
    console.error(`  ${file}`);
  }
  process.exit(1);
}

for (const scan of scans) {
  console.log(
    `out-parameter discipline examined ${scan.name}/: ${scan.files} file(s), ` +
      `${scan.methods} method(s) with an out parameter, ${scan.outParameters} out parameter(s).`
  );
}
console.log(
  `out-parameter discipline passed (${unswept.length} known violations across ` +
    `${BASELINE.size} baselined file(s); 0 outside the baseline).`
);
