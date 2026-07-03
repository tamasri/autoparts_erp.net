Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

$env:SystemRoot = "C:\Windows"
$env:WINDIR = "C:\Windows"
$env:ProgramFiles = "C:\Program Files"
$env:ProgramData = "C:\ProgramData"
$env:CommonProgramFiles = "C:\Program Files\Common Files"
$env:USERPROFILE = "C:\Users\3420 WIN 11 PRO"
$env:HOME = $env:USERPROFILE
$env:NUGET_PACKAGES = "C:\NuGetPackages"
$env:APPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Roaming"
$env:LOCALAPPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Local"
$env:TEMP = "C:\Temp"
$env:TMP = "C:\Temp"
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:PATH = "C:\Program Files\dotnet;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0;$env:USERPROFILE\.dotnet\tools;$env:PATH"

New-Item -ItemType Directory -Force -Path "C:\Temp" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\NuGetPackages" | Out-Null

& "C:\Program Files\dotnet\dotnet.exe" run `
  --project "src\AutoPartsERP.Api" `
  --launch-profile Development
