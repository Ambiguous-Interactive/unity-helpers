Param(
  [switch]$VerboseOutput,
  [switch]$Fix,
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

$manifestPath = Join-Path -Path $repoRoot -ChildPath '.config/dotnet-tools.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
  Write-Failure "Missing .NET tool manifest at .config/dotnet-tools.json."
  Write-Remedy 'CSharpier is pinned there; restore the file before formatting C#.'
  exit 1
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  Write-Failure 'dotnet was not found on PATH, so C# formatting cannot be verified.'
  Write-Remedy 'Install the .NET SDK, then run: dotnet tool restore'
  Write-Remedy 'CI runs "dotnet tool run csharpier check ." and will reject unformatted C# regardless.'
  exit 1
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
