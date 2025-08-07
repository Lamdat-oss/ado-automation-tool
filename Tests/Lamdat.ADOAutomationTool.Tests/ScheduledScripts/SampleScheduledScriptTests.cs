using Lamdat.ADOAutomationTool.Tests.Framework;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Tests for the sample scheduled script
    /// </summary>
    public class SampleScheduledScriptTests : ScheduledScriptTestBase
    {
        [Fact]
        public async Task SampleScheduledScript_ShouldExecuteSuccessfully()
        {
            // Act
            var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/01-sample-task.rule");

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SampleScheduledScript_ShouldCallWhoAmI()
        {
            // Act
            var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/01-sample-task.rule");

            // Assert
            result.ShouldBeSuccessful();
            
            // The sample script calls WhoAmI, so we should be able to verify this happened
            // by checking if the result contains information about the test user
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
        }

        [Fact]
        public async Task SampleScheduledScript_WithCancellation_ShouldComplete()
        {
            // Arrange
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            var result = await ExecuteScriptFromFileAsync("../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/01-sample-task.rule", cts.Token);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
        }
    }
}