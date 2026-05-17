using System.Collections.Generic;
using TSMapEditor.AI;
using TSMapEditor.AI.Validation;
using Xunit;

namespace TSMapEditor.Tests.AI.Validation
{
    public class MapQualityIssueConverterTests
    {
        [Fact]
        public void FromQualityFix_MapsSetSpawnPointToMissingSpawnPointError()
        {
            var fix = new QualityFix("set_spawn_point", "", "Missing spawn point");
            var issue = MapQualityIssueConverter.FromQualityFix(fix);

            Assert.Equal(MapValidationSeverity.Error, issue.Severity);
            Assert.Equal("MissingSpawnPoint", issue.Code);
            Assert.Equal("Missing spawn point", issue.Message);
            Assert.Equal("spawn", issue.Area);
            Assert.Equal("set_spawn_point", issue.SuggestedFix);
        }

        [Fact]
        public void FromQualityFix_MapsPlaceOreToMissingSpawnOreWarning()
        {
            var fix = new QualityFix("place_ore", "", "Missing ore");
            var issue = MapQualityIssueConverter.FromQualityFix(fix);

            Assert.Equal(MapValidationSeverity.Warning, issue.Severity);
            Assert.Equal("MissingSpawnOre", issue.Code);
            Assert.Equal("Missing ore", issue.Message);
            Assert.Equal("resources", issue.Area);
            Assert.Equal("place_ore", issue.SuggestedFix);
        }

        [Fact]
        public void FromQualityFix_MapsFillTerrainToLowTerrainDiversityInfo()
        {
            var fix = new QualityFix("fill_terrain", "", "Low diversity");
            var issue = MapQualityIssueConverter.FromQualityFix(fix);

            Assert.Equal(MapValidationSeverity.Info, issue.Severity);
            Assert.Equal("LowTerrainDiversity", issue.Code);
            Assert.Equal("Low diversity", issue.Message);
            Assert.Equal("terrain", issue.Area);
            Assert.Equal("fill_terrain", issue.SuggestedFix);
        }

        [Fact]
        public void FromQualityFix_MapsUnknownToolToGenericWarning()
        {
            var fix = new QualityFix("random_tool", "", "Random fix");
            var issue = MapQualityIssueConverter.FromQualityFix(fix);

            Assert.Equal(MapValidationSeverity.Warning, issue.Severity);
            Assert.Equal("QualityFixRecommended", issue.Code);
            Assert.Equal("Random fix", issue.Message);
            Assert.Equal(string.Empty, issue.Area);
            Assert.Equal("random_tool", issue.SuggestedFix);
        }

        [Fact]
        public void FromQualityFix_HandlesNullFix()
        {
            var issue = MapQualityIssueConverter.FromQualityFix(null);

            Assert.Equal(MapValidationSeverity.Warning, issue.Severity);
            Assert.Equal("UnknownQualityFix", issue.Code);
            Assert.False(string.IsNullOrEmpty(issue.Message));
        }

        [Fact]
        public void FromQualityFixes_PreservesItemCount()
        {
            var fixes = new List<QualityFix>
            {
                new QualityFix("set_spawn_point", "", ""),
                new QualityFix("place_ore", "", "")
            };

            var issues = MapQualityIssueConverter.FromQualityFixes(fixes);

            Assert.Equal(2, issues.Count);
            Assert.Equal("MissingSpawnPoint", issues[0].Code);
            Assert.Equal("MissingSpawnOre", issues[1].Code);
        }

        [Fact]
        public void FromQualityFixes_HandlesNull()
        {
            var issues = MapQualityIssueConverter.FromQualityFixes(null);
            Assert.Empty(issues);
        }

        [Fact]
        public void FromQualityFix_PlaceOreBalancedPolicyIsWarning()
        {
            var fix = new QualityFix("place_ore", "", "Missing ore");
            var issue = MapQualityIssueConverter.FromQualityFix(fix, MapValidationPolicy.Balanced);

            Assert.Equal(MapValidationSeverity.Warning, issue.Severity);
            Assert.False(issue.AllowedByIntent);
        }

        [Fact]
        public void FromQualityFix_PlaceOreCreativePolicyIsAllowedInfo()
        {
            var fix = new QualityFix("place_ore", "", "Missing ore");
            var issue = MapQualityIssueConverter.FromQualityFix(fix, MapValidationPolicy.Creative);

            Assert.Equal(MapValidationSeverity.Info, issue.Severity);
            Assert.True(issue.AllowedByIntent);
        }

        [Fact]
        public void FromQualityFix_FillTerrainLocalOnlyPolicyIsIntentional()
        {
            var fix = new QualityFix("fill_terrain", "", "Low diversity");
            var issue = MapQualityIssueConverter.FromQualityFix(fix, MapValidationPolicy.LocalOnly);

            Assert.Equal(MapValidationSeverity.Intentional, issue.Severity);
            Assert.True(issue.AllowedByIntent);
        }

        [Fact]
        public void FromQualityFix_UnknownToolScenarioPolicyIsAllowedInfo()
        {
            var fix = new QualityFix("unknown_tool", "", "Some fix");
            var issue = MapQualityIssueConverter.FromQualityFix(fix, MapValidationPolicy.Scenario);

            Assert.Equal(MapValidationSeverity.Info, issue.Severity);
            Assert.True(issue.AllowedByIntent);
        }

        [Fact]
        public void FromQualityFix_SetSpawnPointRemainsErrorForCreativePolicy()
        {
            var fix = new QualityFix("set_spawn_point", "", "Missing spawn");
            var issue = MapQualityIssueConverter.FromQualityFix(fix, MapValidationPolicy.Creative);

            Assert.Equal(MapValidationSeverity.Error, issue.Severity);
            Assert.False(issue.AllowedByIntent);
        }
    }
}
