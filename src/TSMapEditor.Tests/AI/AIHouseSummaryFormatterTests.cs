using System.Collections.Generic;
using TSMapEditor.AI;
using TSMapEditor.Models;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class AIHouseSummaryFormatterTests
    {
        [Fact]
        public void Format_ListsExactIniNames()
        {
            var houses = new List<House>
            {
                new House("Americans"),
                new House("Russians"),
                new House("Neutral"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            Assert.Contains("Americans", result);
            Assert.Contains("Russians", result);
            Assert.Contains("Neutral", result);
        }

        [Fact]
        public void Format_IncludesHouseTypeSideAndColorWhenAvailable()
        {
            var ht = new HouseType("Americans") { Side = "Allied", Color = "Gold" };
            var house = new House("Americans", ht) { Country = "Americans", Color = "Gold" };

            string result = AIHouseSummaryFormatter.Format(new[] { house });

            Assert.Contains("HouseType=Americans", result);
            Assert.Contains("Side=Allied", result);
            Assert.Contains("Color=Gold", result);
            Assert.Contains("Country=Americans", result);
        }

        [Fact]
        public void Format_IncludesAlliesWhenAvailable()
        {
            var ally = new House("French");
            var house = new House("Americans") { Allies = new List<House> { ally } };

            string result = AIHouseSummaryFormatter.Format(new[] { house });

            Assert.Contains("Allies=French", result);
        }

        [Fact]
        public void Format_InstructsExactOwnerUsage()
        {
            var houses = new List<House> { new House("Neutral") };

            string result = AIHouseSummaryFormatter.Format(houses);

            Assert.Contains("精确", result);
            Assert.Contains("ININame", result);
        }

        [Fact]
        public void Format_HandlesNullOrEmptyHouseList()
        {
            string resultNull = AIHouseSummaryFormatter.Format(null);
            string resultEmpty = AIHouseSummaryFormatter.Format(new List<House>());

            Assert.Contains("none", resultNull);
            Assert.Contains("none", resultEmpty);
        }
    }
}
