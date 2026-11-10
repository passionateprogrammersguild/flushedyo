#!/usr/bin/env bash
# Packs the flushedyo CLI as a .NET tool and installs it.
#
#   ./scripts/install-tool.sh                 # install globally (dotnet tool install -g)
#   ./scripts/install-tool.sh --tool-path DIR # install into DIR instead
#   ./scripts/install-tool.sh --pack-only     # only produce dist/FlushedYo.<version>.nupkg
#
# The package is built from this checkout; nothing is published to nuget.org.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO_ROOT/src/FlushedYo.Cli/FlushedYo.Cli.csproj"
PACKAGE_ID="FlushedYo"
TOOL_COMMAND="flushedyo"
DIST_DIR="${FLUSHEDYO_DIST_DIR:-$REPO_ROOT/dist}"

TOOL_PATH=""
PACK_ONLY=false

usage() {
    sed -n '2,8p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit "${1:-0}"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -p|--tool-path)
            [[ $# -ge 2 ]] || { echo "error: $1 requires a directory" >&2; exit 1; }
            TOOL_PATH="$2"
            shift 2
            ;;
        --pack-only)
            PACK_ONLY=true
            shift
            ;;
        -h|--help)
            usage 0
            ;;
        *)
            echo "error: unknown argument '$1'" >&2
            usage 1
            ;;
    esac
done

command -v dotnet >/dev/null 2>&1 || {
    echo "error: the .NET SDK is required — install .NET 10 from https://dotnet.microsoft.com/download/dotnet/10.0" >&2
    exit 1
}

echo "==> Packing $PACKAGE_ID from $PROJECT"
rm -rf "$DIST_DIR"
dotnet pack "$PROJECT" --configuration Release --output "$DIST_DIR"

VERSION="$(dotnet msbuild "$PROJECT" -getProperty:Version -nologo)"
echo "==> Packaged $PACKAGE_ID $VERSION into $DIST_DIR"

if [[ "$PACK_ONLY" == true ]]; then
    echo "==> Skipping install (--pack-only). Install manually with:"
    echo "    dotnet tool install --global $PACKAGE_ID --add-source \"$DIST_DIR\" --version $VERSION"
    exit 0
fi

# Uninstall first: a repack of the same version would otherwise report "already installed".
if [[ -n "$TOOL_PATH" ]]; then
    dotnet tool uninstall --tool-path "$TOOL_PATH" "$PACKAGE_ID" >/dev/null 2>&1 || true
    echo "==> Installing $PACKAGE_ID $VERSION into $TOOL_PATH"
    dotnet tool install --tool-path "$TOOL_PATH" "$PACKAGE_ID" \
        --add-source "$DIST_DIR" --version "$VERSION" --ignore-failed-sources
    INSTALL_DIR="$TOOL_PATH"
else
    dotnet tool uninstall --global "$PACKAGE_ID" >/dev/null 2>&1 || true
    echo "==> Installing $PACKAGE_ID $VERSION globally"
    dotnet tool install --global "$PACKAGE_ID" \
        --add-source "$DIST_DIR" --version "$VERSION" --ignore-failed-sources
    INSTALL_DIR="$HOME/.dotnet/tools"
fi

echo
echo "==> Installed. Verify with:"
echo "    $INSTALL_DIR/$TOOL_COMMAND plugins list"
echo
echo "Relative PluginDirectory / LedgerPath values resolve against the nearest ancestor of the"
echo "current directory containing flushedyo.slnx, or the current directory if there is none."
echo "For a tool-installed build, point them at real locations:"
echo "    export FLUSHEDYO__PLUGINDIRECTORY=\"\$HOME/.flushedyo/plugins\""
echo "    export FLUSHEDYO__LEDGERPATH=\"\$HOME/.flushedyo/data/flushedyo.db\""
