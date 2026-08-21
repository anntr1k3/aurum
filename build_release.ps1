[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"

$rootDir = if ($PSScriptRoot) { $PSScriptRoot } else { $PWD.Path }
if ([string]::IsNullOrWhiteSpace($OutputDir) -or $OutputDir -eq "\dist") {
    $OutputDir = Join-Path $rootDir "dist"
}

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "   AURUM - ATLASOS COMPANION RELEASE BUILDER      " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

$dotnetExe = Join-Path $rootDir ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $dotnetExe = "dotnet"
}

Write-Host "`n[1/4] Verifying project invariants..." -ForegroundColor Yellow
& (Join-Path $rootDir "tools\Verify-Invariants.ps1") -RepositoryRoot $rootDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "Invariant checks failed. Build aborted."
    exit 1
}

Write-Host "`n[2/4] Running tests (Aurum.Core.SelfTests)..." -ForegroundColor Yellow
& $dotnetExe run --project (Join-Path $rootDir "tests\Aurum.Core.SelfTests") --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed. Build aborted."
    exit 1
}

Write-Host "`n[3/4] Preparing output directory: $OutputDir" -ForegroundColor Yellow
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "`n[4/4] Publishing standalone single-file binary..." -ForegroundColor Yellow
& $dotnetExe publish (Join-Path $rootDir "src\Aurum.App\Aurum.App.csproj") `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    --output $OutputDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed."
    exit 1
}

$exePath = Join-Path $OutputDir "Aurum.exe"
if (Test-Path $exePath) {
    $fileInfo = Get-Item $exePath
    $sizeMb = [math]::Round($fileInfo.Length / 1MB, 2)
    $hash = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash

    Write-Host "`n==================================================" -ForegroundColor Green
    Write-Host "   BUILD SUCCEEDED!                               " -ForegroundColor Green
    Write-Host "==================================================" -ForegroundColor Green
    Write-Host "File:    $exePath" -ForegroundColor White
    Write-Host "Size:    $sizeMb MB" -ForegroundColor White
    Write-Host "SHA-256: $hash" -ForegroundColor Gray
    Write-Host "==================================================" -ForegroundColor Green
}
else {
    Write-Warning "Aurum.exe was not found in $OutputDir"
}

