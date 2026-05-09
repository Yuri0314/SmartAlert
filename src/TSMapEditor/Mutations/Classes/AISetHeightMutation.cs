using System;
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
    /// Each call to FSRaiseGroundMutation raises ALL cells at the origin's 
    /// level by 1, then Process() places ramp transition tiles on the border.
    /// We call it N times with the SAME brush size to reach the target height.
    /// 
    /// IMPORTANT: Do NOT shrink the brush between levels (gradient approach).
    /// That creates ramp tiles from earlier passes that don't get re-evaluated 
    /// by later passes, causing black cliff borders.
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
            this.areaWidth = width;
            this.areaHeight = height;
            this.raiseBy = raiseBy;
            this.displayDescription = description;
        }

        private readonly int areaWidth;
        private readonly int areaHeight;
        private readonly byte raiseBy;
        private readonly string displayDescription;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(displayDescription)
                ? $"AI: Raise height by {raiseBy} at {OriginCell} {areaWidth}x{areaHeight}"
                : $"AI: {displayDescription}";
        }

        public override void Perform()
        {
            int brushW = areaWidth + 2;
            int brushH = areaHeight + 2;

            for (int i = 0; i < raiseBy; i++)
            {
                var mutation = new FSRaiseGroundMutation(
                    MutationTarget,
                    OriginCell,
                    new BrushSize(brushW, brushH));

                mutation.Perform();
            }

            MutationTarget.InvalidateMap();
        }
    }
}
