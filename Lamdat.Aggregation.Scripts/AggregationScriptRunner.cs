using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Lamdat.Aggregation.Scripts
{
    internal class AggregationScriptRunner
    {

        public static async Task<ScheduledScriptResult> Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken CancellationToken, string ScriptRunId, DateTime LastRun)
        {
            // Hierarchical Work Item Aggregation Task
            // This script aggregates effort data through the Epic > Feature > PBI/Bug/Glitch > Task hierarchy
            // 1. Bottom-up: Task completed work aggregated to parents (PBI/Bug/Glitch/Feature/Epic)
            // 2. Top-down: Feature estimation/remaining fields aggregated to Epic
            // Runs every 10 minutes to process work items using paging instead of date filtering
            //
            // NOTE: This script is specifically designed for PCLabs Ltd and sets Client.Project = "PCLabs"

            Logger.Information("Starting hierarchical work item aggregation with paging...");
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
        {"Detailed Design", "PO"},
        {"Development", "Development"},
        {"DevOps", "Infra"},
        {"UnBilled", "Others"},
        {"Integration", "Development"},
        {"Investigation", "UnProductive"},
        {"Research", "Development"},
        {"Management", "UnProductive"},
        {"Permissions", "Admin"},
        {"Project Management", "Others"},
        {"Release", "Development"},
        {"Reproduce", "QA"},
        {"Requirements Meeting", "PO"},
        {"Support Team", "Others"},
        {"Support COE", "Capabilities"},
        {"Test Case", "QA"},
        {"Test Cases Approval", "QA"},
        {"Testing", "QA"},
        {"Training", "Capabilities"},
        {"First Line Support", "Admin"},
        {"UX/UI", "PO"},
        {"Regression Testing", "QA"},
        {"Functional Design", "PO"},
        {"Solution Design", "PO"},
        {"Release Infra", "Infra"}

    };

                // Step 1: Find all tasks that have changed since last run using paging
                var changedTasks = await GetChangedTasksWithPaging(LastRun, Client);
                Logger.Information($"Found {changedTasks.Count} changed tasks with completed work since last run");

                // Step 2: Find all features that have changed since last run using paging
                var changedFeatures = await GetChangedFeaturesWithPaging(LastRun, Client);
                Logger.Information($"Found {changedFeatures.Count} changed features since last run");

                // Check for work items in "Removed" state that need parent recalculation
                var removedWorkItemsCount = await CheckForRemovedWorkItems(Logger, LastRun, Client);
                Logger.Information($"Found {removedWorkItemsCount} work items in 'Removed' state that have changed since last run");

                if (changedTasks.Count == 0 && changedFeatures.Count == 0 && removedWorkItemsCount == 0)
                {
                    Logger.Information("No tasks, features, or removed work items with changes found - no aggregation needed");
                    return ScheduledScriptResult.Success(10, "No aggregation needed - next check in 10 minutes");
                }

                // Initialize aggregation statistics
                var aggregationStats = new ConcurrentDictionary<string, int>
                {
                    ["TasksProcessed"] = 0,
                    ["FeaturesProcessed"] = 0,
                    ["PBIsUpdated"] = 0,
                    ["BugsUpdated"] = 0,
                    ["GlitchesUpdated"] = 0,
                    ["FeaturesUpdated"] = 0,
                    ["EpicsUpdated"] = 0,
                    ["RemovedItemsProcessed"] = 0,
                    ["Errors"] = 0
                };

                // Step 3: Process bottom-up aggregation (Tasks → Parents) OR handle removed work items
                if (changedTasks.Count > 0 || removedWorkItemsCount > 0)
                {
                    await ProcessMultiLevelCompletedWorkAggregation(changedTasks, disciplineMappings, aggregationStats, Client);
                }

                // Step 4: Process top-down aggregation (Features → Epics)
                if (changedFeatures.Count > 0)
                {
                    await ProcessTopDownAggregation(changedFeatures, aggregationStats, Client);
                }

                // Log aggregation results
                Logger.Information($"Hierarchical aggregation completed:");
                Logger.Information($"  - Features processed: {aggregationStats["FeaturesProcessed"]}");
                Logger.Information($"  - PBIs updated: {aggregationStats["PBIsUpdated"]}");
                Logger.Information($"  - Bugs updated: {aggregationStats["BugsUpdated"]}");
                Logger.Information($"  - Glitches updated: {aggregationStats["GlitchesUpdated"]}");
                Logger.Information($"  - Features updated: {aggregationStats["FeaturesUpdated"]}");
                Logger.Information($"  - Epics updated: {aggregationStats["EpicsUpdated"]}");
                Logger.Information($"  - Removed items processed: {aggregationStats["RemovedItemsProcessed"]}");
                Logger.Information($"  - Errors: {aggregationStats["Errors"]}");

                var totalWorkItemsUpdated = aggregationStats["PBIsUpdated"] + aggregationStats["BugsUpdated"] + aggregationStats["GlitchesUpdated"] + aggregationStats["FeaturesUpdated"] + aggregationStats["EpicsUpdated"];
                var message = $"Processed {changedTasks.Count} tasks + {changedFeatures.Count} features + {removedWorkItemsCount} removed items, updated {totalWorkItemsUpdated} work items";
                return ScheduledScriptResult.Success(10, message);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Hierarchical aggregation failed");
                return ScheduledScriptResult.Success(5, $"Aggregation failed, will retry in 5 minutes: {ex.Message}");
            }

            // Get changed tasks using paging to avoid query size limits
            async Task<List<WorkItem>> GetChangedTasksWithPaging(DateTime lastRun, IAzureDevOpsClient client)
            {
                var changedTasks = new List<WorkItem>();
                const int pageSize = 200; // Azure DevOps recommended page size
                var skip = 0;
                var hasMoreResults = true;
                DateTime? lastProcessedDate = null;

                Logger.Information("Fetching changed tasks using paging approach...");

                while (hasMoreResults)
                {
                    try
                    {
                        // Build WIQL query with proper date-only format for Azure DevOps
                        var changedTasksQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
                                                  [Microsoft.VSTS.Scheduling.CompletedWork], [Microsoft.VSTS.Common.Activity], 
                                                  [System.ChangedDate]
                                                  FROM WorkItems 
                                                  WHERE [System.WorkItemType] = 'Task' 
                                                  AND [System.TeamProject] = 'PCLabs'";

                        // Add date filtering for paging - use date-only format
                        if (lastProcessedDate.HasValue)
                        {
                            // For subsequent pages, get items older than the last processed date (date-only format)
                            var lastProcessedDateOnly = lastProcessedDate.Value.ToString("yyyy-MM-dd");
                            changedTasksQuery += $" AND [System.ChangedDate] < '{lastProcessedDateOnly}'";
                        }

                        changedTasksQuery += " ORDER BY [System.ChangedDate] DESC";

                        // Use the top parameter in the API call, not in the query
                        var pageResults = await client.QueryWorkItemsByWiql(changedTasksQuery, pageSize);

                        if (pageResults.Count == 0)
                        {
                            hasMoreResults = false;
                            break;
                        }

                        // Filter results by LastRun date and add to collection
                        var filteredPageResults = pageResults.Where(task =>
                        {
                            var changedDate = task.GetField<DateTime?>("System.ChangedDate");
                            return changedDate.HasValue && changedDate.Value.ToUniversalTime() >= lastRun.ToUniversalTime();
                        }).ToList();

                        changedTasks.AddRange(filteredPageResults);

                        Logger.Debug($"Page {skip / pageSize + 1}: Retrieved {pageResults.Count} tasks, {filteredPageResults.Count} match date filter");

                        // Update the last processed date for next iteration
                        if (pageResults.Count > 0)
                        {
                            lastProcessedDate = pageResults.Last().GetField<DateTime?>("System.ChangedDate");
                        }

                        // If we got fewer results than page size, we've reached the end
                        if (pageResults.Count < pageSize)
                        {
                            hasMoreResults = false;
                        }

                        // If this page has no results matching our date filter and we're getting older records,
                        // we can stop as subsequent pages will be even older
                        if (filteredPageResults.Count == 0 && pageResults.Count > 0)
                        {
                            var oldestInPage = pageResults.Min(t => t.GetField<DateTime?>("System.ChangedDate"));
                            if (oldestInPage.HasValue && oldestInPage.Value.ToUniversalTime() < lastRun.ToUniversalTime())
                            {
                                Logger.Debug("Reached tasks older than LastRun, stopping pagination");
                                hasMoreResults = false;
                            }
                        }

                        skip += pageSize;

                        // Safety limit to prevent infinite loops
                        if (skip > 50000) // Adjust this limit based on your needs
                        {
                            Logger.Warning($"Reached safety limit of 50,000 tasks processed, stopping pagination");
                            hasMoreResults = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error fetching page starting at {skip}: {ex.Message}");
                        hasMoreResults = false;
                    }
                }

                Logger.Information($"Completed paging for tasks: found {changedTasks.Count} changed tasks since {lastRun:yyyy-MM-dd HH:mm:ss}");
                return changedTasks;
            }

            // Get changed features using paging to avoid query size limits
            async Task<List<WorkItem>> GetChangedFeaturesWithPaging(DateTime lastRun, IAzureDevOpsClient client)
            {
                var changedFeatures = new List<WorkItem>();
                const int pageSize = 200; // Azure DevOps recommended page size
                var skip = 0;
                var hasMoreResults = true;
                DateTime? lastProcessedDate = null;

                Logger.Information("Fetching changed features using paging approach...");

                while (hasMoreResults)
                {
                    try
                    {
                        // Build WIQL query with proper date-only format for Azure DevOps
                        var changedFeaturesQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.ChangedDate]
                                                     FROM WorkItems 
                                                     WHERE [System.WorkItemType] = 'Feature' 
                                                     AND [System.TeamProject] = 'PCLabs'";

                        // Add date filtering for paging - use date-only format
                        if (lastProcessedDate.HasValue)
                        {
                            // For subsequent pages, get items older than the last processed date (date-only format)
                            var lastProcessedDateOnly = lastProcessedDate.Value.ToString("yyyy-MM-dd");
                            changedFeaturesQuery += $" AND [System.ChangedDate] < '{lastProcessedDateOnly}'";
                        }

                        changedFeaturesQuery += " ORDER BY [System.ChangedDate] DESC";

                        // Use the top parameter in the API call, not in the query
                        var pageResults = await client.QueryWorkItemsByWiql(changedFeaturesQuery, pageSize);

                        if (pageResults.Count == 0)
                        {
                            hasMoreResults = false;
                            break;
                        }

                        // Filter results by LastRun date and add to collection
                        var filteredPageResults = pageResults.Where(feature =>
                        {
                            var changedDate = feature.GetField<DateTime?>("System.ChangedDate");
                            return changedDate.HasValue && changedDate.Value.ToUniversalTime() >= lastRun.ToUniversalTime();
                        }).ToList();

                        changedFeatures.AddRange(filteredPageResults);

                        Logger.Debug($"Page {skip / pageSize + 1}: Retrieved {pageResults.Count} features, {filteredPageResults.Count} match date filter");

                        // Update the last processed date for next iteration
                        if (pageResults.Count > 0)
                        {
                            lastProcessedDate = pageResults.Last().GetField<DateTime?>("System.ChangedDate");
                        }

                        // If we got fewer results than page size, we've reached the end
                        if (pageResults.Count < pageSize)
                        {
                            hasMoreResults = false;
                        }

                        // If this page has no results matching our date filter and we're getting older records,
                        // we can stop as subsequent pages will be even older
                        if (filteredPageResults.Count == 0 && pageResults.Count > 0)
                        {
                            var oldestInPage = pageResults.Min(f => f.GetField<DateTime?>("System.ChangedDate"));
                            if (oldestInPage.HasValue && oldestInPage.Value.ToUniversalTime() < lastRun.ToUniversalTime())
                            {
                                Logger.Debug("Reached features older than LastRun, stopping pagination");
                                hasMoreResults = false;
                            }
                        }

                        skip += pageSize;

                        // Safety limit to prevent infinite loops
                        if (skip > 50000) // Adjust this limit based on your needs
                        {
                            Logger.Warning($"Reached safety limit of 50,000 features processed, stopping pagination");
                            hasMoreResults = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error fetching features page starting at {skip}: {ex.Message}");
                        hasMoreResults = false;
                    }
                }

                Logger.Information($"Completed paging for features: found {changedFeatures.Count} changed features since {lastRun:yyyy-MM-dd HH:mm:ss}");
                return changedFeatures;
            }

            // Process top-down aggregation from Features to Epics
            async Task ProcessTopDownAggregation(List<WorkItem> changedFeatures, ConcurrentDictionary<string, int> stats, IAzureDevOpsClient client)
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

                foreach (var featureBatch in featureBatches) // finding parents of features in batches
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
                        Logger.Debug($"Found { batchEpicParents.Count} epic parent relationships for batch of { featureBatch.Count} features");

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

                await Parallel.ForEachAsync(affectedEpics, CancellationToken, async (epicId, ct) =>
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
                });

                stats["FeaturesProcessed"] = changedFeatures.Count;
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
                    ["OthersEffortEstimation"] = 0,
                    ["InfraEffortEstimation"] = 0,
                    ["CapabilitiesEffortEstimation"] = 0,
                    ["UnProductiveEffortEstimation"] = 0

                };

                // Initialize aggregation totals for remaining fields (from Features)
                var remainingTotals = new Dictionary<string, double>
                {
                    ["TotalRemainingWork"] = 0,
                    ["DevelopmentRemainingWork"] = 0,
                    ["QARemainingWork"] = 0,
                    ["PORemainingWork"] = 0,
                    ["AdminRemainingWork"] = 0,
                    ["OthersRemainingWork"] = 0,
                    ["InfraRemainingWork"] = 0,
                    ["CapabilitiesRemainingWork"] = 0,
                    ["UnProductiveRemainingWork"] = 0
                };

                // Initialize aggregation totals for completed work fields (from Features)
                var completedTotals = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0,
                    ["InfraCompletedWork"] = 0,
                    ["CapabilitiesCompletedWork"] = 0,
                    ["UnProductiveCompletedWork"] = 0
                };

                foreach (var feature in childFeatures)
                {
                    // Additional safety check to ensure we don't include the epic itself
                    if (feature.Id == epicId) continue;

                    var featureWorkItem = await client.GetWorkItem(feature.Id);
                    if (featureWorkItem == null) continue;

                    // Skip features that are in "Removed" state
                    var featureState = featureWorkItem.GetField<string>("System.State");
                    if (string.Equals(featureState, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"Skipping Feature {feature.Id} in 'Removed' state");
                        continue;
                    }

                    // Aggregate estimation fields from Feature (using simplified Custom.* field names)
                    estimationTotals["TotalEffortEstimation"] += featureWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.Effort") ?? 0;
                    estimationTotals["DevelopmentEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.DevelopmentEffortEstimation") ?? 0;
                    estimationTotals["QAEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.QAEffortEstimation") ?? 0;
                    estimationTotals["POEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.POEffortEstimation") ?? 0;
                    estimationTotals["AdminEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.AdminEffortEstimation") ?? 0;
                    estimationTotals["OthersEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.OthersEffortEstimation") ?? 0;
                    estimationTotals["InfraEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.InfraEffortEstimation") ?? 0;
                    estimationTotals["CapabilitiesEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.CapabilitiesEffortEstimation") ?? 0;
                    estimationTotals["UnProductiveEffortEstimation"] += featureWorkItem.GetField<double?>("Custom.UnProductiveEffortEstimation") ?? 0;

                    // Aggregate remaining fields from Feature (using simplified Custom.* field names)
                    remainingTotals["TotalRemainingWork"] += featureWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork") ?? 0;
                    remainingTotals["DevelopmentRemainingWork"] += featureWorkItem.GetField<double?>("Custom.DevelopmentRemainingWork") ?? 0;
                    remainingTotals["QARemainingWork"] += featureWorkItem.GetField<double?>("Custom.QARemainingWork") ?? 0;
                    remainingTotals["PORemainingWork"] += featureWorkItem.GetField<double?>("Custom.PORemainingWork") ?? 0;
                    remainingTotals["AdminRemainingWork"] += featureWorkItem.GetField<double?>("Custom.AdminRemainingWork") ?? 0;
                    remainingTotals["OthersRemainingWork"] += featureWorkItem.GetField<double?>("Custom.OthersRemainingWork") ?? 0;
                    remainingTotals["InfraRemainingWork"] += featureWorkItem.GetField<double?>("Custom.InfraRemainingWork") ?? 0;
                    remainingTotals["CapabilitiesRemainingWork"] += featureWorkItem.GetField<double?>("Custom.CapabilitiesRemainingWork") ?? 0;
                    remainingTotals["UnProductiveRemainingWork"] += featureWorkItem.GetField<double?>("Custom.UnProductiveRemainingWork") ?? 0;


                    // Aggregate completed work fields from Feature (using simplified Custom.* field names)
                    completedTotals["TotalCompletedWork"] += featureWorkItem.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                    completedTotals["DevelopmentCompletedWork"] += featureWorkItem.GetField<double?>("Custom.DevelopmentCompletedWork") ?? 0;
                    completedTotals["QACompletedWork"] += featureWorkItem.GetField<double?>("Custom.QACompletedWork") ?? 0;
                    completedTotals["POCompletedWork"] += featureWorkItem.GetField<double?>("Custom.POCompletedWork") ?? 0;
                    completedTotals["AdminCompletedWork"] += featureWorkItem.GetField<double?>("Custom.AdminCompletedWork") ?? 0;
                    completedTotals["OthersCompletedWork"] += featureWorkItem.GetField<double?>("Custom.OthersCompletedWork") ?? 0;
                    completedTotals["InfraCompletedWork"] += featureWorkItem.GetField<double?>("Custom.InfraCompletedWork") ?? 0;
                    completedTotals["CapabilitiesCompletedWork"] += featureWorkItem.GetField<double?>("Custom.CapabilitiesCompletedWork") ?? 0;
                    completedTotals["UnProductiveCompletedWork"] += featureWorkItem.GetField<double?>("Custom.UnProductiveCompletedWork") ?? 0;

                }

                // Update Epic with aggregated estimation values (using simplified Custom.* field names)
                // Update Epic with aggregated estimation values (using simplified Custom.* field names)
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.Effort", Math.Round(estimationTotals["TotalEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.TotalEffortEstimation", Math.Round(estimationTotals["TotalEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.DevelopmentEffortEstimation", Math.Round(estimationTotals["DevelopmentEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.QAEffortEstimation", Math.Round(estimationTotals["QAEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.POEffortEstimation", Math.Round(estimationTotals["POEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.AdminEffortEstimation", Math.Round(estimationTotals["AdminEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.OthersEffortEstimation", Math.Round(estimationTotals["OthersEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.InfraEffortEstimation", Math.Round(estimationTotals["InfraEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.CapabilitiesEffortEstimation", Math.Round(estimationTotals["CapabilitiesEffortEstimation"], 2));
                epicWorkItem.SetField("Custom.UnProductiveEffortEstimation", Math.Round(estimationTotals["UnProductiveEffortEstimation"], 2));


                // Update Epic with aggregated remaining values (using simplified Custom.* field names)     
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.RemainingWork", Math.Round(remainingTotals["TotalRemainingWork"], 2));
                epicWorkItem.SetField("Custom.DevelopmentRemainingWork", Math.Round(remainingTotals["DevelopmentRemainingWork"], 2));
                epicWorkItem.SetField("Custom.QARemainingWork", Math.Round(remainingTotals["QARemainingWork"], 2));
                epicWorkItem.SetField("Custom.PORemainingWork", Math.Round(remainingTotals["PORemainingWork"], 2));
                epicWorkItem.SetField("Custom.AdminRemainingWork", Math.Round(remainingTotals["AdminRemainingWork"], 2));
                epicWorkItem.SetField("Custom.OthersRemainingWork", Math.Round(remainingTotals["OthersRemainingWork"], 2));
                epicWorkItem.SetField("Custom.InfraRemainingWork", Math.Round(remainingTotals["InfraRemainingWork"], 2));
                epicWorkItem.SetField("Custom.CapabilitiesRemainingWork", Math.Round(remainingTotals["CapabilitiesRemainingWork"], 2));
                epicWorkItem.SetField("Custom.UnProductiveRemainingWork", Math.Round(remainingTotals["UnProductiveRemainingWork"], 2));

                // Update Epic with aggregated completed work values (using simplified Custom.* field names)    
                epicWorkItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", Math.Round(completedTotals["TotalCompletedWork"], 2));
                epicWorkItem.SetField("Custom.DevelopmentCompletedWork", Math.Round(completedTotals["DevelopmentCompletedWork"], 2));
                epicWorkItem.SetField("Custom.QACompletedWork", Math.Round(completedTotals["QACompletedWork"], 2));
                epicWorkItem.SetField("Custom.POCompletedWork", Math.Round(completedTotals["POCompletedWork"], 2));
                epicWorkItem.SetField("Custom.AdminCompletedWork", Math.Round(completedTotals["AdminCompletedWork"], 2));
                epicWorkItem.SetField("Custom.OthersCompletedWork", Math.Round(completedTotals["OthersCompletedWork"], 2));
                epicWorkItem.SetField("Custom.InfraCompletedWork", Math.Round(completedTotals["InfraCompletedWork"], 2));
                epicWorkItem.SetField("Custom.CapabilitiesCompletedWork", Math.Round(completedTotals["CapabilitiesCompletedWork"], 2));
                epicWorkItem.SetField("Custom.UnProductiveCompletedWork", Math.Round(completedTotals["UnProductiveCompletedWork"], 2));

                // Update aggregation timestamp
                //epicWorkItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                await client.SaveWorkItem(epicWorkItem);

                Logger.Debug($"Updated Epic {epicId} COMPLETE aggregation - Estimation: {estimationTotals["TotalEffortEstimation"]}, Remaining: {remainingTotals["TotalRemainingWork"]}, Completed: {completedTotals["TotalCompletedWork"]}");
            }



            ////// Calculate completed work aggregation from child tasks ////

            // Process multi-level completed work aggregation (PBI/Bug → Feature → Epic)
            async Task ProcessMultiLevelCompletedWorkAggregation(List<WorkItem> changedTasks, Dictionary<string, string> disciplineMappings, ConcurrentDictionary<string, int> stats, IAzureDevOpsClient client)
            {
                // Find all affected work items that need completed work re-aggregation
                var affectedPBIs = new HashSet<int>();
                var affectedBugs = new HashSet<int>();
                var affectedGlitches = new HashSet<int>();
                var affectedFeatures = new HashSet<int>();
                var affectedEpics = new HashSet<int>();

                await CalculateAffectedParentWorkItems(Logger, changedTasks, client, affectedPBIs, affectedBugs, affectedGlitches, affectedFeatures, affectedEpics);

                // Add work items in "Removed" state that have changed since last run
                // These need to be included so their parents can be recalculated correctly
                await AddRemovedWorkItemsToAffectedCollections(Logger, LastRun, client, affectedPBIs, affectedBugs, affectedGlitches, affectedFeatures, affectedEpics, stats);

                var allWorkItemsWithTasks = new ConcurrentBag<int>();

                foreach (var item in affectedPBIs)
                    allWorkItemsWithTasks.Add(item);

                foreach (var item in affectedBugs)
                    allWorkItemsWithTasks.Add(item);

                foreach (var item in affectedGlitches)
                    allWorkItemsWithTasks.Add(item);

                // With this block:
                await Parallel.ForEachAsync(allWorkItemsWithTasks, CancellationToken, async (pbiId, ct) =>
                {
                    try
                    {
                        var pbiWorkItem = await client.GetWorkItem(pbiId);
                        if (pbiWorkItem != null)
                        {
                            var workItemType = pbiWorkItem.WorkItemType;
                            Logger.Debug($"Re-aggregating completed work for {workItemType} {pbiId}: {pbiWorkItem.Title}");

                            // Calculate completed work from child tasks
                            var pbiCompletedWork = await CalculateCompletedWorkAggregation(pbiWorkItem, disciplineMappings, client);

                            // Update PBI with aggregated completed work
                            await UpdateWorkItemWithCompletedWorkAggregation(pbiWorkItem, pbiCompletedWork, client);

                            switch (workItemType)
                            {
                                case "Product Backlog Item":
                                    stats["PBIsUpdated"]++;
                                    break;
                                case "Glitch":
                                    stats["GlitchesUpdated"]++;
                                    break;
                                case "Bug":
                                    stats["BugsUpdated"]++;
                                    break;
                                default:
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating PBI {pbiId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                });


                // Re-aggregate completed work for affected Features
                await Parallel.ForEachAsync(affectedFeatures, CancellationToken, async (featureId, ct) =>
                {
                    try
                    {
                        var featureWorkItem = await client.GetWorkItem(featureId);
                        if (featureWorkItem != null)
                        {

                            Logger.Debug($"Re-aggregating completed work for Feature {featureId}: {featureWorkItem.Title}");

                            // Calculate completed work from all descendant tasks (through PBI/Bug/Glitch children)
                            var featureCompletedWork = await CalculateFeatureCompletedWorkFromAllDescendants(featureWorkItem, disciplineMappings, client);

                            // Update Feature with aggregated completed work
                            await UpdateWorkItemWithCompletedWorkAggregation(featureWorkItem, featureCompletedWork, client);

                            stats["FeaturesUpdated"]++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Feature {featureId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                });

                // Re-aggregate completed work for affected Epics
                await Parallel.ForEachAsync(affectedEpics, CancellationToken, async (epicId, ct) =>
                {
                    try
                    {
                        var epicWorkItem = await client.GetWorkItem(epicId);
                        if (epicWorkItem != null)
                        {

                            Logger.Debug($"Re-aggregating completed work for Epic {epicId}: {epicWorkItem.Title}");

                            // Calculate completed work from all descendant tasks (through Feature/PBI/Bug/Glitch children)
                            var epicCompletedWork = await CalculateEpicCompletedWorkFromAllDescendants(epicWorkItem, disciplineMappings, client);

                            // Update Epic completed work fields only (estimation/remaining handled elsewhere)
                            epicWorkItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", Math.Round(epicCompletedWork["TotalCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.DevelopmentCompletedWork", Math.Round(epicCompletedWork["DevelopmentCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.QACompletedWork", Math.Round(epicCompletedWork["QACompletedWork"], 2));
                            epicWorkItem.SetField("Custom.POCompletedWork", Math.Round(epicCompletedWork["POCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.AdminCompletedWork", Math.Round(epicCompletedWork["AdminCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.OthersCompletedWork", Math.Round(epicCompletedWork["OthersCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.InfraCompletedWork", Math.Round(epicCompletedWork["InfraCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.CapabilitiesCompletedWork", Math.Round(epicCompletedWork["CapabilitiesCompletedWork"], 2));
                            epicWorkItem.SetField("Custom.UnProductiveCompletedWork", Math.Round(epicCompletedWork["UnProductiveCompletedWork"], 2));


                            //epicWorkItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                            await client.SaveWorkItem(epicWorkItem);

                            stats["EpicsUpdated"]++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error re-aggregating Epic {epicId} completed work: {ex.Message}");
                        stats["Errors"]++;
                    }
                });
            }

            #region Calculate Epic and Feature completed work from all child tasks

            // Calculate Feature completed work from all descendant tasks
            async Task<Dictionary<string, double>> CalculateFeatureCompletedWorkFromAllDescendants(WorkItem featureItem, Dictionary<string, string> disciplineMappings, IAzureDevOpsClient client)
            {
                const int HOURS_PER_DAY = 8;

                var aggregatedData = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0,
                    ["InfraCompletedWork"] = 0,
                    ["CapabilitiesCompletedWork"] = 0,
                    ["UnProductiveCompletedWork"] = 0
                };

                // Step 1: Get all PBI/Bug/Glitch children of this Feature
                // Note: We exclude the feature itself from results to ensure we only get actual child PBIs/Bugs/Glitches
                var childPBIsQuery = $@"SELECT [Target].[System.Id]
                           FROM WorkItemLinks
                           WHERE [Source].[System.Id] = {featureItem.Id}
                           AND [Source].[System.TeamProject] = 'PCLabs'
                           AND [Target].[System.TeamProject] = 'PCLabs'
                           AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'                         
                           AND [Target].[System.WorkItemType] IN ('Task', 'Product Backlog Item', 'Bug', 'Glitch')
                           AND [Target].[System.Id] <> {featureItem.Id}";

                var childPBIs = await client.QueryWorkItemsByWiql(childPBIsQuery);

                foreach (var pbi in childPBIs)
                {
                    if (pbi.Id == featureItem.Id) continue;
                    // Aggregate completed work fields from Feature (using simplified Custom.* field names)

                    // Skip features that are in "Removed" state
                    var childState = pbi.GetField<string>("System.State");
                    if (string.Equals(childState, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"Skipping feature child {pbi.Id} is in 'Removed' state");
                        continue;
                    }

                    // Handle Task items differently
                    if (pbi.WorkItemType == "Task")
                    {
                        var taskCompletedWork = pbi.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                        var taskCompletedDays = Math.Round(taskCompletedWork / HOURS_PER_DAY, 2);

                        aggregatedData["TotalCompletedWork"] += taskCompletedDays;

                        // Map task activity to discipline for task completed work
                        var activity = pbi.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";
                        if (disciplineMappings.TryGetValue(activity, out var discipline))
                        {
                            switch (discipline)
                            {
                                case "Development":
                                    aggregatedData["DevelopmentCompletedWork"] += taskCompletedDays;
                                    break;
                                case "QA":
                                    aggregatedData["QACompletedWork"] += taskCompletedDays;
                                    break;
                                case "PO":
                                    aggregatedData["POCompletedWork"] += taskCompletedDays;
                                    break;
                                case "Admin":
                                    aggregatedData["AdminCompletedWork"] += taskCompletedDays;
                                    break;
                                case "Others":
                                    aggregatedData["OthersCompletedWork"] += taskCompletedDays;
                                    break;
                                case "Infra":
                                    aggregatedData["InfraCompletedWork"] += taskCompletedDays;
                                    break;
                                case "Capabilities":
                                    aggregatedData["CapabilitiesCompletedWork"] += taskCompletedDays;
                                    break;
                                case "UnProductive":
                                    aggregatedData["UnProductiveCompletedWork"] += taskCompletedDays;
                                    break;
                            }
                        }
                        else
                        {
                            // Unknown activity goes to Others
                            aggregatedData["OthersCompletedWork"] += taskCompletedDays;
                        }
                    }
                    else
                    {
                        // Handle PBI/Bug/Glitch items (aggregate their already calculated completed work fields)
                        aggregatedData["TotalCompletedWork"] += pbi.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                        aggregatedData["DevelopmentCompletedWork"] += pbi.GetField<double?>("Custom.DevelopmentCompletedWork") ?? 0;
                        aggregatedData["QACompletedWork"] += pbi.GetField<double?>("Custom.QACompletedWork") ?? 0;
                        aggregatedData["POCompletedWork"] += pbi.GetField<double?>("Custom.POCompletedWork") ?? 0;
                        aggregatedData["AdminCompletedWork"] += pbi.GetField<double?>("Custom.AdminCompletedWork") ?? 0;
                        aggregatedData["OthersCompletedWork"] += pbi.GetField<double?>("Custom.OthersCompletedWork") ?? 0;
                        aggregatedData["InfraCompletedWork"] += pbi.GetField<double?>("Custom.InfraCompletedWork") ?? 0;
                        aggregatedData["CapabilitiesCompletedWork"] += pbi.GetField<double?>("Custom.CapabilitiesCompletedWork") ?? 0;
                        aggregatedData["UnProductiveCompletedWork"] += pbi.GetField<double?>("Custom.UnProductiveCompletedWork") ?? 0;
                    }
                }

                // Round all values to 2 decimal places
                var roundedAggregatedData = new Dictionary<string, double>();
                foreach (var kvp in aggregatedData)
                {
                    roundedAggregatedData[kvp.Key] = Math.Round(kvp.Value, 2);
                }

                return roundedAggregatedData;
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
                    ["OthersCompletedWork"] = 0,
                    ["InfraCompletedWork"] = 0,
                    ["CapabilitiesCompletedWork"] = 0,
                    ["UnProductiveCompletedWork"] = 0
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

                    // Skip features that are in "Removed" state
                    var featureState = feature.GetField<string>("System.State");
                    if (string.Equals(featureState, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"Skipping feature child {feature.Id} is in 'Removed' state");
                        continue;
                    }

                    aggregatedData["TotalCompletedWork"] += feature.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                    aggregatedData["DevelopmentCompletedWork"] += feature.GetField<double?>("Custom.DevelopmentCompletedWork") ?? 0;
                    aggregatedData["QACompletedWork"] += feature.GetField<double?>("Custom.QACompletedWork") ?? 0;
                    aggregatedData["POCompletedWork"] += feature.GetField<double?>("Custom.POCompletedWork") ?? 0;
                    aggregatedData["AdminCompletedWork"] += feature.GetField<double?>("Custom.AdminCompletedWork") ?? 0;
                    aggregatedData["OthersCompletedWork"] += feature.GetField<double?>("Custom.OthersCompletedWork") ?? 0;
                    aggregatedData["InfraCompletedWork"] += feature.GetField<double?>("Custom.InfraCompletedWork") ?? 0;
                    aggregatedData["CapabilitiesCompletedWork"] += feature.GetField<double?>("Custom.CapabilitiesCompletedWork") ?? 0;
                    aggregatedData["UnProductiveCompletedWork"] += feature.GetField<double?>("Custom.UnProductiveCompletedWork") ?? 0;

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
                workItem.SetField("Custom.InfraCompletedWork", aggregatedData["InfraCompletedWork"]);
                workItem.SetField("Custom.CapabilitiesCompletedWork", aggregatedData["CapabilitiesCompletedWork"]);
                workItem.SetField("Custom.UnProductiveCompletedWork", aggregatedData["UnProductiveCompletedWork"]);

                //workItem.SetField("Custom.LastUpdated", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));

                await client.SaveWorkItem(workItem);

                Logger.Debug($"Updated {workItem.WorkItemType} {workItem.Id} completed work - Total: {aggregatedData["TotalCompletedWork"]}, Dev: {aggregatedData["DevelopmentCompletedWork"]}, QA: {aggregatedData["QACompletedWork"]}");
            }



            #endregion

            // Calculate aggregated completed work data for a work item from its child tasks
            async Task<Dictionary<string, double>> CalculateCompletedWorkAggregation(WorkItem parentItem, Dictionary<string, string> disciplineMappings, IAzureDevOpsClient client)
            {
                const int HOURS_PER_DAY = 8;

                var aggregatedData = new Dictionary<string, double>
                {
                    ["TotalCompletedWork"] = 0,
                    ["DevelopmentCompletedWork"] = 0,
                    ["QACompletedWork"] = 0,
                    ["POCompletedWork"] = 0,
                    ["AdminCompletedWork"] = 0,
                    ["OthersCompletedWork"] = 0,
                    ["InfraCompletedWork"] = 0,
                    ["CapabilitiesCompletedWork"] = 0,
                    ["UnProductiveCompletedWork"] = 0
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

                    // Skip task are in "Removed" state
                    var taskState = task.GetField<string>("System.State");
                    if (string.Equals(taskState, "Removed", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Debug($"Skipping task child {task.Id} is in 'Removed' state");
                        continue;
                    }


                    var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
                    var activity = task.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";

                    if (completedWork > 0)
                    {
                        var completedDays = Math.Round(completedWork / HOURS_PER_DAY, 2);
                        aggregatedData["TotalCompletedWork"] += completedDays;

                        // Map activity to discipline
                        if (disciplineMappings.TryGetValue(activity, out var discipline))
                        {
                            switch (discipline)
                            {
                                case "Development":
                                    aggregatedData["DevelopmentCompletedWork"] += completedDays;
                                    break;
                                case "QA":
                                    aggregatedData["QACompletedWork"] += completedDays;
                                    break;
                                case "PO":
                                    aggregatedData["POCompletedWork"] += completedDays;
                                    break;
                                case "Admin":
                                    aggregatedData["AdminCompletedWork"] += completedDays;
                                    break;
                                case "Others":
                                    aggregatedData["OthersCompletedWork"] += completedDays;
                                    break;
                                case "Infra":
                                    aggregatedData["InfraCompletedWork"] += completedDays;
                                    break;
                                case "Capabilities":
                                    aggregatedData["CapabilitiesCompletedWork"] += completedDays;
                                    break;
                                case "UnProductive":
                                    aggregatedData["UnProductiveCompletedWork"] += completedDays;
                                    break;

                            }
                        }
                        else
                        {
                            // Unknown activity goes to Others
                            aggregatedData["OthersCompletedWork"] += completedDays;
                        }
                    }
                }

                return aggregatedData;
            }


            async Task CalculateAffectedParentWorkItems(ILogger Logger, List<WorkItem> changedTasks, IAzureDevOpsClient client, HashSet<int> affectedPBIs, HashSet<int> affectedBugs, HashSet<int> affectedGlitches, HashSet<int> affectedFeatures, HashSet<int> affectedEpics)
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

            // Find Feature parents for removed PBI/Bug/Glitch items
            async Task FindFeatureParentsForRemovedItems(ILogger Logger, List<WorkItem> removedItems, IAzureDevOpsClient client, HashSet<int> affectedFeatures)
            {
                if (removedItems.Count == 0) return;

                const int batchSize = 50;
                var itemBatches = removedItems.Select((item, index) => new { item, index })
                                             .GroupBy(x => x.index / batchSize)
                                             .Select(g => g.Select(x => x.item).ToList())
                                             .ToList();

                foreach (var itemBatch in itemBatches)
                {
                    var itemIds = string.Join(",", itemBatch.Select(i => i.Id));

                    var featureParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                                                FROM WorkItemLinks
                                                WHERE [Source].[System.Id] IN ({itemIds})
                                                AND [Source].[System.TeamProject] = 'PCLabs'
                                                AND [Target].[System.TeamProject] = 'PCLabs'
                                                AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                                                AND [Target].[System.WorkItemType] = 'Feature'";

                    var featureParents = await client.QueryWorkItemsByWiql(featureParentsQuery);

                    foreach (var parent in featureParents)
                    {
                        if (parent.WorkItemType == "Feature")
                        {
                            affectedFeatures.Add(parent.Id);
                        }
                    }
                }
            }

            // Find Epic parents for removed Feature items
            async Task FindEpicParentsForRemovedItems(ILogger Logger, List<WorkItem> removedFeatures, IAzureDevOpsClient client, HashSet<int> affectedEpics)
            {
                if (removedFeatures.Count == 0) return;

                const int batchSize = 50;
                var featureBatches = removedFeatures.Select((feature, index) => new { feature, index })
                                                   .GroupBy(x => x.index / batchSize)
                                                   .Select(g => g.Select(x => x.feature).ToList())
                                                   .ToList();

                foreach (var featureBatch in featureBatches)
                {
                    var featureIds = string.Join(",", featureBatch.Select(f => f.Id));

                    var epicParentsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
                                            FROM WorkItemLinks
                                            WHERE [Source].[System.Id] IN ({featureIds})
                                            AND [Source].[System.TeamProject] = 'PCLabs'
                                            AND [Target].[System.TeamProject] = 'PCLabs'
                                            AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Reverse'
                                            AND [Target].[System.WorkItemType] = 'Epic'";

                    var epicParents = await client.QueryWorkItemsByWiql(epicParentsQuery);

                    foreach (var parent in epicParents)
                    {
                        if (parent.WorkItemType == "Epic")
                        {
                            affectedEpics.Add(parent.Id);
                        }
                    }
                }
            }

            // Check for removed work items using paging to avoid query size limits
            async Task<int> CheckForRemovedWorkItems(ILogger Logger, DateTime LastRun, IAzureDevOpsClient client)
            {
                Logger.Debug("Checking for work items in 'Removed' state that have changed since last run using paging");

                var removedWorkItemsCount = 0;
                const int pageSize = 200;
                var skip = 0;
                var hasMoreResults = true;
                DateTime? lastProcessedDate = null;

                while (hasMoreResults)
                {
                    try
                    {
                        // Query for all work items in "Removed" state using paging
                        var removedWorkItemsQuery = $@"SELECT [System.Id], [System.ChangedDate]
                                                     FROM WorkItems 
                                                     WHERE [System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Feature', 'Epic', 'Task')
                                                     AND [System.TeamProject] = 'PCLabs'
                                                     AND [System.State] = 'Removed'";

                        // Add date filtering for paging - use date-only format
                        if (lastProcessedDate.HasValue)
                        {
                            var lastProcessedDateOnly = lastProcessedDate.Value.ToString("yyyy-MM-dd");
                            removedWorkItemsQuery += $" AND [System.ChangedDate] < '{lastProcessedDateOnly}'";
                        }

                        removedWorkItemsQuery += " ORDER BY [System.ChangedDate] DESC";

                        // Use the top parameter in the API call, not in the query
                        var pageResults = await client.QueryWorkItemsByWiql(removedWorkItemsQuery, pageSize);

                        if (pageResults.Count == 0)
                        {
                            hasMoreResults = false;
                            break;
                        }

                        // Filter with precise UTC comparison and count
                        var filteredPageResults = pageResults.Where(item =>
                        {
                            var changedDate = item.GetField<DateTime?>("System.ChangedDate");
                            return changedDate.HasValue && changedDate.Value.ToUniversalTime() >= LastRun.ToUniversalTime();
                        }).ToList();

                        removedWorkItemsCount += filteredPageResults.Count;

                        // Update the last processed date for next iteration
                        if (pageResults.Count > 0)
                        {
                            lastProcessedDate = pageResults.Last().GetField<DateTime?>("System.ChangedDate");
                        }

                        // If we got fewer results than page size, we've reached the end
                        if (pageResults.Count < pageSize)
                        {
                            hasMoreResults = false;
                        }

                        // If this page has no results matching our date filter and we're getting older records,
                        // we can stop as subsequent pages will be even older
                        if (filteredPageResults.Count == 0 && pageResults.Count > 0)
                        {
                            var oldestInPage = pageResults.Min(item => item.GetField<DateTime?>("System.ChangedDate"));
                            if (oldestInPage.HasValue && oldestInPage.Value.ToUniversalTime() < LastRun.ToUniversalTime())
                            {
                                Logger.Debug("Reached removed items older than LastRun, stopping pagination");
                                hasMoreResults = false;
                            }
                        }

                        skip += pageSize;

                        // Safety limit to prevent infinite loops
                        if (skip > 50000)
                        {
                            Logger.Warning($"Reached safety limit of 50,000 removed items processed, stopping pagination");
                            hasMoreResults = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error fetching removed items page starting at {skip}: {ex.Message}");
                        hasMoreResults = false;
                    }
                }

                Logger.Debug($"Found {removedWorkItemsCount} work items in 'Removed' state for early exit check");
                return removedWorkItemsCount;
            }

            // Add work items in "Removed" state to affected collections using paging
            async Task AddRemovedWorkItemsToAffectedCollections(ILogger Logger, DateTime LastRun, IAzureDevOpsClient client, HashSet<int> affectedPBIs, HashSet<int> affectedBugs, HashSet<int> affectedGlitches, HashSet<int> affectedFeatures, HashSet<int> affectedEpics, ConcurrentDictionary<string, int> stats)
            {
                Logger.Information("Finding work items in 'Removed' state that have changed since last run using paging");

                var allRemovedWorkItems = new List<WorkItem>();
                const int pageSize = 200;
                var skip = 0;
                var hasMoreResults = true;
                DateTime? lastProcessedDate = null;

                while (hasMoreResults)
                {
                    try
                    {
                        // Query for all work items in "Removed" state using paging
                        var removedWorkItemsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], [System.ChangedDate]
                                                     FROM WorkItems 
                                                     WHERE [System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Feature', 'Epic', 'Task')
                                                     AND [System.TeamProject] = 'PCLabs'
                                                     AND [System.State] = 'Removed'";

                        // Add date filtering for paging - use date-only format
                        if (lastProcessedDate.HasValue)
                        {
                            var lastProcessedDateOnly = lastProcessedDate.Value.ToString("yyyy-MM-dd");
                            removedWorkItemsQuery += $" AND [System.ChangedDate] < '{lastProcessedDateOnly}'";
                        }

                        removedWorkItemsQuery += " ORDER BY [System.ChangedDate] DESC";

                        // Use the top parameter in the API call, not in the query
                        var pageResults = await client.QueryWorkItemsByWiql(removedWorkItemsQuery, pageSize);

                        if (pageResults.Count == 0)
                        {
                            hasMoreResults = false;
                            break;
                        }

                        // Filter with precise UTC comparison
                        var filteredPageResults = pageResults.Where(item =>
                        {
                            var changedDate = item.GetField<DateTime?>("System.ChangedDate");
                            return changedDate.HasValue && changedDate.Value.ToUniversalTime() >= LastRun.ToUniversalTime();
                        }).ToList();

                        allRemovedWorkItems.AddRange(filteredPageResults);

                        Logger.Debug($"Removed items page {skip / pageSize + 1}: Retrieved {pageResults.Count} items, {filteredPageResults.Count} match date filter");

                        // Update the last processed date for next iteration
                        if (pageResults.Count > 0)
                        {
                            lastProcessedDate = pageResults.Last().GetField<DateTime?>("System.ChangedDate");
                        }

                        // If we got fewer results than page size, we've reached the end
                        if (pageResults.Count < pageSize)
                        {
                            hasMoreResults = false;
                        }

                        // If this page has no results matching our date filter and we're getting older records,
                        // we can stop as subsequent pages will be even older
                        if (filteredPageResults.Count == 0 && pageResults.Count > 0)
                        {
                            var oldestInPage = pageResults.Min(item => item.GetField<DateTime?>("System.ChangedDate"));
                            if (oldestInPage.HasValue && oldestInPage.Value.ToUniversalTime() < LastRun.ToUniversalTime())
                            {
                                Logger.Debug("Reached removed items older than LastRun, stopping pagination");
                                hasMoreResults = false;
                            }
                        }

                        skip += pageSize;

                        // Safety limit to prevent infinite loops
                        if (skip > 50000)
                        {
                            Logger.Warning($"Reached safety limit of 50,000 removed items processed, stopping pagination");
                            hasMoreResults = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Error fetching removed items page starting at {skip}: {ex.Message}");
                        hasMoreResults = false;
                    }
                }

                Logger.Information($"Found {allRemovedWorkItems.Count} work items in 'Removed' state that have changed since last run");

                if (allRemovedWorkItems.Count == 0)
                {
                    Logger.Debug("No removed work items found - skipping removed items processing");
                    return;
                }

                // Separate removed items by type for targeted parent queries
                var removedTasks = allRemovedWorkItems.Where(wi => wi.WorkItemType == "Task").ToList();
                var removedPBIs = allRemovedWorkItems.Where(wi => wi.WorkItemType == "Product Backlog Item").ToList();
                var removedBugs = allRemovedWorkItems.Where(wi => wi.WorkItemType == "Bug").ToList();
                var removedGlitches = allRemovedWorkItems.Where(wi => wi.WorkItemType == "Glitch").ToList();
                var removedFeatures = allRemovedWorkItems.Where(wi => wi.WorkItemType == "Feature").ToList();

                Logger.Debug($"Removed items breakdown: {removedTasks.Count} tasks, {removedPBIs.Count} PBIs, {removedBugs.Count} bugs, {removedGlitches.Count} glitches, {removedFeatures.Count} features");

                // For removed tasks, find their PBI/Bug/Glitch/Feature/Epic parents
                if (removedTasks.Count > 0)
                {
                    await CalculateAffectedParentWorkItems(Logger, removedTasks, client, affectedPBIs, affectedBugs, affectedGlitches, affectedFeatures, affectedEpics);
                    Logger.Debug($"Added parents of {removedTasks.Count} removed tasks to affected collections");
                }

                // For removed PBIs/Bugs/Glitches, find their Feature parents
                var removedWorkItemsNeedingFeatureParents = new List<WorkItem>();
                removedWorkItemsNeedingFeatureParents.AddRange(removedPBIs);
                removedWorkItemsNeedingFeatureParents.AddRange(removedBugs);
                removedWorkItemsNeedingFeatureParents.AddRange(removedGlitches);

                if (removedWorkItemsNeedingFeatureParents.Count > 0)
                {
                    await FindFeatureParentsForRemovedItems(Logger, removedWorkItemsNeedingFeatureParents, client, affectedFeatures);
                    Logger.Debug($"Added Feature parents of {removedWorkItemsNeedingFeatureParents.Count} removed PBI/Bug/Glitch items to affected collections");
                }

                // For removed Features, find their Epic parents
                if (removedFeatures.Count > 0)
                {
                    await FindEpicParentsForRemovedItems(Logger, removedFeatures, client, affectedEpics);
                    Logger.Debug($"Added Epic parents of {removedFeatures.Count} removed Feature items to affected collections");
                }

                Logger.Information($"Completed processing removed work items - added parents to affected collections for recalculation");

                // Update statistics for removed items processed
                if (stats.ContainsKey("RemovedItemsProcessed"))
                {
                    stats["RemovedItemsProcessed"] = allRemovedWorkItems.Count;
                }
            }
        }
    }
}