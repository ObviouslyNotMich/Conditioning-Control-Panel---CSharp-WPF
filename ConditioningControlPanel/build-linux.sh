#!/usr/bin/env bash
set -euo pipefail

# Build, test, and smoke-run the Avalonia Linux head.
# Run this inside the project root on the Linux VM.
# The script will check for dependencies and try to install missing ones on Ubuntu/Debian.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

LOG_DIR="$SCRIPT_DIR/logs"
mkdir -p "$LOG_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
MAIN_LOG="$LOG_DIR/linux-build-$TIMESTAMP.log"
RUN_LOG="$LOG_DIR/linux-run-$TIMESTAMP.log"

# Write a plain-text "latest" pointer file. Symlinks are not allowed on some
# VirtualBox shared-folder mounts, so we use a text file instead.
printf '%s\n%s\n' "$(basename "$MAIN_LOG")" "$(basename "$RUN_LOG")" > "$LOG_DIR/LATEST"

# Redirect all script output to main log while still printing to console
exec > >(tee -a "$MAIN_LOG") 2>&1

echo "=== Conditioning Control Panel — Linux Build & Test ==="
echo "Log: $MAIN_LOG"
echo "Started: $(date)"
echo

# Helper: check if a command exists
command_exists() {
    command -v "$1" >/dev/null 2>&1
}

# Helper: check if an apt package is installed
apt_package_installed() {
    dpkg -s "$1" >/dev/null 2>&1
}

# Helper: run sudo, optionally with a password supplied via SUDO_PASSWORD.
# Usage: run_sudo apt-get install -y foo
run_sudo() {
    if [ -n "${SUDO_PASSWORD:-}" ]; then
        printf '%s\n' "$SUDO_PASSWORD" | sudo -S "$@"
    else
        sudo "$@"
    fi
}

# Warn early if sudo will need a password and none was provided.
if [ -z "${SUDO_PASSWORD:-}" ] && ! sudo -n true 2>/dev/null; then
    echo "WARNING: passwordless sudo is not available."
    echo "         The script will prompt for a password, but the prompt may be hidden"
    echo "         by the log tee. To avoid this, run:"
    echo "             SUDO_PASSWORD='your-password' ./build-linux.sh"
    echo
fi

# Detect distro
DISTRO="unknown"
if [ -f /etc/os-release ]; then
    # shellcheck source=/dev/null
    . /etc/os-release
    DISTRO="$ID"
fi

echo "Detected distro: $DISTRO"
echo

if [[ "$DISTRO" != "ubuntu" && "$DISTRO" != "debian" ]]; then
    echo "WARNING: This script auto-installs dependencies only on Ubuntu/Debian."
    echo "On other distros, manually install: .NET 8 SDK, libvlc, Avalonia/Skia runtime deps."
    echo
fi

# -----------------------------------------------------------------------------
# 1. Ensure .NET 8 SDK is installed
# -----------------------------------------------------------------------------
# We use Microsoft's distro-agnostic install script because the apt repository
# URL needs the exact Ubuntu/Debian version and often breaks in VMs/containers.
DOTNET_INSTALL_DIR="$HOME/.dotnet"

install_dotnet() {
    echo "Installing .NET 8 SDK via dotnet-install.sh..."
    echo "Install target: $DOTNET_INSTALL_DIR"

    local install_script="/tmp/dotnet-install.sh"
    echo "Downloading dotnet-install.sh..."
    wget -q "https://dot.net/v1/dotnet-install.sh" -O "$install_script" || {
        echo "ERROR: Failed to download dotnet-install.sh. Check internet connection."
        return 1
    }

    echo "Running dotnet-install.sh..."
    # Run with bash explicitly in case /tmp is mounted noexec.
    bash "$install_script" --channel 8.0 --install-dir "$DOTNET_INSTALL_DIR" || {
        echo "ERROR: dotnet-install.sh failed."
        return 1
    }

    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
    export PATH="$DOTNET_INSTALL_DIR:$PATH"

    if ! command_exists dotnet; then
        echo "ERROR: dotnet still not on PATH after install."
        return 1
    fi

    echo ".NET 8 SDK installed: $(dotnet --version)"
}

# Make sure private dotnet install is on PATH for this session
if [ -d "$DOTNET_INSTALL_DIR" ]; then
    export DOTNET_ROOT="$DOTNET_INSTALL_DIR"
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
fi

if ! command_exists dotnet; then
    echo "[deps] dotnet not found. Installing .NET 8 SDK..."
    install_dotnet || {
        echo "FATAL: Could not install .NET SDK."
        exit 1
    }
else
    DOTNET_VERSION=$(dotnet --version 2>/dev/null | head -n1)
    echo "[deps] dotnet found: $DOTNET_VERSION"
    if [[ ! "$DOTNET_VERSION" =~ ^8\. ]]; then
        echo "[deps] .NET version is not 8.x. Installing .NET 8 SDK side-by-side..."
        install_dotnet || {
            echo "FATAL: Could not install .NET 8 SDK."
            exit 1
        }
    fi
fi

# -----------------------------------------------------------------------------
# 2. Ensure Avalonia / Skia runtime dependencies
# -----------------------------------------------------------------------------
AVALONIA_DEPS=(
    libicu-dev
    libfontconfig1
    libfreetype6
    libgl1
    libgtk-3-0
    libice6
    libsm6
    libx11-6
    libxcursor1
    libxext6
    libxi6
    libxinerama1
    libxrandr2
    libxss1
    libxt6
    libxxf86vm1
)

if [[ "$DISTRO" == "ubuntu" || "$DISTRO" == "debian" ]]; then
    MISSING_DEPS=()
    for pkg in "${AVALONIA_DEPS[@]}"; do
        if ! apt_package_installed "$pkg"; then
            MISSING_DEPS+=("$pkg")
        fi
    done

    if [ ${#MISSING_DEPS[@]} -gt 0 ]; then
        echo "[deps] Installing missing Avalonia runtime packages: ${MISSING_DEPS[*]}"
        run_sudo apt-get update
        run_sudo apt-get install -y "${MISSING_DEPS[@]}"
    else
        echo "[deps] Avalonia runtime packages OK"
    fi
else
    echo "[deps] Skipping Avalonia package check on $DISTRO (manual install required)"
fi

# -----------------------------------------------------------------------------
# 3. Ensure LibVLC is installed
# -----------------------------------------------------------------------------
if [[ "$DISTRO" == "ubuntu" || "$DISTRO" == "debian" ]]; then
    if ! apt_package_installed libvlc-dev || ! apt_package_installed libvlccore-dev || ! command_exists vlc; then
        echo "[deps] Installing LibVLC..."
        run_sudo apt-get update
        run_sudo apt-get install -y libvlc-dev libvlccore-dev vlc
    else
        echo "[deps] LibVLC OK"
    fi
else
    echo "[deps] Skipping LibVLC package check on $DISTRO (manual install required)"
fi

# Ensure libvlc can be found at runtime
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:/usr/lib/x86_64-linux-gnu"

# -----------------------------------------------------------------------------
# 4. Fix line endings if script came from Windows
# -----------------------------------------------------------------------------
if command_exists dos2unix; then
    dos2unix "$0" >/dev/null 2>&1 || true
fi

# -----------------------------------------------------------------------------
# 5. Copy source to a native Linux build directory
# -----------------------------------------------------------------------------
# VirtualBox shared folders do not handle MSBuild file locking well. Build in a
# local directory to avoid "Access to the path ... is denied" errors.
BUILD_DIR="$HOME/.ccp-linux-build"
echo
echo "[setup] Copying source to local build directory: $BUILD_DIR"
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"
(
    cd "$SCRIPT_DIR"
    tar -c \
        --exclude='bin' \
        --exclude='obj' \
        --exclude='.git' \
        --exclude='logs' \
        . | tar -x -C "$BUILD_DIR"
)
cd "$BUILD_DIR"
echo "[setup] Working from $BUILD_DIR"

# -----------------------------------------------------------------------------
# 6. Build CCP.Core
# -----------------------------------------------------------------------------
echo
echo "[1/4] Building CCP.Core..."
dotnet build CCP.Core/CCP.Core.csproj -c Release

# -----------------------------------------------------------------------------
# 6. Run CCP.Core.Tests
# -----------------------------------------------------------------------------
echo
echo "[2/4] Running CCP.Core.Tests..."
# Note: --no-build is omitted because VSTest on Linux misinterprets absolute
# DLL paths starting with '/' as command-line switches.
dotnet test tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release

# -----------------------------------------------------------------------------
# 7. Build CCP.Avalonia.Desktop.Linux
# -----------------------------------------------------------------------------
echo
echo "[3/4] Building CCP.Avalonia.Desktop.Linux..."
dotnet build CCP.Avalonia.Desktop.Linux/CCP.Avalonia.Desktop.Linux.csproj -c Release

# -----------------------------------------------------------------------------
# 8. Run Avalonia Linux head and capture logs
# -----------------------------------------------------------------------------
echo
echo "[4/4] Running Avalonia Linux head (smoke test)..."
echo "Run log: $RUN_LOG"
echo "The app will run for up to 30 seconds, then close automatically."
echo

# Diagnose the display environment so we can tell if a GUI session is active.
echo "DISPLAY=${DISPLAY:-<not set>}"
if command_exists xset; then
    xset q >/dev/null 2>&1 && echo "X server reachable." || echo "WARNING: X server not reachable (xset q failed)."
elif command_exists xdpyinfo; then
    xdpyinfo >/dev/null 2>&1 && echo "X server reachable." || echo "WARNING: X server not reachable (xdpyinfo failed)."
fi
if [ -z "${DISPLAY:-}" ]; then
    echo "WARNING: DISPLAY environment variable is not set."
    echo "         The Avalonia GUI will not open unless you are running a desktop session."
    echo "         If you are in a terminal-only session, this smoke test is expected to fail."
    echo
fi

# Try software rendering first for VirtualBox compatibility
export AVALONIA_D3D11_DISABLED=1
export LIBGL_ALWAYS_SOFTWARE=1
export AVALONIA_LOG_LEVEL=Debug

APP_DLL="CCP.Avalonia.Desktop.Linux/bin/Release/net8.0/CCP.Desktop.Linux.dll"

if [ ! -f "$APP_DLL" ]; then
    echo "ERROR: Built app DLL not found: $APP_DLL"
    exit 1
fi

EXIT_CODE=0
timeout 30s dotnet "$APP_DLL" \
    > >(tee -a "$RUN_LOG") 2>&1 \
    || EXIT_CODE=$?

# timeout returns 124 when it kills the process successfully
if [[ "$EXIT_CODE" == "0" || "$EXIT_CODE" == "124" ]]; then
    echo
    echo "=== Linux smoke test PASSED ==="
    echo "Main log: $MAIN_LOG"
    echo "Run log:  $RUN_LOG"
else
    echo
    echo "=== Linux smoke test FAILED (exit $EXIT_CODE) ==="
    echo "Main log: $MAIN_LOG"
    echo "Run log:  $RUN_LOG"
    echo "Last 50 lines of run log:"
    tail -n 50 "$RUN_LOG" || true
    exit "$EXIT_CODE"
fi
