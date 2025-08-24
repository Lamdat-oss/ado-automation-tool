# Quick Start: Running Performance Tests

## Prerequisites

1. **Azure DevOps Access**:
   - Organization URL (e.g., `https://dev.azure.com/YourOrg`)
   - Personal Access Token with work item permissions
   - Test project (recommended: "PCLabs" or dedicated test project)

2. **Development Environment**:
   - .NET 8 SDK
   - Visual Studio or VS Code

## Setup (Choose One Method)

### Option 1: Environment Variables (Quickest)
```bash
# Windows (PowerShell)
$env:AZURE_DEVOPS_URL = "https://dev.azure.com/YourOrganization"
$env:AZURE_DEVOPS_PAT = "your-personal-access-token"
$env:AZURE_DEVOPS_TEST_PROJECT = "PCLabs"

# Linux/Mac
export AZURE_DEVOPS_URL="https://dev.azure.com/YourOrganization"
export AZURE_DEVOPS_PAT="your-personal-access-token"
export AZURE_DEVOPS_TEST_PROJECT="PCLabs"
```

### Option 2: User Secrets (Most Secure)
```bash
cd Tests/Lamdat.ADOAutomationTool.Tests
dotnet user-secrets set "AzureDevOps:Url" "https://dev.azure.com/YourOrganization"
dotnet user-secrets set "AzureDevOps:PersonalAccessToken" "your-personal-access-token"
dotnet user-secrets set "AzureDevOps:TestProject" "PCLabs"
```

## Running Tests

### Interactive PowerShell Script (Recommended)
```powershell
# From solution root directory
.\Tests\Lamdat.ADOAutomationTool.Tests\Performance\RunPerformanceTests.ps1
```

### Direct Command Line
```bash
# Small baseline test (19 work items, ~30 seconds)
dotnet test Tests/Lamdat.ADOAutomationTool.Tests/Lamdat.ADOAutomationTool.Tests.csproj \
  --filter "FullyQualifiedName~SmallHierarchy" \
  --logger "console;verbosity=normal"

# Medium scalability test (88 work items, ~2 minutes)
dotnet test Tests/Lamdat.ADOAutomationTool.Tests/Lamdat.ADOAutomationTool.Tests.csproj \
  --filter "FullyQualifiedName~MediumHierarchy" \
  --logger "console;verbosity=normal"
```

## What Tests Do

1. **Create Real Work Items** in your Azure DevOps project
2. **Execute the Actual Script** (`08-hierarchical-aggregation.rule`)
3. **Measure Performance** (setup time, execution time, throughput)
4. **Verify Results** (aggregation correctness)
5. **Clean Up** (move work items to "Removed" state)

## Sample Output

```
=== SMALL HIERARCHY PERFORMANCE RESULTS ===
Setup Time: 15,234.50ms
Execution Time: 2,856.25ms
Total Test Time: 18,090.75ms
Work Items Created: 19
Avg Time per Work Item: 150.33ms

Performance Status: ? PASSED
Target: < 30,000ms | Actual: 2,856ms | Margin: 90.5%
```

## ?? Important Notes

- **Tests create REAL work items** in your project
- **Use a test project**, never production
- Work items are automatically cleaned up (moved to "Removed" state)
- Failed tests may leave work items - check your project after running

## Troubleshooting

- **Authentication Error**: Check PAT and permissions
- **Project Not Found**: Verify project name and access
- **Timeout**: Script may be processing large amounts of data
- **Test Failures**: Check Azure DevOps service status

For detailed documentation, see `Performance/README.md`