import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import prettier from "prettier";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const skills = path.join(root, ".llm", "skills");
const references = path.join(root, ".llm", "references");
const manifestPath = path.join(root, ".llm", "skill-splits.json");
const mode = process.argv[2];
const maxLines = 199;
const chunkLines = 180;
const prettierOptions = { ...(await prettier.resolveConfig(manifestPath)), filepath: manifestPath };

async function writeManifest(manifest) {
  const formatted = await prettier.format(JSON.stringify(manifest), prettierOptions);
  fs.writeFileSync(manifestPath, formatted);
}

if (!["--write", "--check", "--plan", "--refresh"].includes(mode)) {
  process.stderr.write("Usage: node scripts/migrate-agent-skills.mjs --plan|--write|--check|--refresh\n");
  process.exit(2);
}

function digest(text) {
  return crypto.createHash("sha256").update(text).digest("hex");
}

function routerDigest(text) {
  const body = text.slice(prefixOf(text).length);
  return digest(body.split("\n").map((line) => line.trimEnd()).filter(Boolean).join("\n"));
}

function lineCount(text) {
  return text.trimEnd().split("\n").length;
}

function prefixOf(text) {
  const trigger = /<!--\s*trigger:[^\r\n]*?-->\n\n/.exec(text);
  if (!trigger || !text.startsWith("# ")) {
    throw new Error("Skill must start with a title and trigger comment");
  }
  return text.slice(0, trigger.index + trigger[0].length);
}

function fencedLines(lines) {
  let character = null;
  let width = 0;
  return lines.map((line) => {
    const match = /^ {0,3}(`{3,}|~{3,})/.exec(line);
    const wasFenced = character !== null;
    if (match && character === null) {
      character = match[1][0];
      width = match[1].length;
    } else if (match && match[1][0] === character && match[1].length >= width) {
      character = null;
      width = 0;
    }
    return wasFenced || character !== null || match !== null;
  });
}

function chunksOf(body) {
  const lines = body.match(/[^\n]*\n|[^\n]+$/g) ?? [];
  const fenced = fencedLines(lines.map((line) => line.replace(/\n$/, "")));
  const chunks = [];
  for (let start = 0; start < lines.length; ) {
    if (lines.length - start <= chunkLines) {
      chunks.push(lines.slice(start).join(""));
      break;
    }

    const candidates = [];
    for (let end = start + 1; end <= Math.min(start + chunkLines, lines.length); end++) {
      if (lines[end - 1].trim() !== "" || fenced[end - 1]) continue;
      const next = lines[end] ?? "";
      const rank = /^## /.test(next) ? 3 : /^### /.test(next) ? 2 : 1;
      candidates.push({ end, rank });
    }
    const preferred = candidates.filter((item) => item.end >= start + 120);
    const choice = (preferred.length ? preferred : candidates).sort(
      (left, right) => right.rank - left.rank || right.end - left.end
    )[0];
    if (!choice) throw new Error(`No safe split boundary within ${chunkLines} lines`);
    chunks.push(lines.slice(start, choice.end).join(""));
    start = choice.end;
  }
  return chunks;
}

function rewriteLinks(text, name, direction) {
  let fenced = false;
  let fenceCharacter = null;
  let fenceWidth = 0;
  return text
    .split(/(?<=\n)/)
    .map((line) => {
      const marker = /^ {0,3}(`{3,}|~{3,})/.exec(line);
      if (marker) {
        if (!fenced) {
          fenced = true;
          fenceCharacter = marker[1][0];
          fenceWidth = marker[1].length;
        } else if (marker[1][0] === fenceCharacter && marker[1].length >= fenceWidth) {
          fenced = false;
        }
        return line;
      }
      if (fenced) return line;
      return line.replace(/\]\(([^)]+)\)/g, (whole, target) => {
        if (direction === "forward" && target.startsWith("#")) {
          return `](../skills/${name}.md${target})`;
        }
        if (direction === "reverse" && target.startsWith(`../skills/${name}.md#`)) {
          return `](${target.slice(`../skills/${name}.md`.length)})`;
        }
        if (!/^\.{1,2}\//.test(target)) return whole;
        const [file, fragment = ""] = target.split("#", 2);
        const from = direction === "forward" ? skills : references;
        const to = direction === "forward" ? references : skills;
        const absolute = path.resolve(from, file);
        let relative = path.relative(to, absolute).replaceAll(path.sep, "/");
        if (!relative.startsWith(".")) relative = `./${relative}`;
        return `](${relative}${fragment ? `#${fragment}` : ""})`;
      });
    })
    .join("");
}

function referenceText(name, index, body) {
  return `# ${name} - Part ${index + 1}\n\n## Split Content\n\n${rewriteLinks(body, name, "forward")}`;
}

function referenceBody(text, part) {
  const heading = /^# [^\n]+\n\n(?:## Split Content\n\n)?/.exec(text);
  if (!heading) throw new Error(`Missing reference title: ${part}`);
  return text.slice(heading[0].length);
}

function slug(text, used) {
  const base = text
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/<[^>]+>/g, "")
    .toLowerCase()
    .trim()
    .replace(/[^\p{L}\p{N}_\- ]/gu, "")
    .replace(/ /g, "-");
  let result = base;
  let suffix = 0;
  while (used.has(result)) result = `${base}-${++suffix}`;
  used.add(result);
  return result;
}

function routerText(prefix, name, chunks) {
  const headings = [];
  chunks.forEach((chunk, index) => {
    const lines = chunk.split("\n");
    const fenced = fencedLines(lines);
    const used = new Set([slug(`${name} - Part ${index + 1}`, new Set()), "split-content"]);
    lines.forEach((line, lineIndex) => {
      if (!fenced[lineIndex] && /^#{2,6} /.test(line)) {
        const anchor = slug(line.replace(/^#{2,6} /, ""), used);
        const target = `../references/${name}-part-${index + 1}.md#${anchor}`;
        if (line.startsWith("## ") || line.includes("[")) {
          headings.push(`${line}\n\n[Read section](${target})`);
        } else {
          headings.push(line.replace(/^(#{3,6}) (.*)$/, `$1 [$2](${target})`));
        }
      }
    });
  });
  const navigation = chunks.map(
    (_, index) => `- [Part ${index + 1}](../references/${name}-part-${index + 1}.md)`
  );
  return `${prefix}## Reference Parts\n\n${navigation.join("\n")}\n\n${headings.join("\n\n")}\n`;
}

function checkEntry(entry) {
  const source = path.join(skills, `${entry.name}.md`);
  const canonical = fs.readFileSync(source, "utf8");
  if (lineCount(canonical) > maxLines) throw new Error(`Oversized router: ${entry.name}`);
  if (entry.routerSha256 && routerDigest(canonical) !== entry.routerSha256) {
    throw new Error(`Router changed outside refresh: ${entry.name}`);
  }
  const prefix = prefixOf(canonical);
  let reconstructed = prefix;
  for (const part of entry.parts) {
    const reference = path.join(references, part);
    const text = fs.readFileSync(reference, "utf8");
    if (lineCount(text) > maxLines) throw new Error(`Oversized reference: ${part}`);
    if (!canonical.includes(`](../references/${part})`)) {
      throw new Error(`Router does not link to ${part}`);
    }
    reconstructed += rewriteLinks(referenceBody(text, part), entry.name, "reverse");
  }
  if (digest(reconstructed) !== entry.sha256) {
    throw new Error(`Reconstruction mismatch: ${entry.name}`);
  }
  const chunks = entry.parts.map((part) =>
    rewriteLinks(referenceBody(fs.readFileSync(path.join(references, part), "utf8"), part), entry.name, "reverse")
  );
  const target = routerText(prefix, entry.name, chunks);
  const links = target.match(/\]\(\.\.\/references\/[^)]*\)/g) ?? [];
  for (const link of links) {
    if (!canonical.includes(link)) throw new Error(`Missing anchored section link in ${entry.name}: ${link}`);
  }
}

if (mode === "--refresh") {
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  const updates = [];
  for (const entry of manifest.skills) {
    const source = path.join(skills, `${entry.name}.md`);
    const canonical = fs.readFileSync(source, "utf8");
    if (entry.routerSha256 && routerDigest(canonical) !== entry.routerSha256) {
      throw new Error(`Router changed outside refresh: ${entry.name}`);
    }
    const prefix = prefixOf(canonical);
    const chunks = entry.parts.map((part) => {
      const contents = fs.readFileSync(path.join(references, part), "utf8");
      return rewriteLinks(referenceBody(contents, part), entry.name, "reverse");
    });
    const original = prefix + chunks.join("");
    const router = routerText(prefix, entry.name, chunks);
    const referenceTexts = chunks.map((chunk, index) => referenceText(entry.name, index, chunk));
    if (lineCount(router) > maxLines || referenceTexts.some((text) => lineCount(text) > maxLines)) {
      throw new Error(`Split exceeds ${maxLines} lines after refresh: ${entry.name}`);
    }
    updates.push({ entry, source, router, referenceTexts });
    entry.sha256 = digest(original);
    entry.routerSha256 = routerDigest(router);
  }
  for (const item of updates) {
    item.entry.parts.forEach((part, index) =>
      fs.writeFileSync(path.join(references, part), item.referenceTexts[index])
    );
    fs.writeFileSync(item.source, item.router);
  }
  await writeManifest(manifest);
}

if (mode === "--write" || mode === "--plan") {
  const entries = fs.existsSync(manifestPath)
    ? JSON.parse(fs.readFileSync(manifestPath, "utf8")).skills
    : [];
  const known = new Set(entries.map((entry) => entry.name));
  const pending = [];
  for (const filename of fs.readdirSync(skills).sort()) {
    if (!filename.endsWith(".md") || filename === "index.md") continue;
    const source = path.join(skills, filename);
    const original = fs.readFileSync(source, "utf8");
    if (lineCount(original) <= maxLines || known.has(path.basename(filename, ".md"))) continue;
    const name = path.basename(filename, ".md");
    const prefix = prefixOf(original);
    const chunks = chunksOf(original.slice(prefix.length));
    const parts = chunks.map((_, index) => `${name}-part-${index + 1}.md`);
    const router = routerText(prefix, name, chunks);
    if (lineCount(router) > maxLines) throw new Error(`Router exceeds ${maxLines} lines: ${name}`);
    const referenceTexts = chunks.map((chunk, index) => referenceText(name, index, chunk));
    if (referenceTexts.some((text) => lineCount(text) > maxLines)) {
      throw new Error(`Reference exceeds ${maxLines} lines: ${name}`);
    }
    const reconstructed = prefix + referenceTexts.map((text) =>
      rewriteLinks(referenceBody(text, name), name, "reverse")
    ).join("");
    if (reconstructed !== original) throw new Error(`Migration would change source content: ${name}`);
    pending.push({ source, parts, referenceTexts, router });
    entries.push({ name, sha256: digest(original), parts, routerSha256: routerDigest(router) });
  }
  if (mode === "--plan") {
    if (pending.length === 0) {
      process.stdout.write("All skills are already below 200 lines.\n");
      process.exit(0);
    }
    const largest = pending.reduce(
      (current, item) => lineCount(item.router) > lineCount(current.router) ? item : current,
      pending[0]
    );
    process.stdout.write(`Ready to split ${pending.length} skills into ${pending.reduce((count, item) => count + item.parts.length, 0)} references. Largest router: ${path.basename(largest.source)} (${lineCount(largest.router)} lines).\n`);
    process.exit(0);
  }
  for (const item of pending) {
    item.parts.forEach((part, index) =>
      fs.writeFileSync(path.join(references, part), item.referenceTexts[index])
    );
    fs.writeFileSync(item.source, item.router);
  }
  entries.sort((left, right) => (left.name < right.name ? -1 : left.name > right.name ? 1 : 0));
  await writeManifest({ version: 1, skills: entries });
}

const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
for (const entry of manifest.skills) checkEntry(entry);
for (const filename of fs.readdirSync(skills)) {
  if (filename.endsWith(".md") && filename !== "index.md") {
    const source = fs.readFileSync(path.join(skills, filename), "utf8");
    if (lineCount(source) > maxLines) throw new Error(`Skill exceeds ${maxLines} lines: ${filename}`);
  }
}
process.stdout.write(`Verified ${manifest.skills.length} reconstructed skill splits and all skill sizes.\n`);
