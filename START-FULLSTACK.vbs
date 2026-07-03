Dim sh
Set sh = CreateObject("WScript.Shell")

Dim root
root = "c:\Users\3420 WIN 11 PRO\OneDrive\Attachments\Documents\autopart_erp.net"

' Kill anything on our ports
sh.Run "cmd /c for /f ""tokens=5"" %p in ('netstat -ano ^| findstr "":47000 ""') do taskkill /PID %p /F", 0, True
sh.Run "cmd /c for /f ""tokens=5"" %p in ('netstat -ano ^| findstr "":47173 ""') do taskkill /PID %p /F", 0, True

' Start API
sh.CurrentDirectory = root
sh.Run "cmd /k ""C:\Program Files\dotnet\dotnet.exe"" run --project src\AutoPartsERP.Api --launch-profile Development --no-build", 1, False

' Start Frontend
sh.CurrentDirectory = root & "\frontend"
sh.Run "cmd /k ""C:\Program Files\nodejs\node.exe"" node_modules\vite\bin\vite.js --host 127.0.0.1 --port 47173", 1, False

WScript.Sleep 2000
sh.Popup "Two console windows opened." & vbCrLf & vbCrLf & _
    "UI  : http://localhost:47173" & vbCrLf & _
    "API : http://localhost:47000" & vbCrLf & _
    "Docs: http://localhost:47000/scalar" & vbCrLf & _
    "pgAdmin : http://localhost:47050" & vbCrLf & _
    "Seq : http://localhost:47341" & vbCrLf & vbCrLf & _
    "Login: admin / Admin@123456", _
    0, "AutoPartsERP Started"
