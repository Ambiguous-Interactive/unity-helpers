#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$request = ConvertFrom-Json -InputObject ([Console]::In.ReadToEnd())
if ($null -ne $request.culture) {
    [System.Globalization.CultureInfo]::CurrentCulture =
        [System.Globalization.CultureInfo]::GetCultureInfo($request.culture)
}
$pws006AllowMarker = '#\s*lint-pwsh-invocations:\s*allow-start-process-argument-list\s+\S+'
$astFacts = [System.Collections.Generic.List[object]]::new()
foreach ($rel in $request.paths) {

    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $request.root $rel), [ref]$null, [ref]$parseErrors)
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

function Get-InvocationWordRanges {
    # Positions in this string are UTF-16 code units, the same units Regex classifies.
    $codeUnits = [string]::new([char[]](0..0xFFFF))
    foreach ($range in [regex]::Matches($codeUnits, '\w+')) {
        , @($range.Index, ($range.Index + $range.Length - 1))
    }
}

$report = @{
    ast = $astFacts.ToArray()
    culture = [System.Globalization.CultureInfo]::CurrentCulture.Name
    word_ranges = @(Get-InvocationWordRanges)
}
[Console]::Out.WriteLine((ConvertTo-Json -InputObject $report -Depth 9 -Compress -EscapeHandling EscapeNonAscii))
