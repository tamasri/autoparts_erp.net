param(
    [switch]$SkipDocker,
    [switch]$SkipBuild,
    [switch]$SkipFrontendInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$logsDir = Join-Path $repoRoot "scripts\logs"
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null

function Wait-Http {
    param(
        [string]$Url,
        [int]$Retries = 40,
        [int]$DelaySeconds = 2
    )
    for ($i = 0; $i -lt $Retries; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 4
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500) { return $true }
        } catch {}
        Start-Sleep -Seconds $DelaySeconds
    }
    return $false
}

function Tail-Log {
    param([string]$Path)
    if (Test-Path $Path) {
        Get-Content $Path -Tail 30 -ErrorAction SilentlyContinue
    } else {
        Write-Host "(no log file: $Path)"
    }
}

Set-Location $repoRoot

$env:SystemRoot = "C:\Windows"
$env:WINDIR = "C:\Windows"
$env:ComSpec = "C:\Windows\System32\cmd.exe"
$env:ProgramFiles = "C:\Program Files"
Set-Item -Path "env:ProgramFiles(x86)" -Value "C:\Program Files (x86)"
$env:ProgramData = "C:\ProgramData"
$env:CommonProgramFiles = "C:\Program Files\Common Files"
Set-Item -Path "env:CommonProgramFiles(x86)" -Value "C:\Program Files (x86)\Common Files"
$env:USERPROFILE = "C:\Users\3420 WIN 11 PRO"
$env:HOMEDRIVE = "C:"
$env:HOMEPATH = "\Users\3420 WIN 11 PRO"
$env:HOME = $env:USERPROFILE
$env:APPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Roaming"
$env:LOCALAPPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Local"
$env:TEMP = "C:\Temp"
$env:TMP = "C:\Temp"
$env:NUGET_PACKAGES = "C:\NuGetPackages"
New-Item -ItemType Directory -Force -Path "C:\Temp" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\NuGetPackages" | Out-Null
$env:PATH = "C:\Program Files\nodejs;C:\Program Files\dotnet;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0;$env:USERPROFILE\.dotnet\tools;$env:PATH"

Write-Host "Stopping existing local processes..."
& (Join-Path $PSScriptRoot "stop-local.ps1")

if (-not $SkipDocker) {
    Write-Host "Starting docker infrastructure..."
    & "C:\Program Files\Docker\Docker\resources\bin\docker.exe" compose -f "docker-compose.dev.yml" up -d
}

if (-not $SkipBuild) {
    Write-Host "Restoring and building backend..."
    & "C:\Program Files\dotnet\dotnet.exe" restore "AutoPartsERP.sln" --configfile (Join-Path $repoRoot "NuGet.Config")
    if (-not $?) { throw "dotnet restore failed." }

    & "C:\Program Files\dotnet\dotnet.exe" build "AutoPartsERP.sln" -c Debug --no-restore
    if (-not $?) { throw "dotnet build failed." }
}

if (-not $SkipFrontendInstall -and -not (Test-Path (Join-Path $repoRoot "frontend\node_modules"))) {
    Write-Host "Installing frontend dependencies..."
    Set-Location (Join-Path $repoRoot "frontend")
    & "C:\Program Files\nodejs\npm.cmd" install
    if (-not $?) { throw "npm install failed." }
    Set-Location $repoRoot
}

$apiOut = Join-Path $logsDir "api.out.log"
$apiErr = Join-Path $logsDir "api.err.log"
$feOut = Join-Path $logsDir "frontend.out.log"
$feErr = Join-Path $logsDir "frontend.err.log"
Remove-Item $apiOut,$apiErr,$feOut,$feErr -Force -ErrorAction SilentlyContinue

Write-Host "Starting API process..."
$apiCmd = "`"C:\Program Files\dotnet\dotnet.exe`" run --project `"src\AutoPartsERP.Api`" --launch-profile Development"
$apiProc = Start-Process -FilePath "cmd.exe" `
    -ArgumentList "/c $apiCmd > `"$apiOut`" 2> `"$apiErr`"" `
    -WorkingDirectory $repoRoot `
    -PassThru -WindowStyle Hidden

Write-Host "Starting Frontend process..."
$viteEntry = "node_modules\vite\bin\vite.js"
$frontendDir = Join-Path $repoRoot "frontend"
if (-not (Test-Path (Join-Path $frontendDir $viteEntry))) {
    throw "Vite is missing. Run npm install in frontend."
}
$feCmd = "`"C:\Program Files\nodejs\node.exe`" `"$viteEntry`" --host 127.0.0.1 --port 47173"
$feProc = Start-Process -FilePath "cmd.exe" `
    -ArgumentList "/c $feCmd > `"$feOut`" 2> `"$feErr`"" `
    -WorkingDirectory $frontendDir `
    -PassThru -WindowStyle Hidden

@{
    ApiPid = $apiProc.Id
    FrontendPid = $feProc.Id
    ApiOut = $apiOut
    ApiErr = $apiErr
    FrontendOut = $feOut
    FrontendErr = $feErr
} | ConvertTo-Json | Set-Content -Path (Join-Path $logsDir "pids.json")

Write-Host "Waiting for health checks..."
$apiOk = Wait-Http -Url "http://127.0.0.1:47000/health"
$feOk = Wait-Http -Url "http://127.0.0.1:47173"

Write-Host ""
Write-Host "==== Local Stack Status ===="
Write-Host ("API      : " + ($(if ($apiOk) { "UP" } else { "DOWN" })))
Write-Host ("Frontend : " + ($(if ($feOk) { "UP" } else { "DOWN" })))
Write-Host "API URL  : http://127.0.0.1:47000"
Write-Host "UI URL   : http://127.0.0.1:47173"
Write-Host "Logs     : $logsDir"

if (-not $apiOk) {
    Write-Host "`n--- API STDERR (tail) ---"
    Tail-Log -Path $apiErr
}
if (-not $feOk) {
    Write-Host "`n--- Frontend STDERR (tail) ---"
    Tail-Log -Path $feErr
}

if (-not $apiOk -or -not $feOk) {
    throw "Local stack startup failed. Check logs under scripts\\logs."
}
