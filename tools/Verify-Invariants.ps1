<#
.SYNOPSIS
    Static checks for defects the compiler and the self-test suite cannot catch.

.DESCRIPTION
    Two classes of failure have reached a release build before:

    1. A XAML StaticResource/DynamicResource key that does not exist. The compiler
       accepts it and the application throws XamlParseException on startup.
    2. A self-test method that is written but never added to the registration array
       in tests/Aurum.Core.SelfTests/Program.cs, so it silently never runs.

    Both are cheap to detect by inspection, so they run in CI ahead of the build.

.PARAMETER RepositoryRoot
    Repository root. Defaults to the parent of this script's directory.
#>
[CmdletBinding()]
param(
    [string]$RepositoryRoot
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

$failures = New-Object System.Collections.Generic.List[string]

function Get-ProjectFile {
    param([string]$Pattern)

    Get-ChildItem -Path $RepositoryRoot -Recurse -File -Filter $Pattern |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.dotnet|artifacts|dist)\\' }
}

Write-Host 'Checking XAML resource keys...' -ForegroundColor Cyan

$xamlFiles = @(Get-ProjectFile -Pattern '*.xaml')
if ($xamlFiles.Count -eq 0) {
    $failures.Add('No XAML files were found; the resource check could not run.')
}

$definedKeys = @{}
foreach ($file in $xamlFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($text, 'x:Key="([^"]+)"')) {
        $definedKeys[$match.Groups[1].Value] = $file.Name
    }
}

$resourceReferences = 0
foreach ($file in $xamlFiles) {
    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($match in [regex]::Matches($lines[$i], '(?:Static|Dynamic)Resource\s+([^}"\s]+)')) {
            $resourceReferences++
            $key = $match.Groups[1].Value
            if (-not $definedKeys.ContainsKey($key)) {
                $failures.Add("$($file.Name):$($i + 1) references undefined resource key '$key'.")
            }
        }
    }
}

Write-Host "  $($definedKeys.Count) keys defined, $resourceReferences references checked." -ForegroundColor Gray

Write-Host 'Checking self-test registration...' -ForegroundColor Cyan

$testProgram = Join-Path $RepositoryRoot 'tests\Aurum.Core.SelfTests\Program.cs'
if (-not (Test-Path -LiteralPath $testProgram)) {
    $failures.Add("Self-test program not found at '$testProgram'.")
}
else {
    $text = Get-Content -LiteralPath $testProgram -Raw -Encoding UTF8

    # Comments must be stripped first, otherwise a commented-out registration still
    # matches and the check passes while the test silently stops running.
    $text = [regex]::Replace($text, '/\*.*?\*/', '', 'Singleline')
    $text = ($text -split "`n" | ForEach-Object {
        $index = $_.IndexOf('//')
        if ($index -ge 0) { $_.Substring(0, $index) } else { $_ }
    }) -join "`n"

    # Test bodies are declared as `private static [async] Task <Name>Async()`.
    $declared = [regex]::Matches($text, '(?m)^\s*private\s+static\s+(?:async\s+)?Task\s+(\w+Async)\s*\(\s*\)') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique

    # The registration array holds `("display name", MethodNameAsync)` tuples.
    $registered = [regex]::Matches($text, '\(\s*"[^"]*"\s*,\s*(\w+Async)\s*\)') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique

    foreach ($name in $declared) {
        if ($registered -notcontains $name) {
            $failures.Add("Self-test '$name' is declared but never registered, so it never runs.")
        }
    }

    Write-Host "  $($declared.Count) test methods declared, $($registered.Count) registered." -ForegroundColor Gray
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "FAILED: $($failures.Count) problem(s) found." -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host ''
Write-Host 'All invariants hold.' -ForegroundColor Green
exit 0
