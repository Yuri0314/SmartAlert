using System.Linq;
using TSMapEditor.AI;
using TSMapEditor.Mutations.Classes;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class AIUnitAliasResolverTests
    {
        private const string TestJson = @"{
  ""aliases"": [
    { ""alias"": ""美国大兵"", ""code"": ""GI"", ""type"": ""Infantry"", ""description"": ""Allied GI"" },
    { ""alias"": ""大兵"", ""code"": ""GI"", ""type"": ""Infantry"", ""description"": ""Allied GI"" },
    { ""alias"": ""盟军大兵"", ""code"": ""GI"", ""type"": ""Infantry"", ""description"": ""Allied GI"" },
    { ""alias"": ""重装大兵"", ""code"": ""GGI"", ""type"": ""Infantry"", ""description"": ""Guardian G.I."" },
    { ""alias"": ""灰熊坦克"", ""code"": ""MTNK"", ""type"": ""Vehicle"", ""description"": ""Allied Grizzly Tank"" },
    { ""alias"": ""盟军坦克"", ""code"": ""MTNK"", ""type"": ""Vehicle"", ""description"": ""Allied Grizzly Tank"" },
    { ""alias"": ""犀牛坦克"", ""code"": ""HTNK"", ""type"": ""Vehicle"", ""description"": ""Soviet Rhino Heavy Tank"" },
    { ""alias"": ""苏军坦克"", ""code"": ""HTNK"", ""type"": ""Vehicle"", ""description"": ""Soviet Rhino Heavy Tank"" }
  ]
}";

        private readonly System.Collections.Generic.IReadOnlyList<AIUnitAliasMatch> aliases =
            AIUnitAliasResolver.LoadFromJson(TestJson);

        [Fact]
        public void LoadFromJson_LoadsAliases()
        {
            Assert.Equal(8, aliases.Count);
            Assert.Equal("GI", aliases[0].Code);
            Assert.Equal("美国大兵", aliases[0].Alias);
            Assert.Equal("Infantry", aliases[0].Type);
        }

        [Fact]
        public void FindMatches_FindsExactChineseAlias()
        {
            var matches = AIUnitAliasResolver.FindMatches(aliases, "美国大兵");
            Assert.Single(matches);
            Assert.Equal("GI", matches[0].Code);
        }

        [Fact]
        public void FindMatches_FindsMultipleTankAliasesForGenericTank()
        {
            var matches = AIUnitAliasResolver.FindMatches(aliases, "坦克");
            // Should find: 灰熊坦克, 盟军坦克, 犀牛坦克, 苏军坦克 (all contain 坦克)
            Assert.True(matches.Count >= 4, $"Expected at least 4 tank matches, got {matches.Count}");
            var codes = matches.Select(m => m.Code).Distinct().ToList();
            Assert.Contains("MTNK", codes);
            Assert.Contains("HTNK", codes);
        }

        [Fact]
        public void ResolveExact_MapsAmericanGIMustBeGI()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "美国大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("GI", code);
        }

        [Fact]
        public void ResolveExact_MapsGuardianGIMustBeGGI()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "重装大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("GGI", code);
        }

        [Fact]
        public void ResolveExact_RespectsObjectType()
        {
            // 灰熊坦克 is a Vehicle, not Infantry
            string asInfantry = AIUnitAliasResolver.ResolveExact(aliases, "灰熊坦克", AIPlaceObjectType.Infantry);
            Assert.Null(asInfantry);

            string asVehicle = AIUnitAliasResolver.ResolveExact(aliases, "灰熊坦克", AIPlaceObjectType.Vehicle);
            Assert.Equal("MTNK", asVehicle);
        }

        [Fact]
        public void ResolveExact_DoesNotResolveBareGenericTank()
        {
            // "坦克" by itself should NOT silently resolve to any single code
            string code = AIUnitAliasResolver.ResolveExact(aliases, "坦克", AIPlaceObjectType.Vehicle);
            Assert.Null(code);
        }

        [Fact]
        public void ResolveExact_ReturnsNullForUnknownAlias()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "不存在的单位", AIPlaceObjectType.Infantry);
            Assert.Null(code);
        }
    }
}
