# Script to build PortAudio locally for development/debugging on Windows
# This builds PortAudio for the current platform only

param(
    [string]$Architecture = "x64"
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PortAudioVersion = "v19.7.0"
$BuildDir = Join-Path $ScriptDir "build\portaudio"
$InstallDir = Join-Path $ScriptDir "src\Bufdio.Spice86\runtimes"

Write-Host "Building PortAudio $PortAudioVersion for local development..." -ForegroundColor Green

# Determine RID based on architecture
switch ($Architecture.ToLower()) {
    "x64" { $RID = "win-x64"; $CMakeArch = "x64" }
    "x86" { $RID = "win-x86"; $CMakeArch = "Win32" }
    "arm64" { $RID = "win-arm64"; $CMakeArch = "ARM64" }
    default {
        Write-Error "Unsupported architecture: $Architecture. Use x64, x86, or arm64."
        exit 1
    }
}

$LibName = "libportaudio.dll"

Write-Host "Platform: Windows"
Write-Host "Architecture: $Architecture"
Write-Host "RID: $RID"
Write-Host "Library: $LibName"

# Check for CMake
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
    Write-Error "CMake is not installed. Please install CMake first."
    exit 1
}

# Check for Git
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Error "Git is not installed. Please install Git first."
    exit 1
}

# Clone PortAudio if not already present
if (-not (Test-Path $BuildDir)) {
    Write-Host "Cloning PortAudio repository..." -ForegroundColor Cyan
    $BuildParent = Split-Path -Parent $BuildDir
    if (-not (Test-Path $BuildParent)) {
        New-Item -ItemType Directory -Path $BuildParent -Force | Out-Null
    }
    git clone --depth 1 --branch $PortAudioVersion https://github.com/PortAudio/portaudio.git $BuildDir
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to clone PortAudio repository"
        exit 1
    }
}

Push-Location $BuildDir

try {
    # Configure CMake
    Write-Host "Configuring CMake..." -ForegroundColor Cyan
    cmake -B build -S . `
        -A $CMakeArch `
        -DCMAKE_BUILD_TYPE=Release `
        -DPA_BUILD_SHARED=ON `
        -DPA_BUILD_STATIC=OFF `
        -DPA_USE_ASIO=OFF `
        -DPA_USE_DS=OFF `
        -DPA_USE_WMME=OFF `
        -DPA_USE_WASAPI=ON `
        -DPA_USE_WDMKS=OFF `
        -DCMAKE_INSTALL_PREFIX=install

    if ($LASTEXITCODE -ne 0) {
        Write-Error "CMake configuration failed"
        exit 1
    }

    # Build
    Write-Host "Building..." -ForegroundColor Cyan
    cmake --build build --config Release

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit 1
    }

    # Install
    Write-Host "Installing..." -ForegroundColor Cyan
    cmake --install build --config Release

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Installation failed"
        exit 1
    }

    # Copy to runtimes directory
    $OutputDir = Join-Path $InstallDir "$RID\native"
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    }

    $SourceLib = Join-Path $BuildDir "install\bin\$LibName"
    $DestLib = Join-Path $OutputDir $LibName

    Copy-Item $SourceLib $DestLib -Force

    Write-Host ""
    Write-Host "✓ PortAudio built successfully!" -ForegroundColor Green
    Write-Host "✓ Library installed to: $OutputDir" -ForegroundColor Green
    Write-Host ""
    Write-Host "You can now build and debug Spice86 with audio support." -ForegroundColor Cyan
}
finally {
    Pop-Location
}
