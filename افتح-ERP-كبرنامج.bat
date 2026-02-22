@echo off
chcp 65001 >nul
REM ============================================
REM  افتح ERP كنافذة مستقلة (بدون شريط المتصفح)
REM  تأكد من تشغيل البرنامج أولاً (dotnet run أو Visual Studio)
REM ============================================

set URL=http://localhost:5074

REM جرب Chrome أولاً
if exist "C:\Program Files\Google\Chrome\Application\chrome.exe" (
    start "" "C:\Program Files\Google\Chrome\Application\chrome.exe" --app=%URL%
    exit /b
)

REM لو مفيش Chrome، استخدم Edge
if exist "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" (
    start "" "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --app=%URL%
    exit /b
)

if exist "C:\Program Files\Microsoft\Edge\Application\msedge.exe" (
    start "" "C:\Program Files\Microsoft\Edge\Application\msedge.exe" --app=%URL%
    exit /b
)

echo لم يتم العثور على Chrome أو Edge.
echo افتح المتصفح يدوياً وادخل: %URL%
pause
