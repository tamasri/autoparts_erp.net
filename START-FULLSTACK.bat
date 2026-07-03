@echo off
setlocal

set ROOT=c:\Users\3420 WIN 11 PRO\OneDrive\Attachments\Documents\autopart_erp.net
set DOTNET=C:\Program Files\dotnet\dotnet.exe
set NODE=C:\Program Files\nodejs\node.exe
set LOGS=%ROOT%\scripts\logs

:: Kill anything on our ports
for /f "tokens=5" %%p in ('netstat -ano 2^>nul ^| findstr ":47000 "') do taskkill /PID %%p /F >nul 2>&1
for /f "tokens=5" %%p in ('netstat -ano 2^>nul ^| findstr ":47173 "') do taskkill /PID %%p /F >nul 2>&1

:: Register + run API task as current user at highest run level
powershell -NoProfile -Command "$a = New-ScheduledTaskAction -Execute '%DOTNET%' -Argument 'run --project src\AutoPartsERP.Api --launch-profile Development --no-build' -WorkingDirectory '%ROOT%'; $s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0; $p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive; Unregister-ScheduledTask -TaskName ERP-API -Confirm:$false -ErrorAction SilentlyContinue; Register-ScheduledTask -TaskName ERP-API -Action $a -Settings $s -Principal $p -Force | Out-Null; Start-ScheduledTask -TaskName ERP-API"

:: Register + run Frontend task as current user at highest run level
powershell -NoProfile -Command "$a = New-ScheduledTaskAction -Execute '%NODE%' -Argument 'node_modules\vite\bin\vite.js --host 127.0.0.1 --port 47173' -WorkingDirectory '%ROOT%\frontend'; $s = New-ScheduledTaskSettingsSet -ExecutionTimeLimit 0; $p = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive; Unregister-ScheduledTask -TaskName ERP-FRONTEND -Confirm:$false -ErrorAction SilentlyContinue; Register-ScheduledTask -TaskName ERP-FRONTEND -Action $a -Settings $s -Principal $p -Force | Out-Null; Start-ScheduledTask -TaskName ERP-FRONTEND"

echo.
echo =========================================
echo  Waiting for services to start...
echo =========================================
ping -n 35 127.0.0.1 > nul

echo.
echo =========================================
echo  UI       : http://localhost:47173
echo  API      : http://localhost:47000
echo  API Docs : http://localhost:47000/scalar
echo  pgAdmin  : http://localhost:47050
echo  Seq Logs : http://localhost:47341
echo  Login    : admin / Admin@123456
echo =========================================
echo.
echo To stop: run STOP-FULLSTACK.bat
pause
