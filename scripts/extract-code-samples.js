#!/usr/bin/env node
/**
 * Code Sample Extractor and Validator
 *
 * Extracts C# code samples from Markdown documentation files and generates
 * compilable test files to validate documentation examples stay in sync with
 * the actual codebase.
 *
 * Usage:
 *   node scripts/extract-code-samples.js [options]
 *
 * Options:
 *   --extract-only    Only extract samples, don't generate compilation units
 *   --verbose         Show detailed output
 *   --output-dir      Directory for generated files (default: artifacts/code-samples)
 *   --include-partial Include partial/incomplete samples (marked with ...)
 */

const fs = require("fs");
const path = require("path");

const REPO_ROOT = path.resolve(__dirname, "..");
// Overridable so the self-test can point the scan at a fixture tree. Nothing in CI sets it, so the
// default is the only path that ships.
const SCAN_ROOT = process.env.CODE_SAMPLES_ROOT
  ? path.resolve(process.env.CODE_SAMPLES_ROOT)
  : REPO_ROOT;
const DEFAULT_OUTPUT_DIR = path.join(REPO_ROOT, "artifacts", "code-samples");

// Patterns to identify incomplete/partial samples
const PARTIAL_MARKERS = ["// ...", "/* ... */", "// (existing code)", "// (omitted)", "..."];

// Common using directives for Unity/UnityHelpers code
const COMMON_USINGS = `using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using WallstopStudios.UnityHelpers.Core.Attributes;
using WallstopStudios.UnityHelpers.Core.DataStructure;
using WallstopStudios.UnityHelpers.Core.Extension;
using WallstopStudios.UnityHelpers.Core.Helper;
using WallstopStudios.UnityHelpers.Core.Random;
using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Tags;
`;

// Parse CLI arguments
function parseArgs() {
  const args = process.argv.slice(2);
  const options = {
    extractOnly: false,
    verbose: false,
    outputDir: DEFAULT_OUTPUT_DIR,
    includePartial: false
  };

  for (let i = 0; i < args.length; i++) {
    switch (args[i]) {
      case "--extract-only":
        options.extractOnly = true;
        break;
      case "--verbose":
        options.verbose = true;
        break;
      case "--include-partial":
        options.includePartial = true;
        break;
      case "--output-dir":
        if (i + 1 < args.length) {
          options.outputDir = path.resolve(args[++i]);
        }
        break;
    }
  }

  return options;
}

// Find all markdown files in the repo (excluding node_modules, etc.)
function findMarkdownFiles(rootDir) {
  const files = [];
  const excludeDirs = new Set([
    "node_modules",
    ".git",
    "Library",
    "Temp",
    "obj",
    "artifacts",
    "Logs"
  ]);

  function walk(dir) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        if (!excludeDirs.has(entry.name)) {
          walk(fullPath);
        }
      } else if (entry.isFile() && /\.md$/i.test(entry.name)) {
        files.push(fullPath);
      }
    }
  }

  walk(rootDir);
  return files;
}

// Extract code blocks from markdown content
function extractCodeBlocks(content, filePath) {
  const blocks = [];
  // Normalize line endings to LF for consistent regex matching
  const normalizedContent = content.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  // Match fenced code blocks with language identifier (3+ backticks)
  const codeBlockRegex = /`{3,}(\w+)?\n([\s\S]*?)`{3,}/g;
  let match;
  let lineNumber = 1;

  // Track line numbers
  let lastIndex = 0;

  while ((match = codeBlockRegex.exec(normalizedContent)) !== null) {
    // Calculate line number by counting newlines before this match
    const precedingText = normalizedContent.slice(lastIndex, match.index);
    lineNumber += (precedingText.match(/\n/g) || []).length;

    const language = match[1] || "";
    const code = match[2];

    blocks.push({
      language: language.toLowerCase(),
      code: code,
      filePath: filePath,
      lineNumber: lineNumber,
      raw: match[0]
    });

    // Update for next iteration
    lastIndex = match.index;
    lineNumber += (match[0].match(/\n/g) || []).length;
  }

  return blocks;
}

// Check if a code block is a complete, compilable sample
function isCompleteSample(code, includePartial) {
  // Skip if it has partial markers (unless includePartial is true)
  if (!includePartial) {
    for (const marker of PARTIAL_MARKERS) {
      if (code.includes(marker)) {
        return false;
      }
    }
  }

  // Check for basic C# structure indicators
  const hasClass = /\bclass\s+\w+/.test(code);
  const hasStruct = /\bstruct\s+\w+/.test(code);
  const hasInterface = /\binterface\s+\w+/.test(code);
  const hasMethod = /\b(void|int|float|bool|string|async|Task)\s+\w+\s*\(/.test(code);
  const hasNamespace = /\bnamespace\s+\w+/.test(code);
  const hasUsing = /\busing\s+[\w.]+;/.test(code);

  // Consider it a complete sample if it has a class/struct/interface OR
  // has method definitions (likely inside a class context)
  return hasClass || hasStruct || hasInterface || (hasMethod && hasUsing);
}

// Check if a code block is a standalone snippet (not full class)
function isStandaloneSnippet(code) {
  // Has method calls or statements but no class definition
  const hasMethodCalls = /\w+\.\w+\(/.test(code);
  const hasAssignments = /\w+\s*=\s*/.test(code);
  const hasNoClass = !/\bclass\s+\w+/.test(code);

  return hasNoClass && (hasMethodCalls || hasAssignments);
}

// Sanitize filename from source path
function sanitizeFileName(filePath, lineNumber) {
  const relativePath = path.relative(REPO_ROOT, filePath);
  const baseName = relativePath
    .replace(/[\/\\]/g, "_")
    .replace(/\.md$/i, "")
    .replace(/[^a-zA-Z0-9_-]/g, "_");
  return `${baseName}_L${lineNumber}`;
}

// Wrap standalone snippet in a compilable class
function wrapInCompilableClass(code, className) {
  // Check if code already has usings
  const hasUsings = /\busing\s+[\w.]+;/.test(code);

  // Check if code has a class definition
  const hasClass = /\bclass\s+\w+/.test(code);

  if (hasClass) {
    // Just add usings if needed
    if (hasUsings) {
      return code;
    }
    return `${COMMON_USINGS}\n${code}`;
  }

  // Wrap in a class
  const safeClassName = className.replace(/[^a-zA-Z0-9_]/g, "_");
  const indentedCode = code
    .split("\n")
    .map((line) => `        ${line}`)
    .join("\n");

  return `${COMMON_USINGS}
namespace WallstopStudios.UnityHelpers.CodeSamples
{
    public class ${safeClassName} : MonoBehaviour
    {
        private void DocumentationExample()
        {
${indentedCode}
        }
    }
}
`;
}

// Generate a report of extracted samples
function generateReport(samples, outputDir) {
  const report = {
    timestamp: new Date().toISOString(),
    totalFiles: new Set(samples.map((s) => s.filePath)).size,
    totalSamples: samples.length,
    completeSamples: samples.filter((s) => s.isComplete).length,
    partialSamples: samples.filter((s) => !s.isComplete).length,
    samples: samples.map((s) => ({
      file: path.relative(REPO_ROOT, s.filePath),
      line: s.lineNumber,
      language: s.language,
      isComplete: s.isComplete,
      generatedFile: s.generatedFile ? path.relative(outputDir, s.generatedFile) : null
    }))
  };

  return report;
}

// Main extraction logic
function extractSamples(options) {
  console.log("🔍 Scanning for Markdown files...");

  if (!fs.existsSync(SCAN_ROOT)) {
    console.error(`\n❌ Scan root does not exist: ${SCAN_ROOT}`);
    process.exit(1);
  }

  const mdFiles = findMarkdownFiles(SCAN_ROOT);
  console.log(`   Found ${mdFiles.length} Markdown files`);

  const allSamples = [];
  const csharpSamples = [];

  for (const mdFile of mdFiles) {
    const content = fs.readFileSync(mdFile, "utf8");
    const blocks = extractCodeBlocks(content, mdFile);

    for (const block of blocks) {
      allSamples.push(block);

      // Only process C# blocks
      if (block.language === "csharp" || block.language === "cs") {
        const isComplete = isCompleteSample(block.code, options.includePartial);
        block.isComplete = isComplete;
        block.isSnippet = isStandaloneSnippet(block.code);
        csharpSamples.push(block);
      }
    }
  }

  console.log(`   Total code blocks: ${allSamples.length}`);
  console.log(`   C# code blocks: ${csharpSamples.length}`);
  console.log(`   Complete samples: ${csharpSamples.filter((s) => s.isComplete).length}`);

  return { allSamples, csharpSamples };
}

// Generate compilable files
function generateCompilableFiles(samples, outputDir) {
  console.log("\n📝 Generating compilable files...");

  // Ensure output directory exists
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
  }

  let generatedCount = 0;
  let skippedCount = 0;

  for (const sample of samples) {
    // Only generate for complete samples or snippets that can be wrapped
    if (!sample.isComplete && !sample.isSnippet) {
      skippedCount++;
      continue;
    }

    const className = sanitizeFileName(sample.filePath, sample.lineNumber);
    const fileName = `${className}.cs`;
    const filePath = path.join(outputDir, fileName);

    let code;
    if (sample.isComplete) {
      // Complete sample - may just need usings
      code = wrapInCompilableClass(sample.code, className);
    } else if (sample.isSnippet) {
      // Snippet - wrap in class
      code = wrapInCompilableClass(sample.code, className);
    }

    fs.writeFileSync(filePath, code, "utf8");
    sample.generatedFile = filePath;
    generatedCount++;
  }

  console.log(`   Generated: ${generatedCount} files`);
  console.log(`   Skipped: ${skippedCount} incomplete samples`);

  return { generatedCount, skippedCount };
}

// Generate assembly definition for the extracted samples
function generateAssemblyDefinition(outputDir) {
  const asmdef = {
    name: "WallstopStudios.UnityHelpers.CodeSamples",
    rootNamespace: "WallstopStudios.UnityHelpers.CodeSamples",
    references: ["WallstopStudios.UnityHelpers", "WallstopStudios.UnityHelpers.Editor"],
    includePlatforms: ["Editor"],
    excludePlatforms: [],
    allowUnsafeCode: false,
    overrideReferences: false,
    precompiledReferences: [],
    autoReferenced: false,
    defineConstraints: [],
    versionDefines: [],
    noEngineReferences: false
  };

  const asmdefPath = path.join(outputDir, "WallstopStudios.UnityHelpers.CodeSamples.asmdef");
  fs.writeFileSync(asmdefPath, JSON.stringify(asmdef, null, 2), "utf8");
  console.log(`   Generated: ${path.basename(asmdefPath)}`);
}

// Main entry point
function main() {
  const options = parseArgs();

  console.log("=".repeat(60));
  console.log("📚 Documentation Code Sample Extractor");
  console.log("=".repeat(60));

  const { allSamples, csharpSamples } = extractSamples(options);

  if (options.extractOnly) {
    const report = generateReport(csharpSamples, options.outputDir);
    const reportPath = path.join(options.outputDir, "extraction-report.json");

    if (!fs.existsSync(options.outputDir)) {
      fs.mkdirSync(options.outputDir, { recursive: true });
    }

    fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), "utf8");
    console.log(`\n📊 Report saved to: ${reportPath}`);
  } else {
    generateCompilableFiles(csharpSamples, options.outputDir);
    generateAssemblyDefinition(options.outputDir);

    const report = generateReport(csharpSamples, options.outputDir);
    const reportPath = path.join(options.outputDir, "extraction-report.json");
    fs.writeFileSync(reportPath, JSON.stringify(report, null, 2), "utf8");

    console.log(`\n📊 Report saved to: ${reportPath}`);
    console.log(`📁 Generated files in: ${options.outputDir}`);
  }

  // Print summary by source file
  if (options.verbose) {
    console.log("\n📋 Samples by file:");
    const byFile = new Map();
    for (const sample of csharpSamples) {
      const relPath = path.relative(SCAN_ROOT, sample.filePath);
      if (!byFile.has(relPath)) {
        byFile.set(relPath, []);
      }
      byFile.get(relPath).push(sample);
    }

    for (const [file, samples] of byFile) {
      const complete = samples.filter((s) => s.isComplete).length;
      const partial = samples.length - complete;
      console.log(`   ${file}: ${complete} complete, ${partial} partial`);
    }
  }

  console.log("\n✅ Done!");

  // A scan that found nothing is the shape this gate exists to catch: the docs tree moved, the
  // fence language changed, or the walk stopped descending. Exiting 0 there left the check unable
  // to go red under any input (#556).
  if (csharpSamples.length === 0) {
    console.error("\n❌ No C# code samples found in documentation!");
    process.exit(1);
  }
}

main();
