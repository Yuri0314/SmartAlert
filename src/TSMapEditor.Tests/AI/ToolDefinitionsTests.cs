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
}
