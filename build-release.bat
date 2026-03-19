@echo off
echo Closing ShakeToFindCursor app...
taskkill /IM ShakeToFindCursor.exe /F 2>nul

echo Waiting for app to close...
timeout /t 2 /nobreak

echo Building in Release mode...
cd /d "%~dp0ShakeToFindCursor"
dotnet build --configuration Release

echo.
echo Build complete! The Release executable is at:
echo %~dp0ShakeToFindCursor\bin\Release\net10.0-windows\ShakeToFindCursor.exe
echo.
pause
