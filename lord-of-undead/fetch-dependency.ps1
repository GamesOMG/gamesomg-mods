# fetch-dependency.ps1 - the BepInEx 6 IL2CPP loader this mod builds against and ships in its Easy-Install zip, pinned to
# the digest of the copy the 2026-11-27 gate sitting booted Lord of Undead with (builds.bepinex.dev publishes no digest).
#   powershell -ExecutionPolicy Bypass -File fetch-dependency.ps1 [-Zip <a local copy of the zip>]
# Writes .deps\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip and unpacks it to .deps\bepinex\. Runs nothing.
param([string]$Zip = '')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$deps = Join-Path $PSScriptRoot '.deps'
$name = 'BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip'
$archive = Join-Path $deps $name
$expected = '5B63D7D0E92A2448CCD13B738FF95FF1CC1026C6D718555D4DF8E6C824953AC4'
New-Item -ItemType Directory -Force -Path $deps | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    if ($Zip) { Copy-Item -LiteralPath $Zip -Destination $archive }
    else { Invoke-WebRequest 'https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip' -OutFile $archive }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) {
    Remove-Item -LiteralPath $archive
    throw 'That BepInEx zip differs from the pinned be.793 digest; deleted.'
}
$out = Join-Path $deps 'bepinex'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $out)
Write-Output "Verified BepInEx 6.0.0-be.793 (SHA-256 $expected), unpacked to $out. Nothing was run."
