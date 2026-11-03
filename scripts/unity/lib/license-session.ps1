function Read-UnityLicenseSession {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$UnityVersion,
        [Parameter(Mandatory = $true)][string]$EditorPath,
        [Parameter(Mandatory = $true)][ValidateSet('requested', 'activated')][string]$State
    )

    if ($env:UH_CENTRAL_LICENSE_RETURN -cne 'true') { throw 'A shared license session requires central return ownership.' }
    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.PSIsContainer -or $item.Length -gt 16384 -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Invalid shared Unity license session file.'
    }
    $pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $session = [IO.File]::ReadAllText($item.FullName) | ConvertFrom-Json -AsHashtable
    $ownerId = 0
    if (-not [int]::TryParse($env:UH_UNITY_SESSION_OWNER_PID, [ref]$ownerId) -or $ownerId -le 0 -or
        $session.SchemaVersion -ne 1 -or $session.State -cne $State -or $session.OwnerProcessId -ne $ownerId -or
        $session.UnityVersion -cne $UnityVersion -or
        -not [string]::Equals([IO.Path]::GetFullPath($session.EditorPath), [IO.Path]::GetFullPath($EditorPath), $pathComparison)) {
        throw 'Shared Unity license session identity differs from this worker.'
    }
    foreach ($name in @('GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_JOB')) {
        if ($session.Identity[$name] -cne [Environment]::GetEnvironmentVariable($name)) {
            throw 'Shared Unity license session belongs to another workflow job.'
        }
    }
    $owner = Get-Process -Id $ownerId -ErrorAction Stop
    try {
        $started = $owner.StartTime.ToUniversalTime().Ticks.ToString([Globalization.CultureInfo]::InvariantCulture)
        if ($IsLinux) {
            $status = [IO.File]::ReadAllText("/proc/$ownerId/stat")
            $fields = $status.Substring($status.LastIndexOf(')') + 2) -split ' '
            $started = $fields[19]
        }
        if ($State -ceq 'activated' -and $session.OwnerStartIdentity -cne $started) {
            throw 'The shared Unity license session owner process changed.'
        }
        $session.OwnerStartIdentity = $started
    } finally { $owner.Dispose() }
    return $session
}

function Complete-UnityLicenseSession {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Session
    )
    $Session.State = 'activated'
    $temporary = "$Path.pending"
    [IO.File]::WriteAllText($temporary, ($Session | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force -ErrorAction Stop
}
