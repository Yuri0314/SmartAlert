using System;
using System.Collections.Generic;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places overlay (e.g., tiberium/ore) in a rectangular area.
    /// Unlike WAE's PlaceOverlayMutation, this does not depend on BrushSize
    /// and works on a specified rectangular region directly.
    /// </summary>
    public class AIPlaceOverlayMutation : Mutation
    {
        public AIPlaceOverlayMutation(IMutationTarget mutationTarget,
            OverlayType overlayType, int startX, int startY,
            int width, int height, string description)
            : base(mutationTarget)
        {
            this.overlayType = overlayType;
            this.startX = startX;
            this.startY = startY;
            this.width = width;
            this.height = height;
            this.description = description;
        }

        private readonly OverlayType overlayType;
        private readonly int startX;
        private readonly int startY;
        private readonly int width;
        private readonly int height;
        private readonly string description;

        // Undo data: stores original overlay state for each modified cell
        private List<OriginalOverlayData> undoData;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Place {overlayType.ININame} at ({startX},{startY}) {width}x{height}"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            undoData = new List<OriginalOverlayData>();
            var map = MutationTarget.Map;

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    var cell = map.GetTile(x, y);
                    if (cell == null)
                        continue;

                    // Save original overlay for undo
                    undoData.Add(new OriginalOverlayData
                    {
                        Position = new Point2D(x, y),
                        OverlayTypeIndex = cell.Overlay?.OverlayType?.Index ?? -1,
                        FrameIndex = cell.Overlay?.FrameIndex ?? 0
                    });

                    // Place overlay
                    cell.Overlay = new Overlay()
                    {
                        Position = new Point2D(x, y),
                        OverlayType = overlayType,
                        FrameIndex = 0
                    };
                }
            }

            // Update frame indices for tiberium smoothing
            if (overlayType.Tiberium)
            {
                for (int y = startY - 1; y <= startY + height; y++)
                {
                    for (int x = startX - 1; x <= startX + width; x++)
                    {
                        var cell = map.GetTile(x, y);
                        if (cell?.Overlay != null)
                        {
                            cell.Overlay.FrameIndex = map.GetOverlayFrameIndex(new Point2D(x, y));
                        }
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            if (undoData == null)
                return;

            var map = MutationTarget.Map;

            foreach (var data in undoData)
            {
                var cell = map.GetTile(data.Position);
                if (cell == null)
                    continue;

                if (data.OverlayTypeIndex < 0)
                {
                    cell.Overlay = null;
                }
                else
                {
                    cell.Overlay = new Overlay()
                    {
                        Position = data.Position,
                        OverlayType = map.Rules.OverlayTypes[data.OverlayTypeIndex],
                        FrameIndex = data.FrameIndex
                    };
                }
            }

            // Re-smooth tiberium after undo
            if (overlayType.Tiberium)
            {
                for (int y = startY - 1; y <= startY + height; y++)
                {
                    for (int x = startX - 1; x <= startX + width; x++)
                    {
                        var cell = map.GetTile(x, y);
                        if (cell?.Overlay != null)
                        {
                            cell.Overlay.FrameIndex = map.GetOverlayFrameIndex(new Point2D(x, y));
                        }
                    }
                }
            }

            MutationTarget.InvalidateMap();
        }

        private struct OriginalOverlayData
        {
            public Point2D Position;
            public int OverlayTypeIndex;
            public int FrameIndex;
        }
    }
}
