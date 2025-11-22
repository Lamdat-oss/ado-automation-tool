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

                // ========================================================
                // TESTING MODE: Uncomment the line below to test with a single Epic ID
                // ========================================================
                //int? testEpicId = 85720; // Replace 12345 with your Epic ID for testing
                int? testEpicId = null; // Comment this line when testing with single Epic

                if (testEpicId.HasValue)
                {
                    Logger.Warning($"========================================================");
                    Logger.Warning($"RUNNING IN TEST MODE - Processing only Epic {testEpicId.Value}");
                    Logger.Warning($"========================================================");
                }

                // RATE LIMITING: Configure delays to avoid API throttling - INCREASED FOR STABILITY
                const int delayBetweenEpicsMs = 2000; // INCREASED: 2 seconds delay between Epic processing
                const int delayBetweenBatchesMs = 3000; // INCREASED: 3 seconds delay between update batches
                const int delayBetweenQueriesMs = 500; // INCREASED: 500ms delay between WIQL queries
                Logger.Information($"Rate limiting configured: {delayBetweenEpicsMs}ms between Epics, {delayBetweenBatchesMs}ms between batches, {delayBetweenQueriesMs}ms between queries");

                // Step 1: Find all Epics that have changed since 2024
                var allEpics = new List<WorkItem>();
                const int pageSize = 500; // RATE LIMIT: Reduced from 1000 to 500
                int? lastEpicId = null;
                bool hasMoreEpics = true;

                Logger.Information("Fetching ALL Epics from 2024 onwards with paging");

                while (hasMoreEpics)
                {
                    // Check cancellation before each page
                    CancellationToken.ThrowIfCancellationRequested();

                    string epicsQuery;

                    // TEST MODE: If testEpicId is set, query only that specific Epic
                    if (testEpicId.HasValue)
                    {
                        epicsQuery = $@"SELECT [System.Id], [System.Title], [System.WorkItemType], 
       [Labs.Category], [Labs.ProjectCode]
        FROM WorkItems 
     WHERE [System.WorkItemType] = 'Epic' 
      AND [System.TeamProject] = 'PCLabs'
      AND [System.Id] = {testEpicId.Value}";
                    }
                    else if (lastEpicId == null)
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

                    // RATE LIMIT: Add delay after each page query
                    await Task.Delay(delayBetweenQueriesMs, CancellationToken);

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

                        // TEST MODE: If testing with single Epic, stop after first page
                        if (testEpicId.HasValue)
                        {
                            hasMoreEpics = false;
                            Logger.Information($"Test mode: Stopping after fetching Epic {testEpicId.Value}");
                        }
                        else if (pageResults.Count < pageSize)
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
                var totalDescendantsSkipped = 0;
                var totalErrors = 0;
                var epicsProcessedCount = 0;
                var epicsSkippedCount = 0;
                var rateLimitRetriesCount = 0;
                var startTime = DateTime.Now;

                Logger.Information($"Starting processing of {allEpics.Count} Epics...");

                // RATE LIMIT: Reduced from 10 to 3 parallel epics to avoid overwhelming API
                const int maxDegreeOfParallelism = 3;
                var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

                var epicProcessingTasks = allEpics.Select(async (epic, epicIndex) =>
                {
                    await semaphore.WaitAsync(CancellationToken);
                    try
                    {
                        // RATE LIMIT: Add delay before processing each epic
                        if (epicIndex > 0)
                        {
                            await Task.Delay(delayBetweenEpicsMs, CancellationToken);
                        }

                        // Check cancellation at start of each epic processing
                        CancellationToken.ThrowIfCancellationRequested();

                        var epicNumber = epicIndex + 1;
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Starting Epic {epic.Id}: {epic.Title}");

                        // Get values from Epic
                        var categoryValue = epic.GetField<string>("Labs.Category");
                        var projectCodeValue = epic.GetField<string>("Labs.ProjectCode");

                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Category: '{categoryValue}', ProjectCode: '{projectCodeValue}'");

                        // If Epic has no values to inherit, skip
                        if (string.IsNullOrEmpty(categoryValue) && string.IsNullOrEmpty(projectCodeValue))
                        {
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - SKIPPED: No values to inherit (both Category and ProjectCode are empty)");
                            Interlocked.Increment(ref epicsSkippedCount);
                            var skippedCount = Interlocked.Increment(ref epicsProcessedCount);
                            return (epic.Id, 0, 0);
                        }

                        // Check cancellation before querying descendants
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.1: Get all descendant work items recursively
                        var allDescendants = new ConcurrentBag<int>();
                        var descendantProcessingStart = DateTime.UtcNow;
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Starting recursive descendant collection");

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
                        await Task.Delay(delayBetweenQueriesMs, CancellationToken); // RATE LIMIT
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {directDescendants.Count} direct descendants");

                        // Check cancellation
                        CancellationToken.ThrowIfCancellationRequested();

                        // Add direct descendants
                        foreach (var descendant in directDescendants)
                        {
                            allDescendants.Add(descendant.Id);
                        }

                        // RATE LIMIT: Reduced parallelism from 10 to 3 for descendant queries
                        var featuresAndPBIs = directDescendants.Where(d => 
                            d.WorkItemType == "Feature" || d.WorkItemType == "Product Backlog Item").ToList();
                        
                        if (featuresAndPBIs.Count > 0)
                        {
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying children for {featuresAndPBIs.Count} Features/PBIs sequentially (this may take a while)...");
                            
                            var featureCount = 0;
                            // RATE LIMIT: Changed to sequential processing with delays
                            foreach (var descendant in featuresAndPBIs)
                            {
                                CancellationToken.ThrowIfCancellationRequested();
                                
                                featureCount++;
                                Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - [{featureCount}/{featuresAndPBIs.Count}] Querying children for {descendant.WorkItemType} {descendant.Id}");
                                var childrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
           FROM WorkItemLinks
    WHERE [Source].[System.Id] = {descendant.Id}
     AND [Source].[System.TeamProject] = 'PCLabs'
        AND [Target].[System.TeamProject] = 'PCLabs'
    AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
 AND [Target].[System.WorkItemType] IN ('Product Backlog Item', 'Bug', 'Glitch', 'Task')";

                                var children = await Client.QueryWorkItemsByWiql(childrenQuery);
                                await Task.Delay(delayBetweenQueriesMs, CancellationToken); // RATE LIMIT
                                Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - [{featureCount}/{featuresAndPBIs.Count}] Found {children.Count} children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var child in children)
                                {
                                    allDescendants.Add(child.Id);
                                }

                                // Collect PBIs/Bugs/Glitches for grandchildren processing
                                var pbisBugsGlitches = children.Where(c => 
                                    c.WorkItemType == "Product Backlog Item" || 
                                    c.WorkItemType == "Bug" || 
                                    c.WorkItemType == "Glitch").ToList();

                                // Query grandchildren sequentially
                                if (pbisBugsGlitches.Count > 0)
                                {
                                    Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - [{featureCount}/{featuresAndPBIs.Count}] Querying grandchildren for {pbisBugsGlitches.Count} PBIs/Bugs/Glitches");
                                }
                                
                                foreach (var child in pbisBugsGlitches)
                                {
                                    CancellationToken.ThrowIfCancellationRequested();
                                    
                                    Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying grandchildren for {child.WorkItemType} {child.Id}");
                                    var grandchildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
 FROM WorkItemLinks
      WHERE [Source].[System.Id] = {child.Id}
 AND [Source].[System.TeamProject] = 'PCLabs'
 AND [Target].[System.TeamProject] = 'PCLabs'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
    AND [Target].[System.WorkItemType] IN ('Bug', 'Glitch', 'Task')";

                                    var grandchildren = await Client.QueryWorkItemsByWiql(grandchildrenQuery);
                                    await Task.Delay(delayBetweenQueriesMs, CancellationToken); // RATE LIMIT
                                    Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Found {grandchildren.Count} grandchildren for {child.WorkItemType} {child.Id}");

                                    foreach (var grandchild in grandchildren)
                                    {
                                        allDescendants.Add(grandchild.Id);
                                    }
                                }
                            }
                            
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Completed querying {featuresAndPBIs.Count} Features/PBIs and their descendants");
                        }

                        // Process direct Bugs/Glitches (though not standard hierarchy)
                        var directBugsGlitches = directDescendants.Where(d => 
                            d.WorkItemType == "Bug" || d.WorkItemType == "Glitch").ToList();
                        
                        if (directBugsGlitches.Count > 0)
                        {
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying task children for {directBugsGlitches.Count} Bugs/Glitches sequentially");
                            
                            var bugCount = 0;
                            // RATE LIMIT: Changed to sequential processing
                            foreach (var descendant in directBugsGlitches)
                            {
                                CancellationToken.ThrowIfCancellationRequested();
                                
                                bugCount++;
                                Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - [{bugCount}/{directBugsGlitches.Count}] Querying task children for {descendant.WorkItemType} {descendant.Id}");
                                var taskChildrenQuery = $@"SELECT [Target].[System.Id], [Target].[System.WorkItemType]
     FROM WorkItemLinks
 WHERE [Source].[System.Id] = {descendant.Id}
  AND [Source].[System.TeamProject] = 'PCLabs'
   AND [Target].[System.TeamProject] = 'PCLabs'
  AND [System.Links.LinkType] = 'System.LinkTypes.Hierarchy-Forward'
     AND [Target].[System.WorkItemType] = 'Task'";

                                var taskChildren = await Client.QueryWorkItemsByWiql(taskChildrenQuery);
                                await Task.Delay(delayBetweenQueriesMs, CancellationToken); // RATE LIMIT
                                Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - [{bugCount}/{directBugsGlitches.Count}] Found {taskChildren.Count} task children for {descendant.WorkItemType} {descendant.Id}");

                                foreach (var task in taskChildren)
                                {
                                    allDescendants.Add(task.Id);
                                }
                            }
                            
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Completed querying {directBugsGlitches.Count} Bugs/Glitches and their task children");
                        }

                        var descendantProcessingDuration = DateTime.UtcNow - descendantProcessingStart;
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Completed recursive descendant collection in {descendantProcessingDuration.TotalSeconds:F2} seconds");

                        // Check cancellation before test case processing
                        CancellationToken.ThrowIfCancellationRequested();

                        // Step 2.1.1: Find Test Cases linked to Feature/PBI/Bug via "Tested By" relationship
                        Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Querying Test Cases linked via 'Tested By' relationship");
                        var testCaseProcessingStart = DateTime.UtcNow;

                        // Get unique work items to check (deduped)
                        var workItemsToCheck = new HashSet<int>(allDescendants);
                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Checking {workItemsToCheck.Count} work items for Test Case links");

                        // RATE LIMIT: Batch test case queries with delays
                        var foundTestCases = new ConcurrentBag<int>();
                        var workItemsList = workItemsToCheck.ToList();
                        const int testCaseBatchSize = 10; // RATE LIMIT: Reduced from 25 to 10 for better stability
                        
                        for (int i = 0; i < workItemsList.Count; i += testCaseBatchSize)
                        {
                            CancellationToken.ThrowIfCancellationRequested();
                            
                            var batch = workItemsList.Skip(i).Take(testCaseBatchSize).ToList();
                            
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
                                await Task.Delay(delayBetweenQueriesMs, CancellationToken); // RATE LIMIT
                                
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
                                
                                // RATE LIMIT: Check if it's a rate limit error and retry with exponential backoff
                                if (ex.Message.Contains("429") || ex.Message.Contains("TF400733") || ex.Message.Contains("rate limit") || ex.Message.Contains("Unavailable"))
                                {
                                    Interlocked.Increment(ref rateLimitRetriesCount);
                                    Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Rate limit/service unavailable detected, waiting 60 seconds before retry...");
                                    await Task.Delay(60000, CancellationToken); // INCREASED: 60 seconds wait
                                    
                                    // Retry once
                                    try
                                    {
                                        var testCases = await Client.QueryWorkItemsByWiql(batchTestCasesQuery);
                                        await Task.Delay(delayBetweenQueriesMs, CancellationToken);
                                        
                                        if (testCases.Count > 0)
                                        {
                                            foreach (var testCase in testCases)
                                            {
                                                if (!batch.Contains(testCase.Id))
                                                {
                                                    foundTestCases.Add(testCase.Id);
                                                }
                                            }
                                        }
                                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Retry successful after rate limit");
                                    }
                                    catch (Exception retryEx)
                                    {
                                        Logger.Error($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Retry failed: {retryEx.Message}");
                                    }
                                }
                            }
                        }

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
                        var descendantsSkipped = 0;
                        var errors = 0;
                        var descendantsList = allDescendants.Distinct().ToList();
                        const int batchSize = 25; // RATE LIMIT: Reduced from 50 to 25 for better stability
                        var totalBatches = (descendantsList.Count + batchSize - 1) / batchSize;

                        if (descendantsList.Count > 0)
                        {
                            Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Processing {descendantsList.Count} descendants in {totalBatches} batches of up to {batchSize} work items");

                            for (int batchIndex = 0; batchIndex < descendantsList.Count; batchIndex += batchSize)
                            {
                                // Check cancellation at start of each batch
                                CancellationToken.ThrowIfCancellationRequested();

                                // RATE LIMIT: Add delay between batches
                                if (batchIndex > 0)
                                {
                                    await Task.Delay(delayBetweenBatchesMs, CancellationToken);
                                }

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
                                        else
                                        {
                                            Interlocked.Increment(ref descendantsSkipped);
                                            Logger.Debug($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - ⊘ Skipped {descendantWorkItem.WorkItemType} {descendantId} (already has correct values)");
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
                                        
                                        // RATE LIMIT: Check for rate limit errors
                                        if (ex.Message.Contains("429") || ex.Message.Contains("TF400733") || ex.Message.Contains("rate limit") || ex.Message.Contains("Unavailable"))
                                        {
                                            Interlocked.Increment(ref rateLimitRetriesCount);
                                            Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Rate limit/service unavailable detected on work item {descendantId}, will skip and continue");
                                        }
                                        
                                        Interlocked.Increment(ref errors);
                                        return false;
                                    }
                                }).ToList();

                                // Wait for current batch to complete with timeout and cancellation support
                                try
                                {
                                    // RATE LIMIT: Increased timeout to 10 minutes per batch to handle delays
                                    using var batchTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
                                    batchTimeoutCts.CancelAfter(TimeSpan.FromMinutes(10));

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
                                    Logger.Warning($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - Batch {currentBatch}/{totalBatches} timed out after 10 minutes");
                                    errors++;
                                }
                            }
                        }

                        Interlocked.Add(ref totalDescendantsUpdated, descendantsUpdated);
                        Interlocked.Add(ref totalDescendantsSkipped, descendantsSkipped);
                        Interlocked.Add(ref totalErrors, errors);
                        var processedCount = Interlocked.Increment(ref epicsProcessedCount);

                        Logger.Information($"[{epicNumber}/{allEpics.Count}] Epic {epic.Id} - ✓ Completed - updated {descendantsUpdated} descendants, skipped {descendantsSkipped} (already correct), {errors} errors (Overall progress: {processedCount}/{allEpics.Count} epics)");

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
                            Logger.Information($"Total descendants skipped so far: {totalDescendantsSkipped}");
                            Logger.Information($"Rate limit retries so far: {rateLimitRetriesCount}");
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
                if (testEpicId.HasValue)
                {
                    Logger.Information($"TEST MODE MIGRATION COMPLETED (Epic {testEpicId.Value} only)");
                }
                else
                {
                    Logger.Information($"HISTORICAL MIGRATION COMPLETED");
                }
                Logger.Information($"=================================================================");
                Logger.Information($"  - Total Epics found: {allEpics.Count}");
                Logger.Information($"  - Epics processed: {epicsProcessedCount - epicsSkippedCount}");
                Logger.Information($"  - Epics skipped (no values to inherit): {epicsSkippedCount}");
                Logger.Information($"  - Total descendants updated: {totalDescendantsUpdated}");
                Logger.Information($"  - Total descendants skipped (already correct): {totalDescendantsSkipped}");
                Logger.Information($"  - Total errors: {totalErrors}");
                Logger.Information($"  - Rate limit retries: {rateLimitRetriesCount}");
                Logger.Information($"  - Total time: {totalElapsed:hh\\:mm\\:ss}");
                Logger.Information($"=================================================================");
                if (!testEpicId.HasValue)
                {
                    Logger.Information($"IMPORTANT: This migration script should now be DISABLED");
                    Logger.Information($"=================================================================");
                }

                var message = testEpicId.HasValue 
                    ? $"Test mode complete: Epic {testEpicId.Value}, {totalDescendantsUpdated} updated, {totalDescendantsSkipped} skipped"
                    : $"Migration complete: {epicsProcessedCount} Epics ({epicsSkippedCount} skipped), {totalDescendantsUpdated} descendants updated, {totalDescendantsSkipped} skipped, {rateLimitRetriesCount} rate limit retries";

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
