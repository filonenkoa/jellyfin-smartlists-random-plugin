#!/bin/bash

# This script builds the SmartLists plugin on macOS.
# It supports Docker-based deployment (for TrueNAS Scale, etc.) and native macOS Jellyfin.
#
# Usage:
#   ./build-macos.sh              # Build for Docker deployment (default)
#   ./build-macos.sh --native     # Build for native macOS Jellyfin installation
#   ./build-macos.sh --install    # Build and install to native macOS Jellyfin
#
# For TrueNAS Scale Docker deployment:
#   1. Run ./build-macos.sh to build the plugin
#   2. Copy build_output/ to your TrueNAS Scale Docker volume
#   3. Mount it to /config/plugins/SmartLists in your Jellyfin container

set -e # Exit immediately if a command exits with a non-zero status.

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Build configuration
VERSION="10.11.0.0"
BUILD_OUTPUT_DIR="$PROJECT_ROOT/build_output"
PLUGIN_NAME="SmartLists"

# Parse arguments
BUILD_MODE="docker"
INSTALL_AFTER_BUILD=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --native)
            BUILD_MODE="native"
            shift
            ;;
        --install)
            BUILD_MODE="native"
            INSTALL_AFTER_BUILD=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --native     Build for native macOS Jellyfin installation"
            echo "  --install    Build and install to native macOS Jellyfin"
            echo "  --help, -h   Show this help message"
            echo ""
            echo "Default mode builds for Docker-based local testing."
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            echo "Use --help for usage information."
            exit 1
            ;;
    esac
done

# Check prerequisites
check_prerequisites() {
    echo "Checking prerequisites..."

    # Check for .NET SDK
    if ! command -v dotnet &> /dev/null; then
        echo -e "${RED}Error: .NET SDK is not installed.${NC}"
        echo "Please install .NET SDK from: https://dotnet.microsoft.com/download"
        exit 1
    fi

    # Check .NET version
    DOTNET_VERSION=$(dotnet --version 2>/dev/null | cut -d'.' -f1)
    if [[ "$DOTNET_VERSION" -lt 9 ]]; then
        echo -e "${YELLOW}Warning: .NET SDK 9.0 or higher is recommended. Found: $(dotnet --version)${NC}"
    fi

    echo -e "${GREEN}✓ .NET SDK found: $(dotnet --version)${NC}"

    # Check for Docker if in docker mode
    if [[ "$BUILD_MODE" == "docker" ]]; then
        if ! command -v docker &> /dev/null; then
            echo -e "${RED}Error: Docker is not installed.${NC}"
            echo "Please install Docker from: https://www.docker.com/products/docker-desktop"
            exit 1
        fi

        if ! docker info &> /dev/null; then
            echo -e "${RED}Error: Docker daemon is not running.${NC}"
            echo "Please start Docker Desktop."
            exit 1
        fi

        echo -e "${GREEN}✓ Docker is running${NC}"
    fi
}

# Build the plugin
build_plugin() {
    echo ""
    echo "========================================"
    echo "Building SmartLists plugin..."
    echo "Version: $VERSION"
    echo "Mode: $BUILD_MODE"
    echo "========================================"
    echo ""

    # Clean previous build
    echo "Cleaning previous build..."
    rm -rf "$BUILD_OUTPUT_DIR"
    mkdir -p "$BUILD_OUTPUT_DIR"

    # Build the project
    echo "Building project..."
    dotnet build "$PROJECT_ROOT/Jellyfin.Plugin.SmartLists/Jellyfin.Plugin.SmartLists.csproj" \
        --configuration Release \
        --output "$BUILD_OUTPUT_DIR" \
        /p:Version="$VERSION" \
        /p:AssemblyVersion="$VERSION"

    if [[ $? -ne 0 ]]; then
        echo -e "${RED}Error: Build failed.${NC}"
        exit 1
    fi

    # Copy required files
    echo "Copying plugin metadata..."
    cp "$SCRIPT_DIR/meta-dev.json" "$BUILD_OUTPUT_DIR/meta.json"
    cp "$PROJECT_ROOT/images/logo.jpg" "$BUILD_OUTPUT_DIR/logo.jpg"

    # Create Configuration directory
    mkdir -p "$BUILD_OUTPUT_DIR/Configuration"

    echo -e "${GREEN}✓ Build completed successfully${NC}"
}

# Setup Docker environment
setup_docker() {
    echo ""
    echo "Setting up Docker environment..."

    # Ensure jellyfin-data directory exists
    mkdir -p "$SCRIPT_DIR/jellyfin-data/config/config"

    # Copy logging configuration for debug logs
    if [[ -f "$SCRIPT_DIR/logging.json" ]]; then
        cp "$SCRIPT_DIR/logging.json" "$SCRIPT_DIR/jellyfin-data/config/config/logging.json"
    fi

    echo -e "${GREEN}✓ Docker environment ready${NC}"
}

# Start Docker container
start_docker() {
    echo ""
    echo "========================================"
    echo "Starting Jellyfin Docker container..."
    echo "========================================"
    echo ""

    cd "$SCRIPT_DIR"

    # Stop existing container if running
    docker compose down 2>/dev/null || true

    # Remove stopped containers
    docker container prune -f &> /dev/null || true

    # Start the container
    docker compose up --detach

    if [[ $? -ne 0 ]]; then
        echo -e "${RED}Error: Failed to start Docker container.${NC}"
        exit 1
    fi

    echo ""
    echo -e "${GREEN}✓ Jellyfin container is starting...${NC}"
    echo ""
    echo "Jellyfin will be available at: http://localhost:8096"
    echo ""
    echo "Plugin files are mounted at: $BUILD_OUTPUT_DIR"
    echo "Logs are available at: $SCRIPT_DIR/jellyfin-data/config/log/"
}

# Get native Jellyfin plugin directory
get_native_plugin_dir() {
    # Check common macOS Jellyfin plugin locations
    local locations=(
        "$HOME/.local/share/jellyfin/plugins"
        "/usr/local/share/jellyfin/plugins"
        "/opt/jellyfin/plugins"
    )

    for location in "${locations[@]}"; do
        if [[ -d "$location" ]]; then
            echo "$location"
            return 0
        fi
    done

    # Default to user directory
    echo "$HOME/.local/share/jellyfin/plugins"
    return 0
}

# Install to native macOS Jellyfin
install_native() {
    echo ""
    echo "========================================"
    echo "Installing to native macOS Jellyfin..."
    echo "========================================"
    echo ""

    local plugin_dir
    plugin_dir=$(get_native_plugin_dir)
    local target_dir="$plugin_dir/$PLUGIN_NAME"

    echo "Plugin directory: $target_dir"

    # Create plugin directory if it doesn't exist
    mkdir -p "$target_dir"

    # Remove old plugin files
    echo "Removing old plugin files..."
    rm -rf "$target_dir"

    # Copy new plugin files
    echo "Installing new plugin files..."
    cp -R "$BUILD_OUTPUT_DIR" "$target_dir"

    # Set proper permissions
    chmod -R 755 "$target_dir"

    echo -e "${GREEN}✓ Plugin installed successfully${NC}"
    echo ""
    echo "Installation location: $target_dir"
    echo ""
    echo -e "${YELLOW}Note: Please restart Jellyfin to load the updated plugin.${NC}"

    # Try to detect if Jellyfin is running via common methods
    if pgrep -x "jellyfin" > /dev/null || pgrep -f "Jellyfin" > /dev/null; then
        echo ""
        echo -e "${YELLOW}Jellyfin appears to be running.${NC}"
        echo "To restart Jellyfin:"
        echo "  - If using launchctl: sudo launchctl kickstart -k system/org.jellyfin.server"
        echo "  - If running manually: stop and restart the Jellyfin process"
    fi
}

# Main execution
main() {
    echo "SmartLists Plugin Build Script for macOS"
    echo "========================================"
    echo ""

    # Check prerequisites
    check_prerequisites

    # Build the plugin
    build_plugin

    # Handle based on build mode
    if [[ "$BUILD_MODE" == "docker" ]]; then
        setup_docker
        start_docker
    else
        if [[ "$INSTALL_AFTER_BUILD" == true ]]; then
            install_native
        else
            echo ""
            echo "========================================"
            echo "Build Output"
            echo "========================================"
            echo ""
            echo "Plugin built successfully at:"
            echo "  $BUILD_OUTPUT_DIR"
            echo ""
            echo "To install manually, copy the contents to:"
            echo "  $(get_native_plugin_dir)/$PLUGIN_NAME/"
            echo ""
            echo "Or run with --install flag to install automatically."
        fi
    fi

    echo ""
    echo -e "${GREEN}Done!${NC}"
}

# Run main function
main
