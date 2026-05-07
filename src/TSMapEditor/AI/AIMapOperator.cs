using System;
using System.Collections.Generic;
using Rampastring.Tools;
using TSMapEditor.AI.Operations;
using TSMapEditor.Models;
using TSMapEditor.Mutations;
using TSMapEditor.Mutations.Classes;
using TSMapEditor.Rendering;
using TSMapEditor.UI;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Translates structured MapOperation objects into WAE Mutation calls.
    /// This is the bridge between AI intent and actual map modifications.
    /// </summary>
    public class AIMapOperator
    {
        private readonly Map map;
        private readonly TheaterGraphics theaterGraphics;
        private readonly MutationManager mutationManager;
        private readonly IMutationTarget mutationTarget;

        public AIMapOperator(Map map, TheaterGraphics theaterGraphics,
            MutationManager mutationManager, IMutationTarget mutationTarget)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.theaterGraphics = theaterGraphics ?? throw new ArgumentNullException(nameof(theaterGraphics));
            this.mutationManager = mutationManager ?? throw new ArgumentNullException(nameof(mutationManager));
            this.mutationTarget = mutationTarget ?? throw new ArgumentNullException(nameof(mutationTarget));
        }

        /// <summary>
        /// Executes a list of map operations, returning a summary of what was done.
        /// </summary>
        public string ExecuteOperations(List<MapOperation> operations)
        {
            if (operations == null || operations.Count == 0)
                return string.Empty;

            var results = new List<string>();

            foreach (var op in operations)
            {
                try
                {
                    string result = ExecuteOperation(op);
                    if (!string.IsNullOrEmpty(result))
                        results.Add(result);
                }
                catch (Exception ex)
                {
                    string error = $"操作失败 ({op.Type}): {ex.Message}";
                    Logger.Log($"AIMapOperator error: {ex}");
                    results.Add(error);
                }
            }

            return string.Join("\n", results);
        }

        private string ExecuteOperation(MapOperation op)
        {
            switch (op.Type?.ToLowerInvariant())
            {
                case "fill_terrain":
                    return ExecuteFillTerrain(op);
                default:
                    return $"不支持的操作类型: {op.Type}";
            }
        }

        private string ExecuteFillTerrain(MapOperation op)
        {
            // Validate coordinates
            if (op.X < 0 || op.Y < 0 || op.Width <= 0 || op.Height <= 0)
                return $"无效的坐标或尺寸: ({op.X},{op.Y}) {op.Width}x{op.Height}";

            // Clamp to map bounds
            int maxX = Math.Min(op.X + op.Width, map.Size.X);
            int maxY = Math.Min(op.Y + op.Height, map.Size.Y);
            int startX = Math.Max(0, op.X);
            int startY = Math.Max(0, op.Y);
            int actualWidth = maxX - startX;
            int actualHeight = maxY - startY;

            if (actualWidth <= 0 || actualHeight <= 0)
                return $"坐标超出地图范围: ({op.X},{op.Y})";

            // Find the tileset by name
            int tileIndex = FindTileIndexByName(op.TileSetName);
            if (tileIndex < 0)
                return $"找不到地形类型: \"{op.TileSetName}\"";

            // Create and execute the mutation
            var mutation = new AITerrainMutation(mutationTarget, startX, startY,
                actualWidth, actualHeight, tileIndex,
                op.Description ?? $"填充 {op.TileSetName} 在 ({startX},{startY}) {actualWidth}x{actualHeight}");

            mutationManager.PerformMutation(mutation);

            return $"✓ 已填充 {op.TileSetName} 在 ({startX},{startY}) 区域 {actualWidth}x{actualHeight}";
        }

        /// <summary>
        /// Finds the start tile index for a tileset by name (case-insensitive).
        /// </summary>
        private int FindTileIndexByName(string tileSetName)
        {
            if (string.IsNullOrWhiteSpace(tileSetName))
                return -1;

            var tileSets = theaterGraphics.Theater.TileSets;

            for (int i = 0; i < tileSets.Count; i++)
            {
                if (string.Equals(tileSets[i].SetName, tileSetName, StringComparison.OrdinalIgnoreCase))
                {
                    return tileSets[i].StartTileIndex;
                }
            }

            // Try partial match as fallback
            for (int i = 0; i < tileSets.Count; i++)
            {
                if (tileSets[i].SetName != null &&
                    tileSets[i].SetName.IndexOf(tileSetName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return tileSets[i].StartTileIndex;
                }
            }

            return -1;
        }
    }
}
