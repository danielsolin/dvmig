[CmdletBinding()]
param(
    [string]$HostDirectory = "C:\tmp\dvmig-xtb-build",
    [string]$ProfileDirectory = ""
)

$ErrorActionPreference = "Stop"

$HostDirectory = $HostDirectory.Trim('"')
$ProfileDirectory = $ProfileDirectory.Trim('"')

$hostDirectoryPath = [System.IO.Path]::GetFullPath($HostDirectory)
$ProfileDirectory = if ([string]::IsNullOrWhiteSpace($ProfileDirectory)) {
    $hostDirectoryPath
} else {
    $ProfileDirectory
}
$profileDirectoryPath = [System.IO.Path]::GetFullPath($ProfileDirectory)
$xrmToolBoxExe = Join-Path $hostDirectoryPath "XrmToolBox.exe"

if (-not (Test-Path $xrmToolBoxExe)) {
    Write-Host "XrmToolBox.exe not found in '$hostDirectoryPath'; skipping dev-host patch."
    return
}

$profilePluginsDirectory = Join-Path $profileDirectoryPath "Plugins"
$profileSettingsDirectory = Join-Path $profileDirectoryPath "Settings"
$profileConnectionsDirectory = Join-Path $profileDirectoryPath "Connections"
New-Item -ItemType Directory -Force -Path $profilePluginsDirectory, $profileSettingsDirectory, $profileConnectionsDirectory | Out-Null

$pluginDll = Join-Path $hostDirectoryPath "dvmig.XTB.dll"
if (Test-Path $pluginDll) {
    Copy-Item $pluginDll $profilePluginsDirectory -Force
}

$settingsFile = Join-Path $profileSettingsDirectory "XrmToolBox.Settings.xml"
if (-not (Test-Path $settingsFile)) {
    @"
<?xml version="1.0" encoding="utf-8"?>
<Options xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <DisplayToolsListFirst>true</DisplayToolsListFirst>
  <DoNotShowStartPage>true</DoNotShowStartPage>
  <CheckUpdateOnStartup>false</CheckUpdateOnStartup>
  <DoNotCheckForUpdates>true</DoNotCheckForUpdates>
  <ShowPluginUpdatesPanelAtStartup>false</ShowPluginUpdatesPanelAtStartup>
  <DisplayPluginsStoreOnStartup>false</DisplayPluginsStoreOnStartup>
  <DisplayPluginsStoreOnlyIfUpdates>false</DisplayPluginsStoreOnlyIfUpdates>
  <AllowLogUsage>false</AllowLogUsage>
  <RepositoryUrl>http://127.0.0.1:9/_odata/plugins</RepositoryUrl>
</Options>
"@ | Set-Content -Path $settingsFile -Encoding UTF8
}

$patcherSource = Join-Path $PSScriptRoot "PatchXrmToolBoxDevHost.cs"
$patcherDirectory = Join-Path $env:TEMP "dvmig-xtb-devhost-patcher"
$patcherExe = Join-Path $patcherDirectory "PatchXrmToolBoxDevHost.exe"
New-Item -ItemType Directory -Force -Path $patcherDirectory | Out-Null

$cecil = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\microsoft.net.illink.tasks") -Filter Mono.Cecil.dll -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\tools\netframework\Mono.Cecil.dll" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $cecil) {
    $cecil = Get-ChildItem (Join-Path $env:USERPROFILE ".nuget\packages\mono.cecil") -Filter Mono.Cecil.dll -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
}

if (-not $cecil) {
    Write-Warning "Could not find Mono.Cecil.dll in the local NuGet cache; skipping XrmToolBox dev-host patch."
    return
}

Copy-Item $cecil.FullName $patcherDirectory -Force

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$netstandard = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll"
$cecilReference = Join-Path $patcherDirectory "Mono.Cecil.dll"
$compilerArgs = @(
    "/nologo",
    "/r:$cecilReference",
    "/r:System.Windows.Forms.dll",
    "/r:$netstandard",
    "/out:$patcherExe",
    $patcherSource
)
& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Failed to compile XrmToolBox dev-host patcher."
}

& $patcherExe $xrmToolBoxExe
if ($LASTEXITCODE -ne 0) {
    throw "Failed to patch XrmToolBox dev host."
}

Write-Host "Prepared XrmToolBox dev host:"
Write-Host "  Host:    $hostDirectoryPath"
Write-Host "  Profile: $profileDirectoryPath"
