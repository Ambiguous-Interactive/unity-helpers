#!/usr/bin/env node
/**
 * A MonoBehaviour declared in an Editor-only assembly may not arrive by accident.
 *
 * Unity refuses to add a MonoBehaviour it can IDENTIFY as an editor script:
 *
 *     Can't add script behaviour 'RegularMonoBehaviour' because it is an editor script.
 *
 * Identification runs through the `MonoScript`, and a `MonoScript` binds by FILE NAME -- so a type
 * nested inside a fixture, or sharing a file with a differently-named type, has no `MonoScript`,
 * escapes the policy, and `AddComponent` works. Eleven tests stood on that loophole without knowing
 * it; the one-type-per-file sweep (#666) gave each double a correctly-named file, Unity could
 * finally classify them, and all eleven went red on every editor version. Three CI matrices (#677)
 * were spent finding that out, because every cheap signal reads healthy: the type compiles and
 * loads, `LoadAssetAtPath` returns the `MonoScript`, `GetClass()` returns the right type, and only
 * `AddComponent` disagrees, by returning null. `typecheck` compiles the file happily, and the
 * owner's own editor cannot reproduce it -- there NO editor-assembly MonoBehaviour adds at all, so
 * the MCP bridge cannot discriminate either. Only the CI matrix can
 * ([#678](https://github.com/Ambiguous-Interactive/unity-helpers/issues/678)).
 *
 * WHERE THIS DETECTS. Statically, from `.asmdef` `includePlatforms` plus folder ownership, rather
 * than from inside Unity. A third rule in `MonoScriptBindingValidator` would be more accurate about
 * what the editor actually does, and it would run only in the matrix -- which is the cost this gate
 * exists to avoid paying. Nothing about the rule needs the editor: "which asmdef owns this file"
 * and "does this class derive from MonoBehaviour" are both answerable from the tree, and the asmdef
 * walk is `lint-typecheck-asmdef-references.js`'s, delegated rather than copied.
 *
 * WHAT TRIGGERS IT. The DECLARATION, with an allowlist -- not the `AddComponent` call site. A
 * use-triggered gate would have to see through `AddComponent(someTypeVariable)`, a prefab and a
 * scene, and the shape it would miss is the one that shipped. So every concrete MonoBehaviour in an
 * Editor-only assembly is reported unless it is listed below with a reason.
 *
 * The allowlist is not prose. Each entry pins `addComponentSites`: how many `AddComponent` call
 * sites in the package name that type. Nought is the claim "this double is only ever reached
 * through `typeof`", and the gate re-measures it, so the day somebody adds the first call site the
 * excuse goes red rather than covering the defect it was written before. An entry whose file or
 * type is gone goes red too -- an allowlist that outlives its subject excuses whatever is put in it
 * next (#445).
 *
 * A type with NO `MonoScript` -- nested, or sharing a file -- is reported as well, and said out
 * loud in the message. It escapes Unity's policy TODAY and is a landmine: the moment somebody gives
 * it a correctly-named file it goes red, which is precisely what #666 did to eleven tests.
 *
 * `abstract` is exempt: `AddComponent` cannot instantiate one, so it is not a subject. Its concrete
 * subclasses are, wherever they are declared.
 *
 * Exit codes: 0 = every editor-assembly MonoBehaviour is accounted for, 1 = a finding, or a scan
 * that looked at nothing.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const { collectAsmdefs, ownerOf } = require("./lint-typecheck-asmdef-references.js");
const { codeOnly } = require("./lint-unsafe-code.js");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can drive the real command line over a fixture tree. Nothing in CI
// sets it, so the default is the only path that ships.
const SCAN_ROOT = process.env.EDITOR_ASSEMBLY_MONOBEHAVIOUR_SCAN_ROOT
  ? path.resolve(process.env.EDITOR_ASSEMBLY_MONOBEHAVIOUR_SCAN_ROOT)
  : REPO_ROOT;
const LABEL = "editor-assembly-monobehaviours";
const ISSUE = "see issue #678";

/**
 * Package-owned source roots. `Samples~` is included because
 * `scripts/unity/export-unitypackage.sh` renames it to `Samples`, so its Editor-only sample
 * assembly ships and is subject to the same rule.
 */
const SOURCE_ROOTS = ["Runtime", "Editor", "Tests", "Samples~"];

/**
 * Base types that ARE MonoBehaviours and are declared outside this package, so no source in the
 * tree resolves them. Everything else is resolved transitively from the package's own
 * declarations, which is how a double deriving from a package base is still caught.
 */
const MONO_BEHAVIOUR_ROOTS = new Set([
  "MonoBehaviour",
  // Odin's serializing base, used by the Odin-gated test targets.
  "SerializedMonoBehaviour",
  // uGUI: UIBehaviour is a MonoBehaviour, and everything drawable descends from it.
  "UIBehaviour",
  "Graphic",
  "MaskableGraphic",
  "Image",
  "RawImage",
  "Text"
]);

/**
 * Editor-assembly MonoBehaviours that are correct where they are, keyed
 * `<repo-relative path>::<type>`.
 *
 * `addComponentSites` is the number of `AddComponent` call sites in the package naming that type,
 * re-measured on every run. It is the entry's falsifiable half: the reason is what a human needs,
 * the count is what stops the reason outliving the facts it was written about.
 */
const EDITOR_ASSEMBLY_BY_DESIGN = new Map([
  [
    "Tests/Editor/TestTypes/Odin/WGroup/OdinWGroupMonoBehaviourTarget.cs::OdinWGroupMonoBehaviourTarget",
    {
      addComponentSites: 2,
      reason:
        "OdinWGroupInspectorTests AddComponents it twice, behind WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR, which no " +
        "CI leg defines -- so the refusal has never been observed on it. It cannot move: it derives from " +
        "Sirenix.OdinInspector.SerializedMonoBehaviour and Tests.Core declares no Sirenix references. Both sites " +
        "were read, and a refusal there FAILS rather than passing vacuously -- one builds a SerializedObject " +
        "from the component and the other asserts on CreateEditor's result, and neither survives a null. That " +
        "is why this entry is safe where the RegularMonoBehaviour one was not. The count is frozen so a third " +
        "site, which might not be self-guarding, cannot appear unnoticed"
    }
  ],
  [
    "Tests/Editor/TestComponents/PrewarmTesterComponent.cs::PrewarmTesterComponent",
    {
      addComponentSites: 0,
      reason:
        "ReflectionHelpersEditorTests asserts the editor type cache finds it; reached only through typeof, never " +
        "added to a GameObject"
    }
  ],
  [
    "Tests/Editor/TestTypes/AlphaRelationalComponent.cs::AlphaRelationalComponent",
    {
      addComponentSites: 0,
      reason:
        "AttributeMetadataCacheTests names it through typeof().AssemblyQualifiedName only, to exercise the " +
        "cache's key handling"
    }
  ],
  [
    "Tests/Editor/TestTypes/BravoRelationalComponent.cs::BravoRelationalComponent",
    {
      addComponentSites: 0,
      reason:
        "The second key in AttributeMetadataCacheTests, reached the same typeof-only way as its Alpha twin"
    }
  ],
  [
    "Tests/Editor/Tags/TestTypes/AlphaAttributesComponent.cs::AlphaAttributesComponent",
    {
      addComponentSites: 0,
      reason:
        "AttributeMetadataCacheTests uses it as an attribute-carrying type for the cache to enumerate; the " +
        "fixture works in Types, never in instances"
    }
  ],
  [
    "Tests/Editor/Tags/TestTypes/BravoAttributesComponent.cs::BravoAttributesComponent",
    {
      addComponentSites: 0,
      reason: "The second attribute-carrying type in the same typeof-only fixture"
    }
  ],
  [
    "Tests/Editor/TestTypes/AnimationEventSource.cs::AnimationEventSource",
    {
      addComponentSites: 0,
      reason:
        "AnimationEventHelpersTests asks GetPossibleAnimatorEvents which methods it registers; the helper takes " +
        "a Type, so no instance is ever needed"
    }
  ],
  [
    "Tests/Editor/TestTypes/AnimationEventDerivedAllowed.cs::AnimationEventDerivedAllowed",
    {
      addComponentSites: 0,
      reason:
        "The inherited-event case for the same fixture: a subclass of AnimationEventSource whose base methods " +
        "must still be discovered, again by Type"
    }
  ],
  [
    "Tests/Editor/TestTypes/AnimationEventDerivedIgnore.cs::AnimationEventDerivedIgnore",
    {
      addComponentSites: 0,
      reason:
        "The opted-out counterpart to AnimationEventDerivedAllowed, discovered the same typeof-only way"
    }
  ],
  [
    "Tests/Editor/TestTypes/AnimationEventPlainBehaviour.cs::AnimationEventPlainBehaviour",
    {
      addComponentSites: 0,
      reason:
        "The negative case for the same fixture -- a MonoBehaviour with no animation events, asserted absent " +
        "from the mapping through typeof"
    }
  ],
  [
    "Tests/Editor/TestTypes/AnimationEventSignatureHost.cs::AnimationEventSignatureHost",
    {
      addComponentSites: 0,
      reason:
        "Carries one method per supported animation-event signature for the same fixture to enumerate by Type; " +
        "never instantiated"
    }
  ],
  [
    "Tests/Editor/TestTypes/NonRelational.cs::NonRelational",
    {
      addComponentSites: 0,
      reason:
        "RelationalComponentAssignerTests uses it as the type with no relational attributes; the fixture builds " +
        "its GameObjects from other components and only names this one through typeof"
    }
  ],
  [
    "Tests/Editor/TestTypes/RelationalConsumer.cs::RelationalConsumer",
    {
      addComponentSites: 0,
      reason:
        "No fixture references it at all today. Left in place rather than deleted by a change that owns no test " +
        "file; delete it or move it when the relational editor fixtures are next touched"
    }
  ]
]);

/** Directories no scan needs to enter, matching the asmdef walk this gate delegates to. */
function isSkippableDirectory(name) {
  return (
    name.startsWith(".") ||
    name === "obj" ||
    name === "bin" ||
    name === "node_modules" ||
    name === "Library" ||
    name === "Temp"
  );
}

/** @returns {string} `full` relative to `root`, with forward slashes. */
function relativeTo(root, full) {
  return path.relative(root, full).split(path.sep).join("/");
}

/**
 * Every `.cs` file under `directory`, read.
 *
 * A file the process cannot read leaves the scan, which is the same vacuum as a scope that narrowed
 * by accident, so it is collected rather than skipped (see `.llm/skills/honest-gates.md`).
 *
 * @param {string} directory Absolute directory to walk.
 * @param {{sources: {path: string, text: string}[], unreadable: string[]}} out Accumulator.
 * @param {string} scanRoot Absolute root the paths are reported relative to.
 * @returns {{sources: {path: string, text: string}[], unreadable: string[]}} The accumulator.
 */
function collectSources(directory, out, scanRoot) {
  let entries;
  try {
    entries = fs.readdirSync(directory, { withFileTypes: true });
  } catch (error) {
    out.unreadable.push(`${relativeTo(scanRoot, directory)} (${error.message})`);
    return out;
  }
  for (const entry of entries) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (!isSkippableDirectory(entry.name)) {
        collectSources(full, out, scanRoot);
      }
      continue;
    }
    if (!entry.isFile() || !entry.name.endsWith(".cs")) {
      continue;
    }
    try {
      out.sources.push({ path: relativeTo(scanRoot, full), text: fs.readFileSync(full, "utf8") });
    } catch (error) {
      out.unreadable.push(`${relativeTo(scanRoot, full)} (${error.message})`);
    }
  }
  return out;
}

/** @returns {boolean} Whether an asmdef compiles for the Editor platform and nothing else. */
function isEditorOnly(asmdef) {
  const platforms = asmdef.includePlatforms ?? [];
  return platforms.length === 1 && platforms[0] === "Editor";
}

const CLASS_DECLARATION =
  /(?:^|[^\w.])((?:(?:public|internal|private|protected|sealed|abstract|static|partial|new|unsafe)\s+)*)class\s+([A-Za-z_]\w*)\s*(?:<[^<>{;]*>)?\s*(?::([^{;=]*?))?\{/g;
const NAMESPACE_DECLARATION = /(?:^|[^\w.])namespace\s+([\w.]+)/;
const USING_DIRECTIVE = /(?:^|[^\w.])using\s+(?:static\s+)?(?:global::)?([\w.]+)\s*;/g;

/** @returns {number} The index of the brace matching the one at `openIndex`, or -1. */
function matchingBrace(code, openIndex) {
  let depth = 0;
  for (let index = openIndex; index < code.length; index++) {
    if (code[index] === "{") {
      depth++;
      continue;
    }
    if (code[index] !== "}") {
      continue;
    }
    depth--;
    if (depth === 0) {
      return index;
    }
  }
  return -1;
}

/**
 * Splits a base list at its top-level commas, so `Foo<A, B>, IBar` yields two names rather than
 * three. Namespace qualifiers are KEPT: they are what tells `Tests.WButton.TestComponent` apart
 * from `Tests.Integrations.VContainer.TestComponent`, and the first draft, which reduced every name
 * to its last segment, decided the ScriptableObject one was a MonoBehaviour.
 *
 * @param {string} baseList The text between `:` and `{`, generic constraints already removed.
 * @returns {string[]} Base type names, generic arguments stripped.
 */
function baseTypeNames(baseList) {
  const names = [];
  let depth = 0;
  let current = "";
  for (const character of baseList) {
    if (character === "<" || character === "(" || character === "[") {
      depth++;
    } else if (character === ">" || character === ")" || character === "]") {
      depth--;
    }
    if (character === "," && depth === 0) {
      names.push(current);
      current = "";
      continue;
    }
    current += character;
  }
  names.push(current);

  return names
    .map((name) => name.replace(/<[\s\S]*$/, "").trim())
    .map((name) => name.replace(/^global::/, ""))
    .filter((name) => /^[A-Za-z_][\w.]*$/.test(name));
}

/** @returns {string} The last segment of a possibly qualified name. */
function simpleNameOf(name) {
  return name.includes(".") ? name.slice(name.lastIndexOf(".") + 1) : name;
}

/**
 * A file parsed into what name resolution needs: its namespace, its `using` directives, and every
 * class it declares.
 *
 * Comments and string literals are blanked first, so the doc comment in
 * `Editor/AnimationEventEditor.cs` showing `class EnemyEvents : MonoBehaviour` is prose rather than
 * a declaration -- it is, and the first draft reported it.
 *
 * @param {{path: string, text: string}} source One C# source.
 * @returns {{path: string, namespace: string, usings: Set<string>, declarations: object[]}} The parse.
 */
function parseSource(source) {
  const code = codeOnly(source.text);
  const namespaceMatch = NAMESPACE_DECLARATION.exec(code);
  const fileNamespace = namespaceMatch === null ? "" : namespaceMatch[1];
  const usings = new Set();
  for (const match of code.matchAll(USING_DIRECTIVE)) {
    usings.add(match[1]);
  }

  const spans = [];
  for (const match of code.matchAll(CLASS_DECLARATION)) {
    const openIndex = match.index + match[0].length - 1;
    const closeIndex = matchingBrace(code, openIndex);
    spans.push({
      file: source.path,
      namespace: fileNamespace,
      usings,
      name: match[2],
      bases: baseTypeNames((match[3] ?? "").replace(/\bwhere\b[\s\S]*$/, "")),
      isAbstract: /\babstract\b/.test(match[1] ?? ""),
      start: openIndex,
      end: closeIndex < 0 ? code.length : closeIndex,
      line: code.slice(0, match.index).split("\n").length
    });
  }

  const fileTypeName = path.basename(source.path, ".cs");
  const declarations = [];
  for (const span of spans) {
    const enclosing = spans
      .filter((other) => other !== span && other.start < span.start && span.end <= other.end)
      .sort((left, right) => left.start - right.start)
      .map((other) => other.name);
    const prefix = [fileNamespace, ...enclosing].filter((part) => part !== "").join(".");
    declarations.push({
      file: span.file,
      line: span.line,
      namespace: fileNamespace,
      usings,
      name: span.name,
      qualified: prefix === "" ? span.name : `${prefix}.${span.name}`,
      bases: span.bases,
      isAbstract: span.isAbstract,
      isNested: 0 < enclosing.length,
      /*
          Unity binds a MonoScript by FILE NAME. A type that is nested, or that shares a file with a
          differently-named type, has none -- so the editor cannot classify it, the refusal never
          fires, and it stays that way only until somebody gives it a file of its own.
      */
      hasMonoScript: enclosing.length === 0 && span.name === fileTypeName
    });
  }
  return { path: source.path, namespace: fileNamespace, usings, declarations };
}

/**
 * A name resolver over the package's own declarations.
 *
 * C# scoping, narrowed to what this tree uses: a simple name binds in the containing namespace, in
 * any ancestor of it, or through a `using`. That is what tells two same-named test doubles apart,
 * and nothing here needs more.
 *
 * @param {object[]} declarations Every class declared in the package.
 * @returns {{resolve: (name: string, context: {namespace: string, usings: Set<string>}) => object, isMonoBehaviour: (declaration: object) => boolean}} The resolver.
 */
function buildTypeIndex(declarations) {
  const byQualified = new Map();
  const bySimple = new Map();
  for (const declaration of declarations) {
    if (!byQualified.has(declaration.qualified)) {
      byQualified.set(declaration.qualified, declaration);
    }
    const siblings = bySimple.get(declaration.name) ?? [];
    siblings.push(declaration);
    bySimple.set(declaration.name, siblings);
  }

  const visibleNamespaces = (context) => {
    const visible = new Set(context.usings);
    let current = context.namespace;
    while (current !== "") {
      visible.add(current);
      const cut = current.lastIndexOf(".");
      current = cut < 0 ? "" : current.slice(0, cut);
    }
    return visible;
  };

  const resolve = (name, context) => {
    if (name.includes(".")) {
      const exact = byQualified.get(name);
      if (exact) {
        return exact;
      }
      const bySuffix = (bySimple.get(simpleNameOf(name)) ?? []).filter((declaration) =>
        declaration.qualified.endsWith(`.${name}`)
      );
      if (bySuffix.length === 1) {
        return bySuffix[0];
      }
      return null;
    }

    const candidates = bySimple.get(name) ?? [];
    if (candidates.length === 0) {
      return null;
    }
    if (candidates.length === 1) {
      return candidates[0];
    }
    /*
        Ambiguous by simple name. Prefer the declaration in the referring file's own namespace, then
        any namespace the file can see; a name that matches none of those is left unresolved rather
        than guessed, because guessing is what merged two TestComponents into one.
    */
    const own = candidates.filter((declaration) => declaration.namespace === context.namespace);
    if (0 < own.length) {
      return own[0];
    }
    const visible = visibleNamespaces(context);
    const reachable = candidates.filter((declaration) => visible.has(declaration.namespace));
    return reachable.length === 1 ? reachable[0] : null;
  };

  const answers = new Map();
  const isMonoBehaviour = (declaration) => {
    const walk = (current, seen) => {
      if (answers.has(current.qualified)) {
        return answers.get(current.qualified);
      }
      if (seen.has(current.qualified)) {
        return false;
      }
      seen.add(current.qualified);
      let result = false;
      for (const base of current.bases) {
        if (MONO_BEHAVIOUR_ROOTS.has(simpleNameOf(base))) {
          result = true;
          break;
        }
        const resolved = resolve(base, current);
        if (resolved && walk(resolved, seen)) {
          result = true;
          break;
        }
      }
      answers.set(current.qualified, result);
      return result;
    };
    return walk(declaration, new Set());
  };

  return { resolve, isMonoBehaviour };
}

/*
    `AddComponent<T>()`, `AddComponent(typeof(T))` and their `Undo.`/`ObjectFactory.` spellings. The
    `AddComponent("Name")` overload cannot be seen here -- string literals are blanked before the
    scan so prose cannot match -- and it is obsolete in every editor this package supports.
*/
const ADD_COMPONENT_SITE =
  /\bAddComponent\s*(?:<\s*([\w.]+)\s*>|\(\s*typeof\s*\(\s*([\w.]+)\s*\)\s*\))/g;

/**
 * @param {{path: string, text: string}[]} sources C# sources to scan.
 * @param {{resolve: Function}} index The package's name resolver.
 * @param {Map<string, {namespace: string, usings: Set<string>}>} contexts Per-file scope, by path.
 * @returns {Map<string, string[]>} Qualified type name to the `path:line` of each call site.
 */
function collectAddComponentSites(sources, index, contexts) {
  const sites = new Map();
  for (const source of sources) {
    const code = codeOnly(source.text);
    const context = contexts.get(source.path) ?? { namespace: "", usings: new Set() };
    for (const match of code.matchAll(ADD_COMPONENT_SITE)) {
      const resolved = index.resolve(match[1] ?? match[2], context);
      if (resolved === null) {
        continue;
      }
      const line = code.slice(0, match.index).split("\n").length;
      const found = sites.get(resolved.qualified) ?? [];
      found.push(`${source.path}:${line}`);
      sites.set(resolved.qualified, found);
    }
  }
  return sites;
}

/**
 * The rule.
 *
 * @param {{path: string, text: string}[]} sources Package-owned C# sources.
 * @param {Map<string, object>} asmdefs Asmdefs keyed by owning directory, from `collectAsmdefs`.
 * @param {Map<string, {addComponentSites: number, reason: string}>} allowlist Entries by `path::type`.
 * @param {string} scanRoot Absolute root the asmdef map is keyed under.
 * @returns {{failures: string[], subjects: object[], allowed: object[], declarations: number, monoBehaviours: number, addComponentSites: number}} Findings and counts.
 */
function analyze(sources, asmdefs, allowlist, scanRoot) {
  const parsed = sources.map(parseSource);
  const declarations = parsed.flatMap((file) => file.declarations);
  const index = buildTypeIndex(declarations);
  const contexts = new Map(
    parsed.map((file) => [file.path, { namespace: file.namespace, usings: file.usings }])
  );
  const sitesByType = collectAddComponentSites(sources, index, contexts);

  const failures = [];
  const subjects = [];
  const allowed = [];
  const justified = new Set();
  let monoBehaviours = 0;

  for (const declaration of declarations) {
    if (!index.isMonoBehaviour(declaration)) {
      continue;
    }
    monoBehaviours++;
    if (declaration.isAbstract) {
      continue;
    }
    const owner = ownerOf(path.join(scanRoot, declaration.file).split(path.sep).join("/"), asmdefs);
    if (!owner || !isEditorOnly(owner)) {
      continue;
    }

    const key = `${declaration.file}::${declaration.name}`;
    const sites = sitesByType.get(declaration.qualified) ?? [];
    subjects.push({ key, owner: owner.name, declaration, sites });

    const entry = allowlist.get(key);
    if (!entry) {
      const binding = declaration.hasMonoScript
        ? "Unity can identify it as an editor script through its MonoScript and will refuse AddComponent"
        : `it has NO MonoScript -- it is ${declaration.isNested ? "nested" : "sharing a file named for another type"}, ` +
          "so it escapes the refusal only until somebody gives it a file of its own, exactly as #666 " +
          "did to eleven tests";
      failures.push(
        `${declaration.file}:${declaration.line}: ${declaration.name} is a MonoBehaviour in ` +
          `${owner.name}, which is Editor-only, and ${binding}` +
          `${0 < sites.length ? `; ${sites.length} AddComponent site(s) already name it: ${sites.join(", ")}` : ""}. ` +
          "Move it to a runtime-capable test assembly (Tests.Core, Tests/Runtime/**), or add it to " +
          `EDITOR_ASSEMBLY_BY_DESIGN with the reason it belongs here, ${ISSUE}.`
      );
      continue;
    }

    justified.add(key);
    allowed.push({ key, reason: entry.reason, sites });
    if (sites.length !== entry.addComponentSites) {
      failures.push(
        `${declaration.file}:${declaration.line}: ${declaration.name} is allowed in the Editor-only ` +
          `assembly ${owner.name} on the claim of ${entry.addComponentSites} AddComponent site(s), and ` +
          `there are now ${sites.length}${0 < sites.length ? ` (${sites.join(", ")})` : ""}. Unity refuses ` +
          "to add a MonoBehaviour it can identify as an editor script, so a new site is the defect " +
          `arriving, not a count to update: move the type to a runtime-capable test assembly, ${ISSUE}.`
      );
    }
  }

  for (const key of allowlist.keys()) {
    if (!justified.has(key)) {
      failures.push(
        `EDITOR_ASSEMBLY_BY_DESIGN names ${key}, which no longer declares that MonoBehaviour in an ` +
          "Editor-only assembly. Remove the entry -- an allowlist that outlives its subject excuses " +
          `whatever is put in it next, ${ISSUE}.`
      );
    }
  }

  return {
    failures,
    subjects,
    allowed,
    declarations: declarations.length,
    monoBehaviours,
    addComponentSites: [...sitesByType.values()].reduce((total, list) => total + list.length, 0)
  };
}

/**
 * The vacuity half. A scan reporting no finding is reporting one of two things and they print the
 * same line, so every subject set this gate narrows through says out loud that it was not empty
 * (`.llm/skills/honest-gates.md`).
 *
 * @param {{asmdefs: number, editorOnlyAssemblies: number, sourcesByRoot: Map<string, number>, monoBehaviours: number, addComponentSites: number, editorAssemblySubjects: number}} counts Measured sizes.
 * @returns {string[]} One message per empty subject set.
 */
function vacuityFailures(counts) {
  const failures = [];
  if (counts.asmdefs === 0) {
    failures.push(
      "found no .asmdef anywhere under the scan root, so nothing could be classified as Editor-only. " +
        "That is a broken scan rather than a clean repository."
    );
  }
  if (counts.editorOnlyAssemblies === 0) {
    failures.push(
      'found no assembly with includePlatforms ["Editor"], so the rule had nothing to apply to.'
    );
  }
  for (const [root, count] of counts.sourcesByRoot) {
    if (count === 0) {
      failures.push(`${root}/ contributed no .cs file, so that whole tree fell out of the scan.`);
    }
  }
  if (counts.monoBehaviours === 0) {
    failures.push(
      "no class in the package resolved to a MonoBehaviour, so the inheritance walk saw none of what " +
        "this gate exists to classify."
    );
  }
  if (counts.addComponentSites === 0) {
    failures.push(
      "no AddComponent call site was resolved in the package, so every allowlist entry's site count " +
        "was compared against a scan that read nothing."
    );
  }
  if (counts.editorAssemblySubjects === 0) {
    failures.push(
      "no MonoBehaviour was found in any Editor-only assembly. That is the state this gate wants to " +
        "reach, but it is also what a narrowed scope prints, so it fails until the allowlist is " +
        "emptied deliberately and this check retired with it."
    );
  }
  return failures;
}

/**
 * Reads the tree and applies both halves.
 *
 * @param {string} scanRoot Absolute repository (or fixture) root.
 * @param {Map<string, {addComponentSites: number, reason: string}>} allowlist Entries by `path::type`.
 * @returns {{failures: string[], summary: string, subjects: object[]}} The run's outcome.
 */
function scan(scanRoot, allowlist) {
  const asmdefs = collectAsmdefs(scanRoot);
  const editorOnly = [...asmdefs.values()].filter(isEditorOnly);

  const collected = { sources: [], unreadable: [] };
  const sourcesByRoot = new Map();
  for (const root of SOURCE_ROOTS) {
    const full = path.join(scanRoot, root);
    if (!fs.existsSync(full)) {
      continue;
    }
    const before = collected.sources.length;
    collectSources(full, collected, scanRoot);
    sourcesByRoot.set(root, collected.sources.length - before);
  }

  const result = analyze(collected.sources, asmdefs, allowlist, scanRoot);
  const failures = [...result.failures];

  failures.push(
    ...vacuityFailures({
      asmdefs: asmdefs.size,
      editorOnlyAssemblies: editorOnly.length,
      sourcesByRoot,
      monoBehaviours: result.monoBehaviours,
      addComponentSites: result.addComponentSites,
      editorAssemblySubjects: result.subjects.length
    })
  );

  for (const entry of collected.unreadable) {
    failures.push(
      `could not be read, so it left the scan without a trace: ${entry}. That is a hole in the ` +
        `measurement rather than a defect in the file, ${ISSUE}.`
    );
  }

  const summary =
    `${asmdefs.size} asmdef(s), ${editorOnly.length} of them Editor-only; ` +
    `${collected.sources.length} source file(s) across ${[...sourcesByRoot.keys()].join(", ")}; ` +
    `${result.declarations} class declaration(s), ${result.monoBehaviours} of them MonoBehaviours; ` +
    `${result.addComponentSites} resolved AddComponent site(s); ` +
    `${result.subjects.length} MonoBehaviour(s) in Editor-only assemblies considered, ` +
    `${result.allowed.length} allowed by design.`;

  return { failures, summary, subjects: result.subjects };
}

/**
 * @returns {Map<string, {addComponentSites: number, reason: string}>} The fixture's allowlist, or
 * an empty one when it supplies none.
 */
function readAllowlistOverride() {
  const file = process.env.EDITOR_ASSEMBLY_MONOBEHAVIOUR_ALLOWLIST;
  if (!file) {
    return new Map();
  }
  return new Map(Object.entries(JSON.parse(fs.readFileSync(file, "utf8"))));
}

function main() {
  const verbose = process.argv.includes("--verbose");
  /*
      The allowlist describes THIS repository's own doubles. Pointed at a fixture tree it would
      report every entry as stale, which would make the self-test's green half unreachable and its
      red half pass for the wrong reason. A fixture supplies its own through
      EDITOR_ASSEMBLY_MONOBEHAVIOUR_ALLOWLIST, a JSON file of the same shape.
  */
  const allowlist = SCAN_ROOT === REPO_ROOT ? EDITOR_ASSEMBLY_BY_DESIGN : readAllowlistOverride();
  const { failures, summary, subjects } = scan(SCAN_ROOT, allowlist);

  if (verbose) {
    for (const subject of subjects) {
      console.log(
        `  ..   ${subject.key} in ${subject.owner}: ${subject.sites.length} AddComponent site(s)` +
          `${subject.declaration.hasMonoScript ? "" : " -- NO MonoScript"}`
      );
    }
  }

  if (0 < failures.length) {
    console.error("");
    for (const failure of failures) {
      console.error(`[${LABEL}] FAIL ${failure}`);
    }
    console.error(`[${LABEL}] ${failures.length} violation(s). ${summary}`);
    process.exit(1);
  }

  console.log(`[${LABEL}] ${summary}`);
}

module.exports = {
  analyze,
  baseTypeNames,
  buildTypeIndex,
  collectAddComponentSites,
  collectSources,
  isEditorOnly,
  parseSource,
  scan,
  simpleNameOf,
  vacuityFailures,
  EDITOR_ASSEMBLY_BY_DESIGN,
  MONO_BEHAVIOUR_ROOTS,
  SOURCE_ROOTS
};

if (require.main === module) {
  main();
}
