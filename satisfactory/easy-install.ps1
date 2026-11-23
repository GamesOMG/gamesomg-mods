<#
GamesOMG Teleport for Satisfactory: the Easy-Install zip. One download that holds SML and the mod, laid out as the game
folder: unzip it into the folder that holds FactoryGame and both land in FactoryGame\Mods.

  powershell -File satisfactory\easy-install.ps1                      → release-out/<version>/…-Easy-Install.zip
  powershell -File satisfactory\easy-install.ps1 -Version 1.0.1 -OurSha <sha256 of the -Windows.zip on the release>

It builds from what is already published, never from a local build: our -Windows.zip is fetched from the release and must
match the SHA-256 the release lists, and SML-Windows.zip is fetched from SML's own release and must match the pinned
digest. Every file of both is copied byte for byte with its own timestamp, and the zip is read back and held to that.

Out, in release-out/<version>/:
  Satisfactory-GamesOMG-Teleport-<v>-Easy-Install.zip
    FactoryGame/Mods/SML/…               SML-Windows.zip <SmlVersion>, unmodified
    FactoryGame/Mods/GamesOMGTeleport/…  our -Windows.zip, unmodified
    GamesOMG-Teleport-README.txt         install steps, what is inside, SML's source (GPL-3.0 §6) — easy-install/README.txt
    SML-LICENSE.txt                      SML's LICENSE.txt at its tag, byte for byte
  LISTING.txt (every file, size, SHA-256 prefix) and the zip's SHA-256 on the last line.
Add the zip to the release with `gh release upload`, and its line to the release's SHA256SUMS.txt.
#>
param(
    [string]$Version = '1.0.0',
    [string]$OurSha = '124b261ce26487f0096b6be0bf90f855806702d707cf51d544dbd87d1f5047dc',
    [string]$Repo = 'GamesOMG/gamesomg-mods'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# SML, pinned to its release digest, the tag's commit and its licence file's digest
$SmlVersion = '3.12.1'
$SmlSha = '5b7d6c60396f2012bfcb71cf8af093132958929f73fba0004d962c753d486359'
$SmlCommit = '0170e5feece4bebcb4baa2ec4806587300160285'
$SmlLicenceSha = '8b1ba204bb69a0ade2bfcf65ef294a920f6bb361b317dba43c7ef29d96332b9b'
$SmlUrl = "https://github.com/satisfactorymodding/SatisfactoryModLoader/releases/download/v$SmlVersion/SML-Windows.zip"
$SmlLicenceUrl = "https://raw.githubusercontent.com/satisfactorymodding/SatisfactoryModLoader/v$SmlVersion/LICENSE.txt"

$root = $PSScriptRoot
$Tag = "satisfactory-teleport-v$Version"
$OurName = "Satisfactory-GamesOMG-Teleport-$Version-Windows.zip"
$OurUrl = "https://github.com/$Repo/releases/download/$Tag/$OurName"
$EasyName = "Satisfactory-GamesOMG-Teleport-$Version-Easy-Install.zip"

function Stop-Build([string]$why) { throw "EASY INSTALL STOPPED: $why" }
$sha = [Security.Cryptography.SHA256]::Create()
function Get-Hex([byte[]]$b) { return ([BitConverter]::ToString($sha.ComputeHash($b)) -replace '-', '').ToLowerInvariant() }

$deps = Join-Path $root '.deps'
New-Item -ItemType Directory -Force -Path $deps | Out-Null
function Get-Pinned([string]$url, [string]$file, [string]$want) {
    $path = Join-Path $deps $file
    if (-not (Test-Path $path)) { Invoke-WebRequest $url -OutFile $path -UseBasicParsing }
    $got = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($got -ne $want) { Remove-Item $path; Stop-Build "$file is $got, not the pinned $want ($url)" }
    return $path
}

# every file of a zip, in its order, with its bytes and its own timestamp
function Read-Zip([string]$path) {
    $files = [ordered]@{}
    $z = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        foreach ($e in $z.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            if ($e.FullName -match '\\|^/|\.\.') { Stop-Build "$path holds an odd entry: $($e.FullName)" }
            $ms = New-Object IO.MemoryStream; $s = $e.Open()
            try { $s.CopyTo($ms) } finally { $s.Dispose() }
            $files[$e.FullName] = [pscustomobject]@{ Bytes = $ms.ToArray(); Time = $e.LastWriteTime }
        }
    } finally { $z.Dispose() }
    return $files
}

$smlZip = Get-Pinned $SmlUrl "SML-Windows-$SmlVersion.zip" $SmlSha
$ourZip = Get-Pinned $OurUrl $OurName $OurSha
$licence = [IO.File]::ReadAllBytes((Get-Pinned $SmlLicenceUrl "SML-$SmlVersion-LICENSE.txt" $SmlLicenceSha))
$sml = Read-Zip $smlZip
$ours = Read-Zip $ourZip

# both plugins hold their .uplugin at the zip root, and neither is a GameFeature (those go in Mods/GameFeatures)
foreach ($p in @(@{ Name = 'SML'; Files = $sml }, @{ Name = 'GamesOMGTeleport'; Files = $ours })) {
    $up = "$($p.Name).uplugin"
    if (-not $p.Files.Contains($up)) { Stop-Build "$up is not at the root of its zip" }
    $json = [Text.Encoding]::UTF8.GetString($p.Files[$up].Bytes) | ConvertFrom-Json
    if ($json.GameFeature) { Stop-Build "$up says GameFeature: it would belong in Mods/GameFeatures" }
    if ($p.Name -eq 'SML' -and $json.SemVersion -ne $SmlVersion) { Stop-Build "SML.uplugin says $($json.SemVersion), not $SmlVersion" }
    if ($p.Name -eq 'GamesOMGTeleport' -and $json.SemVersion -ne $Version) { Stop-Build "GamesOMGTeleport.uplugin says $($json.SemVersion), not $Version" }
}

# the README: the template with this release's numbers, CRLF for Notepad, ASCII only
$readme = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'easy-install/README.txt')
$readme = $readme.Replace('{VERSION}', $Version).Replace('{SML_VERSION}', $SmlVersion).Replace('{SML_SHA256}', $SmlSha).Replace('{SML_COMMIT}', $SmlCommit)
if ($readme -match '\{[A-Z_]+\}') { Stop-Build "README.txt has a placeholder left: $($Matches[0])" }
if ($readme -match '[^\x09\x0A\x0D\x20-\x7E]') { Stop-Build 'README.txt has a non-ASCII character' }
$readme = ($readme -replace "`r`n", "`n") -replace "`n", "`r`n"
$readmeBytes = [Text.Encoding]::ASCII.GetBytes($readme)

# our two text files carry the time of the last commit that touched the README template
$epoch = [long]((& git -C $root log -1 --format=%ct -- easy-install/README.txt) | Out-String).Trim()
if (-not $epoch) { Stop-Build 'easy-install/README.txt is not committed: commit it first, so the zip is repeatable' }
$when = [DateTimeOffset]::FromUnixTimeSeconds($epoch - ($epoch % 2))   # a zip keeps time in 2-second steps
$stamp = New-Object DateTimeOffset ($when.UtcDateTime.Ticks, [TimeSpan]::Zero)

$entries = [ordered]@{}
$entries['GamesOMG-Teleport-README.txt'] = [pscustomobject]@{ Bytes = $readmeBytes; Time = $stamp }
$entries['SML-LICENSE.txt'] = [pscustomobject]@{ Bytes = $licence; Time = $stamp }
foreach ($k in $sml.Keys) { $entries["FactoryGame/Mods/SML/$k"] = $sml[$k] }
foreach ($k in $ours.Keys) { $entries["FactoryGame/Mods/GamesOMGTeleport/$k"] = $ours[$k] }

$out = Join-Path $root "release-out/$Version"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$path = Join-Path $out $EasyName
if (Test-Path $path) { Remove-Item $path }
$fs = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
$zip = New-Object IO.Compression.ZipArchive ($fs, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $entries.Keys) {
        $item = $entries[$name]
        $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $item.Time
        $s = $entry.Open()
        try { $s.Write($item.Bytes, 0, $item.Bytes.Length) } finally { $s.Dispose() }
    }
} finally { $zip.Dispose(); $fs.Dispose() }

# read it back: the exact list, every byte, every timestamp of SML's and ours
$back = Read-Zip $path
$got = @($back.Keys); $want = @($entries.Keys)
$extra = @($got | Where-Object { $want -notcontains $_ }); $missing = @($want | Where-Object { $got -notcontains $_ })
if ($extra.Count -or $missing.Count) { Stop-Build "the zip holds the wrong files. extra: $($extra -join ', ') missing: $($missing -join ', ')" }
$listing = New-Object Collections.Generic.List[string]
foreach ($name in $want) {
    $a = $back[$name]; $b = $entries[$name]
    $ha = Get-Hex $a.Bytes
    if ($ha -ne (Get-Hex $b.Bytes)) { Stop-Build "$name did not read back as written" }
    if ($a.Time.DateTime -ne $b.Time.DateTime) { Stop-Build "$name came back stamped $($a.Time), not $($b.Time)" }
    $listing.Add(('{0,10}  {1}  {2}' -f $a.Bytes.Length, $ha.Substring(0, 12), $name))
}
$smlCount = @($want | Where-Object { $_ -like 'FactoryGame/Mods/SML/*' }).Count
$ourCount = @($want | Where-Object { $_ -like 'FactoryGame/Mods/GamesOMGTeleport/*' }).Count
if ($smlCount -ne $sml.Count -or $ourCount -ne $ours.Count) { Stop-Build 'a plugin folder lost or gained a file' }

$zipSha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $path).Length
$listing.Add('')
$listing.Add("$zipSha  $EasyName")
[IO.File]::WriteAllText((Join-Path $out 'LISTING.txt'), ($listing -join "`n") + "`n")
Write-Output ($listing -join "`n")
Write-Output ''
Write-Output ("{0}  {1:n0} bytes. SML {2} ({3} files) and our {4} ({5} files) byte-identical to their release zips, with their own timestamps." -f $EasyName, $size, $SmlVersion, $smlCount, $OurName, $ourCount)
Write-Output "Upload: gh release upload $Tag `"$path`" --repo $Repo"
