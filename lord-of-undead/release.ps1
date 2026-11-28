# release.ps1 - build GamesOMG Teleport for Lord of Undead's release zips (Windows PowerShell 5.1).
#   powershell -ExecutionPolicy Bypass -File release.ps1 [-Test]
# Needs fetch-dependency.ps1 run first (.deps\BepInEx-...be.793...zip, checked against its pinned digest here again) and a
# Release build (dotnet build -c Release). Writes release-out\<version>\:
#   LordOfUndead-GamesOMG-Teleport-<v>-Easy-Install.zip   BepInEx 6.0.0-be.793 exactly as published (every file of its zip,
#                                                        unmodified) + BepInEx\plugins\GamesOMGTeleport\ + the notices.
#                                                        Unzip into the game folder (next to Lord of Undead.exe).
#   LordOfUndead-GamesOMG-Teleport-<v>-Plugin-Only.zip    just BepInEx\plugins\GamesOMGTeleport\, for players who already
#                                                        run BepInEx 6 IL2CPP. Unzip into the game folder.
#   SHA256SUMS.txt
# Refuses: a BepInEx zip that isn't the pinned one, a DLL whose version isn't the csproj's, a CHANGELOG with no section for
# the version or one still marked "not released" (unless -Test: a sitting's zips, never published), the survey build, a
# licence text missing from licenses\.
param([switch]$Test)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$here = $PSScriptRoot
$proj = [IO.File]::ReadAllText((Join-Path $here 'GamesOMG.LordOfUndeadTeleport.csproj'))
if ($proj -notmatch '<Version>([0-9.]+)</Version>') { throw 'no <Version> in the csproj' }
$v = $Matches[1]
$cl = [IO.File]::ReadAllText((Join-Path $here 'CHANGELOG.md'))
if ($cl -notmatch "(?m)^## $([regex]::Escape($v))\b(.*)$") { throw "CHANGELOG.md has no section for $v" }
if ($Matches[1] -match 'not released' -and -not $Test) { throw "CHANGELOG.md still says ${v} is not released, so run the survey, then take that out (or -Test for a sitting's zips)" }

$bz = Join-Path $here '.deps\BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793+dbc82f0.zip'
if ((Get-FileHash $bz -Algorithm SHA256).Hash -ne '5B63D7D0E92A2448CCD13B738FF95FF1CC1026C6D718555D4DF8E6C824953AC4') { throw 'the BepInEx zip is not the pinned be.793' }
$dll = Join-Path $here 'bin\Release\net6.0\GamesOMG.LordOfUndeadTeleport.dll'
if (-not (Test-Path $dll)) { throw 'no Release build: dotnet build -c Release' }
$fv = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
if (-not $fv.StartsWith($v)) { throw "the DLL is version $fv, the csproj says ${v}: rebuild" }
$bytes = [IO.File]::ReadAllBytes($dll)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
if ($ascii.Contains('GamesOMG.LordOfUndeadTeleport.Survey') -or $ascii.Contains('gamesomg-tp-survey')) { throw 'that DLL carries the survey: build without -p:Survey=true' }
$notices = Join-Path $here 'licenses\THIRD-PARTY-NOTICES.txt'
$need = 'THIRD-PARTY-NOTICES.txt', 'BepInEx-LICENSE.txt', 'Il2CppInterop-LICENSE.txt', 'UnityDoorstop-LICENSE.txt', 'HarmonyX-LICENSE.txt',
        'MonoMod-LICENSE.txt', 'Cpp2IL-LICENSE.txt', 'dotnet-runtime-LICENSE.txt', 'Dobby-LICENSE.txt'
$have = @()
foreach ($n in $need) {
    if (Test-Path (Join-Path $here "licenses\$n")) { $have += $n; continue }
    if ($Test) { Write-Warning "licenses\$n is missing (fine for a sitting's TEST zip, never for a release)"; continue }
    throw "licenses\$n is missing (each component's own licence text, from its repository at the tag the notices name)"
}

$out = Join-Path $here "release-out\$v"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
$easy = Join-Path $out "LordOfUndead-GamesOMG-Teleport-$v-Easy-Install.zip"
$only = Join-Path $out "LordOfUndead-GamesOMG-Teleport-$v-Plugin-Only.zip"

function Add-File($zip, $src, $entry) {
    $e = $zip.CreateEntry($entry.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
    $s = $e.Open(); try { $b = [IO.File]::ReadAllBytes($src); $s.Write($b, 0, $b.Length) } finally { $s.Dispose() }
}
function Add-Mod($zip) {
    Add-File $zip $dll 'BepInEx\plugins\GamesOMGTeleport\GamesOMG.LordOfUndeadTeleport.dll'
    Add-File $zip (Join-Path $here 'README.md') 'BepInEx\plugins\GamesOMGTeleport\README.md'
    Add-File $zip (Join-Path $here 'LICENSE') 'BepInEx\plugins\GamesOMGTeleport\LICENSE.txt'
}

$z = [IO.Compression.ZipFile]::Open($easy, 'Create')
try {
    $src = [IO.Compression.ZipFile]::OpenRead($bz)
    try {
        foreach ($e in $src.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            $d = $z.CreateEntry($e.FullName, [IO.Compression.CompressionLevel]::Optimal)
            $i = $e.Open(); $o = $d.Open(); try { $i.CopyTo($o) } finally { $i.Dispose(); $o.Dispose() }
        }
    } finally { $src.Dispose() }
    Add-Mod $z
    foreach ($n in $have) { Add-File $z (Join-Path $here "licenses\$n") "BepInEx\plugins\GamesOMGTeleport\licenses\$n" }
} finally { $z.Dispose() }

$z = [IO.Compression.ZipFile]::Open($only, 'Create')
try { Add-Mod $z } finally { $z.Dispose() }

$sums = foreach ($f in $easy, $only) { '{0}  {1}' -f (Get-FileHash $f -Algorithm SHA256).Hash.ToLower(), (Split-Path $f -Leaf) }
[IO.File]::WriteAllLines((Join-Path $out 'SHA256SUMS.txt'), $sums)
$sums
if ($Test) { Write-Output "TEST zips (not for release): $out" } else { Write-Output "release zips: $out" }
