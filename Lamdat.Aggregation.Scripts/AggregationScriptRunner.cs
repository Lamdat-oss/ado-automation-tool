using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamdat.Aggregation.Scripts
{
    internal class AggregationScriptRunner
    {

        public static async Task<ScheduledScriptResult> Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken Token, string ScriptRunId, DateTime LastRun)
        {

            // Hierarchical Work Item Aggregation Task
            // This script aggregates effort data through the Epic > Feature > PBI/Bug/Glitch > Task hierarchy
            // 1. Bottom-up: Task completed work aggregated to parents (PBI/Bug/Glitch/Feature/Epic)
            // 2. Top-down: Feature estimation/remaining fields aggregated to Epic
            // Runs every 10 minutes to process work items that have changed since last run
            //
            // NOTE: This script is specifically designed for PCLabs Ltd and sets Client.Project = "PCLabs"
            // For other customers, either modify this script or create a separate version

            Logger.Information("Starting hierarchical work item aggregation...");
            Logger.Information($"Processing changes since: {LastRun:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // Set the project to PCLabs for all operations
                // This script is specifically designed for PCLabs Ltd
                Client.Project = "PCLabs";

                var user = await Client.WhoAmI();
                Logger.Information($"Aggregation running as: {user?.Identity?.DisplayName}");
                Logger.Information($"Working with project: {Client.Project}");

                // Define discipline mappings based on Activity field
                var disciplineMappings = new Dictionary<string, string>
    {
        {"Admin Configuration", "Admin"},
        {"Ceremonies", "Others"},
        {"Code Review", "Development"},
        {"Data Fix", "Development"},
        {"Demo", "Others"},
        {"Design", "PO"},
        {"Development", "Development"},
        {"DevOps", "Others"},
        {"General - Personal", "Others"},
        {"Investigation", "Development"},
        {"Management", "Others"},
        {"Permissions", "Admin"},
        {"Project Management", "Others"},
        {"Release Upgrade", "Others"},
        {"Reproduce", "QA"},
        {"Requirements Meeting", "PO"},
        {"Support", "Others"},
        {"Tech Lead", "Development"},
        {"Technical Debts", "Development"},
        {"Test Case", "QA"},
        {"Test Cases Approval", "QA"},
        {"Testing", "QA"},
        {"Training", "Others"},
        {"Triage", "Admin"},
        {"UX/UI", "Others"}
    };

                // Step 1: Find all tasks that have changed since last run (for bottom-up aggregation)
                // Fix: Use proper WIQL date format (date only, no time) and > 0 for numeric field instead of IS NOT EMPTY
                var sinceLastRun = LastRun.ToString("yyyy-MM-dd");
                var changedTasksQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], [Microsoft.VSTS.Scheduling.CompletedWork], [Microsoft.VSTS.Common.Activity]
                              FROM WorkItems 
                              WHERE [System.WorkItemType] = 'Task' 
                              AND [System.TeamProject] = 'PCLabs'
                              AND [System.ChangedDate] >= '{sinceLastRun}' 
                              ORDER BY [System.ChangedDate]";

                var changedTasks = await Client.QueryWorkItemsByWiql(changedTasksQuery);
                Logger.Information($"Found {changedTasks.Count} changed tasks with completed work since last run");

                // Step 2: Find all features that have changed since last run (for top-down aggregation)
                var changedFeaturesQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType]
                                 FROM WorkItems 
                                 WHERE [System.WorkItemType] = 'Feature' 
                                 AND [System.TeamProject] = 'PCLabs'
                                 AND [System.ChangedDate] >= '{sinceLastRun}' 
                                 ORDER BY [System.ChangedDate]";

                var changedFeatures = await Client.QueryWorkItemsByWiql(changedFeaturesQuery);
                Logger.Information($"Found {changedFeatures.Count} changed features since last run");

                if (changedTasks.Count == 0 && changedFeatures.Count == 0)
                {
                    Logger.Information("No tasks or features with changes found - no aggregation needed");
                    return ScheduledScriptResult.Success(10, "No aggregation needed - next check in 10 minutes");
                }

                // Initialize aggregation statistics
                var aggregationStats = new Dictionary<string, int>
                {
                    ["TasksProcessed"] = 0,
                    ["FeaturesProcessed"] = 0,
                    ["PBIsUpdated"] = 0,
                    ["BugsUpdated"] = 0,
                    ["GlitchesUpdated"] = 0,
                    ["FeaturesUpdated"] = 0,
                    ["EpicsUpdated"] = 0,
                    ["Errors"] = 0
                };

                // Step 3: Process bottom-up aggregation (Tasks → Parents)
                if (changedTasks.Count > 0)
                {
                    //await ProcessBottomUpAggregation(changedTasks, disciplineMappings, aggregationStats, Client);

                    // Step 3.1: Process multi-level completed work aggregation (PBI/Bug → Feature → Epic)
                    await ProcessMultiLevelCompletedWorkAggregation(changedTasks, disciplineMappings, aggregationStats, Client);
                }

                // Step 4: Process top-down aggregation (Features → Epics)
                if (changedFeatures.Count > 0)
                {
                    await ProcessTopDownAggregation(changedFeatures, aggregationStats, Client);
                }

                // Step 5: Process any additional Epic aggregations needed from changed Features
                var affectedEpics = new HashSet<int>();
                
                if (changedFeatures.Count > 0)
                {
                    Logger.Information($"Finding Epic parents for changed features using batched queries");
                    
                    // Process features in batches of 50 to avoid query length limits
                    const int batchSize = 50;
                    var featureBatches = changedFeatures.Select((feature, index) => new { feature, index })
                                                       .GroupBy(x => x.index / batchSize)
                                                       .Select(g => g.Select(x => x.feature).ToList())
                                                       .ToList();

                    foreach (var featureBatch in featureBatches)
                    {
                        // Create IN clause for batch of feature IDs
                        var featureIds = string.Join(",", featureBatch.Select(f => f.Id));
                        
                        // Get Epic parents for this batch of Features using batched WIQL query
                        var batchEpicParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                              FROM WorkItemLinks
                              WHERE [Source].[System.Id] IN ({featureIds})
                              AND [Source].[System.TeamProject] = 'PCLabs'
                              AND [Target].[System.TeamProject] = 'PCLabs'
                              AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                              AND [Target].[System.WorkItemType] = 'Epic'";

                        var batchEpicParents = await Client.QueryWorkItemsByWiql(batchEpicParentsQuery);
                        
                        foreach (var epic in batchEpicParents)
                        {
                            // Additional safety check to ensure we don't include any of the source features
                            if (!featureBatch.Any(f => f.Id == epic.Id))
                            {
                                affectedEpics.Add(epic.Id);
                            }
                        }
                    }
                }

                foreach (var epicId in affectedEpics)
                {
                    try
                    {
                        await ProcessEpicEstimationAggregation(epicId, Client);
                        aggregationStats["EpicsUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error processing Epic {epicId} estimation aggregation: {ex.Message}");
                        aggregationStats["Errors"]++;
                    }
                }

                // Log aggregation results
                Logger.Information($"Hierarchical aggregation completed:");
                Logger.Information($"  - Tasks processed: {aggregationStats["TasksProcessed"]}");
                Logger.Information($"  - Features processed: {aggregationStats["FeaturesProcessed"]}");
                Logger.Information($"  - PBIs updated: {aggregationStats["PBIsUpdated"]}");
                Logger.Information($"  - Bugs updated: {aggregationStats["BugsUpdated"]}");
                Logger.Information($"  - Glitches updated: {aggregationStats["GlitchesUpdated"]}");
                Logger.Information($"  - Features updated: {aggregationStats["FeaturesUpdated"]}");
                Logger.Information($"  - Epics updated: {aggregationStats["EpicsUpdated"]}");
                Logger.Information($"  - Errors: {aggregationStats["Errors"]}");

                var totalWorkItemsUpdated = aggregationStats["PBIsUpdated"] + aggregationStats["BugsUpdated"] + aggregationStats["GlitchesUpdated"] + aggregationStats["FeaturesUpdated"] + aggregationStats["EpicsUpdated"];
                var message = $"Processed {changedTasks.Count} tasks + {changedFeatures.Count} features, updated {totalWorkItemsUpdated} work items";
                return ScheduledScriptResult.Success(10, message);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Hierarchical aggregation failed");
                return ScheduledScriptResult.Success(5, $"Aggregation failed, will retry in 5 minutes: {ex.Message}");
            }

            // Process bottom-up aggregation from Tasks to parent work items
            async Task ProcessBottomUpAggregation(List<WorkItem> changedTasks, Dictionary<string, string> disciplineMappings, Dictionary<string, int> stats, IAzureDevOpsClient client)
            {
                var affectedParents = new HashSet<int>();

                Logger.Information($"Finding affected parents using batched queries");

                // Process tasks in batches of 50 to avoid query length limits
                const int batchSize = 50;
                var taskBatches = changedTasks.Select((task, index) => new { task, index })
                                             .GroupBy(x => x.index / batchSize)
                                             .Select(g => g.Select(x => x.task).ToList())
                                             .ToList();

                Logger.Information($"Processing {changedTasks.Count} changed tasks in {taskBatches.Count} batches of {batchSize}");

                foreach (var taskBatch in taskBatches)
                {
                    // Create IN clause for batch of task IDs
                    var taskIds = string.Join(",", taskBatch.Select(t => t.Id));
                    
                    // Get parent relationships for this batch of tasks using batched WIQL query
                    var batchParentQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                            FROM WorkItemLinks
                            WHERE [Source].[System.Id] IN ({taskIds})
                            AND [Source].[System.TeamProject] = 'PCLabs'
                            AND [Target].[System.TeamProject] = 'PCLabs'
                            AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                            AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Feature', 'Epic')";

                    var batchParents = await client.QueryWorkItemsByWiql(batchParentQuery);
                    Logger.Debug($"Found {batchParents.Count} parent relationships for batch of {taskBatch.Count} tasks");

                    foreach (var parent in batchParents)
                    {
                        // Additional safety check to ensure we don't include any of the source tasks
                        if (!taskBatch.Any(t => t.Id == parent.Id))
                        {
                            affectedParents.Add(parent.Id);
                        }
                    }
                }

                Logger.Information($"Found {affectedParents.Count} parent work items affected by task changes");

                foreach (var parentId in affectedParents)
                {
                    try
                    {
                        var parentWorkItem = await client.GetWorkItem(parentId);
                        if (parentWorkItem == null) continue;

                        Logger.Debug($"Processing completed work aggregation for {parentWorkItem.WorkItemType} {parentId}: {parentWorkItem.Title}");

                        // Calculate aggregated completed work values for this parent
                        var aggregatedData = await CalculateCompletedWorkAggregation(parentWorkItem, disciplineMappings, client);

                        // Update parent work item with aggregated completed work values
                        await UpdateWorkItemWithCompletedWorkAggregation(parentWorkItem, aggregatedData, client);

                        // Update statistics
                        switch (parentWorkItem.WorkItemType)
                        {
                            case "Product Backlog Item":
                                stats["PBIsUpdated"]++;
                                break;
                            case "Bug":
                                stats["BugsUpdated"]++;
                                break;
                            case "Glitch":
                                stats["GlitchesUpdated"]++;
                                break;
                            case "Feature":
                                stats["FeaturesUpdated"]++;
                                break;
                            case "Epic":
                                stats["EpicsUpdated"]++;
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error processing parent {parentId} completed work aggregation: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                stats["TasksProcessed"] = changedTasks.Count;
            }

            // Process top-down aggregation from Features to Epics
            async Task ProcessTopDownAggregation(List<WorkItem> changedFeatures, Dictionary<string, int> stats, IAzureDevOpsClient client)
            {
                var affectedEpics = new HashSet<int>();

                Logger.Information($"Finding affected epics using batched queries");

                // Process features in batches of 50 to avoid query length limits
                const int batchSize = 50;
                var featureBatches = changedFeatures.Select((feature, index) => new { feature, index })
                                                   .GroupBy(x => x.index / batchSize)
                                                   .Select(g => g.Select(x => x.feature).ToList())
                                                   .ToList();

                Logger.Information($"Processing {changedFeatures.Count} changed features in {featureBatches.Count} batches of {batchSize}");

                foreach (var featureBatch in featureBatches)
                {
                    try
                    {
                        // Create IN clause for batch of feature IDs
                        var featureIds = string.Join(",", featureBatch.Select(f => f.Id));
                        
                        Logger.Debug($"Processing batch with feature IDs: {featureIds}");

                        // Get Epic parents for this batch of Features using batched WIQL query
                        var batchEpicParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                              FROM WorkItemLinks
                              WHERE [Source].[System.Id] IN ({featureIds})
                              AND [Source].[System.TeamProject] = 'PCLabs'
                              AND [Target].[System.TeamProject] = 'PCLabs'
                              AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                              AND [Target].[System.WorkItemType] = 'Epic'";

                        var batchEpicParents = await client.QueryWorkItemsByWiql(batchEpicParentsQuery);
                        Logger.Debug($"Found {batchEpicParents.Count} epic parent relationships for batch of {featureBatch.Count} features");

                        foreach (var epic in batchEpicParents)
                        {
                            // Additional safety check to ensure we don't include any of the source features
                            if (!featureBatch.Any(f => f.Id == epic.Id))
                            {
                                affectedEpics.Add(epic.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error processing feature batch: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                Logger.Information($"Found {affectedEpics.Count} epic work items affected by feature changes");

                foreach (var epicId in affectedEpics)
                {
                    try
                    {
                        await ProcessEpicEstimationAggregation(epicId, client);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error processing epic {epicId} estimation aggregation: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                stats["FeaturesProcessed"] = changedFeatures.Count;
            }

            // Get Epic parents for a given work item
            async Task<List<WorkItem>> GetEpicParents(int workItemId, IAzureDevOpsClient client)
            {
                var epicQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                      FROM WorkItemLinks
                      WHERE [Source].[System.Id] = {workItemId}
                      AND [Source].[System.TeamProject] = 'PCLabs'
                      AND [Target].[System.TeamProject] = 'PCLabs'
                      AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                      AND [Target].[System.WorkItemType] = 'Epic'
                      AND [Target].[System.Id] <> {workItemId}";

                var results = await client.QueryWorkItemsByWiql(epicQuery);

                // Additional safety check to ensure we don't include the source work item itself
                return results.Where(wi => wi.Id != workItemId).ToList();
            }

            // Calculate aggregated completed work data for a work item from its child tasks
            async Task<Dictionary<string, double>> CalculateCompletedWorkAggregation(WorkItem parentItem, Dictionary<string, string> disciplineMappings, IAzureDevOpsClient client)
            {
                var aggregatedData = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0
                };

                // Get all child tasks for this work item using the new simple WIQL method
                // Note: We exclude the parent itself from results to ensure we only get actual child tasks
                var childTasksQuery = $@"SELECT [Target].[System.Id], [Target].[Microsoft.VSTS.Scheduling.CompletedWork], [Target].[Microsoft.VSTS.Common.Activity]
                            FROM WorkItemLinks
                            WHERE [Source].[System.Id] = {parentItem.Id}
                            AND [Source].[System.TeamProject] = 'PCLabs'
                            AND [Target].[System.TeamProject] = 'PCLabs'
                            AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                            AND [Target].[System.WorkItemType] = 'Task'
                            AND [Target].[System.Id] <> {parentItem.Id}";

                var childTasks = await client.QueryWorkItemsByWiql(childTasksQuery);

                foreach (var task in childTasks)
                {
                    // Additional safety check to ensure we don't include the parent itself
                    if (task.Id == parentItem.Id) continue;

                    var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                    var activity = task.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";

                    if (completedWork > 0)
                    {
                        aggregatedData["TotalCompletedWork"] += completedWork;

                        // Map activity to discipline
                        if (disciplineMappings.TryGetValue(activity, out var discipline))
                        {
                            switch (discipline)
                            {
                                case "Development":
                                    aggregatedData["DevelopmentCompletedWork"] += completedWork;
                                    break;
                                case "QA":
                                    aggregatedData["QACompletedWork"] += completedWork;
                                    break;
                                case "PO":
                                    aggregatedData["POCompletedWork"] += completedWork;
                                    break;
                                case "Admin":
                                    aggregatedData["AdminCompletedWork"] += completedWork;
                                    break;
                                case "Others":
                                    aggregatedData["OthersCompletedWork"] += completedWork;
                                    break;
                                }
                        }
                        else
                        {
                            // Unknown activity goes to Others
                            aggregatedData["OthersCompletedWork"] += completedWork;
                        }
                    }
                }

                return aggregatedData;
            }

            // Update work item with aggregated completed work values
            async Task UpdateWorkItemWithCompletedWorkAggregation(WorkItem workItem, Dictionary<string, double> aggregatedData, IAzureDevOpsClient client)
            {
                // Update standard Azure DevOps field
                workItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", aggregatedData["TotalCompletedWork"]);

                // Update custom fields with simplified Custom.* naming convention
                workItem.SetField("Custom.DevelopmentCompletedWork", aggregatedData["DevelopmentCompletedWork"]);
                workItem.SetField("Custom.QACompletedWork", aggregatedData["QACompletedWork"]);
                workItem.SetField("Custom.POCompletedWork", aggregatedData["POCompletedWork"]);
                workItem.SetField("Custom.AdminCompletedWork", aggregatedData["AdminCompletedWork"]);
                workItem.SetField("Custom.OthersCompletedWork", aggregatedData["OthersCompletedWork"]);
                //workItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                await client.SaveWorkItem(workItem);

                Logger.Debug($"Updated {workItem.WorkItemType} {workItem.Id} completed work - Total: {aggregatedData["TotalCompletedWork"]}, Dev: {aggregatedData["DevelopmentCompletedWork"]}, QA: {aggregatedData["QACompletedWork"]}");
            }

            // Process Epic estimation and remaining work aggregation from Features
            async Task ProcessEpicEstimationAggregation(int epicId, IAzureDevOpsClient client)
            {
                var epicWorkItem = await client.GetWorkItem(epicId);
                if (epicWorkItem == null) return;

                Logger.Debug($"Aggregating ALL fields for Epic {epicId}: {epicWorkItem.Title}");

                // Get all child Features for this Epic using the new simple WIQL method
                // Note: We exclude the epic itself from results to ensure we only get actual child features
                var childFeaturesQuery = $@"SELECT [Target].[System.Id]
                               FROM WorkItemLinks
                               WHERE [Source].[System.Id] = {epicId}
                               AND [Source].[System.TeamProject] = 'PCLabs'
                               AND [Target].[System.TeamProject] = 'PCLabs'
                               AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                               AND [Target].[System.WorkItemType] = 'Feature'
                               AND [Target].[System.Id] <> {epicId}";

                var childFeatures = await client.QueryWorkItemsByWiql(childFeaturesQuery);

                // Initialize aggregation totals for estimation fields (from Features)
                var estimationTotals = new Dictionary<string, double>
                {
                    ["TotalEffortEstimation"] = 0,
                    ["DevelopmentEffortEstimation"] = 0,
                    ["QAEffortEstimation"] = 0,
                    ["POEffortEstimation"] = 0,
                    ["AdminEffortEstimation"] = 0,
                    ["OthersEffortEstimation"] = 0
                };

                // Initialize aggregation totals for remaining fields (from Features)
                var remainingTotals = new Dictionary<string, double>
                {
                    ["TotalRemainingWork"] = 0,
                    ["DevelopmentRemainingWork"] = 0,
                    ["QARemainingWork"] = 0,
                    ["PORemainingWork"] = 0,
                    ["AdminRemainingWork"] = 0,
                    ["OthersRemainingWork"] = 0
                };

                // Initialize aggregation totals for completed work fields (from Features)
                var completedTotals = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0
                };

                foreach (var feature in childFeatures)
                {
                    // Additional safety check to ensure we don't include the epic itself
                    if (feature.Id == epicId) continue;

                    var featureWorkItem = await client.GetWorkItem(feature.Id);
                    if (featureWorkItem == null) continue;

                    // Aggregate estimation fields from Feature (using simplified Custom.* field names)
                    estimationTotals["TotalEffortEstimation"] += featureWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.Effort") ?? 0;
                    estimationTotals["DevelopmentEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.DevelopmentEffortEstimation") ?? 0;
                    estimationTotals["QAEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.QAEffortEstimation") ?? 0;
                    estimationTotals["POEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.POEffortEstimation") ?? 0;
                    estimationTotals["AdminEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.AdminEffortEstimation") ?? 0;
                    estimationTotals["OthersEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.OthersEffortEstimation") ?? 0;

                    // Aggregate remaining fields from Feature (using simplified Custom.* field names)
                    remainingTotals["TotalRemainingWork"] += featureWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork") ?? 0;
                    remainingTotals["DevelopmentRemainingWork"] += featureWorkItem.GetField<double?>("Custom.DevelopmentRemainingWork") ?? 0;
                    remainingTotals["QARemainingWork"] += featureWorkItem.GetField<double?>("Custom.QARemainingWork") ?? 0;
                    remainingTotals["PORemainingWork"] += featureWorkItem.GetField<double?>("Custom.PORemainingWork") ?? 0;
                    remainingTotals["AdminRemainingWork"] += featureWorkItem.GetField<double?>("Custom.AdminRemainingWork") ?? 0;
                    remainingTotals["OthersRemainingWork"] += featureWorkItem.GetField<double?>("Custom.OthersRemainingWork") ?? 0;

                    // Aggregate completed work fields from Feature (using simplified Custom.* field names)
                    completedTotals["TotalCompletedWork"] += featureWorkItem.GetField<double?>("Custom.TotalCompletedWork") ?? 0;
                    completedTotals["DevelopmentCompletedWork"] += featureWorkItem.GetField<double?>("Custom.DevelopmentCompletedWork") ?? 0;
                    completedTotals["QACompletedWork"] += featureWorkItem.GetField<double?>("Custom.QACompletedWork") ?? 0;
                    completedTotals["POCompletedWork"] += featureWorkItem.GetField<double?>("Custom.POCompletedWork") ?? 0;
                    completedTotals["AdminCompletedWork"] += featureWorkItem.GetField<double?>("Custom.AdminCompletedWork") ?? 0;
                    completedTotals["OthersCompletedWork"] += featureWorkItem.GetField<double?>("Custom.OthersCompletedWork") ?? 0;
                }

                // Update Epic with aggregated estimation values (using simplified Custom.* field names)
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.Effort", estimationTotals["TotalEffortEstimation"]);
                epicWorkItem.SetField("Custom.TotalEffortEstimation", estimationTotals["TotalEffortEstimation"]);
                epicWorkItem.SetField("Custom.DevelopmentEffortEstimation", estimationTotals["DevelopmentEffortEstimation"]);
                epicWorkItem.SetField("Custom.QAEffortEstimation", estimationTotals["QAEffortEstimation"]);
                epicWorkItem.SetField("Custom.POEffortEstimation", estimationTotals["POEffortEstimation"]);
                epicWorkItem.SetField("Custom.AdminEffortEstimation", estimationTotals["AdminEffortEstimation"]);
                epicWorkItem.SetField("Custom.OthersEffortEstimation", estimationTotals["OthersEffortEstimation"]);

                // Update Epic with aggregated remaining values (using simplified Custom.* field names)
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.RemainingWork", remainingTotals["TotalRemainingWork"]);
                epicWorkItem.SetField("Custom.DevelopmentRemainingWork", remainingTotals["DevelopmentRemainingWork"]);
                epicWorkItem.SetField("Custom.QARemainingWork", remainingTotals["QARemainingWork"]);
                epicWorkItem.SetField("Custom.PORemainingWork", remainingTotals["PORemainingWork"]);
                epicWorkItem.SetField("Custom.AdminRemainingWork", remainingTotals["AdminRemainingWork"]);
                epicWorkItem.SetField("Custom.OthersRemainingWork", remainingTotals["OthersRemainingWork"]);

                // Update Epic with aggregated completed work values (using simplified Custom.* field names)
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", completedTotals["TotalCompletedWork"]);
                epicWorkItem.SetField("Custom.DevelopmentCompletedWork", completedTotals["DevelopmentCompletedWork"]);
                epicWorkItem.SetField("Custom.QACompletedWork", completedTotals["QACompletedWork"]);
                epicWorkItem.SetField("Custom.POCompletedWork", completedTotals["POCompletedWork"]);
                epicWorkItem.SetField("Custom.AdminCompletedWork", completedTotals["AdminCompletedWork"]);
                epicWorkItem.SetField("Custom.OthersCompletedWork", completedTotals["OthersCompletedWork"]);

                // Update aggregation timestamp
                //epicWorkItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                await client.SaveWorkItem(epicWorkItem);

                Logger.Debug($"Updated Epic {epicId} COMPLETE aggregation - Estimation: {estimationTotals["TotalEffortEstimation"]}, Remaining: {remainingTotals["TotalRemainingWork"]}, Completed: {completedTotals["TotalCompletedWork"]}");
            }

            // Calculate Epic completed work from all descendant tasks
            async Task<Dictionary<string, double>> CalculateEpicCompletedWorkFromAllDescendants(WorkItem epicItem, Dictionary<string, string> disciplineMappings, IAzureDevOpsClient client)
            {
                var aggregatedData = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0
                };

                // Step 1: Get all Feature children of this Epic
                // Note: We exclude the epic itself from results to ensure we only get actual child features
                var childFeaturesQuery = $@"SELECT [Target].[System.Id]
                               FROM WorkItemLinks
                               WHERE [Source].[System.Id] = {epicItem.Id}
                               AND [Source].[System.TeamProject] = 'PCLabs'
                               AND [Target].[System.TeamProject] = 'PCLabs'
                               AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                               AND [Target].[System.WorkItemType] = 'Feature'
                               AND [Target].[System.Id] <> {epicItem.Id}";

                var childFeatures = await client.QueryWorkItemsByWiql(childFeaturesQuery);

                // Step 2: For each Feature, get its PBI/Bug children
                foreach (var feature in childFeatures)
                {
                    // Additional safety check to ensure we don't include the epic itself
                    if (feature.Id == epicItem.Id) continue;

                    var childPBIsQuery = $@"SELECT [Target].[System.Id]
                               FROM WorkItemLinks
                               WHERE [Source].[System.Id] = {feature.Id}
                               AND [Source].[System.TeamProject] = 'PCLabs'
                               AND [Target].[System.TeamProject] = 'PCLabs'
                               AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                               AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch')
                               AND [Target].[System.Id] <> {feature.Id}";

                    var childPBIs = await client.QueryWorkItemsByWiql(childPBIsQuery);

                    // Step 3: For each PBI/Bug/Glitch, get its Tasks and aggregate
                    foreach (var pbi in childPBIs)
                    {
                        // Additional safety check to ensure we don't include the feature itself
                        if (pbi.Id == feature.Id) continue;

                        var tasksQuery = $@"SELECT [Target].[System.Id], [Target].[Microsoft.VSTS.Scheduling.CompletedWork], [Target].[Microsoft.VSTS.Common.Activity]
                               FROM WorkItemLinks
                               WHERE [Source].[System.Id] = {pbi.Id}
                               AND [Source].[System.TeamProject] = 'PCLabs'
                               AND [Target].[System.TeamProject] = 'PCLabs'
                               AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                               AND [Target].[System.WorkItemType] = 'Task'
                               AND [Target].[System.Id] <> {pbi.Id}";

                        var tasks = await client.QueryWorkItemsByWiql(tasksQuery);

                        foreach (var task in tasks)
                        {
                            // Additional safety check to ensure we don't include the PBI/Bug/Glitch itself
                            if (task.Id == pbi.Id) continue;

                            var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                            var activity = task.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";

                            if (completedWork > 0)
                            {
                                aggregatedData["TotalCompletedWork"] += completedWork;

                                // Map activity to discipline
                                if (disciplineMappings.TryGetValue(activity, out var discipline))
                                {
                                    switch (discipline)
                                    {
                                        case "Development":
                                            aggregatedData["DevelopmentCompletedWork"] += completedWork;
                                            break;
                                        case "QA":
                                            aggregatedData["QACompletedWork"] += completedWork;
                                            break;
                                        case "PO":
                                            aggregatedData["POCompletedWork"] += completedWork;
                                            break;
                                        case "Admin":
                                            aggregatedData["AdminCompletedWork"] += completedWork;
                                            break;
                                        case "Others":
                                            aggregatedData["OthersCompletedWork"] += completedWork;
                                            break;
                                    }
                                }
                                else
                                {
                                    aggregatedData["OthersCompletedWork"] += completedWork;
                                }
                            }
                        }
                    }
                }

                return aggregatedData;
            }

            // Process multi-level completed work aggregation (PBI/Bug → Feature → Epic)
            async Task ProcessMultiLevelCompletedWorkAggregation(List<WorkItem> changedTasks, Dictionary<string, string> disciplineMappings, Dictionary<string, int> stats, IAzureDevOpsClient client)
            {
                // Find all affected work items that need completed work re-aggregation
                var affectedPBIs = new HashSet<int>();
                var affectedBugs = new HashSet<int>();
                var affectedGlitches = new HashSet<int>();
                var affectedFeatures = new HashSet<int>();
                var affectedEpics = new HashSet<int>();

                CalculateAffectedParentWorkItems(Logger, changedTasks, client, affectedPBIs, affectedBugs, affectedGlitches, affectedFeatures, affectedEpics);

                // Re-aggregate completed work for affected PBIs
                foreach (var pbiId in affectedPBIs)
                {
                    try
                    {
                        var pbiWorkItem = await client.GetWorkItem(pbiId);
                        if (pbiWorkItem == null) continue;

                        Logger.Debug($"Re-aggregating completed work for PBI {pbiId}: {pbiWorkItem.Title}");

                        // Calculate completed work from child tasks
                        var pbiCompletedWork = await CalculateCompletedWorkAggregation(pbiWorkItem, disciplineMappings, client);

                        // Update PBI with aggregated completed work
                        await UpdateWorkItemWithCompletedWorkAggregation(pbiWorkItem, pbiCompletedWork, client);

                        stats["PBIsUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating PBI {pbiId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                // Re-aggregate completed work for affected Bugs
                foreach (var bugId in affectedBugs)
                {
                    try
                    {
                        var bugWorkItem = await client.GetWorkItem(bugId);
                        if (bugWorkItem == null) continue;

                        Logger.Debug($"Re-aggregating completed work for Bug {bugId}: {bugWorkItem.Title}");

                        // Calculate completed work from child tasks
                        var bugCompletedWork = await CalculateCompletedWorkAggregation(bugWorkItem, disciplineMappings, client);

                        // Update Bug with aggregated completed work
                        await UpdateWorkItemWithCompletedWorkAggregation(bugWorkItem, bugCompletedWork, client);

                        stats["BugsUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Bug {bugId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                // Re-aggregate completed work for affected Glitches
                foreach (var glitchId in affectedGlitches)
                {
                    try
                    {
                        var glitchWorkItem = await client.GetWorkItem(glitchId);
                        if (glitchWorkItem == null) continue;

                        Logger.Debug($"Re-aggregating completed work for Glitch {glitchId}: {glitchWorkItem.Title}");

                        // Calculate completed work from child tasks
                        var glitchCompletedWork = await CalculateCompletedWorkAggregation(glitchWorkItem, disciplineMappings, client);

                        // Update Glitch with aggregated completed work
                        await UpdateWorkItemWithCompletedWorkAggregation(glitchWorkItem, glitchCompletedWork, client);

                        stats["GlitchesUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Glitch {glitchId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                // Re-aggregate completed work for affected Features
                foreach (var featureId in affectedFeatures)
                {
                    try
                    {
                        var featureWorkItem = await client.GetWorkItem(featureId);
                        if (featureWorkItem == null) continue;

                        Logger.Debug($"Re-aggregating completed work for Feature {featureId}: {featureWorkItem.Title}");

                        // Calculate completed work from all descendant tasks (through PBI/Bug/Glitch children)
                        var featureCompletedWork = await CalculateFeatureCompletedWorkFromAllDescendants(featureWorkItem, disciplineMappings, client);

                        // Update Feature with aggregated completed work
                        await UpdateWorkItemWithCompletedWorkAggregation(featureWorkItem, featureCompletedWork, client);

                        stats["FeaturesUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Feature {featureId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                }

                // Re-aggregate completed work for affected Epics
                foreach (var epicId in affectedEpics)
                {
                    try
                    {
                        var epicWorkItem = await client.GetWorkItem(epicId);
                        if (epicWorkItem == null) continue;

                        Logger.Debug($"Re-aggregating completed work for Epic {epicId}: {epicWorkItem.Title}");

                        // Calculate completed work from all descendant tasks (through Feature/PBI/Bug/Glitch children)
                        var epicCompletedWork = await CalculateEpicCompletedWorkFromAllDescendants(epicWorkItem, disciplineMappings, client);

                        // Update Epic completed work fields only (estimation/remaining handled elsewhere)
                        epicWorkItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", epicCompletedWork["TotalCompletedWork"]);
                        epicWorkItem.SetField("Custom.DevelopmentCompletedWork", epicCompletedWork["DevelopmentCompletedWork"]);
                        epicWorkItem.SetField("Custom.QACompletedWork", epicCompletedWork["QACompletedWork"]);
                        epicWorkItem.SetField("Custom.POCompletedWork", epicCompletedWork["POCompletedWork"]);
                        epicWorkItem.SetField("Custom.AdminCompletedWork", epicCompletedWork["AdminCompletedWork"]);
                        epicWorkItem.SetField("Custom.OthersCompletedWork", epicCompletedWork["OthersCompletedWork"]);
                        //epicWorkItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                        await client.SaveWorkItem(epicWorkItem);

                        stats["EpicsUpdated"]++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Epic {epicId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                }
            }

            // Calculate Feature completed work from all descendant tasks
            async Task<Dictionary<string, double>> CalculateFeatureCompletedWorkFromAllDescendants(WorkItem featureItem, Dictionary<string, string> disciplineMappings, IAzureDevOpsClient client)
            {
                var aggregatedData = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0
                };

                // Step 1: Get all PBI/Bug/Glitch children of this Feature
                // Note: We exclude the feature itself from results to ensure we only get actual child PBIs/Bugs/Glitches
                var childPBIsQuery = $@"SELECT [Target].[System.Id]
                           FROM WorkItemLinks
                           WHERE [Source].[System.Id] = {featureItem.Id}
                           AND [Source].[System.TeamProject] = 'PCLabs'
                           AND [Target].[System.TeamProject] = 'PCLabs'
                           AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                           AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch')
                           AND [Target].[System.Id] <> {featureItem.Id}";

                var childPBIs = await client.QueryWorkItemsByWiql(childPBIsQuery);

                // Step 2: For each PBI/Bug, get its Tasks and aggregate
                foreach (var pbi in childPBIs)
                {
                    // Additional safety check to ensure we don't include the feature itself
                    if (pbi.Id == featureItem.Id) continue;

                    var tasksQuery = $@"SELECT [Target].[System.Id], [Target].[Microsoft.VSTS.Scheduling.CompletedWork], [Target].[Microsoft.VSTS.Common.Activity]
                           FROM WorkItemLinks
                           WHERE [Source].[System.Id] = {pbi.Id}
                           AND [Source].[System.TeamProject] = 'PCLabs'
                           AND [Target].[System.TeamProject] = 'PCLabs'
                           AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
                           AND [Target].[System.WorkItemType] = 'Task'
                           AND [Target].[System.Id] <> {pbi.Id}";

                    var tasks = await client.QueryWorkItemsByWiql(tasksQuery);

                    foreach (var task in tasks)
                    {
                        // Additional safety check to ensure we don't include the PBI/Bug/Glitch itself
                        if (task.Id == pbi.Id) continue;

                        var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                        var activity = task.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";

                        if (completedWork > 0)
                        {
                            aggregatedData["TotalCompletedWork"] += completedWork;

                            // Map activity to discipline
                            if (disciplineMappings.TryGetValue(activity, out var discipline))
                            {
                                switch (discipline)
                                {
                                    case "Development":
                                        aggregatedData["DevelopmentCompletedWork"] += completedWork;
                                        break;
                                    case "QA":
                                        aggregatedData["QACompletedWork"] += completedWork;
                                        break;
                                    case "PO":
                                        aggregatedData["POCompletedWork"] += completedWork;
                                        break;
                                    case "Admin":
                                        aggregatedData["AdminCompletedWork"] += completedWork;
                                        break;
                                    case "Others":
                                        aggregatedData["OthersCompletedWork"] += completedWork;
                                        break;
                                }
                            }
                            else
                            {
                                aggregatedData["OthersCompletedWork"] += completedWork;
                            }
                        }
                    }
                }

                return aggregatedData;
            }
        }

        private static void CalculateAffectedParentWorkItems(ILogger Logger, List<WorkItem> changedTasks, IAzureDevOpsClient client, HashSet<int> affectedPBIs, HashSet<int> affectedBugs, HashSet<int> affectedGlitches, HashSet<int> affectedFeatures, HashSet<int> affectedEpics)
        {
            Logger.Information($"Calculating Affected Parents for Re-Aggregations using batched queries");

            // Process tasks in batches of 50 to avoid query length limits
            const int batchSize = 50;
            var taskBatches = changedTasks.Select((task, index) => new { task, index })
                                         .GroupBy(x => x.index / batchSize)
                                         .Select(g => g.Select(x => x.task).ToList())
                                         .ToList();

            Logger.Information($"Processing {changedTasks.Count} changed tasks in {taskBatches.Count} batches of {batchSize}");

            foreach (var taskBatch in taskBatches)
            {
                // Create IN clause for batch of task IDs
                var taskIds = string.Join(",", taskBatch.Select(t => t.Id));

                // Get all ancestors for this batch of tasks
                // Note: We can't use column aliases in WIQL, so we'll get all relationships and filter afterward
                var batchAncestorsQuery = $@"SELECT [Source].[System.Id], [Target].[System.Id], [Target].[System.WorkItemType]
                               FROM WorkItemLinks
                               WHERE [Source].[System.Id] IN ({taskIds})
                               AND [Source].[System.TeamProject] = 'PCLabs'
                               AND [Target].[System.TeamProject] = 'PCLabs'
                               AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                               AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Feature', 'Epic')";

                var batchAncestors = await client.QueryWorkItemsByWiql(batchAncestorsQuery);
                Logger.Debug($"Found {batchAncestors.Count} ancestor relationships for batch of {taskBatch.Count} tasks");

                batchAncestors = batchAncestors.Where(a => a.WorkItemType != "Task").ToList();

                foreach (var ancestor in batchAncestors)
                {
                    // Since we can't use aliases, we need to ensure we don't include the source task itself
                    // The ancestor.Id is the Target work item ID, so we check it's not in our source task batch
                    var isSourceTask = taskBatch.Any(t => t.Id == ancestor.Id);
                    if (isSourceTask) continue;

                    if (ancestor.WorkItemType == "Feature")
                    {
                        affectedFeatures.Add(ancestor.Id);
                    }
                    else if (ancestor.WorkItemType == "Epic")
                    {
                        affectedEpics.Add(ancestor.Id);
                    }
                    else if (ancestor.WorkItemType == "Product Backlog Item")
                    {
                        affectedPBIs.Add(ancestor.Id);
                    }
                    else if (ancestor.WorkItemType == "Bug")
                    {
                        affectedBugs.Add(ancestor.Id);
                    }
                    else if (ancestor.WorkItemType == "Glitch")
                    {
                        affectedGlitches.Add(ancestor.Id);
                    }
                }
            }

            // Now find Feature parents for all affected PBIs/Bugs/Glitches in batches
            var allWorkItemsNeedingFeatureParents = new List<int>();
            allWorkItemsNeedingFeatureParents.AddRange(affectedPBIs);
            allWorkItemsNeedingFeatureParents.AddRange(affectedBugs);
            allWorkItemsNeedingFeatureParents.AddRange(affectedGlitches);

            if (allWorkItemsNeedingFeatureParents.Count > 0)
            {
                var workItemBatches = allWorkItemsNeedingFeatureParents.Select((id, index) => new { id, index })
                                                                      .GroupBy(x => x.index / batchSize)
                                                                      .Select(g => g.Select(x => x.id).ToList())
                                                                      .ToList();

                Logger.Information($"Finding Feature parents for {allWorkItemsNeedingFeatureParents.Count} work items in {workItemBatches.Count} batches");

                foreach (var workItemBatch in workItemBatches)
                {
                    var workItemIds = string.Join(",", workItemBatch);

                    var featureParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                                        FROM WorkItemLinks
                                        WHERE [Source].[System.Id] IN ({workItemIds})
                                        AND [Source].[System.TeamProject] = 'PCLabs'
                                        AND [Target].[System.TeamProject] = 'PCLabs'
                                        AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                                        AND [Target].[System.WorkItemType] = 'Feature'";

                    var featureParents = await client.QueryWorkItemsByWiql(featureParentsQuery);
                    Logger.Debug($"Found {featureParents.Count} feature parent relationships for batch of {workItemBatch.Count} work items");

                    foreach (var parent in featureParents)
                    {
                        if (parent.WorkItemType == "Feature")
                        {
                            affectedFeatures.Add(parent.Id);
                        }
                    }
                }
            }

            // Now find Epic parents of all affected Features in batches
            if (affectedFeatures.Count > 0)
            {
                var featureBatches = affectedFeatures.Select((id, index) => new { id, index })
                                                    .GroupBy(x => x.index / batchSize)
                                                    .Select(g => g.Select(x => x.id).ToList())
                                                    .ToList();

                Logger.Information($"Finding Epic parents for {affectedFeatures.Count} features in {featureBatches.Count} batches");

                foreach (var featureBatch in featureBatches)
                {
                    var featureIds = string.Join(",", featureBatch);

                    var epicParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                                        FROM WorkItemLinks
                                        WHERE [Source].[System.Id] IN ({featureIds})
                                        AND [Source].[System.TeamProject] = 'PCLabs'
                                        AND [Target].[System.TeamProject] = 'PCLabs'
                                        AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                                        AND [Target].[System.WorkItemType] = 'Epic'";

                    var epicParents = await client.QueryWorkItemsByWiql(epicParentsQuery);
                    Logger.Debug($"Found {epicParents.Count} epic parent relationships for batch of {featureBatch.Count} features");

                    foreach (var parent in epicParents)
                    {
                        if (parent.WorkItemType == "Epic")
                        {
                            affectedEpics.Add(parent.Id);
                        }
                    }
                }
            }

            Logger.Information($"Found {affectedPBIs.Count} PBIs, {affectedBugs.Count} bugs, {affectedGlitches.Count} glitches, {affectedFeatures.Count} features and {affectedEpics.Count} epics needing completed work re-aggregation");
        }
    }
}
