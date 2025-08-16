# Complete Hierarchical Work Item Aggregation Implementation

## ?? **Overview**

This implementation provides **complete dual aggregation** for Azure DevOps work items through the Epic ? Feature ? PBI/Bug ? Task hierarchy:

1. **Bottom-up Aggregation**: Task completed work ? PBI/Bug ? Feature ? Epic
2. **Top-down Aggregation**: Feature estimation/remaining ? Epic

## ?? **Data Flow Architecture**

```
EPIC (Auto-calculated from Features + All Tasks)
??? Estimation Fields (aggregated from Features)
?   ??? Custom.Estimation.TotalEffortEstimation
?   ??? Custom.Estimation.DevelopmentEffortEstimation  
?   ??? Custom.Estimation.QAEffortEstimation
?   ??? Custom.Estimation.POEffortEstimation
?   ??? Custom.Estimation.AdminEffortEstimation
?   ??? Custom.Estimation.OthersEffortEstimation
??? Remaining Fields (aggregated from Features)
?   ??? Custom.Remaining.TotalRemainingEstimation
?   ??? Custom.Remaining.DevelopmentRemainingEstimation
?   ??? Custom.Remaining.QARemainingEstimation
?   ??? Custom.Remaining.PORemainingEstimation
?   ??? Custom.Remaining.AdminRemainingEstimation
?   ??? Custom.Remaining.OthersRemainingEstimation
??? Completed Work Fields (aggregated from all descendant Tasks)
    ??? Custom.Aggregation.TotalCompletedWork
    ??? Custom.Aggregation.DevelopmentCompletedWork
    ??? Custom.Aggregation.QACompletedWork
    ??? Custom.Aggregation.POCompletedWork
    ??? Custom.Aggregation.AdminCompletedWork
    ??? Custom.Aggregation.OthersCompletedWork

FEATURE (Manual input for Estimation/Remaining + Auto-calculated Completed Work)
??? Estimation Fields (MANUAL INPUT by PO/Team Lead)
?   ??? Custom.Estimation.TotalEffortEstimation
?   ??? Custom.Estimation.DevelopmentEffortEstimation
?   ??? Custom.Estimation.QAEffortEstimation
?   ??? Custom.Estimation.POEffortEstimation
?   ??? Custom.Estimation.AdminEffortEstimation
?   ??? Custom.Estimation.OthersEffortEstimation
??? Remaining Fields (MANUAL INPUT by PO/Team Lead)
?   ??? Custom.Remaining.TotalRemainingEstimation
?   ??? Custom.Remaining.DevelopmentRemainingEstimation
?   ??? Custom.Remaining.QARemainingEstimation
?   ??? Custom.Remaining.PORemainingEstimation
?   ??? Custom.Remaining.AdminRemainingEstimation
?   ??? Custom.Remaining.OthersRemainingEstimation
??? Completed Work Fields (aggregated from child Tasks)
    ??? Custom.Aggregation.TotalCompletedWork
    ??? Custom.Aggregation.DevelopmentCompletedWork
    ??? Custom.Aggregation.QACompletedWork
    ??? Custom.Aggregation.POCompletedWork
    ??? Custom.Aggregation.AdminCompletedWork
    ??? Custom.Aggregation.OthersCompletedWork

PBI/BUG (Auto-calculated from child Tasks)
??? Completed Work Fields (aggregated from child Tasks)
    ??? Custom.Aggregation.TotalCompletedWork
    ??? Custom.Aggregation.DevelopmentCompletedWork
    ??? Custom.Aggregation.QACompletedWork
    ??? Custom.Aggregation.POCompletedWork
    ??? Custom.Aggregation.AdminCompletedWork
    ??? Custom.Aggregation.OthersCompletedWork

TASK (Source data)
??? Microsoft.VSTS.Scheduling.CompletedWork (MANUAL INPUT by developers)
??? Microsoft.VSTS.Common.Activity (MANUAL SELECTION by developers)
```

## ?? **Aggregation Process**

### **Step 1: Bottom-Up Aggregation (Every 10 minutes)**
```
1. Find Tasks with Microsoft.VSTS.Scheduling.CompletedWork changes since LastRun
2. For each changed Task:
   - Map Activity field to discipline (Development/QA/PO/Admin/Others)
   - Find immediate parent (PBI/Bug/Feature)
   - Aggregate completed work by discipline to parent
3. Propagate aggregation up hierarchy (PBI?Feature?Epic)
```

### **Step 2: Top-Down Aggregation (Every 10 minutes)**
```
1. Find Features with estimation/remaining field changes since LastRun
2. For each changed Feature:
   - Find Epic parent(s)
   - Aggregate Feature estimation fields to Epic
   - Aggregate Feature remaining fields to Epic
```

## ?? **Field Requirements by Work Item Type**

### **Epic (Read-Only - Auto-Calculated)**
| Field Category | Field Name | Source | Purpose |
|---------------|------------|---------|---------|
| **Estimation** | `Custom.Estimation.TotalEffortEstimation` | Sum from Features | Total planned effort |
| | `Custom.Estimation.DevelopmentEffortEstimation` | Sum from Features | Development planned effort |
| | `Custom.Estimation.QAEffortEstimation` | Sum from Features | QA planned effort |
| | `Custom.Estimation.POEffortEstimation` | Sum from Features | PO planned effort |
| | `Custom.Estimation.AdminEffortEstimation` | Sum from Features | Admin planned effort |
| | `Custom.Estimation.OthersEffortEstimation` | Sum from Features | Others planned effort |
| **Remaining** | `Custom.Remaining.TotalRemainingEstimation` | Sum from Features | Total work remaining |
| | `Custom.Remaining.DevelopmentRemainingEstimation` | Sum from Features | Development work remaining |
| | `Custom.Remaining.QARemainingEstimation` | Sum from Features | QA work remaining |
| | `Custom.Remaining.PORemainingEstimation` | Sum from Features | PO work remaining |
| | `Custom.Remaining.AdminRemainingEstimation` | Sum from Features | Admin work remaining |
| | `Custom.Remaining.OthersRemainingEstimation` | Sum from Features | Others work remaining |
| **Completed** | `Custom.Aggregation.TotalCompletedWork` | Sum from all Tasks | Total actual work |
| | `Custom.Aggregation.DevelopmentCompletedWork` | Sum from all Tasks | Development actual work |
| | `Custom.Aggregation.QACompletedWork` | Sum from all Tasks | QA actual work |
| | `Custom.Aggregation.POCompletedWork` | Sum from all Tasks | PO actual work |
| | `Custom.Aggregation.AdminCompletedWork` | Sum from all Tasks | Admin actual work |
| | `Custom.Aggregation.OthersCompletedWork` | Sum from all Tasks | Others actual work |

### **Feature (Mixed - Manual Input + Auto-Calculated)**
| Field Category | Field Name | Input Type | Purpose |
|---------------|------------|------------|---------|
| **Estimation** | `Custom.Estimation.*EffortEstimation` | **MANUAL** | Team lead enters planned effort by discipline |
| **Remaining** | `Custom.Remaining.*RemainingEstimation` | **MANUAL** | Team lead updates remaining work by discipline |
| **Completed** | `Custom.Aggregation.*CompletedWork` | **AUTO** | Aggregated from child Tasks |

### **PBI/Bug (Read-Only - Auto-Calculated)**
| Field Category | Field Name | Source | Purpose |
|---------------|------------|---------|---------|
| **Completed** | `Custom.Aggregation.*CompletedWork` | Sum from child Tasks | Actual work completed |

### **Task (Manual Input)**
| Field Name | Input Type | Purpose |
|------------|------------|---------|
| `Microsoft.VSTS.Scheduling.CompletedWork` | **MANUAL** | Developers log actual hours worked |
| `Microsoft.VSTS.Common.Activity` | **MANUAL** | Developers select activity type for discipline mapping |

## ?? **Activity to Discipline Mapping**

| Activity | Discipline | Examples |
|----------|------------|----------|
| **Development** | Development | Code Review, Development, Investigation, Tech Lead, Technical Debts, Data Fix |
| **QA** | QA | Testing, Test Case, Test Cases Approval, Reproduce |
| **PO** | PO | Design, Requirements Meeting |
| **Admin** | Admin | Admin Configuration, Permissions, Triage |
| **Others** | Others | Ceremonies, Demo, DevOps, Management, Project Management, Release Upgrade, Support, Training, UX/UI, General - Personal |

## ? **Configuration**

### **appsettings.json**
```json
{
  "Settings": {
    "ScheduledTaskIntervalMinutes": 1,
    "ScheduledScriptDefaultLastRun": "7"
  }
}
```

### **Script Schedule**
- **Frequency**: Every 10 minutes
- **Triggers**: 
  - Tasks with `Microsoft.VSTS.Scheduling.CompletedWork` changes
  - Features with estimation/remaining field changes
- **Performance**: Incremental processing (only changed items)

## ?? **Business Value**

### **For Product Owners:**
- **Epic-level visibility**: See total scope (estimation), progress (completed), remaining work across all Features
- **Discipline breakdown**: Understand effort distribution across Development, QA, PO, Admin, Others
- **Real-time updates**: Data refreshes within 10 minutes of changes

### **For Team Leads:**
- **Feature planning**: Enter estimation by discipline for accurate Epic rollups
- **Progress tracking**: Update remaining work to reflect current status
- **Automatic aggregation**: Completed work bubbles up automatically from developer time logs

### **For Developers:**
- **Simple time logging**: Just log completed work and select activity on Tasks
- **Automatic categorization**: Activity field maps to discipline for reporting
- **Minimal overhead**: No manual aggregation or complex field updates

### **For Management:**
- **Portfolio view**: Epic-level metrics for scope, progress, and remaining work
- **Resource planning**: Discipline breakdown shows where effort is needed
- **Predictive analytics**: Compare estimation vs actual across disciplines

## ?? **Implementation Status**

? **Completed:**
- Enhanced ScheduledScriptEngine with LastRun functionality
- Dual aggregation script (bottom-up + top-down)
- Complete field specification and mapping
- Comprehensive test coverage
- Documentation and setup guides
- Backward compatibility with existing scripts

? **Ready for Deployment:**
- Script: `08-hierarchical-aggregation.rule`
- Documentation: `AGGREGATION-SETUP.md`
- Tests: `HierarchicalAggregationTests.cs`

## ?? **Deployment Checklist**

1. **? Create Custom Fields** in Azure DevOps for all work item types as specified
2. **? Set Field Permissions** (aggregation fields read-only for users)
3. **? Deploy Script** to `scheduled-scripts` directory
4. **? Configure Service** with 1-minute check interval
5. **? Test Hierarchy** with sample Epic?Feature?PBI?Task structure
6. **? Verify Aggregation** works for both completed work and estimation
7. **? Train Users** on manual field input requirements
8. **? Monitor Logs** for aggregation success/errors

## ?? **Monitoring**

### **Success Indicators:**
- "Found X changed tasks with completed work since last run"
- "Found X changed features since last run" 
- "Tasks processed: X, Features processed: Y"
- "Updated Z work items"

### **Error Indicators:**
- "Error processing parent X"
- "Hierarchical aggregation failed"
- Build/compilation errors in logs

This implementation provides **complete hierarchical aggregation** as requested, enabling full visibility and automatic rollup of effort data across the entire Epic?Feature?PBI/Bug?Task hierarchy! ??