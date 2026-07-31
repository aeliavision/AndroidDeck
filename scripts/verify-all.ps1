[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'verify-windows.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot 'verify-android.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
