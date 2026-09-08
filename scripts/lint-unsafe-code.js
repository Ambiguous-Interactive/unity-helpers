#!/usr/bin/env node

/*
    Refuses compiler-unsafe code and the switches that permit it.

    The shipped runtime assembly enabled allowUnsafeCode for two BMI2 branches behind
    NETCOREAPP3_0_OR_GREATER and NET7_0_OR_GREATER -- symbols no Unity player defines, so neither
    ever ran, and one of them only needed the flag because `unsafe` was on a method signature
    rather than around the dead block. A wrong pointer, layout or index assumption is undefined
    behaviour on IL2CPP, so the flag itself is the thing worth refusing
    (https://github.com/Ambiguous-Interactive/unity-helpers/issues/637).

    Scope is package-owned source only: Runtime/, Editor/ and Tests/ assembly definitions and C#
    sources, plus the Generator~ check projects, whose AllowUnsafeBlocks is what makes the local
    gate agree with Unity. Vendored trees are excluded by name below.

    It also refuses a `stackalloc` whose length is neither a compile-time constant nor guarded
    against one in the same statement. A span sized from an argument is bounded by whatever the
    caller passes, and overrunning the stack raises StackOverflowException, which no catch can
    intercept -- the process dies. Two such sites shipped: a caller-sized polygon in
    PointPolygonCheck and the Inspector's whole multi-selection in WButtonGUI.

    And it refuses the attribute that turns IL2CPP's own safety checks off, which reaches the same
    undefined behaviour with neither the keyword nor the asmdef flag. See findIl2CppSafetyDisables.
*/

"use strict";

const { existsSync, readFileSync } = require("node:fs");
const path = require("node:path");
const { spawnSync } = require("node:child_process");

const SOURCE_ROOTS = ["Runtime", "Editor", "Tests"];
const VENDORED_PREFIXES = ["Runtime/Utils/SevenZip/"];
const ISSUE = "see issue #637";
const CONST_DECLARATION = /\bconst\s+[\w.<>[\]]+\s+([A-Za-z_]\w*)\s*=/g;
const STACKALLOC = /\bstackalloc\s+[\w.<>,\s]*?\[/g;
const INTEGER_LITERAL = /^(?:0[xX][0-9a-fA-F]+|\d+)$/;
const IDENTIFIER = /^[A-Za-z_]\w*$/;

/*
    System.Runtime.CompilerServices.Unsafe members that READ, WRITE or REINTERPRET memory. A wrong
    type, layout or index assumption here is undefined behaviour on IL2CPP exactly as a raw pointer
    would be, and it needs no `unsafe` flag to compile -- so the asmdef check above cannot see it.

    The line is REINTERPRETATION, not the word "address". Banned are the members that manufacture
    or consume a reference the type system did not sanction -- As, AsRef, AsPointer, Unbox (which
    hands back a ref into a box, the same wrong-layout hazard with a friendlier spelling), NullRef,
    SkipInit, the offset arithmetic, and the Read/Write/Copy/InitBlock family.

    Permitted, matching #637's size-only allowance, are the pure predicates that cannot read, write
    or reinterpret anything: SizeOf, AreSame, ByteOffset, IsNullRef and the address comparisons.
    WProtoReader uses AreSame to ask whether two spans start at the same element, which is a
    question about identity, not an access.
*/
const UNSAFE_ACCESS =
  /\bUnsafe\s*\.\s*(?:As|AsRef|AsPointer|Unbox|NullRef|SkipInit|Add|Subtract|AddByteOffset|SubtractByteOffset|Read|ReadUnaligned|Write|WriteUnaligned|Copy|CopyBlock|CopyBlockUnaligned|InitBlock|InitBlockUnaligned)\b/g;

/*
    IL2CPP emits a null check on every reference dereference, a bounds check on every array index
    and a divide-by-zero check on every integer division, which is what makes a wrong assumption in
    managed code a catchable exception rather than undefined behaviour.
    `[Il2CppSetOption(Option.NullChecks, false)]` deletes them for whatever it decorates -- a type,
    a method or a property -- and what is left is not a faster correct program: a null dereference
    reads whatever address zero plus the field offset happens to hold, an out-of-range index writes
    past the array into the object next to it, and an integer divide by zero faults the process.
    That is precisely the hazard the `unsafe` rules above exist for, reached with neither the
    keyword nor `allowUnsafeCode`, so no other check in this file can see it
    (https://github.com/Ambiguous-Interactive/unity-helpers/issues/637).

    Zero such sites exist in the package, and that is the whole point of the rule -- there is
    nothing to baseline, so a single site is the first one.
*/
const IL2CPP_SET_OPTION = /\bIl2CppSetOption(?:Attribute)?\s*\(/g;

/*
    The three checks Unity's Option enum can switch off. Matched case-insensitively and with the
    `Option.` qualifier optional, so neither a `using static` alias nor a differently-cased
    re-declaration slips one past. Prose is safe without any of that care because codeOnly blanks
    every comment and string literal first, and #637's own text names all three.
*/
const IL2CPP_OPTION_NAME =
  /(?:\bOption\s*\.\s*)?\b(NullChecks|ArrayBoundsChecks|DivideByZeroChecks)\b/gi;

/*
    The cheap pre-filter, deliberately NOT global: `test` on a /g regex advances its lastIndex and
    leaves it there, so a shared one would start the next file mid-string and skip real sites.
*/
const IL2CPP_MENTION = /Il2CppSetOption|NullChecks|ArrayBoundsChecks|DivideByZeroChecks/i;

/*
    What is left of an argument once its option references are removed. `Option.NullChecks`,
    `option: Option.NullChecks` and `new[] { Option.NullChecks, Option.ArrayBoundsChecks }` all
    reduce to this, so an argument list matching it passes no value at all.
*/
const OPTION_ARGUMENT_RESIDUE = /^(?:\s|,|[{}()[\]]|new|Option)*$/;
const FALSE_LITERAL = /\bfalse\b/i;
const TRUE_LITERAL = /\btrue\b/i;
const ARGUMENT_LABEL = /^\s*[A-Za-z_]\w*\s*:(?!:)/;

/*
    A ratchet, not an allowlist. Both remaining files reinterpret an enum's underlying storage on a
    measured hot path, and #637 gates their safe replacement on the paired player evidence #636
    owes. Freezing the count means a new site anywhere reds the build, and retiring one of these
    reds it too until the number comes down -- so the exception cannot quietly grow or go stale.
*/
const UNSAFE_ACCESS_BASELINE = new Map([
  ["Runtime/Core/Extension/EnumExtensions.cs", 8],
  ["Runtime/Core/Serialization/WallstopProto/WProtoScalarFormatters.cs", 12]
]);

/** Blanks comments and string literals so the keyword scan cannot match prose. */
function codeOnly(text) {
  let output = "";
  let index = 0;
  while (index < text.length) {
    const character = text[index];
    const next = index + 1 < text.length ? text[index + 1] : "";
    if (character === "/" && next === "/") {
      while (index < text.length && text[index] !== "\n") {
        index++;
      }
      continue;
    }
    if (character === "/" && next === "*") {
      index += 2;
      while (index < text.length && !(text[index] === "*" && text[index + 1] === "/")) {
        output += text[index] === "\n" ? "\n" : " ";
        index++;
      }
      index += 2;
      continue;
    }
    if (character === '"' || character === "'") {
      const quote = character;
      const verbatim = 0 < index && text[index - 1] === "@";
      index++;
      while (index < text.length) {
        if (!verbatim && text[index] === "\\") {
          index += 2;
          continue;
        }
        if (text[index] === quote) {
          if (verbatim && text[index + 1] === quote) {
            index += 2;
            continue;
          }
          index++;
          break;
        }
        output += text[index] === "\n" ? "\n" : " ";
        index++;
      }
      continue;
    }
    output += character;
    index++;
  }
  return output;
}

/** Whether a path names a tree this package does not own. */
function isVendored(filePath) {
  const normalized = filePath.split(path.sep).join("/");
  return VENDORED_PREFIXES.some((prefix) => normalized.startsWith(prefix));
}

/** Every name declared `const` anywhere in the package, so a shared bound resolves. */
function collectConstantNames(sources) {
  const names = new Set();
  for (const source of sources) {
    const code = codeOnly(source.text);
    for (const match of code.matchAll(CONST_DECLARATION)) {
      names.add(match[1]);
    }
  }
  return names;
}

/** The text between the previous statement boundary and `index`. */
function statementBefore(code, index) {
  let start = index;
  while (
    0 < start &&
    code[start - 1] !== ";" &&
    code[start - 1] !== "{" &&
    code[start - 1] !== "}"
  ) {
    start--;
  }
  return code.slice(start, index);
}

/** The contents of the `[...]` opening at `openIndex`, or null when it never closes. */
function bracketedLength(code, openIndex) {
  let depth = 0;
  for (let index = openIndex; index < code.length; index++) {
    if (code[index] === "[") {
      depth++;
      continue;
    }
    if (code[index] !== "]") {
      continue;
    }
    depth--;
    if (depth === 0) {
      return code.slice(openIndex + 1, index).trim();
    }
  }
  return null;
}

/** Whether `length` cannot exceed a bound the compiler knows. */
function isConstantLength(length, constantNames) {
  /* `stackalloc T[] { a, b }` states its length by listing it, so the brackets are empty. */
  if (length.length === 0) {
    return true;
  }
  if (INTEGER_LITERAL.test(length)) {
    return true;
  }
  if (/^sizeof\s*\(/.test(length)) {
    return true;
  }
  if (IDENTIFIER.test(length)) {
    return constantNames.has(length);
  }
  const tail = length.includes(".") ? length.slice(length.lastIndexOf(".") + 1) : null;
  return tail !== null && IDENTIFIER.test(tail) && constantNames.has(tail);
}

/*
    A non-constant length is accepted only when the same statement compares it against a constant.
    Same statement rather than an enclosing block, because that is the only placement a reader can
    verify without tracing control flow -- and it is the shape the package already uses:
    `count <= Threshold ? stackalloc T[count] : default`, with a pooled rent on the other branch.
*/
function isGuardedLength(statement, length, constantNames) {
  if (!IDENTIFIER.test(length)) {
    return false;
  }
  const guard = new RegExp(`\\b${length}\\b\\s*<=?\\s*([\\w.]+)`);
  const match = guard.exec(statement);
  if (match !== null && isConstantLength(match[1], constantNames)) {
    return true;
  }
  const reversed = new RegExp(`([\\w.]+)\\s*<=?\\s*\\b${length}\\b`);
  const reverseMatch = reversed.exec(statement);
  return reverseMatch !== null && isConstantLength(reverseMatch[1], constantNames);
}

/**
 * @param {{path: string, text: string}[]} sources C# sources to inspect
 * @returns {{failures: string[], inspected: number}} violations, and how many sites were judged
 */
function findStackAllocations(sources) {
  const constantNames = collectConstantNames(sources);
  const failures = [];
  let inspected = 0;

  for (const source of sources) {
    if (isVendored(source.path)) {
      continue;
    }

    const code = codeOnly(source.text);
    for (const match of code.matchAll(STACKALLOC)) {
      const openIndex = match.index + match[0].length - 1;
      const length = bracketedLength(code, openIndex);
      if (length === null) {
        continue;
      }

      inspected++;
      if (isConstantLength(length, constantNames)) {
        continue;
      }
      if (isGuardedLength(statementBefore(code, match.index), length, constantNames)) {
        continue;
      }

      const line = code.slice(0, match.index).split("\n").length;
      failures.push(
        `${source.path}:${line}: stackalloc of '${length}', which no constant bounds. ` +
          `A caller-sized stack allocation raises StackOverflowException, which no catch ` +
          `intercepts; guard it against a const and rent above it, ${ISSUE}.`
      );
    }
  }

  return { failures, inspected };
}

/**
 * Holds the frozen per-file count of memory-access `Unsafe` members.
 *
 * @param {{path: string, text: string}[]} sources C# sources to inspect
 * @returns {{failures: string[], inspected: number, counted: Map<string, number>}} violations,
 *   how many sites were judged, and the per-file tally the staleness ratchet reads
 */
function findUnsafeAccess(sources) {
  const failures = [];
  const counted = new Map();
  let inspected = 0;

  for (const source of sources) {
    if (isVendored(source.path)) {
      continue;
    }

    const code = codeOnly(source.text);
    const sites = [...code.matchAll(UNSAFE_ACCESS)];
    if (sites.length === 0) {
      continue;
    }

    inspected += sites.length;
    counted.set(source.path, sites.length);
    const allowed = UNSAFE_ACCESS_BASELINE.get(source.path) ?? 0;
    if (sites.length <= allowed) {
      continue;
    }

    const line = code.slice(0, sites[allowed].index).split("\n").length;
    failures.push(
      `${source.path}:${line}: ${sites.length} memory-access Unsafe member(s), ` +
        `${allowed} baselined. Reinterpreting memory is undefined behaviour on IL2CPP if the ` +
        `type, layout or index assumption is wrong; use a checked cast, ${ISSUE}.`
    );
  }

  return { failures, inspected, counted };
}

/**
 * The other half of the ratchet: a baselined file that has shed sites must shed its excuse too.
 * It is separate from {@link findUnsafeAccess} because it is only meaningful over a corpus known
 * to be complete -- asked of one file it would report every other baselined file as retired.
 *
 * @param {Map<string, number>} counted per-file tally from a repository-wide scan
 * @returns {string[]} one message per stale baseline entry
 */
function findStaleUnsafeBaselines(counted) {
  const failures = [];
  for (const [file, allowed] of UNSAFE_ACCESS_BASELINE) {
    const actual = counted.get(file) ?? 0;
    if (actual < allowed) {
      failures.push(
        `${file}: baselined at ${allowed} memory-access Unsafe member(s) but has ${actual}. ` +
          `Lower the baseline so the exception cannot go stale, ${ISSUE}.`
      );
    }
  }
  return failures;
}

/** Every balanced `(...)` in `code`, each as its open index, close index and inner text. */
function parenthesizedRegions(code) {
  const open = [];
  const regions = [];
  for (let index = 0; index < code.length; index++) {
    if (code[index] === "(") {
      open.push(index);
      continue;
    }
    if (code[index] !== ")" || open.length === 0) {
      continue;
    }
    const start = open.pop();
    regions.push({ start, text: code.slice(start + 1, index), end: index });
  }
  return regions;
}

/** The innermost region enclosing `index`, or null when the position is outside every one. */
function innermostRegion(regions, index) {
  let found = null;
  for (const region of regions) {
    if (
      region.start < index &&
      index < region.end &&
      (found === null || found.start < region.start)
    ) {
      found = region;
    }
  }
  return found;
}

/**
 * Splits an argument list on its top-level commas. Angle brackets are deliberately not tracked:
 * `<` is a comparison as often as it is a type argument, and no argument this rule judges nests
 * a comma inside one.
 */
function splitArguments(text) {
  const parts = [];
  let depth = 0;
  let start = 0;
  for (let index = 0; index < text.length; index++) {
    const character = text[index];
    if (character === "(" || character === "[" || character === "{") {
      depth++;
      continue;
    }
    if (character === ")" || character === "]" || character === "}") {
      depth--;
      continue;
    }
    if (character === "," && depth === 0) {
      parts.push(text.slice(start, index));
      start = index + 1;
    }
  }
  parts.push(text.slice(start));
  return parts.filter((part) => 0 < part.trim().length);
}

/** Whether an argument only names options, so it carries no value of its own. */
function isOptionReferenceOnly(argument) {
  const withoutLabel = argument.replace(ARGUMENT_LABEL, " ");
  return OPTION_ARGUMENT_RESIDUE.test(withoutLabel.replace(IL2CPP_OPTION_NAME, " "));
}

/**
 * Judges one argument list. RED is only the DISABLING forms: the attribute exists to switch a
 * check off, so writing it with `true` -- or with no value at all -- leaves the check on and is
 * harmless, which is why some Unity projects carry one to record that a hot path was considered
 * and left checked. A value that is neither literal is RED too, because an attribute argument can
 * only be a constant and `const bool Fast = false;` is the same disable with a friendlier
 * spelling; the gate refuses what it cannot read at face value rather than assuming the kind one.
 *
 * @param {string} argumentText the text between the parentheses
 * @param {boolean} isSetOption whether the list belongs to an Il2CppSetOption attribute or ctor
 * @returns {{options: string[], value: string}|null} the finding, or null when nothing is disabled
 */
function judgeIl2CppArguments(argumentText, isSetOption) {
  const options = [...argumentText.matchAll(IL2CPP_OPTION_NAME)].map((match) => match[1]);
  if (options.length === 0 && !isSetOption) {
    return null;
  }

  const values = splitArguments(argumentText).filter((part) => !isOptionReferenceOnly(part));
  const disabling = values.find((value) => FALSE_LITERAL.test(value));
  if (disabling !== undefined) {
    return { options, value: disabling.trim() };
  }
  /* An Il2CppSetOption naming an option this gate does not know, with nothing false in it. */
  if (options.length === 0) {
    return null;
  }
  if (values.length === 0 || values.every((value) => TRUE_LITERAL.test(value))) {
    return null;
  }
  return { options, value: values.map((value) => value.trim()).join(", ") };
}

/**
 * Refuses source that switches IL2CPP's null, bounds or divide-by-zero checks off.
 *
 * The unit judged is one argument list, reached either from an `Il2CppSetOption` attribute or
 * constructor, or from any list naming one of the three options -- so a helper or an alias that
 * builds the attribute somewhere else is judged the same way as the attribute itself.
 *
 * @param {{path: string, text: string}[]} sources C# sources to inspect
 * @returns {{failures: string[], inspected: number}} violations, and how many lists were judged
 */
function findIl2CppSafetyDisables(sources) {
  const failures = [];
  let inspected = 0;

  for (const source of sources) {
    if (isVendored(source.path)) {
      continue;
    }

    const code = codeOnly(source.text);
    if (!IL2CPP_MENTION.test(code)) {
      continue;
    }

    const regions = parenthesizedRegions(code);
    const judged = new Map();
    for (const match of code.matchAll(IL2CPP_SET_OPTION)) {
      const start = match.index + match[0].length - 1;
      const region = regions.find((candidate) => candidate.start === start);
      if (region !== undefined) {
        judged.set(start, { region, isSetOption: true });
      }
    }
    for (const match of code.matchAll(IL2CPP_OPTION_NAME)) {
      const region = innermostRegion(regions, match.index);
      /* An option named outside every argument list passes no value, so it disables nothing. */
      if (region === null || judged.has(region.start)) {
        continue;
      }
      judged.set(region.start, { region, isSetOption: false });
    }

    for (const { region, isSetOption } of [...judged.values()].sort(
      (left, right) => left.region.start - right.region.start
    )) {
      inspected++;
      const finding = judgeIl2CppArguments(region.text, isSetOption);
      if (finding === null) {
        continue;
      }

      const line = code.slice(0, region.start).split("\n").length;
      const named =
        finding.options.length === 0
          ? "an IL2CPP safety check"
          : `IL2CPP ${[...new Set(finding.options)].join(", ")}`;
      failures.push(
        `${source.path}:${line}: '${finding.value}' switches off ${named}. Without those ` +
          `checks a null dereference, an out-of-range index and an integer divide by zero ` +
          `are undefined behaviour rather than exceptions; write the literal true or drop ` +
          `it, ${ISSUE}.`
      );
    }
  }

  return { failures, inspected };
}

/**
 * @param {{path: string, text: string}[]} asmdefs assembly definitions to inspect
 * @param {{path: string, text: string}[]} sources C# sources to inspect
 * @param {{path: string, text: string}[]} projects check projects to inspect
 * @returns {string[]} one message per violation
 */
function findViolations(asmdefs, sources, projects) {
  const failures = [];

  for (const asmdef of asmdefs) {
    let parsed;
    try {
      parsed = JSON.parse(asmdef.text);
    } catch (error) {
      failures.push(`${asmdef.path}: is not valid JSON (${error.message})`);
      continue;
    }
    if (parsed.allowUnsafeCode === true) {
      failures.push(
        `${asmdef.path}: "allowUnsafeCode": true. A shipped assembly must not permit pointer ` +
          `code; ${ISSUE}.`
      );
    }
  }

  for (const source of sources) {
    if (isVendored(source.path)) {
      continue;
    }

    const lines = codeOnly(source.text).split("\n");
    for (let index = 0; index < lines.length; index++) {
      if (!/(^|[^\w])unsafe([^\w]|$)/.test(lines[index])) {
        continue;
      }
      failures.push(
        `${source.path}:${index + 1}: 'unsafe'. Pointer access is undefined behaviour on IL2CPP ` +
          `when a layout or index assumption is wrong; ${ISSUE}.`
      );
    }
  }

  for (const project of projects) {
    if (/<AllowUnsafeBlocks>\s*true\s*<\/AllowUnsafeBlocks>/i.test(project.text)) {
      failures.push(
        `${project.path}: <AllowUnsafeBlocks>true</AllowUnsafeBlocks>. A check project ` +
          `permitting what Unity refuses is a gate that agrees with nothing; ${ISSUE}.`
      );
    }
  }

  failures.push(...findStackAllocations(sources).failures);
  failures.push(...findIl2CppSafetyDisables(sources).failures);

  return failures;
}

/*
    The IL2CPP rule's subject count is zero and must stay zero, so "it looked at nothing" and "it
    found nothing" are the same run by design and no per-subject count can separate them
    (honest-gates). The control is a positive one instead: the matcher is handed a site it must
    red, and a scan that cannot red THIS cannot red a real one either.
*/
const IL2CPP_CONTROL =
  "namespace Control\n{\n" +
  "    [Il2CppSetOption(Option.NullChecks, false)]\n" +
  "    internal sealed class Subject { }\n}\n";

/*
    Listed by directory and filtered here, never by a recursive-glob pathspec: git's double-star
    segment demands at least one directory level, so that shape silently skips every file sitting
    directly in the root -- `Runtime/WallstopStudios.UnityHelpers.asmdef` and
    `Runtime/AssemblyInfo.cs` among them. Measured: the first draft of this gate passed a tree
    whose runtime asmdef had allowUnsafeCode back on.
*/
function trackedWithExtension(repoRoot, roots, extension) {
  const present = roots.filter((root) => existsSync(path.join(repoRoot, root)));
  if (present.length === 0) {
    return [];
  }

  const result = spawnSync("git", ["-C", repoRoot, "ls-files", "-z", "--", ...present], {
    encoding: "utf8",
    maxBuffer: 64 * 1024 * 1024
  });
  if (result.status !== 0) {
    console.error(`[unsafe-code] git ls-files failed: ${result.stderr}`);
    process.exit(2);
  }
  return result.stdout
    .split("\0")
    .filter((entry) => entry.length !== 0 && entry.endsWith(extension))
    .map((entry) => ({ path: entry, text: readFileSync(path.join(repoRoot, entry), "utf8") }));
}

function main() {
  const repoRoot = path.resolve(__dirname, "..");
  const verbose = process.argv.includes("--verbose");
  const sources = trackedWithExtension(repoRoot, SOURCE_ROOTS, ".cs");
  const failures = findViolations(
    trackedWithExtension(repoRoot, SOURCE_ROOTS, ".asmdef"),
    sources,
    trackedWithExtension(repoRoot, ["Generator~"], ".csproj")
  );

  /*
      "Found no unbounded stackalloc" and "found no stackalloc" print the same clean run (#556), so
      the repository scan counts its subjects. A fixture legitimately has none, which is why this
      lives here rather than in findViolations.
  */
  const inspected = findStackAllocations(sources).inspected;
  if (inspected === 0) {
    failures.push(
      `no stackalloc site was judged across ${sources.length} source(s), so the scan saw ` +
        `none of what it exists to bound; ${ISSUE}.`
    );
  }

  const unsafeAccess = findUnsafeAccess(sources);
  failures.push(...unsafeAccess.failures, ...findStaleUnsafeBaselines(unsafeAccess.counted));
  if (unsafeAccess.inspected === 0) {
    failures.push(
      `no memory-access Unsafe member was judged across ${sources.length} source(s), so either ` +
        `the baseline is fully retired -- delete it and this check -- or the scan is broken; ` +
        `${ISSUE}.`
    );
  }

  const il2cppControl = findIl2CppSafetyDisables([
    { path: "control/Il2CppSetOption.cs", text: IL2CPP_CONTROL }
  ]);
  if (il2cppControl.failures.length === 0) {
    failures.push(
      `the IL2CPP control site was not reported, so a clean scan of the package proves nothing ` +
        `about whether an [Il2CppSetOption(Option.NullChecks, false)] would be seen; ${ISSUE}.`
    );
  }

  if (0 < failures.length) {
    for (const failure of failures) {
      console.error(`[unsafe-code] ${failure}`);
    }
    console.error(`[unsafe-code] ${failures.length} violation(s).`);
    process.exit(1);
  }

  if (verbose) {
    const il2cpp = findIl2CppSafetyDisables(sources).inspected;
    console.log(
      `[unsafe-code] No compiler-unsafe code or enabling switch found; ${inspected} stackalloc ` +
        `site(s) bounded; ${unsafeAccess.inspected} memory-access Unsafe member(s) at their ` +
        `frozen baseline; ${il2cpp} IL2CPP check option(s) set, control site reported.`
    );
  }
}

module.exports = {
  codeOnly,
  findViolations,
  isVendored,
  findStackAllocations,
  findUnsafeAccess,
  findStaleUnsafeBaselines,
  findIl2CppSafetyDisables
};

if (require.main === module) {
  main();
}
