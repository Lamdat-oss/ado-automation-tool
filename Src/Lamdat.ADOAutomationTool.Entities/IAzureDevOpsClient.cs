﻿using Lamdat.ADOAutomationTool.Entities;

namespace Lamdat.ADOAutomationTool.Entities
{
    public interface IAzureDevOpsClient
    {
        string Project { get; set; }

        Task<List<IterationDetails>> GetAllTeamIterations(string teamName);
        //Task<ADOUser> GetLastChangedByUserForWorkItem(int workItemId);
        Task<IterationDetails> GetTeamsIterationDetailsByName(string teamName, string iterationName);
        Task<WorkItem> GetWorkItem(int workItemId);
        Task<bool> SaveWorkItem(WorkItem newWorkItem, bool logErrorOtherwiseWarn = false);
        Task<bool> SaveWorkItemRelations(WorkItem workitem, List<WorkItemRelation> relations);
        Task<List<WorkItem>> QuetyLinksByWiql(QueryLinksByWiqlPrms queryLinksByWiqlPrms);
        Task<ADOUser> WhoAmI();
    }
}