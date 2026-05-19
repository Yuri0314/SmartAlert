using System.Collections.Generic;
using System.Linq;
using TSMapEditor.AI;

namespace TSMapEditor.Tests.AI;

public class ToolDefinitionsTests
{
    [Fact]
    public void GetAllTools_IncludesPlaceInfantry()
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == "place_infantry");
        Assert.NotNull(tool);
    }

    [Fact]
    public void GetAllTools_IncludesGetWorkflowState()
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == "get_workflow_state");
        Assert.NotNull(tool);
    }

    [Fact]
    public void GetWorkflowState_HasNoRequiredParameters()
    {
        var tool = ToolDefinitions.GetWorkflowState;
        Assert.NotNull(tool.Parameters);
        Assert.False(tool.Parameters.ContainsKey("required"));
    }

    [Fact]
    public void GetAllTools_IncludesPlaceInfantries()
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == "place_infantries");
        Assert.NotNull(tool);
    }

    [Fact]
    public void GetAllTools_KeepsVehicleCompatibilityTools()
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.NotNull(tools.FirstOrDefault(t => t.Name == "place_unit"));
        Assert.NotNull(tools.FirstOrDefault(t => t.Name == "place_units"));
    }

    [Fact]
    public void PlaceInfantry_RequiresName()
    {
        var tool = ToolDefinitions.PlaceInfantry;
        Assert.NotNull(tool.Parameters);
        Assert.True(tool.Parameters.TryGetValue("required", out var requiredObj));
        var requiredList = requiredObj as string[];
        Assert.NotNull(requiredList);
        Assert.Contains("name", requiredList);
    }

    [Fact]
    public void PlaceInfantries_RequiresItems()
    {
        var tool = ToolDefinitions.PlaceInfantries;
        Assert.NotNull(tool.Parameters);
        Assert.True(tool.Parameters.TryGetValue("required", out var requiredObj));
        var requiredList = requiredObj as string[];
        Assert.NotNull(requiredList);
        Assert.Contains("items", requiredList);
    }

    [Fact]
    public void GetAllTools_IncludesSetWorkflowState()
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == "set_workflow_state");
        Assert.NotNull(tool);
    }

    [Fact]
    public void SetWorkflowState_HasNoRequiredParameters()
    {
        var tool = ToolDefinitions.SetWorkflowState;
        Assert.NotNull(tool.Parameters);
        Assert.False(tool.Parameters.ContainsKey("required"));
    }

    [Fact]
    public void SetWorkflowState_DefinesIntentEnum()
    {
        var tool = ToolDefinitions.SetWorkflowState;
        Assert.True(tool.Parameters.TryGetValue("properties", out var propsObj));
        var props = propsObj as Dictionary<string, object>;
        Assert.NotNull(props);
        Assert.True(props.TryGetValue("intent", out var intentObj));
        var intentProp = intentObj as Dictionary<string, object>;
        Assert.NotNull(intentProp);
        Assert.True(intentProp.TryGetValue("enum", out var enumObj));
        var enumValues = enumObj as string[];
        Assert.NotNull(enumValues);
        Assert.Contains("BalancedSkirmish", enumValues);
        Assert.Contains("TowerDefense", enumValues);
        Assert.Contains("LocalEdit", enumValues);
    }

    [Fact]
    public void GetAllTools_IncludesGetHouses()
    {
        var tools = ToolDefinitions.GetAllTools();
        Assert.Contains(tools, t => t.Name == "get_houses");
    }

    [Fact]
    public void GetHouses_HasNoRequiredParameters()
    {
        var tool = ToolDefinitions.GetHouses;
        Assert.Equal("get_houses", tool.Name);
        var parameters = tool.Parameters as Dictionary<string, object>;
        Assert.NotNull(parameters);
        // Should not have "required" key, or if present it should be empty
        if (parameters.TryGetValue("required", out var required))
        {
            var reqArray = required as string[];
            Assert.True(reqArray == null || reqArray.Length == 0);
        }
    }

    // ─── coordinate_scope Tests ────────────────────────────────

    private static bool HasCoordinateScope(ToolDefinition tool)
    {
        if (!tool.Parameters.TryGetValue("properties", out var propsObj))
            return false;
        var props = propsObj as Dictionary<string, object>;
        return props != null && props.ContainsKey("coordinate_scope");
    }

    private static bool BatchItemHasCoordinateScope(ToolDefinition tool)
    {
        if (!tool.Parameters.TryGetValue("properties", out var propsObj))
            return false;
        var props = propsObj as Dictionary<string, object>;
        if (props == null || !props.TryGetValue("items", out var itemsObj))
            return false;
        var itemsDef = itemsObj as Dictionary<string, object>;
        if (itemsDef == null || !itemsDef.TryGetValue("items", out var itemSchemaObj))
            return false;
        var itemSchema = itemSchemaObj as Dictionary<string, object>;
        if (itemSchema == null || !itemSchema.TryGetValue("properties", out var itemPropsObj))
            return false;
        var itemProps = itemPropsObj as Dictionary<string, object>;
        return itemProps != null && itemProps.ContainsKey("coordinate_scope");
    }

    [Theory]
    [InlineData("place_building")]
    [InlineData("place_ore")]
    [InlineData("draw_road")]
    [InlineData("place_unit")]
    [InlineData("place_infantry")]
    [InlineData("create_plateau")]
    [InlineData("place_trees")]
    [InlineData("place_decorations")]
    [InlineData("clear_area")]
    [InlineData("place_tile")]
    [InlineData("draw_river")]
    public void PositionBearingTools_ExposeCoordinateScope(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);
        Assert.True(HasCoordinateScope(tool), $"{toolName} should have coordinate_scope property");
    }

    [Fact]
    public void SetSpawnPoint_DoesNotExposeCoordinateScope()
    {
        Assert.False(HasCoordinateScope(ToolDefinitions.SetSpawnPoint),
            "set_spawn_point should NOT have coordinate_scope — it is always global");
    }

    [Theory]
    [InlineData("place_buildings")]
    [InlineData("place_units")]
    [InlineData("place_infantries")]
    public void BatchTools_ItemSchemaExposesCoordinateScope(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);
        Assert.True(BatchItemHasCoordinateScope(tool),
            $"{toolName} batch item schema should have coordinate_scope property");
    }

    [Fact]
    public void CoordinateScope_HasCorrectEnumValues()
    {
        var tool = ToolDefinitions.PlaceBuilding;
        var props = tool.Parameters["properties"] as Dictionary<string, object>;
        Assert.NotNull(props);
        var scopeProp = props["coordinate_scope"] as Dictionary<string, object>;
        Assert.NotNull(scopeProp);
        var enumValues = scopeProp["enum"] as string[];
        Assert.NotNull(enumValues);
        Assert.Equal(2, enumValues.Length);
        Assert.Contains("selection", enumValues);
        Assert.Contains("global", enumValues);
    }
}
