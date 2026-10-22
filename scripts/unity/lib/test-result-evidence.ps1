function Read-TestResultEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('NUnit', 'Trx')][string]$Format
    )

    $file = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($file.PSIsContainer -or $file.Length -lt 1 -or $file.Length -gt 33554432) {
        throw 'Test evidence XML must be a nonempty file of at most 32 MiB.'
    }
    $settings = New-Object Xml.XmlReaderSettings
    $settings.MaxCharactersInDocument = 33554432
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($file.FullName, $settings)
    try {
        $xml = New-Object Xml.XmlDocument
        $xml.XmlResolver = $null
        $xml.Load($reader)
    } finally { $reader.Dispose() }

    $cases = New-Object 'System.Collections.Generic.List[object]'
    $names = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $testIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $executionIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    if ($Format -eq 'NUnit') {
        $run = $xml.SelectSingleNode('/test-run')
        if ($null -eq $run -or $run.GetAttribute('result') -cne 'Passed' -or
            $xml.SelectNodes("//test-suite[not(@result='Passed')]|//failure").Count -ne 0) {
            throw 'NUnit root or fixture lifecycle did not pass.'
        }
        foreach ($leaf in $xml.SelectNodes('//test-case')) {
            $fullName = $leaf.GetAttribute('fullname')
            $testId = $leaf.GetAttribute('id')
            if ([string]::IsNullOrWhiteSpace($fullName) -or -not $names.Add($fullName) -or
                $leaf.GetAttribute('result') -cne 'Passed' -or
                (-not [string]::IsNullOrWhiteSpace($testId) -and -not $testIds.Add($testId))) {
                throw 'NUnit case identity is missing, duplicated, or not passed.'
            }
            $assembly = $leaf.SelectSingleNode("ancestor::test-suite[@type='Assembly'][1]")
            $assemblyPath = ''
            if ($null -ne $assembly) { $assemblyPath = $assembly.GetAttribute('fullname') }
            $cases.Add([pscustomobject]@{
                    Fixture = $leaf.GetAttribute('classname'); FullName = $fullName; Result = 'Passed'
                    AssemblyPath = $assemblyPath; TestId = $testId; ExecutionId = ''; AdapterTypeName = ''
                })
        }
        foreach ($attribute in @('total', 'testcasecount', 'passed', 'failed', 'skipped', 'inconclusive')) {
            $expected = if ($attribute -in @('total', 'testcasecount', 'passed')) { $cases.Count } else { 0 }
            if ($run.GetAttribute($attribute) -cne $expected.ToString([Globalization.CultureInfo]::InvariantCulture)) {
                throw "NUnit summary differs from passed cases: $attribute."
            }
        }
        $runId = $run.GetAttribute('id')
        $started = $run.GetAttribute('start-time')
        $completed = $run.GetAttribute('end-time')
    } else {
        $namespace = New-Object Xml.XmlNamespaceManager $xml.NameTable
        $namespace.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
        $run = $xml.SelectSingleNode('/t:TestRun', $namespace)
        if ($null -eq $run) { throw 'TRX TestRun root or namespace is missing.' }
        $summaries = $run.SelectNodes('t:ResultSummary', $namespace)
        if ($summaries.Count -ne 1 -or $summaries[0].GetAttribute('outcome') -cnotin @('Completed', 'Passed') -or
            $run.SelectNodes('//t:ErrorInfo|//t:RunInfo[not(@outcome="Completed" or @outcome="Passed")]', $namespace).Count -ne 0) {
            throw 'TRX summary or lifecycle did not pass.'
        }
        $definitions = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
        foreach ($definition in $run.SelectNodes('t:TestDefinitions/t:UnitTest', $namespace)) {
            $id = $definition.GetAttribute('id')
            if ([string]::IsNullOrWhiteSpace($id) -or $definitions.ContainsKey($id)) { throw 'TRX definition ID is missing or duplicated.' }
            $definitions.Add($id, $definition)
        }
        $entries = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
        foreach ($entry in $run.SelectNodes('t:TestEntries/t:TestEntry', $namespace)) {
            $execution = $entry.GetAttribute('executionId')
            $id = $entry.GetAttribute('testId')
            if ([string]::IsNullOrWhiteSpace($execution) -or [string]::IsNullOrWhiteSpace($id) -or $entries.ContainsKey($execution)) {
                throw 'TRX entry identity is missing or duplicated.'
            }
            $entries.Add($execution, $id)
        }
        foreach ($leaf in $run.SelectNodes('t:Results/t:UnitTestResult', $namespace)) {
            $id = $leaf.GetAttribute('testId')
            $execution = $leaf.GetAttribute('executionId')
            $fullName = $leaf.GetAttribute('testName')
            if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($execution) -or
                [string]::IsNullOrWhiteSpace($fullName) -or -not $testIds.Add($id) -or
                -not $executionIds.Add($execution) -or
                $leaf.GetAttribute('outcome') -cne 'Passed') {
                throw 'TRX result identity is missing, duplicated, or not passed.'
            }
            if (-not $definitions.ContainsKey($id) -or -not $entries.ContainsKey($execution) -or $entries[$execution] -cne $id) {
                throw 'TRX result does not join its definition and execution entry.'
            }
            $definition = $definitions[$id]
            $methods = $definition.SelectNodes('t:TestMethod', $namespace)
            $executions = $definition.SelectNodes('t:Execution', $namespace)
            if ($methods.Count -ne 1 -or $executions.Count -ne 1 -or
                $executions[0].GetAttribute('id') -cne $execution -or $definition.GetAttribute('name') -cne $fullName -or
                [string]::IsNullOrWhiteSpace($methods[0].GetAttribute('className')) -or
                [string]::IsNullOrWhiteSpace($methods[0].GetAttribute('name')) -or
                [string]::IsNullOrWhiteSpace($methods[0].GetAttribute('codeBase'))) {
                throw 'TRX definition metadata or execution differs from its result.'
            }
            $fixture = $methods[0].GetAttribute('className')
            if (-not $names.Add($fixture + [char]0 + $fullName)) { throw 'TRX fixture and case name are duplicated.' }
            $cases.Add([pscustomobject]@{
                    Fixture = $fixture; FullName = $fullName; Result = 'Passed'
                    AssemblyPath = $methods[0].GetAttribute('codeBase'); TestId = $id; ExecutionId = $execution
                    AdapterTypeName = $methods[0].GetAttribute('adapterTypeName')
                })
        }
        if ($definitions.Count -ne $cases.Count -or $entries.Count -ne $cases.Count -or
            $run.SelectNodes('t:Results/*', $namespace).Count -ne $cases.Count -or
            $run.SelectNodes('t:TestDefinitions/*', $namespace).Count -ne $cases.Count -or
            $run.SelectNodes('t:TestEntries/*', $namespace).Count -ne $cases.Count) {
            throw 'TRX definitions, entries and results are not one-to-one.'
        }
        $counters = $summaries[0].SelectNodes('t:Counters', $namespace)
        if ($counters.Count -ne 1) { throw 'TRX counters are missing or duplicated.' }
        foreach ($attribute in @('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')) {
            $expected = if ($attribute -in @('total', 'executed', 'passed')) { $cases.Count } else { 0 }
            if ($counters[0].GetAttribute($attribute) -cne $expected.ToString([Globalization.CultureInfo]::InvariantCulture)) {
                throw "TRX summary differs from passed cases: $attribute."
            }
        }
        $times = $run.SelectNodes('t:Times', $namespace)
        if ($times.Count -ne 1) { throw 'TRX run times are missing or duplicated.' }
        $runId = $run.GetAttribute('id')
        $started = $times[0].GetAttribute('start')
        $completed = $times[0].GetAttribute('finish')
    }
    if ($cases.Count -eq 0) { throw 'Test evidence contains no passed cases.' }
    return [pscustomobject]@{
        RunId = $runId; StartedUtc = $started; CompletedUtc = $completed; Cases = $cases.ToArray()
        Total = $cases.Count; Passed = $cases.Count; Failed = 0; Skipped = 0; Inconclusive = 0
    }
}
