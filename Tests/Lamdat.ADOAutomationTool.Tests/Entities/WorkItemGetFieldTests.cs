using Lamdat.ADOAutomationTool.Entities;
using FluentAssertions;
using Xunit;

namespace Lamdat.ADOAutomationTool.Tests.Entities
{
    /// <summary>
    /// Tests for WorkItem.GetField type conversion functionality
    /// </summary>
    public class WorkItemGetFieldTests
    {
        [Fact]
        public void GetField_ShouldHandleStringToDoubleConversion()
        {
            // Arrange - Simulate Azure DevOps returning numeric values as strings
            var workItem = new WorkItem();
            workItem.SetField("Microsoft.VSTS.Scheduling.CompletedWork", "12.5");
            workItem.SetField("Microsoft.VSTS.Scheduling.Effort", "40");

            // Act
            var completedWork = workItem.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork");
            var effort = workItem.GetField<double?>("Microsoft.VSTS.Scheduling.Effort");

            // Assert
            completedWork.Should().Be(12.5);
            effort.Should().Be(40.0);
        }

        [Fact]
        public void GetField_ShouldHandleStringToIntConversion()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("System.Id", "123");
            workItem.SetField("System.Rev", "5");

            // Act
            var id = workItem.GetField<int>("System.Id");
            var revision = workItem.GetField<int?>("System.Rev");

            // Assert
            id.Should().Be(123);
            revision.Should().Be(5);
        }

        [Fact]
        public void GetField_ShouldHandleNullableTypes()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("CompletedWork", "8.5");
            workItem.SetField("EmptyField", null);

            // Act
            var completedWork = workItem.GetField<double?>("CompletedWork");
            var emptyValue = workItem.GetField<double?>("EmptyField");
            var missingValue = workItem.GetField<double?>("MissingField");

            // Assert
            completedWork.Should().Be(8.5);
            emptyValue.Should().BeNull();
            missingValue.Should().BeNull();
        }

        [Fact]
        public void GetField_ShouldHandleDirectTypeMatches()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("DoubleValue", 15.75);
            workItem.SetField("IntValue", 42);
            workItem.SetField("StringValue", "Hello World");

            // Act
            var doubleValue = workItem.GetField<double>("DoubleValue");
            var intValue = workItem.GetField<int>("IntValue");
            var stringValue = workItem.GetField<string>("StringValue");

            // Assert
            doubleValue.Should().Be(15.75);
            intValue.Should().Be(42);
            stringValue.Should().Be("Hello World");
        }

        [Fact]
        public void GetField_ShouldHandleEmptyStrings()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("EmptyString", "");
            workItem.SetField("WhitespaceString", "   ");

            // Act
            var emptyAsDouble = workItem.GetField<double?>("EmptyString");
            var whitespaceAsDouble = workItem.GetField<double?>("WhitespaceString");
            var emptyAsString = workItem.GetField<string>("EmptyString");

            // Assert
            emptyAsDouble.Should().BeNull();
            whitespaceAsDouble.Should().BeNull();
            emptyAsString.Should().Be("");
        }

        [Fact]
        public void GetField_ShouldHandleDefaultValues()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("ValidValue", "25.5");

            // Act
            var validValue = workItem.GetField<double>("ValidValue", 0.0);
            var missingValue = workItem.GetField<double>("MissingField", 99.9);

            // Assert
            validValue.Should().Be(25.5);
            missingValue.Should().Be(99.9);
        }

        [Fact]
        public void GetField_ShouldHandleNumericTypeConversions()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("IntValue", 42);
            workItem.SetField("DoubleValue", 42.7);

            // Act
            var intToDouble = workItem.GetField<double>("IntValue");
            var doubleToInt = workItem.GetField<int>("DoubleValue");

            // Assert
            intToDouble.Should().Be(42.0);
            doubleToInt.Should().Be(43); // Convert.ChangeType rounds 42.7 to 43
        }

        [Fact]
        public void GetField_ShouldHandleBooleanConversions()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("TrueBool", "true");
            workItem.SetField("FalseBool", "false");
            workItem.SetField("DirectBool", true);

            // Act
            var trueValue = workItem.GetField<bool>("TrueBool");
            var falseValue = workItem.GetField<bool>("FalseBool");
            var directValue = workItem.GetField<bool>("DirectBool");

            // Assert
            trueValue.Should().BeTrue();
            falseValue.Should().BeFalse();
            directValue.Should().BeTrue();
        }

        [Fact]
        public void GetField_ShouldHandleInvalidConversions()
        {
            // Arrange
            var workItem = new WorkItem();
            workItem.SetField("InvalidNumber", "not-a-number");
            workItem.SetField("InvalidBool", "not-a-bool");

            // Act
            var invalidDouble = workItem.GetField<double?>("InvalidNumber");
            var invalidBool = workItem.GetField<bool?>("InvalidBool");

            // Assert
            invalidDouble.Should().BeNull(); // Should fallback to default
            invalidBool.Should().BeNull(); // Should fallback to default
        }

        [Fact]
        public void GetField_ShouldHandleHierarchicalAggregationScenarios()
        {
            // Arrange - Test the exact scenarios from the hierarchical aggregation script
            var task = new WorkItem();
            task.SetField("Microsoft.VSTS.Scheduling.CompletedWork", "8.5");
            task.SetField("Microsoft.VSTS.Common.Activity", "Development");

            var feature = new WorkItem();
            feature.SetField("Microsoft.VSTS.Scheduling.Effort", "40");
            feature.SetField("Custom.DevelopmentEffortEstimation", "25.5");
            feature.SetField("Custom.QAEffortEstimation", "10");
            feature.SetField("Microsoft.VSTS.Scheduling.RemainingWork", "15.5");

            // Act - Simulate the exact calls from the aggregation script
            var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
            var activity = task.GetField<string>("Microsoft.VSTS.Common.Activity") ?? "";

            var effort = feature.GetField<double?>("Microsoft.VSTS.Scheduling.Effort") ?? 0;
            var devEstimation = feature.GetField<double?>("Custom.DevelopmentEffortEstimation") ?? 0;
            var qaEstimation = feature.GetField<double?>("Custom.QAEffortEstimation") ?? 0;
            var remainingWork = feature.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork") ?? 0;

            // Assert - Verify all conversions work correctly
            completedWork.Should().Be(8.5);
            activity.Should().Be("Development");
            effort.Should().Be(40.0);
            devEstimation.Should().Be(25.5);
            qaEstimation.Should().Be(10.0);
            remainingWork.Should().Be(15.5);
        }
    }
}