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
    internal class EpicFieldInheritance
    {

        public static async Task<ScheduledScriptResult> Run(IAzureDevOpsClient Client, ILogger Logger, CancellationToken CancellationToken, string ScriptRunId, DateTime LastRun)
        {
            // Epic Field Inheritance Scheduled Script
            // This script runs every 10 minutes to update field inheritance from Epics to child entities
            // when Epic fields (Custom.Category, Custom.ProjectCode) are updated
            //
            // Hierarchy: 
            // - Epic -> Feature
            // - Feature -> (PBI, Bug, Glitch, Task)
            // - PBI -> (Bug, Glitch, Task)
            // - Bug/Glitch -> Task
            // - Feature/PBI/Bug -> Test Case (via "Tested By" relationship)
            //
            // This ensures that when an Epic's fields change, all descendant work items are updated

            Logger.Information("Starting Epic field inheritance scheduled task...");
            Logger.Information($"Processing changes since: {LastRun:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // Check cancellation at start
                CancellationToken.ThrowIfCancellationRequested();

                // Set the project
                Client.Project = "Backup-Tests";

                var user = await Client.WhoAmI();
                Logger.Information($"Running as: {user?.Identity?.DisplayName}");
                Logger.Information($"Working with project: {Client.Project}");

                // Check cancellation
                CancellationToken.ThrowIfCancellationRequested();

                // Fields to inherit from Epic
                var fieldsToInherit = new[] { "Labs.Category", "Labs.ProjectCode" };

                // Step 1: Find all Epics that have changed since last run AND check their revision history
                var sinceLastRunUtc = LastRun.Kind == DateTimeKind.Utc
       ? LastRun
     : LastRun.ToUniversalTime();
                var sinceLastRun = sinceLastRunUtc.ToString("yyyy-MM-dd");

                var changedEpics = new List<WorkItem>();
                const int pageSize = 1000; // OPTIMIZATION: Increased from 200 to 1000
                int? lastEpicId = null;
                bool hasMoreEpics = true;

                Logger.Information("Fetching changed Epics with paging and checking revision history for field changes");

                while (hasMoreEpics)
                {
                    // Check cancellation before each page
                    CancellationToken.ThrowIfCancellationRequested();

                    string changedEpicsQuery;

                    if (lastEpicId == null)
                    {
                        changedEpicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
       [Labs.Category], [Labs.ProjectCode]
   FROM WorkItems 
   WHERE [System.WorkItemType] = 'Epic' 
  AND [System.TeamProject] = 'Backup-Tests'
 AND [System.ChangedDate] >= '{sinceLastRun}'    
  ORDER BY [System.Id]";
                    }
                    else
                    {
                        changedEpicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
  [Labs.Category], [Labs.ProjectCode]
     FROM WorkItems 
WHERE [System.WorkItemType] = 'Epic' 
AND [System.TeamProject] = 'Backup-Tests'
      AND [System.ChangedDate] >= '{sinceLastRun}'
     AND [System.Id] > {lastEpicId}     
  ORDER BY [System.Id]";
                    }

                    var pageResults = await Client.QueryWorkItemsByWiql(changedEpicsQuery, pageSize);

                    if (pageResults.Count == 0)
                    {
                        hasMoreEpics = false;
                        Logger.Debug($"No more Epics found, paging complete. Total Epics fetched: {changedEpics.Count}");
                    }
                    else
                    {
                        // Filter by date first
                        var epicsToCheck = pageResults.Where(epic =>
                        {
                            var changedDate = epic.GetField<DateTime?>("System.ChangedDate");
                            return changedDate.HasValue && changedDate.Value.ToUniversalTime() >= LastRun.ToUniversalTime();
                        }).ToList();

                        Logger.Debug($"Checking revision history for {epicsToCheck.Count} Epics to detect field changes");

                        // OPTIMIZATION: Parallel revision checks with increased parallelism
                        var revisionCheckBag = new ConcurrentBag<(WorkItem epic, bool hasChanges)>();
                        
                        await Parallel.ForEachAsync(epicsToCheck, new ParallelOptions 
                        { 
                            MaxDegreeOfParallelism = 10, // OPTIMIZATION: Parallel revision checks
                            CancellationToken = CancellationToken 
                        }, async (epic, ct) =>
                        {
                            try
                            {
                                // Get revisions since last run for this Epic, only requesting the fields we care about
                                var revisions = await Client.GetWorkItemRevisions(
         epic.Id,
   LastRun,
              new List<string> { "Labs.Category", "Labs.ProjectCode", "System.ChangedDate" }
       );

                                // We have multiple revisions, check if Labs.Category or Labs.ProjectCode changed
                                bool fieldChanged = false;
                                string? previousCategory = null;
                                string? previousProjectCode = null;

                                // Sort revisions by date ascending
                                var sortedRevisions = revisions.OrderBy(r => r.GetField<DateTime?>("System.ChangedDate")).ToList();

                                foreach (var revision in sortedRevisions)
                                {
                                    var currentCategory = revision.GetField<string>("Labs.Category");
                                    var currentProjectCode = revision.GetField<string>("Labs.ProjectCode");

                                    // Check if Category field changed
                                    if (previousCategory != currentCategory)
                                    {
                                        Logger.Debug($"Epic {epic.Id} - Labs.Category changed from '{previousCategory}' to '{currentCategory}' in revision {revision.Revision}");
                                        fieldChanged = true;
                                    }

                                    // Check if ProjectCode field changed
                                    if (previousProjectCode != currentProjectCode)
                                    {
                                        Logger.Debug($"Epic {epic.Id} - Labs.ProjectCode changed from '{previousProjectCode}' to '{currentProjectCode}' in revision {revision.Revision}");
                                        fieldChanged = true;
                                    }

                                    previousCategory = currentCategory;
                                    previousProjectCode = currentProjectCode;
                                }

                                if (fieldChanged)
                                {
                                    Logger.Debug($"Epic {epic.Id} - Inheritable fields changed, will process descendants");
                                }
                                else
                                {
                                    Logger.Debug($"Epic {epic.Id} - Changed but inheritable fields unchanged, skipping");
                                }

                                revisionCheckBag.Add((epic, fieldChanged));
                            }
                            catch (OperationCanceledException)
                            {
                                Logger.Debug($"Revision check cancelled for Epic {epic.Id}");
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning($"Error checking revision history for Epic {epic.Id}: {ex.Message}");
                                // On error, include the Epic to be safe
                                revisionCheckBag.Add((epic, true));
                            }
                        });

                        // Filter to only Epics where fields actually changed
                        var filteredPageResults = revisionCheckBag
                            .Where(result => result.hasChanges)
                            .Select(result => result.epic)
                            .ToList();

                        changedEpics.AddRange(filteredPageResults);

                        Logger.Information($"Fetched page with {pageResults.Count} Epics (filtered to {filteredPageResults.Count} with inheritable field changes), last ID: {lastEpicId}, total so far: {changedEpics.Count}");

                        lastEpicId = pageResults.Last().Id;

                        if (pageResults.Count < pageSize)
                        {
                            hasMoreEpics = false;
                            Logger.Debug($"Received fewer results than page size ({pageResults.Count} < {pageSize}), paging complete");
                        }
                    }
                }

                Logger.Information($"Found {changedEpics.Count} changed Epics with inheritable field changes since last run");

                // Check cancellation after fetching epics
                CancellationToken.ThrowIfCancellationRequested();

                if (changedEpics.Count == 0)
                {
                    Logger.Information("No Epics with inheritable field changes found - no field inheritance needed");
                    return ScheduledScriptResult.Success(10, "No field inheritance needed - next check in 10 minutes");
                }

                // Step 2: For each changed Epic, get all descendant work items and update their inherited fields
                // Use thread-safe counters for parallel processing
                var totalDescendantsUpdated = 0;
                var totalErrors = 0;
                var epicsProcessedCount = 0;

                Logger.Information($"Starting parallel processing of {changedEpics.Count} Epics...");

                // OPTIMIZATION: Increased parallelism from 3 to 10 epics at a time
                const int maxDegreeOfParallelism = 10;
                var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

                var epicProcessingTasks = changedEpics.Select(async (epic, epicIndex) =>
                {
                    await semaphore.WaitAsync(CancellationToken);
                    try
                    {
                        // Check cancellation at start of each epic processing
                        CancellationToken.ThrowIfCancellationRequested();

                        var epicNumber = epicIndex + 1;
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Starting Epic {epic.Id}: {epic.Title}");

                        // Get values from Epic
                        var categoryValue = epic.GetField<string>("Labs.Category");
                        var projectCodeValue = epic.GetField<string>("Labs.ProjectCode");

                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Category: '{categoryValue}', ProjectCode: '{projectCodeValue}'");

                        // Check cancellation before querying descendants
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.1: Get all descendant work items (Features, PBIs, Bugs, Glitches, Tasks) in the hierarchy
                        var descendantsQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
    FROM WorkItemLinks
    WHERE [Source].[System.Id] = {epic.Id}
     AND [Source].[System.TeamProject] = 'Backup-Tests'
     AND [Target].[System.TeamProject] = 'Backup-Tests'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
      AND [Target].[System.WorkItemType] IN ('Feature', 'Product Backlog Item', 'Bug', 'Glitch', 'Task')
      ORDER BY [Target].[System.Id]";

                        var directDescendants = await Client.QueryWorkItemsByWiql(descendantsQuery);
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {directDescendants.Count} direct descendants");

                        // Check cancellation
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.2: For each direct descendant, get their children recursively
                        var allDescendants = new ConcurrentBag<int>();
                        var descendantProcessingStart = DateTime.UtcNow;
                        Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Starting recursive descendant collection");

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
                            Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying children for {featuresAndPBIs.Count} Features/PBIs in parallel");
                            
                            await Parallel.ForEachAsync(featuresAndPBIs, new ParallelOptions 
                            { 
                                MaxDegreeOfParallelism = 10,
                                CancellationToken = CancellationToken 
                            }, async (descendant, ct) =>
                            {
                                Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying children for {descendant.WorkItemType} {descendant.Id}");
                                var childrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
     WHERE [Source].[System.Id] = {descendant.Id}
    AND [Source].[System.TeamProject] = 'Backup-Tests'
   AND [Target].[System.TeamProject] = 'Backup-Tests'
     AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
 AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Task')";

                                var children = await Client.QueryWorkItemsByWiql(childrenQuery);
                                Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {children.Count} children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var child in children)
                                {
                                    allDescendants.Add(child.Id);
                                    Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Added child {child.WorkItemType} {child.Id}");
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
                                        Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying grandchildren for {child.WorkItemType} {child.Id}");
                                        var grandchildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
  FROM WorkItemLinks
  WHERE [Source].[System.Id] = {child.Id}
   AND [Source].[System.TeamProject] = 'Backup-Tests'
  AND [Target].[System.TeamProject] = 'Backup-Tests'
      AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
        AND [Target].[System.WorkItemType] IN ('Bug', 'Glitch', 'Task')";

                                        var grandchildren = await Client.QueryWorkItemsByWiql(grandchildrenQuery);
                                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {grandchildren.Count} grandchildren for {child.WorkItemType} {child.Id}");

                                        foreach (var grandchild in grandchildren)
                                        {
                                            allDescendants.Add(grandchild.Id);
                                            Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Added grandchild {grandchild.WorkItemType} {grandchild.Id}");
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
                            Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying task children for {directBugsGlitches.Count} Bugs/Glitches in parallel");
                            
                            await Parallel.ForEachAsync(directBugsGlitches, new ParallelOptions 
                            { 
                                MaxDegreeOfParallelism = 10,
                                CancellationToken = CancellationToken 
                            }, async (descendant, ct) =>
                            {
                                Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying task children for {descendant.WorkItemType} {descendant.Id}");
                                var taskChildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
 WHERE [Source].[System.Id] = {descendant.Id}
  AND [Source].[System.TeamProject] = 'Backup-Tests'
   AND [Target].[System.TeamProject] = 'Backup-Tests'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
   AND [Target].[System.WorkItemType] = 'Task'";

                                var taskChildren = await Client.QueryWorkItemsByWiql(taskChildrenQuery);
                                Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {taskChildren.Count} task children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var task in taskChildren)
                                {
                                    allDescendants.Add(task.Id);
                                    Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Added task {task.Id}");
                                }
                            });
                        }

                        var descendantProcessingDuration = DateTime.UtcNow - descendantProcessingStart;
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Completed recursive descendant collection in {descendantProcessingDuration.TotalSeconds:F2} seconds");

                        // Check cancellation before test case processing
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.2.1: Find Test Cases linked to Feature/PBI/Bug via "Tested By" relationship
                        Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Querying Test Cases linked via 'Tested By' relationship");
                        var testCaseProcessingStart = DateTime.UtcNow;

                        // Get unique work items to check (deduped)
                        var workItemsToCheck = new HashSet<int>(allDescendants);
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Checking {workItemsToCheck.Count} work items for Test Case links");

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
 AND [Source].[System.TeamProject] = 'Backup-Tests'
  AND [Target].[System.TeamProject] = 'Backup-Tests'
     AND [System.Links.LinkType] = 'Microsoft.VSTS.Common.TestedBy-Forward'
  AND [Target].[System.WorkItemType] = 'Test Case'";

                                try
                                {
                                    var testCases = await Client.QueryWorkItemsByWiql(batchTestCasesQuery);
                                    
                                    if (testCases.Count > 0)
                                    {
                                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {testCases.Count} Test Cases in batch");

                                        foreach (var testCase in testCases)
                                        {
                                            // Exclude source work items
                                            if (!batch.Contains(testCase.Id))
                                            {
                                                foundTestCases.Add(testCase.Id);
                                                Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Added Test Case {testCase.Id}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warning($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Error querying Test Cases for batch: {ex.Message}");
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
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Completed Test Case collection in {testCaseProcessingDuration.TotalSeconds:F2} seconds");
                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {foundTestCases.Distinct().Count()} unique Test Cases");

                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Found {allDescendants.Count} total descendants (including Test Cases)");

                        // Check cancellation before batch processing
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.3: Update each descendant work item with inherited fields (in parallel batches for better performance)
                        var descendantsUpdated = 0;
                        var errors = 0;
                        var descendantsList = allDescendants.Distinct().ToList();
                        const int batchSize = 100; // OPTIMIZATION: Increased from 20 to 100
                        var totalBatches = (descendantsList.Count + batchSize - 1) / batchSize;

                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Processing {descendantsList.Count} descendants in {totalBatches} batches of up to {batchSize} work items");

                        for (int batchIndex = 0; batchIndex < descendantsList.Count; batchIndex += batchSize)
                        {
                            // Check cancellation at start of each batch
                            CancellationToken.ThrowIfCancellationRequested();

                            var batch = descendantsList.Skip(batchIndex).Take(batchSize).ToList();
                            var currentBatch = (batchIndex / batchSize) + 1;

                            Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Processing batch {currentBatch}/{totalBatches} ({batch.Count} work items)");

                            var batchTasks = batch.Select(async descendantId =>
                            {
                                try
                                {
                                    // Check cancellation inside task
                                    CancellationToken.ThrowIfCancellationRequested();

                                    Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Fetching work item {descendantId}");
                                    var descendantWorkItem = await Client.GetWorkItem(descendantId);
                                    if (descendantWorkItem == null)
                                    {
                                        Logger.Warning($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Work item {descendantId} not found");
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
                                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - ✓ Updated {descendantWorkItem.WorkItemType} {descendantId}: {string.Join(", ", updateDetails)}");
                                        return true;
                                    }
                                    else
                                    {
                                        Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Skipped {descendantWorkItem.WorkItemType} {descendantId} (no changes needed)");
                                    }

                                    return false;
                                }
                                catch (OperationCanceledException)
                                {
                                    Logger.Debug($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Update cancelled for work item {descendantId}");
                                    throw; // Re-throw to propagate cancellation
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warning($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - ✗ Error updating descendant work item {descendantId}: {ex.Message}");
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

                                Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} completed - updated {batchUpdated}/{batch.Count} work items");
                            }
                            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                            {
                                Logger.Warning($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} cancelled");
                                throw; // Re-throw if it's the main cancellation token
                            }
                            catch (OperationCanceledException)
                            {
                                Logger.Warning($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} timed out after 5 minutes");
                                errors++;
                            }
                        }

                        Interlocked.Add(ref totalDescendantsUpdated, descendantsUpdated);
                        Interlocked.Add(ref totalErrors, errors);
                        var processedCount = Interlocked.Increment(ref epicsProcessedCount);

                        Logger.Information($"[{epicNumber}/{changedEpics.Count}] Epic {epic.Id} - ✓ Completed - updated {descendantsUpdated} descendants, {errors} errors (Overall progress: {processedCount}/{changedEpics.Count} epics)");

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
                        Logger.Information($"{epic.Id} - ✗ Failed (Overall progress: {processedCount}/{changedEpics.Count} epics)");
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

                Logger.Information($"Epic field inheritance completed:");
                Logger.Information($"  - Epics processed: {changedEpics.Count}");
                Logger.Information($"  - Descendants updated: {totalDescendantsUpdated}");
                Logger.Information($"  - Errors: {totalErrors}");

                var message = $"Processed {changedEpics.Count} Epics, updated {totalDescendantsUpdated} descendants";
                return ScheduledScriptResult.Success(10, message);
            }
            catch (OperationCanceledException)
            {
                Logger.Warning("Epic field inheritance was cancelled");
                return ScheduledScriptResult.Success(5, "Field inheritance cancelled, will retry in 5 minutes");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Epic field inheritance failed");
                return ScheduledScriptResult.Success(5, $"Field inheritance failed, will retry in 5 minutes: {ex.Message}");
            }


        }
    }
}
