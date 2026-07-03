@echo off
title AutoParts ERP - Dev Environment
cd /d "%~dp0"

echo [1/3] Starting Docker Services (Postgres, Redis, etc.)...
docker compose -f docker-compose.dev.yml up -d

echo Waiting for database to be ready...
timeout /t 5 /nobreak > nul

echo [2/3] Starting Backend API...
:: Using double quotes around the cd path to handle the space in "3420 WIN 11 PRO"
start "ERP Backend API" cmd /k "cd /d ""%~dp0src\AutoPartsERP.Api"" && dotnet run --launch-profile Development"

echo [3/3] Starting Frontend SPA...
start "ERP Frontend" cmd /k "cd /d ""%~dp0frontend"" && npm run dev"

echo.
echo ===================================================
echo [SUCCESS] Both servers are starting in separate windows!
echo Please wait about 30 seconds for the API to apply migrations and seed data.
echo Then, open your browser to: http://localhost:47173
echo Login: admin / Admin@123456
echo ===================================================
pause
