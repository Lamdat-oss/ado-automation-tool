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
        public async Task SampleScheduledScript_WithInterval_ShouldExecuteSuccessfully()
        {
            // Arrange - Create a sample script with interval support similar to the actual one
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                Logger.Information($""Running as: {user?.Identity?.DisplayName}"");
                
                // Return success with custom interval (2 minutes)
                return ScheduledScriptResult.Success(2, ""Sample task executed with 2-minute interval"");
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(2);
            result.ShouldReturnSuccessfulResult();
            result.ShouldHaveResultMessageContaining("2-minute interval");
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Running as: Test User");
            result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SampleScheduledScript_Legacy_ShouldExecuteSuccessfully()
        {
            // Arrange - Create a legacy script without interval return
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                // Your automation logic here (no return statement)
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldUseLegacyMode();
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldExecuteWithin(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SampleScheduledScript_ShouldCallWhoAmI()
        {
            // Arrange - Create a sample script that uses WhoAmI with interval
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                var displayName = user?.Identity?.DisplayName ?? ""Unknown"";
                Logger.Information($""Running as user: {displayName}"");
                
                return ScheduledScriptResult.Success(5, ""Task completed successfully"");
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(5);
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Running as user: Test User");
            result.ShouldHaveResultMessage("Task completed successfully");
        }

        [Fact]
        public async Task SampleScheduledScript_WithCancellation_ShouldComplete()
        {
            // Arrange
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                Logger.Information(""Task completed"");
                
                return ScheduledScriptResult.Success(10, ""Task completed with cancellation support"");
            ";
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Act
            var result = await ExecuteScriptAsync(sampleScript, cts.Token);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(10);
            result.ShouldHaveLogMessageContaining("My scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Task completed");
            result.ShouldHaveResultMessageContaining("cancellation support");
        }

        [Fact]
        public async Task SampleScheduledScript_FromActualFile_ShouldWork()
        {
            // Arrange - Test with actual file if it exists, otherwise skip
            var scriptPath = "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/01-sample-task.rule";
            
            if (!File.Exists(scriptPath))
            {
                // Create a temporary file for testing with the new interval format
                var tempFile = Path.GetTempFileName();
                var sampleContent = @"Logger.Information(""My scheduled task is running!"");
var user = await Client.WhoAmI();
Logger.Information($""Running as: {user?.Identity?.DisplayName}"");

// Your automation logic here
Logger.Information(""Sample task completed successfully"");

// Return result with custom interval (2 minutes)
return ScheduledScriptResult.Success(2, ""Sample task executed with 2-minute interval"");";
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
                
                // The actual file should be interval-aware with 2-minute interval
                if (result.IsIntervalAware)
                {
                    result.ShouldHaveNextInterval(2);
                    result.ShouldHaveResultMessageContaining("2-minute interval");
                }
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

        [Fact]
        public async Task SampleScheduledScript_WithFailure_ShouldReturnFailedResult()
        {
            // Arrange - Create a script that returns a failure result
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                try
                {
                    // Simulate some work that fails
                    throw new InvalidOperationException(""Something went wrong"");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Error in scheduled task"");
                    return ScheduledScriptResult.Failure($""Task failed: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful(); // Script executed without throwing
            result.ShouldBeIntervalAware();
            result.ShouldReturnFailedResult();
            result.ShouldHaveResultMessageContaining("Task failed: Something went wrong");
            result.ShouldHaveLogMessageContaining("Error in scheduled task");
        }

        [Fact]
        public async Task SampleScheduledScript_WithDynamicInterval_ShouldAdjustInterval()
        {
            // Arrange - Create a script that adjusts interval based on conditions
            var sampleScript = @"
                Logger.Information(""My scheduled task is running!"");
                var user = await Client.WhoAmI();
                
                // Adjust interval based on time of day
                var currentHour = DateTime.Now.Hour;
                int interval = currentHour >= 9 && currentHour <= 17 ? 5 : 30; // 5 min during business hours, 30 min otherwise
                
                Logger.Information($""Setting next interval to {interval} minutes"");
                
                return ScheduledScriptResult.Success(interval, $""Task scheduled for next {interval} minutes"");
            ";

            // Act
            var result = await ExecuteScriptAsync(sampleScript);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldReturnSuccessfulResult();
            
            // Verify the interval is either 5 or 30 based on current time
            var currentHour = DateTime.Now.Hour;
            var expectedInterval = currentHour >= 9 && currentHour <= 17 ? 5 : 30;
            result.ShouldHaveNextInterval(expectedInterval);
            
            result.ShouldHaveLogMessageContaining($"Setting next interval to {expectedInterval} minutes");
            result.ShouldHaveResultMessageContaining($"next {expectedInterval} minutes");
        }
    }
}