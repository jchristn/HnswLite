#!/usr/bin/env bash
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${ROOT}/src"

FAILED=0

banner() {
    echo ""
    echo "============================================================================"
    echo "  $1"
    echo "============================================================================"
}

banner "Test.Automated (console runner)"
dotnet run --project Test.Automated/Test.Automated.csproj -c Release || FAILED=1

banner "Test.XUnit"
dotnet test Test.XUnit/Test.XUnit.csproj -c Release --nologo || FAILED=1

banner "Test.NUnit"
dotnet test Test.NUnit/Test.NUnit.csproj -c Release --nologo || FAILED=1

banner "Test.MSTest"
dotnet test Test.MSTest/Test.MSTest.csproj -c Release --nologo || FAILED=1

if [ "${FAILED}" -ne 0 ]; then
    echo ""
    echo "One or more test runs failed."
    exit 1
fi

echo ""
echo "All test runs passed."
exit 0
