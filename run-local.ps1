param(
    [switch]$SkipDocker,
    [switch]$SkipBuild,
    [switch]$SkipFrontendInstall,
    [switch]$Headless
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

& (Join-Path $PSScriptRoot "scripts\start-local.ps1") `
    -SkipDocker:$SkipDocker `
    -SkipBuild:$SkipBuild `
    -SkipFrontendInstall:$SkipFrontendInstall `
    -Headless:$Headless
