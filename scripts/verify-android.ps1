[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$androidRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'AndroidCompanion'
Push-Location $androidRoot
try {
    $gradleWrapper = if ($IsWindows) { '.\gradlew.bat' } else { './gradlew' }
    if (-not (Test-Path $gradleWrapper)) {
        throw "Gradle wrapper not found at $gradleWrapper."
    }

    & $gradleWrapper --no-daemon --stacktrace clean lintDebug testDebugUnitTest assembleDebug assembleRelease
    if ($LASTEXITCODE -ne 0) { throw "Android verification failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}
