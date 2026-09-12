"use strict";

const assert = require("node:assert/strict");
const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const zlib = require("node:zlib");

const {
  ASSET_ROOT,
  collectAssets,
  createUnityPackage,
  tarHeader,
  writeUnityPackage
} = require("../unity/create-unitypackage.js");

function metadata(guid) {
  return `fileFormatVersion: 2\nguid: ${guid}\n`;
}

function parseArchive(archivePath) {
  const archive = zlib.gunzipSync(fs.readFileSync(archivePath));
  const entries = new Map();
  let terminated = false;
  for (let offset = 0; offset + 512 <= archive.length;) {
    const header = archive.subarray(offset, offset + 512);
    if (header.every((value) => value === 0)) {
      assert.equal(offset + 1024, archive.length);
      assert(header.every((value) => value === 0));
      assert(archive.subarray(offset + 512).every((value) => value === 0));
      terminated = true;
      break;
    }
    assert.equal(header.subarray(257, 263).toString("ascii"), "ustar\0");
    assert.equal(header.subarray(263, 265).toString("ascii"), "00");
    const storedChecksum = Number.parseInt(
      header.subarray(148, 156).toString("ascii").replace(/\0.*$/s, "").trim(),
      8
    );
    const checksumHeader = Buffer.from(header);
    checksumHeader.fill(0x20, 148, 156);
    assert.equal(
      storedChecksum,
      checksumHeader.reduce((sum, value) => sum + value, 0)
    );
    const name = header.subarray(0, 100).toString("utf8").replace(/\0.*$/s, "");
    const sizeText = header.subarray(124, 136).toString("ascii").replace(/\0.*$/s, "").trim();
    const size = Number.parseInt(sizeText || "0", 8);
    const contentOffset = offset + 512;
    entries.set(name, Buffer.from(archive.subarray(contentOffset, contentOffset + size)));
    offset = contentOffset + Math.ceil(size / 512) * 512;
  }
  assert(terminated, "archive must end with two zero blocks");
  return entries;
}

async function main() {
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), "unitypackage-test-"));
  try {
    const stagedRoot = path.join(temporary, "payload");
    const folder = path.join(stagedRoot, "Samples");
    fs.mkdirSync(folder, { recursive: true });
    fs.writeFileSync(path.join(stagedRoot, "Readme.txt"), "read me");
    fs.writeFileSync(
      path.join(stagedRoot, "Readme.txt.meta"),
      metadata("11111111111111111111111111111111")
    );
    fs.writeFileSync(path.join(folder, "Example.cs"), "class Example {}\n");
    fs.writeFileSync(
      path.join(folder, "Example.cs.meta"),
      metadata("22222222222222222222222222222222")
    );
    fs.writeFileSync(path.join(stagedRoot, ".gitkeep"), "ignored");

    const assets = collectAssets(stagedRoot);
    assert.equal(assets.length, 4);
    assert.deepEqual(assets.map((asset) => asset.pathname).sort(), [
      ASSET_ROOT,
      `${ASSET_ROOT}/Readme.txt`,
      `${ASSET_ROOT}/Samples`,
      `${ASSET_ROOT}/Samples/Example.cs`
    ]);
    const generatedFolder = assets.find((asset) => asset.pathname.endsWith("/Samples"));
    assert.match(
      generatedFolder.meta.toString("utf8"),
      new RegExp(`guid: ${generatedFolder.guid}`)
    );

    const firstArchive = path.join(temporary, "first.unitypackage");
    const secondArchive = path.join(temporary, "second.unitypackage");
    const firstDigest = await writeUnityPackage(assets, firstArchive);
    const secondDigest = await writeUnityPackage(assets, secondArchive);
    assert.equal(firstDigest, secondDigest);
    assert.deepEqual(fs.readFileSync(firstArchive), fs.readFileSync(secondArchive));
    assert.equal(
      firstDigest,
      crypto.createHash("sha256").update(fs.readFileSync(firstArchive)).digest("hex")
    );
    assert.equal(
      fs.readFileSync(`${firstArchive}.sha256`, "ascii"),
      `${firstDigest}  first.unitypackage\n`
    );

    const entries = parseArchive(firstArchive);
    assert.equal(entries.size, 10);
    for (const asset of assets) {
      assert.equal(entries.get(`${asset.guid}/pathname`).toString("utf8"), asset.pathname);
      assert.deepEqual(entries.get(`${asset.guid}/asset.meta`), asset.meta);
      assert.equal(entries.has(`${asset.guid}/asset`), Boolean(asset.sourcePath));
    }

    fs.writeFileSync(path.join(stagedRoot, "Missing.txt"), "missing metadata");
    assert.throws(() => collectAssets(stagedRoot), /has no metadata/);
    fs.writeFileSync(
      path.join(stagedRoot, "Missing.txt.meta"),
      metadata("11111111111111111111111111111111")
    );
    assert.throws(() => collectAssets(stagedRoot), /duplicate GUIDs/);
    assert.throws(() => tarHeader("x".repeat(101), 0), /too long/);

    const repoRoot = path.resolve(__dirname, "..", "..");
    const unityVersion = JSON.parse(
      fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8")
    ).release;
    const composedArchive = path.join(temporary, "composed.unitypackage");
    const composed = await createUnityPackage({
      repoRoot,
      outputPath: composedArchive,
      unityVersion
    });
    assert(composed.assets.length > 1000);
    const composedEntries = parseArchive(composedArchive);
    const composedFiles = composed.assets.filter((asset) => asset.sourcePath).length;
    assert.equal(composedEntries.size, composed.assets.length * 2 + composedFiles);
    assert(
      [...composedEntries.values()].some(
        (value) => value.toString("utf8") === `${ASSET_ROOT}/package.json`
      )
    );

    process.stdout.write("Unity package archive controls passed.\n");
  } finally {
    fs.rmSync(temporary, { recursive: true, force: true });
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
