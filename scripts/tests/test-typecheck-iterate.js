#!/usr/bin/env node

"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const { CHECK_PROJECTS } = require("../typecheck-controls.js");
const { NOTICE, VARIANTS, buildArguments, run } = require("../typecheck-iterate.js");

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
    assert.deepEqual(args.slice(8), VARIANTS[variant]);
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

assert.match(NOTICE, /compiler and source generators/);
assert.match(NOTICE, /WUH analyzers are omitted/);
assert.match(NOTICE, /normal typecheck gates before publication/);
console.log(`Typecheck iteration: ${controls} controls passed.`);
