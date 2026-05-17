using TSMapEditor.AI;
using TSMapEditor.Models;

namespace TSMapEditor.Tests.AI;

public class AIHouseResolverTests
{
    private static List<House> CreateTestHouses()
    {
        return new List<House>
        {
            new House("Americans"),
            new House("Russians"),
            new House("Neutral"),
            new House("<Player @ A>"),
            new House("<Player @ B>"),
        };
    }

    [Fact]
    public void ExactName_Resolves()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "Russians");
        Assert.NotNull(result);
        Assert.Equal("Russians", result.ININame);
    }

    [Fact]
    public void CaseInsensitive_Resolves()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "russians");
        Assert.NotNull(result);
        Assert.Equal("Russians", result.ININame);
    }

    [Fact]
    public void CaseInsensitive_MixedCase_Resolves()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "NEUTRAL");
        Assert.NotNull(result);
        Assert.Equal("Neutral", result.ININame);
    }

    [Fact]
    public void EmptyOwner_ResolvesToNeutral_WhenPresent()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "");
        Assert.NotNull(result);
        Assert.Equal("Neutral", result.ININame);
    }

    [Fact]
    public void WhitespaceOwner_ResolvesToNeutral_WhenPresent()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "   ");
        Assert.NotNull(result);
        Assert.Equal("Neutral", result.ININame);
    }

    [Fact]
    public void NullOwner_ResolvesToNeutral_WhenPresent()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, null);
        Assert.NotNull(result);
        Assert.Equal("Neutral", result.ININame);
    }

    [Fact]
    public void EmptyOwner_ReturnsNull_WhenNeutralMissing()
    {
        var houses = new List<House>
        {
            new House("Americans"),
            new House("Russians"),
        };
        var result = AIHouseResolver.ResolveOwner(houses, "");
        Assert.Null(result);
    }

    [Fact]
    public void UnknownOwner_ReturnsNull_DoesNotFallbackToFirst()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "NonExistentFaction");
        Assert.Null(result);
    }

    [Fact]
    public void PartialName_DoesNotMatch()
    {
        var houses = CreateTestHouses();
        // "Amer" is a prefix of "Americans" but should NOT match
        var result = AIHouseResolver.ResolveOwner(houses, "Amer");
        Assert.Null(result);
    }

    [Fact]
    public void PartialName_Russian_DoesNotMatch()
    {
        var houses = CreateTestHouses();
        // "Russia" is a prefix of "Russians" but should NOT match
        var result = AIHouseResolver.ResolveOwner(houses, "Russia");
        Assert.Null(result);
    }

    [Fact]
    public void PlayerTag_Resolves()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "<Player @ A>");
        Assert.NotNull(result);
        Assert.Equal("<Player @ A>", result.ININame);
    }

    [Fact]
    public void EmptyHouseList_ReturnsNull()
    {
        var houses = new List<House>();
        var result = AIHouseResolver.ResolveOwner(houses, "Neutral");
        Assert.Null(result);
    }

    [Fact]
    public void NullHouseList_ReturnsNull()
    {
        var result = AIHouseResolver.ResolveOwner(null, "Neutral");
        Assert.Null(result);
    }

    [Fact]
    public void FormatAvailableOwners_ListsAll()
    {
        var houses = CreateTestHouses();
        string formatted = AIHouseResolver.FormatAvailableOwners(houses);
        Assert.Contains("Americans", formatted);
        Assert.Contains("Russians", formatted);
        Assert.Contains("Neutral", formatted);
        Assert.Contains("<Player @ A>", formatted);
    }

    [Fact]
    public void FormatAvailableOwners_EmptyList_ReturnsNone()
    {
        var houses = new List<House>();
        string formatted = AIHouseResolver.FormatAvailableOwners(houses);
        Assert.Equal("(none)", formatted);
    }

    [Fact]
    public void FormatAvailableOwners_NullList_ReturnsNone()
    {
        string formatted = AIHouseResolver.FormatAvailableOwners(null);
        Assert.Equal("(none)", formatted);
    }

    [Fact]
    public void TrimmedInput_Resolves()
    {
        var houses = CreateTestHouses();
        var result = AIHouseResolver.ResolveOwner(houses, "  Russians  ");
        Assert.NotNull(result);
        Assert.Equal("Russians", result.ININame);
    }
}
