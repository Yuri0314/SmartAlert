using System;
using TSMapEditor.AI.Validation;
using Xunit;

namespace TSMapEditor.Tests.AI.Validation
{
    public class MapValidationIssueTests
    {
        [Fact]
        public void SeverityEnum_IncludesExpectedValues()
        {
            Assert.True(Enum.IsDefined(typeof(MapValidationSeverity), "Error"));
            Assert.True(Enum.IsDefined(typeof(MapValidationSeverity), "Warning"));
            Assert.True(Enum.IsDefined(typeof(MapValidationSeverity), "Info"));
            Assert.True(Enum.IsDefined(typeof(MapValidationSeverity), "Intentional"));
        }

        [Fact]
        public void PolicyEnum_IncludesExpectedValues()
        {
            Assert.True(Enum.IsDefined(typeof(MapValidationPolicy), "Balanced"));
            Assert.True(Enum.IsDefined(typeof(MapValidationPolicy), "Creative"));
            Assert.True(Enum.IsDefined(typeof(MapValidationPolicy), "Scenario"));
            Assert.True(Enum.IsDefined(typeof(MapValidationPolicy), "LocalOnly"));
        }

        [Fact]
        public void Constructor_AssignsProperties()
        {
            var issue = new MapValidationIssue(
                MapValidationSeverity.Error,
                "CODE",
                "msg",
                "area",
                true,
                "fix");

            Assert.Equal(MapValidationSeverity.Error, issue.Severity);
            Assert.Equal("CODE", issue.Code);
            Assert.Equal("msg", issue.Message);
            Assert.Equal("area", issue.Area);
            Assert.True(issue.AllowedByIntent);
            Assert.Equal("fix", issue.SuggestedFix);
        }

        [Fact]
        public void Constructor_NormalizesNullStrings()
        {
            var issue = new MapValidationIssue(MapValidationSeverity.Info, null, null, null, false, null);

            Assert.Equal(string.Empty, issue.Code);
            Assert.Equal(string.Empty, issue.Message);
            Assert.Equal(string.Empty, issue.Area);
            Assert.Equal(string.Empty, issue.SuggestedFix);
        }

        [Fact]
        public void RequiresAttention_IsTrueForErrorAndWarning()
        {
            Assert.True(new MapValidationIssue(MapValidationSeverity.Error, "", "").RequiresAttention);
            Assert.True(new MapValidationIssue(MapValidationSeverity.Warning, "", "").RequiresAttention);
        }

        [Fact]
        public void RequiresAttention_IsFalseForInfoAndIntentional()
        {
            Assert.False(new MapValidationIssue(MapValidationSeverity.Info, "", "").RequiresAttention);
            Assert.False(new MapValidationIssue(MapValidationSeverity.Intentional, "", "").RequiresAttention);
        }

        [Fact]
        public void ToSummary_IncludesCoreFields()
        {
            var issue = new MapValidationIssue(MapValidationSeverity.Warning, "SpawnBlocked", "Player 1 spawn has limited deploy space");
            var summary = issue.ToSummary();

            Assert.Contains("[Warning]", summary);
            Assert.Contains("SpawnBlocked", summary);
            Assert.Contains("Player 1 spawn has limited deploy space", summary);
        }

        [Fact]
        public void ToSummary_IncludesAllowedByIntentWhenTrue()
        {
            var issue = new MapValidationIssue(MapValidationSeverity.Warning, "Test", "Test message", "test_area", true, "test_fix");
            var summary = issue.ToSummary();

            Assert.Contains("allowed by intent", summary);
            Assert.Contains("area: test_area", summary);
            Assert.Contains("suggested fix: test_fix", summary);
        }

        [Fact]
        public void ToSummary_OmitsOptionalFieldsWhenEmpty()
        {
            var issue = new MapValidationIssue(MapValidationSeverity.Info, "TestCode", "Test message", "", false, "");
            var summary = issue.ToSummary();

            Assert.DoesNotContain("(", summary);
            Assert.DoesNotContain(")", summary);
            Assert.DoesNotContain("area", summary);
            Assert.DoesNotContain("allowed by intent", summary);
            Assert.DoesNotContain("suggested fix", summary);
        }
    }
}
