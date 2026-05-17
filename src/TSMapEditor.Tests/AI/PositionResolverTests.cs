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
    }
}
