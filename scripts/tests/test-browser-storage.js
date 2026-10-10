"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const vm = require("node:vm");
const { spawnSync } = require("node:child_process");
const { test } = require("node:test");

const repoRoot = path.resolve(__dirname, "../..");
const plugin = fs.readFileSync(
  path.join(repoRoot, "Runtime/Plugins/WebGL/BrowserStorage.jslib"),
  "utf8"
);

function createBridge(options = {}) {
  const values = new Map();
  const allocations = new Map();
  let nextPointer = 1;
  const calls = { freed: [], allocated: [], cleared: 0 };
  const storage = {
    getItem(key) {
      if (options.readFailure) throw new Error("read denied");
      return values.has(key) ? values.get(key) : null;
    },
    setItem(key, value) {
      if (options.writeFailure) throw new Error("quota exceeded");
      values.set(key, value);
    },
    removeItem(key) {
      if (options.deleteFailure) throw new Error("delete denied");
      values.delete(key);
    },
    clear() {
      ++calls.cleared;
      values.clear();
    }
  };
  const window = {};
  Object.defineProperty(window, "localStorage", {
    get() {
      if (options.accessFailure) throw new Error("storage blocked");
      return storage;
    }
  });
  const library = {};
  vm.runInNewContext(plugin, {
    window,
    LibraryManager: { library },
    mergeInto(target, members) {
      Object.assign(target, members);
    },
    UTF8ToString(pointer) {
      assert.ok(allocations.has(pointer), "unknown input pointer");
      const buffer = allocations.get(pointer);
      return buffer.subarray(0, buffer.indexOf(0)).toString("utf8");
    },
    lengthBytesUTF8(value) {
      return Buffer.byteLength(value, "utf8");
    },
    _malloc(size) {
      if (options.allocationFailure) return 0;
      const pointer = nextPointer++;
      calls.allocated.push(size);
      allocations.set(pointer, Buffer.alloc(size, 0xff));
      return pointer;
    },
    _free(pointer) {
      assert.ok(allocations.delete(pointer), "double free or invalid pointer");
      calls.freed.push(pointer);
    },
    stringToUTF8(value, pointer, size) {
      if (options.encodingFailure) throw new Error("encoding failed");
      const bytes = Buffer.from(value, "utf8");
      assert.equal(size, bytes.length + 1);
      bytes.copy(allocations.get(pointer));
      allocations.get(pointer)[bytes.length] = 0;
    }
  });
  function input(value) {
    const pointer = nextPointer++;
    allocations.set(pointer, Buffer.from(value + "\0", "utf8"));
    return pointer;
  }
  function read(pointer) {
    const buffer = allocations.get(pointer);
    assert.ok(buffer, "missing output allocation");
    assert.equal(buffer[buffer.length - 1], 0, "missing terminator");
    const value = buffer.subarray(0, buffer.length - 1).toString("utf8");
    allocations.delete(pointer);
    return value;
  }
  return { library, values, calls, allocations, input, read };
}

for (const value of ["", " ", "plain", "café 雪 😀", 'line\n\t"quoted"\\']) {
  test(`UTF-8 round trip ${JSON.stringify(value)}`, () => {
    const bridge = createBridge();
    const key = bridge.input("scoped:key");
    assert.equal(bridge.library.WUHBrowserStorageGetString(key), 0);
    assert.equal(bridge.library.WUHBrowserStorageSetString(key, bridge.input(value)), 1);
    const pointer = bridge.library.WUHBrowserStorageGetString(key);
    assert.ok(pointer);
    assert.equal(bridge.read(pointer), value);
    assert.deepEqual(bridge.calls.allocated, [Buffer.byteLength(value, "utf8") + 1]);
    assert.equal(bridge.library.WUHBrowserStorageDeleteKey(key), 1);
    assert.equal(bridge.library.WUHBrowserStorageDeleteKey(key), 1);
    assert.equal(bridge.library.WUHBrowserStorageGetString(key), 0);
  });
}

for (const failure of ["accessFailure", "readFailure", "writeFailure", "deleteFailure"]) {
  test(`${failure} fails without changing existing keys`, () => {
    const bridge = createBridge({ [failure]: true });
    bridge.values.set("existing", "retained");
    bridge.values.set("unrelated", "untouched");
    const key = bridge.input("existing");
    if (failure === "accessFailure" || failure === "readFailure") {
      assert.equal(bridge.library.WUHBrowserStorageGetString(key), 0);
    }
    if (failure === "accessFailure" || failure === "writeFailure") {
      assert.equal(bridge.library.WUHBrowserStorageSetString(key, bridge.input("new")), 0);
    }
    if (failure === "accessFailure" || failure === "deleteFailure") {
      assert.equal(bridge.library.WUHBrowserStorageDeleteKey(key), 0);
    }
    assert.equal(bridge.values.get("existing"), "retained");
    assert.equal(bridge.values.get("unrelated"), "untouched");
    assert.equal(bridge.calls.cleared, 0);
    assert.deepEqual(bridge.calls.allocated, []);
  });
}

for (const value of ["head\0tail", "head\uD800tail", "head\uDC00tail"]) {
  test(`invalid external text ${JSON.stringify(value)} is rejected without truncation`, () => {
    const bridge = createBridge();
    bridge.values.set("external", value);
    assert.equal(bridge.library.WUHBrowserStorageGetString(bridge.input("external")), 0);
    assert.deepEqual(bridge.calls.allocated, []);
    assert.equal(bridge.values.get("external"), value);
  });
}

test("allocation failure returns failure and preserves storage", () => {
  const bridge = createBridge({ allocationFailure: true });
  bridge.values.set("existing", "retained");
  assert.equal(bridge.library.WUHBrowserStorageGetString(bridge.input("existing")), 0);
  assert.equal(bridge.values.get("existing"), "retained");
  assert.deepEqual(bridge.calls.freed, []);
});

test("encoding failure frees owned return buffer", () => {
  const bridge = createBridge({ encodingFailure: true });
  bridge.values.set("existing", "retained");
  assert.equal(bridge.library.WUHBrowserStorageGetString(bridge.input("existing")), 0);
  assert.equal(bridge.calls.allocated.length, 1);
  assert.equal(bridge.calls.freed.length, 1);
  assert.equal(bridge.allocations.size, 1, "only caller-owned input remains");
});

test("deleting one key preserves unrelated origin storage", () => {
  const bridge = createBridge();
  bridge.values.set("owned", "one");
  bridge.values.set("foreign", "two");
  assert.equal(bridge.library.WUHBrowserStorageDeleteKey(bridge.input("owned")), 1);
  assert.equal(bridge.values.get("foreign"), "two");
  assert.equal(bridge.calls.cleared, 0);
});

test("meta generator and committed plugin enable only WebGL", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "browser-storage-meta-"));
  try {
    const target = path.join(directory, "BrowserStorage.jslib");
    fs.writeFileSync(target, plugin);
    const result = spawnSync("bash", [path.join(repoRoot, "scripts/generate-meta.sh"), target], {
      encoding: "utf8"
    });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    for (const meta of [
      fs.readFileSync(target + ".meta", "utf8"),
      fs.readFileSync(
        path.join(repoRoot, "Runtime/Plugins/WebGL/BrowserStorage.jslib.meta"),
        "utf8"
      )
    ]) {
      assert.match(meta, /PluginImporter:/);
      assert.match(meta, /Any:\s*second:\s*enabled: 0/);
      assert.match(meta, /WebGL: WebGL\s*second:\s*enabled: 1/);
      assert.equal((meta.match(/enabled: 1/g) || []).length, 1);
    }
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
});
