#!/usr/bin/env node
/**
 * Every attribute this package reads by reflection AT RUNTIME must carry
 * [UnityEngine.Scripting.Preserve].
 *
 * The failure it guards against is silent and gameplay-shaped: if the IL2CPP managed linker drops
 * such an attribute, `GetCustomAttributes` finds nothing and the code reading it falls back to its
 * default. A [SingletonCreation(NeverCreate)] singleton starts conjuring empty stand-ins again; a
 * [ScriptableSingletonPath] asset is looked for in the wrong folder. Nothing throws, and nothing in
 * the editor reproduces it.
 *
 * The package's link.xml preserves the whole runtime assembly today, so this is defense in depth
 * rather than a live bug. It is worth having anyway: link.xml's own comment calls the wholesale
 * preserve a "low-maintenance choice", so a later narrowing must not silently re-open this, and
 * [Preserve] states the requirement at the declaration where the next reader is looking.
 *
 * Exit codes: 0 = all annotated, 1 = at least one is missing.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
const RUNTIME_ROOT = path.join(REPO_ROOT, "Runtime");

/**
 * Attribute types that are read reflectively but are NOT ours to annotate, with the reason.
 * An entry here is a claim that the type is declared outside this package.
 */
const NOT_OURS = new Map([
  ["ObsoleteAttribute", "System.ObsoleteAttribute"],
  ["JsonConverterAttribute", "System.Text.Json.Serialization.JsonConverterAttribute"],
  ["SerializeField", "UnityEngine.SerializeField"]
]);

function collectCsFiles(root) {
  const out = [];
  const walk = (dir) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(full);
      } else if (entry.isFile() && entry.name.endsWith(".cs")) {
        out.push(full);
      }
    }
  };
  walk(root);
  return out.sort();
}

/**
 * Attribute type names used as a reflection target anywhere under Runtime/.
 * Deliberately over-collects: a name that turns out not to be one of ours is answered by NOT_OURS
 * or by simply having no declaration in Runtime/, both of which are quiet.
 */
function findReflectedAttributeNames(files) {
  const pattern =
    /(?:TryGetAttributeSafe|IsAttributeDefined|GetCustomAttributes?|IsDefined)\s*(?:<\s*([A-Za-z0-9_]*Attribute)\s*>)?[^;]{0,300}?(?:out\s+([A-Za-z0-9_]*Attribute)\b|typeof\(\s*([A-Za-z0-9_]*Attribute)\s*\))/gs;
  const names = new Map();
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    let match;
    while ((match = pattern.exec(text)) !== null) {
      for (const candidate of [match[1], match[2], match[3]]) {
        if (!candidate || candidate === "TAttribute" || candidate === "Attribute") {
          continue;
        }
        if (!names.has(candidate)) {
          names.set(candidate, path.relative(REPO_ROOT, file));
        }
      }
    }
  }
  return names;
}

/**
 * Base attribute types whose subclasses are read reflectively through a generic `TAttribute`, which
 * the scan above cannot see: it only ever observes the type parameter's name. Each subclass is
 * therefore required to be preserved on the strength of its base.
 */
const REFLECTED_THROUGH_BASE = new Map([
  [
    "BaseRelationalComponentAttribute",
    "BaseRelationalComponentAttribute reads it via IsAttributeDefined<TAttribute>"
  ]
]);

/** Where each attribute type is declared under Runtime/, and whether that declaration is annotated. */
function findDeclarations(files) {
  const declarations = new Map();
  for (const file of files) {
    const text = fs.readFileSync(file, "utf8");
    const pattern =
      /((?:^[ \t]*\[[^\]]*\][ \t]*\r?\n)*)^[ \t]*(?:public|internal)[^\r\n]*?\bclass\s+([A-Za-z0-9_]+Attribute)\b/gm;
    let match;
    while ((match = pattern.exec(text)) !== null) {
      const declarationLine = text.slice(
        match.index,
        text.indexOf("\n", match.index + match[0].length)
      );
      const baseMatch = /:\s*([A-Za-z0-9_]+)/.exec(
        declarationLine.slice(match[0].length - match[2].length)
      );
      declarations.set(match[2], {
        file: path.relative(REPO_ROOT, file),
        preserved: /\[\s*Preserve\s*(?:\(|\])/.test(match[1]),
        baseType: baseMatch ? baseMatch[1] : null
      });
    }
  }
  return declarations;
}

function main() {
  const files = collectCsFiles(RUNTIME_ROOT);
  const reflected = findReflectedAttributeNames(files);
  const declarations = findDeclarations(files);

  const failures = [];
  const checked = [];

  // Subclasses of a base the package reflects over generically are reflected too.
  for (const [name, declaration] of declarations) {
    const reason = declaration.baseType ? REFLECTED_THROUGH_BASE.get(declaration.baseType) : null;
    if (reason && !reflected.has(name)) {
      reflected.set(name, reason);
    }
  }

  for (const [name, usedIn] of [...reflected.entries()].sort()) {
    const declaration = declarations.get(name);
    if (!declaration) {
      if (!NOT_OURS.has(name)) {
        // Not declared under Runtime/ and not declared as foreign: say so rather than pass quietly,
        // because a rule whose scope nobody states is a rule nobody can trust.
        failures.push(
          `${name} is read by reflection in ${usedIn} but is declared nowhere under Runtime/. ` +
            `If it belongs to another library, add it to NOT_OURS in ${path.relative(REPO_ROOT, __filename)} with its full name.`
        );
      }
      continue;
    }

    checked.push(name);
    if (!declaration.preserved) {
      failures.push(
        `${name} (${declaration.file}) is read by reflection at runtime from ${usedIn}, but is not marked ` +
          `[Preserve]. The IL2CPP linker may drop it, and the code reading it then silently falls back to its default.`
      );
    }
  }

  if (checked.length === 0) {
    console.error(
      "[lint-preserve-attributes] Found no runtime-reflected package attributes at all. " +
        "That is almost certainly a broken scan rather than a clean repository."
    );
    process.exit(1);
  }

  for (const name of checked) {
    console.log(`  OK   ${name}`);
  }

  if (failures.length > 0) {
    console.error("");
    for (const failure of failures) {
      console.error(`  FAIL ${failure}`);
    }
    console.error(`\n[lint-preserve-attributes] ${failures.length} attribute(s) need [Preserve].`);
    process.exit(1);
  }

  console.log(
    `\n[lint-preserve-attributes] ${checked.length} runtime-reflected attribute(s) are preserved.`
  );
}

main();
