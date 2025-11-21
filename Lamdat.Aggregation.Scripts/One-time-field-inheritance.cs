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
                // Check cancellation at start
                CancellationToken.ThrowIfCancellationRequested();

                // Set the project
                Client.Project = "PCLabs";

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
                const int pageSize = 1000; // OPTIMIZATION: Increased from 200 to 1000
                int? lastEpicId = null;
                bool hasMoreEpics = true;

                Logger.Information("Fetching ALL Epics from 2024 onwards with paging");

                while (hasMoreEpics)
                {
                    // Check cancellation before each page
                    CancellationToken.ThrowIfCancellationRequested();

                    string epicsQuery;

                    if (lastEpicId == null)
                    {
                        epicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
       [Labs.Category], [Labs.ProjectCode]
        FROM WorkItems 
     WHERE [System.WorkItemType] = 'Epic' 
      AND [System.TeamProject] = 'PCLabs'
  AND [System.ChangedDate] >= '{migrationStartDate}'
       ORDER BY [System.Id]";
                    }
                    else
                    {
                        epicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
      [Labs.Category], [Labs.ProjectCode]
     FROM WorkItems 
WHERE [System.WorkItemType] = 'Epic' 
          AND [System.TeamProject] = 'PCLabs'
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

                // Check cancellation after fetching epics
                CancellationToken.ThrowIfCancellationRequested();

                if (allEpics.Count == 0)
                {
                    Logger.Information("No Epics found - migration complete");
                    return ScheduledScriptResult.Success(1440, "Migration complete - no Epics to process");
                }

                // Step 2: Process each Epic and update all its descendants
                // Use thread-safe counters for parallel processing
                var totalDescendantsUpdated = 0;
                var totalErrors = 0;
                var epicsProcessedCount = 0;
                var startTime = DateTime.Now;

                Logger.Information($"Starting parallel processing of {allEpics.Count} Epics...");

                // OPTIMIZATION: Increased parallelism from sequential to 10 epics at a time
                const int maxDegreeOfParallelism = 10;
                var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

                var epicProcessingTasks = allEpics.Select(async (epic, epicIndex) =>
                {
                    await semaphore.WaitAsync(CancellationToken);
                    try
                    {
                        // Check cancellation at start of each epic processing
                        CancellationToken.ThrowIfCancellationRequested();

                        var epicNumber = epicIndex + 1;
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Starting Epic {epic.Id}: {epic.Title}");

                        // Get values from Epic
                        var categoryValue = epic.GetField<string>("Labs.Category");
                        var projectCodeValue = epic.GetField<string>("Labs.ProjectCode");

                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Category: '{categoryValue}', ProjectCode: '{projectCodeValue}'");

                        // If Epic has no values to inherit, skip
                        if (string.IsNullOrEmpty(categoryValue) && string.IsNullOrEmpty(projectCodeValue))
                        {
                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} has no values to inherit - skipping");
                            var skippedCount = Interlocked.Increment(ref epicsProcessedCount);
                            return (epic.Id, 0, 0);
                        }

                        // Check cancellation before querying descendants
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.1: Get all descendant work items recursively
                        var allDescendants = new ConcurrentBag<int>();
                        var descendantProcessingStart = DateTime.UtcNow;
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Starting recursive descendant collection");

                        // Get direct children (Features, and potentially PBIs, Bugs, Glitches, Tasks)
                        var directDescendantsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
       WHERE [Source].[System.Id] = {epic.Id}
        AND [Source].[System.TeamProject] = 'PCLabs'
      AND [Target].[System.TeamProject] = 'PCLabs'
      AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
       AND [Target].[System.WorkItemType] IN ('Feature', 'Product Backlog Item', 'Bug', 'Glitch', 'Task')
 ORDER BY [Target].[System.Id]";

                        var directDescendants = await Client.QueryWorkItemsByWiql(directDescendantsQuery);
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {directDescendants.Count} direct descendants");

                        // Check cancellation
                        CancellationToken.ThrowIfCancellationRequested();

                        // Add direct descendants
                        foreach (var descendant in directDescendants)
                        {
                            allDescendants.Add(descendant.Id);
                        }

                        // OPTIMIZATION: Parallel processing of descendants at each level
                        var featuresAndPBIs = directDescendants.Where(d => 
                            d.WorkItemType == "Feature" || d.WorkItemType == "Product Backlog Item").ToList();
                        
                        if (featuresAndPBIs.Count > 0)
                        {
                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying children for {featuresAndPBIs.Count} Features/PBIs in parallel");
                            
                            await Parallel.ForEachAsync(featuresAndPBIs, new ParallelOptions 
                            { 
                                MaxDegreeOfParallelism = 10,
                                CancellationToken = CancellationToken 
                            }, async (descendant, ct) =>
                            {
                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying children for {descendant.WorkItemType} {descendant.Id}");
                                var childrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
           FROM WorkItemLinks
    WHERE [Source].[System.Id] = {descendant.Id}
     AND [Source].[System.TeamProject] = 'PCLabs'
        AND [Target].[System.TeamProject] = 'PCLabs'
    AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
 AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Task')";

                                var children = await Client.QueryWorkItemsByWiql(childrenQuery);
                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {children.Count} children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var child in children)
                                {
                                    allDescendants.Add(child.Id);
                                }

                                // Collect PBIs/Bugs/Glitches for grandchildren processing
                                var pbisBugsGlitches = children.Where(c => 
                                    c.WorkItemType == "Product Backlog Item" || 
                                    c.WorkItemType == "Bug" || 
                                    c.WorkItemType == "Glitch").ToList();

                                // Query grandchildren in parallel
                                if (pbisBugsGlitches.Count > 0)
                                {
                                    await Parallel.ForEachAsync(pbisBugsGlitches, new ParallelOptions 
                                    { 
                                        MaxDegreeOfParallelism = 10,
                                        CancellationToken = ct 
                                    }, async (child, ct2) =>
                                    {
                                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying grandchildren for {child.WorkItemType} {child.Id}");
                                        var grandchildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
 FROM WorkItemLinks
      WHERE [Source].[System.Id] = {child.Id}
 AND [Source].[System.TeamProject] = 'PCLabs'
 AND [Target].[System.TeamProject] = 'PCLabs'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
    AND [Target].[System.WorkItemType] IN ('Bug', 'Glitch', 'Task')";

                                        var grandchildren = await Client.QueryWorkItemsByWiql(grandchildrenQuery);
                                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {grandchildren.Count} grandchildren for {child.WorkItemType} {child.Id}");

                                        foreach (var grandchild in grandchildren)
                                        {
                                            allDescendants.Add(grandchild.Id);
                                        }
                                    });
                                }
                            });
                        }

                        // Process direct Bugs/Glitches (though not standard hierarchy)
                        var directBugsGlitches = directDescendants.Where(d => 
                            d.WorkItemType == "Bug" || d.WorkItemType == "Glitch").ToList();
                        
                        if (directBugsGlitches.Count > 0)
                        {
                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying task children for {directBugsGlitches.Count} Bugs/Glitches in parallel");
                            
                            await Parallel.ForEachAsync(directBugsGlitches, new ParallelOptions 
                            { 
                                MaxDegreeOfParallelism = 10,
                                CancellationToken = CancellationToken 
                            }, async (descendant, ct) =>
                            {
                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying task children for {descendant.WorkItemType} {descendant.Id}");
                                var taskChildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
 WHERE [Source].[System.Id] = {descendant.Id}
  AND [Source].[System.TeamProject] = 'PCLabs'
   AND [Target].[System.TeamProject] = 'PCLabs'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
     AND [Target].[System.WorkItemType] = 'Task'";

                                var taskChildren = await Client.QueryWorkItemsByWiql(taskChildrenQuery);
                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {taskChildren.Count} task children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var task in taskChildren)
                                {
                                    allDescendants.Add(task.Id);
                                }
                            });
                        }

                        var descendantProcessingDuration = DateTime.UtcNow - descendantProcessingStart;
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Completed recursive descendant collection in {descendantProcessingDuration.TotalSeconds:F2} seconds");

                        // Check cancellation before test case processing
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.1.1: Find Test Cases linked to Feature/PBI/Bug via "Tested By" relationship
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying Test Cases linked via 'Tested By' relationship");
                        var testCaseProcessingStart = DateTime.UtcNow;

                        // Get unique work items to check (deduped)
                        var workItemsToCheck = new HashSet<int>(allDescendants);
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Checking {workItemsToCheck.Count} work items for Test Case links");

                        // OPTIMIZATION: Batch test case queries in chunks to reduce API calls
                        var foundTestCases = new ConcurrentBag<int>();
                        var workItemsList = workItemsToCheck.ToList();
                        const int testCaseBatchSize = 50; // OPTIMIZATION: Query 50 work items for test cases at once
                        
                        var testCaseBatches = new List<Task>();
                        for (int i = 0; i < workItemsList.Count; i += testCaseBatchSize)
                        {
                            var batch = workItemsList.Skip(i).Take(testCaseBatchSize).ToList();
                            
                            testCaseBatches.Add(Task.Run(async () =>
                            {
                                // Build a query for this batch
                                var sourceFilter = string.Join(" OR ", batch.Select(id => $"[Source].[System.Id] = {id}"));
                                
                                var batchTestCasesQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
   FROM WorkItemLinks
   WHERE ({sourceFilter})
 AND [Source].[System.TeamProject] = 'PCLabs'
  AND [Target].[System.TeamProject] = 'PCLabs'
     AND [System.Links.LinkType] = 'Microsoft.VSTS.Common.TestedBy-Forward'
  AND [Target].[System.WorkItemType] = 'Test Case'";

                                try
                                {
                                    var testCases = await Client.QueryWorkItemsByWiql(batchTestCasesQuery);
                                    
                                    if (testCases.Count > 0)
                                    {
                                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {testCases.Count} Test Cases in batch");

                                        foreach (var testCase in testCases)
                                        {
                                            // Exclude source work items
                                            if (!batch.Contains(testCase.Id))
                                            {
                                                foundTestCases.Add(testCase.Id);
                                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Added Test Case {testCase.Id}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Error querying Test Cases for batch: {ex.Message}");
                                }
                            }, CancellationToken));
                        }
                        
                        await Task.WhenAll(testCaseBatches);

                        // Add unique test cases to descendants
                        foreach (var testCaseId in foundTestCases.Distinct())
                        {
                            allDescendants.Add(testCaseId);
                        }

                        var testCaseProcessingDuration = DateTime.UtcNow - testCaseProcessingStart;
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Completed Test Case collection in {testCaseProcessingDuration.TotalSeconds:F2} seconds");
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {foundTestCases.Distinct().Count()} unique Test Cases");

                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {allDescendants.Count} total descendants (including Test Cases)");

                        // Check cancellation before batch processing
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.2: Update each descendant work item with inherited fields (in parallel batches for better performance)
                        var descendantsUpdated = 0;
                        var errors = 0;
                        var descendantsList = allDescendants.Distinct().ToList();
                        const int batchSize = 100; // OPTIMIZATION: Increased from 20 to 100
                        var totalBatches = (descendantsList.Count + batchSize - 1) / batchSize;

                        if (descendantsList.Count > 0)
                        {
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Processing {descendantsList.Count} descendants in {totalBatches} batches of up to {batchSize} work items");

                            for (int batchIndex = 0; batchIndex < descendantsList.Count; batchIndex += batchSize)
                            {
                                // Check cancellation at start of each batch
                                CancellationToken.ThrowIfCancellationRequested();

                                var batch = descendantsList.Skip(batchIndex).Take(batchSize).ToList();
                                var currentBatch = (batchIndex / batchSize) + 1;

                                Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Processing batch {currentBatch}/{totalBatches} ({batch.Count} work items)");

                                var batchTasks = batch.Select(async descendantId =>
                                {
                                    try
                                    {
                                        // Check cancellation inside task
                                        CancellationToken.ThrowIfCancellationRequested();

                                        var descendantWorkItem = await Client.GetWorkItem(descendantId);
                                        if (descendantWorkItem == null)
                                        {
                                            Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Work item {descendantId} not found");
                                            return false;
                                        }

                                        // Skip work items in "Removed" state
                                        var state = descendantWorkItem.GetField<string>("System.State");
                                        if (string.Equals(state, "Removed", StringComparison.OrdinalIgnoreCase))
                                        {
                                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Skipping {descendantWorkItem.WorkItemType} {descendantId} in 'Removed' state");
                                            return false;
                                        }

                                        bool needsUpdate = false;
                                        var updateDetails = new List<string>();

                                        // Update Category field
                                        if (!string.IsNullOrEmpty(categoryValue))
                                        {
                                            var currentCategory = descendantWorkItem.GetField<string>("Labs.Category");
                                            if (currentCategory != categoryValue)
                                            {
                                                descendantWorkItem.SetField("Labs.Category", categoryValue);
                                                needsUpdate = true;
                                                updateDetails.Add($"Category: '{currentCategory}' -> '{categoryValue}'");
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
                                                updateDetails.Add($"ProjectCode: '{currentProjectCode}' -> '{projectCodeValue}'");
                                            }
                                        }

                                        if (needsUpdate)
                                        {
                                            // Check cancellation before save
                                            CancellationToken.ThrowIfCancellationRequested();

                                            await Client.SaveWorkItem(descendantWorkItem);
                                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - ✓ Updated {descendantWorkItem.WorkItemType} {descendantId}: {string.Join(", ", updateDetails)}");
                                            return true;
                                        }

                                        return false;
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Update cancelled for work item {descendantId}");
                                        throw; // Re-throw to propagate cancellation
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - ✗ Error updating descendant work item {descendantId}: {ex.Message}");
                                        Interlocked.Increment(ref errors);
                                        return false;
                                    }
                                }).ToList();

                                // Wait for current batch to complete with timeout and cancellation support
                                try
                                {
                                    // OPTIMIZATION: Increased timeout from 2 to 5 minutes per batch due to larger batches
                                    using var batchTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                                    batchTimeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

                                    var results = await Task.WhenAll(batchTasks).ConfigureAwait(false);
                                    var batchUpdated = results.Count(r => r);
                                    descendantsUpdated += batchUpdated;

                                    Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} completed - updated {batchUpdated}/{batch.Count} work items");
                                }
                                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                                {
                                    Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} cancelled");
                                    throw; // Re-throw if it's the main cancellation token
                                }
                                catch (OperationCanceledException)
                                {
                                    Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} timed out after 5 minutes");
                                    errors++;
                                }
                            }
                        }

                        Interlocked.Add(ref totalDescendantsUpdated, descendantsUpdated);
                        Interlocked.Add(ref totalErrors, errors);
                        var processedCount = Interlocked.Increment(ref epicsProcessedCount);

                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - ✓ Completed - updated {descendantsUpdated} descendants, {errors} errors (Overall progress: {processedCount}/{allEpics.Count} epics)");

                        // Log progress every 10 Epics
                        if (processedCount % 10 == 0)
                        {
                            var elapsed = DateTime.Now - startTime;
                            var avgPerEpic = elapsed.TotalSeconds / processedCount;
                            var estimatedRemaining = TimeSpan.FromSeconds(avgPerEpic * (allEpics.Count - processedCount));

                            Logger.Information($"========================================================");
                            Logger.Information($"PROGRESS: {processedCount}/{allEpics.Count} Epics ({(processedCount * 100.0 / allEpics.Count):F1}%)");
                            Logger.Information($"Time elapsed: {elapsed:hh\\:mm\\:ss}");
                            Logger.Information($"Estimated remaining: {estimatedRemaining:hh\\:mm\\:ss}");
                            Logger.Information($"Total descendants updated so far: {totalDescendantsUpdated}");
                            Logger.Information($"========================================================");
                        }

                        return (epic.Id, descendantsUpdated, errors);
                    }
                    catch (OperationCanceledException)
                    {
                        Logger.Warning($"Epic {epic.Id} - Processing cancelled");
                        throw; // Re-throw to stop further processing
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Epic {epic.Id} - Error processing: {ex.Message}");
                        Interlocked.Increment(ref totalErrors);
                        var processedCount = Interlocked.Increment(ref epicsProcessedCount);
                        Logger.Information($"Epic {epic.Id} - ✗ Failed (Overall progress: {processedCount}/{allEpics.Count} epics)");
                        return (epic.Id, 0, 1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                // Wait for all epic processing tasks to complete
                try
                {
                    await Task.WhenAll(epicProcessingTasks);
                    Logger.Information("All epics processed successfully");
                }
                catch (OperationCanceledException)
                {
                    Logger.Warning("Epic processing was cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Warning($"One or more epics failed to process: {ex.Message}");
                }

                var totalElapsed = DateTime.Now - startTime;

                Logger.Information($"=================================================================");
                Logger.Information($"HISTORICAL MIGRATION COMPLETED");
                Logger.Information($"=================================================================");
                Logger.Information($"  - Total Epics processed: {epicsProcessedCount}");
                Logger.Information($"  - Total descendants updated: {totalDescendantsUpdated}");
                Logger.Information($"  - Total errors: {totalErrors}");
                Logger.Information($"  - Total time: {totalElapsed:hh\\:mm\\:ss}");
                Logger.Information($"=================================================================");
                Logger.Information($"IMPORTANT: This migration script should now be DISABLED");
                Logger.Information($"=================================================================");

                var message = $"Migration complete: {epicsProcessedCount} Epics, {totalDescendantsUpdated} descendants updated";

                // Return with a long interval (1 day) since this is a one-time migration
                // In production, this script should be disabled after successful execution
                return ScheduledScriptResult.Success(1440, message);
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("Historical migration was cancelled");
                return ScheduledScriptResult.Success(60, "Migration cancelled, will retry in 60 minutes");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Historical migration failed");
                return ScheduledScriptResult.Success(60, $"Migration failed, will retry in 60 minutes: {ex.Message}");
            }

        }
    }
}
