"use strict";

// Critical rule 16, enforced across the CALL GRAPH rather than against one method body.
//
// An AssetPostprocessor callback must not reach a heavy AssetDatabase / component / reflection API
// while Unity is importing: doing so deserializes assets mid-import, which runs consumer OnValidate
// code and produces "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate".
// The canonical way to satisfy it is AssetPostprocessorDeferral.Schedule, which runs the work on the
// next editor tick.
//
// The EditMode contract that used to own this scanned the text of the callback's own body, so one
// helper hop hid a violation completely -- and one was hiding: DetectAssetChangeProcessor's
// OnPostprocessAllAssets reaches AssetDatabase.LoadAssetAtPath five frames down through
// EnsureInitialized, and the contract passed (#439, found while investigating #280). A rule that is
// documented AND machine-checked is worse than one that is only documented when the check has a hole
// the rule's own wording does not, because the next person to add a helper reasonably believes they
// are covered.
//
// Three deliberate bounds, each stated rather than discovered later:
//
// 1. **Same declaring type only.** Following a call into another type needs a symbol table, which is
//    a Roslyn analyzer's job. Every violation this package has actually had is a private helper
//    beside the callback, and stopping at the type boundary is what keeps this a fifty-millisecond
//    script instead of a compiler.
// 2. **Deferred bodies are stripped first**, so a forbidden call inside
//    AssetPostprocessorDeferral.Schedule(...) is fine -- that is the remedy, not the violation --
//    and a helper called only from inside one is not walked either.
// 3. **A name is a name.** Overloads share a body set here, so a violation in any overload of a
//    reached name is reported. That over-reports rather than under-reports, which is the correct
//    direction for a guard.
// 4. **The walk stops at a NAMED deferral boundary**, and at nothing else. DEFERRAL_BOUNDARIES
//    below lists the methods whose job is to move work off the import stack; the walk scans their
//    own bodies and does not follow their callees.
//
//    Deriving that property instead -- "any method that calls Schedule is a boundary" -- was tried
//    first and is wrong, provably: a callback that both schedules its real work AND calls a helper
//    is then never walked at all. A deliberately planted three-hop violation survived that version,
//    which is the only reason this list is explicit.
//
//    The list is not an escape hatch. Every entry is asserted to actually contain a
//    Schedule call, so a boundary that stops deferring fails the contract instead of quietly
//    continuing to hide everything beneath it.
//
//    The cost is flow-insensitivity, stated rather than hidden: EnqueueAssetChanges schedules when
//    `deferProcessing` is true and runs the drain inline when it is false, and no textual walk can
//    know which argument the callback passed. It passes true -- that is the whole point of the
//    parameter -- and the inline branch belongs to callers that are not in the import phase.
// 5. **Methods, not properties.** A property whose getter reaches a forbidden API is invisible here.
//    Taking property bodies too means matching members with no parameter list, and the same relaxed
//    pattern then matches a nested type declaration -- whose whole body would be attributed to a
//    `new Foo(` call, turning one constructor into a hundred false reachable lines. The hole is
//    stated rather than closed badly; the shape that has actually bitten this package is a private
//    helper method, twice.

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "../..");

// The canonical Unity callbacks that run inside the import phase. Static or instance; Unity finds
// them by name, so this list is the contract's whole notion of "an import-phase entry point".
const CALLBACK_NAMES = new Set([
  "OnPostprocessAllAssets",
  "OnPreprocessAsset",
  "OnPreprocessTexture",
  "OnPostprocessTexture",
  "OnPostprocessCubemap",
  "OnPreprocessModel",
  "OnPostprocessModel",
  "OnPreprocessAudio",
  "OnPostprocessAudio",
  "OnPreprocessSpeedTree",
  "OnPostprocessSpeedTree",
  "OnPreprocessAnimation",
  "OnPostprocessAnimation",
  "OnPreprocessMaterialDescription",
  "OnPreprocessCameraDescription",
  "OnPreprocessLightDescription",
  "OnPostprocessPrefab",
  "OnPostprocessMeshHierarchy",
  "OnPostprocessMaterial",
  "OnPostprocessGameObjectWithUserProperties",
  "OnPostprocessSprites",
  "OnPostprocessAssetbundleNameChanged"
]);

// Substrings that must not be reachable. The generic and non-generic spellings are both listed
// because the non-generic overload is equally heavy during import, and listing only the generic one
// would let `GetComponents(typeof(T), buffer)` through.
const FORBIDDEN_TOKENS = [
  "AssetDatabase.LoadAssetAtPath",
  "AssetDatabase.LoadAllAssetsAtPath",
  "AssetDatabase.LoadMainAssetAtPath",
  "AssetDatabase.LoadAllAssetRepresentationsAtPath",
  ".GetComponentsInChildren<",
  ".GetComponentsInChildren(",
  ".GetComponents<",
  ".GetComponents(",
  ".AddComponent<",
  ".AddComponent(",
  "Object.Instantiate",
  "GameObject.Instantiate",
  "UnityEngine.Object.Instantiate",
  "DestroyImmediate",
  "MethodInfo.Invoke"
];

const DEFERRAL_CALL = "AssetPostprocessorDeferral.Schedule(";

// The methods that ARE the seam between the import phase and the next editor tick. Each is asserted
// below to contain a Schedule call, so this cannot become a place to park a violation.
const DEFERRAL_BOUNDARIES = new Map([
  [
    "EnqueueAssetChanges",
    "schedules DrainPendingChangesAction; the callback passes deferProcessing: true"
  ]
]);

function sourceFiles(directory) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      found.push(...sourceFiles(full));
    } else if (entry.name.endsWith(".cs")) {
      found.push(full);
    }
  }
  return found;
}

// Comments go before anything is matched, so prose describing a forbidden API does not fire the
// contract. Only whole-line `//` comments and block comments are removed: a `//` mid-line can sit
// inside a string literal, and truncating there would delete real code.
function stripComments(text) {
  const withoutBlocks = text.replace(/\/\*[\s\S]*?\*\//g, "");
  return withoutBlocks
    .split("\n")
    .map((line) => (line.trim().startsWith("//") ? "" : line))
    .join("\n");
}

// The span from `open` (an index pointing at `{`) to its matching close, exclusive of both braces.
// Brace counting rather than a parser: string and character literals are skipped so that a `{` in a
// format string or a `'}'` literal cannot unbalance the count.
function blockAt(text, open) {
  let depth = 0;
  for (let index = open; index < text.length; index++) {
    const character = text[index];
    if (character === '"') {
      index = skipString(text, index);
      continue;
    }
    if (character === "'") {
      index = skipChar(text, index);
      continue;
    }
    if (character === "{") {
      depth++;
    } else if (character === "}") {
      depth--;
      if (depth === 0) {
        return { body: text.slice(open + 1, index), end: index };
      }
    }
  }
  return null;
}

function skipString(text, start) {
  const verbatim = 0 < start && text[start - 1] === "@";
  for (let index = start + 1; index < text.length; index++) {
    if (!verbatim && text[index] === "\\") {
      index++;
      continue;
    }
    if (text[index] === '"') {
      if (verbatim && text[index + 1] === '"') {
        index++;
        continue;
      }
      return index;
    }
  }
  return text.length;
}

function skipChar(text, start) {
  for (let index = start + 1; index < text.length; index++) {
    if (text[index] === "\\") {
      index++;
      continue;
    }
    if (text[index] === "'") {
      return index;
    }
  }
  return text.length;
}

// Every method declared in the file, by name, with each overload's body. Matched on the declaration
// shape rather than on a name list, so a helper added tomorrow is walked without editing this file.
// A `(` on the line and a `{` after the closing `)` is what separates a method from a property, a
// field initializer or a `using` directive; expression-bodied members carry no `{`, and their whole
// body is one expression, which is captured too.
function methodBodies(text) {
  const bodies = new Map();

  // Matched per LINE rather than over the whole file. The same pattern applied to the file at once
  // is a nested quantifier over megabytes and backtracks for minutes on the first non-match; a line
  // bounds it to a hundred characters, where the same shape is instant.
  const declaration =
    /^[ \t]*(?:(?:public|private|protected|internal|static|sealed|override|virtual|abstract|extern|unsafe|async|partial|new|readonly)[ \t]+)+[^;=]*?\b([A-Za-z_]\w*)[ \t]*(?:<[^()<>]*>)?[ \t]*\(/;

  let offset = 0;
  for (const line of text.split("\n")) {
    const lineStart = offset;
    offset += line.length + 1;

    const match = declaration.exec(line);
    if (match === null) {
      continue;
    }

    const name = match[1];
    const parenthesis = lineStart + match[0].length - 1;
    const close = matchingParenthesis(text, parenthesis);
    if (close < 0) {
      continue;
    }

    let cursor = close + 1;
    while (cursor < text.length && /\s/.test(text[cursor])) {
      cursor++;
    }

    // A generic constraint sits between the parameter list and the body.
    if (text.startsWith("where", cursor)) {
      const brace = text.indexOf("{", cursor);
      if (brace < 0) {
        continue;
      }
      cursor = brace;
    }

    let body = null;
    if (text[cursor] === "{") {
      const block = blockAt(text, cursor);
      if (block != null) {
        body = block.body;
      }
    } else if (text.startsWith("=>", cursor)) {
      const semicolon = text.indexOf(";", cursor);
      body = 0 <= semicolon ? text.slice(cursor + 2, semicolon) : null;
    }

    if (body == null) {
      continue;
    }

    if (!bodies.has(name)) {
      bodies.set(name, []);
    }
    bodies.get(name).push(body);
  }

  return bodies;
}

function matchingParenthesis(text, open) {
  let depth = 0;
  for (let index = open; index < text.length; index++) {
    const character = text[index];
    if (character === '"') {
      index = skipString(text, index);
      continue;
    }
    if (character === "'") {
      index = skipChar(text, index);
      continue;
    }
    if (character === "(") {
      depth++;
    } else if (character === ")") {
      depth--;
      if (depth === 0) {
        return index;
      }
    }
  }
  return -1;
}

// Removes each `AssetPostprocessorDeferral.Schedule(...)` argument list. What is inside one runs
// after the import phase, so it is not merely allowed there -- it is the fix, and a walk that
// followed into it would report the remedy as the defect.
function stripDeferredWork(body) {
  let result = body;
  for (;;) {
    const start = result.indexOf(DEFERRAL_CALL);
    if (start < 0) {
      return result;
    }

    const open = start + DEFERRAL_CALL.length - 1;
    const close = matchingParenthesis(result, open);
    if (close < 0) {
      return result.slice(0, start);
    }

    // The WHOLE call goes, name included. Leaving `Schedule()` behind makes the next `indexOf` find
    // the same call again and the loop never terminates -- which it did, silently, until a run that
    // never finished said so.
    result = result.slice(0, start) + result.slice(close + 1);
  }
}

function forbiddenIn(body) {
  const fired = [];
  for (const token of FORBIDDEN_TOKENS) {
    if (body.includes(token)) {
      fired.push(token);
    }
  }

  if (/(^|[^\w.])Instantiate\s*\(/.test(body)) {
    fired.push("Instantiate(");
  }

  return fired;
}

// The names this body calls that are declared in the same type. `this.` and `base.` count; any other
// qualifier means the call left the type and is out of scope by bound 1.
//
// One pass over the body collecting every `identifier(` rather than one regex per known name: the
// latter is O(names x bodies) compiled patterns, which for a file with a hundred methods is tens of
// thousands of regex builds per walk.
function calledNames(body, known) {
  const called = new Set();
  const invocation = /(^|[^\w.]|\bthis\.|\bbase\.)([A-Za-z_]\w*)[ \t]*(?:<[^()<>;\n]*>)?[ \t]*\(/g;
  let match;
  while ((match = invocation.exec(body)) !== null) {
    if (known.has(match[2])) {
      called.add(match[2]);
    }
  }
  return called;
}

const failures = [];
let scannedCallbacks = 0;
let scannedProcessors = 0;

for (const file of sourceFiles(path.join(root, "Editor"))) {
  const text = stripComments(fs.readFileSync(file, "utf8"));
  if (!/\bclass\s+\w+\s*:[^{]*\bAssetPostprocessor\b/.test(text)) {
    continue;
  }

  scannedProcessors++;
  const bodies = methodBodies(text);
  const names = new Set(bodies.keys());

  for (const callback of names) {
    if (!CALLBACK_NAMES.has(callback)) {
      continue;
    }

    scannedCallbacks++;

    // Breadth-first so the reported path is the shortest one to the violation, which is the one a
    // reader has to follow. The visited set is what makes a recursive helper terminate.
    const visited = new Set([callback]);
    const queue = [[callback]];
    while (0 < queue.length) {
      const trail = queue.shift();
      const current = trail[trail.length - 1];
      for (const rawBody of bodies.get(current) ?? []) {
        const body = stripDeferredWork(rawBody);
        const fired = forbiddenIn(body);
        if (0 < fired.length) {
          failures.push(
            `${path.relative(root, file)}: ${trail.join(" -> ")} reaches ${fired.join(", ")}. ` +
              "Route the call through AssetPostprocessorDeferral.Schedule(...) so it runs outside " +
              "Unity's asset-import phase."
          );
        }

        // Bound 4: this method is where the work leaves the import stack, so what it calls next
        // runs on a later tick. Its own body was scanned above; nothing under it is reachable
        // during the import.
        if (DEFERRAL_BOUNDARIES.has(current)) {
          if (!rawBody.includes(DEFERRAL_CALL)) {
            failures.push(
              `${path.relative(root, file)}: ${current} is listed as a deferral boundary but no ` +
                "longer calls AssetPostprocessorDeferral.Schedule. Either restore the deferral or " +
                "remove it from DEFERRAL_BOUNDARIES so everything it reaches is walked again."
            );
          }
          continue;
        }

        for (const next of calledNames(body, names)) {
          if (visited.has(next)) {
            continue;
          }
          visited.add(next);
          queue.push([...trail, next]);
        }
      }
    }
  }
}

assert.ok(
  0 < scannedProcessors,
  "found no AssetPostprocessor subclass under Editor/ -- the scan's discovery is broken, not the code"
);
assert.ok(
  0 < scannedCallbacks,
  "found no import-phase callback to walk -- the scan's discovery is broken, not the code"
);
assert.deepEqual(
  failures,
  [],
  `AssetPostprocessor callbacks reach forbidden synchronous APIs:\n\n${failures.join("\n\n")}`
);

console.log(
  `AssetPostprocessor reachability: ${scannedCallbacks} callback(s) across ${scannedProcessors} processor(s), no forbidden API reachable.`
);
