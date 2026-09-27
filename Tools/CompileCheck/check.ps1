# Type-checks the first-party Unity code without the Unity editor (which usually holds the
# project lock). Builds the three compile-check projects and prints only errors and warnings.
# Usage (from anywhere):  powershell -NoProfile -ExecutionPolicy Bypass -File Tools/CompileCheck/check.ps1
# Exit code: 0 = all clean, 1 = at least one project has errors.

$ErrorActionPreference = 'Stop'
$projects = @('MedievalZen.Runtime.csproj', 'MedievalZen.Editor.csproj', 'MedievalZen.Tests.csproj')
$failed = $false

foreach ($project in $projects) {
    $path = Join-Path $PSScriptRoot $project
    $output = & dotnet build $path -nologo -v q -clp:NoSummary 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
    $issues = $output | Where-Object { $_ -match ': (error|warning) ' } | Sort-Object -Unique
    if ($exitCode -ne 0) { $failed = $true }

    if ($issues) {
        Write-Output "== $project"
        $issues | ForEach-Object { Write-Output "  $_" }
    }
    elseif ($exitCode -ne 0) {
        Write-Output "== $project FAILED (exit $exitCode)"
        $output | ForEach-Object { Write-Output "  $_" }
    }
    else {
        Write-Output "== $project OK"
    }
}

if ($failed) { exit 1 }
exit 0
