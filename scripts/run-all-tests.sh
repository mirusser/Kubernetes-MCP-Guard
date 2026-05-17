#!/usr/bin/env bash
set -euo pipefail

# run-all-tests.sh — Run all tests exactly as they are executed in the GitHub CI workflows.
# This script will fail if any test tier fails or if prerequisites (Docker, Kubernetes) are missing.

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

echo "=== Running All Tests (CI Parity) ==="

echo "1. Running Unit Tests..."
dotnet test "$REPO_ROOT/InfraGate.slnx" --filter "Category!=Keycloak" --configuration Release

echo "2. Running Keycloak Integration Tests..."
dotnet test "$REPO_ROOT/tests/InfraGate.McpGateway.KeycloakTests/InfraGate.McpGateway.KeycloakTests.csproj" --filter "Category=Keycloak" --configuration Release

echo "3. Running McpServer Integration Tests..."
INFRA_GATE_RUN_INTEGRATION=1 dotnet test "$REPO_ROOT/tests/InfraGate.McpServer.Tests/InfraGate.McpServer.Tests.csproj" --configuration Release

echo "4. Running Gateway Integration Tests..."
INFRA_GATE_RUN_GATEWAY_INTEGRATION=1 dotnet test "$REPO_ROOT/tests/InfraGate.McpGateway.Tests/InfraGate.McpGateway.Tests.csproj" --filter "Category!=Keycloak" --configuration Release

echo "5. Running Safety E2E Tests..."
INFRA_GATE_RUN_SAFETY_E2E=1 KUBECONFIG="$REPO_ROOT/.kube/mcp-nginx-demo.config" dotnet test "$REPO_ROOT/tests/InfraGate.Safety.E2E.Tests/InfraGate.Safety.E2E.Tests.csproj" --filter "Category=SafetyE2E" --configuration Release

echo "=== All tests completed successfully! ==="