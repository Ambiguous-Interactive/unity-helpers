#!/usr/bin/env node
"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { Readable } = require("node:stream");
const { pipeline } = require("node:stream/promises");
const zlib = require("node:zlib");

const { stageUnityPackage } = require("./stage-unitypackage.js");

const ASSET_ROOT = "Assets/WallstopStudios/UnityHelpers";
const BLOCK_SIZE = 512;

function writeOctal(header, offset, length, value) {
  const encoded = value.toString(8).padStart(length - 1, "0");
  if (encoded.length !== length - 1) {
    throw new Error(`Tar value ${value} does not fit in ${length} bytes.`);
  }
  header.write(encoded, offset, length - 1, "ascii");
  header[offset + length - 1] = 0;
}

function tarHeader(name, size) {
  if (Buffer.byteLength(name) > 100) {
    throw new Error(`Unity package archive path is too long: ${name}`);
  }
  const header = Buffer.alloc(BLOCK_SIZE);
  header.write(name, 0, 100, "utf8");
  writeOctal(header, 100, 8, 0o644);
  writeOctal(header, 108, 8, 0);
  writeOctal(header, 116, 8, 0);
  writeOctal(header, 124, 12, size);
  writeOctal(header, 136, 12, 0);
  header.fill(0x20, 148, 156);
  header[156] = "0".charCodeAt(0);
  header.write("ustar\0", 257, 6, "ascii");
  header.write("00", 263, 2, "ascii");
  writeOctal(
    header,
    148,
    8,
    header.reduce((sum, value) => sum + value, 0)
  );
  return header;
}

function paddingFor(size) {
  const remainder = size % BLOCK_SIZE;
  return remainder === 0 ? Buffer.alloc(0) : Buffer.alloc(BLOCK_SIZE - remainder);
}

function readGuid(metaPath) {
  const meta = fs.readFileSync(metaPath);
  const match = /^guid: ([0-9a-f]{32})\r?$/m.exec(meta.toString("utf8"));
  if (!match) {
    throw new Error(`Unity metadata has no valid GUID: ${metaPath}`);
  }
  return { guid: match[1], meta };
}

function generatedFolderMetadata(pathname) {
  const guid = crypto.createHash("sha256").update(pathname).digest("hex").slice(0, 32);
  return {
    guid,
    meta: Buffer.from(
      `fileFormatVersion: 2\nguid: ${guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`
    )
  };
}

function collectAssets(stagedRoot) {
  const rootFolder = generatedFolderMetadata(ASSET_ROOT);
  const assets = [
    {
      guid: rootFolder.guid,
      pathname: ASSET_ROOT,
      meta: rootFolder.meta
    }
  ];
  const pending = [stagedRoot];
  while (pending.length > 0) {
    const directory = pending.pop();
    const children = fs.readdirSync(directory, { withFileTypes: true });
    children.sort((left, right) => left.name.localeCompare(right.name, "en"));
    for (const child of children) {
      if (child.name.startsWith(".") || child.name.endsWith(".meta")) {
        continue;
      }
      const sourcePath = path.join(directory, child.name);
      if (child.isDirectory()) {
        pending.push(sourcePath);
      } else if (!child.isFile()) {
        throw new Error(`Unity package payload contains an unsupported entry: ${sourcePath}`);
      }
      const metaPath = `${sourcePath}.meta`;
      if (!fs.existsSync(metaPath) && !child.isDirectory()) {
        throw new Error(`Unity package payload entry has no metadata: ${sourcePath}`);
      }
      const relativePath = path.relative(stagedRoot, sourcePath).split(path.sep).join("/");
      const pathname = `${ASSET_ROOT}/${relativePath}`;
      const { guid, meta } = fs.existsSync(metaPath)
        ? readGuid(metaPath)
        : generatedFolderMetadata(pathname);
      assets.push({
        guid,
        pathname,
        meta,
        sourcePath: child.isFile() ? sourcePath : undefined
      });
    }
  }
  assets.sort((left, right) => left.guid.localeCompare(right.guid, "en"));
  const uniqueGuids = new Set(assets.map((asset) => asset.guid));
  if (uniqueGuids.size !== assets.length) {
    throw new Error("Unity package payload contains duplicate GUIDs.");
  }
  return assets;
}

async function* archiveChunks(assets) {
  for (const asset of assets) {
    const entries = [];
    if (asset.sourcePath) {
      entries.push({ name: "asset", content: fs.readFileSync(asset.sourcePath) });
    }
    entries.push({ name: "asset.meta", content: asset.meta });
    entries.push({ name: "pathname", content: Buffer.from(asset.pathname) });
    for (const entry of entries) {
      const archivePath = `${asset.guid}/${entry.name}`;
      yield tarHeader(archivePath, entry.content.length);
      yield entry.content;
      yield paddingFor(entry.content.length);
    }
  }
  yield Buffer.alloc(BLOCK_SIZE * 2);
}

async function writeUnityPackage(assets, outputPath) {
  const temporaryOutput = `${outputPath}.tmp`;
  const checksumPath = `${outputPath}.sha256`;
  const temporaryChecksum = `${checksumPath}.tmp`;
  try {
    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.rmSync(temporaryOutput, { force: true });
    fs.rmSync(temporaryChecksum, { force: true });
    await pipeline(
      Readable.from(archiveChunks(assets)),
      zlib.createGzip({ level: zlib.constants.Z_BEST_COMPRESSION, mtime: 0 }),
      fs.createWriteStream(temporaryOutput, { flags: "wx" })
    );
    fs.renameSync(temporaryOutput, outputPath);
    const digest = crypto.createHash("sha256").update(fs.readFileSync(outputPath)).digest("hex");
    fs.writeFileSync(temporaryChecksum, `${digest}  ${path.basename(outputPath)}\n`, "ascii");
    fs.renameSync(temporaryChecksum, checksumPath);
    return digest;
  } finally {
    fs.rmSync(temporaryOutput, { force: true });
    fs.rmSync(temporaryChecksum, { force: true });
  }
}

async function createUnityPackage({ repoRoot, outputPath, unityVersion }) {
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), "unitypackage-create-"));
  try {
    const projectPath = path.join(scratch, "project");
    stageUnityPackage({ repoRoot, projectPath, unityVersion });
    const stagedRoot = path.join(projectPath, ASSET_ROOT.split("/").join(path.sep));
    const assets = collectAssets(stagedRoot);
    const digest = await writeUnityPackage(assets, outputPath);
    console.log(`Created ${outputPath} with ${assets.length} Unity assets.`);
    return { assets, digest };
  } finally {
    fs.rmSync(scratch, { recursive: true, force: true });
  }
}

async function main(args) {
  const repoRoot = path.resolve(__dirname, "..", "..");
  const metadata = JSON.parse(fs.readFileSync(path.join(repoRoot, "package.json"), "utf8"));
  let outputPath = path.join(
    repoRoot,
    ".artifacts",
    "release",
    `${metadata.name}-${metadata.version}.unitypackage`
  );
  for (let index = 0; index < args.length; index += 2) {
    if (args[index] !== "--output" || !args[index + 1]) {
      throw new Error(`Unknown or incomplete argument: ${args[index]}`);
    }
    outputPath = path.resolve(args[index + 1]);
  }
  const unityVersion = JSON.parse(
    fs.readFileSync(path.join(repoRoot, ".github", "unity-versions.json"), "utf8")
  ).release;
  await createUnityPackage({ repoRoot, outputPath, unityVersion });
}

if (require.main === module) {
  main(process.argv.slice(2)).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}

module.exports = { ASSET_ROOT, collectAssets, createUnityPackage, tarHeader, writeUnityPackage };
