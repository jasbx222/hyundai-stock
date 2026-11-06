@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo.
echo  سيناريو اختبار جرد هيونداي
echo  الصفحة راح تنفتح على http://127.0.0.1:5180
echo  لوحة التحكم لازم تكون على http://127.0.0.1:5169
echo.
start "" http://127.0.0.1:5180/
python -m http.server 5180
if errorlevel 1 (
  echo ماگدرت اشغل python. افتح index.html يدوياً من هالمجلد.
  start "" "%~dp0index.html"
  pause
)
