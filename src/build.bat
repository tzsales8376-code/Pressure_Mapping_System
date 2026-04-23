@echo off
chcp 65001 >nul
REM ===================================================================
REM  TRANZX Pressure Mapping System - Build Script
REM  建置主程式 + MSI 安裝包
REM ===================================================================
REM
REM  前置需求:
REM   - .NET 8 SDK (https://dot.net)
REM   - WiX Toolset v5: dotnet tool install --global wix
REM   - (選用) FFmpeg: 放入 PATH 或程式目錄以支援 MP4 匯出
REM
REM ===================================================================

echo.
echo  ========================================
echo   TRANZX Pressure Mapping System v1.1
echo   Build Script
echo  ========================================
echo.

REM Step 1: Restore packages
echo [1/4] Restoring NuGet packages...
dotnet restore PressureMappingSystem.sln
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Package restore failed!
    pause
    exit /b 1
)

REM Step 2: Build
echo [2/4] Building solution (Release)...
dotnet build PressureMappingSystem.sln -c Release --no-restore
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

REM Step 3: Publish (self-contained)
echo [3/4] Publishing self-contained package...
dotnet publish PressureMappingSystem\PressureMappingSystem.csproj -c Release -r win-x64 --self-contained -o PressureMappingSystem\bin\Release\net8.0-windows\win-x64\publish
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Publish failed!
    pause
    exit /b 1
)

REM Step 4: Build MSI (if WiX is installed)
echo [4/4] Building MSI installer...
where wix >nul 2>nul
if %ERRORLEVEL% EQU 0 (
    dotnet build PressureMappingSystem.Installer\PressureMappingSystem.Installer.wixproj -c Release
    if %ERRORLEVEL% EQU 0 (
        echo.
        echo MSI installer created successfully!
        echo   Location: PressureMappingSystem.Installer\bin\Release\
    ) else (
        echo WARNING: MSI build failed. You can still run the app from the publish folder.
    )
) else (
    echo WARNING: WiX Toolset not found. Skipping MSI build.
    echo   Install: dotnet tool install --global wix
)

echo.
echo ===================================================================
echo  Build complete!
echo.
echo  Run the app:
echo    PressureMappingSystem\bin\Release\net8.0-windows\win-x64\publish\PressureMappingSystem.exe
echo.
echo  MSI installer (if built):
echo    PressureMappingSystem.Installer\bin\Release\*.msi
echo ===================================================================
pause
