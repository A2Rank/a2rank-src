# Builds the installer, the portable zip and the update packages with Velopack.
#   build\release.ps1                  version from src\Aion2Meter.App\Aion2Meter.App.csproj
#   build\release.ps1 -Version 0.2.0   explicit version (must be higher than the last release to be offered as an update)
# Output: build\releases\  (A2Rank-win-Setup.exe, A2Rank-win-Portable.zip, *.nupkg, releases.win.json)
# Needs: dotnet SDK 8 and the vpk tool (dotnet tool install -g vpk).
param([string]$Version, [string]$OutDir, [switch]$Obfuscate)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Aion2Meter.App\Aion2Meter.App.csproj'
if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$publish = Join-Path $PSScriptRoot 'publish'
$out = if ($OutDir) { $OutDir } else { Join-Path $PSScriptRoot 'releases' }
Remove-Item $publish -Recurse -Force -ErrorAction SilentlyContinue

# Release notes of this version = its section in docs\CHANGELOG.md. They travel in the signed update list, so the app can show
# what changes before the update is applied. A release without a changelog entry is refused.
$notes = Join-Path $PSScriptRoot 'release-notes.md'
$section = [System.Collections.Generic.List[string]]::new()
$inSection = $false
foreach ($line in Get-Content (Join-Path $root 'docs\CHANGELOG.md') -Encoding UTF8) {
    if ($line -match '^##\s') { $inSection = (($line -replace '^##\s+', '') -replace '\s*\(.*$', '') -eq $Version; continue }
    if ($inSection) { $section.Add($line) }
}
if (-not ($section | Where-Object { $_ -match '^\s*- ' })) { throw "docs\CHANGELOG.md has no entry for version $Version - add one before releasing" }
Set-Content $notes $section -Encoding UTF8

dotnet publish $project -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'publish failed' }

# Obfuscation is OFF by default (readable build, decided 2026-11-21). With -Obfuscate: obfuscate the upload code only (build\obfuscar.xml: DataUploader and WindowUploader in A2Rank.dll: names and strings hidden); the rest of the program and the
# Protocol, Capture and Core assemblies stay readable on purpose. Pass -Obfuscate to turn it on.
if ($Obfuscate) {
    $obfIn = Join-Path $PSScriptRoot 'obfuscate-in'
    $obfOut = Join-Path $PSScriptRoot 'obfuscate-out'
    Remove-Item $obfIn, $obfOut -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item $publish $obfIn -Recurse
    $cfg = (Get-Content (Join-Path $PSScriptRoot 'obfuscar.xml') -Raw -Encoding UTF8).Replace('$(ObfuscarIn)', $obfIn).Replace('$(ObfuscarOut)', $obfOut)
    $cfgFile = Join-Path $PSScriptRoot 'obfuscar.run.xml'
    [IO.File]::WriteAllText($cfgFile, $cfg, (New-Object Text.UTF8Encoding($false)))
    obfuscar.console $cfgFile
    if ($LASTEXITCODE) { throw 'obfuscation failed' }
    # the obfuscated A2Rank.dll replaces the published one; the mapping file (old name -> new name) stays out of the packages
    foreach ($dll in 'A2Rank.dll') { Copy-Item (Join-Path $obfOut $dll) (Join-Path $publish $dll) -Force }
    Copy-Item (Join-Path $obfOut 'Mapping.txt') (Join-Path $PSScriptRoot 'obfuscar-mapping.txt') -Force
    Remove-Item $obfIn, $obfOut, $cfgFile -Recurse -Force -ErrorAction SilentlyContinue
}

# Keep earlier packages in $out: vpk builds small delta updates from them.
# A virus scanner can briefly lock the freshly built Setup.exe at the end, so retry a few times.
$icon = Join-Path $root 'src\Aion2Meter.App\app.ico'
foreach ($attempt in 1..4) {
    vpk pack --packId A2Rank --packVersion $Version --packDir $publish --mainExe A2Rank.exe --runtime win-x64 --icon $icon `
        --packTitle 'A2Rank' --packAuthors 'A2Rank' --releaseNotes $notes --outputDir $out
    if ($LASTEXITCODE -eq 0) { break }
    if ($attempt -eq 4) { throw 'vpk pack failed' }
    Write-Host "vpk pack failed (attempt $attempt), retrying..."
    Start-Sleep 5
}

# Sign the update list: installed apps only accept a releases.win.json whose releases.win.json.sig verifies with our key.
# The private key is %USERPROFILE%\.aion2meter\update-signing.key (create it once with: dotnet run --project tools\SignFeed -- gen-key).
dotnet run --project (Join-Path $root 'tools\SignFeed') -c Release -- sign (Join-Path $out 'releases.win.json')
if ($LASTEXITCODE) { throw 'signing the update list failed' }
Write-Host "`nDone: version $Version -> $out"
