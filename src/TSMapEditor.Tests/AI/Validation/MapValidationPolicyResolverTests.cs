using TSMapEditor.AI.Validation;
using Xunit;

namespace TSMapEditor.Tests.AI.Validation
{
    public class MapValidationPolicyResolverTests
    {
        [Fact]
        public void Resolve_DefaultsToBalancedForEmptyInput()
        {
            Assert.Equal(MapValidationPolicy.Balanced, MapValidationPolicyResolver.Resolve(null, null));
            Assert.Equal(MapValidationPolicy.Balanced, MapValidationPolicyResolver.Resolve("", ""));
            Assert.Equal(MapValidationPolicy.Balanced, MapValidationPolicyResolver.Resolve("  ", "  "));
        }

        [Fact]
        public void Resolve_UsesExplicitIntentBeforeUserGoal()
        {
            // Intent says Scenario, goal says Creative — intent wins
            var result = MapValidationPolicyResolver.Resolve("TowerDefense", "创意对战地图");
            Assert.Equal(MapValidationPolicy.Scenario, result);
        }

        [Fact]
        public void Resolve_MapsBalancedSkirmishIntentToBalanced()
        {
            Assert.Equal(MapValidationPolicy.Balanced, MapValidationPolicyResolver.Resolve("BalancedSkirmish", ""));
        }

        [Fact]
        public void Resolve_MapsCreativeSkirmishIntentToCreative()
        {
            Assert.Equal(MapValidationPolicy.Creative, MapValidationPolicyResolver.Resolve("CreativeSkirmish", ""));
        }

        [Fact]
        public void Resolve_MapsSurvivalChallengeIntentToCreative()
        {
            Assert.Equal(MapValidationPolicy.Creative, MapValidationPolicyResolver.Resolve("SurvivalChallenge", ""));
        }

        [Fact]
        public void Resolve_MapsTowerDefenseIntentToScenario()
        {
            Assert.Equal(MapValidationPolicy.Scenario, MapValidationPolicyResolver.Resolve("TowerDefense", ""));
        }

        [Fact]
        public void Resolve_MapsScenarioStoryIntentToScenario()
        {
            Assert.Equal(MapValidationPolicy.Scenario, MapValidationPolicyResolver.Resolve("ScenarioStory", ""));
        }

        [Fact]
        public void Resolve_MapsBeautifyExistingMapIntentToLocalOnly()
        {
            Assert.Equal(MapValidationPolicy.LocalOnly, MapValidationPolicyResolver.Resolve("BeautifyExistingMap", ""));
        }

        [Fact]
        public void Resolve_MapsLocalEditIntentToLocalOnly()
        {
            Assert.Equal(MapValidationPolicy.LocalOnly, MapValidationPolicyResolver.Resolve("LocalEdit", ""));
        }

        [Fact]
        public void Resolve_MapsChineseTowerDefenseGoalToScenario()
        {
            Assert.Equal(MapValidationPolicy.Scenario, MapValidationPolicyResolver.Resolve("", "帮我生成一个塔防地图"));
            Assert.Equal(MapValidationPolicy.Scenario, MapValidationPolicyResolver.Resolve(null, "防守地图"));
        }

        [Fact]
        public void Resolve_MapsChineseLocalGoalToLocalOnly()
        {
            Assert.Equal(MapValidationPolicy.LocalOnly, MapValidationPolicyResolver.Resolve("", "在这里加几棵树美化一下"));
            Assert.Equal(MapValidationPolicy.LocalOnly, MapValidationPolicyResolver.Resolve(null, "局部编辑选区"));
        }

        [Fact]
        public void Resolve_MapsChineseCreativeGoalToCreative()
        {
            Assert.Equal(MapValidationPolicy.Creative, MapValidationPolicyResolver.Resolve("", "做一张创意地图"));
            Assert.Equal(MapValidationPolicy.Creative, MapValidationPolicyResolver.Resolve(null, "生存挑战模式"));
        }
    }
}
