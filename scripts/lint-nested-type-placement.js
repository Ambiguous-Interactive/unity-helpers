#!/usr/bin/env node
/**
 * One member ordering, enforced at 100% (#672), plus the nested-type placement rule it extends
 * (#575): a nested type is declared at the END of its containing type, never between members.
 *
 * The member ordering is the owner's, from #672. Every tier below is ordered public → protected →
 * internal → private, including const:
 *
 *   1. const
 *   2. static properties
 *   3. static fields
 *   4. properties
 *   5. fields
 *   6. constructors
 *   7. static methods
 *   8. methods
 *
 * Two deliberate details the issue records so an implementer does not "fix" them back: static
 * properties come before static fields and properties before fields (the reverse of StyleCop's
 * SA1201 default), and const takes the accessibility ordering too. Nested types are resolved to
 * stay LAST (issue option 1): this gate already enforces that, 1918 files were clean under it, and
 * moving nested types first would invert a shipped rule and every file it swept. The order does
 * not name events or delegates; rather than leave 41 members in 67 files unenforceable, they take
 * one tier of their own immediately after const -- declared surface (an event handler, a delegate
 * shape) reads like the consts it accompanies, and every other choice measured moved more code.
 *
 * The rule is the owner's, from review on #574: "Please place class/struct definitions at the end
 * of the file, or in their own file, do not intersperse with code." A reader scrolling a type for
 * a method should not have to step over a nested type to find it, and a type declaration in the
 * middle of a body reads as the start of a new file's worth of content. Recorded in
 * .llm/context.md (rule 6) and create-csharp-file (rule 5b); this is what stops it decaying (#575).
 *
 * Both rules tokenize rather than grep, because the interesting words are not always declarations:
 *   * `where T : class` and `where T : struct` are constraints, not nested types.
 *   * `record` is contextual and is a perfectly good field or parameter name.
 *   * `[Attr(new[] { 1, 2 })]` puts a brace before the member's own body brace.
 *   * a field initializer -- `private int[] _a = { 1, 2 };` -- does the same.
 *   * `private (int X, int Y) _point;` opens a tuple type, not a parameter list, so a member whose
 *     declaration prefix breaks at `(` is only a method when the token before the paren is an
 *     identifier (or `>` of a generic, or `operator`).
 * Parameter lists, attribute sections and bracketed types are skipped as balanced spans, and
 * comments and string literals are masked to spaces first so a brace inside either cannot count.
 *
 * The ordering comparison resets at a conditional boundary: members on opposite sides of an
 * `#if`/`#else` pair are never compared, because the compiler sees at most one of them. A member
 * that does not sit wholly inside ONE conditional region is reported but never rewritten. A member
 * inside `#if UNITY_EDITOR` is compiled only there, so moving it past the `#endif` changes which
 * build sees it -- and a slice that carries an unbalanced `#if` lands the directive mid-line, where
 * C# refuses it outright. Two files in the #575 sweep hit exactly that. Members are reordered only
 * when every one of them opens and closes in the same region, which leaves a conditional wholly
 * inside a member (the common `#if UNITY_EDITOR` inside a method) free to move with it.
 *
 * `--fix` reorders members into the canonical order and moves each nested type to the end of its
 * containing type body. The rewrite is a PERMUTATION of exact source slices: every member carries
 * its own leading trivia (doc comment, attributes) and its trailing same-line comment, the slices
 * tile the body with no gaps, and the rewritten file is asserted to have the same length as the
 * original before it is written. A fix that changes a byte of anything other than order is a bug,
 * and the length check is what says so.
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
      const raw = verbatim || interpolatedVerbatim;
      const hasHoles = interpolated || interpolatedVerbatim;
      const quote = text.indexOf('"', index);
      let cursor = quote + 1;
      // Interpolated strings carry C# code inside `{...}` holes, and that code can hold nested
      // strings -- `"{(x == null ? "(null)" : $"\"{y}\"")}"` -- whose quotes would otherwise end
      // the outer string early and leak braces into the balance. Track hole depth: inside a hole,
      // strings (plain, char, and nested interpolated) are skipped whole, `{` deepens and `}`
      // surfaces; outside one, `\\` escapes, `{{` is a literal brace, and the closing quote ends
      // the string.
      let holeDepth = 0;
      while (cursor < text.length) {
        const c = text[cursor];
        if (holeDepth === 0) {
          if (raw) {
            if (c === '"' && text[cursor + 1] === '"') {
              cursor += 2;
              continue;
            }
          } else if (c === "\\") {
            cursor += 2;
            continue;
          }
          if (c === '"') {
            break;
          }
          if (hasHoles && c === "{") {
            if (text[cursor + 1] === "{") {
              cursor += 2;
              continue;
            }
            holeDepth += 1;
            cursor += 1;
            continue;
          }
          if (!raw && c === "\n") {
            break;
          }
          cursor += 1;
          continue;
        }
        if (c === "'") {
          let nested = cursor + 1;
          while (nested < text.length && text[nested] !== "'" && text[nested] !== "\n") {
            nested += text[nested] === "\\" ? 2 : 1;
          }
          cursor = nested + 1;
          continue;
        }
        if (c === '"') {
          let nested = cursor + 1;
          while (nested < text.length) {
            if (!raw && text[nested] === "\\") {
              nested += 2;
              continue;
            }
            if (raw && text[nested] === '"' && text[nested + 1] === '"') {
              nested += 2;
              continue;
            }
            if (text[nested] === '"' || text[nested] === "\n") {
              break;
            }
            nested += 1;
          }
          cursor = nested + 1;
          continue;
        }
        if (c === "{") {
          holeDepth += 1;
          cursor += 1;
          continue;
        }
        if (c === "}") {
          holeDepth -= 1;
          cursor += 1;
          continue;
        }
        if (c === "\n") {
          break;
        }
        cursor += 1;
      }
      blank(index, Math.min(cursor + 1, text.length));
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
      // Extend through an expression tail: a body-opening brace is not always the end of the
      // statement. `} = 5;` (accessor initializer), `}.ToImmutableHashSet();` (object-initializer
      // chain), `});` (initializer inside a call argument) all continue past the brace. A member
      // declared next can never begin with one of these characters, so they always mean "the
      // statement goes on": scan to the terminating `;` at bracket depth zero.
      let tailCursor = close;
      for (let guard = 0; guard < 8; guard += 1) {
        let after = tailCursor;
        while (after < bodyEnd && /\s/.test(masked[after])) {
          after += 1;
        }
        const char = masked[after];
        if (char === ";") {
          tailCursor = after + 1;
          break;
        }
        if (!".)+=,?:]".includes(char) || char === undefined) {
          break;
        }
        let depth = 0;
        let scan = after;
        let reached = false;
        while (scan < bodyEnd) {
          const c = masked[scan];
          if (c === "(" || c === "[" || c === "{") {
            const inner = skipBalanced(masked, scan);
            if (inner < 0) {
              return null;
            }
            scan = inner;
            continue;
          }
          if (c === ";" && depth === 0) {
            tailCursor = scan + 1;
            reached = true;
            break;
          }
          scan += 1;
        }
        if (!reached) {
          return null;
        }
      }
      let after = tailCursor;
      while (after < bodyEnd && /\s/.test(masked[after])) {
        after += 1;
      }
      if (masked[after] === ";") {
        end = after + 1;
      } else {
        end = tailCursor;
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
      name: isType ? match[2] : null
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

/**
 * The #672 member ordering. Each tier is ordered public → protected → internal → private, including
 * const; `none` is the absence of an access modifier (a static constructor, an implicitly-local
 * member) and sorts after every named access within its tier.
 */
const ORDER_TIERS = Object.freeze([
  "const",
  "event",
  "delegate",
  "static property",
  "static field",
  "property",
  "field",
  "constructor",
  "static method",
  "method"
]);
const TIER_RANK = new Map(ORDER_TIERS.map((tier, index) => [tier, index]));
const ACCESS_RANK = Object.freeze({ public: 0, protected: 1, internal: 2, private: 3, none: 4 });

const MODIFIER_KEYWORDS = new Set([
  "public",
  "protected",
  "internal",
  "private",
  "static",
  "readonly",
  "const",
  "volatile",
  "new",
  "virtual",
  "override",
  "abstract",
  "sealed",
  "unsafe",
  "extern",
  "async",
  "partial",
  "required",
  "file",
  "ref",
  "event"
]);

/**
 * Scans a member's declaration prefix: everything from the first token through the character that
 * opens its body or terminates its header. Balanced `[...]` spans (attributes, an indexer's
 * parameter brackets) are skipped whole. Returns the trimmed prefix and the break character, or
 * null when a span never closes (the member is reported through the placement rules instead).
 */
function declarationPrefix(masked, start, end) {
  let scan = start;
  while (scan < end) {
    const char = masked[scan];
    if (char === "{" || char === "(" || char === "=" || char === ";") {
      return { prefix: masked.slice(start, scan).trim(), breakChar: char };
    }
    if (char === "[") {
      const close = skipBalanced(masked, scan);
      if (close < 0) {
        return null;
      }
      scan = close;
      continue;
    }
    scan += 1;
  }
  return { prefix: masked.slice(start, end).trim(), breakChar: "" };
}

/**
 * True when `=` at `index` starts a field initializer rather than being part of `==`, `=>`,
 * `+=`, `<=` or `>=`.
 */
function isPlainEquals(masked, index) {
  const previous = masked[index - 1];
  if (previous && "=!<>=+-*/%&|^?:".includes(previous)) {
    return false;
  }
  return masked[index + 1] !== "=" && masked[index + 1] !== ">";
}

/**
 * Property or field? Both end in `;` and can both carry an initializer, so the first body-opening
 * token decides: an accessor list `{` or an arrow `=>` makes a property, a real `=` initializer or
 * a bare `;` makes a field. A method never reaches here (it broke at `(`), and an expression-bodied
 * method's `(` always precedes its arrow.
 */
function accessorKind(masked, start, end) {
  for (let scan = start; scan < end; scan += 1) {
    const char = masked[scan];
    if (char === "[") {
      const close = skipBalanced(masked, scan);
      if (close < 0) {
        return null;
      }
      scan = close - 1;
      continue;
    }
    if (char === "=") {
      if (masked[scan + 1] === ">") {
        return "property";
      }
      if (isPlainEquals(masked, scan)) {
        return "field";
      }
      continue;
    }
    if (char === "{") {
      return "property";
    }
    if (char === ";") {
      return "field";
    }
  }
  return "field";
}

/** The identifier (or `~Identifier`) immediately before the member's opening paren, if any. */
function callableNameBefore(masked, parenIndex) {
  let cursor = parenIndex - 1;
  while (0 <= cursor && /\s/.test(masked[cursor])) {
    cursor -= 1;
  }
  if (0 <= cursor && masked[cursor] === ">") {
    return ">";
  }
  let end = cursor + 1;
  while (0 <= cursor && /[A-Za-z0-9_]/.test(masked[cursor])) {
    cursor -= 1;
  }
  if (0 <= cursor && masked[cursor] === "~") {
    cursor -= 1;
  }
  const name = masked.slice(cursor + 1, end);
  return /^[A-Za-z_]\w*$/.test(name) && !MODIFIER_KEYWORDS.has(name) ? name : null;
}

/**
 * Classifies one non-type member into its ordering tier and access, exempt, or unclassifiable.
 * `typeName` is the containing type's name; a declaration prefix whose last identifier is that
 * name (or `~` + that name) is a constructor.
 */
function classifyMember(masked, member, typeName) {
  const prefix = declarationPrefix(masked, member.headerStart, member.end);
  if (prefix === null) {
    return { unclassifiable: true };
  }
  const access = /\bpublic\b/.test(prefix.prefix)
    ? "public"
    : /\bprotected\b/.test(prefix.prefix)
      ? "protected"
      : /\binternal\b/.test(prefix.prefix)
        ? "internal"
        : /\bprivate\b/.test(prefix.prefix)
          ? "private"
          : "none";
  const isStatic = /\bstatic\b/.test(prefix.prefix);
  const isConst = /\bconst\b/.test(prefix.prefix);

  if (/\bevent\b/.test(prefix.prefix)) {
    return { tier: "event", access };
  }
  if (/\bdelegate\b/.test(prefix.prefix)) {
    return { tier: "delegate", access };
  }

  if (isConst) {
    return { tier: "const", access };
  }

  if (prefix.breakChar === "(") {
    if (/(\boperator\b|\bimplicit\b|\bexplicit\b)/.test(prefix.prefix)) {
      return { tier: isStatic ? "static method" : "method", access };
    }
    // Find the paren that terminated the prefix: the first top-level `(` after the header.
    let paren = -1;
    for (let scan = member.headerStart; scan < member.end; scan += 1) {
      if (masked[scan] === "(") {
        paren = scan;
        break;
      }
      if (masked[scan] === "[") {
        const close = skipBalanced(masked, scan);
        if (close < 0) {
          break;
        }
        scan = close - 1;
        continue;
      }
    }
    const callable = 0 <= paren ? callableNameBefore(masked, paren) : null;
    if (callable === null) {
      // A type-position paren: a tuple-typed member. Property or field, per its accessor.
      const kind = accessorKind(masked, member.headerStart, member.end);
      return kind === null
        ? { unclassifiable: true }
        : {
            tier:
              kind === "property"
                ? isStatic
                  ? "static property"
                  : "property"
                : isStatic
                  ? "static field"
                  : "field",
            access
          };
    }
    const lastIdent = /~?[A-Za-z_]\w*\s*$/.exec(prefix.prefix)?.[0]?.trim();
    if (lastIdent !== null && (lastIdent === typeName || lastIdent === `~${typeName}`)) {
      return { tier: "constructor", access };
    }
    return { tier: isStatic ? "static method" : "method", access };
  }

  if (prefix.breakChar === "{") {
    return { tier: isStatic ? "static property" : "property", access };
  }

  const kind = accessorKind(masked, member.headerStart, member.end);
  if (kind === null) {
    return { unclassifiable: true };
  }
  if (kind === "property") {
    return { tier: isStatic ? "static property" : "property", access };
  }
  return { tier: isStatic ? "static field" : "field", access };
}

/** The ordering rank used for both comparison and the stable reordering. */
function memberRank(masked, member, typeName) {
  const classified = classifyMember(masked, member, typeName);
  if (classified.exempt || classified.unclassifiable) {
    return classified;
  }
  return {
    ...classified,
    rank: TIER_RANK.get(classified.tier) * 8 + ACCESS_RANK[classified.access]
  };
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

/** The member's declaring identifier, for the violation message. */
function memberName(masked, member) {
  const prefix = declarationPrefix(masked, member.headerStart, member.end);
  if (prefix === null) {
    return null;
  }
  if (prefix.breakChar === "(") {
    // The name sits before the paren -- unless a tuple type opened it
    // (`private (int X, int Y) _point;`), in which case it sits after the span.
    let paren = -1;
    for (let scan = member.headerStart; scan < member.end; scan += 1) {
      if (masked[scan] === "(") {
        paren = scan;
        break;
      }
      if (masked[scan] === "[") {
        const inner = skipBalanced(masked, scan);
        if (inner < 0) {
          return null;
        }
        scan = inner - 1;
        continue;
      }
    }
    if (0 <= paren && callableNameBefore(masked, paren) !== null) {
      return /~?[A-Za-z_]\w*\s*$/.exec(prefix.prefix)?.[0]?.trim() ?? null;
    }
    let scan = 0 <= paren ? skipBalanced(masked, paren) : member.headerStart;
    if (scan < 0) {
      return null;
    }
    while (scan < member.end && /\s/.test(masked[scan])) {
      scan += 1;
    }
    const after = /^[A-Za-z_]\w*/.exec(masked.slice(scan, member.end));
    return after === null ? null : after[0];
  }
  const last = /~?[A-Za-z_]\w*\s*$/.exec(prefix.prefix);
  return last === null ? null : last[0].trim();
}

/**
/**
 * If a member's slice opens with full directive lines (`#if`, `#endif`, `#pragma`, ... -- the
 * leading trivia `membersOf` fused onto it), returns the index just past the last such line, so
 * the fixer can hold the directives in place while the member itself reorders. Otherwise 0.
 */
function directivePrefixEnd(text, start, end) {
  let cursor = start;
  let last = 0;
  while (cursor < end) {
    let scan = cursor;
    while (
      scan < end &&
      (text[scan] === " " || text[scan] === "\t" || text[scan] === "\r" || text[scan] === "\n")
    ) {
      scan += 1;
    }
    if (scan < end && text[scan] === "#") {
      const newline = text.indexOf("\n", scan);
      if (newline < 0 || end <= newline) {
        return last;
      }
      cursor = newline + 1;
      last = cursor;
      continue;
    }
    break;
  }
  return last;
}

/**
 * Every violation in one file, plus the rewrite that removes them.
 *
 * Two rules produce violations here: the #672 member ordering and the #575 nested-type placement.
 * When every member of a body sits in one conditional region and classifies cleanly, one edit
 * reorders the whole body (members stable-sorted into the canonical tiers, nested types appended
 * after them). Otherwise the ordering is still compared -- with the baseline reset at every
 * conditional boundary, so members on opposite sides of an `#if` are never compared -- and the
 * nested types fall back to the targeted move that keeps every `#if` member in place.
 */
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

    // --- #672 member ordering -------------------------------------------------------------
    const orderedMembers = members.filter((member) => !member.isType && !member.trailing);
    let orderingOffenders = [];
    let unclassifiable = 0;

    let previousRank = -1;
    let previousRegion = null;
    let previousTier = null;
    let previousAccess = null;
    for (const member of orderedMembers) {
      const classified = memberRank(masked, member, body.name);
      if (classified.unclassifiable) {
        unclassifiable += 1;
        continue;
      }
      const region = keys[member.headerStart];
      if (region !== previousRegion) {
        previousRank = -1;
        previousRegion = region;
      }
      if (0 <= previousRank && classified.rank < previousRank) {
        violations.push({
          line: lineOf(text, member.headerStart),
          kind: classified.tier,
          access: classified.access,
          name: memberName(masked, member),
          container: body.name,
          after: previousTier,
          afterAccess: previousAccess
        });
        orderingOffenders.push({ member, rank: classified.rank });
      }
      // Adjacent comparison: each member is judged against the one before it, which is what the
      // violation message claims. A scrambled body reports each adjacent descent; the fix
      // reorders the body once.
      previousRank = classified.rank;
      previousTier = classified.tier;
      previousAccess = classified.access;
    }
    if (unclassifiable !== 0) {
      violations.push({
        line: lineOf(text, body.start),
        kind: "unclassified member",
        name: null,
        container: body.name
      });
    }

    const hasTypeOffenders = (() => {
      const lastNonType = members.reduce(
        (found, member, index) => (member.isType || member.trailing ? found : index),
        -1
      );
      return members.some((member, index) => member.isType && index < lastNonType);
    })();
    if (orderingOffenders.length === 0 && !hasTypeOffenders) {
      continue;
    }

    if (0 < orderingOffenders.length) {
      if (edits.some((edit) => edit.start <= body.start && body.end <= edit.end)) {
        continue;
      }
      // Reorder the body in RUNS. A run is a maximal stretch of classifiable non-type members
      // between barriers; everything else -- a nested type, a trailing `#endif`, a `#pragma
      // warning restore`, a member that straddles an `#if` boundary -- is a barrier that keeps
      // its exact position, because moving it changes which build compiles it or the scope of
      // what it scopes. Directives fused into a member's leading trivia are split into their own
      // barrier chunk first, so the member itself can still sort within its side of the
      // conditional. Each run stable-sorts into the canonical tiers, the permutation never
      // crosses a directive line, and the pieces still tile the body byte for byte.
      let changed = false;
      const pieces = [];
      let run = [];
      const flushRun = () => {
        if (1 < run.length) {
          const sorted = [...run]
            .map((member, index) => ({
              member,
              index,
              rank: memberRank(masked, member, body.name).rank
            }))
            .sort((a, b) => a.rank - b.rank || a.index - b.index)
            .map((entry) => entry.member);
          if (sorted.some((member, index) => member !== run[index])) {
            changed = true;
          }
          pieces.push(...sorted);
        } else {
          pieces.push(...run);
        }
        run = [];
      };
      // Static members whose initializer runs real code at type-initialization time are
      // initialization-order hazards: static initializers execute in textual order, so moving
      // such a member past another static member it can read changes what is null when it runs
      // (#672: `Instance = new()` before the permutation table it copies was a standalone-leg
      // NRE). A self-typed construction, or an initializer that names another member of this
      // body, is treated as able to read any of them and becomes a barrier.
      const staticMemberNames = [];
      for (const member of members) {
        if (member.isType || member.trailing) continue;
        const prefix = declarationPrefix(masked, member.headerStart, member.end);
        if (!prefix) continue;
        const name = /([A-Za-z_]\w*)\s*(<[^<>]*>)?\s*$/.exec(prefix.prefix)?.[1];
        if (name) {
          staticMemberNames.push(name);
        }
      }
      const initializesAtTypeLoad = (member) => {
        const prefix = declarationPrefix(masked, member.headerStart, member.end);
        if (!prefix || !/\bstatic\b/.test(prefix.prefix) || prefix.breakChar === "(") {
          return false;
        }
        const slice = masked.slice(member.headerStart, member.end);
        const match = /[^=!<>]([+|&^]?|\|\||&&)=([^=>]|$)/.exec(slice);
        if (!match) return false;
        const eq = match.index + 1;
        const init = slice.slice(eq);
        const name = /([A-Za-z_]\w*)\s*(<[^<>]*>)?\s*$/.exec(prefix.prefix)?.[1] ?? "";
        const declaredType = prefix.prefix.slice(
          0,
          Math.max(0, prefix.prefix.lastIndexOf(name))
        );
        const selfTyped =
          new RegExp("\\bnew\\s+" + body.name + "\\b").test(init) ||
          (/=\s*new\s*\(\s*\)/.test(init) && declaredType.includes(body.name));
        const readsSibling =
          !selfTyped &&
          staticMemberNames.some(
            (sibling) => sibling !== name && new RegExp("\\b" + sibling + "\\b").test(init)
          );
        return selfTyped || readsSibling;
      };

      for (const member of members) {
        const directiveEnd = directivePrefixEnd(text, member.start, member.end);
        if (0 < directiveEnd) {
          const stripped = text.slice(member.start, directiveEnd);
          const last = 0 < run.length ? run[run.length - 1] : pieces[pieces.length - 1];
          // A `#pragma warning restore` closes a disable that wraps the PREVIOUS member; fold it
          // back into that member so the pair travels as one unit and the member after it can
          // still sort. Any other directive is a barrier that stays exactly where it is.
          const closesWrappedMember =
            /^\s*#pragma\s+warning\s+restore\b/.test(stripped) &&
            last !== undefined &&
            !("raw" in last) &&
            /#pragma\s+warning\s+disable\b/.test(text.slice(last.start, last.end));
          if (closesWrappedMember) {
            last.end = directiveEnd;
          } else {
            flushRun();
            pieces.push({ raw: stripped });
          }
        }
        const start = 0 < directiveEnd ? directiveEnd : member.start;
        const slice = text.slice(start, member.end);
        const movable =
          !member.isType &&
          !member.trailing &&
          slice.trim().length !== 0 &&
          !/^[ \t]*#/.test(slice) &&
          keys[start] === keys[Math.max(member.end - 1, 0)] &&
          !memberRank(masked, member, body.name).unclassifiable &&
          !initializesAtTypeLoad(member);
        if (movable) {
          run.push({ ...member, start });
        } else {
          flushRun();
          if (start < member.end) {
            pieces.push({ ...member, start });
          }
        }
      }
      flushRun();
      if (changed) {
        edits.push({
          start: body.start,
          end: body.end,
          replacement: pieces
            .map((piece) => ("raw" in piece ? piece.raw : text.slice(piece.start, piece.end)))
            .join("")
        });
      }
      continue;
    }

    // --- #575 nested-type placement (targeted fallback) -----------------------------------
    if (!hasTypeOffenders) {
      continue;
    }
    const lastNonType = members.reduce(
      (found, member, index) => (member.isType || member.trailing ? found : index),
      -1
    );
    const offenders = members.filter((member, index) => member.isType && index < lastNonType);
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
      if (violation.kind === "unclassified member") {
        remaining.push(
          `${relative}:${violation.line}: a member of '${violation.container}' defeats the ` +
            `declaration parser and cannot be classified for the member ordering; classify it ` +
            `by hand or extend the parser, and never let a file pass on a silent skip`
        );
        continue;
      }
      if (ORDER_TIERS.includes(violation.kind)) {
        const described = (tier, access) =>
          access === null || access === "none" ? tier : `${access} ${tier}`;
        remaining.push(
          `${relative}:${violation.line}: ${described(violation.kind, violation.access)} ` +
            `'${violation.name}' is declared after a ${described(violation.after, violation.afterAccess)} ` +
            `in '${violation.container}' (#672)`
        );
        continue;
      }
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
      `[nested-type-placement] ${remaining.length} member-placement violation(s). ` +
        "Reorder members into the #672 ordering (const, static properties, static fields, " +
        "properties, fields, constructors, static methods, methods; each tier public → " +
        "protected → internal → private) and move nested types to the end of their containing " +
        "type (issue #575)."
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

module.exports = {
  ORDER_TIERS,
  maskNoise,
  regionKeys,
  membersOf,
  typeBodies,
  analyzeFile,
  applyEdits,
  declarationPrefix,
  accessorKind,
  classifyMember,
  memberRank
};

if (require.main === module) {
  process.exit(main(process.argv.slice(2)));
}
