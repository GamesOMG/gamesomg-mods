# GamesOMG Teleport for Satisfactory: build and package.
#   powershell -File satisfactory\build.ps1            → compile (editor target: proves the code builds)
#   powershell -File satisfactory\build.ps1 -Package   → + the release zip for Windows, WindowsServer, LinuxServer
# Needs: Coffee Stain's engine (UE 5.6.1-CSS) registered as "5.6.1-CSS", VS 2022 with MSVC 14.38, the Linux cross toolchain
# (LINUX_MULTIARCH_ROOT), Wwise integrated into the starter project (D:\Tools\sml-toolchain\wwise.ps1), the SML starter
# project at D:\SatisfactoryModding\SML (SML 3.12.1). Same commands SML's own CI runs (SatisfactoryModLoader .github/workflows/build.yml).
# The uplugin's integer "Version" must equal SemVersion's major (1 for 1.x.y): ficsit.app refuses an upload where they
# differ ("SemVer major version should match Version"). 1.1.0 and 1.1.1 on GitHub carry 2 and 3; SMR's 1.1.1 carries 1.
param([switch]$Package, [string]$Project = 'D:\SatisfactoryModding\SML')
$ErrorActionPreference = 'Stop'

$engine = (Get-ItemProperty 'HKCU:\Software\Epic Games\Unreal Engine\Builds' -ErrorAction SilentlyContinue).'5.6.1-CSS'
if (-not $engine) { $engine = 'C:\Program Files\Unreal Engine - CSS' }
if (-not (Test-Path "$engine\Engine\Build\BatchFiles\Build.bat")) { throw "No engine at $engine (register it as 5.6.1-CSS)" }
# the Linux cross toolchain is registered machine-wide; a shell started before that won't have it
if (-not $env:LINUX_MULTIARCH_ROOT) { $env:LINUX_MULTIARCH_ROOT = [Environment]::GetEnvironmentVariable('LINUX_MULTIARCH_ROOT', 'Machine') }
if ($Package -and -not (Test-Path "$env:LINUX_MULTIARCH_ROOT")) { throw "No Linux toolchain (LINUX_MULTIARCH_ROOT)" }
$uproject = Join-Path $Project 'FactoryGame.uproject'
$src = Join-Path $PSScriptRoot 'GamesOMGTeleport'
$dst = Join-Path $Project 'Mods\GamesOMGTeleport'

# the mod's source lives in the repo; the starter project gets a mirror (its Binaries/Intermediate stay)
robocopy $src $dst /MIR /XD Binaries Intermediate Saved /NJH /NJS /NP /NFL /NDL | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

$t0 = Get-Date
& "$engine\Engine\Build\BatchFiles\Build.bat" FactoryEditor Win64 Development "-project=$uproject" -WaitMutex -NoHotReload
if ($LASTEXITCODE -ne 0) { throw "editor build failed ($LASTEXITCODE)" }
Write-Host ("editor build ok in {0:n0} s" -f ((Get-Date) - $t0).TotalSeconds)

if ($Package) {
  $t0 = Get-Date
  & "$engine\Engine\Build\BatchFiles\RunUAT.bat" "-ScriptsForProject=$uproject" PackagePlugin "-Project=$uproject" `
    -dlcname=GamesOMGTeleport -merge -build -server -clientconfig=Shipping -serverconfig=Shipping `
    -platform=Win64 -serverplatform=Win64+Linux -nocompileeditor -installed
  if ($LASTEXITCODE -ne 0) { throw "package failed ($LASTEXITCODE)" }
  $out = Join-Path $Project 'Saved\ArchivedPlugins\GamesOMGTeleport'
  Get-ChildItem $out -Filter *.zip | ForEach-Object { '{0}  {1:n0} bytes  {2}' -f $_.Name, $_.Length, (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower() }
  Write-Host ("package ok in {0:n0} s → {1}" -f ((Get-Date) - $t0).TotalSeconds, $out)
}
