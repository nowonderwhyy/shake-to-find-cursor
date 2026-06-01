@echo off
echo Closing ShakeToFindCursor app...
taskkill /IM ShakeToFindCursor.exe /F 2>nul

echo Waiting for app to close...
timeout /t 2 /nobreak >nul

echo Publishing single-file Release build...
cd /d "%~dp0"
dotnet publish ShakeToFindCursor\ShakeToFindCursor.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
if errorlevel 1 (
    echo.
    echo Build FAILED.
    pause
    exit /b 1
)

echo.
echo Done! Run the app here:
echo %~dp0dist\ShakeToFindCursor.exe
echo.
pause
