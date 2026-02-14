# إصلاح خطأ NU1301 (فشل استعادة حزم NuGet)
# المشكلة: عدم الاتصال بـ api.nuget.org — وليس خطأ في الكود
# تشغيل: .\Scripts\fix-nuget-restore.ps1

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $projectRoot

Write-Host "1) مسح ذاكرة التخزين المؤقت لـ NuGet..." -ForegroundColor Yellow
dotnet nuget locals all --clear

Write-Host "2) استعادة الحزم..." -ForegroundColor Yellow
dotnet restore

if ($LASTEXITCODE -eq 0) {
    Write-Host "تم بنجاح." -ForegroundColor Green
} else {
    Write-Host "فشل. تأكد من الاتصال بالإنترنت وأن api.nuget.org متاح." -ForegroundColor Red
}
