# GamesOMG Item Spawner (from GamesOMG Teleport): the one public compile reference, BepInEx.dll, pinned to its official release digest.
$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 has neither loaded by default (2026-11-19: a clean run stopped on [IO.Compression.ZipFile])
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$taskDependencyRoot = Join-Path $PSScriptRoot '.deps'
$taskArchive = Join-Path $taskDependencyRoot 'BepInEx_win_x64_5.4.23.4.zip'
$taskReference = Join-Path $taskDependencyRoot 'BepInEx.dll'
$taskExpected = 'F881201B79DA03E513BF97CDF39607FFA7F9E0D31A519B1AEECA8EB60F8309E7'
New-Item -ItemType Directory -Force -Path $taskDependencyRoot | Out-Null
if (-not (Test-Path -LiteralPath $taskArchive)) {
    Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip' -OutFile $taskArchive
}
if ((Get-FileHash -LiteralPath $taskArchive -Algorithm SHA256).Hash -ne $taskExpected) {
    throw 'The compile-reference archive differs from the pinned official digest.'
}
$taskZip = [IO.Compression.ZipFile]::OpenRead($taskArchive)
try {
    $taskEntry = $taskZip.GetEntry('BepInEx/core/BepInEx.dll')
    if ($null -eq $taskEntry) { throw 'The pinned archive has no expected BepInEx reference.' }
    $taskInput = $taskEntry.Open()
    $taskOutput = [IO.File]::Create($taskReference)
    try { $taskInput.CopyTo($taskOutput) }
    finally { $taskInput.Dispose(); $taskOutput.Dispose() }
} finally { $taskZip.Dispose() }
Write-Output 'Verified BepInEx 5.4.23.4 compile reference; no game or loader has been executed.'
