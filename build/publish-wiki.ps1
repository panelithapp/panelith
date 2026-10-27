#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes docs/ to the GitHub wiki.

.DESCRIPTION
    The docs folder is the source of truth; the wiki is a generated copy. Each
    page is renamed after its heading, links between pages are rewritten to wiki
    page names, and links to repository files and images point at main.

    GitHub only creates the wiki repository once its first page has been saved
    on the website, so do that once before running this script.

.PARAMETER Remote
    The wiki's git URL.

.PARAMETER DryRun
    Write the pages to a folder and print it instead of pushing.
#>
[CmdletBinding()]
param(
    [string] $Remote = 'https://github.com/panelithapp/panelith.wiki.git',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $root 'docs'
$blob = 'https://github.com/panelithapp/panelith/blob/main'
$raw = 'https://raw.githubusercontent.com/panelithapp/panelith/main'

$work = Join-Path ([IO.Path]::GetTempPath()) "panelith-wiki-$([guid]::NewGuid().ToString('N'))"
if ($DryRun) {
    New-Item -ItemType Directory -Path $work | Out-Null
} else {
    & git clone --quiet $Remote $work
    if ($LASTEXITCODE -ne 0) {
        throw "Could not clone $Remote. If the wiki is new, save a first page at https://github.com/panelithapp/panelith/wiki first."
    }
    Get-ChildItem $work -Filter *.md | Remove-Item
}

function Get-PageName([IO.FileInfo] $file) {
    if ($file.Name -eq 'README.md') { return 'Home' }
    $title = ((Get-Content $file.FullName -TotalCount 1 -Encoding UTF8) -replace '^#\s*', '').Trim()
    return ($title -replace '[^\w\s-]', '' -replace '\s+', '-')
}

$files = Get-ChildItem $docs -Filter *.md
$pages = @{}
foreach ($file in $files) { $pages[$file.Name] = Get-PageName $file }

$utf8 = New-Object Text.UTF8Encoding $false
foreach ($file in $files) {
    $text = [IO.File]::ReadAllText($file.FullName, $utf8)
    $text = [regex]::Replace($text, '\]\(([^)\s#]*)(#[^)\s]*)?\)', {
        param($m)
        $target = $m.Groups[1].Value
        $anchor = $m.Groups[2].Value
        if ($target -match '^(https?|mailto):' -or $target -eq '') { return $m.Value }
        $leaf = Split-Path $target -Leaf
        if ($target -notmatch '/' -and $pages.ContainsKey($leaf)) { return "]($($pages[$leaf])$anchor)" }
        $full = [IO.Path]::GetFullPath((Join-Path $docs $target))
        $relative = $full.Substring($root.Length + 1).Replace('\', '/')
        if ($relative -match '\.(png|jpe?g|gif|webp)$') { return "]($raw/$relative)" }
        return "]($blob/$relative$anchor)"
    })
    if ($file.Name -eq 'README.md') {
        $text = $text -replace 'The same pages are published on the \[wiki\]\([^)]*\)\.', "This wiki is generated from the [docs folder]($blob/docs) of the repository."
    }
    [IO.File]::WriteAllText((Join-Path $work "$($pages[$file.Name]).md"), $text, $utf8)
}

$sidebar = New-Object Text.StringBuilder
[void] $sidebar.AppendLine('**[Home](Home)**')
$section = $null
foreach ($line in [IO.File]::ReadAllLines((Join-Path $docs 'README.md'), $utf8)) {
    if ($line -match '^##\s+(.+)$') { [void] $sidebar.AppendLine("`n**$($Matches[1])**`n") }
    elseif ($line -match '^\|\s*\[([^\]]+)\]\(([^)]+)\)') { [void] $sidebar.AppendLine("- [$($Matches[1])]($($pages[$Matches[2]]))") }
}
[IO.File]::WriteAllText((Join-Path $work '_Sidebar.md'), $sidebar.ToString(), $utf8)
[IO.File]::WriteAllText((Join-Path $work '_Footer.md'),
    "Panelith is licensed under the [PolyForm Noncommercial License 1.0.0]($blob/LICENSE). Edit these pages in [docs/]($blob/docs); the wiki is regenerated from there.`n", $utf8)

if ($DryRun) {
    Write-Host "Wiki pages written to $work"
    return
}

Push-Location $work
try {
    # git reports line ending changes and progress on stderr, which would
    # otherwise be a terminating error here even though nothing went wrong.
    $ErrorActionPreference = 'Continue'
    & git add -A
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 0) { Write-Host 'The wiki is already up to date.'; return }
    $commit = (& git -C $root rev-parse --short HEAD).Trim()
    & git commit --quiet -m "Update from docs at $commit"
    & git push --quiet origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Pushing the wiki failed.' }
    Write-Host 'Wiki updated: https://github.com/panelithapp/panelith/wiki'
} finally {
    Pop-Location
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
