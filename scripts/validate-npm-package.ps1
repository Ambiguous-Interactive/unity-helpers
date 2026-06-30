Param(
  [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info($msg) {
  if ($VerboseOutput) { Write-Host "[validate-npm-package] $msg" -ForegroundColor Cyan }
}

function Write-Success($msg) {
  Write-Host "[validate-npm-package] $msg" -ForegroundColor Green
}

function Write-Error-Custom($msg) {
  Write-Host "[validate-npm-package] $msg" -ForegroundColor Red
}

function Get-TrackedPackageFiles {
  param(
    [string]$RepoRoot,
    [string[]]$PackageRoots
  )

  $trackedRoots = @($PackageRoots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($trackedRoots.Count -eq 0) {
    return @()
  }

  $trackedFiles = (& git -C $RepoRoot ls-files -z -- @trackedRoots) -split "`0" | Where-Object { $_ -ne '' }
  if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed while collecting tracked package files."
  }

  return @(
    $trackedFiles |
      ForEach-Object { $_ -replace '\\', '/' } |
      Sort-Object
  )
}

function Get-PackedPackageFiles {
  param(
    [string]$PackageDir
  )

  return @(
    Get-ChildItem -LiteralPath $PackageDir -Recurse -File -Force |
      ForEach-Object {
        $_.FullName.Replace("$PackageDir\", "").Replace("$PackageDir/", "") -replace '\\', '/'
      } |
      Sort-Object
  )
}

function Test-ExpectedPackageExclusion {
  param(
    [string]$RelativePath
  )

  $excludePatterns = @(
    '.gitkeep',
    '*.dll',
    '*.pdb',
    '*.tmp',
    '*.log',
    '*.rsp'
  )

  $fileName = Split-Path -Leaf $RelativePath
  foreach ($pattern in $excludePatterns) {
    if (($RelativePath -like $pattern) -or ($fileName -like $pattern)) {
      return $true
    }
  }

  return $false
}

$repoRoot = (Get-Location).Path

# Step 1: Create a temporary directory for npm pack
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "npm-package-validation-$(Get-Random)"
Write-Info "Creating temporary directory: $tempDir"
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
  # Step 2: Run npm pack
  Write-Info "Running npm pack..."
  $packOutput = npm pack --pack-destination $tempDir 2>&1 | Out-String
  Write-Info "npm pack output: $packOutput"

  # Step 3: Find the tarball
  $tarball = Get-ChildItem -Path $tempDir -Filter "*.tgz" | Select-Object -First 1
  if (-not $tarball) {
    Write-Error-Custom "No tarball found in $tempDir"
    exit 1
  }
  Write-Info "Found tarball: $($tarball.Name)"

  # Step 4: Extract the tarball
  $extractDir = Join-Path $tempDir "extracted"
  New-Item -ItemType Directory -Path $extractDir -Force | Out-Null
  Write-Info "Extracting tarball to $extractDir"
  
  # Use tar to extract (available on Windows 10+ and Linux/macOS)
  tar -xzf $tarball.FullName -C $extractDir
  
  # The content is in a "package" subdirectory
  $packageDir = Join-Path $extractDir "package"
  if (-not (Test-Path $packageDir)) {
    Write-Error-Custom "Package directory not found after extraction"
    exit 1
  }

  Write-Info "Package extracted to: $packageDir"

  # Step 5: Validate Unity folders and meta files
  $errors = @()

  $forbiddenPackageEntries = @(
    '.artifacts',
    '.cursor',
    '.git',
    '.github',
    '.githooks',
    '.llm',
    '.mcp.json',
    'node_modules',
    'package-lock.json',
    'Tests'
  )

  $allowedTopLevelEntries = @(
    'CHANGELOG.md',
    'CHANGELOG.md.meta',
    'Editor',
    'Editor.meta',
    'LICENSE',
    'LICENSE.meta',
    'README.md',
    'README.md.meta',
    'Runtime',
    'Runtime.meta',
    'Samples~',
    'scripts/postinstall-hooks.js',
    'Shaders',
    'Shaders.meta',
    'Styles',
    'Styles.meta',
    'URP',
    'URP.meta',
    'docs',
    'docs.meta',
    'link.xml',
    'link.xml.meta',
    'package.json',
    'package.json.meta',
    'scripts'
  )

  foreach ($entry in $forbiddenPackageEntries) {
    $entryPath = Join-Path $packageDir $entry
    if (Test-Path $entryPath) {
      $errors += "Forbidden development entry included in npm package: $entry"
    }
  }

  $topLevelEntries = Get-ChildItem -LiteralPath $packageDir -Force | ForEach-Object { $_.Name }
  foreach ($entry in $topLevelEntries) {
    if ($entry -cnotin $allowedTopLevelEntries) {
      $errors += "Unexpected top-level entry included in npm package: $entry"
    }
  }

  $scriptsDir = Join-Path $packageDir 'scripts'
  if (Test-Path -LiteralPath $scriptsDir) {
    $allowedScriptsEntries = @('postinstall-hooks.js')
    $scriptEntries = Get-ChildItem -LiteralPath $scriptsDir -Recurse -File | ForEach-Object {
      $_.FullName.Replace("$scriptsDir\", "").Replace("$scriptsDir/", "") -replace '\\', '/'
    }
    foreach ($entry in $scriptEntries) {
      if ($entry -cnotin $allowedScriptsEntries) {
        $errors += "Unexpected script included in npm package: scripts/$entry"
      }
    }
  }

  $requiredTopLevelEntries = @(
    'CHANGELOG.md',
    'CHANGELOG.md.meta',
    'Editor',
    'Editor.meta',
    'LICENSE',
    'LICENSE.meta',
    'README.md',
    'README.md.meta',
    'Runtime',
    'Runtime.meta',
    'Samples~',
    'scripts/postinstall-hooks.js',
    'Shaders',
    'Shaders.meta',
    'Styles',
    'Styles.meta',
    'URP',
    'URP.meta',
    'link.xml',
    'link.xml.meta',
    'package.json',
    'package.json.meta'
  )
  foreach ($entry in $requiredTopLevelEntries) {
    $entryPath = Join-Path $packageDir $entry
    if (-not (Test-Path -LiteralPath $entryPath)) {
      $errors += "Missing required top-level package entry: $entry"
    }
  }

  $packageContentRoots = @(
    'CHANGELOG.md',
    'CHANGELOG.md.meta',
    'Editor',
    'Editor.meta',
    'LICENSE',
    'LICENSE.meta',
    'README.md',
    'README.md.meta',
    'Runtime',
    'Runtime.meta',
    'Samples~',
    'Shaders',
    'Shaders.meta',
    'Styles',
    'Styles.meta',
    'URP',
    'URP.meta',
    'docs',
    'docs.meta',
    'link.xml',
    'link.xml.meta',
    'package.json',
    'package.json.meta',
    'scripts/postinstall-hooks.js'
  )

  $allowedCsRoots = @('Runtime/', 'Editor/', 'Samples~/', 'Styles/')
  $packedCsFiles = Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter '*.cs' | ForEach-Object {
    $_.FullName.Replace("$packageDir\", "").Replace("$packageDir/", "") -replace '\\', '/'
  }
  foreach ($entry in $packedCsFiles) {
    $isAllowed = $false
    foreach ($root in $allowedCsRoots) {
      if ($entry.StartsWith($root, [System.StringComparison]::Ordinal)) {
        $isAllowed = $true
        break
      }
    }
    if (-not $isAllowed) {
      $errors += "C# source outside Unity package roots included in npm package: $entry"
    }
  }
  
  # Folders that should be in the npm package
  $unityFolders = @('Runtime', 'Editor', 'Samples~', 'Shaders', 'Styles', 'URP')
  
  foreach ($folder in $unityFolders) {
    $folderPath = Join-Path $packageDir $folder
    
    if (-not (Test-Path $folderPath)) {
      $errors += "Missing required folder: $folder"
      continue
    }
    
    Write-Info "Validating folder: $folder"
    
    # Check if folder has .meta file. Samples~ is the Unity package-manager
    # convention for samples and intentionally has no root folder .meta.
    if ($folder -ne 'Samples~') {
      $folderMetaPath = "$folderPath.meta"
      if (-not (Test-Path $folderMetaPath)) {
        $errors += "Missing .meta file for folder: $folder"
      }
    }
    
    # Get all files and subdirectories in this folder (recursively)
    $items = Get-ChildItem -Path $folderPath -Recurse
    
    foreach ($item in $items) {
      # Get relative path for better error messages
      $relativePath = $item.FullName.Replace("$packageDir\", "").Replace("$packageDir/", "")
      $relativePath = $relativePath -replace '\\', '/'
      
      # Skip .meta files themselves
      if ($item.Name -like "*.meta") {
        # This is a meta file - verify the source exists
        $sourcePath = $item.FullName -replace '\.meta$', ''
        if (-not (Test-Path $sourcePath)) {
          $errors += "Orphaned .meta file (missing source): $relativePath"
        }
        continue
      }
      
      # Check if this item has a corresponding .meta file
      $metaPath = "$($item.FullName).meta"
      if (-not (Test-Path $metaPath)) {
        $itemType = if ($item.PSIsContainer) { "directory" } else { "file" }
        $errors += "Missing .meta file for $itemType`: $relativePath"
      }
    }
  }

  # Step 6: Validate that packed release payload matches git repo
  Write-Info "Validating that npm package content matches git repository..."
  
  $gitPackageFiles = Get-TrackedPackageFiles -RepoRoot $repoRoot -PackageRoots $packageContentRoots
  $npmPackageFiles = Get-PackedPackageFiles -PackageDir $packageDir

  foreach ($gitFile in $gitPackageFiles) {
    if (($gitFile -cnotin $npmPackageFiles) -and (-not (Test-ExpectedPackageExclusion -RelativePath $gitFile))) {
      $errors += "File in git repo but missing in npm package: $gitFile"
    }
  }

  foreach ($npmFile in $npmPackageFiles) {
    if ($npmFile -cnotin $gitPackageFiles) {
      $errors += "File in npm package but not tracked in git repo: $npmFile"
    }
  }

  # Step 7: Report results
  if ($errors.Count -gt 0) {
    Write-Error-Custom "`nValidation failed with $($errors.Count) error(s):"
    Write-Host ""
    foreach ($errorMessage in $errors | Sort-Object) {
      Write-Host "  ✗ $errorMessage" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Error-Custom "NPM package validation failed."
    exit 1
  } else {
    Write-Host ""
    Write-Success "✓ All Unity files have corresponding .meta files"
    Write-Success "✓ All .meta files have corresponding source files"
    Write-Success "✓ NPM package content matches git repository"
    Write-Host ""
    Write-Success "NPM package validation passed!"
    exit 0
  }

} finally {
  # Clean up
  Write-Info "Cleaning up temporary directory: $tempDir"
  Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
