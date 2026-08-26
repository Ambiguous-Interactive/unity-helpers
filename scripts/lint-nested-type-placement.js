#!/usr/bin/env node
/**
 * A nested type is declared at the END of its containing type, never between other members.
 *
 * The rule is the owner's, from review on #574: "Please place class/struct definitions at the end
 * of the file, or in their own file, do not intersperse with code." A reader scrolling a type for
 * a method should not have to step over a nested type to find it, and a type declaration in the
 * middle of a body reads as the start of a new file's worth of content. Recorded in
 * .llm/context.md (rule 6) and create-csharp-file (rule 5b); this is what stops it decaying (#575).
 *
 * Only the five declarations that OPEN A BODY are types for this purpose -- class, struct,
 * interface, record and enum. A `delegate` is a nested type too, but it is a single statement with
 * no body, so it cannot be the wall of content the rule is about.
 *
 * This tokenizes rather than greps, because the interesting words are not always declarations:
 *   * `where T : class` and `where T : struct` are constraints, not nested types.
 *   * `record` is contextual and is a perfectly good field or parameter name.
 *   * `[Attr(new[] { 1, 2 })]` puts a brace before the member's own body brace.
 *   * a field initializer -- `private int[] _a = { 1, 2 };` -- does the same.
 * Parameter lists, attribute sections and bracketed types are skipped as balanced spans, and
 * comments and string literals are masked to spaces first so a brace inside either cannot count.
 *
 * A member that does not sit wholly inside ONE conditional region is reported but never
 * rewritten. A member inside `#if UNITY_EDITOR` is compiled only there, so moving it past the
 * `#endif` changes which build sees it -- and a slice that carries an unbalanced `#if` lands the
 * directive mid-line, where C# refuses it outright. Two files in the #575 sweep hit exactly that.
 * Members are reordered only when every one of them opens and closes in the same region, which
 * leaves a conditional wholly inside a member (the common `#if UNITY_EDITOR` inside a method) free
 * to move with it.
 *
 * `--fix` moves each nested type to the end of its containing type body. The rewrite is a
 * PERMUTATION of exact source slices: every member carries its own leading trivia (doc comment,
 * attributes) and its trailing same-line comment, the slices tile the body with no gaps, and the
 * rewritten file is asserted to have the same length as the original before it is written. A fix
 * that changes a byte of anything other than order is a bug, and the length check is what says so.
 *
 * Exit codes: 0 = clean (or every violation fixed), 1 = at least one violation remains.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");

// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.NESTED_TYPE_PLACEMENT_ROOTS
  ? process.env.NESTED_TYPE_PLACEMENT_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests", "Generator~"];

// Vendored upstream verbatim. `lint:comparison-direction` excludes it for the same reason: a local
// reordering makes the next upstream diff unreadable.
const EXCLUDED_PREFIXES = ["Runtime/Utils/SevenZip"];

const TYPE_KEYWORDS = ["class", "struct", "interface", "record", "enum"];
const TYPE_DECLARATION = new RegExp(`\\b(${TYPE_KEYWORDS.join("|")})\\s+(@?[A-Za-z_]\\w*)`);

// `class` and `struct` also introduce a generic constraint, and `where T : class where U : new()`
// puts an identifier-shaped token right where a type name would be. Every word that can legally
// follow one of the five keywords WITHOUT being a declared name is refused here.
const NOT_A_TYPE_NAME = new Set([
  "where",
  "new",
  "class",
  "struct",
  "record",
  "interface",
  "enum",
  "unmanaged",
  "notnull",
  "default",
  "null",
  "this",
  "base",
  "void",
  "delegate"
]);

const OPENERS = { "(": ")", "[": "]", "{": "}" };

/**
 * Replaces every comment and string/char literal with spaces, preserving length and newlines, so
 * brace matching cannot be thrown by a brace inside either. Interpolated strings are masked whole,
 * braces included, which keeps the balance the caller relies on.
 */
function maskNoise(text) {
  const out = text.split("");
  const blank = (start, end) => {
    for (let i = start; i < end && i < out.length; i += 1) {
      if (out[i] !== "\n" && out[i] !== "\r") {
        out[i] = " ";
      }
    }
  };

  let index = 0;
  while (index < text.length) {
    const char = text[index];
    const next = text[index + 1];

    if (char === "/" && next === "/") {
      let end = text.indexOf("\n", index);
      end = end < 0 ? text.length : end;
      blank(index, end);
      index = end;
      continue;
    }
    if (char === "/" && next === "*") {
      let end = text.indexOf("*/", index + 2);
      end = end < 0 ? text.length : end + 2;
      blank(index, end);
      index = end;
      continue;
    }
    if (char === "'") {
      let cursor = index + 1;
      while (cursor < text.length && text[cursor] !== "'") {
        cursor += text[cursor] === "\\" ? 2 : 1;
      }
      blank(index, cursor + 1);
      index = cursor + 1;
      continue;
    }

    const verbatim = char === "@" && next === '"';
    const interpolatedVerbatim =
      (char === "$" && next === "@" && text[index + 2] === '"') ||
      (char === "@" && next === "$" && text[index + 2] === '"');
    const interpolated = char === "$" && next === '"';
    if (char === '"' || verbatim || interpolated || interpolatedVerbatim) {
      const quote = text.indexOf('"', index);
      const raw = verbatim || interpolatedVerbatim;
      let cursor = quote + 1;
      while (cursor < text.length) {
        if (raw) {
          if (text[cursor] === '"') {
            if (text[cursor + 1] === '"') {
              cursor += 2;
              continue;
            }
            break;
          }
          cursor += 1;
          continue;
        }
        if (text[cursor] === "\\") {
          cursor += 2;
          continue;
        }
        if (text[cursor] === '"' || text[cursor] === "\n") {
          break;
        }
        cursor += 1;
      }
      blank(index, cursor + 1);
      index = cursor + 1;
      continue;
    }
    index += 1;
  }
  return out.join("");
}

/**
 * A key per index naming the conditional branch that is open there. Two positions share a key only
 * when the same `#if`/`#elif`/`#else` branches are active, so comparing keys answers "would moving
 * this change which build compiles it".
 */
function regionKeys(text) {
  const keys = new Array(text.length + 1);
  const stack = [];
  let issued = 0;
  let index = 0;

  while (index <= text.length) {
    const newline = text.indexOf("\n", index);
    const lineEnd = newline < 0 ? text.length : newline + 1;
    const line = text.slice(index, lineEnd).trim();

    if (line.startsWith("#if")) {
      issued += 1;
      stack.push(issued);
    } else if (line.startsWith("#elif") || line.startsWith("#else")) {
      issued += 1;
      stack[Math.max(0, stack.length - 1)] = issued;
    } else if (line.startsWith("#endif")) {
      stack.pop();
    }

    const key = stack.join("/");
    for (let cursor = index; cursor < lineEnd; cursor += 1) {
      keys[cursor] = key;
    }
    if (text.length <= newline || newline < 0) {
      keys[text.length] = key;
      break;
    }
    index = lineEnd;
  }
  return keys;
}

/** Index just past the span opened at `open`, or -1 when it never closes. */
function skipBalanced(masked, open) {
  const stack = [OPENERS[masked[open]]];
  for (let index = open + 1; index < masked.length; index += 1) {
    const char = masked[index];
    if (OPENERS[char]) {
      stack.push(OPENERS[char]);
      continue;
    }
    if (char === stack[stack.length - 1]) {
      stack.pop();
      if (stack.length === 0) {
        return index + 1;
      }
    }
  }
  return -1;
}

/** Extends a member's end through a trailing same-line comment and its newline. */
function endOfLineAfter(text, masked, end) {
  let cursor = end;
  while (cursor < text.length && (text[cursor] === " " || text[cursor] === "\t")) {
    cursor += 1;
  }
  if (masked[cursor] === " " || masked[cursor] === "\n" || masked[cursor] === "\r") {
    // Whitespace in the mask where the source has content is a comment; either way the rest of the
    // line belongs to the member that just ended, not to the one that starts on the next line.
    while (cursor < text.length && text[cursor] !== "\n") {
      if (masked[cursor] !== " " && masked[cursor] !== "\r") {
        return end;
      }
      cursor += 1;
    }
    return cursor < text.length ? cursor + 1 : text.length;
  }
  return end;
}

/**
 * Splits a type body into members that tile it exactly. Each member owns its leading trivia and
 * ends at its own terminator, so concatenating the members in any order reproduces valid source.
 */
function membersOf(text, masked, bodyStart, bodyEnd) {
  const members = [];
  let cursor = bodyStart;

  while (cursor < bodyEnd) {
    // Trivia and whitespace belong to the member that follows them.
    let scan = cursor;
    while (scan < bodyEnd && /\s/.test(masked[scan])) {
      scan += 1;
    }
    if (bodyEnd <= scan) {
      break;
    }

    const headerStart = scan;
    let bodyBrace = -1;
    let terminator = -1;
    while (scan < bodyEnd) {
      const char = masked[scan];
      if (char === "(" || char === "[") {
        const close = skipBalanced(masked, scan);
        if (close < 0) {
          return null;
        }
        scan = close;
        continue;
      }
      if (char === "{") {
        bodyBrace = scan;
        break;
      }
      if (char === ";") {
        terminator = scan;
        break;
      }
      scan += 1;
    }

    let end;
    if (0 <= terminator) {
      end = terminator + 1;
    } else if (0 <= bodyBrace) {
      const close = skipBalanced(masked, bodyBrace);
      if (close < 0) {
        return null;
      }
      let after = close;
      while (after < bodyEnd && /\s/.test(masked[after])) {
        after += 1;
      }
      if (masked[after] === ";") {
        end = after + 1;
      } else if (masked[after] === "=") {
        // `public int Count { get; } = 5;` -- the accessor block is not the end of the member.
        let tail = after;
        while (tail < bodyEnd && masked[tail] !== ";") {
          if (masked[tail] === "{" || masked[tail] === "(" || masked[tail] === "[") {
            const inner = skipBalanced(masked, tail);
            if (inner < 0) {
              return null;
            }
            tail = inner;
            continue;
          }
          tail += 1;
        }
        end = Math.min(tail + 1, bodyEnd);
      } else {
        end = close;
      }
    } else {
      // No terminator before the body closes. That is a preprocessor line, an attribute on the
      // closing brace, or source this cannot parse; it becomes trailing trivia rather than
      // discarding the whole body, because a body this cannot split is a body it silently stops
      // checking (#556).
      members.push({
        start: cursor,
        end: bodyEnd,
        headerStart: headerStart,
        isType: false,
        trailing: true
      });
      return members;
    }

    end = endOfLineAfter(text, masked, Math.min(end, bodyEnd));
    if (end <= cursor) {
      return null;
    }

    const header = masked.slice(headerStart, 0 <= bodyBrace ? bodyBrace : end);
    const match = TYPE_DECLARATION.exec(header);
    const isType = match !== null && !NOT_A_TYPE_NAME.has(match[2]);

    members.push({
      start: cursor,
      end,
      headerStart,
      isType,
      kind: isType ? match[1] : null,
      name: isType ? match[2] : null,
      innerBody:
        isType && 0 <= bodyBrace ? [bodyBrace + 1, skipBalanced(masked, bodyBrace) - 1] : null
    });
    cursor = end;
  }

  if (cursor < bodyEnd) {
    members.push({
      start: cursor,
      end: bodyEnd,
      headerStart: cursor,
      isType: false,
      trailing: true
    });
  }
  return members;
}

/** Every type body in a file, outermost first. Nested types are bodies too, so this does not
 * skip past one it has just found -- the scan continues inside it. */
function typeBodies(masked) {
  const bodies = [];
  const declaration = /\b(class|struct|interface|record|enum)\s+(@?[A-Za-z_]\w*)/g;
  let match;
  while ((match = declaration.exec(masked)) !== null) {
    if (NOT_A_TYPE_NAME.has(match[2])) {
      continue;
    }
    let cursor = match.index + match[0].length;
    let open = -1;
    while (cursor < masked.length) {
      const char = masked[cursor];
      if (char === "(" || char === "[") {
        const close = skipBalanced(masked, cursor);
        if (close < 0) {
          break;
        }
        cursor = close;
        continue;
      }
      if (char === "{") {
        open = cursor;
        break;
      }
      if (char === ";" || char === "}") {
        break;
      }
      cursor += 1;
    }
    if (open < 0) {
      continue;
    }
    const close = skipBalanced(masked, open);
    if (close < 0) {
      continue;
    }
    bodies.push({ kind: match[1], name: match[2], start: open + 1, end: close - 1 });
  }
  bodies.sort((a, b) => a.start - b.start || b.end - a.end);
  return bodies;
}

function lineOf(text, index) {
  let line = 1;
  for (let cursor = 0; cursor < index; cursor += 1) {
    if (text[cursor] === "\n") {
      line += 1;
    }
  }
  return line;
}

/** Every violation in one file, plus the rewrite that removes them. */
function analyzeFile(text) {
  const masked = maskNoise(text);
  const keys = regionKeys(text);
  const bodies = typeBodies(masked);
  const violations = [];
  const edits = [];

  for (const body of bodies) {
    if (body.kind === "enum") {
      continue;
    }
    const members = membersOf(text, masked, body.start, body.end);
    if (members === null) {
      continue;
    }
    const lastNonType = members.reduce(
      (found, member, index) => (member.isType || member.trailing ? found : index),
      -1
    );
    const offenders = members.filter((member, index) => member.isType && index < lastNonType);
    if (offenders.length === 0) {
      continue;
    }
    for (const offender of offenders) {
      violations.push({
        line: lineOf(text, offender.headerStart),
        kind: offender.kind,
        name: offender.name,
        container: body.name
      });
    }

    // The move is targeted rather than a whole-body permutation: only the offending types are
    // relocated, and everything else -- including every `#if` member -- keeps its position and so
    // its directive order. A type is movable only when it carries no conditional boundary of its
    // own and the end of the body is in the same region it is, which is what stops a member being
    // compiled into a different build than the one it was written for.
    // The end of the body is the closing brace itself, past any trailing `#endif`, so a type that
    // was unconditional before the move is still unconditional after it.
    const destination = keys[body.end];
    const movable = offenders.filter(
      (member) =>
        keys[member.start] === destination && keys[Math.max(member.end - 1, 0)] === destination
    );
    if (movable.length === 0) {
      continue;
    }

    if (edits.some((edit) => edit.start <= body.start && body.end <= edit.end)) {
      // An enclosing body is already being rewritten this pass, and its replacement was built from
      // the text as it is now. Two overlapping edits would clobber each other; the caller
      // re-analyzes until the file settles, which reaches this one on the next pass.
      continue;
    }

    const moved = new Set(movable);
    // Trailing whitespace before the closing brace reads better after the moved types; a trailing
    // `#endif` must stay where it is, which is what put the destination past it.
    const trailingMember = members.find(
      (member) => member.trailing && !/^[ \t]*#/m.test(text.slice(member.start, member.end))
    );
    const ordered = members
      .filter((member) => !moved.has(member) && member !== trailingMember)
      .concat(movable)
      .concat(trailingMember ? [trailingMember] : []);
    edits.push({
      start: body.start,
      end: body.end,
      replacement: ordered.map((member) => text.slice(member.start, member.end)).join("")
    });
  }

  return { violations, edits };
}

function applyEdits(text, edits) {
  const ordered = [...edits].sort((a, b) => b.start - a.start);
  let updated = text;
  for (const edit of ordered) {
    updated = updated.slice(0, edit.start) + edit.replacement + updated.slice(edit.end);
  }
  return updated;
}

function sourceFiles(directory, found) {
  if (!fs.existsSync(directory)) {
    return found;
  }
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "bin" || entry.name === "obj" || entry.name === "node_modules") {
        continue;
      }
      sourceFiles(full, found);
    } else if (entry.name.endsWith(".cs")) {
      found.push(full);
    }
  }
  return found;
}

function main(argv) {
  const fix = argv.includes("--fix");
  const verbose = argv.includes("--verbose");

  const files = [];
  for (const root of SCAN_ROOTS) {
    sourceFiles(path.isAbsolute(root) ? root : path.join(REPO_ROOT, root), files);
  }
  const scanned = files
    .map((file) => path.relative(REPO_ROOT, file).split(path.sep).join("/"))
    .filter((relative) => !EXCLUDED_PREFIXES.some((prefix) => relative.startsWith(prefix)))
    .sort();

  if (scanned.length === 0) {
    console.error("[nested-type-placement] no C# files found; the scan roots are wrong.");
    return 1;
  }

  const remaining = [];
  let fixedFiles = 0;
  let fixedSites = 0;

  for (const relative of scanned) {
    const file = path.join(REPO_ROOT, relative);
    let text = fs.readFileSync(file, "utf8");
    let result = analyzeFile(text);

    if (fix && 0 < result.edits.length) {
      // Nesting means an outer move can expose an inner one; re-analyze until the file settles.
      // A file keeps whatever this CAN fix even when something in it cannot be moved, or one
      // conditional-bound type would hold a whole file's backlog hostage.
      const original = text;
      const before = result.violations.length;
      let guard = 0;
      while (0 < result.edits.length && guard < 12) {
        const updated = applyEdits(text, result.edits);
        if (updated.length !== text.length) {
          console.error(
            `[nested-type-placement] refusing to rewrite ${relative}: the reordering changed ` +
              `${text.length} bytes into ${updated.length}. Fix by hand.`
          );
          break;
        }
        if (updated === text) {
          break;
        }
        text = updated;
        result = analyzeFile(text);
        guard += 1;
      }
      if (text !== original) {
        fs.writeFileSync(file, text);
        fixedFiles += 1;
        fixedSites += before - result.violations.length;
      }
    }

    for (const violation of result.violations) {
      remaining.push(
        `${relative}:${violation.line}: nested ${violation.kind} '${violation.name}' is declared ` +
          `between members of '${violation.container}'`
      );
    }
  }

  if (fix) {
    console.log(
      `[nested-type-placement] moved ${fixedSites} nested type(s) to the end of their containing ` +
        `type across ${fixedFiles} file(s).`
    );
  }
  if (0 < remaining.length) {
    console.error(
      `[nested-type-placement] ${remaining.length} nested type(s) are declared between members. ` +
        "Move each to the end of its containing type, or into its own file (issue #575)."
    );
    for (const entry of remaining) {
      console.error(`  ${entry}`);
    }
    return 1;
  }
  if (verbose) {
    console.log(`[nested-type-placement] ${scanned.length} file(s) clean.`);
  }
  return 0;
}

module.exports = { maskNoise, regionKeys, membersOf, typeBodies, analyzeFile, applyEdits };

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
