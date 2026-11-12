param([switch]$VerboseOutput)
$ErrorActionPreference = 'Stop'
$arguments = @('run', '--quiet', '--project', (Join-Path $PSScriptRoot '../python~'), '--locked', 'pytest', (Join-Path $PSScriptRoot 'test_doc_links.py'))
if ($VerboseOutput) { $arguments += '-v' } else { $arguments += '-q' }
& uv @arguments
exit $LASTEXITCODE
