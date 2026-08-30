#!/usr/bin/env node
/**
 * Extracts the documentation samples a compiler can judge, so a compiler judges them.
 *
 * `lint:code-samples` pulls 3,061 C# blocks out of `docs/` and validates none of them
 * ([#611](https://github.com/Ambiguous-Interactive/unity-helpers/issues/611)), and
 * `lint-doc-identifiers.js` checks the two things a regex can check without lying: a namespace in a
 * `using`, an assembly name in a `link.xml`. Neither can see a MEMBER that moved, which was the
 * shape of every defect session 236 found by hand, including `AnalyzerRunner.RunAndExport`: a type
 * existing nowhere in the repository.
 *
 * A regex cannot close that gap. Measured on the real corpus, a first attempt keyed on
 * `Type.Member` reported `Serializer.ProtoDeserialize` as missing 17 times, because it cannot see a
 * generic method, an extension method, or an overload set, and a gate that cries wolf is one people
 * stop reading. So resolution is left to Roslyn: this script emits samples as compilation units and
 * `WallstopStudios.UnityHelpers.DocSamplesCheck` compiles them against the real `Runtime/**`.
 *
 * WHICH BLOCKS, AND WHY A MARKER. Measured over the whole tree: 280 blocks are declaration-shaped,
 * and only 103 of them compile standing alone. The other 177 are continuations -- a subtype whose
 * base was declared in the block above, an inspector attribute on a field of a class the prose has
 * already introduced -- and they are correct documentation. A gate that failed two blocks in three
 * would be switched off inside a week, so a sample opts IN by saying it stands alone:
 *
 *     <!-- doc-sample: compiles -->
 *     ```csharp
 *     [WProtoContract]
 *     public partial class Player { [WProtoMember(1)] public int Level; }
 *     ```
 *
 * The marker is an HTML comment on its own line before the fence, invisible in rendered Markdown.
 * The checked COUNT is printed on every run, beside the total the tree holds, so a corpus that
 * stops being scanned is visible rather than silent -- and a run that marks nothing fails rather
 * than reporting a clean sweep of an empty set.
 *
 * TWO WRAPPER SHAPES, because documentation has two. A block that declares a type of its own goes
 * into a namespace; a block that is a set of MEMBERS goes onto a `MonoBehaviour`, which is what its
 * prose says it decorates. Wrapping every block in a namespace reported `[WShowIf(...)] public int
 * Shield;` as `CS0116`, which reads as the author's mistake and is the gate's.
 *
 * Exit codes: 0 = samples extracted, 1 = none were, or a marker is misplaced.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it, so the
// default is the only path that ships.
const SCAN_ROOT = process.env.DOC_SAMPLES_ROOT
  ? path.resolve(process.env.DOC_SAMPLES_ROOT)
  : REPO_ROOT;
const OUTPUT_DIR = process.env.DOC_SAMPLES_OUT
  ? path.resolve(process.env.DOC_SAMPLES_OUT)
  : path.join(REPO_ROOT, "artifacts", "doc-samples");

/** Where documentation is read from. */
const DOC_ROOTS = ["docs"];

const COMPILES_MARKER = "<!-- doc-sample: compiles -->";
const FENCE = /^```(csharp|cs)\s*$/;
const CLOSING_FENCE = /^```\s*$/;
const USING_LINE = /^\s*using\s+[A-Za-z0-9_.<>, ]+\s*;\s*$/;
const ASSEMBLY_ATTRIBUTE = /^\s*\[assembly\s*:/m;
const DECLARATION_START =
  /^(\[|public |internal |sealed |abstract |static |partial |namespace |enum |class |struct |interface |record )/;

/** A type declaration at brace depth zero, which decides which wrapper a block gets. */
const TYPE_KEYWORD = /(^|[^A-Za-z0-9_])(class|struct|interface|enum|record)\s+[A-Za-z_]/;

/** Text that means the author cut something out, so the block cannot compile by construction. */
const ELISIONS = ["// ...", "/* ... */", "// (existing", "// (omitted", "..."];

/**
 * The using directives every sample gets, so an example does not open with a wall of them.
 *
 * Deliberately the namespaces a reader would already have in a Unity file, plus this package's own
 * roots. A sample needing anything else writes its own `using`, which is hoisted with these.
 */
const COMMON_USINGS = [
  "using System;",
  "using System.Collections;",
  "using System.Collections.Generic;",
  "using System.Linq;",
  "using System.Threading;",
  "using System.Threading.Tasks;",
  "using UnityEngine;",
  "using WallstopStudios.UnityHelpers.Core.Attributes;",
  "using WallstopStudios.UnityHelpers.Core.DataStructure;",
  "using WallstopStudios.UnityHelpers.Core.Extension;",
  "using WallstopStudios.UnityHelpers.Core.Helper;",
  "using WallstopStudios.UnityHelpers.Core.Math;",
  "using WallstopStudios.UnityHelpers.Core.Random;",
  "using WallstopStudios.UnityHelpers.Core.Serialization;",
  "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;",
  "using WallstopStudios.UnityHelpers.Tags;"
];

/**
 * Every Markdown file under a root.
 *
 * @param {string} root Absolute directory to walk.
 * @returns {string[]} Absolute paths, sorted so two runs agree.
 */
function markdownUnder(root) {
  if (!fs.existsSync(root)) {
    return [];
  }

  const found = [];
  const pending = [root];
  while (0 < pending.length) {
    const current = pending.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) {
        if (!entry.name.startsWith(".") && entry.name !== "node_modules") {
          pending.push(full);
        }

        continue;
      }

      if (entry.name.endsWith(".md")) {
        found.push(full);
      }
    }
  }

  return found.sort();
}

/**
 * Splits one file's C# blocks into the ones to compile and the ones deliberately left alone.
 *
 * @param {string} file Absolute path, for provenance and for errors.
 * @param {string} text The file's contents.
 * @param {{declaration: number, usage: number, empty: number}} skipped Counters.
 * @param {string[]} problems Receives one line per misplaced marker.
 * @returns {{file: string, line: number, body: string[]}[]} The blocks to compile.
 */
function samplesIn(file, text, skipped, problems) {
  const lines = text.replace(/\r\n/g, "\n").split("\n");
  const found = [];
  for (let index = 0; index < lines.length; index++) {
    if (lines[index].trim() === COMPILES_MARKER) {
      let next = index + 1;
      while (next < lines.length && lines[next].trim().length === 0) {
        next++;
      }

      if (lines.length <= next || !FENCE.test(lines[next].trim())) {
        // A marker that claims nothing is worse than no marker: it reads as a considered decision
        // and adds no block to the corpus at all.
        problems.push(
          `${file}:${index + 1}: the ${COMPILES_MARKER} marker is not followed by a \`\`\`csharp fence, so it adds nothing to the checked corpus.`
        );
      }

      continue;
    }

    if (!FENCE.test(lines[index].trim())) {
      continue;
    }

    let end = index + 1;
    while (end < lines.length && !CLOSING_FENCE.test(lines[end].trim())) {
      end++;
    }

    const body = lines.slice(index + 1, Math.min(end, lines.length));
    const start = index + 2;
    index = end;

    // `start` is the 1-based first body line, so the fence is at 0-based `start - 2` and the line
    // that may carry the marker is the one before it.
    let marker = start - 3;
    while (0 <= marker && lines[marker].trim().length === 0) {
      marker--;
    }

    const joined = body.join("\n");
    const first = body.find((line) => line.trim().length !== 0 && !line.trim().startsWith("//"));
    if (first === undefined) {
      skipped.empty++;
      continue;
    }

    if (DECLARATION_START.test(first.trim())) {
      skipped.declaration++;
    } else {
      skipped.usage++;
    }

    if (!(0 <= marker && lines[marker].trim() === COMPILES_MARKER)) {
      continue;
    }

    // Past this point the author has CLAIMED the sample stands alone, so every remaining reason to
    // skip it is a contradiction of that claim and is reported rather than swallowed.
    if (ELISIONS.some((elision) => joined.includes(elision))) {
      problems.push(
        `${file}:${start}: a sample marked ${COMPILES_MARKER} contains an elision, so it cannot compile. Remove the marker or the elision.`
      );
      continue;
    }

    if (ASSEMBLY_ATTRIBUTE.test(joined)) {
      problems.push(
        `${file}:${start}: a sample marked ${COMPILES_MARKER} may not carry an [assembly: ...] attribute; each sample is wrapped in its own namespace.`
      );
      continue;
    }

    if (DECLARATION_START.test(first.trim())) {
      skipped.declaration--;
    } else {
      skipped.usage--;
    }

    found.push({ file, line: start, body });
  }

  return found;
}

/**
 * Whether a block declares a type of its own, or is a set of MEMBERS.
 *
 * @param {string[]} body The block's lines.
 * @returns {boolean} <c>true</c> when some line declares a type outside any brace.
 * @remarks
 * The two need different wrappers, and getting it wrong is most of the corpus. Documentation for an
 * attribute is overwhelmingly written as the member it decorates -- `[WShowIf(nameof(HasShield))]
 * public int Shield;` -- which is a complete, checkable statement about the API and is not a type.
 * Wrapping every block in a namespace reported those as `CS0116`, which reads as the author's
 * mistake and is the gate's.
 */
function declaresType(body) {
  let depth = 0;
  for (const line of body) {
    const code = line.replace(/\/\/.*$/, "");
    if (depth === 0 && TYPE_KEYWORD.test(code)) {
      return true;
    }

    for (const character of code) {
      if (character === "{") {
        depth++;
      } else if (character === "}") {
        depth--;
      }
    }
  }

  return false;
}

/**
 * Renders one sample as a compilation unit.
 *
 * @param {{file: string, line: number, body: string[]}} sample What was extracted.
 * @param {string} suffix The unique namespace suffix.
 * @returns {string} The file text.
 * @remarks
 * The sample's own `using` directives are hoisted beside the common ones rather than left where
 * they sit, because C# refuses a `using` after a type declaration: a sample that opens with a type
 * and imports something later is ordinary prose, and would otherwise be a syntax error the gate
 * blamed on the author.
 */
function render(sample, suffix) {
  const usings = [];
  const rest = [];
  for (const line of sample.body) {
    if (USING_LINE.test(line) && !line.includes("(")) {
      usings.push(line.trim());
      continue;
    }

    rest.push(line);
  }

  const seen = new Set();
  const unique = COMMON_USINGS.concat(usings).filter((one) =>
    seen.has(one) ? false : seen.add(one)
  );
  const relative = path.relative(SCAN_ROOT, sample.file).split(path.sep).join("/");
  const header = [
    "// <auto-generated />",
    `// Extracted from ${relative}:${sample.line}. Edit the documentation, not this file.`,
    "#pragma warning disable",
    `namespace WallstopStudios.UnityHelpers.DocSamples.${suffix}`,
    "{",
    unique.map((one) => `    ${one}`).join("\n"),
    ""
  ];

  if (declaresType(rest)) {
    return header.concat([rest.join("\n"), "}", ""]).join("\n");
  }

  // A member sample gets a MonoBehaviour to live on, because that is what its documentation says it
  // decorates: `transform`, `gameObject` and `enabled` resolve, and an inspector attribute is
  // checked against the field it is actually written on.
  return header
    .concat([
      "    internal sealed class DocSample : UnityEngine.MonoBehaviour",
      "    {",
      rest.join("\n"),
      "    }",
      "}",
      ""
    ])
    .join("\n");
}

/**
 * A stable, filesystem-safe, collision-free suffix for one sample.
 *
 * @param {{file: string, line: number}} sample What was extracted.
 * @returns {string} A C# identifier.
 */
function suffixFor(sample) {
  const relative = path.relative(SCAN_ROOT, sample.file).split(path.sep).join("_");
  const cleaned = relative.replace(/\.md$/, "").replace(/[^A-Za-z0-9]/g, "_");
  return `S_${cleaned}_${sample.line}`;
}

function main() {
  const problems = [];
  const samples = [];
  const skipped = { declaration: 0, usage: 0, empty: 0 };
  let scanned = 0;
  for (const root of DOC_ROOTS) {
    for (const file of markdownUnder(path.join(SCAN_ROOT, root))) {
      scanned++;
      samples.push(...samplesIn(file, fs.readFileSync(file, "utf8"), skipped, problems));
    }
  }

  fs.rmSync(OUTPUT_DIR, { recursive: true, force: true });
  fs.mkdirSync(OUTPUT_DIR, { recursive: true });
  for (const sample of samples) {
    const suffix = suffixFor(sample);
    fs.writeFileSync(path.join(OUTPUT_DIR, `${suffix}.cs`), render(sample, suffix), "utf8");
  }

  console.log(`[doc-samples] Markdown files scanned: ${scanned}`);
  console.log(`[doc-samples] Samples compiled: ${samples.length}`);
  console.log(
    `[doc-samples] Unmarked: ${skipped.declaration} declaration-shaped, ${skipped.usage} usage-shaped, ${skipped.empty} empty`
  );
  console.log(`[doc-samples] Output: ${path.relative(REPO_ROOT, OUTPUT_DIR)}`);

  for (const problem of problems) {
    console.error(`[doc-samples] ERROR ${problem}`);
  }

  if (0 < problems.length) {
    process.exitCode = 1;
    return;
  }

  if (samples.length === 0) {
    // The gate's own coverage, reported as a failure rather than as a clean run. A corpus that
    // shrank to nothing looks exactly like a corpus that passed, which is the shape #556 refuses.
    console.error(
      `[doc-samples] ERROR no documentation sample carries the ${COMPILES_MARKER} marker, so this gate checked nothing.`
    );
    process.exitCode = 1;
  }
}

main();
