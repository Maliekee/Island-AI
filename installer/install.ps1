# Copyright (c) 2026 Maliekee
# SPDX-License-Identifier: GPL-3.0-only
#
# Installs the mod in payload\ into Mad Island. Run install.bat, or:
#   powershell -ExecutionPolicy Bypass -File install.ps1 [-GameDir "D:\...\Mad Island"] [-DryRun] [-Uninstall]
#
# What it does, in order:
#   1. finds the game through Steam (or asks you for the folder)
#   2. if BepInEx 5 is not there yet, copies the bundled one in (payload\bepinex) - nothing is downloaded
#   3. copies the mod into BepInEx\plugins
# What it never does: touch other mods, touch anything in BepInEx\config, or go online.
# -DryRun prints what it would do and changes nothing.
param(
    [string]$GameDir = '',
    [switch]$Uninstall,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$info = Import-PowerShellDataFile (Join-Path $here 'payload\mod.psd1')
$appId = '2739590'
$exe = 'Mad Island.exe'

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Stop-Install($text) { Say "`n$text" 'Red'; exit 1 }
function Act($text, [scriptblock]$do) {
    if ($DryRun) { Say "  would: $text" 'DarkGray'; return }
    Say "  $text"
    & $do
}

function Find-Game {
    $steamRoots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $props = Get-ItemProperty $key -ErrorAction Stop
            foreach ($name in 'SteamPath', 'InstallPath') {
                if ($props.$name) { $steamRoots += [System.IO.Path]::GetFullPath($props.$name) }
            }
        }
        catch { }
    }
    foreach ($root in ($steamRoots | Select-Object -Unique)) {
        # every Steam library, not just the one Steam itself lives in
        $libraries = @($root)
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libraries += $m.Groups[1].Value.Replace('\\', '\')
            }
        }
        foreach ($library in ($libraries | Select-Object -Unique)) {
            $folder = 'Mad Island'
            $manifest = Join-Path $library "steamapps\appmanifest_$appId.acf"
            if (Test-Path $manifest) {
                $m = [regex]::Match((Get-Content $manifest -Raw), '"installdir"\s+"([^"]+)"')
                if ($m.Success) { $folder = $m.Groups[1].Value }
            }
            $candidate = Join-Path $library "steamapps\common\$folder"
            if (Test-Path (Join-Path $candidate $exe)) { return $candidate }
        }
    }
    return $null
}

$what = 'Installing'
if ($Uninstall) { $what = 'Uninstalling' }
Say "$what $($info.Name) $($info.Version)" 'Cyan'
if ($DryRun) { Say '(dry run: nothing will be changed)' 'Yellow' }

# --- 1. the game
if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir) {
    Say "`nCould not find Mad Island through Steam."
    Say 'In Steam: right-click the game, Manage, Browse local files - then paste that folder here.'
    $GameDir = (Read-Host 'Game folder').Trim().Trim('"')
}
if (-not (Test-Path (Join-Path $GameDir $exe))) { Stop-Install "'$GameDir' does not contain $exe." }
Say "Game: $GameDir"

if (Get-Process -Name 'Mad Island' -ErrorAction SilentlyContinue) {
    if (-not $DryRun) { Stop-Install 'The game is running. Close it and run this again.' }
    Say 'The game is running - a real run would stop here.' 'Yellow'
}

$plugins = Join-Path $GameDir 'BepInEx\plugins'

try {
    if ($Uninstall) {
        foreach ($relative in $info.Remove) {
            $target = Join-Path $GameDir $relative
            if (Test-Path $target) { Act "remove $relative" { Remove-Item $target -Recurse -Force } }
            else { Say "  $relative is not there" }
        }
        Say "`nDone. BepInEx, your other mods and all settings were left alone." 'Green'
        exit 0
    }

    # --- 2. BepInEx: only if it is not there
    $core = Join-Path $GameDir 'BepInEx\core\BepInEx.dll'
    $loader = Join-Path $GameDir 'winhttp.dll'
    $bundle = Join-Path $here 'payload\bepinex'
    if (Test-Path $core) {
        $version = (Get-Item $core).VersionInfo.FileVersion
        Say "BepInEx: found, version $version - keeping it"
        if (-not $version.StartsWith('5.')) {
            Stop-Install "$($info.Name) needs BepInEx 5; this game has $version. Nothing was changed."
        }
        if (-not (Test-Path $loader)) {
            Say 'BepInEx is there but its loader (winhttp.dll) is missing, so it would not start.' 'Yellow'
            foreach ($name in 'winhttp.dll', 'doorstop_config.ini', '.doorstop_version') {
                if (-not (Test-Path (Join-Path $GameDir $name))) {
                    Act "restore $name" { Copy-Item (Join-Path $bundle $name) $GameDir }
                }
            }
        }
    }
    else {
        Say "BepInEx: not found - installing the bundled $($info.BepInEx)"
        if (Test-Path $loader) {
            Say 'A winhttp.dll is already in the game folder, but BepInEx is not: another mod loader may be using it.' 'Yellow'
            if (-not $DryRun) {
                $answer = Read-Host 'Replace it with BepInEx''s? [y/N]'
                if ($answer -notmatch '^[yY]') { Stop-Install 'Nothing was changed.' }
            }
        }
        Act 'copy BepInEx into the game folder' {
            Get-ChildItem $bundle -Force | Copy-Item -Destination $GameDir -Recurse -Force
        }
    }
    if ((Test-Path (Join-Path $GameDir 'MelonLoader')) -or (Test-Path (Join-Path $GameDir 'version.dll'))) {
        Say 'Note: MelonLoader also seems to be installed. Two mod loaders in one game can conflict.' 'Yellow'
    }

    # --- 3. the mod. An older copy of our own DLL anywhere under plugins would load twice.
    if (Test-Path $plugins) {
        foreach ($dll in $info.Dlls) {
            foreach ($old in (Get-ChildItem $plugins -Recurse -Filter $dll -ErrorAction SilentlyContinue)) {
                $shown = $old.FullName.Substring($GameDir.Length + 1)
                Act "remove the previous $shown" { Remove-Item $old.FullName -Force }
            }
        }
    }
    Act "copy $($info.Name) into BepInEx\plugins" {
        Get-ChildItem (Join-Path $here 'payload\mod') -Force | Copy-Item -Destination $GameDir -Recurse -Force
    }
}
catch [System.UnauthorizedAccessException] {
    Stop-Install "Windows refused to write to the game folder. Right-click install.bat and choose 'Run as administrator'."
}

if ($DryRun) { Say "`nDry run finished: nothing was changed." 'Green'; exit 0 }
Say "`nDone. Start the game through Steam as usual." 'Green'
Say 'The first start with BepInEx takes a little longer. Settings appear after that first start in'
Say "  $(Join-Path $GameDir 'BepInEx\config')"
Say "To remove the mod again: uninstall.bat.  Source and updates: $($info.Web)"
