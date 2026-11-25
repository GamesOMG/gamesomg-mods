# release.ps1 - build GamesOMG Teleport for Grounded 2's release zips (Windows PowerShell 5.1).
#   powershell -ExecutionPolicy Bypass -File release.ps1 -UE4SSZip <path to UE4SS_v4.0.0.zip>
# Writes release-out\<version>\:
#   Grounded2-GamesOMG-Teleport-<v>-Easy-Install.zip   UE4SS 4.0.0 (its DLLs, licence, shared Lua helpers; NO stock mods)
#                                                     + this mod. Unzip into Grounded2\Augusta\Binaries\Win64.
#   Grounded2-GamesOMG-Teleport-<v>-Mod-Only.zip       just Augusta\Binaries\Win64\ue4ss\Mods\GamesOMGTeleport, for players who
#                                                     already run UE4SS 4. Unzip into the Grounded 2 folder. The game's own
#                                                     layout from its root, because Vortex's Grounded 2 installer cancels a zip
#                                                     whose root is ue4ss\Mods (night runner's Vortex test, 11-24). Written
#                                                     with / in its entry names (.NET ZipFile), not Compress-Archive's \.
#   SHA256SUMS.txt
# Refuses: a UE4SS zip that isn't the v4.0.0 release (hash), a version mismatch, a provisional places.lua, any file not on
# the exact lists below (dev harness, survey files, logs, recent places never ship).
param(
    [Parameter(Mandatory = $true)][string]$UE4SSZip
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$mod = Join-Path $here "GamesOMGTeleport"
$main = [IO.File]::ReadAllText((Join-Path $mod "Scripts\main.lua"))
if ($main -notmatch 'local VERSION = "([0-9.]+)"') { throw "no VERSION in main.lua" }
$v = $Matches[1]
$cl = [IO.File]::ReadAllText((Join-Path $here "CHANGELOG.md"))
if ($cl -notmatch "## $([regex]::Escape($v))\b") { throw "CHANGELOG.md has no section for $v" }
$places = [IO.File]::ReadAllText((Join-Path $mod "Scripts\places.lua"))
if ($places -match 'PROVISIONAL') { throw "places.lua is the provisional UI-test list: build the real list from a survey first" }

$want = "251ad0d1381a241750e036f5918cbf2b5d7ec58ebc608cb2dd09d459d0c81e45"   # github.com/UE4SS-RE/RE-UE4SS/releases/tag/v4.0.0
if ((Get-FileHash $UE4SSZip -Algorithm SHA256).Hash.ToLower() -ne $want) { throw "that is not the UE4SS v4.0.0 release zip" }

$out = Join-Path $here "release-out\$v"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
$stage = Join-Path $out "stage"
$ue = Join-Path $stage "easy"
$tmp = Join-Path $out "ue4ss-src"
New-Item -ItemType Directory -Force $ue, $tmp | Out-Null
Expand-Archive -Path $UE4SSZip -DestinationPath $tmp

# UE4SS itself, without its stock mods (they turn on the cheat manager and console; ours needs none of them)
Copy-Item (Join-Path $tmp "dwmapi.dll") $ue
New-Item -ItemType Directory -Force (Join-Path $ue "ue4ss\Mods") | Out-Null
foreach ($f in "UE4SS.dll", "LICENSE") { Copy-Item (Join-Path $tmp "ue4ss\$f") (Join-Path $ue "ue4ss\$f") }
Copy-Item (Join-Path $tmp "ue4ss\UE4SS_SDK_Backends") (Join-Path $ue "ue4ss") -Recurse
Copy-Item (Join-Path $tmp "ue4ss\Mods\shared") (Join-Path $ue "ue4ss\Mods") -Recurse
$ini = [IO.File]::ReadAllText((Join-Path $tmp "ue4ss\UE4SS-settings.ini"))
$ini = $ini -replace '(?m)^MajorVersion\s*=.*$', 'MajorVersion = 5'      # Grounded 2 0.6 = UE 5.7.4 (proven 11-19/11-20)
$ini = $ini -replace '(?m)^MinorVersion\s*=.*$', 'MinorVersion = 7'
$ini = $ini -replace '(?m)^ConsoleEnabled\s*=.*$', 'ConsoleEnabled = 0'
$ini = $ini -replace '(?m)^GuiConsoleEnabled\s*=.*$', 'GuiConsoleEnabled = 0'
$ini = $ini -replace '(?m)^GuiConsoleVisible\s*=.*$', 'GuiConsoleVisible = 0'
$ini = $ini -replace '(?m)^EnableAutoReloadingLuaMods\s*=.*$', 'EnableAutoReloadingLuaMods = 0'
[IO.File]::WriteAllText((Join-Path $ue "ue4ss\UE4SS-settings.ini"), $ini, (New-Object Text.ASCIIEncoding))
[IO.File]::WriteAllText((Join-Path $ue "ue4ss\Mods\mods.txt"), "GamesOMGTeleport : 1`r`n`r`n; Built-in keybinds, do not move up!`r`nKeybinds : 0`r`n", (New-Object Text.ASCIIEncoding))

# the mod: exactly these files
$files = "enabled.txt", "settings.txt", "Scripts\main.lua", "Scripts\places.lua"
$modDst = Join-Path $ue "ue4ss\Mods\GamesOMGTeleport"
foreach ($f in $files) {
    $d = Join-Path $modDst $f
    New-Item -ItemType Directory -Force (Split-Path $d) | Out-Null
    Copy-Item (Join-Path $mod $f) $d
}
foreach ($f in "README.md", "LICENSE") { Copy-Item (Join-Path $here $f) (Join-Path $modDst $f) }
Copy-Item (Join-Path $tmp "ue4ss\LICENSE") (Join-Path $modDst "UE4SS-LICENSE.txt")

$mo = Join-Path $stage "modonly\Augusta\Binaries\Win64\ue4ss\Mods"
New-Item -ItemType Directory -Force $mo | Out-Null
Copy-Item $modDst $mo -Recurse

# exact lists: anything else in a zip is a mistake
function Listing($root) { Get-ChildItem $root -Recurse -File | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') } | Sort-Object }
$modList = @("enabled.txt", "LICENSE", "README.md", "Scripts/main.lua", "Scripts/places.lua", "settings.txt", "UE4SS-LICENSE.txt") | ForEach-Object { "ue4ss/Mods/GamesOMGTeleport/$_" }
$easyWant = @("dwmapi.dll", "ue4ss/LICENSE", "ue4ss/Mods/mods.txt", "ue4ss/UE4SS-settings.ini", "ue4ss/UE4SS.dll", "ue4ss/UE4SS_SDK_Backends/UE4SS.json", "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua") + $modList | Sort-Object
$easyGot = Listing $ue
if (Compare-Object $easyWant $easyGot) { Compare-Object $easyWant $easyGot | Format-Table | Out-String | Write-Host; throw "Easy zip file list is not the expected one" }
$moGot = Listing (Join-Path $stage "modonly")
if (Compare-Object ($modList | ForEach-Object { "Augusta/Binaries/Win64/$_" } | Sort-Object) $moGot) { throw "Mod-only zip file list is not the expected one" }
foreach ($f in $easyGot) { if ($f -match 'dev\.lua|survey|verify_targets|gomg_|\.log$') { throw "dev file in the zip: $f" } }

$easy = Join-Path $out "Grounded2-GamesOMG-Teleport-$v-Easy-Install.zip"
$only = Join-Path $out "Grounded2-GamesOMG-Teleport-$v-Mod-Only.zip"
Compress-Archive -Path (Join-Path $ue "*") -DestinationPath $easy
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $stage "modonly"), $only, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage, $tmp -Recurse -Force
$sums = foreach ($z in $easy, $only) { "{0}  {1}" -f (Get-FileHash $z -Algorithm SHA256).Hash.ToLower(), (Split-Path $z -Leaf) }
[IO.File]::WriteAllText((Join-Path $out "SHA256SUMS.txt"), ($sums -join "`n") + "`n", (New-Object Text.ASCIIEncoding))
$sums | ForEach-Object { Write-Host $_ }
Get-ChildItem $out -File | ForEach-Object { Write-Host ("{0,10:N0}  {1}" -f $_.Length, $_.Name) }
