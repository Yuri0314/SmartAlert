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
                MakeHouse("Americans", "GDI"),
                MakeHouse("Russians", "Nod"),
                MakeHouse("Neutral", "Civilian"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            Assert.Contains("Americans", result);
            Assert.Contains("Russians", result);
            Assert.Contains("Neutral", result);
        }

        [Fact]
        public void Format_IncludesHouseTypeSideAndColorWhenAvailable()
        {
            var ht = new HouseType("Americans") { Side = "GDI", Color = "Gold" };
            var house = new House("Americans", ht) { Country = "Americans", Color = "Gold" };

            string result = AIHouseSummaryFormatter.Format(new[] { house });

            Assert.Contains("HouseType=Americans", result);
            Assert.Contains("Side=GDI", result);
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

        // ─── Grouped Formatter Tests ────────────────────────────────

        [Fact]
        public void Format_GroupsHousesByMoFactionSide()
        {
            var houses = new List<House>
            {
                MakeHouse("Americans", "GDI"),
                MakeHouse("Russians", "Nod"),
                MakeHouse("YuriCountry", "ThirdSide"),
                MakeHouse("Guild1", "FourthSide"),
                MakeHouse("Neutral", "Civilian"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            // Group headers present
            Assert.Contains("Allied", result);
            Assert.Contains("盟军", result);
            Assert.Contains("Soviet", result);
            Assert.Contains("苏军", result);
            Assert.Contains("Epsilon", result);
            Assert.Contains("厄普西隆", result);
            Assert.Contains("Foehn", result);
            Assert.Contains("焚风", result);

            // Exact INI names still present
            Assert.Contains("Americans", result);
            Assert.Contains("Russians", result);
            Assert.Contains("YuriCountry", result);
            Assert.Contains("Guild1", result);
            Assert.Contains("Neutral", result);
        }

        [Fact]
        public void Format_GroupsPlayerSlotsSeparately()
        {
            var houses = new List<House>
            {
                MakeHouse("Americans", "GDI"),
                new House("<Player @ A>"),
                new House("<Player @ B>"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            // Multiplayer slots group
            Assert.Contains("Multiplayer Slots", result);
            Assert.Contains("多人模板槽位", result);
            Assert.Contains("<Player @ A>", result);
            Assert.Contains("<Player @ B>", result);

            // Americans still in Allied group
            Assert.Contains("Allied", result);
            Assert.Contains("Americans", result);
        }

        [Fact]
        public void Format_PreservesHouseDetails()
        {
            var ht = new HouseType("Americans") { Side = "GDI", Color = "Gold" };
            var ally = new House("French");
            var house = new House("Americans", ht)
            {
                Country = "Americans",
                Color = "Gold",
                PlayerControl = true,
                Allies = new List<House> { ally }
            };

            string result = AIHouseSummaryFormatter.Format(new[] { house });

            Assert.Contains("Country=Americans", result);
            Assert.Contains("HouseType=Americans", result);
            Assert.Contains("Side=GDI", result);
            Assert.Contains("Color=Gold", result);
            Assert.Contains("PlayerControl=Yes", result);
            Assert.Contains("Allies=French", result);
        }

        [Fact]
        public void Format_GroupsMissingOrUnknownSideAsOther()
        {
            var houses = new List<House>
            {
                new House("CustomHouse"),  // null HouseType → empty side
                MakeHouse("ModFaction", "CustomSide"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            Assert.Contains("Other", result);
            Assert.Contains("未分组", result);
            Assert.Contains("CustomHouse", result);
            Assert.Contains("ModFaction", result);
        }

        [Fact]
        public void Format_SideGroupsAppearInCorrectOrder()
        {
            var houses = new List<House>
            {
                MakeHouse("Neutral", "Civilian"),
                MakeHouse("Guild1", "FourthSide"),
                MakeHouse("YuriCountry", "ThirdSide"),
                MakeHouse("Russians", "Nod"),
                MakeHouse("Americans", "GDI"),
                new House("<Player @ A>"),
            };

            string result = AIHouseSummaryFormatter.Format(houses);

            // Verify ordering: Allied before Soviet before Epsilon before Foehn
            // before Civilian before Multiplayer Slots
            int alliedIdx = result.IndexOf("Allied");
            int sovietIdx = result.IndexOf("Soviet");
            int epsilonIdx = result.IndexOf("Epsilon");
            int foehnIdx = result.IndexOf("Foehn");
            int civilianIdx = result.IndexOf("Neutral / Special");
            int slotsIdx = result.IndexOf("Multiplayer Slots");

            Assert.True(alliedIdx < sovietIdx, "Allied should appear before Soviet");
            Assert.True(sovietIdx < epsilonIdx, "Soviet should appear before Epsilon");
            Assert.True(epsilonIdx < foehnIdx, "Epsilon should appear before Foehn");
            Assert.True(foehnIdx < civilianIdx, "Foehn should appear before Civilian");
            Assert.True(civilianIdx < slotsIdx, "Civilian should appear before Multiplayer Slots");
        }

        [Fact]
        public void GetFactionGroupTitle_ReturnsCorrectLabels()
        {
            Assert.Contains("Allied", AIHouseSummaryFormatter.GetFactionGroupTitle("GDI"));
            Assert.Contains("盟军", AIHouseSummaryFormatter.GetFactionGroupTitle("GDI"));
            Assert.Contains("Soviet", AIHouseSummaryFormatter.GetFactionGroupTitle("Nod"));
            Assert.Contains("苏军", AIHouseSummaryFormatter.GetFactionGroupTitle("Nod"));
            Assert.Contains("Epsilon", AIHouseSummaryFormatter.GetFactionGroupTitle("ThirdSide"));
            Assert.Contains("厄普西隆", AIHouseSummaryFormatter.GetFactionGroupTitle("ThirdSide"));
            Assert.Contains("Foehn", AIHouseSummaryFormatter.GetFactionGroupTitle("FourthSide"));
            Assert.Contains("焚风", AIHouseSummaryFormatter.GetFactionGroupTitle("FourthSide"));
            Assert.Contains("Neutral", AIHouseSummaryFormatter.GetFactionGroupTitle("Civilian"));
            Assert.Contains("未分组", AIHouseSummaryFormatter.GetFactionGroupTitle(""));
            Assert.Contains("未分组", AIHouseSummaryFormatter.GetFactionGroupTitle(null));
            Assert.Contains("CustomSide", AIHouseSummaryFormatter.GetFactionGroupTitle("CustomSide"));
        }

        [Fact]
        public void IsMultiplayerSlot_DetectsSlotsByPrefix()
        {
            Assert.True(AIHouseSummaryFormatter.IsMultiplayerSlot(new House("<Player @ A>")));
            Assert.True(AIHouseSummaryFormatter.IsMultiplayerSlot(new House("<Player @ H>")));
            Assert.False(AIHouseSummaryFormatter.IsMultiplayerSlot(new House("Americans")));
            Assert.False(AIHouseSummaryFormatter.IsMultiplayerSlot(new House("Neutral")));
            Assert.False(AIHouseSummaryFormatter.IsMultiplayerSlot(null));
        }

        // ─── Helpers ────────────────────────────────────────────────

        private static House MakeHouse(string iniName, string side)
        {
            var ht = new HouseType(iniName) { Side = side };
            return new House(iniName, ht) { Country = iniName };
        }
    }
}
