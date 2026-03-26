@echo off
setlocal

set PRESET=%1
if "%PRESET%"=="" set PRESET=x64-debug

:: ── Trouver Visual Studio via vswhere ────────────────────────────────────────
for /f "usebackq tokens=*" %%i in (
  `"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`
) do set VS_PATH=%%i

if not defined VS_PATH (
    echo [build-native] ERREUR : Visual Studio introuvable via vswhere.
    exit /b 1
)

:: ── Initialiser l'environnement VC x64 ───────────────────────────────────────
call "%VS_PATH%\VC\Auxiliary\Build\vcvarsall.bat" x64 > nul 2>&1

:: ── Configurer cmake si pas encore fait ──────────────────────────────────────
if not exist "out\build\%PRESET%\build.ninja" (
    echo [build-native] Configuration cmake --preset %PRESET% ...
    cmake --preset %PRESET%
    if errorlevel 1 exit /b 1
)

:: ── Tout builder : native + copie DLLs + .NET ────────────────────────────────
echo [build-native] cmake --build out\build\%PRESET% ...
cmake --build out\build\%PRESET%
exit /b %ERRORLEVEL%
