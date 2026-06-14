Param(
    [string[]]$Paths,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info {
    param([string]$Message)

    if ($VerboseOutput) {
        Write-Host "[markdown-fence-fix] $Message" -ForegroundColor Cyan
    }
}

function Get-RepoRoot {
    $repoRoot = & git rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($repoRoot)) {
        return ([string]$repoRoot).Trim()
    }

    return (Get-Location).Path
}

function Get-MarkdownPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [string[]]$CandidatePaths
    )

    $results = New-Object System.Collections.Generic.List[string]
    if ($null -eq $CandidatePaths -or $CandidatePaths.Count -eq 0) {
        Push-Location $RepoRoot
        try {
            $CandidatePaths = @(& git diff --cached --name-only --diff-filter=ACM -- '*.md' '*.markdown' 2>$null)
        }
        finally {
            Pop-Location
        }
    }

    foreach ($path in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $normalizedPath = $path.Replace('\', '/')
        if ($normalizedPath -notlike '*.md' -and $normalizedPath -notlike '*.markdown') {
            continue
        }

        $fullPath = if ([System.IO.Path]::IsPathRooted($path)) {
            $path
        }
        else {
            Join-Path -Path $RepoRoot -ChildPath $path
        }

        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $results.Add($fullPath) | Out-Null
        }
    }

    return @($results | Sort-Object -Unique)
}

function Test-LooksLikeJson {
    param([string]$Text)

    $trimmed = $Text.Trim()
    if (-not (($trimmed.StartsWith('{') -and $trimmed.EndsWith('}')) -or ($trimmed.StartsWith('[') -and $trimmed.EndsWith(']')))) {
        return $false
    }

    try {
        $null = $trimmed | ConvertFrom-Json -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Test-LooksLikeYaml {
    param([string[]]$Lines)

    $keyValueCount = 0
    $contentCount = 0
    foreach ($line in $Lines) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        $contentCount++
        if ($trimmed -match '^[-]?\s*[A-Za-z0-9_.-]+:\s*[^:]*$' -or $trimmed -match '^-\s+[A-Za-z0-9_.-]+:\s*') {
            $keyValueCount++
        }
    }

    return ($contentCount -gt 0 -and $keyValueCount -eq $contentCount)
}

function Get-InferredFenceLanguage {
    param([string[]]$Lines)

    $contentLines = @($Lines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($contentLines.Count -eq 0) {
        return 'text'
    }

    $trimmedLines = @($contentLines | ForEach-Object { $_.Trim() })
    $text = ($contentLines -join "`n")
    $first = $trimmedLines[0]

    if ($first -match '^(graph|flowchart)\s+' -or $first -match '^(sequenceDiagram|classDiagram|stateDiagram|erDiagram|journey|gantt|pie)\b') {
        return 'mermaid'
    }

    if ($first -match '^diff --git ' -or $first -match '^(---|\+\+\+) ' -or $first -match '^@@ ') {
        return 'diff'
    }

    if (Test-LooksLikeJson -Text $text) {
        return 'json'
    }

    if ($first -match '^<\?xml\b' -or ($first -match '^</?[A-Za-z!][^>]*>' -and $text -match '</?[A-Za-z!][^>]*>')) {
        return 'xml'
    }

    if ($text -match '(?m)^\s*(Param\s*\(|function\s+[A-Za-z0-9_-]+\s*\{|Write-Host\b|Get-[A-Za-z]+\b|Set-[A-Za-z]+\b|Invoke-[A-Za-z]+\b|\$env:|\$LASTEXITCODE\b)') {
        return 'powershell'
    }

    if ($text -match '(?m)^\s*(#!/usr/bin/env bash|#!/bin/(ba)?sh|npm\s+run\b|git\s+\S+|bash\s+\S+|pwsh\s+|dotnet\s+|node\s+|export\s+[A-Za-z_][A-Za-z0-9_]*=|cd\s+|chmod\s+|curl\s+|UNITY_[A-Z0-9_]+=)' -or
        $text -match '(?m)^\s*(if\s+\[|for\s+\S+\s+in\s+.+;\s+do|while\s+.+;\s+do)') {
        return 'bash'
    }

    if ($text -match '(?m)^\s*(using\s+[A-Za-z0-9_.]+;|namespace\s+[A-Za-z0-9_.]+|public\s+(sealed\s+|static\s+|partial\s+)?(class|struct|enum|interface)\b|private\s+|protected\s+|internal\s+)' -or
        $text -match '\b(UnityEngine|MonoBehaviour|ScriptableObject|IEnumerable<|List<|Dictionary<)\b') {
        return 'csharp'
    }

    if ($text -match '(?m)^\s*(const|let|var)\s+[A-Za-z_$][A-Za-z0-9_$]*\s*=' -or
        $text -match '(?m)^\s*(async\s+)?function\s+[A-Za-z_$][A-Za-z0-9_$]*\s*\(' -or
        $text -match '\b(console\.log|require\(|process\.argv|module\.exports|=>)\b') {
        return 'javascript'
    }

    if (Test-LooksLikeYaml -Lines $contentLines) {
        return 'yaml'
    }

    return 'text'
}

function Repair-MarkdownFenceLanguages {
    param([string]$Content)

    $newline = if ($Content.Contains("`r`n")) { "`r`n" } else { "`n" }
    $lines = [regex]::Split($Content, '\r\n|\n|\r')
    $inFence = $false
    $openingIndex = -1
    $openingIndent = ''
    $openingFence = ''
    $openingChar = ''
    $openingLength = 0
    $openingMissingLanguage = $false
    $bodyLines = New-Object System.Collections.Generic.List[string]
    $fixCount = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (-not $inFence) {
            if ($line -match '^([ \t]{0,3})(`{3,}|~{3,})([ \t]*)(.*)$') {
                $info = ([string]$Matches[4]).Trim()
                $inFence = $true
                $openingIndex = $i
                $openingIndent = $Matches[1]
                $openingFence = $Matches[2]
                $openingChar = $openingFence.Substring(0, 1)
                $openingLength = $openingFence.Length
                $openingMissingLanguage = [string]::IsNullOrWhiteSpace($info)
                $bodyLines.Clear()
            }
            continue
        }

        if ($line -match '^([ \t]{0,3})(`{3,}|~{3,})[ \t]*$') {
            $closingFence = $Matches[2]
            if ($closingFence.Substring(0, 1) -eq $openingChar -and $closingFence.Length -ge $openingLength) {
                if ($openingMissingLanguage) {
                    $language = Get-InferredFenceLanguage -Lines @($bodyLines)
                    $lines[$openingIndex] = "$openingIndent$openingFence$language"
                    $fixCount++
                }

                $inFence = $false
                $openingIndex = -1
                $openingIndent = ''
                $openingFence = ''
                $openingChar = ''
                $openingLength = 0
                $openingMissingLanguage = $false
                $bodyLines.Clear()
                continue
            }
        }

        if ($openingMissingLanguage) {
            $bodyLines.Add($line) | Out-Null
        }
    }

    return [pscustomobject]@{
        Content  = [string]::Join($newline, $lines)
        FixCount = $fixCount
    }
}

$repoRoot = Get-RepoRoot
$markdownPaths = Get-MarkdownPaths -RepoRoot $repoRoot -CandidatePaths $Paths
$changedFiles = 0
$changedFences = 0
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

foreach ($path in $markdownPaths) {
    $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $repair = Repair-MarkdownFenceLanguages -Content $content
    if ($repair.FixCount -eq 0 -or $repair.Content -ceq $content) {
        continue
    }

    [System.IO.File]::WriteAllText($path, $repair.Content, $utf8NoBom)
    $changedFiles++
    $changedFences += $repair.FixCount
    Write-Info "Added language specifiers to $($repair.FixCount) fenced code block(s): $path"
}

if ($changedFences -gt 0) {
    Write-Host "[markdown-fence-fix] Added language specifiers to $changedFences fenced code block(s) across $changedFiles file(s)." -ForegroundColor Green
}

exit 0
