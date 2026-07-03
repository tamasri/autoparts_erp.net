@echo off
powershell -NoProfile -Command "Stop-ScheduledTask -TaskName ERP-API -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName ERP-API -Confirm:$false -ErrorAction SilentlyContinue; Stop-ScheduledTask -TaskName ERP-FRONTEND -ErrorAction SilentlyContinue; Unregister-ScheduledTask -TaskName ERP-FRONTEND -Confirm:$false -ErrorAction SilentlyContinue"
for /f "tokens=5" %%p in ('netstat -ano 2^>nul ^| findstr ":47000 "') do taskkill /PID %%p /F >nul 2>&1
for /f "tokens=5" %%p in ('netstat -ano 2^>nul ^| findstr ":47173 "') do taskkill /PID %%p /F >nul 2>&1
echo Stack stopped.
pause
