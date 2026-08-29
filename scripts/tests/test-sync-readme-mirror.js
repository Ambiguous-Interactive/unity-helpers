#!/usr/bin/env node
// MIT License — Copyright (c) wallstop studios
//
// Contract tests for scripts/sync-readme-mirror.js and scripts/lint-readme-mirror.js.
//
// The red half is the point (#556). `docs/readme.md` is generated, so the repository is clean by
// construction and a green `--check` proves nothing on its own: the fixture tree below is edited
// until the generator MUST report, and the test fails if it stays quiet. The two sluggers get the
// same treatment -- they are asserted to DISAGREE on the headings the rewrite exists for, because
// a slugger pair that always agreed would rewrite nothing and still pass every green assertion.

"use strict";

const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const repoRoot = path.resolve(__dirname, "..", "..");
const generatorPath = path.join(repoRoot, "scripts", "sync-readme-mirror.js");
const linterPath = path.join(repoRoot, "scripts", "lint-readme-mirror.js");
const { githubSlug, mkdocsSlug, headingsOf, buildMirror } = require(generatorPath);

let passed = 0;
let failed = 0;
const failures = [];

function runTest(name, body) {
  try {
    body();
    console.log(`  [PASS] ${name}`);
    passed++;
  } catch (error) {
    console.log(`  [FAIL] ${name}`);
    console.log(`         ${error.message}`);
    failed++;
    failures.push(name);
  }
}

/** Reads the real repository, except for README.md, which the caller is standing in for. */
function readerFor(source) {
  return (file) =>
    file === "README.md" ? source : fs.readFileSync(path.join(repoRoot, file), "utf8");
}

/** @returns {string} The mirror `buildMirror` produces, with its generated banner stripped. */
function mirrorOf(source) {
  const result = buildMirror(source, readerFor(source));
  assert.deepStrictEqual(result.problems, [], `unexpected problems: ${result.problems.join("; ")}`);
  return result.content.split("\n").slice(2).join("\n");
}

function problemsOf(source) {
  return buildMirror(source, readerFor(source)).problems;
}

function run(script, args, env) {
  return spawnSync(process.execPath, [script, ...args], {
    cwd: repoRoot,
    encoding: "utf8",
    env: { ...process.env, ...env },
    windowsHide: true
  });
}

// ── The two sluggers, and the headings they must disagree about ───────────────────────────────

/** [heading, GitHub fragment, published fragment]. The first four are why rewrite 3 exists. */
const SLUGS = [
  ["⚡ Top Time-Savers", "-top-time-savers", "top-time-savers"],
  ["1. 🎨 Inspector Tooling", "1--inspector-tooling", "1-inspector-tooling"],
  ["Benchmarking & Verification", "benchmarking--verification", "benchmarking-verification"],
  [
    "⚠️ IL2CPP Code Stripping Considerations",
    "-il2cpp-code-stripping-considerations",
    "il2cpp-code-stripping-considerations"
  ],
  // And these agree, so the rewrite must leave them alone.
  ["Core Features", "core-features", "core-features"],
  ["IL2CPP/WebGL notes", "il2cppwebgl-notes", "il2cppwebgl-notes"],
  ["Random Number Generators", "random-number-generators", "random-number-generators"]
];

for (const [heading, github, published] of SLUGS) {
  runTest(`slugs of '${heading}'`, () => {
    assert.strictEqual(githubSlug(heading), github, "GitHub fragment");
    assert.strictEqual(mkdocsSlug(heading), published, "published fragment");
  });
}

runTest("the sluggers disagree on the headings the anchor rewrite exists for", () => {
  const disagreements = SLUGS.filter(([heading]) => githubSlug(heading) !== mkdocsSlug(heading));
  assert.ok(
    4 <= disagreements.length,
    "a slugger pair that agreed everywhere would rewrite nothing and still pass every green case"
  );
});

runTest("a '#' inside a fenced code block is not a heading", () => {
  const headings = headingsOf(
    ["# Real", "", "```bash", "# not a heading", "```", "", "## Also real"].join("\n")
  );
  assert.deepStrictEqual(headings, ["Real", "Also real"]);
});

// ── The five rewrites ─────────────────────────────────────────────────────────────────────────

runTest("a link into the docs tree loses its docs/ segment", () => {
  assert.ok(
    mirrorOf("# T\n\n[Guide](./docs/overview/getting-started.md)\n").includes(
      "[Guide](./overview/getting-started.md)"
    )
  );
});

runTest("an HTML attribute into the docs tree keeps the author's un-prefixed style", () => {
  const mirror = mirrorOf('# T\n\n<img src="docs/images/unity-helpers-banner.svg" />\n');
  assert.ok(mirror.includes('src="images/unity-helpers-banner.svg"'), mirror);
});

runTest("a reference that leaves the docs tree becomes an absolute GitHub URL", () => {
  // The rewrite `mkdocs build --strict` is the only local check for: nothing else reports it.
  const mirror = mirrorOf("# T\n\n[Changelog](./CHANGELOG.md) and [llms](./llms.txt)\n");
  assert.ok(mirror.includes("https://github.com/wallstop/unity-helpers/blob/main/CHANGELOG.md"));
  assert.ok(mirror.includes("https://github.com/wallstop/unity-helpers/blob/main/llms.txt"));
});

runTest("a Samples~ link becomes an absolute GitHub URL, percent-encoding intact", () => {
  const mirror = mirrorOf("# T\n\n[DI](./Samples~/DI%20-%20VContainer/README.md)\n");
  assert.ok(
    mirror.includes(
      "https://github.com/wallstop/unity-helpers/blob/main/Samples~/DI%20-%20VContainer/README.md"
    ),
    mirror
  );
});

runTest("a same-document fragment is copied through, not re-slugged", () => {
  // Both renderers resolve this one, and markdownlint's MD051 checks it with GitHub's slugger.
  const source = "# T\n\n## Top Time Savers\n\n[Jump](#top-time-savers)\n";
  assert.ok(mirrorOf(source).includes("[Jump](#top-time-savers)"));
});

runTest("a same-document fragment whose heading publishes differently is reported", () => {
  // The mirror can carry only one of the two spellings, so the heading is where this is fixed.
  const problems = problemsOf("# T\n\n## ⚡ Top Time-Savers\n\n[Jump](#-top-time-savers)\n");
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("#top-time-savers"), problems[0]);
  assert.ok(problems[0].includes("Rename the heading"), problems[0]);
});

runTest("a cross-document fragment is re-slugged from the TARGET document's headings", () => {
  const source =
    "# T\n\n[B](./docs/features/utilities/reflection-helpers.md#benchmarking--verification)\n";
  assert.ok(
    mirrorOf(source).includes(
      "[B](./features/utilities/reflection-helpers.md#benchmarking-verification)"
    )
  );
});

runTest("a fragment on a link that left the tree keeps its GitHub slug", () => {
  // GitHub is the renderer resolving it now, so re-slugging it would break it.
  const mirror = mirrorOf("# T\n\n[C](./CHANGELOG.md#-something-odd)\n");
  assert.ok(mirror.includes("CHANGELOG.md#-something-odd"), mirror);
});

// ── Everything the rewrite must NOT touch ─────────────────────────────────────────────────────

runTest("an external URL is left alone", () => {
  const source = "# T\n\n[Unity](https://docs.unity3d.com/Manual/managed-code-stripping.html)\n";
  assert.ok(
    mirrorOf(source).includes("(https://docs.unity3d.com/Manual/managed-code-stripping.html)")
  );
});

runTest("a docs/ path inside a fenced code block is sample text, not a reference", () => {
  const source = ["# T", "", "```markdown", "[x](./docs/overview/index.md)", "```", ""].join("\n");
  assert.ok(mirrorOf(source).includes("[x](./docs/overview/index.md)"));
});

runTest("prose is copied byte for byte", () => {
  const prose = "Unity Helpers includes 200+ extension methods for common Unity operations:";
  assert.ok(mirrorOf(`# T\n\n${prose}\n`).includes(prose));
});

// ── The generator refusing to guess ───────────────────────────────────────────────────────────

runTest("a same-document fragment that matches no heading is reported", () => {
  const problems = problemsOf("# T\n\n## Real Heading\n\n[Dead](#no-such-heading)\n");
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("#no-such-heading"), problems[0]);
});

runTest("a relative link to a file that does not exist is reported", () => {
  const problems = problemsOf("# T\n\n[Gone](./NOT-A-REAL-FILE.md)\n");
  assert.strictEqual(problems.length, 1, problems.join("; "));
  assert.ok(problems[0].includes("does not exist"), problems[0]);
});

// ── End to end against a fixture tree, including the red half ─────────────────────────────────

runTest(
  "the generator writes a mirror, then agrees with it, then REPORTS when it is edited",
  () => {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), "readme-mirror-"));
    try {
      fs.mkdirSync(path.join(root, "docs"));
      fs.writeFileSync(path.join(root, "CHANGELOG.md"), "# Changelog\n");
      fs.writeFileSync(path.join(root, "docs", "guide.md"), "# Guide\n\n## Alpha & Beta\n");
      fs.writeFileSync(
        path.join(root, "README.md"),
        [
          "# Fixture",
          "",
          "## Top Time Savers",
          "",
          "[Self](#top-time-savers) [Guide](./docs/guide.md#alpha--beta) [Log](./CHANGELOG.md)",
          ""
        ].join("\n")
      );
      const env = { README_MIRROR_ROOT: root };

      const wrote = run(generatorPath, ["--verbose"], env);
      assert.strictEqual(wrote.status, 0, wrote.stderr);
      const mirror = fs.readFileSync(path.join(root, "docs", "readme.md"), "utf8");
      assert.ok(mirror.includes("[Self](#top-time-savers)"), mirror);
      assert.ok(mirror.includes("[Guide](./guide.md#alpha-beta)"), mirror);
      assert.ok(
        mirror.includes("[Log](https://github.com/wallstop/unity-helpers/blob/main/CHANGELOG.md)"),
        mirror
      );

      const clean = run(generatorPath, ["--check", "--verbose"], env);
      assert.strictEqual(clean.status, 0, clean.stderr);

      // The red half: hand-edit the generated file the way a contributor would, and require a report.
      fs.writeFileSync(
        path.join(root, "docs", "readme.md"),
        mirror.replace("# Fixture", "# Fixture edited by hand")
      );
      const drifted = run(generatorPath, ["--check"], env);
      assert.strictEqual(drifted.status, 1, "an edited mirror must be reported");
      assert.ok(drifted.stderr.includes("npm run sync:readme-mirror"), drifted.stderr);
      assert.ok(drifted.stderr.includes("edited by hand"), drifted.stderr);

      // And the wrapper must carry that verdict, not swallow it.
      const viaLinter = run(linterPath, [], env);
      assert.strictEqual(
        viaLinter.status,
        1,
        "the wrapper must propagate the generator's exit code"
      );

      // A missing mirror is drift, not a crash.
      fs.rmSync(path.join(root, "docs", "readme.md"));
      assert.strictEqual(run(generatorPath, ["--check"], env).status, 1);
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
);

runTest("the wrapper reports a generator it cannot find", () => {
  const missing = run(linterPath, ["--sync-script", "scripts/no-such-generator.js"], {});
  assert.strictEqual(missing.status, 1);
  assert.ok(missing.stderr.includes("generator not found"), missing.stderr);
});

runTest("the wrapper passes its arguments through to the generator", () => {
  const verbose = run(linterPath, ["--verbose"], {});
  assert.strictEqual(verbose.status, 0, verbose.stderr);
  assert.ok(verbose.stdout.includes("matches"), verbose.stdout);
});

// ── The nine anchors docs/overview/index.md depends on ────────────────────────────────────────

runTest("every ../readme.md#anchor in docs/overview/index.md resolves in BOTH renderers", () => {
  // The contract the mirror exists to keep, and the one a careless rewrite breaks silently:
  // `lint:docs` does not check fragments at all, and MD051 does not follow a cross-file one.
  // `#core-math-extensions` was dead on GitHub for exactly this reason -- the heading read
  // `Core Math & Extensions`, which GitHub slugs `core-math--extensions`.
  const index = fs.readFileSync(path.join(repoRoot, "docs", "overview", "index.md"), "utf8");
  const referenced = new Set(
    [...index.matchAll(/\.\.\/readme\.md#([^)\s|]+)/g)].map((match) => match[1])
  );
  assert.ok(0 < referenced.size, "the index must still link into the mirror");
  const headings = headingsOf(fs.readFileSync(path.join(repoRoot, "docs", "readme.md"), "utf8"));
  const published = new Set(headings.map(mkdocsSlug));
  const onGithub = new Set(headings.map(githubSlug));
  const dead = [...referenced]
    .filter((anchor) => !published.has(anchor) || !onGithub.has(anchor))
    .sort();
  assert.deepStrictEqual(dead, [], `dead anchors into docs/readme.md: ${dead.join(", ")}`);
});

console.log(`\n${passed} passed, ${failed} failed`);
if (0 < failed) {
  console.error(`Failed: ${failures.join(", ")}`);
  process.exit(1);
}
