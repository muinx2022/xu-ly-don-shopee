@echo off
rem ============================================================
rem  Chay app "Xu ly don Shopee" (ban Release).
rem  May nay bat WDAC -> file moi build bi chan (0x800711C7) cho
rem  toi khi Windows/ISG duyet xong. Script nay tu bam lai nhieu
rem  lan cho toi khi cua so mo len, de khoi phai double-click nhieu lan.
rem ============================================================
cd /d "%~dp0"
set "EXE=%~dp0src\XuLyDonShopee.App\bin\Release\net8.0\XuLyDonShopee.App.exe"
if not exist "%EXE%" (
  echo Chua thay file: %EXE%
  echo Hay build truoc:  dotnet build -c Release
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe='%EXE%'; for($t=1;$t -le 10;$t++){ $p=Start-Process $exe -PassThru; $ok=$false; for($i=0;$i -lt 12;$i++){ Start-Sleep -Milliseconds 500; if($p.HasExited){break}; if($p.MainWindowHandle -ne 0){$ok=$true;break} }; if($ok){ Write-Host ('App da mo (lan '+$t+').'); exit 0 }; if(-not $p.HasExited){ $p.Kill() | Out-Null }; Write-Host ('Lan '+$t+' chua duoc (Windows dang duyet file)...') }; Write-Host 'Van chua mo duoc sau 10 lan - doi vai giay roi chay lai file nay.'; exit 1"
