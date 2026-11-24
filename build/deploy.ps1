# Puts build\releases on GitHub, where the apps look for updates, and checks that what is served matches the local files.
#   build\deploy.ps1
# Everything is done with the token of the account "arank8934-bot" (gh auth login as that account once, in a private browser window; the active
# gh account stays the personal one), so that the releases show only that account. The update list is trimmed to the newest versions and
# signed again; the packages of older versions are removed from the release.
param([string]$OutDir, [string]$Repo = 'A2Rank/releases', [string]$BotAccount = 'arank8934-bot', [int]$Keep = 3)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$releases = if ($OutDir) { $OutDir } else { Join-Path $PSScriptRoot 'releases' }
$setup = 'A2Rank-win-Setup.exe'
if (-not (Test-Path (Join-Path $releases 'releases.win.json'))) { throw 'releases.win.json is missing - run build\release.ps1 first' }
Add-Type -AssemblyName System.Net.Http
# gh writes progress and "not found" to stderr: with ErrorActionPreference Stop, Windows PowerShell 5.1 would turn that into an error, so gh runs with Continue and the exit code is what counts.
function Invoke-Gh { $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; try { & gh @args 2>&1 | ForEach-Object { "$_" } } finally { $ErrorActionPreference = $old } }
function Sha256($bytes) { [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace('-', '') }

$token = (Invoke-Gh auth token --user $BotAccount) | Select-Object -First 1
if (-not $token) { throw "no gh login for $BotAccount - run: gh auth login --hostname github.com --web (in a private browser window, as that account)" }
$env:GH_TOKEN = $token
try {
    # The update list of the newest $Keep versions only; an app further behind gets the newest full package.
    $feed = Get-Content (Join-Path $releases 'releases.win.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $versions = $feed.Assets | ForEach-Object { [version]$_.Version } | Sort-Object -Unique -Descending | Select-Object -First $Keep
    $kept = @($feed.Assets | Where-Object { $versions -contains [version]$_.Version })
    $stage = Join-Path $PSScriptRoot 'github-stage'
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $stage | Out-Null
    $json = (@{ Assets = $kept } | ConvertTo-Json -Depth 6 -Compress)
    [IO.File]::WriteAllText((Join-Path $stage 'releases.win.json'), $json, (New-Object Text.UTF8Encoding($false)))
    dotnet run --project (Join-Path $root 'tools\SignFeed') -c Release -- sign (Join-Path $stage 'releases.win.json')
    if ($LASTEXITCODE) { throw 'signing the trimmed update list failed' }

    $packages = @($kept | ForEach-Object { $_.FileName }) + $setup
    foreach ($p in $packages) { if (-not (Test-Path (Join-Path $releases $p))) { throw "$p is missing in $releases (the kept versions need their packages there)" } }

    Invoke-Gh release view update --repo $Repo | Out-Null
    if ($LASTEXITCODE) {
        Invoke-Gh release create update --repo $Repo --title update --notes 'Update files.' --latest
        if ($LASTEXITCODE) { throw 'creating the "update" release failed' }
    }
    # Packages first, then the list and its signature, so nobody sees a list that points to a missing file.
    Invoke-Gh release upload update --repo $Repo --clobber ($packages | ForEach-Object { Join-Path $releases $_ })
    if ($LASTEXITCODE) { throw 'uploading the packages failed' }
    Invoke-Gh release upload update --repo $Repo --clobber (Join-Path $stage 'releases.win.json') (Join-Path $stage 'releases.win.json.sig')
    if ($LASTEXITCODE) { throw 'uploading the update list failed' }
    # Drop what is no longer wanted (packages of older versions).
    $want = $packages + 'releases.win.json' + 'releases.win.json.sig'
    (Invoke-Gh release view update --repo $Repo --json assets | ConvertFrom-Json).assets | Where-Object { $want -notcontains $_.name } | ForEach-Object {
        Invoke-Gh release delete-asset update $_.name --repo $Repo --yes
        "removed old asset $($_.name)"
    }

    # Download what the public address serves (no login) and compare with the local files.
    $http = New-Object System.Net.Http.HttpClient
    $http.Timeout = [TimeSpan]::FromMinutes(10)
    $base = "https://github.com/$Repo/releases/download/update/"
    # What GitHub serves can lag behind an upload for a minute or two (its download cache): ask again for up to two minutes before saying a file differs.
    $bad = 0
    for ($try = 1; $try -le 7; $try++) {
        $bad = 0
        $lines = @()
        foreach ($c in @(@{ Name = 'releases.win.json'; Path = Join-Path $stage 'releases.win.json' }, @{ Name = 'releases.win.json.sig'; Path = Join-Path $stage 'releases.win.json.sig' },
                         @{ Name = $setup; Path = Join-Path $releases $setup })) {
            try { $bytes = $http.GetByteArrayAsync($base + $c.Name).GetAwaiter().GetResult() } catch { $bytes = [byte[]]@() }   # a file that is being replaced answers 404 for a moment
            if ((Get-FileHash $c.Path -Algorithm SHA256).Hash -eq (Sha256 $bytes)) { $lines += "OK   $($c.Name)" } else { $lines += "DIFF $($c.Name)"; $bad++ }
        }
        if ($bad -eq 0 -or $try -eq 7) { $lines; break }
        "    (attempt ${try}: $bad file(s) not served yet, asking again in 20 s)"
        Start-Sleep -Seconds 20
    }
    if ($bad) { throw "$bad file(s) differ on GitHub" }
    Write-Host "`nDeployed to $base"
}
finally { Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue }
