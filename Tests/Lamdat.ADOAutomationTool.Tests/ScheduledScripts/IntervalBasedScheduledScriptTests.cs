using Lamdat.ADOAutomationTool.Tests.Framework;
using FluentAssertions;

namespace Lamdat.ADOAutomationTool.Tests.ScheduledScripts
{
    /// <summary>
    /// Tests for interval-based scheduled scripts functionality
    /// </summary>
    public class IntervalBasedScheduledScriptTests : ScheduledScriptTestBase
    {
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(15)]
        [InlineData(60)]
        [InlineData(1440)] // Daily
        [InlineData(10080)] // Weekly
        public async Task IntervalAwareScript_ShouldReturnSpecifiedInterval(int intervalMinutes)
        {
            // Arrange
            var script = $@"
                Logger.Information(""Interval test script running"");
                return ScheduledScriptResult.Success({intervalMinutes}, ""Test interval: {intervalMinutes} minutes"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(intervalMinutes);
            result.ShouldReturnSuccessfulResult();
            result.ShouldHaveResultMessage($"Test interval: {intervalMinutes} minutes");
        }

        [Fact]
        public async Task HealthCheckScript_ShouldReturnShortInterval()
        {
            // Arrange - Simulate health check script
            var script = @"
                Logger.Information(""Starting system health check..."");
                
                try 
                {
                    var user = await Client.WhoAmI();
                    Logger.Information($""Health check running as: {user?.Identity?.DisplayName}"");
                    
                    var healthStatus = ""Healthy"";
                    Logger.Information($""System status: {healthStatus}"");
                    
                    Logger.Information(""Health check completed successfully"");
                    
                    // Return success with 10-minute interval
                    return ScheduledScriptResult.Success(10, ""Health check scheduled for next 10 minutes"");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Health check failed"");
                    
                    // For critical monitoring, retry sooner on failure
                    return ScheduledScriptResult.Success(2, $""Health check failed, will retry in 2 minutes: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(10);
            result.ShouldReturnSuccessfulResult();
            result.ShouldHaveLogMessageContaining("Health check completed successfully");
            result.ShouldHaveResultMessage("Health check scheduled for next 10 minutes");
        }

        [Fact]
        public async Task DailyReportScript_ShouldReturnDailyInterval()
        {
            // Arrange - Simulate daily report script
            var script = @"
                Logger.Information(""Starting daily report generation..."");
                
                try 
                {
                    var user = await Client.WhoAmI();
                    Logger.Information($""Generating daily report as: {user?.Identity?.DisplayName}"");
                    
                    Logger.Information($""Daily report generated at {DateTime.Now:yyyy-MM-dd HH:mm:ss}"");
                    
                    // Return success with 24-hour interval (1440 minutes)
                    return ScheduledScriptResult.Success(1440, ""Daily report task scheduled for next 24 hours"");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Error generating daily report"");
                    
                    // Return failure result - the script will retry with default interval
                    return ScheduledScriptResult.Failure($""Daily report failed: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(1440);
            result.ShouldReturnSuccessfulResult();
            result.ShouldHaveLogMessageContaining("Daily report generated");
            result.ShouldHaveResultMessage("Daily report task scheduled for next 24 hours");
        }

        [Fact]
        public async Task WeeklyCleanupScript_ShouldReturnWeeklyInterval()
        {
            // Arrange - Simulate weekly cleanup script
            var script = @"
                Logger.Information(""Starting weekly cleanup task..."");
                
                try 
                {
                    var user = await Client.WhoAmI();
                    Logger.Information($""Weekly cleanup running as: {user?.Identity?.DisplayName}"");
                    
                    Logger.Information(""Performing weekly maintenance tasks..."");
                    
                    var itemsProcessed = 0; // Placeholder for actual cleanup count
                    Logger.Information($""Weekly cleanup completed. Processed {itemsProcessed} items."");
                    
                    // Return success with 7-day interval (10080 minutes)
                    return ScheduledScriptResult.Success(10080, ""Weekly cleanup scheduled for next 7 days"");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Weekly cleanup failed"");
                    
                    // For weekly tasks, retry in a few hours on failure
                    return ScheduledScriptResult.Success(240, $""Weekly cleanup failed, will retry in 4 hours: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(10080);
            result.ShouldReturnSuccessfulResult();
            result.ShouldHaveLogMessageContaining("Weekly cleanup completed");
            result.ShouldHaveResultMessage("Weekly cleanup scheduled for next 7 days");
        }

        [Fact]
        public async Task SmartIntervalScript_ShouldAdjustBasedOnTimeOfDay()
        {
            // Arrange - Smart interval script
            var script = @"
                Logger.Information(""Starting smart interval task..."");
                
                try 
                {
                    var user = await Client.WhoAmI();
                    Logger.Information($""Smart task running as: {user?.Identity?.DisplayName}"");
                    
                    var currentHour = DateTime.Now.Hour;
                    int nextInterval;
                    string reason;
                    
                    if (currentHour >= 9 && currentHour <= 17) 
                    {
                        // Business hours: check more frequently
                        nextInterval = 5;
                        reason = ""Business hours - checking every 5 minutes"";
                    }
                    else if (currentHour >= 18 && currentHour <= 22)
                    {
                        // Evening: moderate frequency
                        nextInterval = 15;
                        reason = ""Evening hours - checking every 15 minutes"";
                    }
                    else 
                    {
                        // Night time: less frequent
                        nextInterval = 60;
                        reason = ""Night hours - checking every hour"";
                    }
                    
                    Logger.Information($""Smart task completed. {reason}"");
                    
                    return ScheduledScriptResult.Success(nextInterval, reason);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Smart interval task failed"");
                    
                    // On error, retry more frequently
                    return ScheduledScriptResult.Success(2, $""Task failed, retrying in 2 minutes: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldReturnSuccessfulResult();

            // Verify the interval is correct based on current time
            var currentHour = DateTime.Now.Hour;
            int expectedInterval;
            string expectedReason;

            if (currentHour >= 9 && currentHour <= 17)
            {
                expectedInterval = 5;
                expectedReason = "Business hours - checking every 5 minutes";
            }
            else if (currentHour >= 18 && currentHour <= 22)
            {
                expectedInterval = 15;
                expectedReason = "Evening hours - checking every 15 minutes";
            }
            else
            {
                expectedInterval = 60;
                expectedReason = "Night hours - checking every hour";
            }

            result.ShouldHaveNextInterval(expectedInterval);
            result.ShouldHaveResultMessage(expectedReason);
            result.ShouldHaveLogMessageContaining($"Smart task completed. {expectedReason}");
        }

        [Fact]
        public async Task FailingScript_ShouldReturnFailureResult()
        {
            // Arrange - Script that fails
            var script = @"
                Logger.Information(""Starting task that will fail..."");
                
                try 
                {
                    // Simulate some work that fails
                    throw new InvalidOperationException(""Simulated failure"");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, ""Task failed"");
                    return ScheduledScriptResult.Failure($""Task failed: {ex.Message}"");
                }
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful(); // Script executed without throwing
            result.ShouldBeIntervalAware();
            result.ShouldReturnFailedResult();
            result.ShouldHaveResultMessage("Task failed: Simulated failure");
            result.ShouldHaveLogMessageContaining("Task failed");
        }

        [Fact]
        public async Task Script_WithNoInterval_ShouldReturnSuccessWithoutInterval()
        {
            // Arrange - Script that returns success without specifying interval
            var script = @"
                Logger.Information(""Task running without specific interval"");
                var user = await Client.WhoAmI();
                Logger.Information(""Task completed"");
                
                return ScheduledScriptResult.Success();
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldReturnSuccessfulResult();
            result.NextExecutionIntervalMinutes.Should().BeNull();
            result.ShouldHaveLogMessageContaining("Task completed");
        }

        [Fact]
        public async Task Script_WithCustomMessage_ShouldReturnMessage()
        {
            // Arrange
            var customMessage = "Custom completion message for interval test";
            var script = $@"
                Logger.Information(""Task with custom message running"");
                return ScheduledScriptResult.Success(30, ""{customMessage}"");
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldBeIntervalAware();
            result.ShouldHaveNextInterval(30);
            result.ShouldHaveResultMessage(customMessage);
        }

        [Fact]
        public async Task LegacyScript_ShouldNotBeIntervalAware()
        {
            // Arrange - Legacy script without return statement
            var script = @"
                Logger.Information(""Legacy scheduled task is running!"");
                var user = await Client.WhoAmI();
                Logger.Information($""Legacy task running as: {user?.Identity?.DisplayName}"");
                
                // No return statement - uses old interface
            ";

            // Act
            var result = await ExecuteScriptAsync(script);

            // Assert
            result.ShouldBeSuccessful();
            result.ShouldUseLegacyMode();
            result.ShouldHaveLogMessageContaining("Legacy scheduled task is running!");
            result.ShouldHaveLogMessageContaining("Legacy task running as: Test User");
        }

        [Fact]
        public async Task ActualScriptFiles_ShouldExecuteCorrectly()
        {
            // Arrange - Test actual script files
            var scriptFiles = new[]
            {
                "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/02-daily-report.rule",
                "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/03-health-check.rule",
                "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/04-weekly-cleanup.rule",
                "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/05-legacy-task.rule",
                "../../../../Src/Lamdat.ADOAutomationTool/scheduled-scripts/06-smart-interval.rule"
            };

            foreach (var scriptPath in scriptFiles)
            {
                if (File.Exists(scriptPath))
                {
                    // Act
                    var result = await ExecuteScriptFromFileAsync(scriptPath);

                    // Assert
                    result.ShouldBeSuccessful();
                    
                    // Check if it's interval-aware based on filename
                    if (Path.GetFileName(scriptPath).Contains("legacy"))
                    {
                        result.ShouldUseLegacyMode();
                    }
                    else
                    {
                        result.ShouldBeIntervalAware();
                        result.ShouldReturnSuccessfulResult();
                        result.NextExecutionIntervalMinutes.Should().BeGreaterThan(0);
                    }
                }
            }
        }
    }
}