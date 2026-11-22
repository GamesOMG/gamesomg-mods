<#
GamesOMG Item Spawner for The Planet Crafter: the release packager (GamesOMG Teleport's, with this mod's names and checks). One script, three zips, each checked against an exact list.

  ./release.ps1 -GameManaged '<game>/Planet Crafter_Data/Managed' -Tag planet-crafter-item-spawner-v<version>
  ./release.ps1 -GameManaged '<game>/Planet Crafter_Data/Managed' -DryRun

A real run builds only from a tagged commit: the tag must be planet-crafter-item-spawner-v<the csproj's Version>, it must point at
HEAD, and this folder must have no changes. -DryRun makes the same zips from the working tree and says so.

Out, in release-out/<version>/:
  PlanetCrafter-GamesOMG-Item-Spawner-<v>-Easy-Install.zip  BepInEx 5.4.23.5 exactly as released + our DLL + every licence
  PlanetCrafter-GamesOMG-Item-Spawner-<v>-Plugin-Only.zip   our DLL, for a game that already runs BepInEx 5
  PlanetCrafter-GamesOMG-Item-Spawner-<v>-Thunderstore.zip  manifest.json, README.md, icon.png, CHANGELOG.md, LICENSE, the DLL
  SHA256SUMS.txt, release.json (version, date, size, SHA-256, download URL of each), LISTING.txt (every file in every zip)
The DLL embeds no font (the panel uses the game's own), so no font licence ships. The run still stops if the DLL embeds a
font with no licence listed in $FontLicences.

The game's DLLs are compile references only and never enter a zip. The DLL is built with no debug file, so no folder path
of the machine that built it is inside it. Every zip entry is stamped with the commit's time, so one commit gives
byte-identical zips on any machine with the same .NET SDK AND the same PowerShell: Windows PowerShell 5.1 and PowerShell 7
compress differently (same files and timestamps, different zip bytes; Codex C61 F11). release.json records which one ran.
Publish the hashes of the files actually uploaded.
#>
param(
    [Parameter(Mandatory = $true)][string]$GameManaged,
    [string]$Tag,
    [switch]$DryRun,
    [string]$Repo = 'GamesOMG/gamesomg-mods'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = $PSScriptRoot
$BepVersion = '5.4.23.5'
$BepSha = '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4'
$BepZipName = "BepInEx_win_x64_$BepVersion.zip"
$BepUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepVersion/$BepZipName"
$ThunderDependency = 'BepInEx-BepInExPack-5.4.2305'
$DllName = 'GamesOMG.PlanetCrafterSpawn.dll'
$PluginDir = 'BepInEx/plugins/GamesOMGSpawn'
$OurDocs = @('README.md', 'CHANGELOG.md', 'LICENSE')
# fonts embedded in the DLL (fonts.py), each with the licence file that must travel beside the DLL in every zip
$FontLicences = [ordered]@{}   # none
$Licences = @('THIRD-PARTY-NOTICES.txt', 'BepInEx-LICENSE.txt', 'UnityDoorstop-LICENSE.txt', 'HarmonyX-LICENSE.txt', 'MonoMod-LICENSE.txt', 'Mono.Cecil-LICENSE.txt')

function Stop-Release([string]$why) { throw "RELEASE STOPPED: $why" }

# git, with stderr kept out of PowerShell 5.1's error stream
function Invoke-Git([string[]]$GitArgs) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & git -C $root @GitArgs 2>&1 | Where-Object { $_ -is [string] } }
    finally { $ErrorActionPreference = $old }
    return [pscustomobject]@{ Code = $LASTEXITCODE; Out = (@($out) -join "`n").Trim() }
}

function Assert-Exit([string]$what) { if ($LASTEXITCODE -ne 0) { Stop-Release "$what failed (exit $LASTEXITCODE)" } }

# ── 1. version and the commit ──
$csprojText = Get-Content -Raw -Encoding UTF8 (Join-Path $root 'GamesOMG.PlanetCrafterSpawn.csproj')
$found = [regex]::Matches($csprojText, '<Version>([^<]+)</Version>')
if ($found.Count -ne 1) { Stop-Release 'the csproj must hold exactly one <Version>' }
$version = $found[0].Groups[1].Value.Trim()
$wantTag = "planet-crafter-item-spawner-v$version"
$head = (Invoke-Git @('rev-parse', 'HEAD')).Out
if ($DryRun) {
    $label = 'DRY RUN (working tree, no tag)'
    $Tag = $wantTag
} else {
    if ($Tag -ne $wantTag) { Stop-Release "the tag must be $wantTag (the csproj says $version); got '$Tag'" }
    $tagged = Invoke-Git @('rev-parse', "$Tag^{commit}")
    if ($tagged.Code -ne 0) { Stop-Release "there is no tag $Tag here" }
    if ($tagged.Out -ne $head) { Stop-Release "tag $Tag is $($tagged.Out), HEAD is ${head}; check the tag out first" }
    $dirty = (Invoke-Git @('status', '--porcelain', '--', '.')).Out
    if ($dirty) { Stop-Release "this folder has changes that are not in the tagged commit:`n$dirty" }
    $label = "tag $Tag at $($head.Substring(0, 12))"
}
$epoch = [long](Invoke-Git @('log', '-1', '--format=%ct', 'HEAD')).Out
$when = [DateTimeOffset]::FromUnixTimeSeconds($epoch)
# a zip stores local time with 2-second steps and no zone: keep the UTC wall clock so every machine writes the same bytes
$stamp = New-Object DateTimeOffset ($when.UtcDateTime.Ticks, [TimeSpan]::Zero)
$date = $when.UtcDateTime.ToString('yyyy-MM-dd')
Write-Output "GamesOMG Item Spawner $version, $label, commit date $date (UTC)"

Push-Location $root
try {
    # ── 2. the checks that need no game, then the build ──
    python tests/strings_test.py
    Assert-Exit 'the string table check'
    python tests/styles_test.py
    Assert-Exit 'the text contrast check'
    # the hooks check guards the private in-game test harness (sitting/), which is not in the public repo
    if (Test-Path (Join-Path $root 'sitting')) {
        python tests/hooks_test.py
        Assert-Exit 'the hooks check'
    }
    $manifestVersion = (Get-Content -Raw -Encoding UTF8 (Join-Path $root 'manifest.json') | ConvertFrom-Json).version_number
    if ($manifestVersion -ne $version) { Stop-Release "manifest.json says $manifestVersion, the csproj $version" }
    if (-not (Select-String -LiteralPath (Join-Path $root 'CHANGELOG.md') -Pattern "^## $([regex]::Escape($version))\b" -Quiet)) { Stop-Release "CHANGELOG.md has no '## $version' entry" }
    $out = Join-Path $root "release-out/$version"
    $build = Join-Path $root 'release-out/build/'
    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    dotnet build GamesOMG.PlanetCrafterSpawn.csproj -c Release --no-incremental "-p:GameManaged=$GameManaged" `
        -p:DebugType=none -p:Deterministic=true "-p:OutputPath=$build"
    Assert-Exit 'the build'
    $dllPath = Join-Path $build $DllName
    $fv = (Get-Item $dllPath).VersionInfo.FileVersion
    if (-not $fv.StartsWith($version)) { Stop-Release "the DLL's FileVersion is $fv, the csproj's $version" }
    # the Unity-free checks, and the built DLL's metadata: never the achievement settings, never a ground drop
    $said = dotnet run --project tests/Spawn.Tests.csproj -c Release -- $dllPath
    $said | ForEach-Object { Write-Output $_ }
    if ($LASTEXITCODE -ne 0 -or -not ($said -match '^spawn checks: \d+ passed$') -or -not ($said -match '^DLL scan:')) { Stop-Release 'the unit checks failed or did not run' }
    $dll = [IO.File]::ReadAllBytes($dllPath)

    # nothing of the build machine, and no test-only survey code, inside the DLL
    $latin = [Text.Encoding]::GetEncoding(28591).GetString($dll)
    $wide = [Text.Encoding]::Unicode.GetString($dll) + "`n" + [Text.Encoding]::Unicode.GetString($dll, 1, $dll.Length - 1)
    foreach ($text in @($latin, $wide)) {
        # a drive path (D:\… or D:/…, never the s:// of a URL), a debug file, a user folder, survey code
        $leak = [regex]::Match($text, '(?<![A-Za-z0-9])[A-Za-z]:(\\|/(?!/))[^\x00]{2,80}|[^\x00\\/]{0,60}\.pdb\b|Users[\\/]|Survey|queriesHitBackfaces')
        if ($leak.Success) { Stop-Release "the DLL carries '$($leak.Value)'" }
    }
    # every font inside the DLL is one we ship the licence for (the OFL travels with every copy of the font)
    $embedded = @([regex]::Matches($latin, '[A-Za-z0-9_-]+\.(ttf|otf|woff2?)\b') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    foreach ($font in $embedded) {
        $family = ($font -split '[-_.]')[0]
        if (-not $FontLicences.Contains($family)) { Stop-Release "the DLL embeds $font and this script ships no licence for $family" }
        $text = Get-Content -Raw -Encoding UTF8 (Join-Path $root "licenses/$($FontLicences[$family])")
        if ($text -notmatch [regex]::Escape("The $family Project Authors") -or $text -notmatch 'SIL OPEN FONT LICENSE Version 1\.1') {
            Stop-Release "licenses/$($FontLicences[$family]) is not $family's OFL 1.1 with its copyright line"
        }
    }
    Write-Output "Fonts inside the DLL: $(if ($embedded.Count) { $embedded -join ', ' } else { 'none' }); each one's licence ships beside it."

    # ── 3. the loader, pinned to its official release digest ──
    $deps = Join-Path $root '.deps'
    New-Item -ItemType Directory -Force -Path $deps | Out-Null
    $bepZip = Join-Path $deps $BepZipName
    if (-not (Test-Path $bepZip)) { Invoke-WebRequest $BepUrl -OutFile $bepZip -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $bepZip -Algorithm SHA256).Hash -ne $BepSha) { Stop-Release "$BepZipName differs from the pinned official digest" }
    $bep = [ordered]@{}
    $bepArchive = [IO.Compression.ZipFile]::OpenRead($bepZip)
    try {
        foreach ($e in $bepArchive.Entries) {
            if ($e.FullName.EndsWith('/')) { continue }
            $ms = New-Object IO.MemoryStream; $s = $e.Open()
            try { $s.CopyTo($ms) } finally { $s.Dispose() }
            $bep[$e.FullName] = [pscustomobject]@{ Bytes = $ms.ToArray(); Time = $e.LastWriteTime }
        }
    } finally { $bepArchive.Dispose() }

    # ── 4. the three zips ──
    function Read-Ours([string]$relative) { return [pscustomobject]@{ Bytes = [IO.File]::ReadAllBytes((Join-Path $root $relative)); Time = $stamp } }
    $dllEntry = [pscustomobject]@{ Bytes = $dll; Time = $stamp }
    $base = "PlanetCrafter-GamesOMG-Item-Spawner-$version"

    $easy = [ordered]@{}
    foreach ($k in $bep.Keys) { $easy[$k] = $bep[$k] }
    $easy["$PluginDir/$DllName"] = $dllEntry
    foreach ($d in $OurDocs) { $easy["$PluginDir/$d"] = Read-Ours $d }
    foreach ($f in $FontLicences.Values) { $easy["$PluginDir/$f"] = Read-Ours "licenses/$f" }
    foreach ($l in $Licences) { $easy["BepInEx/licenses/$l"] = Read-Ours "licenses/$l" }

    $plugin = [ordered]@{}
    $plugin["$PluginDir/$DllName"] = $dllEntry
    foreach ($d in $OurDocs) { $plugin["$PluginDir/$d"] = Read-Ours $d }
    foreach ($f in $FontLicences.Values) { $plugin["$PluginDir/$f"] = Read-Ours "licenses/$f" }

    $thunder = [ordered]@{}
    foreach ($d in @('manifest.json', 'README.md', 'icon.png', 'CHANGELOG.md', 'LICENSE')) { $thunder[$d] = Read-Ours $d }
    foreach ($f in $FontLicences.Values) { $thunder[$f] = Read-Ours "licenses/$f" }
    $thunder[$DllName] = $dllEntry

    $zips = [ordered]@{ "$base-Easy-Install.zip" = $easy; "$base-Plugin-Only.zip" = $plugin; "$base-Thunderstore.zip" = $thunder }
    foreach ($name in $zips.Keys) {
        $path = Join-Path $out $name
        $fs = [IO.File]::Open($path, [IO.FileMode]::CreateNew)
        $zip = New-Object IO.Compression.ZipArchive ($fs, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($entryName in $zips[$name].Keys) {
                $item = $zips[$name][$entryName]
                $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $item.Time
                $s = $entry.Open()
                try { $s.Write($item.Bytes, 0, $item.Bytes.Length) } finally { $s.Dispose() }
            }
        } finally { $zip.Dispose(); $fs.Dispose() }
    }

    # ── 5. read every zip back and hold it to its exact list ──
    $sha = [Security.Cryptography.SHA256]::Create()
    function Get-Hex([byte[]]$b) { return ([BitConverter]::ToString($sha.ComputeHash($b)) -replace '-', '').ToLowerInvariant() }
    $forbidden = '(?i)assembly-csharp|unityengine|unity\.inputsystem|netcode|\.pdb$|places\.json|\.cfg$|\.log$|survey|managed/|planet crafter_data|\.mdb$'
    $machine = '(?<![A-Za-z0-9])[A-Za-z]:(\\|/(?!/))|Users[\\/]|App[Dd]ata'
    $listing = New-Object Collections.Generic.List[string]
    $assets = @()
    foreach ($name in $zips.Keys) {
        $path = Join-Path $out $name
        $read = [IO.Compression.ZipFile]::OpenRead($path)
        try {
            $got = @($read.Entries | ForEach-Object { $_.FullName })
            $want = @($zips[$name].Keys)
            $extra = @($got | Where-Object { $want -notcontains $_ })
            $missing = @($want | Where-Object { $got -notcontains $_ })
            if ($extra.Count -or $missing.Count -or $got.Count -ne $want.Count) { Stop-Release "$name holds the wrong files. extra: $($extra -join ', ') missing: $($missing -join ', ')" }
            $listing.Add("$name")
            foreach ($e in $read.Entries) {
                if ($e.FullName -match $forbidden) { Stop-Release "$name carries a forbidden file: $($e.FullName)" }
                $ms = New-Object IO.MemoryStream; $s = $e.Open()
                try { $s.CopyTo($ms) } finally { $s.Dispose() }
                $bytes = $ms.ToArray()
                if ((Get-Hex $bytes) -ne (Get-Hex $zips[$name][$e.FullName].Bytes)) { Stop-Release "$name/$($e.FullName) did not read back as written" }
                if ($e.FullName -like "*$DllName" -and (Get-Hex $bytes) -ne (Get-Hex $dll)) { Stop-Release "$name carries a DLL that is not this build" }
                if ($bep.Contains($e.FullName) -and (Get-Hex $bytes) -ne (Get-Hex $bep[$e.FullName].Bytes)) { Stop-Release "$name changed BepInEx's $($e.FullName)" }
                if (-not $bep.Contains($e.FullName) -and $e.FullName -match '\.(md|txt|json)$|LICENSE$') {
                    $hit = [regex]::Match([Text.Encoding]::UTF8.GetString($bytes), $machine)
                    if ($hit.Success) { Stop-Release "$name/$($e.FullName) names '$($hit.Value)'" }
                }
                $listing.Add(('  {0,9}  {1}  {2}' -f $bytes.Length, (Get-Hex $bytes).Substring(0, 12), $e.FullName))
            }
            $dllAt = @($got | Where-Object { $_ -like "*$DllName" })
            foreach ($f in $FontLicences.Values) {
                foreach ($d in $dllAt) {
                    $beside = ($d.Substring(0, $d.Length - $DllName.Length)) + $f
                    if ($got -notcontains $beside) { Stop-Release "$name carries the DLL at $d with no $beside beside it" }
                }
            }
            if ($name -like '*-Thunderstore.zip') {
                $manifest = [Text.Encoding]::UTF8.GetString($zips[$name]['manifest.json'].Bytes) | ConvertFrom-Json
                if ($manifest.name -notmatch '^[A-Za-z0-9_]+$') { Stop-Release "manifest name '$($manifest.name)' has characters Thunderstore refuses" }
                if ($manifest.version_number -ne $version) { Stop-Release "manifest version_number $($manifest.version_number) is not $version" }
                if ($manifest.description.Length -gt 250) { Stop-Release "manifest description is $($manifest.description.Length) characters (Thunderstore: 250)" }
                if (@($manifest.dependencies) -join ',' -ne $ThunderDependency) { Stop-Release "manifest dependencies are '$(@($manifest.dependencies) -join ',')', not $ThunderDependency" }
                if ($manifest.website_url -notmatch '^https://') { Stop-Release 'manifest website_url is not an https link' }
                $png = $zips[$name]['icon.png'].Bytes
                $w = ([int]$png[16] -shl 24) -bor ([int]$png[17] -shl 16) -bor ([int]$png[18] -shl 8) -bor [int]$png[19]
                $h = ([int]$png[20] -shl 24) -bor ([int]$png[21] -shl 16) -bor ([int]$png[22] -shl 8) -bor [int]$png[23]
                if ($png[1] -ne 0x50 -or $w -ne 256 -or $h -ne 256) { Stop-Release "icon.png is $w x $h, not a 256 x 256 PNG" }
            }
        } finally { $read.Dispose() }
        $zipBytes = [IO.File]::ReadAllBytes($path)
        $assets += [pscustomobject]@{
            name = $name; size = $zipBytes.Length; sha256 = (Get-Hex $zipBytes)
            url = "https://github.com/$Repo/releases/download/$Tag/$name"
        }
    }

    # ── 6. the sums, the page's numbers, the listing ──
    $sums = ($assets | ForEach-Object { "$($_.sha256)  $($_.name)" }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), $sums + "`n")
    $release = [ordered]@{
        mod = 'GamesOMG Item Spawner'; game = 'The Planet Crafter'; version = $version; tag = $Tag; commit = $head; date = $date
        dryRun = [bool]$DryRun; powershell = "$($PSVersionTable.PSEdition) $($PSVersionTable.PSVersion)"
        bepinex = $BepVersion; bepinexRelease = "https://github.com/BepInEx/BepInEx/releases/tag/v$BepVersion"
        thunderstoreDependency = $ThunderDependency; assets = $assets
    }
    [IO.File]::WriteAllText((Join-Path $out 'release.json'), ($release | ConvertTo-Json -Depth 4) + "`n")
    [IO.File]::WriteAllText((Join-Path $out 'LISTING.txt'), ($listing -join "`n") + "`n")
    Write-Output ''
    Write-Output ($listing -join "`n")
    Write-Output ''
    Write-Output $sums
    # our DLL and our text files are path-scanned; BepInEx's own DLLs are shipped byte for byte and keep their upstream build paths
    Write-Output "All three zips match their exact lists; no game file; no machine path in our DLL or our text files; BepInEx $BepVersion byte-identical to its release. $label (PowerShell $($PSVersionTable.PSVersion))"
}
finally { Pop-Location }
