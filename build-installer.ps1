<#
.SYNOPSIS
  Publishes CodeSearch as a self-contained single executable and builds the Windows installer.

.DESCRIPTION
  Output:
    dist\publish\CodeSearch.exe                       portable executable (no .NET install needed)
    installer\Output\CodeSearch-Setup-<ver>-x64.exe   installer
  Requires the .NET 9 SDK and Inno Setup 6 (https://jrsoftware.org/isinfo.php,
  or: winget install JRSoftware.InnoSetup).
#>
param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

[xml]$proj = Get-Content (Join-Path $root "CodeSearch.csproj")
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "Version not found in CodeSearch.csproj" }
Write-Host "Building CodeSearch $version" -ForegroundColor Cyan

$publishDir = Join-Path $root "dist\publish"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish (Join-Path $root "CodeSearch.csproj") -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=true -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$version" "/DPublishDir=$publishDir" (Join-Path $root "installer\CodeSearch.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

Get-ChildItem (Join-Path $root "installer\Output") -Filter "*.exe" | ForEach-Object {
    Write-Host ("Installer: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) -ForegroundColor Green
}
