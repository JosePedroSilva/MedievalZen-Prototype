# Runs MonoRunner.exe inside the Mono runtime the Unity EDITOR embeds (EmbedRuntime\mono-2.0-bdwgc.dll with the
# unityjit-win32 class libraries, as Editor.log lists them), hosted by this PowerShell process through Mono's embedding API.
# The standalone bin\mono.exe is a different build (other class libraries) and can give different last bits.
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File embedded.ps1 [-DebugAgent] golden|tests <assembly dir> [filter]
param([switch]$DebugAgent, [string]$MonoOptions = '', [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest)
$ErrorActionPreference = 'Stop'
$editor = if ($env:UNITY_EDITOR) { $env:UNITY_EDITOR } else { 'C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor' }
$mbe = Join-Path $editor 'Data\MonoBleedingEdge'
$here = $PSScriptRoot
$dll = Join-Path $mbe 'EmbedRuntime\mono-2.0-bdwgc.dll'
$src = @"
using System;
using System.Runtime.InteropServices;
public static class MonoHost
{
    const string M = @"$dll";
    [DllImport(M)] public static extern void mono_set_dirs(string assemblyDir, string configDir);
    [DllImport(M)] public static extern void mono_set_assemblies_path(string path);
    [DllImport(M)] public static extern void mono_jit_parse_options(int argc, string[] argv);
    [DllImport(M)] public static extern void mono_debug_init(int format);
    [DllImport(M)] public static extern IntPtr mono_jit_init_version(string name, string version);
    [DllImport(M)] public static extern IntPtr mono_domain_assembly_open(IntPtr domain, string name);
    [DllImport(M)] public static extern int mono_jit_exec(IntPtr domain, IntPtr assembly, int argc, string[] argv);
}
"@
Add-Type -TypeDefinition $src
# Editor.log: Mono path[0] = Data/Managed, Mono path[1] = MonoBleedingEdge/lib/mono/unityjit-win32, config = MonoBleedingEdge/etc.
[MonoHost]::mono_set_dirs((Join-Path $mbe 'lib'), (Join-Path $mbe 'etc'))
[MonoHost]::mono_set_assemblies_path((Join-Path $editor 'Data\Managed') + ';' + (Join-Path $mbe 'lib\mono\unityjit-win32') + ';' + (Join-Path $editor 'Data\Managed\UnityEngine'))
if ($DebugAgent) {
    # The editor's monoOptions in its default Debug code optimisation mode (Editor.log "Using monoOptions").
    [MonoHost]::mono_jit_parse_options(1, @('--debugger-agent=transport=dt_socket,embedding=1,server=y,suspend=n,address=127.0.0.1:56999'))
    [MonoHost]::mono_debug_init(1)
}
if ($MonoOptions) { $o = $MonoOptions.Split(' '); [MonoHost]::mono_jit_parse_options($o.Length, $o) }
$domain = [MonoHost]::mono_jit_init_version('MonoRunner', 'v4.0.30319')
$exe = Join-Path $here $(if ($env:MZ_RUNNER) { $env:MZ_RUNNER } else { 'MonoRunner.exe' })
$asm = [MonoHost]::mono_domain_assembly_open($domain, $exe)
$argv = @($exe) + $Rest
$r = [MonoHost]::mono_jit_exec($domain, $asm, $argv.Length, $argv)
exit $r
