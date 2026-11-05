# Pure EditMode tests (or the finite-world golden hashes) in the Mono runtime the Unity editor uses, without the editor.
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/MonoCheck/run.ps1 [-Filter EndlessGeneratorTests.FiniteWorld] [-Golden] [-Clr] [-NoBuild]
# Builds the compile-check projects (Tools/CompileCheck, variant "mono": optimised IL, as Unity's ScriptAssemblies are) and
# copies MedievalZen.dll + MedievalZen.Tests.dll to bin\run, then runs MonoRunner.exe inside the editor's
# EmbedRuntime\mono-2.0-bdwgc.dll with --optimize=-float32 (embedded.ps1): this reproduced the editor's Test Runner hashes
# bit for bit on 2026-10-06 (the standalone bin\mono.exe is another build and gives other last bits). -Clr runs the same
# assemblies on CoreCLR (.NET 8) to compare runtimes. Builds with UseSharedCompilation=false, so no compiler server keeps
# Unity's files mapped. Tests that need the engine are reported as NATIVE and skipped. MonoRunner.exe is compiled from
# MonoRunner.cs with the editor's own csc when missing or older than its source. UNITY_EDITOR overrides the editor path.
param([string]$Filter = '', [switch]$Golden, [switch]$Clr, [switch]$NoBuild)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$editor = if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } else { 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor' }
$mbe = Join-Path $editor 'Data\MonoBleedingEdge'
$run = Join-Path $here 'bin\run'

if (-not $NoBuild) {
    $proj = Join-Path $here '..\CompileCheck\MedievalZen.Tests.csproj'
    $output = & dotnet build $proj -nologo -v q -clp:NoSummary -p:Variant=mono\ -p:Optimize=true -p:GenerateDocumentationFile=false -p:UseSharedCompilation=false -nodeReuse:false 2>&1 | ForEach-Object { "$_" }
    $errors = $output | Where-Object { $_ -match ': error ' } | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0 -or $errors) { $errors | ForEach-Object { Write-Output $_ }; Write-Output 'build failed'; exit 1 }
    New-Item -ItemType Directory -Force $run | Out-Null
    $built = Join-Path $here '..\CompileCheck\bin\mono\MedievalZen.Tests\Debug\netstandard2.1'
    Copy-Item (Join-Path $built 'MedievalZen.dll'), (Join-Path $built 'MedievalZen.Tests.dll') $run -Force
    Write-Output "built $run"
}

$runner = Join-Path $here 'MonoRunner.exe'
$source = Join-Path $here 'MonoRunner.cs'
if (-not (Test-Path $runner) -or (Get-Item $runner).LastWriteTime -lt (Get-Item $source).LastWriteTime) {
    & (Join-Path $mbe 'bin\mono.exe') (Join-Path $mbe 'lib\mono\4.5\csc.exe') -nologo -optimize+ "-out:$runner" $source
    if ($LASTEXITCODE -ne 0) { Write-Output 'MonoRunner.exe did not compile'; exit 1 }
}

$mode = if ($Golden) { 'golden' } else { 'tests' }
$args2 = @($mode, $run)
if ($Filter) { $args2 += $Filter }
if ($Clr) {
    $clrBin = Join-Path $here 'bin\clr'
    if (-not (Test-Path (Join-Path $clrBin 'ClrRunner.dll'))) {
        dotnet build (Join-Path $here 'clr\ClrRunner.csproj') -nologo -v q -o $clrBin "-p:BaseIntermediateOutputPath=$(Join-Path $here 'bin\clrobj')\" -p:UseSharedCompilation=false -nodeReuse:false | Out-Null
    }
    dotnet (Join-Path $clrBin 'ClrRunner.dll') @args2
} else {
    powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here 'embedded.ps1') -MonoOptions '--optimize=-float32' @args2 2>$null |
        Where-Object { $_ -notmatch 'internal call|out of sync|^$|update, compile|the other too|Do not report|probably have a broken|If you see other|fix your mono' }
}
