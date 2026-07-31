# build_android.ps1
# This script builds the VCFEditor Companion Android app using Gradle.

$androidDir = Join-Path $PSScriptRoot "AndroidCompanion"
$gradlew = Join-Path $androidDir "gradlew.bat"

if (-not (Test-Path $androidDir)) {
    Write-Error "AndroidCompanion directory not found at $androidDir"
    exit 1
}

# Check if gradlew exists, if not, try to generate it or use installed gradle
if (-not (Test-Path $gradlew)) {
    Write-Host "gradlew.bat not found. Attempting to build using 'gradle assembleDebug'..."
    Push-Location $androidDir
    try {
        gradle assembleDebug
    } catch {
        Write-Error "Failed to build. Ensure Gradle is installed and in your PATH."
        exit 1
    } finally {
        Pop-Location
    }
} else {
    Write-Host "Building using gradlew..."
    Push-Location $androidDir
    try {
        & .\gradlew.bat assembleDebug
    } finally {
        Pop-Location
    }
}

$apkPath = Join-Path $androidDir "app/build/outputs/apk/debug/app-debug.apk"
if (Test-Path $apkPath) {
    Write-Host "`nSuccess! APK built at: $apkPath" -ForegroundColor Green
} else {
    Write-Error "Build failed or APK not found at expected location."
}
