[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'The .NET SDK is required. Install the version pinned in global.json.'
    }

    $lockFiles = Get-ChildItem -Path $repositoryRoot -Filter packages.lock.json -Recurse -File
    if ($lockFiles.Count -eq 0) {
        Write-Host 'NuGet lock files are missing; generating them once before locked verification.'
        dotnet restore VcfEditor.sln --use-lock-file
        if ($LASTEXITCODE -ne 0) { throw "initial dotnet restore failed with exit code $LASTEXITCODE." }
    }

    dotnet restore VcfEditor.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "locked dotnet restore failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase3-build-blockers.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 build-blocker source verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase3-windows-build-fixes-v2.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 Windows build fixes v2 verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-dotnet-analyzers.py
    if ($LASTEXITCODE -ne 0) { throw ".NET analyzer source verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase3-compile-fixes.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 compile-fix verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase3-navigation.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 navigation verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase3-resources.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 3 resource verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase4.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 4 architecture verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase4-compile-contracts.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 4 compile-contract verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase5.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 5 desktop modernization verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase9-windows.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 9 Windows performance verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase9-build-fix.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 9 Windows build-fix verification failed with exit code $LASTEXITCODE." }

    python scripts/verify-phase9-xaml-runtime-fixes.py
    if ($LASTEXITCODE -ne 0) { throw "Phase 9 XAML runtime-fix verification failed with exit code $LASTEXITCODE." }

    dotnet run --project tools/DesignTokenGenerator/DesignTokenGenerator.csproj -c Release --no-restore -- --check
    if ($LASTEXITCODE -ne 0) { throw "design-token generation check failed with exit code $LASTEXITCODE." }

    dotnet build VcfEditor.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

    dotnet test VcfEditor.sln -c Release --no-build --collect:'XPlat Code Coverage'
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
