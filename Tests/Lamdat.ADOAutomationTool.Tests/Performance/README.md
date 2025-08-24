# Hierarchical Aggregation Performance Tests

This directory contains performance tests for the hierarchical aggregation script (`08-hierarchical-aggregation.rule`) that run against real Azure DevOps instances to measure actual performance characteristics.

## ?? Important Warnings

**These tests create real work items in your Azure DevOps project!**

- Tests will create hundreds of work items during execution
- Work items are automatically moved to "Removed" state after tests (unless `SkipCleanup` is enabled)
- Failed tests may leave work items in your project
- Always run in a test/development project, never in production

## Prerequisites

### 1. Azure DevOps Setup
- Access to an Azure DevOps organization and project
- Personal Access Token (PAT) with work item read/write permissions
- Preferably a dedicated test project (recommended: use "PCLabs" or similar)

### 2. Permissions Required
- Create work items
- Update work items  
- Create work item links
- Query work items

### 3. .NET Requirements
- .NET 8 SDK installed
- All project dependencies restored

## Configuration

### Method 1: Environment Variables
```bash
# Set these environment variables before running tests
export AZURE_DEVOPS_URL="https://dev.azure.com/YourOrganization"
export AZURE_DEVOPS_PAT="your-personal-access-token"
export AZURE_DEVOPS_TEST_PROJECT="PCLabs"
export SKIP_CLEANUP="false"
```

### Method 2: Configuration File
Create or update `appsettings.Performance.json`:
```json
{
  "AzureDevOps": {
    "Url": "https://dev.azure.com/YourOrganization",
    "PersonalAccessToken": "your-pat-token-here",
    "TestProject": "PCLabs"
  },
  "PerformanceTests": {
    "SkipCleanup": false
  }
}
```

### Method 3: User Secrets (Recommended for tokens)
```bash
cd Tests/Lamdat.ADOAutomationTool.Tests
dotnet user-secrets set "AzureDevOps:PersonalAccessToken" "your-pat-token-here"
dotnet user-secrets set "AzureDevOps:Url" "https://dev.azure.com/YourOrganization"
```

## Running Performance Tests

### Option 1: PowerShell Script (Recommended)
```powershell
# Run the interactive script
.\Tests\Lamdat.ADOAutomationTool.Tests\Performance\RunPerformanceTests.ps1

# Run specific test
.\Tests\Lamdat.ADOAutomationTool.Tests\Performance\RunPerformanceTests.ps1 -TestFilter "*SmallHierarchy*"

# Run all tests
.\Tests\Lamdat.ADOAutomationTool.Tests\Performance\RunPerformanceTests.ps1 -RunAll

# Skip cleanup (leaves work items in project)
.\Tests\Lamdat.ADOAutomationTool.Tests\Performance\RunPerformanceTests.ps1 -SkipCleanup
```

### Option 2: Direct dotnet test
```bash
# Build first
dotnet build Tests/Lamdat.ADOAutomationTool.Tests/Lamdat.ADOAutomationTool.Tests.csproj

# Run specific performance test
dotnet test Tests/Lamdat.ADOAutomationTool.Tests/Lamdat.ADOAutomationTool.Tests.csproj \
  --filter "FullyQualifiedName~SmallHierarchy" \
  --logger "console;verbosity=normal"

# Run all performance tests
dotnet test Tests/Lamdat.ADOAutomationTool.Tests/Lamdat.ADOAutomationTool.Tests.csproj \
  --filter "Category=Performance" \
  --logger "console;verbosity=normal"
```

## Test Scenarios

### 1. Small Hierarchy Baseline (`SmallHierarchy`)
**Purpose**: Establish baseline performance metrics

**Test Data**:
- 1 Epic ? 2 Features ? 4 PBIs ? 12 Tasks
- Total: 19 work items
- Expected execution time: < 30 seconds

**Use Case**: Regular small project aggregation

### 2. Medium Hierarchy Scalability (`MediumHierarchy`)
**Purpose**: Test scalability with moderate data volume

**Test Data**:
- 2 Epics ? 6 Features ? 20 PBIs ? 60 Tasks  
- Total: 88 work items
- Expected execution time: < 2 minutes

**Use Case**: Typical project size aggregation

### 3. Large Hierarchy Stress Test (`LargeHierarchy`)
**Purpose**: Stress test with large hierarchy

**Test Data**:
- 5 Epics ? 15 Features ? 75 PBIs ? 300 Tasks
- Total: 395 work items
- Expected execution time: < 10 minutes

**Use Case**: Large enterprise project aggregation

### 4. No Changes Optimization (`NoChanges`)
**Purpose**: Verify early exit optimization

**Test Data**:
- Small hierarchy with old change dates
- Expected execution time: < 10 seconds

**Use Case**: Scheduled runs with no recent activity

### 5. Concurrent Execution Safety (`ConcurrentExecution`)
**Purpose**: Test concurrent script execution safety

**Test Data**:
- Small hierarchy with 3 concurrent executions
- Expected execution time: < 2 minutes

**Use Case**: Multiple automation systems or manual triggers

## Performance Metrics

Each test measures and reports:

### Timing Metrics
- **Setup Time**: Time to create test work items
- **Execution Time**: Time for script to complete aggregation
- **Total Test Time**: Setup + Execution time
- **Avg Time per Work Item**: Execution time ÷ work item count

### Throughput Metrics
- **Work Items Created**: Total test work items
- **Work Items Processed**: Work items updated by script
- **WIQL Queries Executed**: Database query count
- **API Calls Made**: Azure DevOps REST API calls

### Success Metrics
- **Script Success Rate**: Percentage of successful executions
- **Data Integrity**: Verification that aggregation results are correct
- **Error Rate**: Percentage of failed operations

## Performance Expectations

### Baseline Targets
| Test Scenario | Work Items | Target Time | Acceptable Time |
|---------------|------------|-------------|-----------------|
| Small Hierarchy | 19 | < 15 seconds | < 30 seconds |
| Medium Hierarchy | 88 | < 1 minute | < 2 minutes |
| Large Hierarchy | 395 | < 5 minutes | < 10 minutes |
| No Changes | N/A | < 5 seconds | < 10 seconds |
| Concurrent (3x) | 19 | < 30 seconds | < 2 minutes |

### Performance Factors
- **Azure DevOps Response Time**: API latency varies by region/load
- **Network Latency**: Connection speed to Azure DevOps
- **Work Item Complexity**: Number of fields and relationships
- **Query Complexity**: WIQL query performance
- **Batching Efficiency**: Script's batching strategy effectiveness

## Troubleshooting

### Common Issues

#### Authentication Errors
```
Error: Azure DevOps PAT not configured
```
**Solution**: Verify PAT is set in environment variables or configuration

#### Permission Errors
```
Error: Access denied creating work items
```
**Solution**: Ensure PAT has "Work Items (Read & Write)" permissions

#### Timeout Errors
```
Error: Script execution timed out
```
**Solution**: Increase timeout or check Azure DevOps service status

#### Performance Issues
```
Warning: Execution time exceeded expectations
```
**Solutions**:
- Check Azure DevOps service health
- Verify network connectivity
- Run during off-peak hours
- Check for Azure DevOps API rate limiting

### Cleanup Issues

#### Manual Cleanup Required
If tests fail and leave work items:

1. **Query for test work items**:
   ```
   SELECT [System.Id], [System.Title]
   FROM WorkItems 
   WHERE [System.Title] CONTAINS "Performance Test"
   AND [System.TeamProject] = "PCLabs"
   ```

2. **Bulk update to Removed state**:
   - Use Azure DevOps web interface
   - Or create cleanup script with Azure DevOps REST API

3. **Permanently delete** (if supported by project settings)

## Best Practices

### Running Performance Tests

1. **Use Dedicated Test Project**: Never run in production
2. **Off-Peak Hours**: Run during low Azure DevOps usage
3. **Baseline Multiple Runs**: Run tests 3-5 times for consistent results
4. **Monitor Azure DevOps**: Watch for API rate limiting
5. **Document Results**: Track performance trends over time

### Test Data Management

1. **Enable Cleanup**: Unless specifically testing cleanup failure
2. **Verify Cleanup**: Check project after tests complete
3. **Limit Concurrency**: Don't run multiple performance test sessions
4. **Monitor Storage**: Large tests consume project storage quota

### Performance Analysis

1. **Compare Baselines**: Track performance regression
2. **Identify Bottlenecks**: Use detailed logging to find slow operations
3. **Test Variations**: Different hierarchy structures, activity distributions
4. **Load Testing**: Gradually increase test data size

## Example Performance Report

```
=== MEDIUM HIERARCHY PERFORMANCE RESULTS ===
Setup Time: 45,234.50ms
Execution Time: 12,856.25ms
Total Test Time: 58,090.75ms
Work Items Created: 88
Avg Time per Work Item: 146.09ms

Script Performance Metrics:
- Tasks Found: 60
- PBIs Updated: 20
- Features Updated: 6
- Epics Updated: 2
- WIQL Queries: 15
- API Calls: 94
- Errors: 0

Performance Status: ? PASSED
Target: < 120,000ms | Actual: 12,856ms | Margin: 89.3%
```

## Contributing

When adding new performance tests:

1. **Follow Naming Convention**: `HierarchicalAggregation_[Scenario]_[Purpose]`
2. **Include Cleanup**: Always clean up test data
3. **Add Timeout**: Use reasonable timeout values
4. **Log Metrics**: Provide detailed performance logging
5. **Document Expectations**: Update this README with new test details
6. **Test Multiple Sizes**: Consider various data volumes

## Security Notes

- **PAT Security**: Never commit PATs to source control
- **Test Project**: Use dedicated test projects
- **Cleanup Verification**: Always verify test data removal
- **Access Control**: Limit PAT permissions to minimum required
- **Environment Isolation**: Separate test and production environments