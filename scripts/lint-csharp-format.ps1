Param(
  [switch]$VerboseOutput,
  [switch]$Fix,
  # Report a missing tool manifest or a missing dotnet as a skip rather than a failure. Only the
  # changed-file pass uses this, so it can run inside a scratch repository that has neither; the
  # whole-repository check in validate:prepush never sets it, which is what keeps the gate real.
  [switch]$SkipWhenUnavailable,
  [string[]]$Paths
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

function Write-Info($msg) {
  if ($VerboseOutput) {
    Write-Host "[lint-csharp-format] $msg" -ForegroundColor Cyan
  }
}

function Write-Failure($msg) {
  Write-Host "[lint-csharp-format] $msg" -ForegroundColor Red
}

function Write-Remedy($msg) {
  Write-Host "  $msg" -ForegroundColor Yellow
}

function Exit-Unavailable([string]$Reason, [string[]]$Remedies) {
  if ($SkipWhenUnavailable) {
    # Announced, never silent. A gate that quietly does nothing is the defect this script exists
    # to close, so the skip is always printed even outside verbose mode.
    Write-Host "[lint-csharp-format] Skipped: $Reason" -ForegroundColor Yellow
    exit 0
  }

  Write-Failure $Reason
  foreach ($remedy in $Remedies) {
    Write-Remedy $remedy
  }

  exit 1
}

$manifestPath = Join-Path -Path $repoRoot -ChildPath '.config/dotnet-tools.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
  Exit-Unavailable 'no .NET tool manifest at .config/dotnet-tools.json.' @(
    'CSharpier is pinned there; restore the file before formatting C#.'
  )
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Exit-Unavailable 'dotnet was not found on PATH, so C# formatting cannot be verified.' @(
    'Install the .NET SDK, then run: dotnet tool restore',
    'CI runs "dotnet tool run csharpier check ." and will reject unformatted C# regardless.'
  )
}

# CSharpier only reaches the repository's pinned version through the manifest, and the manifest is
# only honored from the repository root -- a caller-relative invocation silently resolves a
# different (or no) tool.
Push-Location $repoRoot
try {
  Write-Info 'Restoring .NET tools.'
  $restoreOutput = & dotnet tool restore 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Failure "dotnet tool restore failed with exit code $LASTEXITCODE."
    foreach ($line in @($restoreOutput)) {
      Write-Host "  $line" -ForegroundColor DarkGray
    }
    Write-Remedy 'Run "dotnet tool restore" from the repository root and resolve the error above.'
    exit 1
  }

  $targets = @()
  if ($null -ne $Paths) {
    $targets = @($Paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  }

  $scopeLabel = 'the repository'
  if ($targets.Count -gt 0) {
    # A path list that resolves to nothing must not become a whole-repository run: the caller asked
    # about specific files, and silently widening the scope hides which files were actually checked.
    $existing = @($targets | Where-Object {
        $candidate = $_
        if ([System.IO.Path]::IsPathRooted($candidate)) {
          Test-Path -LiteralPath $candidate
        }
        else {
          Test-Path -LiteralPath (Join-Path -Path $repoRoot -ChildPath $candidate)
        }
      })

    if ($existing.Count -eq 0) {
      Write-Info 'No existing C# targets to check.'
      exit 0
    }

    $targets = $existing
    $scopeLabel = "$($targets.Count) changed file(s)"
  }
  else {
    $targets = @('.')
  }

  $verb = if ($Fix) { 'format' } else { 'check' }
  Write-Info "Running csharpier $verb over $scopeLabel."

  $arguments = @('tool', 'run', 'csharpier', $verb) + $targets
  & dotnet @arguments
  $exitCode = $LASTEXITCODE

  if ($exitCode -ne 0) {
    if ($Fix) {
      Write-Failure "csharpier format failed with exit code $exitCode."
    }
    else {
      Write-Failure "C# formatting does not match CSharpier ($scopeLabel)."
      Write-Remedy 'Fix with: dotnet tool run csharpier format .'
      Write-Remedy 'Or scope it: npm run agent:preflight:fix'
    }

    exit $exitCode
  }
}
finally {
  Pop-Location
}

Write-Info 'C# formatting matches CSharpier.'
exit 0
