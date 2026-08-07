#Requires -Version 7
<#
.SYNOPSIS
Builds rimage-gui release binaries and compresses them with the system UPX.

.DESCRIPTION
Builds the release GUI for the selected MSVC target(s) and then runs UPX at
level 6 (--compress-icons=0 keeps the icon resource intact). The system UPX
is used when available; compression is skipped when it is not installed.
#>
[CmdletBinding()]
param(
    [ValidateSet("x86_64-pc-windows-msvc", "i686-pc-windows-msvc", "both")]
    [string]$Target = "both",
    [switch]$SkipUpx
)

$ErrorActionPreference = "Stop"

$targets = if ($Target -eq "both") {
    @("x86_64-pc-windows-msvc", "i686-pc-windows-msvc")
} else {
    @($Target)
}

foreach ($target in $targets) {
    Write-Host "Building $target (release)..."
    rustup run "stable-$target" cargo build --release --target $target
}

if ($SkipUpx) {
    Write-Host "UPX compression skipped (-SkipUpx)."
    return
}

$upx = Get-Command upx -ErrorAction SilentlyContinue
if (-not $upx) {
    Write-Warning "UPX is not installed; skipping compression."
    return
}

foreach ($target in $targets) {
    $exe = Join-Path $PSScriptRoot "target\$target\release\rimage-gui.exe"
    $dist = Join-Path $PSScriptRoot "dist"
    $new_exe = Join-Path $dist "rimage-gui.exe"


    if (-not (Test-Path -LiteralPath $exe)) {
        throw "Release binary not found: $exe"
    }

    Write-Host "Compressing $exe with UPX..."
    & $upx.Source -6 -k --force-overwrite --compress-icons=0 $exe

    Write-Host "Copy to dist Dir"
    if ($exe | Select-String -Pattern "_64" -SimpleMatch -Quiet) {
        Copy-Item $exe (Join-Path $dist "rimage-gui_x64.exe")
    } else {
        Copy-Item $exe (Join-Path $dist "rimage-gui_x86.exe")
    }
}
