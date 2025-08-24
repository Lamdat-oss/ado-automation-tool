# Hierarchical Aggregation Functional Tests

This document describes the comprehensive functional test suite for the hierarchical work item aggregation script (`08-hierarchical-aggregation.rule`).

## Overview

The `HierarchicalAggregationFunctionalTests` class provides end-to-end testing of the hierarchical aggregation script, covering both:

1. **Bottom-up aggregation**: Task completed work ? PBI/Bug/Glitch ? Feature ? Epic
2. **Top-down aggregation**: Feature estimates/remaining work ? Epic

## Test Scenarios

### 1. `HierarchicalAggregation_BottomUp_TaskHours_ShouldAggregateToParents`

**Purpose**: Tests basic bottom-up aggregation through the complete hierarchy.

**Test Setup**:
- Creates: Epic ? Feature ? PBI ? 2 Tasks
- Task 1: 8 hours of "Development" activity
- Task 2: 4 hours of "Testing" activity
- Both tasks have recent change dates

**Verifications**:
- PBI gets 12 total hours (8 + 4)
- PBI gets 8 development hours, 4 QA hours
- Feature inherits PBI's aggregated values
- Epic inherits Feature's aggregated values
- All discipline breakdowns are correct

### 2. `HierarchicalAggregation_BottomUp_MultiplePBIs_ShouldAggregateCorrectly`

**Purpose**: Tests complex scenarios with multiple PBIs, bugs, and tasks.

**Test Setup**:
- Creates: Epic ? Feature ? (PBI1, PBI2, Bug1)
- PBI1 has 2 tasks: 10h Development + 3h Testing = 13h total
- PBI2 has 1 task: 6h Development
- Bug1 has 1 task: 2h Development
- Expected Feature total: 21h (13 + 6 + 2)

**Verifications**:
- Feature aggregates correctly from all children: 21h total
- Epic inherits Feature's values: 21h total
- Discipline breakdown: 18h Development (10+6+2), 3h QA

### 3. `HierarchicalAggregation_TopDown_FeatureEstimates_ShouldAggregateToEpic`

**Purpose**: Tests top-down aggregation of estimation and remaining work from Features to Epic.

**Test Setup**:
- Creates: Epic ? (Feature1, Feature2)
- Feature1: 40h effort, 30h remaining (with discipline breakdowns)
- Feature2: 60h effort, 50h remaining (with discipline breakdowns)

**Verifications**:
- Epic gets 100h total effort (40 + 60)
- Epic gets 80h total remaining (30 + 50)
- All discipline breakdowns aggregate correctly
- Standard Azure DevOps fields are updated

### 4. `HierarchicalAggregation_CombinedScenario_ShouldHandleBothDirections`

**Purpose**: Tests both bottom-up and top-down aggregation in the same hierarchy.

**Test Setup**:
- Epic ? (Feature1 with completed tasks, Feature2 with estimates)
- Feature1 ? PBI ? Tasks with completed work
- Feature2 has estimation data

**Verifications**:
- Epic shows completed work from bottom-up aggregation
- Epic shows estimates from top-down aggregation
- Both aggregation types work independently and correctly

### 5. `HierarchicalAggregation_NoChanges_ShouldExitEarly`

**Purpose**: Tests that script exits early when no recent changes are found.

**Test Setup**:
- Creates hierarchy with tasks that have old change dates (5 days ago)
- Tasks have completed work but won't be picked up due to date filter

**Verifications**:
- Script logs "No tasks or features with changes found"
- Script returns "No aggregation needed" message
- No work items are updated
- Script exits with 10-minute interval

### 6. `HierarchicalAggregation_DisciplineMapping_ShouldMapActivitiesCorrectly`

**Purpose**: Tests the activity-to-discipline mapping functionality.

**Test Setup**:
- Creates PBI with 6 tasks having different activities:
  - "Development" ? Development discipline (8h)
  - "Testing" ? QA discipline (4h)
  - "Design" ? PO discipline (2h)
  - "Admin Configuration" ? Admin discipline (1h)
  - "Ceremonies" ? Others discipline (3h)
  - "Unknown Activity" ? Others discipline (2h)

**Verifications**:
- Total: 20h
- Development: 8h
- QA: 4h
- PO: 2h
- Admin: 1h
- Others: 5h (3h + 2h)

## Test Architecture

### Base Class
- Inherits from `ScheduledScriptTestBase`
- Uses `MockAzureDevOpsClient` for Azure DevOps simulation
- Executes the actual script file using `ExecuteScriptFromFileAsync()`

### Work Item Creation
- Creates hierarchical relationships using `WorkItemRelation` objects
- Sets all work items to "PCLabs" project (as required by the script)
- Uses recent change dates to ensure work items are picked up by date filters

### Assertions
- Verifies script execution success and proper interval handling
- Checks log messages for expected processing steps
- Validates aggregated field values on updated work items
- Confirms both standard Azure DevOps fields and custom fields

## Key Features Tested

### Bottom-up Aggregation
- ? Task completed work aggregation to PBI/Bug/Glitch
- ? PBI/Bug/Glitch aggregation to Feature
- ? Feature aggregation to Epic
- ? Multi-level hierarchy processing
- ? Discipline-based work categorization

### Top-down Aggregation
- ? Feature estimation aggregation to Epic
- ? Feature remaining work aggregation to Epic
- ? Discipline-specific estimation breakdowns
- ? Standard Azure DevOps field updates

### Script Behavior
- ? Date-based filtering (only recent changes)
- ? Project filtering (PCLabs only)
- ? Early exit when no changes found
- ? Proper interval management (10 minutes)
- ? Error handling and logging

### Activity Mapping
- ? Development activities ? Development discipline
- ? Testing activities ? QA discipline
- ? Design activities ? PO discipline
- ? Admin activities ? Admin discipline
- ? Other/unknown activities ? Others discipline

## Running the Tests

### Individual Test
```bash
dotnet test --filter "FullyQualifiedName~HierarchicalAggregation_BottomUp_TaskHours_ShouldAggregateToParents"
```

### All Functional Tests
```bash
dotnet test --filter "FullyQualifiedName~HierarchicalAggregationFunctionalTests"
```

### All Hierarchical Aggregation Tests
```bash
dotnet test --filter "FullyQualifiedName~HierarchicalAggregation"
```

## Field Mapping Reference

### Standard Azure DevOps Fields
- `Microsoft.VSTS.Scheduling.CompletedWork` - Total completed work
- `Microsoft.VSTS.Scheduling.Effort` - Total effort estimation
- `Microsoft.VSTS.Scheduling.RemainingWork` - Total remaining work

### Custom Discipline Fields
- `Custom.DevelopmentCompletedWork` - Development completed work
- `Custom.QACompletedWork` - QA/Testing completed work
- `Custom.POCompletedWork` - Product Owner completed work
- `Custom.AdminCompletedWork` - Admin/Configuration completed work
- `Custom.OthersCompletedWork` - Other activities completed work

### Custom Estimation Fields
- `Custom.TotalEffortEstimation` - Total effort estimation
- `Custom.DevelopmentEffortEstimation` - Development effort estimation
- `Custom.QAEffortEstimation` - QA effort estimation
- `Custom.POEffortEstimation` - PO effort estimation
- `Custom.AdminEffortEstimation` - Admin effort estimation
- `Custom.OthersEffortEstimation` - Others effort estimation

### Custom Remaining Work Fields
- `Custom.DevelopmentRemainingWork` - Development remaining work
- `Custom.QARemainingWork` - QA remaining work
- `Custom.PORemainingWork` - PO remaining work
- `Custom.AdminRemainingWork` - Admin remaining work
- `Custom.OthersRemainingWork` - Others remaining work

## Test Data Patterns

### Hierarchy Creation
```csharp
// Create work items
var epic = CreateTestWorkItem("Epic", "Test Epic", "Active");
var feature = CreateTestWorkItem("Feature", "Test Feature", "Active");
var pbi = CreateTestWorkItem("Product Backlog Item", "Test PBI", "Active");
var task = CreateTestWorkItem("Task", "Test Task", "Done");

// Set project (required)
epic.SetField("System.TeamProject", "PCLabs");
// ... repeat for all work items

// Set up relationships
epic.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = feature.Id });
feature.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = pbi.Id });
pbi.Relations.Add(new WorkItemRelation { RelationType = "Child", RelatedWorkItemId = task.Id });
```

### Task Setup for Bottom-up
```csharp
task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", 8.0);
task.SetField("Microsoft.VSTS.Common.Activity", "Development");
task.SetField("System.ChangedDate", DateTime.Now); // Recent change
```

### Feature Setup for Top-down
```csharp
feature.SetField("Microsoft.VSTS.Scheduling.Effort", 40.0);
feature.SetField("Custom.DevelopmentEffortEstimation", 25.0);
feature.SetField("Custom.QAEffortEstimation", 10.0);
feature.SetField("System.ChangedDate", DateTime.Now); // Recent change
```

This comprehensive test suite ensures the hierarchical aggregation script works correctly in all scenarios and provides confidence in the production deployment.