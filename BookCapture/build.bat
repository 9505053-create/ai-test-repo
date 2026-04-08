@echo off
chcp 65001 >nul
echo ============================================
echo  BookCapture v4.0 - PyInstaller Build
echo ============================================

cd /d "%~dp0book_capture"

pyinstaller ^
  --onefile ^
  --windowed ^
  --name "BookCapture" ^
  --add-data "config.py;." ^
  --add-data "capturer.py;." ^
  --add-data "utils;utils" ^
  main.py

echo.
echo Build complete. EXE is in: dist\BookCapture.exe
pause
