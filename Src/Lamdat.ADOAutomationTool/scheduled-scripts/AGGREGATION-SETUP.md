# Hierarchical Work Item Aggregation Configuration

This document describes the custom fields and setup required for the complete hierarchical work item aggregation feature.

## Overview

The hierarchical aggregation system provides **dual aggregation**:
1. **Bottom-up**: Task completed work aggregated to all parent levels (PBI/Bug ? Feature ? Epic)
2. **Top-down**: Feature estimation/remaining fields aggregated to Epic level

## Hierarchy Structure

```
Epic (receives aggregated estimation/remaining from Features + completed work from all levels)
??? Feature (manual estimation/remaining input + receives completed work from children)
?   ??? Product Backlog Item (PBI) (receives completed work from Tasks)
?   ?   ??? Task (source of completed work + activity)
?   ??? Bug (receives completed work from Tasks)
?       ??? Task (source of completed work + activity)
??? Product Backlog Item (PBI) (receives completed work from Tasks)
    ??? Task (source of completed work + activity)
```

## Required Custom Fields

Add these custom fields to your Azure DevOps project work item types:

### For Feature Work Item Type:

#### Manual Input Fields (entered by users):
- `Custom.Estimation.TotalEffortEstimation` (Decimal) - Total effort estimation
- `Custom.Estimation.DevelopmentEffortEstimation` (Decimal) - Development effort estimation
- `Custom.Estimation.QAEffortEstimation` (Decimal) - QA effort estimation
- `Custom.Estimation.POEffortEstimation` (Decimal) - Product Owner effort estimation
- `Custom.Estimation.AdminEffortEstimation` (Decimal) - Admin effort estimation
- `Custom.Estimation.OthersEffortEstimation` (Decimal) - Others effort estimation

#### Manual Remaining Fields (entered by users):
- `Custom.Remaining.TotalRemainingEstimation` (Decimal) - Total remaining estimation
- `Custom.Remaining.DevelopmentRemainingEstimation` (Decimal) - Development remaining estimation
- `Custom.Remaining.QARemainingEstimation` (Decimal) - QA remaining estimation
- `Custom.Remaining.PORemainingEstimation` (Decimal) - Product Owner remaining estimation
- `Custom.Remaining.AdminRemainingEstimation` (Decimal) - Admin remaining estimation
- `Custom.Remaining.OthersRemainingEstimation` (Decimal) - Others remaining estimation

#### Auto-Calculated Completed Work Fields:
- `Custom.Aggregation.TotalCompletedWork` (Decimal) - Total completed work from child tasks
- `Custom.Aggregation.DevelopmentCompletedWork` (Decimal) - Development completed work
- `Custom.Aggregation.QACompletedWork` (Decimal) - QA completed work
- `Custom.Aggregation.POCompletedWork` (Decimal) - Product Owner completed work
- `Custom.Aggregation.AdminCompletedWork` (Decimal) - Admin completed work
- `Custom.Aggregation.OthersCompletedWork` (Decimal) - Others completed work
- `Custom.Aggregation.LastUpdated` (DateTime) - Last aggregation update timestamp

### For Epic Work Item Type:

#### Auto-Calculated Estimation Fields (aggregated from Features):
- `Custom.Estimation.TotalEffortEstimation` (Decimal) - Total effort estimation from all Features
- `Custom.Estimation.DevelopmentEffortEstimation` (Decimal) - Development effort from all Features
- `Custom.Estimation.QAEffortEstimation` (Decimal) - QA effort from all Features
- `Custom.Estimation.POEffortEstimation` (Decimal) - Product Owner effort from all Features
- `Custom.Estimation.AdminEffortEstimation` (Decimal) - Admin effort from all Features
- `Custom.Estimation.OthersEffortEstimation` (Decimal) - Others effort from all Features

#### Auto-Calculated Remaining Fields (aggregated from Features):
- `Custom.Remaining.TotalRemainingEstimation` (Decimal) - Total remaining from all Features
- `Custom.Remaining.DevelopmentRemainingEstimation` (Decimal) - Development remaining from all Features
- `Custom.Remaining.QARemainingEstimation` (Decimal) - QA remaining from all Features
- `Custom.Remaining.PORemainingEstimation` (Decimal) - Product Owner remaining from all Features
- `Custom.Remaining.AdminRemainingEstimation` (Decimal) - Admin remaining from all Features
- `Custom.Remaining.OthersRemainingEstimation` (Decimal) - Others remaining from all Features

#### Auto-Calculated Completed Work Fields (aggregated from all children):
- `Custom.Aggregation.TotalCompletedWork` (Decimal) - Total completed work from all descendant tasks
- `Custom.Aggregation.DevelopmentCompletedWork` (Decimal) - Development completed work
- `Custom.Aggregation.QACompletedWork` (Decimal) - QA completed work
- `Custom.Aggregation.POCompletedWork` (Decimal) - Product Owner completed work
- `Custom.Aggregation.AdminCompletedWork` (Decimal) - Admin completed work
- `Custom.Aggregation.OthersCompletedWork` (Decimal) - Others completed work
- `Custom.Aggregation.LastUpdated` (DateTime) - Last aggregation update timestamp

### For Product Backlog Item and Bug Work Item Types:

#### Auto-Calculated Completed Work Fields (aggregated from child Tasks):
- `Custom.Aggregation.TotalCompletedWork` (Decimal) - Total completed work from child tasks
- `Custom.Aggregation.DevelopmentCompletedWork` (Decimal) - Development completed work
- `Custom.Aggregation.QACompletedWork` (Decimal) - QA completed work
- `Custom.Aggregation.POCompletedWork` (Decimal) - Product Owner completed work
- `Custom.Aggregation.AdminCompletedWork` (Decimal) - Admin completed work
- `Custom.Aggregation.OthersCompletedWork` (Decimal) - Others completed work
- `Custom.Aggregation.LastUpdated` (DateTime) - Last aggregation update timestamp

## Activity to Discipline Mapping

The system uses the `Microsoft.VSTS.Common.Activity` field to categorize work by discipline:

### Development
- Code Review
- Data Fix
- Development
- Investigation
- Tech Lead
- Technical Debts

### QA
- Reproduce
- Test Case
- Test Cases Approval
- Testing

### Product Owner (PO)
- Design
- Requirements Meeting

### Admin
- Admin Configuration
- Permissions
- Triage

### Others
- Ceremonies
- Demo
- DevOps
- General - Personal
- Management
- Project Management
- Release Upgrade
- Support
- Training
- UX/UI

## Aggregation Flow

### 1. Bottom-Up Aggregation (Completed Work)
```
Task (completed work + activity) 
  ? aggregates to
PBI/Bug (completed work by discipline)
  ? aggregates to  
Feature (completed work by discipline)
  ? aggregates to
Epic (completed work by discipline)
```

### 2. Top-Down Aggregation (Estimation & Remaining)
```
Feature (manual estimation/remaining by discipline)
  ? aggregates to
Epic (estimation/remaining by discipline)
```

### 3. Trigger Conditions
- **Bottom-up**: Any Task with `Microsoft.VSTS.Scheduling.CompletedWork` changes since last run
- **Top-down**: Any Feature with estimation/remaining field changes since last run

## Script Execution Schedule

- **Interval**: Every 10 minutes
- **Triggers**: 
  - Tasks with completed work changes since last run
  - Features with estimation/remaining changes since last run
- **Process**: Dual aggregation (bottom-up + top-down)
- **Performance**: Only processes changed items for efficiency

## Configuration Settings

Update your `appsettings.json`:

```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "1"
  }
}
```

## User Workflow

### For Product Owners/Team Leads:
1. **Create Epic** with high-level goals
2. **Create Features** under Epic
3. **Enter estimation by discipline** on Features:
   - `Custom.Estimation.DevelopmentEffortEstimation`
   - `Custom.Estimation.QAEffortEstimation`
   - `Custom.Estimation.POEffortEstimation`
   - etc.
4. **Update remaining work** on Features as work progresses
5. **View aggregated totals** on Epic automatically

### For Development Teams:
1. **Create PBIs/Bugs** under Features
2. **Create Tasks** under PBIs/Bugs
3. **Set Activity field** on Tasks (Development, Testing, etc.)
4. **Log completed work** on Tasks via `Microsoft.VSTS.Scheduling.CompletedWork`
5. **View aggregated completed work** bubble up through hierarchy

## Installation Steps

1. **Add Custom Fields**: Create all the custom fields listed above for each work item type
2. **Set Field Permissions**: Ensure aggregation fields are read-only for users (auto-calculated)
3. **Deploy Script**: Place `08-hierarchical-aggregation.rule` in the `scheduled-scripts` directory
4. **Configure Service**: Ensure scheduled task service is running with 1-minute check interval
5. **Test**: Create test hierarchy and verify dual aggregation works

## Monitoring and Troubleshooting

### Log Messages to Monitor:
- "Starting hierarchical work item aggregation..."
- "Found X changed tasks with completed work since last run"
- "Found X changed features since last run"
- "Tasks processed: X, Features processed: Y"

### Common Issues:
- **Missing Custom Fields**: Ensure all required fields are created for correct work item types
- **Permission Issues**: Service account needs read/write access to all work item types
- **Relationship Issues**: Verify Epic?Feature?PBI/Bug?Task links are correctly set up
- **Field Mapping**: Ensure Activity field is populated on Tasks for proper discipline categorization

### Performance Considerations:
- Script processes only items that changed since last run
- Dual aggregation processes both Tasks (bottom-up) and Features (top-down) in single run
- Uses efficient WIQL queries to minimize API calls
- Aggregation is incremental, not full recalculation

## Reporting and Visualization

After implementation, you can create:
- **Epic Dashboards**: Show total estimation vs completed work by discipline
- **Feature Progress**: Track estimation, remaining, and completed work
- **Discipline Analysis**: Compare effort distribution across Development, QA, PO, Admin, Others
- **Burndown Charts**: Track remaining work vs completed work over time
- **Velocity Reports**: Measure team velocity by discipline

## Data Flow Example

```
Epic: "Mobile App Enhancement" 
??? Estimation Total: 100 hours (aggregated from Features)
??? Remaining Total: 60 hours (aggregated from Features)  
??? Completed Total: 40 hours (aggregated from all Tasks)
?
??? Feature: "User Authentication" 
?   ??? Estimation: 50 hours (manual input by PO)
?   ??? Remaining: 30 hours (manual update by PO)
?   ??? Completed: 20 hours (aggregated from Tasks)
?   ?
?   ??? PBI: "Login Screen"
?   ?   ??? Completed: 12 hours (aggregated from Tasks)
?   ?   ??? Task: "UI Development" (8 hours, Activity: Development)
?   ?   ??? Task: "Unit Testing" (4 hours, Activity: Testing)
?   ?
?   ??? PBI: "Password Reset"
?       ??? Completed: 8 hours (aggregated from Tasks)
?       ??? Task: "API Development" (8 hours, Activity: Development)
?
??? Feature: "Profile Management"
    ??? Estimation: 50 hours (manual input by PO)
    ??? Remaining: 30 hours (manual update by PO)
    ??? Completed: 20 hours (aggregated from Tasks)
    ??? (similar PBI/Task structure)
```

This provides complete visibility: **Epic shows total scope** (estimation), **current progress** (completed), and **work remaining** (remaining) with **discipline breakdown** at all levels!