Param(
    [string[]]$Paths,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPaths,
    [switch]$VerboseOutput
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($VerboseOutput) { $VerbosePreference = 'Continue' }
. (Join-Path $PSScriptRoot 'comment-stripping.ps1')
$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName

function Test-PathWithCase {
    param(
        [string]$FullPath,
        [string]$RepoRoot
    )

    if ([string]::IsNullOrWhiteSpace($FullPath)) { return $false }
    if (-not (Test-Path -LiteralPath $FullPath)) { return $false }

    try {
        $resolved = [System.IO.Path]::GetFullPath($FullPath)
    } catch {
        return $false
    }

    $repoRootResolved = $null
    if (-not [string]::IsNullOrEmpty($RepoRoot)) {
        try {
            $repoRootResolved = [System.IO.Path]::GetFullPath($RepoRoot)
            # GetFullPath preserves a trailing separator on inputs like 'C:\repo\'.
            # Strip it so the `-ceq` short-circuit comparison below matches the
            # repo-root marker `current` (which never carries a trailing sep).
            $repoRootResolved = $repoRootResolved.TrimEnd([IO.Path]::DirectorySeparatorChar, '/')
        } catch { $repoRootResolved = $null }
    }

    $current = $resolved
    while ($true) {
        # Stop at $RepoRoot — trust the repo root exists with correct case.
        if ($repoRootResolved -and ($current -ceq $repoRootResolved)) { break }

        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent) -or $parent -eq $current) { break }
        $leaf = [System.IO.Path]::GetFileName($current)
        if ([string]::IsNullOrEmpty($leaf)) { break }
        try {
            $entries = Get-ChildItem -LiteralPath $parent -Force -ErrorAction Stop
        } catch {
            return $false
        }
        $match = $entries | Where-Object { $_.Name -ceq $leaf }
        if (-not $match) { return $false }
        $current = $parent
    }
    return $true
}

$pathWithCaseCache = [System.Collections.Generic.Dictionary[string, bool]]::new([System.StringComparer]::Ordinal)
$repoFileSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)

function Test-CachedPathWithCase {
    param(
        [string]$FullPath,
        [string]$RepoRoot
    )

    $cacheKey = "$RepoRoot`0$FullPath"
    if ($pathWithCaseCache.ContainsKey($cacheKey)) {
        return $pathWithCaseCache[$cacheKey]
    }

    $result = Test-PathWithCase -FullPath $FullPath -RepoRoot $RepoRoot
    $pathWithCaseCache[$cacheKey] = $result
    return $result
}

function Test-RepoIndexedPath {
    param(
        [string]$FullPath,
        [string]$RepoRoot
    )

    if ($repoFileSet.Count -eq 0) {
        return Test-CachedPathWithCase -FullPath $FullPath -RepoRoot $RepoRoot
    }

    try {
        $repoRelativePath = [System.IO.Path]::GetRelativePath($RepoRoot, $FullPath) -replace '\\', '/'
    } catch {
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($repoRelativePath) -or $repoRelativePath.StartsWith('../') -or $repoRelativePath -eq '..') {
        return $false
    }

    return $repoFileSet.Contains($repoRelativePath)
}

function Write-Violation {
    param(
        [string]$File,
        [int]$LineNumber,
        [string]$Message,
        [string]$Line
    )
    $prefix = "${File}:${LineNumber}:"
    if ($VerboseOutput) {
        Write-Host "$prefix $Message`n  $Line" -ForegroundColor Red
    } else {
        Write-Host "$prefix $Message" -ForegroundColor Red
    }
}

$schemePattern = [regex]'^[a-zA-Z][a-zA-Z0-9+\.-]*:'

function Get-MarkdownLinkTarget {
    param([string]$RawTarget)

    if ([string]::IsNullOrWhiteSpace($RawTarget)) {
        return $null
    }

    $trimmed = $RawTarget.Trim()

    if ($trimmed.StartsWith('<') -and $trimmed.Contains('>')) {
        $closingIndex = $trimmed.IndexOf('>')
        if ($closingIndex -gt 1) {
            $trimmed = $trimmed.Substring(1, $closingIndex - 1)
        }
    }

    $spaceIndex = $trimmed.IndexOfAny(@(' ', "`t"))
    if ($spaceIndex -ge 0) {
        $trimmed = $trimmed.Substring(0, $spaceIndex)
    }

    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    return $trimmed
}

function Is-LocalTarget {
    param([string]$Target)

    if ([string]::IsNullOrWhiteSpace($Target)) {
        return $false
    }

    $trimmed = $Target.Trim()
    if ($schemePattern.IsMatch($trimmed)) {
        return $false
    }

    if ($trimmed.StartsWith('#') -or $trimmed.StartsWith('//')) {
        return $false
    }

    return $true
}

function Resolve-LocalPath {
    param(
        [string]$SourceFile,
        [string]$Target,
        [string]$RepoRoot
    )

    if ([string]::IsNullOrWhiteSpace($Target)) {
        return $null
    }

    $normalized = $Target.Trim() -replace '\\', '/'
    $cutIndex = $normalized.IndexOfAny(@('#', '?'))
    if ($cutIndex -ge 0) {
        $normalized = $normalized.Substring(0, $cutIndex)
    }

    # Strip GitHub Pages baseurl prefix if present
    # This handles absolute paths like /unity-helpers/docs/images/...
    if ($normalized.StartsWith('/unity-helpers/')) {
        $normalized = $normalized.Substring('/unity-helpers'.Length)
    }

    while ($normalized.StartsWith('./')) {
        $normalized = $normalized.Substring(2)
    }

    $normalized = $normalized.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        return $null
    }

    try {
        $normalized = [System.Uri]::UnescapeDataString($normalized)
    } catch {
        # ignore decoding errors and keep original
    }

    $separator = [System.IO.Path]::DirectorySeparatorChar
    $normalized = $normalized.Replace('/', $separator).Replace('\', $separator)

    $sourceDirectory = Split-Path -Path $SourceFile -Parent
    $candidate = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($sourceDirectory, $normalized))

    if (Test-RepoIndexedPath -FullPath $candidate -RepoRoot $RepoRoot) {
        return $candidate
    }

    if ($normalized.Length -gt 0 -and $normalized[0] -ne '.') {
        $rootRelative = $normalized.TrimStart($separator)
        $candidateFromRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($RepoRoot, $rootRelative))
        if (Test-RepoIndexedPath -FullPath $candidateFromRoot -RepoRoot $RepoRoot) {
            return $candidateFromRoot
        }
    }

    return $null
}

$mdPattern = [regex]'[A-Za-z0-9._/\-]+\.md(?:#[A-Za-z0-9_\-]+)?(?![A-Za-z0-9_-])'
# LIMITATION: [^)] patterns cannot handle URLs with parentheses like file(1).md
# This is valid markdown but rare. Files with parens in names should be renamed.
$linkPattern = [regex]'\[[^\]]+\]\([^)]+\)'
$standardLinkPattern = [regex]'(?<!\!)\[(?<text>[^\]]+)\]\((?<target>[^)]+)\)'
$anglePattern = [regex]'<[^>]+>'
$filenameTextLinkPattern = [regex]'\[(?<text>[^\]]+?\.md(?:#[^\]]+)?)\]\((?<target>[^)]+?\.md(?:#[^)]+)?)\)'
# Match actual markdown inline code spans (double-backtick then single-backtick)
$codeSpanPattern = [regex]'(?:``(.+?)``|`([^`\n]+)`)'
$codeSpanMarkdownPathPattern = [regex]'(?i)(^|[^A-Za-z0-9_.-])(?<path>[A-Za-z0-9._/\\-]+\.md(?:#[A-Za-z0-9_\-]+)?)(?![A-Za-z0-9_-])'
$imagePattern = [regex]'!\[[^\]]*\]\((?<target>[^)]+)\)'
$imageReferencePattern = [regex]'!\[[^\]]*\]\[(?<label>[^\]]+)\]'
$definitionPattern = [regex]'^\s*\[(?<label>[^\]]+)\]:\s*(?<rest>.+)$'
# Pattern to detect internal links missing ./ or ../ prefix (starts with letter, not a scheme)
# This is CRITICAL for GitHub Pages - jekyll-relative-links requires explicit relative paths
# BEHAVIOR: This intentionally matches broadly (any link starting with a letter) and then
# filters out external schemes (http://, ftp://, etc.) in the loop below. This approach
# ensures we catch all internal links that need the relative prefix, while correctly
# skipping external links regardless of their scheme.
$missingRelativePrefixPattern = [regex]'\]\((?<target>[a-zA-Z][^)]*)\)'

# Pattern to detect absolute GitHub Pages paths that won't work in CI
# Links like /unity-helpers/ or /unity-helpers/docs/... break when validated locally
# because /unity-helpers is the GitHub Pages site baseurl, not a real directory
$absoluteGitHubPagesPrefixPattern = [regex]'\]\((?<target>/unity-helpers(?:/[^)]*)?)\)'

$violationCount = 0
$codeDocsPattern = [regex]'(?i)docs[\\/][A-Za-z0-9._/\\-]+\.md(?:#[A-Za-z0-9_\-]+)?'
$codeFileExtensions = @('.cs', '.csproj', '.props', '.targets', '.ps1', '.psm1', '.psd1', '.py', '.ts', '.tsx', '.js', '.jsx', '.json', '.yml', '.yaml', '.sh', '.cmd')

function ConvertTo-RepoRelativePath {
    param(
        [string]$RepoRoot,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $candidate = $Path.Trim()
    $fullPath = if ([System.IO.Path]::IsPathRooted($candidate)) {
        [System.IO.Path]::GetFullPath($candidate)
    } else {
        [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($RepoRoot, $candidate))
    }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        return $null
    }

    $repoRootFullPath = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, '/')
    if ($fullPath -ne $repoRootFullPath -and -not $fullPath.StartsWith($repoRootFullPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return ([System.IO.Path]::GetRelativePath($RepoRoot, $fullPath) -replace '\\', '/')
}

# Use git ls-files for efficiency and to respect .gitignore.
# Always anchor at the repository root so invoking this script from a
# subdirectory still yields repo-relative paths.
$allGitFiles = & git -C $repoRoot ls-files --cached --others --exclude-standard 2>$null
if (-not $allGitFiles) {
    Write-Host "Warning: Not in a git repository or git not available, falling back to filesystem scan" -ForegroundColor Yellow
    $allGitFiles = Get-ChildItem -Path $repoRoot -Recurse -File | ForEach-Object {
        [System.IO.Path]::GetRelativePath($repoRoot, $_.FullName) -replace '\\', '/'
    }
}

foreach ($gitFile in $allGitFiles) {
    if (-not [string]::IsNullOrWhiteSpace($gitFile)) {
        $repoFileSet.Add($gitFile) | Out-Null
    }
}

$effectivePaths = @()
if ($Paths -and $Paths.Count -gt 0) {
    $effectivePaths += $Paths
}
if ($AdditionalPaths -and $AdditionalPaths.Count -gt 0) {
    $effectivePaths += $AdditionalPaths
}

$gitFiles = @()
if ($effectivePaths.Count -gt 0) {
    $seenPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($path in $effectivePaths) {
        $relativePath = ConvertTo-RepoRelativePath -RepoRoot $repoRoot -Path $path
        if ($relativePath -and $seenPaths.Add($relativePath)) {
            $gitFiles += $relativePath
        }
    }
} else {
    $gitFiles = $allGitFiles
}

$mdFiles = $gitFiles | Where-Object { $_ -match '\.md$' }

$mdFiles | ForEach-Object {
    $relativePath = $_
    $file = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $file)) { return }
    # Wrap with @() so single-line files (or files with no trailing newline that
    # collapse to a bare scalar under Set-StrictMode) are still treated as a
    # one-element array. Without this, `.Length` would yield character count and
    # `$lines[$index]` would return a [char], which the regex engine would coerce
    # to a single-character string and silently miss every match.
    $lines = @(Get-Content -LiteralPath $file)
    $lineCount = $lines.Length
    $linkDefinitions = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase)
    $inFence = $false

    for ($index = 0; $index -lt $lineCount; $index++) {
        $line = $lines[$index]
        $lineNo = $index + 1
        if ($line -match '^\s*```' -or $line -match '^\s*~~~') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        $definitionMatch = $definitionPattern.Match($line)
        if ($definitionMatch.Success) {
            $label = $definitionMatch.Groups['label'].Value.Trim()
            $rest = $definitionMatch.Groups['rest'].Value
            $target = Get-MarkdownLinkTarget $rest
            if ($label -and $target) {
                $linkDefinitions[$label] = [pscustomobject]@{
                    Target = $target
                    LineNumber = $lineNo
                }
            }
        }
    }

    foreach ($entry in $linkDefinitions.GetEnumerator()) {
        $definitionTarget = $entry.Value.Target
        $definitionLine = $entry.Value.LineNumber
        if (-not $definitionTarget) { continue }
        if (-not (Is-LocalTarget $definitionTarget)) { continue }
        if ($definitionTarget -notmatch '(?i)\.md($|[?#])') { continue }

        $resolvedDefinition = Resolve-LocalPath -SourceFile $file -Target $definitionTarget -RepoRoot $repoRoot
        if (-not $resolvedDefinition) {
            $violationCount++
            Write-Violation -File $file -LineNumber $definitionLine -Message "Reference link target '$definitionTarget' does not resolve to an existing markdown file" -Line ''
        }
    }

    $inFence = $false
    for ($index = 0; $index -lt $lineCount; $index++) {
        $line = $lines[$index]
        $lineNo = $index + 1

        if ($line -match '^\s*```' -or $line -match '^\s*~~~') {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        # Check for inline code that mentions .md files (but not bare extension references)
        # Uses proper backtick-pair parsing to avoid matching text between separate code spans
        foreach ($match in $codeSpanPattern.Matches($line)) {
            $content = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            # Skip glob patterns and bare extensions: .md, *.md, **/*.md, .json, etc.
            if ($content -match '^(\*\*/|\./)?(\*)?\.[\w]+$') { continue }
            $markdownPathMatch = $codeSpanMarkdownPathPattern.Match($content)
            if (-not $markdownPathMatch.Success) { continue }
            $violationCount++
            $markdownPath = $markdownPathMatch.Groups['path'].Value
            Write-Violation -File $file -LineNumber $lineNo -Message "Inline code mentions markdown file '$markdownPath'; use a human-readable link instead" -Line $line
        }

        $stripped = $linkPattern.Replace($line, '')
        $stripped = $anglePattern.Replace($stripped, '')
        $stripped = $codeSpanPattern.Replace($stripped, '')

        if ($mdPattern.IsMatch($stripped)) {
            $violationCount++
            Write-Violation -File $file -LineNumber $lineNo -Message "Bare .md mention; convert to [Readable Text](file.md)" -Line $line
        }

        foreach ($m in $filenameTextLinkPattern.Matches($line)) {
            $text = $m.Groups['text'].Value
            $target = $m.Groups['target'].Value
            if ($text -match '\.md') {
                $violationCount++
                Write-Violation -File $file -LineNumber $lineNo -Message "Link text is a filename; use human-readable text for $target" -Line $line
            }
        }

        # Check for absolute GitHub Pages paths (e.g., /unity-helpers/ or /unity-helpers/docs/...)
        # These break in CI because /unity-helpers is the GitHub Pages baseurl, not a real directory
        # IMPORTANT: First strip inline code (backticks) to avoid false positives from example links
        $lineWithoutInlineCode = $codeSpanPattern.Replace($line, '')
        foreach ($match in $absoluteGitHubPagesPrefixPattern.Matches($lineWithoutInlineCode)) {
            $target = $match.Groups['target'].Value
            $violationCount++
            # Provide helpful fix suggestion
            $suggestedFix = $target -replace '^/unity-helpers/?', './'
            if ($suggestedFix -eq './') { $suggestedFix = './README.md' }
            Write-Violation -File $file -LineNumber $lineNo -Message "Absolute GitHub Pages path '$target' will break in CI; use relative path instead (e.g., '$suggestedFix')" -Line $line
        }

        # Check for internal links missing ./ or ../ relative prefix
        # This is CRITICAL for GitHub Pages - jekyll-relative-links requires explicit relative paths
        # Skip: external links (http/https/mailto), anchors (#), images (!), and links already with ./
        # IMPORTANT: First strip inline code (backticks) to avoid false positives from example links
        # LIMITATIONS of inline code stripping:
        # - Does not handle escaped backticks (\`) - these are rare in practice
        # - Triple+ backticks on same line may have unexpected interactions
        # - Nested backticks are not standard markdown
        # The -replace pattern removes double-backtick spans first (``text``), then single spans
        foreach ($match in $missingRelativePrefixPattern.Matches($lineWithoutInlineCode)) {
            $target = $match.Groups['target'].Value
            # Skip external links (http:// https:// mailto: etc.)
            if ($target -match '^[a-zA-Z][a-zA-Z0-9+\.-]*:') { continue }
            # Skip if target is empty or starts with ./ or ../
            if ($target -match '^\.\.?/') { continue }
            # Skip anchor-only links
            if ($target.StartsWith('#')) { continue }
            # This is a bare path without relative prefix - flag it
            $violationCount++
            Write-Violation -File $file -LineNumber $lineNo -Message "Internal link '$target' missing relative prefix (./ or ../); jekyll-relative-links requires explicit relative paths" -Line $line
        }

        foreach ($match in $standardLinkPattern.Matches($line)) {
            $rawTarget = $match.Groups['target'].Value
            $resolvedTarget = Get-MarkdownLinkTarget $rawTarget
            if (-not $resolvedTarget) { continue }
            if (-not (Is-LocalTarget $resolvedTarget)) { continue }
            if ($resolvedTarget -notmatch '(?i)\.md($|[?#])') { continue }

            $linkPath = Resolve-LocalPath -SourceFile $file -Target $resolvedTarget -RepoRoot $repoRoot
            if (-not $linkPath) {
                $violationCount++
                Write-Violation -File $file -LineNumber $lineNo -Message "Markdown link target '$resolvedTarget' does not resolve to an existing markdown file" -Line $line
            }
        }

        foreach ($match in $imagePattern.Matches($line)) {
            $rawTarget = $match.Groups['target'].Value
            $resolvedTarget = Get-MarkdownLinkTarget $rawTarget
            if ($resolvedTarget -and (Is-LocalTarget $resolvedTarget)) {
                $imagePath = Resolve-LocalPath -SourceFile $file -Target $resolvedTarget -RepoRoot $repoRoot
                if (-not $imagePath) {
                    $violationCount++
                    Write-Violation -File $file -LineNumber $lineNo -Message "Image target '$resolvedTarget' does not resolve to a file" -Line $line
                }
            }
        }

        foreach ($match in $imageReferencePattern.Matches($line)) {
            $label = $match.Groups['label'].Value.Trim()
            if ($linkDefinitions.ContainsKey($label)) {
                $definition = $linkDefinitions[$label]
                $resolvedTarget = $definition.Target
                if ($resolvedTarget -and (Is-LocalTarget $resolvedTarget)) {
                    $imagePath = Resolve-LocalPath -SourceFile $file -Target $resolvedTarget -RepoRoot $repoRoot
                    if (-not $imagePath) {
                        $violationCount++
                        Write-Violation -File $file -LineNumber $lineNo -Message "Image reference '$label' points to '$resolvedTarget' which does not resolve" -Line $line
                    }
                }
            }
        }
    }
}

# Process code files for docs references.
# Comment-stripping handles fictional paths in language comments and docstrings
# (e.g., PowerShell <# .EXAMPLE #>, C# /// xml-doc, Python triple-quote module docs).
# Test files remain excluded because their fixture content is encoded as live string
# literals — masking only protects comments, and rewriting every fictional fixture
# string is out of scope for this lint.
$codeFiles = $gitFiles | Where-Object {
    $ext = [System.IO.Path]::GetExtension($_)
    if (-not ($codeFileExtensions -contains $ext)) { return $false }
    if ($_ -match '(?i)(^|[\\/])tests?[\\/]') { return $false }
    if ($_ -match '(?i)(^|[\\/])test_[^\\/]+\.py$') { return $false }
    if ($_ -match '(?i)(^|[\\/])[^\\/]+_test\.py$') { return $false }
    return $true
}

$codeFiles | ForEach-Object {
    $relativePath = $_
    $file = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $file)) { return }
    $language = Get-LanguageFromExtension -Path $file
    if (-not $language) { return }
    Write-Verbose "Scanning code file: $relativePath (language: $language)"
    $text = [System.IO.File]::ReadAllText($file)
    if (-not $codeDocsPattern.IsMatch($text)) { return }
    $lines = @($text -split '\r?\n')
    if ($lines.Count -gt 1 -and $lines[-1] -eq '') {
        $lines = $lines[0..($lines.Count - 2)]
    }
    $maskedLines = Get-CommentMaskedLines -Lines $lines -Language $language
    for ($index = 0; $index -lt $lines.Length; $index++) {
        $originalLine = $lines[$index]
        $scanLine = if ($index -lt $maskedLines.Length) { $maskedLines[$index] } else { $originalLine }
        $lineNo = $index + 1
        foreach ($match in $codeDocsPattern.Matches($scanLine)) {
            $rawTarget = $match.Value
            $normalizedTarget = $rawTarget -replace '\\', '/'
            $resolvedPath = Resolve-LocalPath -SourceFile $file -Target $normalizedTarget -RepoRoot $repoRoot
            if (-not $resolvedPath) {
                $violationCount++
                Write-Violation -File $file -LineNumber $lineNo -Message "Source reference '$normalizedTarget' does not resolve to an existing markdown file" -Line $originalLine
            }
        }
    }
}

if ($violationCount -gt 0) {
    Write-Host "Markdown link lint failed: $violationCount issue(s) found." -ForegroundColor Red
    exit 1
} else {
    Write-Host "Markdown link lint passed: no issues found." -ForegroundColor Green
    exit 0
}

