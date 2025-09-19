@echo off
echo Starting TTS Modular System...

if not exist "bin\TTS.Main.exe" (
    echo ❌ TTS.Main.exe not found in bin directory!
    echo Please run build.bat first.
    pause
    exit /b 1
)

cd bin
start TTS.Main.exe
cd ..

echo ✅ TTS Modular System started!
echo Check the GUI window for module status.
