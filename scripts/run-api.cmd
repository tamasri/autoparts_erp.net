@echo off
cd /d "c:\Users\3420 WIN 11 PRO\OneDrive\Attachments\Documents\autopart_erp.net"
"C:\Program Files\dotnet\dotnet.exe" run --project src\AutoPartsERP.Api --launch-profile Development --no-build > "scripts\logs\api.out.log" 2> "scripts\logs\api.err.log"
