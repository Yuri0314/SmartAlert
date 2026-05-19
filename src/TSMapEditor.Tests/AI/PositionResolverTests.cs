using TSMapEditor.AI;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using Xunit;

namespace TSMapEditor.Tests.AI
{
    public class PositionResolverTests
    {
        /// <summary>
        /// Creates a minimal Map stub for PositionResolver.
        /// Map Size = (200, 200), giving Center=199, DiamondRadius=99.
        /// This large diamond ensures test selections fit entirely within valid space.
        /// </summary>
        private static Map CreateTestMap()
        {
            var map = new Map();
            map.Size = new GameMath.Point2D(200, 200);
            return map;
        }

        private static PositionResolver CreateResolver() => new PositionResolver(CreateTestMap());

        // ─── Full-map Resolve baseline ─────────────────────────────

        [Fact]
        public void Resolve_CenterReturnsMapCenter()
        {
            var resolver = CreateResolver();
            var pos = resolver.Resolve("center");

            Assert.Equal(resolver.Center, pos.X);
            Assert.Equal(resolver.Center, pos.Y);
        }

        // ─── ResolveWithinSelection tests ──────────────────────────

        [Fact]
        public void ResolveWithinSelection_CenterReturnsSelectionCenter()
        {
            var resolver = CreateResolver();
            // Selection centered around map center (199, 199) to stay in diamond
            var selection = new AISelection(190, 190, 21, 21);
            // center = (190+10, 190+10) = (200, 200)

            var pos = resolver.ResolveWithinSelection(selection, "center");

            Assert.Equal(200, pos.X);
            Assert.Equal(200, pos.Y);
        }

        [Fact]
        public void ResolveWithinSelection_ZeroZeroReturnsSelectionTopLeft()
        {
            var resolver = CreateResolver();
            // Place selection well within the diamond
            var selection = new AISelection(180, 180, 40, 40);

            var pos = resolver.ResolveWithinSelection(selection, null, 0, 0);

            Assert.Equal(180, pos.X);
            Assert.Equal(180, pos.Y);
        }

        [Fact]
        public void ResolveWithinSelection_HundredHundredReturnsSelectionBottomRight()
        {
            var resolver = CreateResolver();
            var selection = new AISelection(180, 180, 40, 40);
            // Bottom-right should be (180+39, 180+39) = (219, 219)

            var pos = resolver.ResolveWithinSelection(selection, null, 100, 100);

            Assert.Equal(219, pos.X);
            Assert.Equal(219, pos.Y);
        }

        [Fact]
        public void ResolveWithinSelection_SemanticNorthwestMapsInsideSelection()
        {
            var resolver = CreateResolver();
            // Large selection centered in the diamond
            var selection = new AISelection(160, 160, 80, 80);

            var pos = resolver.ResolveWithinSelection(selection, "northwest");

            // northwest = 15%/15%, should be within selection bounds
            Assert.True(pos.X >= selection.X && pos.X < selection.X + selection.Width,
                $"Expected X in [{selection.X}, {selection.X + selection.Width - 1}], got {pos.X}");
            Assert.True(pos.Y >= selection.Y && pos.Y < selection.Y + selection.Height,
                $"Expected Y in [{selection.Y}, {selection.Y + selection.Height - 1}], got {pos.Y}");

            // Should be in the northwest quadrant (first half) of the selection
            int selCenterX = selection.X + selection.Width / 2;
            int selCenterY = selection.Y + selection.Height / 2;
            Assert.True(pos.X < selCenterX, $"Expected X < {selCenterX}, got {pos.X}");
            Assert.True(pos.Y < selCenterY, $"Expected Y < {selCenterY}, got {pos.Y}");
        }

        [Fact]
        public void ResolveWithinSelection_NullSelectionFallsBackToFullMap()
        {
            var resolver = CreateResolver();

            var posSelection = resolver.ResolveWithinSelection(null, "center");
            var posFullMap = resolver.Resolve("center");

            Assert.Equal(posFullMap.X, posSelection.X);
            Assert.Equal(posFullMap.Y, posSelection.Y);
        }

        [Fact]
        public void ResolveWithinSelection_PercentagesClampedToZeroHundred()
        {
            var resolver = CreateResolver();
            var selection = new AISelection(180, 180, 40, 40);

            // Over-100 should clamp to bottom-right
            var posOver = resolver.ResolveWithinSelection(selection, null, 200, 200);
            var posMax = resolver.ResolveWithinSelection(selection, null, 100, 100);

            Assert.Equal(posMax.X, posOver.X);
            Assert.Equal(posMax.Y, posOver.Y);

            // Under-0 should clamp to top-left
            var posUnder = resolver.ResolveWithinSelection(selection, null, -50, -50);
            var posMin = resolver.ResolveWithinSelection(selection, null, 0, 0);

            Assert.Equal(posMin.X, posUnder.X);
            Assert.Equal(posMin.Y, posUnder.Y);
        }

        [Fact]
        public void ResolveWithinSelection_SmallSelectionWidthOneCellIsHandledSafely()
        {
            var resolver = CreateResolver();
            var selection = new AISelection(199, 199, 1, 1);

            var pos = resolver.ResolveWithinSelection(selection, "center");

            Assert.Equal(199, pos.X);
            Assert.Equal(199, pos.Y);
        }

        [Fact]
        public void ResolveWithinSelection_FiftyFiftyReturnsCenterOfSelection()
        {
            var resolver = CreateResolver();
            var selection = new AISelection(180, 180, 41, 21);
            // Center should be (180+20, 180+10) = (200, 190)

            var pos = resolver.ResolveWithinSelection(selection, null, 50, 50);

            Assert.Equal(200, pos.X);
            Assert.Equal(190, pos.Y);
        }

        // ─── BUG-F1a / BUG-F3: Off-center selection regression tests ──

        [Fact]
        public void ResolveWithinSelection_OffCenterSelection_FiftyFiftyStaysAtSelectionCenter()
        {
            // Reproduces BUG-F1a from live verification.
            // Selection (142,93) 12x9 is far from map center (199,199).
            // 50%/50% should resolve to approximately (148,97) — the selection center.
            var resolver = CreateResolver();
            var selection = new AISelection(142, 93, 12, 9);

            var pos = resolver.ResolveWithinSelection(selection, null, 50, 50);

            // Expected center: (142 + round(0.5*11), 93 + round(0.5*8)) = (148, 97)
            int expectedX = 142 + (int)System.Math.Round(0.5 * 11); // 148
            int expectedY = 93 + (int)System.Math.Round(0.5 * 8);   // 97

            Assert.True(pos.X >= selection.X && pos.X <= selection.X + selection.Width - 1,
                $"Expected X in [{selection.X}, {selection.X + selection.Width - 1}], got {pos.X}");
            Assert.True(pos.Y >= selection.Y && pos.Y <= selection.Y + selection.Height - 1,
                $"Expected Y in [{selection.Y}, {selection.Y + selection.Height - 1}], got {pos.Y}");
            Assert.Equal(expectedX, pos.X);
            Assert.Equal(expectedY, pos.Y);
        }

        [Fact]
        public void ResolveWithinSelection_OffCenterSelection_CornersStayAtSelectionCorners()
        {
            // Reproduces BUG-F3 from live verification.
            // (0%,0%) → (142,93), (100%,100%) → (153,101).
            // ClampToDiamond must not displace these.
            var resolver = CreateResolver();
            var selection = new AISelection(142, 93, 12, 9);

            var topLeft = resolver.ResolveWithinSelection(selection, null, 0, 0);
            var bottomRight = resolver.ResolveWithinSelection(selection, null, 100, 100);

            Assert.Equal(142, topLeft.X);
            Assert.Equal(93, topLeft.Y);
            Assert.Equal(153, bottomRight.X);
            Assert.Equal(101, bottomRight.Y);
        }

        [Fact]
        public void ResolveWithinSelection_OffCenterSelection_AllPointsStayInsideSelection()
        {
            // Verifies that no percentage-based resolution escapes the selection bounds,
            // regardless of where the selection is on the map.
            var resolver = CreateResolver();
            var selection = new AISelection(142, 93, 12, 9);

            int selRight = selection.X + selection.Width - 1;
            int selBottom = selection.Y + selection.Height - 1;

            // Test a grid of percentages
            for (int xPct = 0; xPct <= 100; xPct += 25)
            {
                for (int yPct = 0; yPct <= 100; yPct += 25)
                {
                    var pos = resolver.ResolveWithinSelection(selection, null, xPct, yPct);
                    Assert.True(pos.X >= selection.X && pos.X <= selRight,
                        $"({xPct}%,{yPct}%): Expected X in [{selection.X}, {selRight}], got {pos.X}");
                    Assert.True(pos.Y >= selection.Y && pos.Y <= selBottom,
                        $"({xPct}%,{yPct}%): Expected Y in [{selection.Y}, {selBottom}], got {pos.Y}");
                }
            }
        }
    }
}
