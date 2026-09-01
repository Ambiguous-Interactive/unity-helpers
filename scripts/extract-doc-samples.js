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
 * WHICH BLOCKS, AND WHY A MARKER. Measured over the whole tree: of 749 non-empty C# blocks, 319
 * compile standing alone and 430 do not. The rest are continuations -- a subtype whose base was
 * declared in the block above, an inspector attribute on a field of a class the prose has already
 * introduced, a snippet naming the `player` the reader is expected to supply -- and they are
 * correct documentation. A gate that failed two blocks in three would be switched off inside a
 * week, so a sample opts IN by saying it stands alone:
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
 * THREE WRAPPER SHAPES, because documentation has three. A block that declares a type of its own
 * goes into a namespace; a block that is a set of MEMBERS goes onto a `MonoBehaviour`, which is what
 * its prose says it decorates; a block that is STATEMENTS goes into a method body on that same
 * `MonoBehaviour`. Each wrong answer reads as the AUTHOR's mistake: wrapping every block in a
 * namespace reported `[WShowIf(...)] public int Shield;` as `CS0116`, and wrapping statements as
 * members reported `int i = -1;` as `CS1519`. Measured over `docs/`, 78 blocks compile only in a
 * namespace, 17 only as members and 67 only inside a method body, so all three are load-bearing.
 *
 * WHAT IS LEFT, AND WHY, measured rather than guessed
 * ([#615](https://github.com/Ambiguous-Interactive/unity-helpers/issues/615)). Of the 430 blocks
 * that do not compile: 49 carry an elision, so nothing can compile them; 68 fail structurally --
 * a fragment, a signature with no body, pseudo-code; and 313 fail only because a name does not
 * resolve. Those 313 split by what is missing: 34 name NUnit, 20 name Odin, 12 name `UnityEditor`,
 * one names uGUI, and **246 name something the reader is expected to supply** -- the `player` in
 * `player.Health`.
 *
 * A PER-PAGE PREAMBLE -- one marked block per page declaring the vocabulary its snippets share --
 * would convert those 246. It was priced and declined. Across the 33 pages it would serve, the
 * median page needs 8 declarations and the worst 68, and **81% of them (382 of 470) would serve
 * exactly one block**. That is not a shared vocabulary; it is each snippet's scaffold hoisted out
 * of the snippet, a second and unread copy of the documentation, largest on exactly the pages
 * where it would pay most. The 246 stay unchecked, by decision rather than by accident.
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
/** A type declaration at brace depth zero, which decides which scope a block's item goes in. */
const TYPE_KEYWORD = /(^|[^A-Za-z0-9_])(class|struct|interface|enum|record)\s+[A-Za-z_]/;

/**
 * Separates a block's `using` directives from the rest of it.
 *
 * @param {string[]} body The block's lines.
 * @returns {{usings: string[], rest: string[]}} The directives, and everything else.
 * @remarks
 * The directives are hoisted rather than left where they sit, because C# refuses a `using` after a
 * type declaration: a sample that opens with a type and imports something later is ordinary prose,
 * and would otherwise be a syntax error the gate blamed on the author.
 */
function hoist(body) {
  const usings = [];
  const rest = [];
  for (const line of body) {
    if (USING_LINE.test(line) && !line.includes("(")) {
      usings.push(line.trim());
      continue;
    }

    rest.push(line);
  }

  return { usings, rest };
}

/** Text that means the author cut something out, so the block cannot compile by construction. */
const ELISIONS = ["// ...", "/* ... */", "// (existing", "// (omitted", "..."];

/**
 * The using directives every sample gets, so an example does not open with a wall of them.
 *
 * Deliberately the namespaces a reader would already have in a Unity file, plus this package's own
 * roots. A sample needing anything else writes its own `using`, which is hoisted with these.
 *
 * `Integrations.Zenject`, `Integrations.VContainer` and `Integrations.Reflex` are deliberately
 * absent: all three declare `RelationalSceneAssignmentOptions`, so importing them together turns
 * every use of that name into `CS0104` in a sample that is correct.
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
  "using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;",
  "using WallstopStudios.UnityHelpers.Core.Extension;",
  "using WallstopStudios.UnityHelpers.Core.Helper;",
  "using WallstopStudios.UnityHelpers.Core.Math;",
  "using WallstopStudios.UnityHelpers.Core.Model;",
  "using WallstopStudios.UnityHelpers.Core.Random;",
  "using WallstopStudios.UnityHelpers.Core.Serialization;",
  "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;",
  "using WallstopStudios.UnityHelpers.Tags;",
  "using WallstopStudios.UnityHelpers.Utils;"
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
 * @param {{declaration: number, member: number, usage: number, empty: number}} skipped Counters.
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

    if (!(0 <= marker && lines[marker].trim() === COMPILES_MARKER)) {
      /*
       * Counted by the SAME sort that decides the wrapper, so the report says how much of the
       * corpus is reachable rather than only how much of it is claimed. Reading the shape off the
       * first line instead put every block opening with a `using` directive or a `private` member
       * in the statement column: 448 of 646 were called usage-shaped, and 153 of those were not.
       */
      const scopes = scopesFor(hoist(body).rest);
      if (0 < scopes.type.length) {
        skipped.declaration++;
      } else if (scopes.hasStatement) {
        skipped.usage++;
      } else {
        skipped.member++;
      }

      continue;
    }

    // Past this point the author has CLAIMED the sample stands alone, so every remaining reason not
    // to compile it contradicts that claim and is reported rather than swallowed.
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

    found.push({ file, line: start, body });
  }

  return found;
}

/**
 * One stripped line per input line, with comments and string CONTENTS removed.
 *
 * @param {string[]} body The block's lines.
 * @returns {string[]} The same lines, reduced to the code a brace count may be taken over.
 * @remarks
 * Everything downstream counts braces and looks for a terminator, and both are wrong on raw text:
 * `Debug.Log("} // done")` closes a scope that never opened and truncates the line at a comment
 * that is not one. Only the CLASSIFICATION reads this; what is emitted is always the author's own
 * line, so a mistake here can never rewrite a sample.
 */
function stripped(body) {
  const out = [];
  let inBlockComment = false;
  let inVerbatim = false;
  for (const raw of body) {
    let result = "";
    let index = 0;
    while (index < raw.length) {
      if (inVerbatim) {
        if (raw[index] === '"' && raw[index + 1] === '"') {
          index += 2;
          continue;
        }

        if (raw[index] === '"') {
          inVerbatim = false;
        }

        index++;
        continue;
      }

      const pair = raw.slice(index, index + 2);
      if (inBlockComment) {
        if (pair === "*/") {
          inBlockComment = false;
          index += 2;
          continue;
        }

        index++;
        continue;
      }

      if (pair === "/*") {
        inBlockComment = true;
        index += 2;
        continue;
      }

      if (pair === "//") {
        break;
      }

      const character = raw[index];
      if (character === '"' && 0 < index && raw[index - 1] === "@") {
        inVerbatim = true;
        index++;
        result += " ";
        continue;
      }

      if (character === '"' || character === "'") {
        index++;
        while (index < raw.length) {
          if (raw[index] === "\\") {
            index += 2;
            continue;
          }

          if (raw[index] === character) {
            index++;
            break;
          }

          index++;
        }

        result += " ";
        continue;
      }

      result += character;
      index++;
    }

    out.push(result);
  }

  return out;
}

/**
 * Splits a block into the top-level items it declares, as line ranges covering the whole block.
 *
 * @param {string[]} code The block's STRIPPED lines, with its `using` directives already removed.
 * @returns {{start: number, end: number}[]} Half-open ranges, contiguous and in source order.
 * @remarks
 * An item ends where brace depth returns to zero on a line that closes it -- a `;` when the item
 * had no block, a `}` when it did. The ranges are contiguous rather than trimmed, so a blank line
 * or a comment before a declaration travels with the declaration instead of being dropped, and
 * anything a block leaves unterminated is a final item that is classified rather than lost.
 */
function topLevelItems(code) {
  const items = [];
  let depth = 0;
  let opened = false;
  let start = 0;
  let sawCode = false;
  for (let index = 0; index < code.length; index++) {
    const line = code[index];
    if (!sawCode && line.trim().length === 0) {
      continue;
    }

    sawCode = true;
    for (const character of line) {
      if (character === "{") {
        depth++;
        opened = true;
      } else if (character === "}") {
        depth--;
      }
    }

    const trimmed = line.trim();
    const closed = opened ? /\}[\s;,]*$/.test(trimmed) : /;\s*$/.test(trimmed);
    if (depth <= 0 && closed) {
      items.push({ start, end: index + 1 });
      start = index + 1;
      opened = false;
      depth = 0;
      sawCode = false;
    }
  }

  if (start < code.length) {
    items.push({ start, end: code.length });
  }

  return items;
}

/** A member declaration's leading keyword; none of these can open a statement. */
const MEMBER_MODIFIER =
  /^(public|private|protected|internal|static|readonly|const|virtual|override|abstract|sealed|extern|event|partial|volatile|unsafe|new)\s/;

/** `ReturnType Name(` -- two identifiers and a parameter list, which no call or assignment is. */
const METHOD_SIGNATURE =
  /^[A-Za-z_][A-Za-z0-9_<>,.\[\]?]*\s+[A-Za-z_][A-Za-z0-9_]*\s*(<[^>()]*>)?\s*\(/;

/** Keywords that open a STATEMENT, so `return Compute(x);` is not read as a method declaration. */
const STATEMENT_KEYWORD =
  /^(return|throw|yield|await|new|if|else|while|for|foreach|switch|case|lock|using|try|catch|finally|do|checked|unchecked|fixed|goto|break|continue|var|ref|out|in|delegate)\s/;

/**
 * What one top-level item is, which is what decides the block's wrapper.
 *
 * @param {string} item One entry from {@link topLevelItems}, as its STRIPPED text.
 * @returns {"trivia"|"type"|"member"|"statement"} Its kind.
 */
function kindOf(item) {
  if (item.trim().length === 0) {
    // A trailing comment is the commonest last "item" in a documentation block, and reading it as
    // a statement moved eight member-only samples into a method body and reported CS0106.
    return "trivia";
  }

  const withoutAttributes = item.replace(/^(\s*\[[^\]]*\]\s*\n?)+/, "");
  const head = withoutAttributes.split("{")[0];
  if (TYPE_KEYWORD.test(head)) {
    return "type";
  }

  if (item.trim().startsWith("[")) {
    // An attribute list that did not lead to a type declaration is decorating a member, and a
    // statement cannot carry one.
    return "member";
  }

  const trimmed = withoutAttributes.trim();
  if (MEMBER_MODIFIER.test(trimmed)) {
    return "member";
  }

  if (METHOD_SIGNATURE.test(trimmed) && !STATEMENT_KEYWORD.test(trimmed)) {
    return "member";
  }

  if (/\{\s*get\b/.test(item) && !/=>/.test(head)) {
    return "member";
  }

  return "statement";
}

/**
 * Sorts a block's lines into the scopes C# would accept them in.
 *
 * @param {string[]} body The block's lines, with its `using` directives already removed.
 * @returns {{type: string[], rest: string[], hasStatement: boolean}} The author's own lines,
 *   in source order within each scope.
 * @remarks
 * Getting this wrong is most of the corpus, and each wrong answer reads as the AUTHOR's mistake.
 * Documentation comes in three shapes and only three: a block that declares types of its own, a
 * block that is the MEMBERS the prose says an attribute decorates -- `[WShowIf(nameof(HasShield))]
 * public int Shield;`, which a namespace cannot contain and reported `CS0116` -- and a block that
 * is STATEMENTS, which is the largest shape in the tree and which neither of the other two scopes
 * can hold. Measured over `docs/`: 78 blocks compile only in a namespace, 17 only as members, and
 * 67 only inside a method body.
 *
 * A TYPE is lifted out, because documentation routinely declares a small helper type and then uses
 * it -- `public enum Stance { ... }` followed by `Stance stance = Stance.Idle;` -- and that is one
 * sample, not two. 21 blocks in the tree have that shape and no single wrapper holds any of them.
 * A type can be lifted safely because nothing it contains can close over a local.
 *
 * NOTHING ELSE IS SEPARATED, and the two samples that proved it are worth keeping: a block
 * declaring `void OnDestroy() { proxy.OnCollisionEnter -= ...; }` beside `CollisionProxy proxy =
 * ...;` compiles only when the method stays beside the local as a LOCAL FUNCTION, and one
 * declaring `IEnumerator Start()` beside `async Task<string> DownloadDataAsync()` compiles only
 * when both stay together. So a block with any statement in it puts every non-type item in the
 * method body; a block with none puts them all on the class.
 *
 * Only the SORT is inferred; each line is emitted exactly as its author wrote it.
 */
function scopesFor(body) {
  const code = stripped(body);
  const type = [];
  const rest = [];
  let hasStatement = false;
  let previous = rest;
  for (const item of topLevelItems(code)) {
    const kind = kindOf(code.slice(item.start, item.end).join("\n"));
    const lines = body.slice(item.start, item.end);
    if (kind === "trivia") {
      // A comment belongs beside whatever it was written under.
      previous.push(...lines);
      continue;
    }

    previous = kind === "type" ? type : rest;
    hasStatement = hasStatement || kind === "statement";
    previous.push(...lines);
  }

  return { type, rest, hasStatement };
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
  const { usings, rest } = hoist(sample.body);
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

  const scopes = scopesFor(rest);
  const parts = header.slice();
  if (0 < scopes.type.length) {
    parts.push(scopes.type.join("\n"));
  }

  /*
   * Whatever is not a type needs a MonoBehaviour to live on, because that is what the documentation
   * says it belongs to: `transform`, `gameObject` and `enabled` resolve, and an inspector attribute
   * is checked against the field it is actually written on.
   */
  if (0 < scopes.rest.length) {
    parts.push("    internal sealed class DocSample : UnityEngine.MonoBehaviour", "    {");
    const joined = scopes.rest.join("\n");
    if (scopes.hasStatement) {
      /*
       * The signature follows what the statements do rather than being fixed, because a coroutine
       * sample is written as `yield return` and an async one as `await`, and a `void` wrapper
       * would report both as the author's error.
       */
      let signature = "private void DocSampleUsage()";
      if (/\byield\s+(return|break)\b/.test(joined)) {
        signature = "private System.Collections.IEnumerator DocSampleUsage()";
      } else if (/\bawait\s/.test(joined)) {
        signature = "private async System.Threading.Tasks.Task DocSampleUsage()";
      }

      parts.push(`        ${signature}`, "        {", joined, "        }");
    } else {
      parts.push(joined);
    }

    parts.push("    }");
  }

  return parts.concat(["}", ""]).join("\n");
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
  const skipped = { declaration: 0, member: 0, usage: 0, empty: 0 };
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
    `[doc-samples] Unmarked: ${skipped.declaration} declaration-shaped, ${skipped.member} member-shaped, ${skipped.usage} statement-shaped, ${skipped.empty} empty`
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
