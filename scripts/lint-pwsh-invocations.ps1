#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Detect anti-patterns in bash -> PowerShell (.ps1) script invocations.

.DESCRIPTION
    PowerShell's `-File` CLI mode does NOT honor the POSIX `--` argument
    separator. Passing `--` as an argument surfaces as:

      "Parameter cannot be processed because the parameter name ''
       is ambiguous."

    This lint scans the repo for invocation anti-patterns so we catch the
    mistake at commit / CI time rather than during a rare hook branch.

    Error codes emitted:
      PWS001 - `pwsh|powershell[.exe] -File|-f <script> --` (the core bug)
      PWS002 - In-process `& <script>.ps1 --` inside scripts/tests/*.ps1
               (tests MUST exercise the same invocation path production uses;
               the in-process call operator masks CLI-binding bugs)
      PWS003 - A scripts/<name>.ps1 file invokes `pwsh|powershell[.exe]
               -NoProfile -File|-f scripts/<sibling>.ps1` via subprocess when it already
               runs inside a PowerShell host. Windows PowerShell 5.1 hosts
               may not have pwsh on PATH, and the subprocess boundary wastes
               startup time; dot-source a shared helper or use in-process
               `&` with a function that does not call `exit` instead.
               Opt-out per file: add a top-of-file comment marker
               `# lint-pwsh-invocations: allow-subprocess-pwsh` with a
               one-line rationale (e.g. "called script uses `exit` heavily;
               subprocess isolation required").
      PWS004 - `pwsh|powershell[.exe] -File|-f .githooks/<extensionless-hook>`.
               PowerShell -File targets must be .ps1 files on every supported
               host; run the extensionless hook directly or invoke the
               companion `.githooks/<hook>.ps1` implementation.
      PWS005 - A script declares an array parameter without BOTH a
               `[Parameter(ValueFromRemainingArguments = $true)]` sibling and
               `[CmdletBinding(PositionalBinding = $false)]`, so
               `pwsh -File <script> -Paths a b` binds only 'a' and puts 'b' on
               whichever parameter is positionally next -- silently.
      PWS006 - `Start-Process -ArgumentList <array|variable>`. Start-Process
               joins the array with spaces and adds NO quoting, so an argument
               containing a space is split into separate arguments. Use
               `System.Diagnostics.ProcessStartInfo.ArgumentList`, which quotes
               each element. Opt-out per site: a comment marker
               `# lint-pwsh-invocations: allow-start-process-argument-list <rationale>`
               on the invocation's line or in the comment block directly above.

    Scanned paths:
      - *.sh
      - .githooks/*
      - .github/workflows/*.yml
      - scripts/**/*.ps1             (PWS003 only applies to top-level scripts/*.ps1)
      - package.json

    Multi-line invocation detection:
      Bash / YAML `run: |` blocks may split a `pwsh ... -File ... -- ...`
      invocation across physical lines using `\` continuations. We first scan
      each physical line, then compute a "logically joined" view — any line
      ending in a trailing `\` (ignoring trailing whitespace) is joined with
      the next line — and scan that view too. Violations found only on the
      joined view report the physical line number where the invocation STARTS.

    Excluded:
      - Lines inside PowerShell comment-based help blocks (open/close markers).
      - Lines beginning with a '#' comment character in .ps1, .sh, .yml, .yaml
        files. (Caveat: `#` inside quoted YAML strings is treated as a
        comment start; we accept this minor edge case because scanning for
        invocations inside a quoted YAML string is not a pattern we care
        about.)
      - This script itself (scripts/lint-pwsh-invocations.ps1) and the
        corresponding test script, which use the anti-pattern as fixture text.

.PARAMETER VerboseOutput
    Show per-file diagnostics including files that were scanned with no
    violations.

.EXAMPLE
    ./scripts/lint-pwsh-invocations.ps1
    Lint the whole repo.

.EXAMPLE
    ./scripts/lint-pwsh-invocations.ps1 -VerboseOutput
    Lint with verbose per-file output.
#>
param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[lint-pwsh-invocations] $msg" -ForegroundColor Cyan }
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$selfRel = 'scripts/lint-pwsh-invocations.ps1'
$selfTestRel = 'scripts/tests/test-lint-pwsh-invocations.ps1'

function Get-RepoRelativePath {
    param([string]$FullPath)
    $normalized = $FullPath.Replace('\', '/')
    $root = $repoRoot.Replace('\', '/')
    if ($normalized.StartsWith($root + '/')) {
        return $normalized.Substring($root.Length + 1)
    }
    return $normalized
}

function Get-TargetFiles {
    $results = [System.Collections.Generic.List[string]]::new()

    # Prune excluded roots before recursion; an explicit symlink root would otherwise be followed.
    Get-ChildItem -LiteralPath $repoRoot -File -Filter '*.sh' -ErrorAction SilentlyContinue |
        ForEach-Object { $results.Add($_.FullName) | Out-Null }
    foreach ($directory in (Get-ChildItem -LiteralPath $repoRoot -Directory -ErrorAction SilentlyContinue)) {
        if ($directory.Name -match '^(node_modules|site|\.git)$' -or
            ($directory.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            continue
        }
        Get-ChildItem -LiteralPath $directory.FullName -Recurse -File -Filter '*.sh' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # .githooks/* (non-recursive files)
    $hooksDir = Join-Path $repoRoot '.githooks'
    if (Test-Path -LiteralPath $hooksDir) {
        Get-ChildItem -LiteralPath $hooksDir -File -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # .github/workflows/*.yml
    $wfDir = Join-Path (Join-Path $repoRoot '.github') 'workflows'
    if (Test-Path -LiteralPath $wfDir) {
        Get-ChildItem -LiteralPath $wfDir -File -Filter '*.yml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
        Get-ChildItem -LiteralPath $wfDir -File -Filter '*.yaml' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # scripts/**/*.ps1. PWS003 remains scoped below to top-level scripts/*.ps1,
    # but the other pwsh invocation checks should cover nested automation too.
    $scriptsDir = Join-Path $repoRoot 'scripts'
    if (Test-Path -LiteralPath $scriptsDir) {
        Get-ChildItem -LiteralPath $scriptsDir -Recurse -File -Filter '*.ps1' -ErrorAction SilentlyContinue |
            ForEach-Object { $results.Add($_.FullName) | Out-Null }
    }

    # package.json
    $pkgJson = Join-Path $repoRoot 'package.json'
    if (Test-Path -LiteralPath $pkgJson) { $results.Add($pkgJson) | Out-Null }

    return $results | Sort-Object -Unique
}

$targets = @(Get-TargetFiles)
Write-Info "Scanning $($targets.Count) file(s)"
$sources = @(foreach ($file in $targets) {
    $relative = Get-RepoRelativePath $file
    if ($relative -eq $selfRel -or $relative -eq $selfTestRel) { continue }
    try {
        $lines = @(Get-Content -LiteralPath $file -ErrorAction Stop)
    }
    catch {
        Write-Info "Skipping unreadable file: $relative"
        continue
    }
    @{ path = $relative; lines = $lines }
})

# PWS005: a script that declares ANY [string[]] parameter must also declare a
# ValueFromRemainingArguments sibling. `pwsh -File <script> -Paths a b c` binds ONLY `a` and
# silently drops the rest, so a multi-file invocation lints one file and reports success -- the
# worst shape a gate can fail in, and the shape `.llm/context.md` documents for lint-tests.ps1.
# Found in six scripts at once (session 221); the pattern was already written down in
# .llm/skills/bash-pwsh-invocation.md and nothing enforced it.
#
# The rule originally matched the parameter NAMED `Paths`, which is not what makes -File binding
# drop arguments -- the array-ness is. Four more scripts carried the same latent defect under
# other names (#556).
#
# And a ValueFromRemainingArguments sibling ALONE does not fix it. With positional binding on --
# the default -- `pwsh -File s.ps1 -Versions a b` binds 'b' to whichever named parameter is
# positionally next and the catch-all never sees it. Measured: an ensure-editor.ps1 invocation
# silently put 'b' in -InstallRoot. So the script must ALSO declare
# [CmdletBinding(PositionalBinding = $false)], which is what routes every stray value to the
# catch-all. Requiring only the sibling institutionalised a non-fix.
# The PowerShell files this repository OWNS: everything under scripts/, plus the four
# .githooks/<hook>.ps1 implementations, which are scripts that take arguments like any other.
#
# Ignored files are dropped. CI checks out tracked content only, so a gate that scans a
# developer's gitignored scratch script reports a violation the repository cannot fix and CI
# can never see -- and this container has one (`scripts/run-unity*` is in .gitignore).
# Untracked-but-not-ignored files are still scanned: a new script you forgot to stage is
# exactly the one a rule should catch. `git` failing (a fixture root is not a repository)
# excludes nothing.
function Get-OwnedPowerShellScript {
    $candidates = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($scriptDirectory in @('scripts', '.githooks')) {
        $absolute = Join-Path $repoRoot $scriptDirectory
        if (-not (Test-Path -LiteralPath $absolute -PathType Container)) { continue }
        foreach ($file in (Get-ChildItem -LiteralPath $absolute -Filter '*.ps1' -Recurse -File)) {
            $candidates.Add($file) | Out-Null
        }
    }

    $ignored = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    try {
        $listed = & git -C $repoRoot ls-files --others --ignored --exclude-standard -- 'scripts' '.githooks' 2>$null
        if ($LASTEXITCODE -eq 0) {
            foreach ($ignoredPath in @($listed)) {
                if (-not [string]::IsNullOrWhiteSpace($ignoredPath)) {
                    $ignored.Add($ignoredPath.Replace('\', '/')) | Out-Null
                }
            }
        }
    } catch {
        # No git, or no repository. Scan everything rather than nothing.
    }

    $owned = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    foreach ($candidate in $candidates) {
        $candidateRelative = [System.IO.Path]::GetRelativePath($repoRoot, $candidate.FullName).Replace('\', '/')
        if ($ignored.Contains($candidateRelative)) { continue }
        $owned.Add($candidate) | Out-Null
    }

    return $owned
}

function Invoke-InvocationPolicy {
    param([hashtable]$InputBatch)

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = @(Get-Command uv -CommandType Application -ErrorAction Stop)[0].Source
    $start.WorkingDirectory = $repoRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $start.StandardInputEncoding = $utf8
    $start.StandardOutputEncoding = $utf8
    $start.StandardErrorEncoding = $utf8
    foreach ($argument in @('run', '--quiet', '--project', (Join-Path $PSScriptRoot 'python~'), '--locked', '--no-dev', 'python', (Join-Path $PSScriptRoot 'pwsh_invocation_asts.py'), '--include-text')) {
        $start.ArgumentList.Add($argument)
    }

    $payload = ConvertTo-Json -InputObject $InputBatch -Depth 9 -Compress -EscapeHandling EscapeNonAscii
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $inputFailure = $null
        try {
            try { $process.StandardInput.WriteLine($payload) }
            finally { $process.StandardInput.Close() }
        }
        catch { $inputFailure = $_ }
        $process.WaitForExit()
        $output = $stdout.GetAwaiter().GetResult()
        $errorOutput = $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "PowerShell invocation policy failed (exit $($process.ExitCode)): $errorOutput"
        }
        if ($null -ne $inputFailure) { throw $inputFailure }
        $decoded = ConvertFrom-Json -InputObject $output -NoEnumerate
        if ($decoded -isnot [pscustomobject] -or $decoded.findings -isnot [array]) {
            throw 'PowerShell invocation policy did not return a diagnostic array.'
        }
        return $decoded
    }
    finally {
        $process.Dispose()
    }
}

# PowerShell supplies native node types and recognizes directive tokens with its own matching
# rules. Python decides which commands and parameter blocks those facts permit.
$pws006AllowMarker = '#\s*lint-pwsh-invocations:\s*allow-start-process-argument-list\s+\S+'
$astFacts = [System.Collections.Generic.List[object]]::new()
foreach ($scriptPath in (Get-OwnedPowerShellScript)) {
    $rel = [System.IO.Path]::GetRelativePath($repoRoot, $scriptPath.FullName).Replace('\', '/')
    if ($rel -eq $selfRel -or $rel -eq $selfTestRel) { continue }

    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptPath.FullName, [ref]$null, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        $firstError = $parseErrors[0]
        $astFacts.Add(@{
            path = $rel
            error = @{
                line = $firstError.Extent.StartLineNumber
                message = $firstError.Message
                text = $firstError.Extent.Text
            }
            commands = @()
            comment_lines = @()
            marker_lines = @()
            block = $null
        })
        continue
    }

    $commands = @(foreach ($command in $ast.FindAll(
            { param($node) $node -is [System.Management.Automation.Language.CommandAst] },
            $true
        )) {
        if ($command.GetCommandName() -notmatch '^(?:Start-Process|saps)$') { continue }
        $elements = $command.CommandElements
        $arguments = @(for ($index = 1; $index -lt $elements.Count; $index++) {
            $element = $elements[$index]
            if ($element -isnot [System.Management.Automation.Language.CommandParameterAst]) { continue }
            $value = $element.Argument
            if ($null -eq $value -and ($index + 1) -lt $elements.Count) {
                $value = $elements[$index + 1]
            }
            if ($null -ne $value) {
                @{ name = $element.ParameterName; kind = $value.GetType().Name }
            }
        })
        if ($arguments.Count -gt 0) {
            @{
                line = $command.Extent.StartLineNumber
                text = $command.Extent.Text.Split("`n")[0].Trim()
                arguments = $arguments
            }
        }
    })

    $paramBlock = $ast.ParamBlock
    $block = if ($null -ne $paramBlock) {
        @{
            line = $paramBlock.Extent.StartLineNumber
            text = $paramBlock.Extent.Text.Split("`n")[0].Trim()
            positional_disabled = [bool](@(foreach ($attribute in $paramBlock.Attributes) { $attribute.Extent.Text }) -match 'PositionalBinding\s*=\s*\$false')
            parameters = @(foreach ($parameter in $paramBlock.Parameters) {
                $remaining = [bool](@(foreach ($attribute in $parameter.Attributes) { $attribute.Extent.Text }) -match 'ValueFromRemainingArguments')
                @{
                    name = $parameter.Name.VariablePath.UserPath
                    array_type = -not $remaining -and $null -ne $parameter.StaticType -and $parameter.StaticType.IsArray
                    catches_remaining = $remaining
                }
            })
        }
    }
    if ($commands.Count -eq 0 -and $null -eq $block) { continue }
    $commentLines = [System.Collections.Generic.List[int]]::new()
    $markerLines = [System.Collections.Generic.List[int]]::new()
    if ($commands.Count -gt 0) {
        $reader = [System.IO.StringReader]::new($ast.Extent.Text)
        try {
            $lineNumber = 0
            while ($null -ne ($line = $reader.ReadLine())) {
                ++$lineNumber
                if ($line -match '^\s*#') { $commentLines.Add($lineNumber) }
                if ($line -match $pws006AllowMarker) { $markerLines.Add($lineNumber) }
            }
        }
        finally { $reader.Dispose() }
    }
    $astFacts.Add(@{
        path = $rel
        error = $null
        commands = $commands
        comment_lines = $commentLines.ToArray()
        marker_lines = $markerLines.ToArray()
        block = $block
    })
}

try {
    $report = Invoke-InvocationPolicy -InputBatch @{
        sources = $sources
        ast = $astFacts.ToArray()
        culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
    }
}
catch {
    [Console]::Error.WriteLine("[lint-pwsh-invocations] Checks failed: " + $_.Exception.Message)
    exit 1
}

$violations = $report.findings
if ($violations.Count -gt 0) {
    foreach ($v in $violations) {
        Write-Host ("{0}:{1}: {2} {3}" -f $v.Path, $v.Line, $v.Code, $v.Message) -ForegroundColor Red
        if ($VerboseOutput) {
            Write-Host ("    > {0}" -f $v.Content) -ForegroundColor DarkGray
        }
    }
    Write-Host ""
    Write-Host ("[lint-pwsh-invocations] {0} violation(s) found." -f $violations.Count) -ForegroundColor Red
}
if ($report.ast_error) {
    [Console]::Error.WriteLine("[lint-pwsh-invocations] AST checks failed: " + $report.ast_error)
    exit 1
}
if ($violations.Count -gt 0) { exit 1 }

if ($VerboseOutput) {
    Write-Host "[lint-pwsh-invocations] OK: No pwsh/powershell invocation anti-patterns detected." -ForegroundColor Green
}
exit 0
