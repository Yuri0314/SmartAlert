using System;
using TSMapEditor.GameMath;
using TSMapEditor.Models;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Converts semantic positions ("center", "northwest") and percentage positions (x_pct, y_pct)
    /// to actual isometric cell coordinates for RA2/YR maps.
    /// 
    /// For a WxH map, the isometric coordinate space has:
    /// - Center at approximately ((W+H-1)/2, (W+H-1)/2)
    /// - Valid cells forming a diamond shape
    /// - Diamond radius = min(W,H)/2 - 1
    /// </summary>
    public class PositionResolver
    {
        private readonly Map map;

        public PositionResolver(Map map)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
        }

        /// <summary>
        /// Map center in isometric coordinates.
        /// </summary>
        public int Center => (map.Size.X + map.Size.Y - 1) / 2;

        /// <summary>
        /// Diamond radius (max manhattan distance from center to edge).
        /// </summary>
        public int DiamondRadius => Math.Min(map.Size.X, map.Size.Y) / 2 - 1;

        /// <summary>
        /// Resolves a position to isometric cell coordinates.
        /// Accepts either a semantic name or percentage coordinates.
        /// </summary>
        public Point2D Resolve(string semantic = null, int? xPct = null, int? yPct = null)
        {
            Point2D result;

            if (!string.IsNullOrEmpty(semantic))
                result = ResolveSemantic(semantic);
            else if (xPct.HasValue && yPct.HasValue)
                result = ResolvePercentage(xPct.Value, yPct.Value);
            else
                result = new Point2D(Center, Center); // Default to center

            return ClampToDiamond(result);
        }

        /// <summary>
        /// Resolves a semantic position name to isometric coordinates.
        /// </summary>
        private Point2D ResolveSemantic(string position)
        {
            // Convert semantic to percentage, then resolve
            var (xPct, yPct) = position.ToLowerInvariant() switch
            {
                "center" => (50, 50),
                "north" => (50, 5),
                "south" => (50, 95),
                "east" => (95, 50),
                "west" => (5, 50),
                "northwest" => (2, 2),
                "northeast" => (98, 2),
                "southwest" => (2, 98),
                "southeast" => (98, 98),
                // Aliases
                "top" => (50, 5),
                "bottom" => (50, 95),
                "left" => (5, 50),
                "right" => (95, 50),
                "top_left" => (2, 2),
                "top_right" => (98, 2),
                "bottom_left" => (2, 98),
                "bottom_right" => (98, 98),
                _ => (50, 50), // Unknown → center
            };

            return ResolvePercentage(xPct, yPct);
        }

        /// <summary>
        /// Converts percentage coordinates (0-100) to isometric cell coordinates.
        /// 0% = diamond edge (min), 50% = center, 100% = diamond edge (max).
        ///
        /// The percentage axes map to user-facing screen directions:
        /// - X: 0% = screen-left, 100% = screen-right
        /// - Y: 0% = screen-top, 100% = screen-bottom
        ///
        /// The isometric screen projection is approximately:
        ///   screenX ∝ cellX - cellY   (screen horizontal)
        ///   screenY ∝ cellX + cellY   (screen vertical)
        ///
        /// This method converts screen-space offsets back to iso cell offsets:
        ///   cellX offset = (screenXOffset + screenYOffset) / 2
        ///   cellY offset = (screenYOffset - screenXOffset) / 2
        /// </summary>
        private Point2D ResolvePercentage(int xPct, int yPct)
        {
            // Clamp to 0-100
            xPct = Math.Max(0, Math.Min(100, xPct));
            yPct = Math.Max(0, Math.Min(100, yPct));

            int center = Center;
            int radius = DiamondRadius;

            // Map percentage to screen-space offset from center
            // 50% → 0 offset, 0% → -radius, 100% → +radius
            // Use 95% of radius to keep things safely inside the diamond
            // (ClampToDiamond provides the final boundary guarantee)
            int safeRadius = (int)(radius * 0.95);

            double screenXOffset = (xPct - 50.0) / 50.0 * safeRadius;
            double screenYOffset = (yPct - 50.0) / 50.0 * safeRadius;

            // Convert screen-space offsets to isometric cell offsets
            int x = center + (int)((screenXOffset + screenYOffset) / 2.0);
            int y = center + (int)((screenYOffset - screenXOffset) / 2.0);

            return new Point2D(x, y);
        }

        /// <summary>
        /// Clamps a coordinate to the nearest valid position inside the diamond.
        /// Projects from center toward the target point, stopping at the boundary.
        /// </summary>
        public Point2D ClampToDiamond(Point2D point)
        {
            int center = Center;
            int dx = point.X - center;
            int dy = point.Y - center;
            int dist = Math.Abs(dx) + Math.Abs(dy);
            int maxDist = DiamondRadius - 2; // Leave margin from edge

            if (dist <= maxDist)
                return point;

            // Scale down proportionally, preserving direction
            if (dist == 0) dist = 1;
            double scale = (double)maxDist / dist;
            int newX = center + (int)(dx * scale);
            int newY = center + (int)(dy * scale);

            return new Point2D(newX, newY);
        }

        /// <summary>
        /// Returns a summary of the map's coordinate space for the AI system prompt.
        /// </summary>
        public string GetMapInfoSummary()
        {
            return $"地图逻辑尺寸: {map.Size.X}x{map.Size.Y}，" +
                   $"等距中心: ({Center},{Center})，" +
                   $"菱形半径: {DiamondRadius}";
        }

        /// <summary>
        /// Resolves a position relative to an active selection rectangle.
        /// If selection is null, falls back to full-map Resolve.
        /// - 0%/0% maps to selection top-left (X, Y).
        /// - 100%/100% maps to selection bottom-right (X+Width-1, Y+Height-1).
        /// - Semantic positions are mapped relative to the selection bounds.
        ///
        /// NOTE: Unlike full-map Resolve(), this method does NOT apply ClampToDiamond().
        /// UI selections are created from real map cell coordinates by AISelectionCursorAction,
        /// so points computed within the selection rectangle are already valid map cells.
        /// Applying the full-map diamond projection would pull off-center selections toward
        /// the theoretical map center, displacing coordinates outside the selected area.
        /// </summary>
        public Point2D ResolveWithinSelection(AISelection selection, string semantic = null, int? xPct = null, int? yPct = null)
        {
            if (selection == null)
                return Resolve(semantic, xPct, yPct);

            // Convert semantic to percentage relative to selection
            var (resolvedXPct, resolvedYPct) = ResolveToPercentages(semantic, xPct, yPct);

            // Map percentages to selection coordinates
            int clampedX = Math.Max(0, Math.Min(100, resolvedXPct));
            int clampedY = Math.Max(0, Math.Min(100, resolvedYPct));

            int selRight = selection.X + selection.Width - 1;
            int selBottom = selection.Y + selection.Height - 1;

            int x, y;
            if (selection.Width <= 1)
                x = selection.X;
            else
                x = selection.X + (int)Math.Round((double)clampedX / 100.0 * (selection.Width - 1));

            if (selection.Height <= 1)
                y = selection.Y;
            else
                y = selection.Y + (int)Math.Round((double)clampedY / 100.0 * (selection.Height - 1));

            // Clamp to selection bounds (no full-map diamond clamp — see XML comment)
            x = Math.Max(selection.X, Math.Min(selRight, x));
            y = Math.Max(selection.Y, Math.Min(selBottom, y));

            return new Point2D(x, y);
        }

        /// <summary>
        /// Converts a semantic position name or explicit percentages to (xPct, yPct).
        /// Used internally by both full-map and selection-relative resolution.
        /// </summary>
        private (int xPct, int yPct) ResolveToPercentages(string semantic, int? xPct, int? yPct)
        {
            if (!string.IsNullOrEmpty(semantic))
            {
                return semantic.ToLowerInvariant() switch
                {
                    "center" => (50, 50),
                    "north" or "top" => (50, 10),
                    "south" or "bottom" => (50, 90),
                    "east" or "right" => (90, 50),
                    "west" or "left" => (10, 50),
                    "northwest" or "top_left" => (15, 15),
                    "northeast" or "top_right" => (85, 15),
                    "southwest" or "bottom_left" => (15, 85),
                    "southeast" or "bottom_right" => (85, 85),
                    _ => (50, 50),
                };
            }

            if (xPct.HasValue && yPct.HasValue)
                return (xPct.Value, yPct.Value);

            return (50, 50); // Default to center
        }
    }
}
