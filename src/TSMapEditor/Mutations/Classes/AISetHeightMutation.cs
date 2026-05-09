using System;
using Rampastring.Tools;
using TSMapEditor.GameMath;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area to a target height level.
    /// Uses FSRaiseGroundMutation (non-steep, same as the editor's default toolbar button)
    /// with a large BrushSize to cover the entire target area.
    /// </summary>
    public class AISetHeightMutation : FSRaiseGroundMutation
    {
        public AISetHeightMutation(IMutationTarget mutationTarget,
            int startX, int startY, int width, int height,
            byte targetHeight, string description)
            : base(mutationTarget,
                   new Point2D(startX + width / 2, startY + height / 2),
                   new BrushSize(width + 2, height + 2))
        {
            this.startX = startX;
            this.startY = startY;
            this.areaWidth = width;
            this.areaHeight = height;
            this.targetHeight = targetHeight;
            this.displayDescription = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int areaWidth;
        private readonly int areaHeight;
        private readonly byte targetHeight;
        private readonly string displayDescription;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(displayDescription)
                ? $"AI: Set height to {targetHeight} at ({startX},{startY}) {areaWidth}x{areaHeight}"
                : $"AI: {displayDescription}";
        }

        public override void Perform()
        {
            int centerX = startX + areaWidth / 2;
            int centerY = startY + areaHeight / 2;

            Logger.Log($"AISetHeight: target height={targetHeight}, area=({startX},{startY}) {areaWidth}x{areaHeight}, center=({centerX},{centerY})");

            for (int level = 0; level < targetHeight; level++)
            {
                int shrink = level;
                int effectiveWidth = Math.Max(1, areaWidth - shrink * 2);
                int effectiveHeight = Math.Max(1, areaHeight - shrink * 2);
                int brushW = effectiveWidth + 2;
                int brushH = effectiveHeight + 2;

                var centerCell = Map.GetTile(centerX, centerY);
                Logger.Log($"  Level {level}->{level + 1}: brush={brushW}x{brushH}, centerCell.Level={centerCell?.Level}");

                var mutation = new FSRaiseGroundMutation(
                    MutationTarget,
                    new Point2D(centerX, centerY),
                    new BrushSize(brushW, brushH));

                mutation.Perform();

                // Log a sample of cell heights after this pass
                var afterCell = Map.GetTile(centerX, centerY);
                var edgeCell = Map.GetTile(startX, startY);
                Logger.Log($"  After: center.Level={afterCell?.Level}, edge.Level={edgeCell?.Level}");
            }

            MutationTarget.InvalidateMap();
        }
    }
}
