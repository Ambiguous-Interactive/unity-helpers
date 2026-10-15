#!/usr/bin/env node

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { CHECK_PROJECTS } = require("../typecheck-controls.js");
const {
  NOTICE,
  VARIANTS,
  compilerIdentity,
  buildArguments,
  run
} = require("../typecheck-iterate.js");

let controls = 0;
for (const project of CHECK_PROJECTS) {
  assert.ok(fs.existsSync(path.resolve(__dirname, "../..", project.project)));
  for (const variant of Object.keys(VARIANTS)) {
    const args = buildArguments([project.id, variant]);
    assert.equal(args[0], "build");
    assert.equal(args[1], project.project);
    assert.ok(args.includes("--no-restore"));
    assert.ok(args.includes("-p:RunAnalyzers=false"));
    assert.ok(args.includes("-p:UseSharedCompilation=false"));
    assert.deepEqual(args.slice(8, -2), VARIANTS[variant]);
    assert.equal(args.at(-2), "-p:OutputPath=bin/iteration/" + variant + "/");
    assert.equal(args.at(-1), "-p:IntermediateOutputPath=obj/iteration/" + variant + "/");
    ++controls;
  }
  assert.deepEqual(buildArguments([project.id]), buildArguments([project.id, "default"]));
}

for (const args of [
  [],
  ["unknown"],
  ["../outside.csproj"],
  ["tests", "-p:Anything=true"],
  ["tests", "default", "--no-warnings"],
  ["tests", "__proto__"],
  ["tests", "constructor"]
]) {
  assert.throws(() => buildArguments(args), /Usage:/);
  assert.equal(
    run(
      args,
      () => assert.fail("Invalid input must not launch dotnet"),
      () => {}
    ),
    2
  );
  ++controls;
}

for (const status of [0, 1, 42]) {
  const messages = [];
  let launches = 0;
  assert.equal(
    run(
      ["tests", "odin"],
      (command, args, options) => {
        ++launches;
        assert.equal(command, "dotnet");
        assert.deepEqual(args, buildArguments(["tests", "odin"]));
        assert.equal(options.shell, false);
        assert.equal(options.stdio, "inherit");
        assert.equal(options.cwd, path.resolve(__dirname, "../.."));
        return { status };
      },
      (message) => messages.push(message)
    ),
    status
  );
  assert.equal(launches, 1);
  assert.deepEqual(messages, [NOTICE]);
  ++controls;
}

for (const result of [{ status: null, signal: "SIGTERM" }, { error: new Error("missing SDK") }]) {
  const messages = [];
  assert.equal(
    run(
      ["runtime"],
      () => result,
      (message) => messages.push(message)
    ),
    1
  );
  assert.equal(messages[0], NOTICE);
  if (result.error) {
    assert.equal(messages[1], "missing SDK");
  }
  ++controls;
}

const root = path.resolve(__dirname, "../..");
const originalPayloads = [Buffer.from("proto"), Buffer.from("analyzer"), Buffer.from("migration")];
const identity = compilerIdentity("editor-tests", "default", originalPayloads);
assert.equal(identity, compilerIdentity("editor-tests", "default", originalPayloads));
assert.notEqual(identity, compilerIdentity("editor-tests", "odin", originalPayloads));
assert.notEqual(identity, compilerIdentity("tests", "default", originalPayloads));
for (let index = 0; index < originalPayloads.length; ++index) {
  const changed = [...originalPayloads];
  changed[index] = Buffer.concat([changed[index], Buffer.from("changed")]);
  assert.notEqual(identity, compilerIdentity("editor-tests", "default", changed));
  ++controls;
}
assert.notEqual(
  compilerIdentity("tests", "default", [Buffer.from("ab"), Buffer.from("c")]),
  compilerIdentity("tests", "default", [Buffer.from("a"), Buffer.from("bc")])
);
const serverArguments = buildArguments(["editor-tests", "--server"]);
assert.ok(serverArguments.includes("-p:UseSharedCompilation=true"));
assert.match(serverArguments.at(-1), /^-p:SharedCompilationId=unity-helpers-iterate-[0-9a-f]{64}$/);
assert.notEqual(
  serverArguments.at(-1),
  buildArguments(["editor-tests", "odin", "--server"]).at(-1)
);
assert.notEqual(serverArguments.at(-1), buildArguments(["tests", "--server"]).at(-1));
assert.ok(
  !buildArguments(["editor-tests"]).some((argument) =>
    argument.startsWith("-p:SharedCompilationId=")
  )
);
const first = "Tests/Editor/Tools/OdinMigration/OdinMigrationLiveDictionaryTests.cs";
const second = "Tests/Editor/Tools/OdinMigration/OdinMigrationLiveAddressTests.cs";
for (const variant of ["default", "odin"]) {
  const args = buildArguments(["editor-tests", variant, "--files", first, second, first]);
  assert.equal(
    args.at(-1),
    "-p:WallstopIterationTestSources=" +
      [first, second].map((file) => path.resolve(root, file)).join("%3B")
  );
  assert.deepEqual(args.slice(8, -3), VARIANTS[variant]);
  const messages = [];
  assert.equal(
    run(
      ["editor-tests", variant, "--files", first],
      (_command, _args, options) => {
        assert.equal(options.shell, false);
        return { status: 0 };
      },
      (message) => messages.push(message)
    ),
    0
  );
  assert.equal(messages[0], NOTICE);
  assert.match(messages[1], /only the named editor fixtures/);
  ++controls;
}
assert.deepEqual(
  buildArguments(["editor-tests", "--files", first]),
  buildArguments(["editor-tests", "default", "--files", first])
);
for (const args of [
  ["editor-tests", "--files"],
  ["editor-tests", "--server", "--server"],
  ["runtime", "--files", first],
  ["editor-tests", "default", "unexpected", first],
  ["editor-tests", "--files", "Runtime/Core/Helper/BrowserStorage.cs"],
  ["editor-tests", "--files", "Tests/Editor/../../Runtime/Core/Helper/BrowserStorage.cs"],
  ["editor-tests", "--files", "Tests/Editor/AbsentFixture.cs"],
  ["editor-tests", "--files", "Tests/Editor/*.cs"],
  ["editor-tests", "--files", first + ";WallstopProto=false"],
  ["editor-tests", "--files", first + "%3Boutside.cs"],
  ["editor-tests", "--files", first + "\n"],
  ["editor-tests", "--files", "Tests/Editor/Tools/OdinMigration"]
]) {
  assert.throws(() => buildArguments(args), /Usage:|requires existing/);
  assert.equal(
    run(
      args,
      () => assert.fail("Invalid scope must not launch dotnet"),
      () => {}
    ),
    2
  );
  ++controls;
}
const temporary = fs.mkdtempSync(path.join(root, "Tests/Editor/.iteration-controls-"));
try {
  const outside = path.join(temporary, "Outside.cs");
  fs.symlinkSync(path.join(root, "Runtime/Core/Helper/BrowserStorage.cs"), outside);
  assert.throws(() => buildArguments(["editor-tests", "--files", outside]), /requires existing/);
  ++controls;
  if (process.platform !== "win32") {
    for (const name of ["Literal*.cs", "Literal?.cs", "Literal[1].cs"]) {
      const literal = path.join(temporary, name);
      fs.writeFileSync(literal, "// Intentionally empty control.\n");
      assert.throws(
        () => buildArguments(["editor-tests", "--files", literal]),
        /requires existing/
      );
      ++controls;
    }
  }
} finally {
  fs.rmSync(temporary, { recursive: true, force: true });
}
const projectText = fs.readFileSync(
  path.join(root, CHECK_PROJECTS.find((project) => project.id === "editor-tests").project),
  "utf8"
);
assert.match(projectText, /<ItemGroup Condition="'\$\(WallstopIterationTestSources\)' != ''">/);
assert.match(
  projectText,
  /<Compile\s+Condition="'\$\(WallstopIterationTestSources\)' == ''"\s+Include="\$\(RepoRoot\)\/Tests\/Editor\/\*\*\/\*\.cs"/
);
assert.doesNotMatch(projectText, /<Compile Remove="\$\(RepoRoot\)\/Tests\/Editor\/\*\*\/\*\.cs"/);
assert.match(projectText, /<Compile Include="\$\(WallstopIterationTestSources\)"\s*\/>/);

assert.match(NOTICE, /compiler and source generators/);
assert.match(NOTICE, /WUH analyzers are omitted/);
assert.match(NOTICE, /normal typecheck gates before publication/);
console.log(`Typecheck iteration: ${controls} controls passed.`);
