function Test-FixtureBlockEndsWithExplicitExit {
  param([System.Management.Automation.Language.Ast]$Block)

  if ($null -eq $Block -or $Block.Statements.Count -eq 0) { return $false }
  $lastStatement = $Block.Statements[$Block.Statements.Count - 1]
  if ($lastStatement -is [System.Management.Automation.Language.ExitStatementAst]) { return $true }
  if ($lastStatement -is [System.Management.Automation.Language.IfStatementAst]) {
    if ($null -eq $lastStatement.ElseClause) { return $false }
    foreach ($clause in $lastStatement.Clauses) {
      if (-not (Test-FixtureBlockEndsWithExplicitExit -Block $clause.Item2)) { return $false }
    }
    return Test-FixtureBlockEndsWithExplicitExit -Block $lastStatement.ElseClause
  }
  if ($lastStatement -is [System.Management.Automation.Language.TryStatementAst]) {
    if (-not (Test-FixtureBlockEndsWithExplicitExit -Block $lastStatement.Body)) { return $false }
    foreach ($clause in $lastStatement.CatchClauses) {
      if (-not (Test-FixtureBlockEndsWithExplicitExit -Block $clause.Body)) { return $false }
    }
    return $true
  }
  return $false
}

function Assert-FixtureExplicitExitContract {
  param([string]$ScriptPath)

  $tokens = $null
  $parseErrors = $null
  $ast = [System.Management.Automation.Language.Parser]::ParseFile($ScriptPath, [ref]$tokens, [ref]$parseErrors)
  if ($parseErrors.Count -gt 0) { throw "Fixture has PowerShell parse errors: $parseErrors" }
  $earlyEscapes = @($ast.FindAll({
    param($node)
    $isReturn = $node -is [System.Management.Automation.Language.ReturnStatementAst]
    $isLoopEscape = $node -is [System.Management.Automation.Language.BreakStatementAst] -or
      $node -is [System.Management.Automation.Language.ContinueStatementAst]
    if (-not $isReturn -and -not $isLoopEscape) { return $false }
    $hasContainingLoop = $false
    $parent = $node.Parent
    while ($null -ne $parent) {
      if ($parent -is [System.Management.Automation.Language.FunctionDefinitionAst] -or
          $parent -is [System.Management.Automation.Language.ScriptBlockExpressionAst]) {
        if ($isReturn) { return $false }
        break
      }
      if ($parent -is [System.Management.Automation.Language.LoopStatementAst] -or
          $parent -is [System.Management.Automation.Language.SwitchStatementAst]) { $hasContainingLoop = $true }
      $parent = $parent.Parent
    }
    if ($isReturn) { return $true }
    return -not $hasContainingLoop -or -not [string]::IsNullOrEmpty($node.Label)
  }, $true))
  if ($earlyEscapes.Count -gt 0 -or -not (Test-FixtureBlockEndsWithExplicitExit -Block $ast.EndBlock)) {
    throw 'Fixture can return without an exit status: explicit terminal exit required; native LASTEXITCODE is insufficient.'
  }
}

function Invoke-IsolatedFixture {
  param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [hashtable]$Parameters = @{},
    [string]$WorkingDirectory = (Get-Location).Path
  )

  Assert-FixtureExplicitExitContract -ScriptPath $ScriptPath
  $runner = [System.Management.Automation.PowerShell]::Create()
  try {
    $null = $runner.AddScript('param($path) Set-Location -LiteralPath $path').AddArgument($WorkingDirectory).AddStatement()
    $null = $runner.AddCommand($ScriptPath)
    foreach ($name in $Parameters.Keys) { $null = $runner.AddParameter($name, $Parameters[$name]) }
    $result = $runner.Invoke()
    $output = [System.Collections.Generic.List[string]]::new()
    foreach ($item in $result) { $output.Add($item.ToString()) }
    foreach ($item in $runner.Streams.Information) { $output.Add($item.MessageData.ToString()) }
    foreach ($item in $runner.Streams.Warning) { $output.Add($item.Message) }
    foreach ($item in $runner.Streams.Error) { $output.Add($item.ToString()) }
    if ($runner.Streams.Error.Count -gt 0) {
      throw "Fixture emitted a PowerShell error: $($output -join [Environment]::NewLine)"
    }
    $exitCode = $runner.Runspace.SessionStateProxy.GetVariable('LASTEXITCODE')
    if ($null -eq $exitCode) {
      throw "Fixture returned without an exit status. $($output -join [Environment]::NewLine)"
    }
    return @{ ExitCode = [int]$exitCode; Output = ($output -join [Environment]::NewLine) }
  }
  finally {
    $runner.Dispose()
  }
}

function Test-IsolatedFixtureHarness {
  $controlPath = Join-Path ([System.IO.Path]::GetTempPath()) ('fixture-control-' + [guid]::NewGuid().ToString('N') + '.ps1')
  try {
    foreach ($expectedExit in @(0, 1, 7)) {
      Set-Content -LiteralPath $controlPath -Value "Write-Host 'control-output'; Write-Warning 'control-warning'; exit $expectedExit"
      $result = Invoke-IsolatedFixture -ScriptPath $controlPath
      Write-TestResult "Runspace.ExactExit$expectedExit" ($result.ExitCode -eq $expectedExit -and $result.Output.Contains('control-output') -and $result.Output.Contains('control-warning'))
    }
    foreach ($control in @(
      @{ Name = 'MissingStatus'; Content = "Write-Host 'missing-status'"; Diagnostic = 'without an exit status' },
      @{ Name = 'ErrorExitZero'; Content = "Write-Error 'unexpected'; exit 0"; Diagnostic = 'PowerShell error' },
      @{ Name = 'ErrorExitOne'; Content = "Write-Error 'unexpected'; exit 1"; Diagnostic = 'PowerShell error' },
      @{ Name = 'Throw'; Content = "throw 'unexpected-throw'; exit 0"; Diagnostic = 'unexpected-throw' },
      @{ Name = 'NativeMissingExit'; Content = 'git --version > $null'; Diagnostic = 'without an exit status' },
      @{ Name = 'UnreachableExitAfterReturn'; Content = 'git --version > $null; return; exit 0'; Diagnostic = 'without an exit status' },
      @{ Name = 'ConditionalMissingExit'; Content = 'git --version > $null; if ($false) { exit 0 }'; Diagnostic = 'without an exit status' },
      @{ Name = 'UnscopedBreakBeforeExit'; Content = 'git --version > $null; break; exit 0'; Diagnostic = 'without an exit status' },
      @{ Name = 'FunctionBreakBeforeExit'; Content = 'function Stop-Fixture { break }; git --version > $null; Stop-Fixture; exit 7'; Diagnostic = 'without an exit status' },
      @{ Name = 'FunctionContinueBeforeExit'; Content = 'function Stop-Fixture { continue }; git --version > $null; Stop-Fixture; exit 7'; Diagnostic = 'without an exit status' },
      @{ Name = 'ScriptBlockBreakBeforeExit'; Content = 'git --version > $null; & { break }; exit 7'; Diagnostic = 'without an exit status' },
      @{ Name = 'ScriptBlockContinueBeforeExit'; Content = 'git --version > $null; & { continue }; exit 7'; Diagnostic = 'without an exit status' },
      @{ Name = 'FunctionLabeledBreak'; Content = 'function Stop-Fixture { :inside foreach ($item in @(1)) { break inside } }; git --version > $null; Stop-Fixture; exit 7'; Diagnostic = 'without an exit status' },
      @{ Name = 'ScriptBlockLabeledContinue'; Content = 'git --version > $null; & { :inside foreach ($item in @(1)) { continue inside } }; exit 7'; Diagnostic = 'without an exit status' }
    )) {
      Set-Content -LiteralPath $controlPath -Value $control.Content
      $rejected = $false
      try { $null = Invoke-IsolatedFixture -ScriptPath $controlPath }
      catch { $rejected = $_.Exception.Message.Contains($control.Diagnostic) }
      Write-TestResult "Runspace.Rejects$($control.Name)" $rejected
    }
    foreach ($control in @(
      @{ Name = 'FunctionReturn'; Content = 'function Get-FixtureResult { return "function-return" }; Get-FixtureResult; exit 7'; Output = 'function-return' },
      @{ Name = 'FunctionScopedLoopEscapes'; Content = 'function Get-FixtureResult { foreach ($item in @(1, 2)) { if ($item -eq 1) { continue }; break }; "function-loop" }; Get-FixtureResult; exit 7'; Output = 'function-loop' },
      @{ Name = 'FunctionScopedSwitchBreak'; Content = 'function Get-FixtureResult { switch (1) { 1 { break } }; "function-switch" }; Get-FixtureResult; exit 7'; Output = 'function-switch' },
      @{ Name = 'ScriptBlockScopedLoopEscapes'; Content = '& { foreach ($item in @(1, 2)) { if ($item -eq 1) { continue }; break }; "script-block-loop" }; exit 7'; Output = 'script-block-loop' }
    )) {
      Set-Content -LiteralPath $controlPath -Value $control.Content
      $result = Invoke-IsolatedFixture -ScriptPath $controlPath
      Write-TestResult "Runspace.Accepts$($control.Name)" ($result.ExitCode -eq 7 -and $result.Output.Contains($control.Output))
    }
    Set-Content -LiteralPath $controlPath -Value "if (Get-Variable FixtureControlState -Scope Global -ErrorAction SilentlyContinue) { exit 7 }; `$global:FixtureControlState = 1; exit 0"
    $first = Invoke-IsolatedFixture -ScriptPath $controlPath
    $second = Invoke-IsolatedFixture -ScriptPath $controlPath
    Write-TestResult 'Runspace.FreshGlobalState' ($first.ExitCode -eq 0 -and $second.ExitCode -eq 0)
    Set-Content -LiteralPath $controlPath -Value 'git --version > $null'
    $null = & pwsh -NoProfile -File $controlPath 2>&1
    Write-TestResult 'CLI.NativeImplicitSuccessStillNeedsExplicitFixtureExit' ($LASTEXITCODE -eq 0)
  }
  finally {
    Remove-Item -LiteralPath $controlPath -Force -ErrorAction SilentlyContinue
  }
}
