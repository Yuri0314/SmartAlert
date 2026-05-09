using System;
using Rampastring.Tools;
using TSMapEditor.GameMath;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area by N levels.
    /// Uses FSRaiseGroundMutation (non-steep, same as the editor's default 
    /// toolbar "Raise Ground" button) with a large BrushSize.
    /// 
    /// Key insight: each call to FSRaiseGroundMutation.Perform() calls RaiseGround()
    /// which raises ALL cells at the origin's level by 1, then Process() places ramps.
    /// We call it N times with the SAME brush size (no gradient shrinking).
    /// Process() handles the border ramps automatically each time.
    /// 
    /// DO NOT shrink the brush between levels - that creates ramp tiles from earlier
    /// passes that don't get updated by later passes, causing black cliff borders.
    /// </summary>
    public class AISetHeightMutation : FSRaiseGroundMutation
    {
        public AISetHeightMutation(IMutationTarget mutationTarget,
            int startX, int startY, int width, int height,
            byte raiseBy, string description)
            : base(mutationTarget,
                   new Point2D(startX + width / 2, startY + height / 2),
                   new BrushSize(width + 2, height + 2))
        {
            this.startX = startX;
            this.startY = startY;
            this.areaWidth = width;
            this.areaHeight = height;
            this.raiseBy = raiseBy;
            this.displayDescription = description;
        }

        private readonly int startX;
        private readonly int startY;
        private readonly int areaWidth;
        private readonly int areaHeight;
        private readonly byte raiseBy;
        private readonly string displayDescription;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(displayDescription)
                ? $"AI: Raise height by {raiseBy} at ({startX},{startY}) {areaWidth}x{areaHeight}"
                : $"AI: {displayDescription}";
        }

        public override void Perform()
        {
            int centerX = startX + areaWidth / 2;
            int centerY = startY + areaHeight / 2;
            // Fixed brush size for all passes - DO NOT shrink between levels
            int brushW = areaWidth + 2;
            int brushH = areaHeight + 2;

            var centerCell = Map.GetTile(centerX, centerY);
            if (centerCell == null) return;

            Logger.Log($"AISetHeight: raiseBy={raiseBy}, area=({startX},{startY}) {areaWidth}x{areaHeight}, " +
                       $"center=({centerX},{centerY}), baseHeight={centerCell.Level}, brush={brushW}x{brushH}");

            // Each call raises ALL cells at origin's level by 1, then fixes ramps.
            // Using the SAME brush size each time ensures each pass's Process() 
            // re-evaluates the SAME border cells, keeping ramps consistent.
            for (int i = 0; i < raiseBy; i++)
            {
                var mutation = new FSRaiseGroundMutation(
                    MutationTarget,
                    new Point2D(centerX, centerY),
                    new BrushSize(brushW, brushH));

                mutation.Perform();

                var afterCell = Map.GetTile(centerX, centerY);
                Logger.Log($"  Pass {i}: center.Level={afterCell?.Level}");
            }

            MutationTarget.InvalidateMap();
        }
    }
}
