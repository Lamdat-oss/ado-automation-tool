using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.Framework
{
    /// <summary>
    /// Assertion helpers for scheduled script testing
    /// </summary>
    public static class ScheduledScriptAssertions
    {
        /// <summary>
        /// Assert that a work item has the expected field value
        /// </summary>
        public static void ShouldHaveField(this WorkItem workItem, string fieldName, object expectedValue)
        {
            workItem.Should().NotBeNull();
            workItem.Fields.Should().ContainKey(fieldName);
            workItem.Fields[fieldName].Should().Be(expectedValue);
        }

        /// <summary>
        /// Assert that a work item has the expected title
        /// </summary>
        public static void ShouldHaveTitle(this WorkItem workItem, string expectedTitle)
        {
            workItem.ShouldHaveField("System.Title", expectedTitle);
        }

        /// <summary>
        /// Assert that a work item has the expected state
        /// </summary>
        public static void ShouldHaveState(this WorkItem workItem, string expectedState)
        {
            workItem.ShouldHaveField("System.State", expectedState);
        }

        /// <summary>
        /// Assert that a work item has the expected work item type
        /// </summary>
        public static void ShouldHaveWorkItemType(this WorkItem workItem, string expectedType)
        {
            workItem.ShouldHaveField("System.WorkItemType", expectedType);
        }

        /// <summary>
        /// Assert that the test result was successful
        /// </summary>
        public static void ShouldBeSuccessful(this ScheduledScriptTestResult result)
        {
            result.Should().NotBeNull();
            result.Success.Should().BeTrue(because: $"Script should execute successfully. Error: {result.ErrorMessage}");
            result.Exception.Should().BeNull();
        }

        /// <summary>
        /// Assert that the test result failed with an exception
        /// </summary>
        public static void ShouldFail(this ScheduledScriptTestResult result)
        {
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.Exception.Should().NotBeNull();
        }

        /// <summary>
        /// Assert that the test result failed with a specific exception type
        /// </summary>
        public static void ShouldFailWith<T>(this ScheduledScriptTestResult result) where T : Exception
        {
            result.ShouldFail();
            result.Exception.Should().BeOfType<T>();
        }

        /// <summary>
        /// Assert that the test result contains a specific log message
        /// </summary>
        public static void ShouldHaveLogMessage(this ScheduledScriptTestResult result, string expectedMessage)
        {
            result.Should().NotBeNull();
            result.HasLogMessage(expectedMessage).Should().BeTrue(
                because: $"Log should contain message: '{expectedMessage}'. Actual logs: {string.Join(", ", result.LogMessages)}");
        }

        /// <summary>
        /// Assert that the test result contains a log message with specific substring
        /// </summary>
        public static void ShouldHaveLogMessageContaining(this ScheduledScriptTestResult result, string substring)
        {
            result.Should().NotBeNull();
            result.HasLogMessageContaining(substring).Should().BeTrue(
                because: $"Log should contain substring: '{substring}'. Actual logs: {string.Join(", ", result.LogMessages)}");
        }

        /// <summary>
        /// Assert that execution time is within expected bounds
        /// </summary>
        public static void ShouldExecuteWithin(this ScheduledScriptTestResult result, TimeSpan maxDuration)
        {
            result.Should().NotBeNull();
            result.ExecutionTime.Should().BeLessOrEqualTo(maxDuration);
        }

        /// <summary>
        /// Assert that the mock client received the expected number of work item saves
        /// </summary>
        public static void ShouldHaveSavedWorkItems(this MockAzureDevOpsClient mockClient, int expectedCount)
        {
            mockClient.SavedWorkItems.Should().HaveCount(expectedCount);
        }

        /// <summary>
        /// Assert that the mock client executed the expected number of queries
        /// </summary>
        public static void ShouldHaveExecutedQueries(this MockAzureDevOpsClient mockClient, int expectedCount)
        {
            mockClient.ExecutedQueries.Should().HaveCount(expectedCount);
        }

        /// <summary>
        /// Assert that the mock client saved relations for work items
        /// </summary>
        public static void ShouldHaveSavedRelations(this MockAzureDevOpsClient mockClient, int expectedCount)
        {
            mockClient.SavedRelations.Should().HaveCount(expectedCount);
        }
    }
}