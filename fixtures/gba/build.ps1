param(
    [string]$ToolchainBin = "C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK\toolchains\llvm\prebuilt\windows-x86_64\bin",
    [ValidateSet("frame_sequence", "bg0_priority", "bg_tie")]
    [string]$Fixture = "frame_sequence",
    [switch]$InstallIntoUnityStreamingAssets
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "out"
$clang = Join-Path $ToolchainBin "clang.exe"
$linker = Join-Path $ToolchainBin "ld.lld.exe"
$objcopy = Join-Path $ToolchainBin "llvm-objcopy.exe"

foreach ($tool in @($clang, $linker, $objcopy)) {
    if (-not (Test-Path -LiteralPath $tool)) {
        throw "Required LLVM tool was not found: $tool"
    }
}

New-Item -ItemType Directory -Force -Path $out | Out-Null
$source = Join-Path $root "$Fixture.s"
$object = Join-Path $out "$Fixture.o"
$elf = Join-Path $out "$Fixture.elf"
$rom = Join-Path $out "$Fixture.gba"

if (-not (Test-Path -LiteralPath $source)) {
    throw "Fixture source was not found: $source"
}

& $clang --target=arm-none-eabi -mcpu=arm7tdmi -marm -ffreestanding -fno-pic -c $source -o $object
& $linker -T (Join-Path $root "linker.ld") -o $elf $object
& $objcopy -O binary $elf $rom

Write-Host "Built synthetic fixture: $rom"

if ($InstallIntoUnityStreamingAssets) {
    $streamingAssets = Join-Path $root "..\..\unity\Assets\StreamingAssets\gba-fixtures"
    New-Item -ItemType Directory -Force -Path $streamingAssets | Out-Null
    Copy-Item -LiteralPath $rom -Destination (Join-Path $streamingAssets "$Fixture.gba") -Force
    Write-Host "Installed synthetic fixture into Unity StreamingAssets. Rebuild the APK to package it."
}
