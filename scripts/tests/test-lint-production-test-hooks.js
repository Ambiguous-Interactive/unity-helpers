#!/usr/bin/env node
// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
"use strict";
const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");
const script = path.resolve(__dirname, "..", "lint-production-test-hooks.js");
const { analyzeFile, isProductionPath } = require(script);
let count = 0;
function check(name, body) {
  body();
  count++;
  console.log(`[PASS] ${name}`);
}
for (const source of [
  "internal void ApplyForTesting() {}",
  "internal int CountForTests => 1;",
  "internal class TestHooks {}",
  "private Action TestOnlyCallback;",
  "internal class TestHook {}",
  "internal void runforTesting() {}",
  "private Action testOnlyCallback;",
  "internal void @RunForTesting() {}",
  "internal void RunFor\\u0054esting() {}",
  "#if UNITY_INCLUDE_TESTS\ninternal void RunForTests() {}\n#endif",
  "internal void RunForTests\r() {}"
])
  check(source, () => assert(analyzeFile(source).length));
check("generated lowercase singular hook", () =>
  assert(analyzeFile('writer.Line("internal class testHook {}");', true).length)
);
for (const source of [
  "// RunForTesting is forbidden\ninternal void Run() {}",
  "/* internal class TestHooks {} */ internal class Real {}",
  'const string Message = "ForTesting";',
  'const string Message = @"TestHooks "" quoted";',
  'const string Message = """ForTests\nTestOnly""";',
  "internal bool IsTestAssembly(Assembly assembly) => true;",
  "#if UNITY_INCLUDE_TESTS\n// Runtime scans authoring assemblies.\ninternal bool IsTestAssemblyLoaded() => true;\n#endif",
  "internal void Update(float elapsed) {}",
  "internal Action Callback;"
])
  check(source, () => assert.deepStrictEqual(analyzeFile(source), []));
check("explicit test-only injection marker", () =>
  assert(analyzeFile("// Test-only callback injection\ninternal Action Callback;").length)
);
check("test-only types are legitimate authoring docs", () =>
  assert.deepStrictEqual(
    analyzeFile("/// Excludes test-only singleton types from discovery.\ninternal class Filter {}"),
    []
  )
);
check("generated regular literal", () =>
  assert(analyzeFile('writer.Line("internal void RunForTesting() {}");', true).length)
);
check("generated verbatim literal", () =>
  assert(analyzeFile('writer.Line(@"internal class TestHooks {}");', true).length)
);
check("generated escaped identifier", () =>
  assert(analyzeFile('writer.Line("internal void RunFor\\u0054esting() {}");', true).length)
);
check("generated raw literal", () =>
  assert(analyzeFile('writer.Line("""internal int CountForTests;""");', true).length)
);
check("generated test-only directive", () =>
  assert(analyzeFile('writer.Line("#if UNITY_INCLUDE_TESTS");', true).length)
);
check("generated ordinary registration", () =>
  assert.deepStrictEqual(
    analyzeFile('writer.Line("internal static void Register() {}");', true),
    []
  )
);
for (const relative of [
  "Runtime/Helpers.cs",
  "Editor/Helpers.cs",
  "Generator~/WallstopStudios.UnityHelpers.Analyzers/Foo.cs",
  "Generator~/WallstopStudios.UnityHelpers.Proto.Generator/Foo.cs",
  "Generator~/NewProduction/Foo.cs",
  "Samples~/Demo/Foo.cs",
  "Styles/Foo.cs"
])
  check(relative, () => assert(isProductionPath(relative)));
for (const relative of [
  "Tests/Runtime/Foo.cs",
  "Generator~/WallstopStudios.UnityHelpers.Analyzers.Tests/Foo.cs",
  "Generator~/WallstopStudios.UnityHelpers.TestCheck/Foo.cs",
  "Generator~/WallstopStudios.UnityHelpers.SyntaxPolicy/Foo.cs",
  "Generator~/ProtobufNetV2Oracle/Foo.cs"
])
  check(relative, () => assert(!isProductionPath(relative)));
check("uppercase Unicode source escape", () =>
  assert(analyzeFile("internal void RunFor\\U00000054esting() {}").length)
);
check("generated concatenated identifier", () =>
  assert(analyzeFile('writer.Line("internal void RunFor" + "Testing() {}");', true).length)
);
check("generated uppercase Unicode escape", () =>
  assert(analyzeFile('writer.Line("internal void RunFor\\U00000054esting() {}");', true).length)
);
check("generated documentation mention is not a member", () =>
  assert.deepStrictEqual(analyzeFile('writer.Line("/// ForTesting is forbidden.");', true), [])
);
check("generated escaped string value is not a member", () =>
  assert.deepStrictEqual(
    analyzeFile('writer.Line("internal const string Message = \\"ForTesting\\";");', true),
    []
  )
);
check("ordinary character literal is masked", () =>
  assert.deepStrictEqual(analyzeFile("private char Quote = '\\''; internal void Run() {}"), [])
);
check("hook in otherwise inactive branch is rejected", () =>
  assert(analyzeFile("#if UNKNOWN_DEFINE\ninternal void RunForTests() {}\n#endif").length)
);
check("production Tests subfolder does not bypass policy", () =>
  assert(isProductionPath("Runtime/Tests/Hook.cs"))
);
check("production host-looking folder does not bypass policy", () =>
  assert(isProductionPath("Editor/WallstopStudios.UnityHelpers.TestCheck/Hook.cs"))
);
check("nested generator Tests folder remains production", () =>
  assert(isProductionPath("Generator~/Emitter/Tests/Hook.cs"))
);
check("interpolation-only reference is explicitly outside naming scan", () =>
  assert.deepStrictEqual(analyzeFile('string Message => $"{Foreign.RunForTests()}";'), [])
);
check("actual hook declaration remains rejected beside interpolation", () =>
  assert(analyzeFile('internal void RunForTests() {} string Message => $"{RunForTests()}";').length)
);
check("singular production hook is rejected", () =>
  assert(analyzeFile("internal bool GetConditionForTest() => true;").length)
);
check("singular escaped production hook is rejected", () =>
  assert(analyzeFile("internal void RunFor\\u0054est() {}").length)
);
check("singular generated hook is rejected", () =>
  assert(analyzeFile('writer.Line("internal void RunForTest() {}");', true).length)
);
check("singular escaped generated hook is rejected", () =>
  assert(analyzeFile('writer.Line("internal void RunFor\\u0054est() {}");', true).length)
);
const root = fs.mkdtempSync(path.join(os.tmpdir(), "production-test-hooks-"));
function write(relative, source) {
  const file = path.join(root, relative);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, source);
}
function cli() {
  return spawnSync(process.execPath, [script], {
    env: { ...process.env, PRODUCTION_TEST_HOOKS_ROOT: root },
    encoding: "utf8"
  });
}
try {
  write("Runtime/Real.cs", "internal class Real {}");
  check("CLI rejects partial roots despite real subjects", () => {
    const result = cli();
    assert.strictEqual(result.status, 1);
    assert(result.stderr.includes("missing production root directory Editor"));
  });
  fs.unlinkSync(path.join(root, "Runtime/Real.cs"));
  for (const directory of ["Runtime", "Editor", "Generator~", "Samples~", "Styles"])
    fs.mkdirSync(path.join(root, directory), { recursive: true });
  check("CLI refuses empty acceptance", () => assert.strictEqual(cli().status, 1));
  write("Runtime/Real.cs", "internal class Real {}");
  write(
    "Generator~/WallstopStudios.UnityHelpers.Analyzers.Tests/Excluded.cs",
    "internal class TestHooks {}"
  );
  check("CLI checks real source while excluding test tooling", () =>
    assert.strictEqual(cli().status, 0)
  );
  write("Samples~/Demo/Hook.cs", "internal void RunForTesting() {}");
  check("CLI rejects sample hook", () => {
    const result = cli();
    assert.strictEqual(result.status, 1);
    assert(result.stderr.includes("Samples~/Demo/Hook.cs"));
  });
  fs.unlinkSync(path.join(root, "Samples~/Demo/Hook.cs"));
  fs.renameSync(path.join(root, "Editor"), path.join(root, "RenamedEditor"));
  check("CLI rejects renamed root with other valid source", () => {
    const result = cli();
    assert.strictEqual(result.status, 1);
    assert(result.stderr.includes("missing production root directory Editor"));
  });
  fs.renameSync(path.join(root, "RenamedEditor"), path.join(root, "Editor"));
  fs.rmSync(path.join(root, "Styles"), { recursive: true });
  fs.writeFileSync(path.join(root, "Styles"), "not a directory");
  check("CLI rejects file replacing expected root", () => {
    const result = cli();
    assert.strictEqual(result.status, 1);
    assert(result.stderr.includes("missing production root directory Styles"));
  });
  fs.unlinkSync(path.join(root, "Styles"));
  fs.mkdirSync(path.join(root, "Styles"));
  write("Generator~/Emitter/Emitter.cs", 'writer.Line("internal class TestHooks {}");');
  check("CLI rejects generated source hook", () => {
    const result = cli();
    assert.strictEqual(result.status, 1);
    assert(result.stderr.includes("forbidden emitted identifier"));
  });
} finally {
  fs.rmSync(root, { recursive: true, force: true });
}
console.log(`[test-lint-production-test-hooks] ${count} controls passed.`);
