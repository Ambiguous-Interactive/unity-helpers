"use strict";

const assert = require("node:assert/strict");
const { resolveTestMatrix } = require("../unity/resolve-test-matrix.js");
const versions = ["2021.3.45f1", "2022.3.45f1", "6000.5.2f1", "6000.6.0f1"];
let controls = 0;
for (const version of ["2022.3.45f1", "6000.5.2f1"]) {
  for (const mode of ["all", "editmode", "playmode", "standalone"]) {
    assert.throws(
      () => resolveTestMatrix(versions, version, mode, "nullableconsumer"),
      /Nullable consumer acceptance requires its pinned floor or latest Unity version/
    );
    controls++;
  }
}
for (const version of ["2021.3.45f1", "6000.6.0f1"]) {
  const resolved = resolveTestMatrix(versions, version, "editmode", "nullableconsumer");
  assert.deepEqual(resolved["unity-versions"], [version]);
  assert.deepEqual(resolved["test-modes"], ["editmode", "standalone"]);
  controls++;
}
for (const version of ["", "   "]) {
  const resolved = resolveTestMatrix(versions, version, "all", "nullableconsumer");
  assert.deepEqual(resolved["unity-versions"], versions);
  assert.deepEqual(resolved["test-modes"], ["editmode", "playmode", "standalone"]);
  controls++;
}
for (const acceptance of ["none", "serialization", "all"]) {
  for (const version of ["2022.3.45f1", "6000.5.2f1"]) {
    const resolved = resolveTestMatrix(versions, version, "standalone", acceptance);
    assert.deepEqual(resolved["unity-versions"], [version]);
    controls++;
  }
}
assert.equal(controls, 18);
console.log(`Nullable consumer matrix controls passed: ${controls}`);
