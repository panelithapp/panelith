<#
.SYNOPSIS
    Builds, signs and publishes a Panelith release on GitHub, including the
    signed update feed that installed copies check.

.DESCRIPTION
    1. Checks that main is clean and pushed, and that the version is new.
    2. Runs every test.
    3. Runs build\publish.ps1: signed binaries and a signed installer.
    4. Writes appcast.xml and appcast.xml.signature for the installer with
       NetSparkle's generator, signed with the Ed25519 update key.
    5. Tags the commit and publishes the GitHub release with the installer,
       the appcast, SHA256SUMS.txt and the public signing certificate.

    Installed copies read
    https://github.com/panelithapp/panelith/releases/latest/download/appcast.xml,
    so publishing a release is all it takes for them to be offered the update.

    Needs, outside the repository:
        PANELITH_SIGN_THUMBPRINT   (or PANELITH_SIGN_PFX) the Authenticode certificate; see sign-file.ps1.
        PANELITH_UPDATE_KEYS       folder with NetSparkle_Ed25519.priv and .pub
                                   (default: %LOCALAPPDATA%\Panelith Release\update-keys).
        GitHub access              gh signed in, GH_TOKEN, or Git Credential Manager's github.com sign-in.

.EXAMPLE
    ./build/release.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $Draft,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$repository = 'panelithapp/panelith'
$releaseDir = Join-Path $root 'artifacts\release'
$keys = if ($env:PANELITH_UPDATE_KEYS) { $env:PANELITH_UPDATE_KEYS } else { Join-Path $env:LOCALAPPDATA 'Panelith Release\update-keys' }

function Invoke-Native([string] $what, [scriptblock] $command) {
    # git and gh report progress on stderr, which PowerShell would otherwise turn
    # into a terminating error on a perfectly successful command. The exit code is
    # the only thing here that says whether the command actually worked.
    & { $ErrorActionPreference = 'Continue'; & $command }
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

if (-not $Version) {
    [xml] $props = Get-Content (Join-Path $root 'Directory.Build.props')
    $Version = $props.SelectSingleNode('//VersionPrefix').InnerText
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, not '$Version'." }
$tag = "v$Version"

# --- Preconditions -----------------------------------------------------------

Push-Location $root
try {
    if (git status --porcelain) { throw 'The working tree has uncommitted changes. Commit or stash them first.' }
    $branch = git rev-parse --abbrev-ref HEAD
    if ($branch -ne 'main') { throw "Releases are made from main, not '$branch'." }
    Invoke-Native 'git fetch' { git fetch --quiet origin main --tags }
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { throw 'main is not the same as origin/main. Push or pull first.' }
    if (git tag --list $tag) { throw "Tag $tag already exists. Bump the version in Directory.Build.props." }
}
finally {
    Pop-Location
}

if (-not ($env:PANELITH_SIGN_THUMBPRINT -or $env:PANELITH_SIGN_PFX)) {
    throw 'Releases must be signed. Set PANELITH_SIGN_THUMBPRINT (or PANELITH_SIGN_PFX); see build\sign-file.ps1.'
}
if (-not (Test-Path (Join-Path $keys 'NetSparkle_Ed25519.priv'))) {
    throw "The update signing key was not found in $keys. Without it installed copies cannot verify the update."
}

$changelog = Get-Content (Join-Path $root 'CHANGELOG.md') -Raw
$section = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($Version))\][^\n]*\n(.*?)(?=^## \[|\z)")
if (-not $section.Success -or -not $section.Groups[1].Value.Trim()) {
    throw "CHANGELOG.md has no section for $Version. Add '## [$Version] - <date>' with the changes."
}

$gh = Get-Command gh -ErrorAction SilentlyContinue | ForEach-Object Source
if (-not $gh) {
    $gh = Join-Path $env:LOCALAPPDATA 'Panelith Release\gh\bin\gh.exe'
    if (-not (Test-Path $gh)) { throw 'The GitHub CLI was not found. Install it from https://cli.github.com.' }
}
if (-not $env:GH_TOKEN) {
    # Windows PowerShell turns redirected stderr into terminating errors under 'Stop'.
    & { $ErrorActionPreference = 'Continue'; & $gh auth status --hostname github.com 2>&1 | Out-Null }
    if ($LASTEXITCODE -ne 0) {
        # Fall back to the sign-in git itself uses for pushes. The request goes
        # through a file because Windows PowerShell prefixes piped stdin with a
        # byte-order mark, which git rejects.
        $request = [IO.Path]::GetTempFileName()
        try {
            [IO.File]::WriteAllText($request, "protocol=https`nhost=github.com`n`n", [Text.Encoding]::ASCII)
            $credential = & { $ErrorActionPreference = 'Continue'; cmd /c "git credential fill < `"$request`" 2>nul" }
        }
        finally {
            Remove-Item $request -ErrorAction SilentlyContinue
        }
        $token = ($credential | Where-Object { $_ -like 'password=*' } | Select-Object -First 1) -replace '^password=', ''
        if (-not $token) { throw 'Not signed in to GitHub. Run: gh auth login' }
        $env:GH_TOKEN = $token
    }
}

# --- Build -------------------------------------------------------------------

if (-not $SkipTests) {
    Write-Host 'Running tests'
    Invoke-Native 'dotnet test' { dotnet test (Join-Path $root 'Panelith.slnx') --configuration Release }
}

& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version

$setupName = "Panelith-$Version-setup.exe"
$setup = Join-Path $root "artifacts\installer\$setupName"
if (-not (Test-Path $setup)) { throw "$setupName was not built." }
$signature = Get-AuthenticodeSignature $setup
if (-not $signature.SignerCertificate) { throw "$setupName is not signed." }

if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null
Copy-Item $setup $releaseDir

# --- Update feed -------------------------------------------------------------

Write-Host 'Writing the signed update feed'
Push-Location $root
try {
    Invoke-Native 'netsparkle-generate-appcast' {
        dotnet tool restore | Out-Null
        dotnet netsparkle-generate-appcast `
            --single-file (Join-Path $releaseDir $setupName) `
            --file-version $Version `
            --appcast-output-directory $releaseDir `
            --base-url "https://github.com/$repository/releases/download/$tag" `
            --product-name 'Panelith' `
            --os windows `
            --link-tag "https://github.com/$repository/releases/latest/download/appcast.xml" `
            --description-tag "Panelith $Version" `
            --key-path $keys `
            --human-readable
    }
    Invoke-Native 'appcast signature check' {
        dotnet netsparkle-generate-appcast --verify (Join-Path $releaseDir 'appcast.xml') `
            --signature (Get-Content (Join-Path $releaseDir 'appcast.xml.signature') -Raw).Trim() --key-path $keys
    }
}
finally {
    Pop-Location
}

$signature.SignerCertificate | Export-Certificate -FilePath (Join-Path $releaseDir 'Panelith-code-signing.cer') -Type CERT | Out-Null

$sums = Get-ChildItem $releaseDir -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines((Join-Path $releaseDir 'SHA256SUMS.txt'), [string[]] $sums)

$thumbprint = $signature.SignerCertificate.Thumbprint
$notes = @"
$($section.Groups[1].Value.Trim())

## Install

Download **$setupName** below and run it. Installed copies of Panelith 0.1.0 and later update themselves from now on.

The installer is signed by **$($signature.SignerCertificate.GetNameInfo('SimpleName', $false))** (certificate thumbprint ``$thumbprint``). Until Panelith has a certificate from a public authority, Windows SmartScreen may say the publisher is unknown: choose **More info**, check the publisher, then **Run anyway**. See [Installing](https://github.com/$repository/blob/main/docs/installing.md) for details and for checking the download against ``SHA256SUMS.txt``.
"@
$notesFile = Join-Path $releaseDir 'notes.md'
[IO.File]::WriteAllText($notesFile, $notes)

# --- Publish -----------------------------------------------------------------

Write-Host "Tagging $tag and publishing the release"
Push-Location $root
try {
    Invoke-Native 'git tag' { git tag -a $tag -m "Panelith $Version" }
    Invoke-Native 'git push tag' { git push origin $tag }
}
finally {
    Pop-Location
}

$assets = Get-ChildItem $releaseDir -File | Where-Object Name -ne 'notes.md' | ForEach-Object FullName
$ghArgs = @('release', 'create', $tag) + $assets + @('--repo', $repository, '--title', "Panelith $Version", '--notes-file', $notesFile, '--verify-tag')
if ($Draft) { $ghArgs += '--draft' }
Invoke-Native 'gh release create' { & $gh @ghArgs }

Write-Host "Published https://github.com/$repository/releases/tag/$tag"
