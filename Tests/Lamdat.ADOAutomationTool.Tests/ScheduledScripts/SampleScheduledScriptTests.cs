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
            // Arrange - Create a sample script similar to the actual one
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                // Your automation logic here
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SampleScheduledScript_ShouldCallWhoAmI()
        {
            // Arrange - Create a sample script that uses WhoAmI
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                var displayName = user?.Identity?.DisplayName ?? ""Unknown"";
                Logger.Information($""Running as user: {displayName}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Running as user: Test User");
        }

        [Fact]
        public async Task SampleScheduledScript_WithCancellation_ShouldComplete()
        {
            // Arrange
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                Logger.Information(""Task completed"");
            ";
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            var result = await ExecuteScriptAsync(sampleScript, cts.Token);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Task completed");
        }

        [Fact]
        public async Task SampleScheduledScript_FromActualFile_ShouldWork()
        {
            // Arrange - Test with actual file if it exists, otherwise skip
            var scriptPath = "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/01-sample-task.rule";
            
            if (!File.Exists(scriptPath))
            {
                // Create a temporary file for testing
                var tempFile = Path.GetTempFileName();
                var sampleContent = @"Logger.Information(""My scheduled task is running!"");
   var user = await Client.WhoAmI();
   // Your automation logic here";
                await File.WriteAllTextAsync(tempFile, sampleContent);
                scriptPath = tempFile;
            }

            try
            {
                // Act
                var result = await ExecuteScriptFromFileAsync(scriptPath);

                // Assert
                result.ShouldBeSuccessful();
                result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            }
            finally
            {
                // Cleanup temp file if we created one
                if (scriptPath.Contains("tmp"))
                {
                    File.Delete(scriptPath);
                }
            }
        }
    }
}