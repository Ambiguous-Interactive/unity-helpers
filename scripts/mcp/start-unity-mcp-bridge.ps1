#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RelayCommand = $env:UNITY_MCP_RELAY_COMMAND,

    [Parameter(Mandatory = $false)]
    [int]$Port = 0,

    [Parameter(Mandatory = $false)]
    [string]$McpPath = "/mcp",

    [Parameter(Mandatory = $false)]
    [ValidateSet("info", "debug", "none")]
    [string]$LogLevel = "info",

    [Parameter(Mandatory = $false)]
    [switch]$Stateful
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$supergatewayPackage = if ($env:UNITY_MCP_SUPERGATEWAY_PACKAGE) { $env:UNITY_MCP_SUPERGATEWAY_PACKAGE } else { "supergateway@3.4.3" }
$defaultPort = if ($env:UNITY_MCP_DEFAULT_PORT) { [int]$env:UNITY_MCP_DEFAULT_PORT } else { 9003 }

if (-not $PSBoundParameters.ContainsKey("Port")) {
    if ($env:UNITY_MCP_BRIDGE_PORT) {
        $Port = [int]$env:UNITY_MCP_BRIDGE_PORT
    }
    elseif ($env:UNITY_MCP_PORT) {
        $Port = [int]$env:UNITY_MCP_PORT
    }
    else {
        $Port = $defaultPort
    }
}

if ($Port -lt 1 -or $Port -gt 65535) {
    throw "Port must be between 1 and 65535. Got: $Port"
}

if (-not $PSBoundParameters.ContainsKey("McpPath")) {
    if ($env:UNITY_MCP_BRIDGE_PATH) {
        $McpPath = $env:UNITY_MCP_BRIDGE_PATH
    }
    elseif ($env:UNITY_MCP_PATH) {
        $McpPath = $env:UNITY_MCP_PATH
    }
}

if ([string]::IsNullOrWhiteSpace($RelayCommand)) {
    throw @"
UNITY_MCP_RELAY_COMMAND is required.

Example:
  `$env:UNITY_MCP_RELAY_COMMAND = 'C:\Path\To\relay_win.exe --mcp'
    pwsh -File scripts/mcp/start-unity-mcp-bridge.ps1 -Port 9003
"@
}

if ([string]::IsNullOrWhiteSpace($McpPath)) {
    $McpPath = "/mcp"
}

if (-not $McpPath.StartsWith('/')) {
    $McpPath = "/$McpPath"
}

$null = Get-Command npx -ErrorAction Stop

$supergatewayArgs = @(
    "-y",
    $supergatewayPackage,
    "--stdio", $RelayCommand,
    "--outputTransport", "streamableHttp",
    "--streamableHttpPath", $McpPath,
    "--port", $Port.ToString(),
    "--logLevel", $LogLevel
)

if ($Stateful.IsPresent) {
    $supergatewayArgs += "--stateful"
}

Write-Host "Starting Unity MCP bridge with supergateway..."
Write-Host "Relay command: $RelayCommand"
Write-Host "Bridge URL: http://0.0.0.0:$Port$McpPath"

& npx @supergatewayArgs
