param(
    [switch]$StopDocker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$logsDir = Join-Path $repoRoot "scripts\logs"
$pidFile = Join-Path $logsDir "pids.json"

Write-Host "Stopping local API/frontend listeners..."

if (Test-Path $pidFile) {
    try {
        $p = Get-Content $pidFile -Raw | ConvertFrom-Json
        foreach ($pid in @($p.ApiPid, $p.FrontendPid)) {
            if ($pid) {
                try { Stop-Process -Id $pid -Force -ErrorAction Stop; Write-Host "Stopped PID=$pid" } catch {}
            }
        }
    } catch {}
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

foreach ($port in @(5000,5173)) {
    try {
        $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop
        foreach ($l in $listeners) {
            if ($l.OwningProcess -gt 0) {
                try { Stop-Process -Id $l.OwningProcess -Force -ErrorAction Stop; Write-Host "Killed PID=$($l.OwningProcess) on port $port" } catch {}
            }
        }
    } catch {}
}

try {
    $proc = Get-CimInstance Win32_Process |
        Where-Object {
            ($_.Name -match "dotnet\.exe" -and $_.CommandLine -match "AutoPartsERP\.Api") -or
            ($_.Name -match "node\.exe|npm\.cmd" -and $_.CommandLine -match "vite|5173|frontend")
        }
    foreach ($p in $proc) {
        try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop } catch {}
    }
} catch {}

if ($StopDocker) {
    Write-Host "Stopping docker compose services..."
    try {
        & "C:\Program Files\Docker\Docker\resources\bin\docker.exe" compose -f (Join-Path $repoRoot "docker-compose.dev.yml") down
    } catch {
        Write-Host "Docker stop skipped: $($_.Exception.Message)"
    }
}

Write-Host "Done."
