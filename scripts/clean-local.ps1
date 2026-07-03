param(
    [switch]$Deep,
    [switch]$PruneDocker
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

Write-Host "Cleaning local artifacts..."

try {
    & (Join-Path $PSScriptRoot "stop-local.ps1")
} catch {}

Get-ChildItem -Path "src","tests" -Recurse -Directory -Filter "bin" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path "src","tests" -Recurse -Directory -Filter "obj" -ErrorAction SilentlyContinue |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

Remove-Item -Recurse -Force "frontend\dist" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "frontend\.vite" -ErrorAction SilentlyContinue

# Remove generated diagnostic artifacts from repository root only.
$rootJunkPatterns = @(
    "s*_*.txt",
    "step*_*.txt",
    "fix*.txt",
    "diag*.txt",
    "restore*.txt",
    "cmd_*.txt",
    "compose_*.txt",
    "dotnet_*.txt",
    "npm_*.txt",
    "pg_*.txt",
    "redis_*.txt",
    "table_count*.txt",
    "infra_*.txt",
    "api_run_*.log",
    "fc_*.txt",
    "post_audit_*.txt",
    "repair_*.txt",
    "sdks_*.txt",
    "child_env.txt",
    ".tmp_*.txt"
)

foreach ($pattern in $rootJunkPatterns) {
    Get-ChildItem -Path $repoRoot -File -Filter $pattern -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

if ($Deep) {
    Remove-Item -Recurse -Force "frontend\node_modules" -ErrorAction SilentlyContinue
    Remove-Item -Force "frontend\package-lock.json" -ErrorAction SilentlyContinue
    & "C:\Program Files\dotnet\dotnet.exe" nuget locals all --clear | Out-Null
}

if ($PruneDocker) {
    try {
        & "C:\Program Files\Docker\Docker\resources\bin\docker.exe" system prune -f
    } catch {
        Write-Host "Docker prune skipped: $($_.Exception.Message)"
    }
}

Write-Host "Clean completed."
