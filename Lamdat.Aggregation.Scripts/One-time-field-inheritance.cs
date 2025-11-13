using Lamdat.ADOAutomationTool.Entities;
using Lamdat.ADOAutomationTool.ScriptEngine;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Lamdat.Aggregation.Scripts
{
    internal class OneTimeFieldInheritance
    {

        public static async Task<ScheduledScriptResult> Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken CancellationToken, string ScriptRunId, DateTime LastRun)
        {
            // Historical Epic Field Inheritance Migration Script
            // ONE-TIME EXECUTION: This script backfills field inheritance from 2024 onwards
            // It processes ALL Epics and their descendants to ensure historical data consistency
            //
            // Hierarchy: 
            // - Epic -> Feature
            // - Feature -> (PBI, Bug, Glitch, Task)
            // - PBI -> (Bug, Glitch, Task)
            // - Bug/Glitch -> Task
            // - Feature/PBI/Bug -> Test Case (via "Tested By" relationship)
            // Test Cases can be linked to Feature, PBI, Bug via "Tested By" relationship
            //
            // Fields inherited: Labs.Category, Labs.ProjectCode
            //
            // NOTE: This script should be run ONCE to backfill historical data, then disabled/removed
            // The regular epic-field-inheritance.rule script handles ongoing updates

            Logger.Information("=================================================================");
            Logger.Information("Starting HISTORICAL Epic field inheritance migration (from 2024)");
            Logger.Information("=================================================================");
            Logger.Information($"This is a ONE-TIME migration script");
            Logger.Information($"Script started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // Set the project
                Client.Project = "Backup-Tests";

                var user = await Client.WhoAmI();
                Logger.Information($"Running as: {user?.Identity?.DisplayName}");
                Logger.Information($"Working with project: {Client.Project}");

                // Fields to inherit from Epic
                var fieldsToInherit = new[] { "Labs.Category", "Labs.ProjectCode" };

                // Migration start date - from January 1, 2024
                var migrationStartDate = "2024-01-01";
                Logger.Information($"Processing all Epics changed since: {migrationStartDate}");

                // Step 1: Find all Epics that have changed since 2024
                var allEpics = new List<WorkItem>();
                const int pageSize = 200;
                int? lastEpicId = null;
                bool hasMoreEpics = true;

                Logger.Information("Fetching ALL Epics from 2024 onwards with paging");

                while (hasMoreEpics)
                {
                    string epicsQuery;

                    if (lastEpicId == null)
                    {
                        epicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
       [Labs.Category], [Labs.ProjectCode]
        FROM WorkItems 
     WHERE [System.WorkItemType] = 'Epic' 
      AND [System.TeamProject] = 'Backup-Tests'
  AND [System.ChangedDate] >= '{migrationStartDate}'
       ORDER BY [System.Id]";
                    }
                    else
                    {
                        epicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
      [Labs.Category], [Labs.ProjectCode]
     FROM WorkItems 
WHERE [System.WorkItemType] = 'Epic' 
          AND [System.TeamProject] = 'Backup-Tests'
               AND [System.ChangedDate] >= '{migrationStartDate}'
  AND [System.Id] > {lastEpicId}
  ORDER BY [System.Id]";
                    }

                    var pageResults = await Client.QueryWorkItemsByWiql(epicsQuery, pageSize);

                    if (pageResults.Count == 0)
                    {
                        hasMoreEpics = false;
                        Logger.Information($"No more Epics found, paging complete. Total Epics fetched: {allEpics.Count}");
                    }
                    else
                    {
                        allEpics.AddRange(pageResults);
                        lastEpicId = pageResults.Last().Id;

                        Logger.Information($"Fetched page with {pageResults.Count} Epics, last ID: {lastEpicId}, total so far: {allEpics.Count}");

                        if (pageResults.Count < pageSize)
                        {
                            hasMoreEpics = false;
                            Logger.Information($"Received fewer results than page size ({pageResults.Count} < {pageSize}), paging complete");
                        }
                    }
                }

                Logger.Information($"========================================================");
                Logger.Information($"Found {allEpics.Count} total Epics to process");
                Logger.Information($"========================================================");

                if (allEpics.Count == 0)
                {
                    Logger.Information("No Epics found - migration complete");
                    return ScheduledScriptResult.Success(1440, "Migration complete - no Epics to process");
                }

                // Step 2: Process each Epic and update all its descendants
                int totalEpicsProcessed = 0;
                int totalDescendantsUpdated = 0;
                int totalErrors = 0;
                var startTime = DateTime.Now;

                foreach (var epic in allEpics)
                {
                    try
                    {
                        totalEpicsProcessed++;
                        Logger.Information($"Processing Epic {totalEpicsProcessed}/{allEpics.Count}: {epic.Id} - {epic.Title}");

                        // Get values from Epic
                        var categoryValue = epic.GetField<string>("Labs.Category");
                        var projectCodeValue = epic.GetField<string>("Labs.ProjectCode");

                        Logger.Debug($"Epic {epic.Id} - Category: '{categoryValue}', ProjectCode: '{projectCodeValue}'");

                        // If Epic has no values to inherit, skip
                        if (string.IsNullOrEmpty(categoryValue) && string.IsNullOrEmpty(projectCodeValue))
                        {
                            Logger.Debug($"Epic {epic.Id} has no values to inherit - skipping");
                            continue;
                        }

                        // Step 2.1: Get all descendant work items recursively
                        var allDescendants = new HashSet<int>();
                        var descendantProcessingStart = DateTime.UtcNow;
                        Logger.Debug($"Starting recursive descendant collection for Epic {epic.Id}");

                        // Get direct children (Features, and potentially PBIs, Bugs, Glitches, Tasks)
                        var directDescendantsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
       WHERE [Source].[System.Id] = {epic.Id}
        AND [Source].[System.TeamProject] = 'Backup-Tests'
      AND [Target].[System.TeamProject] = 'Backup-Tests'
      AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
       AND [Target].[System.WorkItemType] IN ('Feature', 'Product Backlog Item', 'Bug', 'Glitch', 'Task')
 ORDER BY [Target].[System.Id]";

                        var directDescendants = await Client.QueryWorkItemsByWiql(directDescendantsQuery);
                        Logger.Debug($"Found {directDescendants.Count} direct descendants for Epic {epic.Id}");

                        // Process each direct descendant recursively
                        foreach (var descendant in directDescendants)
                        {
                            allDescendants.Add(descendant.Id);
                            Logger.Debug($"Processing descendant {descendant.WorkItemType} {descendant.Id}");

                            // If it's a Feature or PBI, get its children (PBI, Bug, Glitch, Task for Feature; Bug, Glitch, Task for PBI)
                            if (descendant.WorkItemType == "Feature" || descendant.WorkItemType == "Product Backlog Item")
                            {
                                Logger.Debug($"Querying children for {descendant.WorkItemType} {descendant.Id}");
                                var childrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
           FROM WorkItemLinks
    WHERE [Source].[System.Id] = {descendant.Id}
     AND [Source].[System.TeamProject] = 'Backup-Tests'
        AND [Target].[System.TeamProject] = 'Backup-Tests'
    AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
 AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Task')";

                                var children = await Client.QueryWorkItemsByWiql(childrenQuery);
                                Logger.Debug($"Found {children.Count} children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var child in children)
                                {
                                    allDescendants.Add(child.Id);

                                    // If it's a PBI/Bug/Glitch, get its children (Bug, Glitch, Task)
                                    if (child.WorkItemType == "Product Backlog Item" || child.WorkItemType == "Bug" || child.WorkItemType == "Glitch")
                                    {
                                        Logger.Debug($"Querying grandchildren for {child.WorkItemType} {child.Id}");
                                        var grandchildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
 FROM WorkItemLinks
      WHERE [Source].[System.Id] = {child.Id}
 AND [Source].[System.TeamProject] = 'Backup-Tests'
 AND [Target].[System.TeamProject] = 'Backup-Tests'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
    AND [Target].[System.WorkItemType] IN ('Bug', 'Glitch', 'Task')";

                                        var grandchildren = await Client.QueryWorkItemsByWiql(grandchildrenQuery);
                                        Logger.Debug($"Found {grandchildren.Count} grandchildren for {child.WorkItemType} {child.Id}");

                                        foreach (var grandchild in grandchildren)
                                        {
                                            allDescendants.Add(grandchild.Id);
                                        }
                                    }
                                }
                            }
                            // If it's a Bug or Glitch (direct child of Epic, though not in standard hierarchy), get its Task children
                            else if (descendant.WorkItemType == "Bug" || descendant.WorkItemType == "Glitch")
                            {
                                Logger.Debug($"Querying task children for {descendant.WorkItemType} {descendant.Id}");
                                var taskChildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
 WHERE [Source].[System.Id] = {descendant.Id}
  AND [Source].[System.TeamProject] = 'Backup-Tests'
   AND [Target].[System.TeamProject] = 'Backup-Tests'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
     AND [Target].[System.WorkItemType] = 'Task'";

                                var taskChildren = await Client.QueryWorkItemsByWiql(taskChildrenQuery);
                                Logger.Debug($"Found {taskChildren.Count} task children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var task in taskChildren)
                                {
                                    allDescendants.Add(task.Id);
                                }
                            }
                        }

                        var descendantProcessingDuration = DateTime.UtcNow - descendantProcessingStart;
                        Logger.Debug($"Completed recursive descendant collection in {descendantProcessingDuration.TotalSeconds:F2} seconds");

                        // Step 2.1.1: Find Test Cases linked to Feature/PBI/Bug via "Tested By" relationship
                        Logger.Debug($"Querying Test Cases linked via 'Tested By' relationship");
                        var testCaseProcessingStart = DateTime.UtcNow;

                        // Get all Features, PBIs, and Bugs from allDescendants
                        var testableWorkItems = allDescendants.Where(id =>
                        {
                            var workItem = directDescendants.FirstOrDefault(d => d.Id == id);
                            return workItem != null && (workItem.WorkItemType == "Feature" ||
                                                          workItem.WorkItemType == "Product Backlog Item" ||
                                                          workItem.WorkItemType == "Bug");
                        }).ToList();

                        Logger.Debug($"Found {testableWorkItems.Count} work items that can have Test Cases");

                        foreach (var testableWorkItemId in testableWorkItems)
                        {
                            var testCasesQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
   WHERE [Source].[System.Id] = {testableWorkItemId}
     AND [Source].[System.TeamProject] = 'Backup-Tests'
     AND [Target].[System.TeamProject] = 'Backup-Tests'
     AND [System.Links.LinkType] = 'Microsoft.VSTS.Common.TestedBy-Reverse'
  AND [Target].[System.WorkItemType] = 'Test Case'";

                            var testCases = await Client.QueryWorkItemsByWiql(testCasesQuery);
                            Logger.Debug($"Found {testCases.Count} Test Cases linked to work item {testableWorkItemId}");

                            foreach (var testCase in testCases)
                            {
                                allDescendants.Add(testCase.Id);
                            }
                        }

                        var testCaseProcessingDuration = DateTime.UtcNow - testCaseProcessingStart;
                        Logger.Debug($"Completed Test Case collection in {testCaseProcessingDuration.TotalSeconds:F2} seconds");

                        Logger.Information($"Found {allDescendants.Count} total descendants for Epic {epic.Id} (including Test Cases)");

                        // Step 2.2: Update each descendant work item (in parallel batches for better performance)
                        var descendantsUpdated = 0;
                        var errors = 0;
                        var descendantsList = allDescendants.ToList();
                        const int batchSize = 20; // Process 20 work items at a time
                        var totalBatches = (descendantsList.Count + batchSize - 1) / batchSize;

                        if (descendantsList.Count > 0)
                        {
                            Logger.Information($"Processing {descendantsList.Count} descendants in {totalBatches} batches of up to {batchSize} work items");

                            for (int batchIndex = 0; batchIndex < descendantsList.Count; batchIndex += batchSize)
                            {
                                var batch = descendantsList.Skip(batchIndex).Take(batchSize).ToList();
                                var currentBatch = (batchIndex / batchSize) + 1;

                                Logger.Debug($"Processing batch {currentBatch}/{totalBatches} ({batch.Count} work items)");

                                var batchTasks = batch.Select(async descendantId =>
                                {
                                    try
                                    {
                                        var descendantWorkItem = await Client.GetWorkItem(descendantId);
                                        if (descendantWorkItem == null) return false;

                                        // Skip work items in "Removed" state
                                        var state = descendantWorkItem.GetField<string>("System.State");
                                        if (string.Equals(state, "Removed", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Logger.Debug($"Skipping {descendantWorkItem.WorkItemType} {descendantId} in 'Removed' state");
                                            return false;
                                        }

                                        bool needsUpdate = false;

                                        // Update Category field
                                        if (!string.IsNullOrEmpty(categoryValue))
                                        {
                                            var currentCategory = descendantWorkItem.GetField<string>("Labs.Category");
                                            if (currentCategory != categoryValue)
                                            {
                                                descendantWorkItem.SetField("Labs.Category", categoryValue);
                                                needsUpdate = true;
                                                Logger.Debug($"Updated Category for {descendantWorkItem.WorkItemType} {descendantId}: '{currentCategory}' -> '{categoryValue}'");
                                            }
                                        }

                                        // Update ProjectCode field
                                        if (!string.IsNullOrEmpty(projectCodeValue))
                                        {
                                            var currentProjectCode = descendantWorkItem.GetField<string>("Labs.ProjectCode");
                                            if (currentProjectCode != projectCodeValue)
                                            {
                                                descendantWorkItem.SetField("Labs.ProjectCode", projectCodeValue);
                                                needsUpdate = true;
                                                Logger.Debug($"Updated ProjectCode for {descendantWorkItem.WorkItemType} {descendantId}: '{currentProjectCode}' -> '{projectCodeValue}'");
                                            }
                                        }

                                        if (needsUpdate)
                                        {
                                            await Client.SaveWorkItem(descendantWorkItem);
                                            Logger.Debug($"Updated {descendantWorkItem.WorkItemType} {descendantId}");
                                            return true;
                                        }

                                        return false;
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warning($"Error updating descendant work item {descendantId}: {ex.Message}");
                                        Interlocked.Increment(ref errors);
                                        return false;
                                    }
                                }).ToList();

                                // Wait for current batch to complete before starting next batch
                                var results = await Task.WhenAll(batchTasks);
                                var batchUpdated = results.Count(r => r);
                                descendantsUpdated += batchUpdated;

                                Logger.Debug($"Batch {currentBatch}/{totalBatches} completed - updated {batchUpdated} work items");
                            }
                        }

                        totalDescendantsUpdated += descendantsUpdated;
                        totalErrors += errors;

                        Logger.Information($"Completed Epic {totalEpicsProcessed}/{allEpics.Count} - updated {descendantsUpdated} descendants, {errors} errors");

                        // Log progress every 10 Epics
                        if (totalEpicsProcessed % 10 == 0)
                        {
                            var elapsed = DateTime.Now - startTime;
                            var avgPerEpic = elapsed.TotalSeconds / totalEpicsProcessed;
                            var estimatedRemaining = TimeSpan.FromSeconds(avgPerEpic * (allEpics.Count - totalEpicsProcessed));

                            Logger.Information($"========================================================");
                            Logger.Information($"PROGRESS: {totalEpicsProcessed}/{allEpics.Count} Epics ({(totalEpicsProcessed * 100.0 / allEpics.Count):F1}%)");
                            Logger.Information($"Time elapsed: {elapsed:hh\\:mm\\:ss}");
                            Logger.Information($"Estimated remaining: {estimatedRemaining:hh\\:mm\\:ss}");
                            Logger.Information($"Total descendants updated so far: {totalDescendantsUpdated}");
                            Logger.Information($"========================================================");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Error processing Epic {epic.Id}: {ex.Message}");
                        totalErrors++;
                    }
                }

                var totalElapsed = DateTime.Now - startTime;

                Logger.Information($"=================================================================");
                Logger.Information($"HISTORICAL MIGRATION COMPLETED");
                Logger.Information($"=================================================================");
                Logger.Information($"  - Total Epics processed: {totalEpicsProcessed}");
                Logger.Information($"  - Total descendants updated: {totalDescendantsUpdated}");
                Logger.Information($"  - Total errors: {totalErrors}");
                Logger.Information($"  - Total time: {totalElapsed:hh\\:mm\\:ss}");
                Logger.Information($"=================================================================");
                Logger.Information($"IMPORTANT: This migration script should now be DISABLED");
                Logger.Information($"=================================================================");

                var message = $"Migration complete: {totalEpicsProcessed} Epics, {totalDescendantsUpdated} descendants updated";

                // Return with a long interval (1 day) since this is a one-time migration
                // In production, this script should be disabled after successful execution
                return ScheduledScriptResult.Success(1440, message);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Historical migration failed");
                return ScheduledScriptResult.Success(60, $"Migration failed, will retry in 60 minutes: {ex.Message}");
            }

        }
    }
}
