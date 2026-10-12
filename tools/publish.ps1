<#
.SYNOPSIS
    Builds the release BtVolumeGuard.exe and optionally installs it into a folder.

.DESCRIPTION
    Publishes a framework-dependent, single-file win-x64 build (needs the .NET 10 Desktop Runtime
    on the machine) to <repo>\publish\BtVolumeGuard.exe.

    With -InstallDir the executable is copied into that folder. A running BtVolumeGuard is stopped
    first and started again afterwards. Settings, remembered volumes and the log live next to the
    executable and are left untouched. The folder must be writable by the user, so not
    Program Files.

.EXAMPLE
    .\tools\publish.ps1

.EXAMPLE
    .\tools\publish.ps1 -InstallDir D:\Tools\BtVolumeGuard

.EXAMPLE
    .\tools\publish.ps1 -Version 0.2.0    # overrides <Version> from the project (used by CI for tags)
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$publishDir = Join-Path $root 'publish'

[string[]]$versionArgs = if ($Version) { "-p:Version=$Version" } else { @() }
dotnet publish (Join-Path $root 'src\BtVolumeGuard') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=embedded @versionArgs `
    -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $publishDir 'BtVolumeGuard.exe'
"Published $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)."

if (-not $InstallDir) { return }

$InstallDir = [IO.Path]::GetFullPath($InstallDir)
if ($InstallDir.StartsWith($env:ProgramFiles, 'OrdinalIgnoreCase') -or
    ($env:ProgramW6432 -and $InstallDir.StartsWith($env:ProgramW6432, 'OrdinalIgnoreCase')) -or
    (${env:ProgramFiles(x86)} -and $InstallDir.StartsWith(${env:ProgramFiles(x86)}, 'OrdinalIgnoreCase'))) {
    throw "Program Files is not writable without admin rights; settings and log could not be saved there."
}
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

$target = Join-Path $InstallDir 'BtVolumeGuard.exe'
$running = @(Get-Process BtVolumeGuard -ErrorAction SilentlyContinue)
$wasRunningFromTarget = $running | Where-Object { $_.Path -and $_.Path -ieq $target }
if ($running) {
    "Stopping running BtVolumeGuard ($($running.Path -join ', '))."
    $running | Stop-Process
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

# Windows may keep the file locked for a moment after the process exits.
for ($attempt = 1; ; $attempt++) {
    try {
        Copy-Item $exe $target -Force
        break
    }
    catch [System.IO.IOException] {
        if ($attempt -ge 20) { throw }
        Start-Sleep -Milliseconds 250
    }
}
"Installed $target."

if ($wasRunningFromTarget) {
    Start-Process $target
    "Started $target again."
}
