<#
  Creates src/AutoPartsERP.Api/appsettings.Development.json for a fresh checkout: copies the example and fills in a NEW RSA key pair
  for JWT signing (generated on this machine, never committed) and, when given, the dev database password.
  The real file is git-ignored. Run it once; it refuses to overwrite an existing file.

    powershell -ExecutionPolicy Bypass -File .\scripts\init-dev-settings.ps1 -DbPassword <password from docker-compose.dev.yml>
#>
param(
  [string]$DbPassword = '',
  [string]$OutFile = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$api = Join-Path $repoRoot 'src\AutoPartsERP.Api'
if (-not $OutFile) { $OutFile = Join-Path $api 'appsettings.Development.json' }

if (Test-Path $OutFile) { throw "$OutFile already exists; delete it first if you really want new keys (all dev sessions are signed out)." }

$settings = Get-Content (Join-Path $api 'appsettings.Development.example.json') -Raw | ConvertFrom-Json
$rsa = [System.Security.Cryptography.RSA]::Create(2048)
$toBase64 = { param($pem) [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pem)) }
$settings.Jwt.PrivateKeyPemBase64 = & $toBase64 $rsa.ExportPkcs8PrivateKeyPem()
$settings.Jwt.PublicKeyPemBase64 = & $toBase64 $rsa.ExportSubjectPublicKeyInfoPem()
if ($DbPassword) { $settings.Database.ConnectionString = $settings.Database.ConnectionString -replace 'Password=[^;]*', "Password=$DbPassword" }

# First-admin password: random, written only into the git-ignored file (never printed). Used once, when the admin user does not exist yet.
$bytes = [byte[]]::new(18)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
$adminPassword = 'Dev!' + ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x') + '9a'
$settings | Add-Member -NotePropertyName Seed -NotePropertyValue ([pscustomobject]@{ AdminPassword = $adminPassword }) -Force

$settings | ConvertTo-Json -Depth 5 | Set-Content -Path $OutFile -Encoding UTF8
Write-Host "Wrote $OutFile (git-ignored). Keys and the first-admin password (Seed.AdminPassword) are local to this machine."
