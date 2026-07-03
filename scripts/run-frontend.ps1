Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location (Join-Path $repoRoot "frontend")

$env:SystemRoot = "C:\Windows"
$env:WINDIR = "C:\Windows"
$env:ProgramFiles = "C:\Program Files"
$env:ProgramData = "C:\ProgramData"
$env:CommonProgramFiles = "C:\Program Files\Common Files"
$env:USERPROFILE = "C:\Users\3420 WIN 11 PRO"
$env:APPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Roaming"
$env:LOCALAPPDATA = "C:\Users\3420 WIN 11 PRO\AppData\Local"
$env:TEMP = "C:\Temp"
$env:TMP = "C:\Temp"
$env:PATH = "C:\Program Files\nodejs;C:\Windows\system32;C:\Windows;C:\Windows\System32\WindowsPowerShell\v1.0;$env:PATH"

New-Item -ItemType Directory -Force -Path "C:\Temp" | Out-Null

$viteEntry = "node_modules\\vite\\bin\\vite.js"
if (-not (Test-Path (Join-Path (Get-Location).Path $viteEntry))) {
    throw "Vite is missing. Run: npm install (inside frontend)."
}

& "C:\Program Files\nodejs\node.exe" $viteEntry --host 127.0.0.1 --port 5173
