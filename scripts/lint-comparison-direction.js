#!/usr/bin/env node
/**
 * Every comparison in this repository reads left-to-right: only `<` and `<=`.
 *
 * `index >= 0` becomes `0 <= index`, `a > b` becomes `b < a`, and a range reads as one line of
 * number line -- `0 <= sum && sum < max` -- instead of the reader mentally reversing half of it.
 * The rule is in .llm/context.md; this is what stops it decaying (#554).
 *
 * `>` is not always a comparison, which is why this tokenizes rather than greps:
 *   * generic closers -- `Dictionary<string, Func<object, object>>`
 *   * lambda arrows `=>`, pointer access `->`, shifts `>>` / `>>=` / `>>>`
 *   * relational patterns -- `c is >= 'A' and <= 'Z'`, `{ Count: > 0 }`, `case > 5:`. These have no
 *     left-hand operand to move, so they are exempt rather than flagged.
 *   * `operator >` declarations, which C# requires be declared pairwise.
 *
 * A `>` is a comparison only when the token before it can END an expression (an identifier, a
 * literal, `)`, `]`, `++`, `--`, or one of `this`/`base`/`true`/`false`/`default`). Anything this
 * cannot classify is left alone: a false negative costs a conversion, a false positive costs a
 * broken rewrite.
 *
 * `--fix` swaps the operands. It refuses when BOTH operands can have a side effect, because that is
 * the only case where the order they are evaluated in is observable: `Next() > Peek()` and
 * `Peek() < Next()` call them in the opposite order, while `GetCount() > 0` cannot tell.
 *
 * Exit codes: 0 = clean (or every violation fixed), 1 = at least one violation remains.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it.
const SCAN_ROOTS = process.env.COMPARISON_DIRECTION_ROOTS
  ? process.env.COMPARISON_DIRECTION_ROOTS.split(path.delimiter).filter(Boolean)
  : ["Runtime", "Editor", "Tests"];

/* ------------------------------------------------------------------ lexer */

// `<`, `>`, `<=`, `<<`, `>>` and `>>=` are absent on purpose: angle brackets are lexed one
// character at a time so `List<List<int>>` ends in two closers rather than one shift, and the
// multi-character forms are recovered from adjacency in the analysis pass.
const PUNCTUATORS = [
  "??=",
  "...",
  "=>",
  "==",
  "!=",
  "&&",
  "||",
  "??",
  "?.",
  "?[",
  "++",
  "--",
  "+=",
  "-=",
  "*=",
  "/=",
  "%=",
  "&=",
  "|=",
  "^=",
  "->",
  "::"
];

const KEYWORDS = new Set([
  "abstract",
  "as",
  "base",
  "bool",
  "break",
  "byte",
  "case",
  "catch",
  "char",
  "checked",
  "class",
  "const",
  "continue",
  "decimal",
  "default",
  "delegate",
  "do",
  "double",
  "else",
  "enum",
  "event",
  "explicit",
  "extern",
  "false",
  "finally",
  "fixed",
  "float",
  "for",
  "foreach",
  "goto",
  "if",
  "implicit",
  "in",
  "int",
  "interface",
  "internal",
  "is",
  "lock",
  "long",
  "namespace",
  "new",
  "null",
  "object",
  "operator",
  "out",
  "override",
  "params",
  "private",
  "protected",
  "public",
  "readonly",
  "ref",
  "return",
  "sbyte",
  "sealed",
  "short",
  "sizeof",
  "stackalloc",
  "static",
  "string",
  "struct",
  "switch",
  "this",
  "throw",
  "true",
  "try",
  "typeof",
  "uint",
  "ulong",
  "unchecked",
  "unsafe",
  "ushort",
  "using",
  "virtual",
  "void",
  "volatile",
  "while"
]);

/** Keywords that END an expression, so a `>` after one is a comparison rather than a pattern. */
const EXPRESSION_TERMINATING_KEYWORDS = new Set(["this", "base", "true", "false", "default"]);

/**
 * Tokens that may appear inside a type-argument list. Anything else proves the `<` was a
 * comparison: `a < b && c` cannot be `a<b && c>`.
 */
const TYPE_ARGUMENT_KEYWORDS = new Set([
  "bool",
  "byte",
  "char",
  "decimal",
  "double",
  "float",
  "int",
  "long",
  "object",
  "sbyte",
  "short",
  "string",
  "uint",
  "ulong",
  "ushort",
  "void",
  "in",
  "out",
  "ref",
  "readonly"
]);
// `(` and `)` are here for tuple types -- `Dictionary<string, (int Line, string Text)>` -- and are
// balance-checked in the scan, so `if (a < b) Foo(c > d)` cannot masquerade as one type argument.
const TYPE_ARGUMENT_PUNCTUATION = new Set([".", ",", "?", "[", "]", "*", "::", "(", ")"]);

function tokenize(text, lineOffset = 0, columnOffset = 0, holeTag = undefined) {
  const tokens = [];
  let i = 0;
  let line = 1;
  let lineStart = 0;
  const length = text.length;

  const push = (kind, start, end) => {
    tokens.push({
      kind,
      text: text.slice(start, end),
      start,
      end,
      line: line + lineOffset,
      column: start - lineStart + 1 + (line === 1 ? columnOffset : 0),
      /**
       * Identifies the interpolation hole this token came from, or undefined at the top level.
       * Hole tokens are emitted BEFORE the literal that contains them, so an operand scan that
       * ignored this would walk out of `$"{a > b}"` and swallow the whole literal.
       */
      hole: holeTag,
      /** True when the previous character is not whitespace -- how `>=` is told from `> =`. */
      glued: 0 < start && !/\s/.test(text[start - 1])
    });
  };

  const advanceLines = (from, to) => {
    for (let j = from; j < to; j++) {
      if (text[j] === "\n") {
        line++;
        lineStart = j + 1;
      }
    }
  };

  while (i < length) {
    const c = text[i];

    if (c === "\n") {
      line++;
      i++;
      lineStart = i;
      continue;
    }
    if (/\s/.test(c)) {
      i++;
      continue;
    }

    // Preprocessor directives own their whole line and never contain a C# comparison.
    if (c === "#" && text.slice(lineStart, i).trim() === "") {
      const end = text.indexOf("\n", i);
      i = end < 0 ? length : end;
      continue;
    }

    if (c === "/" && text[i + 1] === "/") {
      const end = text.indexOf("\n", i);
      i = end < 0 ? length : end;
      continue;
    }
    if (c === "/" && text[i + 1] === "*") {
      const end = text.indexOf("*/", i + 2);
      const stop = end < 0 ? length : end + 2;
      advanceLines(i, stop);
      i = stop;
      continue;
    }

    if (c === '"' || c === "'" || c === "@" || c === "$") {
      const literal = consumeLiteral(text, i);
      if (literal) {
        // An interpolated string's holes are real code, so they are tokenized in place; the
        // literal segments around them are not.
        for (const hole of literal.holes) {
          const holeLine = line + countNewlines(text.slice(0, hole.start).slice(lineStart));
          const holeLineStart = text.lastIndexOf("\n", hole.start - 1) + 1;
          for (const token of tokenize(
            text.slice(hole.start, hole.end),
            holeLine + lineOffset - 1,
            hole.start - holeLineStart,
            `${holeTag === undefined ? "" : holeTag}/${hole.start}`
          )) {
            tokens.push({ ...token, start: token.start + hole.start, end: token.end + hole.start });
          }
        }
        push("string", i, literal.end);
        advanceLines(i, literal.end);
        i = literal.end;
        continue;
      }
    }

    if (/[A-Za-z_]/.test(c) || (c === "@" && /[A-Za-z_]/.test(text[i + 1] || ""))) {
      let j = c === "@" ? i + 1 : i;
      while (j < length && /[A-Za-z0-9_]/.test(text[j])) {
        j++;
      }
      const word = text.slice(c === "@" ? i + 1 : i, j);
      // A verbatim identifier (`@class`) is an identifier, never the keyword it spells.
      push(c !== "@" && KEYWORDS.has(word) ? "keyword" : "identifier", i, j);
      i = j;
      continue;
    }

    if (/[0-9]/.test(c)) {
      let j = i + 1;
      const hexadecimal = /[xX]/.test(text[i + 1] || "") && c === "0";
      while (j < length && /[0-9A-Za-z_.]/.test(text[j])) {
        // A `.` only continues a number when a digit follows: `1.ToString()` is not `1.To`.
        if (text[j] === "." && !/[0-9]/.test(text[j + 1] || "")) {
          break;
        }
        if (!hexadecimal && /[eE]/.test(text[j]) && /[+-]/.test(text[j + 1] || "")) {
          j++;
        }
        j++;
      }
      push("number", i, j);
      i = j;
      continue;
    }

    if (c === "<" || c === ">") {
      push("angle", i, i + 1);
      i++;
      continue;
    }

    const punctuator = PUNCTUATORS.find((candidate) => text.startsWith(candidate, i));
    if (punctuator) {
      push("punctuation", i, i + punctuator.length);
      i += punctuator.length;
      continue;
    }

    push("punctuation", i, i + 1);
    i++;
  }

  return tokens;
}

function countNewlines(value) {
  let count = 0;
  for (let i = 0; i < value.length; i++) {
    if (value[i] === "\n") {
      count++;
    }
  }
  return count;
}

/**
 * Consumes a string or character literal starting at `start`, or returns null when `start` does not
 * begin one. `holes` are an interpolated string's interpolation expressions, which are code.
 */
function consumeLiteral(text, start) {
  let i = start;
  let interpolated = false;
  let verbatim = false;
  while (text[i] === "@" || text[i] === "$") {
    if (text[i] === "$") {
      interpolated = true;
    } else {
      verbatim = true;
    }
    i++;
  }
  const quote = text[i];
  if (quote !== '"' && quote !== "'") {
    return null;
  }

  // Raw string literal: the opening fence's length sets the closing fence's length.
  if (quote === '"' && text.startsWith('"""', i)) {
    let fence = 0;
    while (text[i + fence] === '"') {
      fence++;
    }
    const closing = '"'.repeat(fence);
    const found = text.indexOf(closing, i + fence);
    return { end: found < 0 ? text.length : found + fence, holes: [] };
  }

  i++;
  const holes = [];
  while (i < text.length) {
    const c = text[i];
    if (verbatim && c === quote && text[i + 1] === quote) {
      i += 2;
      continue;
    }
    if (!verbatim && c === "\\") {
      i += 2;
      continue;
    }
    if (interpolated && c === "{") {
      if (text[i + 1] === "{") {
        i += 2;
        continue;
      }
      const holeStart = i + 1;
      let depth = 1;
      i++;
      while (i < text.length && 0 < depth) {
        if (text[i] === "{") {
          depth++;
        } else if (text[i] === "}") {
          depth--;
          if (depth === 0) {
            break;
          }
        } else if (text[i] === '"' || text[i] === "'") {
          const nested = consumeLiteral(text, i);
          if (nested) {
            i = nested.end;
            continue;
          }
        }
        i++;
      }
      holes.push({ start: holeStart, end: i });
      i++;
      continue;
    }
    if (c === quote) {
      return { end: i + 1, holes };
    }
    if (!verbatim && c === "\n") {
      // Unterminated on this line: not a literal, rather than swallowing the rest of the file.
      return null;
    }
    i++;
  }
  return { end: i, holes };
}

/* --------------------------------------------------------------- analysis */

/**
 * Marks every `<`/`>` token index that opens or closes a type-argument list.
 *
 * The scan is deliberately permissive toward "this is a generic": `Foo(a < b, c > d)` is
 * type-argument shaped and will be read as one, which costs a conversion. Reading a real generic as
 * a comparison would cost a broken rewrite.
 */
function markGenerics(tokens) {
  const generic = new Set();
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (token.kind !== "angle" || token.text !== "<" || generic.has(i)) {
      continue;
    }
    const previous = tokens[i - 1];
    if (!previous) {
      continue;
    }
    const opensTypeArguments =
      previous.kind === "identifier" ||
      (previous.kind === "keyword" && TYPE_ARGUMENT_KEYWORDS.has(previous.text));
    if (!opensTypeArguments) {
      continue;
    }

    let depth = 0;
    let parentheses = 0;
    let matched = -1;
    for (let j = i; j < tokens.length; j++) {
      const inner = tokens[j];
      if (inner.kind === "angle" && inner.text === "<") {
        depth++;
        continue;
      }
      if (inner.kind === "angle" && inner.text === ">") {
        depth--;
        if (depth === 0 && parentheses === 0) {
          matched = j;
        }
        if (depth <= 0) {
          break;
        }
        continue;
      }
      if (inner.kind === "punctuation" && inner.text === "(") {
        parentheses++;
      } else if (inner.kind === "punctuation" && inner.text === ")") {
        parentheses--;
        // An unmatched `)` means the scan has left the expression that opened it, so this `<` was
        // a comparison: `if (a < b) Foo(c > d)`.
        if (parentheses < 0) {
          break;
        }
      }
      const allowed =
        inner.kind === "identifier" ||
        (inner.kind === "keyword" && TYPE_ARGUMENT_KEYWORDS.has(inner.text)) ||
        (inner.kind === "punctuation" && TYPE_ARGUMENT_PUNCTUATION.has(inner.text));
      if (!allowed) {
        break;
      }
    }
    if (matched < 0) {
      continue;
    }
    for (let k = i; k <= matched; k++) {
      if (tokens[k].kind === "angle") {
        generic.add(k);
      }
    }
  }
  return generic;
}

const EXPRESSION_TERMINATING_PUNCTUATION = new Set([")", "]", "++", "--"]);
/**
 * Contextual pattern keywords. `and > 5` as a comparison against a variable named `and` is not a
 * shape this repository has, so reading them as pattern connectives is the safe direction.
 */
const PATTERN_CONNECTIVES = new Set(["and", "or", "not"]);

/** Every `>` / `>=` used as a comparison, with the token index of its operator. */
function findViolations(tokens, generic) {
  const violations = [];
  for (let i = 0; i < tokens.length; i++) {
    const token = tokens[i];
    if (token.kind !== "angle" || token.text !== ">") {
      continue;
    }
    const next = tokens[i + 1];
    if (generic.has(i)) {
      continue;
    }
    // `>>`, `>>=`, `>>>` -- shifts, not comparisons. Consume the whole run.
    if (next && next.glued && next.kind === "angle" && next.text === ">") {
      while (tokens[i + 1] && tokens[i + 1].glued && tokens[i + 1].kind === "angle") {
        i++;
      }
      continue;
    }

    let operator = ">";
    let operatorEnd = token.end;
    if (next && next.glued && next.kind === "punctuation" && next.text === "=") {
      operator = ">=";
      operatorEnd = next.end;
    } else if (next && next.glued && next.kind === "punctuation" && next.text === "==") {
      // `>==` is not C#; leave it alone rather than guess.
      continue;
    }

    const left = tokens[i - 1];
    if (!left) {
      continue;
    }
    const endsExpression =
      (left.kind === "identifier" && !PATTERN_CONNECTIVES.has(left.text)) ||
      left.kind === "number" ||
      left.kind === "string" ||
      (left.kind === "keyword" && EXPRESSION_TERMINATING_KEYWORDS.has(left.text)) ||
      (left.kind === "punctuation" && EXPRESSION_TERMINATING_PUNCTUATION.has(left.text)) ||
      (left.kind === "angle" && left.text === ">" && generic.has(i - 1));
    if (!endsExpression) {
      continue;
    }
    violations.push({
      operator,
      operatorStart: token.start,
      operatorEnd,
      line: token.line,
      column: token.column,
      operatorIndex: i,
      operatorTokenEnd: operator === ">=" ? i + 1 : i
    });
  }
  return violations;
}

/* ------------------------------------------------------------------- fixer */

/**
 * Tokens that cannot belong to a relational operand at nesting depth zero. Relational operators
 * bind tighter than equality, `&`, `^`, `|`, `&&`, `||`, `??` and `?:`, and looser than `+`, `-`,
 * `*`, `/`, `%` and the shifts -- so the arithmetic operators are deliberately absent here.
 */
const OPERAND_BOUNDARY_PUNCTUATION = new Set([
  "(",
  ")",
  "[",
  "]",
  "{",
  "}",
  ",",
  ";",
  ":",
  "?",
  "=>",
  "==",
  "!=",
  "&&",
  "||",
  "??",
  "&",
  "|",
  "^",
  "=",
  "+=",
  "-=",
  "*=",
  "/=",
  "%=",
  "&=",
  "|=",
  "^=",
  "??=",
  "..."
]);
/** Keywords that introduce an expression without being part of it. */
const OPERAND_BOUNDARY_KEYWORDS = new Set([
  "return",
  "if",
  "while",
  "do",
  "for",
  "foreach",
  "switch",
  "case",
  "is",
  "as",
  "throw",
  "else",
  "in",
  "out",
  "ref",
  "lock",
  "using",
  "goto",
  "when",
  "yield",
  "params",
  "const"
]);

function isBoundary(token) {
  return (
    (token.kind === "punctuation" && OPERAND_BOUNDARY_PUNCTUATION.has(token.text)) ||
    (token.kind === "keyword" && OPERAND_BOUNDARY_KEYWORDS.has(token.text)) ||
    token.kind === "angle"
  );
}

/** The half-open token range `[start, end)` of the operand on one side of the operator. */
function operandRange(tokens, operatorIndex, operatorTokenEnd, direction) {
  const step = direction === "left" ? -1 : 1;
  const opens = direction === "left" ? [")", "]", "}"] : ["(", "[", "{"];
  const closes = direction === "left" ? ["(", "[", "{"] : [")", "]", "}"];
  let depth = 0;
  let i = direction === "left" ? operatorIndex - 1 : operatorTokenEnd + 1;
  let last = -1;
  const hole = tokens[operatorIndex].hole;
  while (0 <= i && i < tokens.length) {
    const token = tokens[i];
    // An operand cannot leave the interpolation hole it started in.
    if (token.hole !== hole) {
      break;
    }
    if (token.kind === "punctuation" && opens.includes(token.text)) {
      depth++;
      last = i;
      i += step;
      continue;
    }
    if (token.kind === "punctuation" && closes.includes(token.text)) {
      if (depth === 0) {
        break;
      }
      depth--;
      last = i;
      i += step;
      continue;
    }
    if (depth === 0 && isBoundary(token)) {
      break;
    }
    last = i;
    i += step;
  }
  if (last < 0 || 0 < depth) {
    return null;
  }
  return direction === "left"
    ? { start: last, end: operatorIndex }
    : { start: operatorTokenEnd + 1, end: last + 1 };
}

/**
 * True when evaluating the operand can be observed. Swapping is only unsafe when BOTH sides are
 * impure, because that is the only case where the order they run in is visible.
 */
function isImpure(tokens, range) {
  for (let i = range.start; i < range.end; i++) {
    const token = tokens[i];
    if (token.kind === "punctuation" && (token.text === "++" || token.text === "--")) {
      return true;
    }
    if (token.kind === "keyword" && (token.text === "new" || token.text === "stackalloc")) {
      return true;
    }
    if (token.kind === "identifier" && token.text === "await") {
      return true;
    }
    if (token.kind === "punctuation" && token.text === "(") {
      const previous = tokens[i - 1];
      const isInvocation =
        previous &&
        (previous.kind === "identifier" ||
          (previous.kind === "angle" && previous.text === ">") ||
          (previous.kind === "punctuation" && (previous.text === ")" || previous.text === "]")));
      if (isInvocation) {
        return true;
      }
    }
  }
  return false;
}

/** A rewrite for one violation, or a reason it needs a human. */
function planFix(text, tokens, generic, violation) {
  const left = operandRange(tokens, violation.operatorIndex, violation.operatorTokenEnd, "left");
  const right = operandRange(tokens, violation.operatorIndex, violation.operatorTokenEnd, "right");
  if (!left || !right) {
    return { reason: "operand boundary not resolvable" };
  }
  for (let i = left.start; i < right.end; i++) {
    if (tokens[i].kind === "angle" && !generic.has(i) && i !== violation.operatorIndex) {
      return { reason: "operand contains a shift or another comparison" };
    }
  }
  const start = tokens[left.start].start;
  const end = tokens[right.end - 1].end;
  const span = text.slice(start, end);
  if (span.includes("\n")) {
    return { reason: "comparison spans more than one line" };
  }
  if (span.includes("//") || span.includes("/*")) {
    return { reason: "comparison contains a comment" };
  }
  if (isImpure(tokens, left) && isImpure(tokens, right)) {
    return { reason: "both operands can have a side effect, so swapping changes evaluation order" };
  }
  const leftText = text.slice(tokens[left.start].start, tokens[left.end - 1].end);
  const rightText = text.slice(tokens[right.start].start, tokens[right.end - 1].end);
  const flipped = violation.operator === ">" ? "<" : "<=";
  return { start, end, replacement: `${rightText} ${flipped} ${leftText}` };
}

/* --------------------------------------------------------------------- cli */

/**
 * Trees vendored from upstream verbatim. Restyling them makes every future upstream diff harder to
 * read for no gain here -- the design is the original author's, and docs/project/third-party-notices.md
 * says so. Adapted sources (the sorts, the PRNGs) are this package's code and are NOT exempt.
 */
const VENDORED_DIRECTORIES = ["Runtime/Utils/SevenZip"];

function isVendored(file) {
  const relative = path.relative(REPO_ROOT, file).split(path.sep).join("/");
  return VENDORED_DIRECTORIES.some((directory) => relative.startsWith(`${directory}/`));
}

function collectCsFiles(root) {
  const out = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(full);
      } else if (entry.isFile() && entry.name.endsWith(".cs") && !isVendored(full)) {
        out.push(full);
      }
    }
  };
  if (fs.existsSync(root)) {
    walk(root);
  }
  return out.sort();
}

function analyzeFile(file) {
  const text = fs.readFileSync(file, "utf8");
  const tokens = tokenize(text);
  const generic = markGenerics(tokens);
  return { text, tokens, generic, violations: findViolations(tokens, generic) };
}

function main(argv) {
  const fix = argv.includes("--fix");
  const verbose = argv.includes("--verbose") || argv.includes("-VerboseOutput");
  const explicitFiles = argv.filter((argument) => argument.endsWith(".cs"));
  const files =
    0 < explicitFiles.length
      ? explicitFiles.map((file) => path.resolve(REPO_ROOT, file))
      : SCAN_ROOTS.flatMap((root) => collectCsFiles(path.resolve(REPO_ROOT, root)));

  let remaining = 0;
  let fixed = 0;
  const manual = [];
  for (const file of files) {
    const { text, tokens, generic, violations } = analyzeFile(file);
    if (violations.length === 0) {
      continue;
    }
    const relative = path.relative(REPO_ROOT, file).split(path.sep).join("/");
    const edits = [];
    for (const violation of violations) {
      const plan = fix ? planFix(text, tokens, generic, violation) : { reason: "not fixing" };
      if (fix && plan.replacement !== undefined) {
        edits.push(plan);
        fixed++;
        continue;
      }
      remaining++;
      const line = text.split("\n")[violation.line - 1] || "";
      const entry = `${relative}:${violation.line}:${violation.column}: '${violation.operator}' should read left-to-right -- ${line.trim()}`;
      manual.push(fix ? `${entry}\n    (${plan.reason})` : entry);
    }
    if (0 < edits.length) {
      edits.sort((a, b) => b.start - a.start);
      let updated = text;
      for (const edit of edits) {
        updated = updated.slice(0, edit.start) + edit.replacement + updated.slice(edit.end);
      }
      fs.writeFileSync(file, updated);
    }
  }

  if (fix) {
    console.log(
      `[comparison-direction] rewrote ${fixed} comparison(s) across ${files.length} file(s).`
    );
  }
  if (0 < remaining) {
    console.error(
      `[comparison-direction] ${remaining} comparison(s) do not read left-to-right. ` +
        "Use only '<' and '<=': swap the operands, not the meaning (issue #554)."
    );
    for (const entry of manual) {
      console.error(`  ${entry}`);
    }
    return 1;
  }
  if (verbose) {
    console.log(`[comparison-direction] ${files.length} file(s) clean.`);
  }
  return 0;
}

module.exports = { tokenize, markGenerics, findViolations, consumeLiteral, planFix, analyzeFile };

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
