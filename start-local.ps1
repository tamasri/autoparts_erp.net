#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root   = 'c:\Users\3420 WIN 11 PRO\OneDrive\Attachments\Documents\autopart_erp.net'
$docker = 'C:\Program Files\Docker\Docker\resources\bin\docker.exe'

# ── Stop anything on our ports ───────────────────────────────────────────────
foreach ($port in @(47000, 47173)) {
    try {
        Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop |
            ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    } catch {}
}

# ── Docker infra ─────────────────────────────────────────────────────────────
Write-Host 'Starting Docker infrastructure...'
& $docker compose -f "$root\docker-compose.dev.yml" up -d
if (-not $?) { throw 'docker compose up failed' }

Write-Host 'Waiting for Postgres...'
for ($i = 0; $i -lt 20; $i++) {
    & $docker exec autopart_erpnet-postgres-1 pg_isready -U erp_user -d autoparts_erp 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Host 'Postgres ready.'; break }
    Start-Sleep 2
}

# ── Write launcher cmd files ─────────────────────────────────────────────────
$apiCmd = @"
@echo off
cd /d "$root"
"C:\Program Files\dotnet\dotnet.exe" run --project src\AutoPartsERP.Api --launch-profile Development --no-build
pause
"@

$feCmd = @"
@echo off
cd /d "$root\frontend"
"C:\Program Files\nodejs\node.exe" node_modules\vite\bin\vite.js --host 127.0.0.1 --port 47173
pause
"@

$apiCmdPath = "$root\scripts\run-api.cmd"
$feCmdPath  = "$root\scripts\run-frontend.cmd"

Set-Content -Path $apiCmdPath -Value $apiCmd -Encoding ASCII
Set-Content -Path $feCmdPath  -Value $feCmd  -Encoding ASCII

# ── Launch each in its own window ────────────────────────────────────────────
Write-Host 'Starting API window...'
Start-Process -FilePath $apiCmdPath -WindowStyle Normal

Write-Host 'Starting Frontend window...'
Start-Process -FilePath $feCmdPath -WindowStyle Normal

Write-Host ''
Write-Host '========================================'
Write-Host ' Two console windows opened.'
Write-Host ' Wait ~30s for API to seed the DB.'
Write-Host '----------------------------------------'
Write-Host ' UI      : http://localhost:47173'
Write-Host ' API     : http://localhost:47000'
Write-Host ' Docs    : http://localhost:47000/scalar'
Write-Host ' pgAdmin : http://localhost:47050'
Write-Host ' Seq     : http://localhost:47341'
Write-Host ' Login   : admin / Admin@123456'
Write-Host '========================================'
