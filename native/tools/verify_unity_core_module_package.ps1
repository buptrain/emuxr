[CmdletBinding()]
param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$ReadElf = 'C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-readelf.exe'
)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Require-LoadAlignment([string]$Path) {
    $loads = @(& $ReadElf -Wl $Path | Where-Object { $_ -match '^\s*LOAD\s' })
    if ($loads.Count -eq 0 -or ($loads | Where-Object { $_ -notmatch '\s0x4000\s*$' })) { throw "16 KB LOAD alignment missing: $Path" }
}
if (-not (Test-Path -LiteralPath $ReadElf)) { throw "llvm-readelf was not found: $ReadElf" }
$root = Join-Path $ProjectRoot 'unity\Assets\Plugins\Android\GbaXrCoreModuleExperimental\arm64-v8a'
$manifestPath = Join-Path $root 'package-manifest.json'
$hostPath = Join-Path $root 'libemuxr_core_host.so'
$modulePath = Join-Path $root 'emuxr_mgba_libretro_core.so'
$directPath = Join-Path $ProjectRoot 'unity\Assets\Plugins\Android\arm64-v8a\libgba_xr.so'
foreach ($path in @($manifestPath, $hostPath, $modulePath, $directPath)) { if (-not (Test-Path -LiteralPath $path)) { throw "Required package file is missing: $path" } }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not $manifest.experimental -or $manifest.runtimeSelection -ne 'disabled') { throw 'Package must remain experimental and runtime-disabled.' }
if ((Get-Sha256 $directPath) -ne $manifest.directPluginIntegrity.sha256) { throw 'Direct libgba_xr.so changed.' }
foreach ($artifact in $manifest.artifacts) { $path = Join-Path $root $artifact.path; if ((Get-Sha256 $path) -ne $artifact.sha256) { throw "Artifact hash mismatch: $($artifact.path)" }; Require-LoadAlignment $path }
$hostDynamic = (& $ReadElf -d $hostPath) -join "`n"
if ($hostDynamic -match 'mgba|libretro') { throw 'Fixed host has an mGBA/libretro ELF dependency.' }
$hostSymbols = (& $ReadElf -Ws $hostPath) -join "`n"
foreach ($symbol in @('emuxr_core_host_create', 'emuxr_core_host_start_module', 'emuxr_core_host_destroy')) { if ($hostSymbols -notmatch "\bGLOBAL\b.*$symbol") { throw "Fixed host export missing: $symbol" } }
$moduleSymbols = (& $ReadElf -Ws $modulePath) -join "`n"
if ($moduleSymbols -notmatch '\bGLOBAL\b.*emuxr_core_module_get_descriptor_v1' -or $moduleSymbols -notmatch '\bGLOBAL\b.*retro_run') { throw 'Core module descriptor or libretro exports are missing.' }
Write-Output "GBA XR fixed-host core-module package verified: host=$(Get-Sha256 $hostPath); module=$(Get-Sha256 $modulePath)"
