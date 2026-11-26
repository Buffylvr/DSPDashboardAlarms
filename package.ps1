# Builds the plugin and packages it for Thunderstore into dist/<Team>-<Name>-<version>.zip.
# Usage: powershell -ExecutionPolicy Bypass -File package.ps1 [-Team Buffylvr]
param([string]$Team = "Buffylvr")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$manifest = Get-Content (Join-Path $root "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number
$name = $manifest.name

# The manifest, the plugin and the csproj must agree on the version.
$pluginVersion = (Select-String -Path (Join-Path $root "src\Plugin.cs") -Pattern 'Version = "([^"]+)"').Matches[0].Groups[1].Value
if ($pluginVersion -ne $version) { throw "Plugin.cs version $pluginVersion != manifest $version" }

$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
& $dotnet build (Join-Path $root "DashboardAlarms.csproj") -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dist = Join-Path $root "dist"
$stage = Join-Path $dist "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

Copy-Item (Join-Path $root "manifest.json") $stage
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "CHANGELOG.md") $stage
Copy-Item (Join-Path $root "icon.png") $stage
Copy-Item (Join-Path $root "bin\Release\DashboardAlarms.dll") $stage

$zip = Join-Path $dist "$Team-$name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Write-Output "Packaged $zip"
