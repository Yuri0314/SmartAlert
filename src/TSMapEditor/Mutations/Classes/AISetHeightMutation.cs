using System;
using TSMapEditor.GameMath;
using TSMapEditor.Mutations.Classes.HeightMutations;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that raises ground in a rectangular area to a target height level.
    /// 
    /// Uses the editor's built-in RaiseGround() method directly, which handles
    /// all ramp transitions correctly. The trick is to use a BrushSize large 
    /// enough to cover the entire target area.
    /// 
    /// RaiseGround() internally does:
    ///   xSize = BrushSize.Width - 2;
    ///   ySize = BrushSize.Height - 2;
    /// So BrushSize = (areaWidth + 2, areaHeight + 2) gives the exact target area.
    /// 
    /// Each call to RaiseGround() raises all cells at the origin's level by 1.
    /// To reach height N, we call it N times from the same origin.
    /// </summary>
    public class AISetHeightMutation : RaiseGroundMutation
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
            // Call the editor's own RaiseGround() method for each height level.
            // RaiseGround() uses BrushSize to determine the area, raises all cells
            // at OriginCell's current level by 1, and calls Process() which
            // correctly handles ALL ramp transitions in one pass.
            //
            // For height N with shrinking gradient:
            // Level 0→1: full area (BrushSize = areaWidth+2 x areaHeight+2)
            // Level 1→2: shrunk area (BrushSize = areaWidth x areaHeight)  
            // Level 2→3: further shrunk, etc.
            
            for (int level = 0; level < targetHeight; level++)
            {
                int shrink = level;
                int w = Math.Max(3, areaWidth - shrink * 2 + 2);
                int h = Math.Max(3, areaHeight - shrink * 2 + 2);

                // Reconfigure brush size and origin for this level's area
                // We need to use reflection or a workaround since BrushSize/OriginCell are readonly
                // Instead, create a new instance for each level
                int centerX = startX + areaWidth / 2;
                int centerY = startY + areaHeight / 2;
                
                var levelMutation = new RaiseGroundMutation(
                    MutationTarget,
                    new Point2D(centerX, centerY),
                    new BrushSize(w, h));
                
                levelMutation.Perform();
            }

            MutationTarget.InvalidateMap();
        }
    }
}
