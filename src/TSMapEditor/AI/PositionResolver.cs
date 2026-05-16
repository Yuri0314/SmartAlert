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
                "north" => (50, 10),
                "south" => (50, 90),
                "east" => (90, 50),
                "west" => (10, 50),
                "northwest" => (15, 15),
                "northeast" => (85, 15),
                "southwest" => (15, 85),
                "southeast" => (85, 85),
                // Aliases
                "top" => (50, 10),
                "bottom" => (50, 90),
                "left" => (10, 50),
                "right" => (90, 50),
                "top_left" => (15, 15),
                "top_right" => (85, 15),
                "bottom_left" => (15, 85),
                "bottom_right" => (85, 85),
                _ => (50, 50), // Unknown → center
            };

            return ResolvePercentage(xPct, yPct);
        }

        /// <summary>
        /// Converts percentage coordinates (0-100) to isometric cell coordinates.
        /// 0% = diamond edge (min), 50% = center, 100% = diamond edge (max).
        /// 
        /// For the user and AI, the percentage axes map to screen directions:
        /// - X: 0% = screen-left, 100% = screen-right
        /// - Y: 0% = screen-top, 100% = screen-bottom
        /// </summary>
        private Point2D ResolvePercentage(int xPct, int yPct)
        {
            // Clamp to 0-100
            xPct = Math.Max(0, Math.Min(100, xPct));
            yPct = Math.Max(0, Math.Min(100, yPct));

            int center = Center;
            int radius = DiamondRadius;

            // Map percentage to offset from center
            // 50% → 0 offset, 0% → -radius, 100% → +radius
            // Use 85% of radius to keep things safely inside the diamond
            int safeRadius = (int)(radius * 0.85);

            double xOffset = (xPct - 50.0) / 50.0 * safeRadius;
            double yOffset = (yPct - 50.0) / 50.0 * safeRadius;

            int x = center + (int)xOffset;
            int y = center + (int)yOffset;

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
    }
}
