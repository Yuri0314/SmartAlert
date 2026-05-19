using System.Linq;
using TSMapEditor.AI;
using TSMapEditor.Mutations.Classes;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class AIUnitAliasResolverTests
    {
        // Test JSON uses MO-specific INI IDs (source: MO fandom wiki)
        private const string TestJson = @"{
  ""aliases"": [
    { ""alias"": ""美国大兵"", ""code"": ""E1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" },
    { ""alias"": ""大兵"", ""code"": ""E1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" },
    { ""alias"": ""盟军大兵"", ""code"": ""E1"", ""type"": ""Infantry"", ""description"": ""Allied G.I. (MO)"" },
    { ""alias"": ""重装大兵"", ""code"": ""GGI"", ""type"": ""Infantry"", ""description"": ""Guardian G.I. (MO)"" },
    { ""alias"": ""动员兵"", ""code"": ""E2"", ""type"": ""Infantry"", ""description"": ""Soviet Conscript (MO)"" },
    { ""alias"": ""灰熊坦克"", ""code"": ""MTNK"", ""type"": ""Vehicle"", ""description"": ""Allied Cavalier Tank (MO)"" },
    { ""alias"": ""盟军坦克"", ""code"": ""MTNK"", ""type"": ""Vehicle"", ""description"": ""Allied Cavalier Tank (MO)"" },
    { ""alias"": ""犀牛坦克"", ""code"": ""HTNK"", ""type"": ""Vehicle"", ""description"": ""Soviet Rhino Heavy Tank (MO)"" },
    { ""alias"": ""苏军坦克"", ""code"": ""HTNK"", ""type"": ""Vehicle"", ""description"": ""Soviet Rhino Heavy Tank (MO)"" },
    { ""alias"": ""天启坦克"", ""code"": ""MAMM"", ""type"": ""Vehicle"", ""description"": ""Apocalypse Tank (MO)"" },
    { ""alias"": ""基洛夫飞艇"", ""code"": ""ZEP"", ""type"": ""Vehicle"", ""description"": ""Kirov Airship (MO)"" }
  ]
}";

        private readonly System.Collections.Generic.IReadOnlyList<AIUnitAliasMatch> aliases =
            AIUnitAliasResolver.LoadFromJson(TestJson);

        [Fact]
        public void LoadFromJson_LoadsAliases()
        {
            Assert.Equal(11, aliases.Count);
            Assert.Equal("E1", aliases[0].Code);
            Assert.Equal("美国大兵", aliases[0].Alias);
            Assert.Equal("Infantry", aliases[0].Type);
        }

        [Fact]
        public void FindMatches_FindsExactChineseAlias()
        {
            var matches = AIUnitAliasResolver.FindMatches(aliases, "美国大兵");
            Assert.Single(matches);
            Assert.Equal("E1", matches[0].Code);
        }

        [Fact]
        public void FindMatches_FindsMultipleTankAliasesForGenericTank()
        {
            var matches = AIUnitAliasResolver.FindMatches(aliases, "坦克");
            // Should find: 灰熊坦克, 盟军坦克, 犀牛坦克, 苏军坦克, 天启坦克 (all contain 坦克)
            Assert.True(matches.Count >= 5, $"Expected at least 5 tank matches, got {matches.Count}");
            var codes = matches.Select(m => m.Code).Distinct().ToList();
            Assert.Contains("MTNK", codes);
            Assert.Contains("HTNK", codes);
            Assert.Contains("MAMM", codes);
        }

        // --- MO-specific alias tests ---

        [Fact]
        public void ResolveExact_MapsAmericanGIToE1()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "美国大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("E1", code);
        }

        [Fact]
        public void ResolveExact_MapsDabingToE1()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("E1", code);
        }

        [Fact]
        public void ResolveExact_MapsAlliedDabingToE1()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "盟军大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("E1", code);
        }

        [Fact]
        public void ResolveExact_MapsGuardianGIMustBeGGI()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "重装大兵", AIPlaceObjectType.Infantry);
            Assert.Equal("GGI", code);
        }

        [Fact]
        public void ResolveExact_MapsConscriptToE2()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "动员兵", AIPlaceObjectType.Infantry);
            Assert.Equal("E2", code);
        }

        [Fact]
        public void ResolveExact_MapsApocalypseToMAMM()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "天启坦克", AIPlaceObjectType.Vehicle);
            Assert.Equal("MAMM", code);
        }

        [Fact]
        public void ResolveExact_MapsKirovToZEP()
        {
            string code = AIUnitAliasResolver.ResolveExact(aliases, "基洛夫飞艇", AIPlaceObjectType.Vehicle);
            Assert.Equal("ZEP", code);
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

        // --- Regression: old vanilla YR codes should NOT appear ---

        [Fact]
        public void ResolveExact_DoesNotReturnVanillaGI()
        {
            // "美国大兵" must NOT return vanilla "GI" — MO uses "E1"
            string code = AIUnitAliasResolver.ResolveExact(aliases, "美国大兵", AIPlaceObjectType.Infantry);
            Assert.NotEqual("GI", code);
            Assert.Equal("E1", code);
        }

        [Fact]
        public void ResolveExact_DoesNotReturnVanillaCONSCRIPT()
        {
            // "动员兵" must NOT return vanilla "CONSCRIPT" — MO uses "E2"
            string code = AIUnitAliasResolver.ResolveExact(aliases, "动员兵", AIPlaceObjectType.Infantry);
            Assert.NotEqual("CONSCRIPT", code);
            Assert.Equal("E2", code);
        }

        [Fact]
        public void ResolveExact_DoesNotReturnVanillaAPOC()
        {
            // "天启坦克" must NOT return vanilla "APOC" — MO uses "MAMM" (APOC is 灾厄坦克 in MO)
            string code = AIUnitAliasResolver.ResolveExact(aliases, "天启坦克", AIPlaceObjectType.Vehicle);
            Assert.NotEqual("APOC", code);
            Assert.Equal("MAMM", code);
        }
    }
}
