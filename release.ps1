# Publishes a new ProgramTabs version as a GitHub Release; installed copies pick it up automatically.
#   .\release.ps1 -Version 0.3.0 -Notes "Wat er nieuw is"
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = ""
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versie moet de vorm X.Y.Z hebben" }
if (git status --porcelain -- src build.cmd) { Write-Host "Let op: er zijn niet-gecommitte wijzigingen; die gaan mee in deze release." }

# 1. version number in the exe
$prog = "src\Program.cs"
$text = [IO.File]::ReadAllText($prog)
$text = $text -replace 'AssemblyVersion\("[\d.]+"\)', "AssemblyVersion(""$Version.0"")"
$text = $text -replace 'AssemblyFileVersion\("[\d.]+"\)', "AssemblyFileVersion(""$Version.0"")"
[IO.File]::WriteAllText($prog, $text, (New-Object Text.UTF8Encoding $false))

# 2. build + checksum
cmd /c build.cmd
if ($LASTEXITCODE -ne 0) { throw "Build mislukt" }
$hash = (Get-FileHash bin\ProgramTabs.exe -Algorithm SHA256).Hash.ToLower()
[IO.File]::WriteAllText("bin\ProgramTabs.exe.sha256", "$hash  ProgramTabs.exe`n")

# 3. commit, tag, push
git add -A
git commit -m "Release v$Version"
git tag "v$Version"
git push origin HEAD
git push origin "v$Version"

# 4. GitHub release with the exe and its checksum
if ($Notes -eq "") { $Notes = "ProgramTabs $Version" }
gh release create "v$Version" bin\ProgramTabs.exe bin\ProgramTabs.exe.sha256 --title "ProgramTabs $Version" --notes $Notes
Write-Host "Release v$Version gepubliceerd."
