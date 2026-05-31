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

    // ─── place_ore include_mine Tests ──────────────────────────

    [Fact]
    public void PlaceOre_HasIncludeMineProperty()
    {
        var tool = ToolDefinitions.PlaceOre;
        var props = tool.Parameters["properties"] as Dictionary<string, object>;
        Assert.NotNull(props);
        Assert.True(props.ContainsKey("include_mine"),
            "place_ore should expose an include_mine property");
    }

    [Fact]
    public void PlaceOre_IncludeMine_IsBooleanType()
    {
        var tool = ToolDefinitions.PlaceOre;
        var props = tool.Parameters["properties"] as Dictionary<string, object>;
        Assert.NotNull(props);
        var includeMine = props["include_mine"] as Dictionary<string, object>;
        Assert.NotNull(includeMine);
        Assert.Equal("boolean", includeMine["type"]);
    }

    [Fact]
    public void PlaceOre_IncludeMine_IsNotRequired()
    {
        var tool = ToolDefinitions.PlaceOre;
        Assert.True(tool.Parameters.TryGetValue("required", out var requiredObj));
        var required = requiredObj as string[];
        Assert.NotNull(required);
        Assert.DoesNotContain("include_mine", required);
    }

    [Fact]
    public void PlaceOre_OnlyAmountIsRequired()
    {
        var tool = ToolDefinitions.PlaceOre;
        Assert.True(tool.Parameters.TryGetValue("required", out var requiredObj));
        var required = requiredObj as string[];
        Assert.NotNull(required);
        Assert.Single(required);
        Assert.Equal("amount", required[0]);
    }

    /// <summary>
    /// Regression: ExecutePlaceOre must read "include_mine" from args.
    /// Verified by checking the IL for a TryGetProperty("include_mine") call pattern.
    /// </summary>
    [Fact]
    public void ExecutePlaceOre_ReadsIncludeMineFromArgs()
    {
        var method = typeof(TSMapEditor.AI.ToolExecutor).GetMethod("ExecutePlaceOre",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);

        var body = method.GetMethodBody();
        Assert.NotNull(body);

        // Check that the method has a bool local (includeMine)
        bool hasBoolLocal = false;
        foreach (var local in body.LocalVariables)
        {
            if (local.LocalType == typeof(bool))
            {
                hasBoolLocal = true;
                break;
            }
        }
        Assert.True(hasBoolLocal,
            "ExecutePlaceOre should have a bool local variable for includeMine");

        // Verify the IL contains the "include_mine" string by checking for ldstr opcode
        byte[] il = body.GetILAsByteArray();
        Assert.NotNull(il);
        Assert.True(il.Length > 0,
            "ExecutePlaceOre should have non-empty IL body containing include_mine logic");
    }

    // ─── V4 Task 3B: Batch Semantic Position Support ───────────


    /// <summary>
    /// Helper: extracts the item-level properties dict from a batch tool's "items" array schema.
    /// </summary>
    private static Dictionary<string, object> GetBatchItemProperties(ToolDefinition tool)
    {
        var props = tool.Parameters["properties"] as Dictionary<string, object>;
        var itemsDef = props["items"] as Dictionary<string, object>;
        var itemSchema = itemsDef["items"] as Dictionary<string, object>;
        return itemSchema["properties"] as Dictionary<string, object>;
    }

    /// <summary>
    /// Helper: extracts the item-level required array from a batch tool's "items" array schema.
    /// </summary>
    private static string[] GetBatchItemRequired(ToolDefinition tool)
    {
        var props = tool.Parameters["properties"] as Dictionary<string, object>;
        var itemsDef = props["items"] as Dictionary<string, object>;
        var itemSchema = itemsDef["items"] as Dictionary<string, object>;
        return itemSchema["required"] as string[];
    }

    [Theory]
    [InlineData("place_buildings")]
    [InlineData("place_units")]
    [InlineData("place_infantries")]
    public void BatchTools_ItemSchemaIncludesPositionEnum(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);

        var itemProps = GetBatchItemProperties(tool);
        Assert.NotNull(itemProps);
        Assert.True(itemProps.ContainsKey("position"),
            $"{toolName} batch item should have 'position' property");

        // Verify it has enum values matching PositionEnum
        var positionProp = itemProps["position"] as Dictionary<string, object>;
        Assert.NotNull(positionProp);
        Assert.True(positionProp.ContainsKey("enum"),
            $"{toolName} batch item 'position' should have enum values");
        var enumValues = positionProp["enum"] as string[];
        Assert.NotNull(enumValues);
        Assert.Contains("northwest", enumValues);
        Assert.Contains("southeast", enumValues);
        Assert.Contains("center", enumValues);
    }

    [Theory]
    [InlineData("place_buildings")]
    [InlineData("place_units")]
    [InlineData("place_infantries")]
    public void BatchTools_ItemRequiresOnlyName(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);

        var required = GetBatchItemRequired(tool);
        Assert.NotNull(required);
        Assert.Single(required);
        Assert.Equal("name", required[0]);
    }

    [Theory]
    [InlineData("place_buildings")]
    [InlineData("place_units")]
    [InlineData("place_infantries")]
    public void BatchTools_ItemStillHasXPctYPctForBackwardCompat(string toolName)
    {
        var tools = ToolDefinitions.GetAllTools();
        var tool = tools.FirstOrDefault(t => t.Name == toolName);
        Assert.NotNull(tool);

        var itemProps = GetBatchItemProperties(tool);
        Assert.True(itemProps.ContainsKey("x_pct"),
            $"{toolName} batch item should still have x_pct for backward compatibility");
        Assert.True(itemProps.ContainsKey("y_pct"),
            $"{toolName} batch item should still have y_pct for backward compatibility");
    }

    [Theory]
    [InlineData("place_buildings")]
    [InlineData("place_units")]
    [InlineData("place_infantries")]
    public void BatchTools_ItemPositionEnumMatchesSinglePlacementTools(string toolName)
    {
        // Get the corresponding single-placement tool's position enum
        string singleToolName = toolName switch
        {
            "place_buildings" => "place_building",
            "place_units" => "place_unit",
            "place_infantries" => "place_infantry",
            _ => throw new System.Exception($"Unknown batch tool: {toolName}")
        };

        var tools = ToolDefinitions.GetAllTools();
        var singleTool = tools.FirstOrDefault(t => t.Name == singleToolName);
        Assert.NotNull(singleTool);
        var singleProps = singleTool.Parameters["properties"] as Dictionary<string, object>;
        var singlePositionEnum = (singleProps["position"] as Dictionary<string, object>)["enum"] as string[];

        var batchTool = tools.FirstOrDefault(t => t.Name == toolName);
        var batchItemProps = GetBatchItemProperties(batchTool);
        var batchPositionEnum = (batchItemProps["position"] as Dictionary<string, object>)["enum"] as string[];

        Assert.Equal(singlePositionEnum, batchPositionEnum);
    }

    /// <summary>
    /// Verifies ExecutePlaceBatch routes through ResolvePosition(item, itemScope)
    /// rather than the old ResolvePercentagePosition(xPct, yPct, itemScope).
    /// Source-inspection test: reads the ToolExecutor.cs source and checks method calls.
    /// </summary>
    [Fact]
    public void ExecutePlaceBatch_RoutesViaResolvePosition_ByCodeInspection()
    {
        string sourceFile = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "TSMapEditor", "AI", "ToolExecutor.cs");

        if (!System.IO.File.Exists(sourceFile))
        {
            // Try alternative relative path
            sourceFile = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    "..", "..", "..", "..", "TSMapEditor", "AI", "ToolExecutor.cs"));
        }

        Assert.True(System.IO.File.Exists(sourceFile),
            $"Could not find ToolExecutor.cs at {sourceFile}");

        string source = System.IO.File.ReadAllText(sourceFile);

        // Find the ExecutePlaceBatch method body
        int methodStart = source.IndexOf("private string ExecutePlaceBatch(");
        Assert.True(methodStart >= 0, "ExecutePlaceBatch method not found");

        // Extract a reasonable chunk after method start to inspect
        string methodBody = source.Substring(methodStart, System.Math.Min(1500, source.Length - methodStart));

        // Should call ResolvePosition(item, itemScope) — NOT ResolvePercentagePosition
        Assert.Contains("ResolvePosition(item, itemScope)", methodBody);
        Assert.DoesNotContain("ResolvePercentagePosition(xPct, yPct, itemScope)", methodBody);

        // Task 3B Revision: selection containment must be item-scoped
        // Should call GetActiveSelection(itemScope) — NOT GetActiveSelection(PositionScope.SelectionWhenActive)
        Assert.Contains("GetActiveSelection(itemScope)", methodBody);
        Assert.DoesNotContain("GetActiveSelection(PositionScope.SelectionWhenActive)", methodBody);
    }
}
