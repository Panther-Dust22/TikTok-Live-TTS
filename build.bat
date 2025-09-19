@echo off
echo Building TTS Modular System...

REM Clean previous builds
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

REM Build the solution
dotnet build TTS.Modular.sln --configuration Release --verbosity minimal

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ✅ Build successful!
    echo.
    echo Copying files to bin directory...
    
    REM Create bin directory
    mkdir bin 2>nul
    
    REM Copy main executable and config files
    copy "TTS.Main\bin\Release\net8.0-windows\TTS.Main.exe" "bin\" >nul
    copy "TTS.Main\bin\Release\net8.0-windows\TTS.Main.runtimeconfig.json" "bin\" >nul
    copy "TTS.Main\bin\Release\net8.0-windows\TTS.Main.deps.json" "bin\" >nul
    
    REM Copy DLLs
    copy "TTS.Shared\bin\Release\net8.0-windows\TTS.Shared.dll" "bin\" >nul
    copy "TTS.Processing\bin\Release\net8.0-windows\TTS.Processing.dll" "bin\" >nul
    copy "TTS.TTS\bin\Release\net8.0-windows\TTS.TTS.dll" "bin\" >nul
    copy "TTS.AudioQueue\bin\Release\net8.0-windows\TTS.AudioQueue.dll" "bin\" >nul
    copy "TTS.UserTracking\bin\Release\net8.0-windows\TTS.UserTracking.dll" "bin\" >nul
    
    
    REM Copy runtime files
    xcopy "TTS.Main\bin\Release\net8.0-windows\*.dll" "bin\" /Y >nul
    
    echo.
    echo 🎉 Build complete! Run TTS.Main.exe from the bin directory.
    echo.
) else (
    echo.
    echo ❌ Build failed!
    echo.
)

pause
