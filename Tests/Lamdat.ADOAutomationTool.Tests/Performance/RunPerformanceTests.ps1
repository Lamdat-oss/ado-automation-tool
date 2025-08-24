# Performance Test Runner for Hierarchical Aggregation
# This script helps run performance tests against real Azure DevOps

param(
    [Parameter(Mandatory=$false)]
    [string]$AzureDevOpsUrl = "",
    
    [Parameter(Mandatory=$false)]
    [string]$PersonalAccessToken = "",
    
    [Parameter(Mandatory=$false)]
    [string]$TestProject = "PCLabs",
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipCleanup = $false,
    
    [Parameter(Mandatory=$false)]
    [string]$TestFilter = "*Performance*",
    
    [Parameter(Mandatory=$false)]
    [switch]$RunAll = $false
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Hierarchical Aggregation Performance Tests" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if we're in the right directory
$testProjectPath = "Tests\Lamdat.ADOAutomationTool.Tests\Lamdat.ADOAutomationTool.Tests.csproj"
if (-not (Test-Path $testProjectPath)) {
    Write-Error "Test project not found at $testProjectPath. Please run this script from the solution root directory."
    exit 1
}

# Get Azure DevOps configuration
if ([string]::IsNullOrEmpty($AzureDevOpsUrl)) {
    $AzureDevOpsUrl = $env:AZURE_DEVOPS_URL
    if ([string]::IsNullOrEmpty($AzureDevOpsUrl)) {
        $AzureDevOpsUrl = Read-Host "Enter Azure DevOps URL (e.g., https://dev.azure.com/YourOrg)"
    }
}

if ([string]::IsNullOrEmpty($PersonalAccessToken)) {
    $PersonalAccessToken = $env:AZURE_DEVOPS_PAT
    if ([string]::IsNullOrEmpty($PersonalAccessToken)) {
        $PersonalAccessToken = Read-Host "Enter Azure DevOps Personal Access Token" -AsSecureString
        $PersonalAccessToken = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($PersonalAccessToken))
    }
}

# Set environment variables for the test run
$env:AZURE_DEVOPS_URL = $AzureDevOpsUrl
$env:AZURE_DEVOPS_PAT = $PersonalAccessToken
$env:AZURE_DEVOPS_TEST_PROJECT = $TestProject
$env:SKIP_CLEANUP = $SkipCleanup.ToString()

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Azure DevOps URL: $AzureDevOpsUrl" -ForegroundColor Gray
Write-Host "  Test Project: $TestProject" -ForegroundColor Gray
Write-Host "  Skip Cleanup: $SkipCleanup" -ForegroundColor Gray
Write-Host "  Test Filter: $TestFilter" -ForegroundColor Gray

# Warning about test impact
Write-Host ""
Write-Host "WARNING: Performance tests will create real work items in Azure DevOps!" -ForegroundColor Red
Write-Host "This may impact your project if cleanup fails." -ForegroundColor Red
if (-not $SkipCleanup) {
    Write-Host "Work items will be moved to 'Removed' state after tests." -ForegroundColor Yellow
} else {
    Write-Host "Cleanup is DISABLED - work items will remain in the project!" -ForegroundColor Red
}

$confirm = Read-Host "Continue? (y/N)"
if ($confirm -ne "y" -and $confirm -ne "Y") {
    Write-Host "Performance tests cancelled." -ForegroundColor Yellow
    exit 0
}

# Available performance tests
$performanceTests = @(
    @{
        Name = "SmallHierarchy"
        Description = "Baseline test with 1 Epic -> 2 Features -> 4 PBIs -> 12 Tasks"
        Filter = "*SmallHierarchy*"
    },
    @{
        Name = "MediumHierarchy" 
        Description = "Scalability test with 2 Epics -> 6 Features -> 20 PBIs -> 60 Tasks"
        Filter = "*MediumHierarchy*"
    },
    @{
        Name = "LargeHierarchy"
        Description = "Stress test with 5 Epics -> 15 Features -> 75 PBIs -> 300 Tasks"
        Filter = "*LargeHierarchy*"
    },
    @{
        Name = "NoChanges"
        Description = "Optimization test with old data (should exit quickly)"
        Filter = "*NoChanges*"
    },
    @{
        Name = "ConcurrentExecution"
        Description = "Safety test with concurrent script executions"
        Filter = "*ConcurrentExecution*"
    }
)

if (-not $RunAll) {
    Write-Host ""
    Write-Host "Available Performance Tests:" -ForegroundColor Yellow
    for ($i = 0; $i -lt $performanceTests.Count; $i++) {
        $test = $performanceTests[$i]
        Write-Host "  $($i + 1). $($test.Name) - $($test.Description)" -ForegroundColor Gray
    }
    Write-Host "  A. Run All Tests" -ForegroundColor Gray
    
    $selection = Read-Host "Select test to run (1-$($performanceTests.Count), A for all, or Enter for custom filter)"
    
    if ($selection -eq "A" -or $selection -eq "a") {
        $RunAll = $true
    } elseif ([int]::TryParse($selection, [ref]$null) -and [int]$selection -ge 1 -and [int]$selection -le $performanceTests.Count) {
        $selectedTest = $performanceTests[[int]$selection - 1]
        $TestFilter = $selectedTest.Filter
        Write-Host "Running: $($selectedTest.Name)" -ForegroundColor Green
    } elseif (-not [string]::IsNullOrEmpty($selection)) {
        $TestFilter = "*$selection*"
        Write-Host "Using custom filter: $TestFilter" -ForegroundColor Green
    }
}

try {
    Write-Host ""
    Write-Host "Building test project..." -ForegroundColor Yellow
    dotnet build $testProjectPath --configuration Release --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }

    Write-Host "Starting performance tests..." -ForegroundColor Yellow
    Write-Host ""

    if ($RunAll) {
        # Run all performance tests
        foreach ($test in $performanceTests) {
            Write-Host "Running $($test.Name)..." -ForegroundColor Cyan
            dotnet test $testProjectPath --configuration Release --filter "FullyQualifiedName~$($test.Filter)" --logger "console;verbosity=normal" --no-build
            Write-Host ""
        }
    } else {
        # Run specific test filter
        dotnet test $testProjectPath --configuration Release --filter "FullyQualifiedName~$TestFilter" --logger "console;verbosity=normal" --no-build
    }

    Write-Host "Performance tests completed!" -ForegroundColor Green
    
} catch {
    Write-Error "Performance test execution failed: $_"
    exit 1
} finally {
    # Clear sensitive environment variables
    Remove-Item Env:AZURE_DEVOPS_PAT -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Performance Test Notes:" -ForegroundColor Yellow
Write-Host "- Review console output for timing information" -ForegroundColor Gray
Write-Host "- Check Azure DevOps project for any remaining test work items" -ForegroundColor Gray
Write-Host "- Consider running tests multiple times for consistent results" -ForegroundColor Gray
Write-Host "- Monitor Azure DevOps API rate limits during large tests" -ForegroundColor Gray