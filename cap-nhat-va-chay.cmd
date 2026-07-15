@echo off
rem ============================================================
rem  Cap nhat (publish ban moi nhat tu ma nguon) ROI chay app.
rem  Dung file NAY cho shortcut desktop de LUON chay ban moi nhat.
rem
rem  Cach hoat dong:
rem   1) Dong app cu neu dang chay (tranh khoa file khi publish).
rem   2) Publish ban Release self-contained (IL-thuan, khong R2R)
rem      vao publish\win-x64  ->  chi ghi lai DLL khi ma nguon doi.
rem   3) Chay exe trong publish\win-x64, tu bam lai nhieu lan
rem      phong khi WDAC/ISG con dang duyet file moi (0x800711C7).
rem ============================================================
cd /d "%~dp0"

echo [1/3] Dong app cu (neu dang chay)...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Get-Process XuLyDonShopee.App -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; if(-not $_.WaitForExit(8000)){ try{ $_.Kill() }catch{} } }"

echo [2/3] Build + publish ban moi nhat (co the mat vai chuc giay lan dau)...
dotnet publish src\XuLyDonShopee.App\XuLyDonShopee.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64 --nologo
if errorlevel 1 (
  echo.
  echo *** Publish that bai. Neu bao "file dang bi khoa": dong het app dang chay roi chay lai file nay.
  pause
  exit /b 1
)

echo [3/3] Dang mo app...
set "EXE=%~dp0publish\win-x64\XuLyDonShopee.App.exe"
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$exe='%EXE%'; for($t=1;$t -le 10;$t++){ $p=Start-Process $exe -PassThru; $ok=$false; for($i=0;$i -lt 12;$i++){ Start-Sleep -Milliseconds 500; if($p.HasExited){break}; if($p.MainWindowHandle -ne 0){$ok=$true;break} }; if($ok){ Write-Host ('App da mo (lan '+$t+').'); exit 0 }; if(-not $p.HasExited){ $p.Kill() | Out-Null }; Write-Host ('Lan '+$t+' chua duoc (Windows dang duyet file)...') }; Write-Host 'Van chua mo duoc sau 10 lan - doi vai giay roi chay lai file nay.'; exit 1"
