#!/usr/bin/env bash

# Exit immediately if any command exits with a non-zero status
set -e

echo "==> Cleaning old test results and coverage reports..."
rm -rf TestResults/
rm -rf coveragereport/

echo "==> Building solution/projects..."
dotnet build --no-incremental

echo "==> Running xUnit tests with native code coverage..."
dotnet test --coverage --coverage-output-format cobertura

echo "==> Locating generated coverage file..."
# MTP outputs the cobertura XML into the TestResults directory
COVERAGE_FILE=$(find . -path "*/TestResults/*.cobertura.xml" | head -n 1)

if [ -z "$COVERAGE_FILE" ]; then
    echo "Error: coverage XML file could not be found!" >&2
    exit 1
fi

echo "Found coverage file at: $COVERAGE_FILE"

echo "==> Verifying ReportGenerator tool availability..."
if ! command -v reportgenerator &> /dev/null; then
    echo "Error: 'reportgenerator' global tool is not installed." >&2
    echo "Install it by running: dotnet tool install -g dotnet-reportgenerator-globaltool" >&2
    exit 1
fi

echo "==> Generating HTML report..."
reportgenerator -reports:"$COVERAGE_FILE" -targetdir:"coveragereport" -reporttypes:Html -filefilters:"-**/Models/**;-**/obj/*"

echo "==> Success! HTML coverage report generated at: coveragereport/index.html"