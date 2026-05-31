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

        /// <summary>
        /// Screen horizontal coordinate (proportional to cellX - cellY).
        /// Positive = screen-right, negative = screen-left.
        /// </summary>
        private static int ScreenX(Point2D point) => point.X - point.Y;

        /// <summary>
        /// Screen vertical coordinate relative to center (proportional to cellX + cellY).
        /// Positive = screen-down, negative = screen-up.
        /// </summary>
        private static int ScreenY(PositionResolver resolver, Point2D point)
            => point.X + point.Y - (resolver.Center * 2);

        // ─── Full-map Resolve baseline ─────────────────────────────

        [Fact]
        public void Resolve_CenterReturnsMapCenter()
        {
            var resolver = CreateResolver();
            var pos = resolver.Resolve("center");

            Assert.Equal(resolver.Center, pos.X);
            Assert.Equal(resolver.Center, pos.Y);
        }

        // ─── Screen-space cardinal direction tests ─────────────────

        [Fact]
        public void Resolve_SemanticCardinalDirectionsUseScreenSpaceAxes()
        {
            var resolver = CreateResolver();
            int tolerance = 2; // allow small integer truncation errors

            var center = resolver.Resolve("center");
            Assert.Equal(0, ScreenX(center));
            Assert.Equal(0, ScreenY(resolver, center));

            var north = resolver.Resolve("north");
            Assert.True(System.Math.Abs(ScreenX(north)) <= tolerance,
                $"north ScreenX should be ~0, got {ScreenX(north)}");
            Assert.True(ScreenY(resolver, north) < 0,
                $"north ScreenY should be < 0, got {ScreenY(resolver, north)}");

            var south = resolver.Resolve("south");
            Assert.True(System.Math.Abs(ScreenX(south)) <= tolerance,
                $"south ScreenX should be ~0, got {ScreenX(south)}");
            Assert.True(ScreenY(resolver, south) > 0,
                $"south ScreenY should be > 0, got {ScreenY(resolver, south)}");

            var east = resolver.Resolve("east");
            Assert.True(ScreenX(east) > 0,
                $"east ScreenX should be > 0, got {ScreenX(east)}");
            Assert.True(System.Math.Abs(ScreenY(resolver, east)) <= tolerance,
                $"east ScreenY should be ~0, got {ScreenY(resolver, east)}");

            var west = resolver.Resolve("west");
            Assert.True(ScreenX(west) < 0,
                $"west ScreenX should be < 0, got {ScreenX(west)}");
            Assert.True(System.Math.Abs(ScreenY(resolver, west)) <= tolerance,
                $"west ScreenY should be ~0, got {ScreenY(resolver, west)}");
        }

        // ─── Screen-space diagonal direction tests ─────────────────

        [Fact]
        public void Resolve_SemanticDiagonalDirectionsUseScreenSpaceQuadrants()
        {
            var resolver = CreateResolver();

            var nw = resolver.Resolve("northwest");
            Assert.True(ScreenX(nw) < 0, $"northwest ScreenX should be < 0, got {ScreenX(nw)}");
            Assert.True(ScreenY(resolver, nw) < 0, $"northwest ScreenY should be < 0, got {ScreenY(resolver, nw)}");

            var ne = resolver.Resolve("northeast");
            Assert.True(ScreenX(ne) > 0, $"northeast ScreenX should be > 0, got {ScreenX(ne)}");
            Assert.True(ScreenY(resolver, ne) < 0, $"northeast ScreenY should be < 0, got {ScreenY(resolver, ne)}");

            var sw = resolver.Resolve("southwest");
            Assert.True(ScreenX(sw) < 0, $"southwest ScreenX should be < 0, got {ScreenX(sw)}");
            Assert.True(ScreenY(resolver, sw) > 0, $"southwest ScreenY should be > 0, got {ScreenY(resolver, sw)}");

            var se = resolver.Resolve("southeast");
            Assert.True(ScreenX(se) > 0, $"southeast ScreenX should be > 0, got {ScreenX(se)}");
            Assert.True(ScreenY(resolver, se) > 0, $"southeast ScreenY should be > 0, got {ScreenY(resolver, se)}");
        }

        // ─── Percentage corner tests ───────────────────────────────

        [Fact]
        public void Resolve_PercentageCornersUseScreenSpaceQuadrants()
        {
            var resolver = CreateResolver();

            // (15,15) → screen top-left
            var topLeft = resolver.Resolve(null, 15, 15);
            Assert.True(ScreenX(topLeft) < 0, $"(15,15) ScreenX should be < 0, got {ScreenX(topLeft)}");
            Assert.True(ScreenY(resolver, topLeft) < 0, $"(15,15) ScreenY should be < 0, got {ScreenY(resolver, topLeft)}");

            // (85,15) → screen top-right
            var topRight = resolver.Resolve(null, 85, 15);
            Assert.True(ScreenX(topRight) > 0, $"(85,15) ScreenX should be > 0, got {ScreenX(topRight)}");
            Assert.True(ScreenY(resolver, topRight) < 0, $"(85,15) ScreenY should be < 0, got {ScreenY(resolver, topRight)}");

            // (15,85) → screen bottom-left
            var bottomLeft = resolver.Resolve(null, 15, 85);
            Assert.True(ScreenX(bottomLeft) < 0, $"(15,85) ScreenX should be < 0, got {ScreenX(bottomLeft)}");
            Assert.True(ScreenY(resolver, bottomLeft) > 0, $"(15,85) ScreenY should be > 0, got {ScreenY(resolver, bottomLeft)}");

            // (85,85) → screen bottom-right
            var bottomRight = resolver.Resolve(null, 85, 85);
            Assert.True(ScreenX(bottomRight) > 0, $"(85,85) ScreenX should be > 0, got {ScreenX(bottomRight)}");
            Assert.True(ScreenY(resolver, bottomRight) > 0, $"(85,85) ScreenY should be > 0, got {ScreenY(resolver, bottomRight)}");
        }

        // ─── Alias tests ───────────────────────────────────────────

        [Fact]
        public void Resolve_AliasesMatchCorrespondingSemanticPositions()
        {
            var resolver = CreateResolver();

            // top_left == northwest
            var nw = resolver.Resolve("northwest");
            var tl = resolver.Resolve("top_left");
            Assert.Equal(nw.X, tl.X);
            Assert.Equal(nw.Y, tl.Y);

            // top_right == northeast
            var ne = resolver.Resolve("northeast");
            var tr = resolver.Resolve("top_right");
            Assert.Equal(ne.X, tr.X);
            Assert.Equal(ne.Y, tr.Y);

            // bottom_left == southwest
            var sw = resolver.Resolve("southwest");
            var bl = resolver.Resolve("bottom_left");
            Assert.Equal(sw.X, bl.X);
            Assert.Equal(sw.Y, bl.Y);

            // bottom_right == southeast
            var se = resolver.Resolve("southeast");
            var br = resolver.Resolve("bottom_right");
            Assert.Equal(se.X, br.X);
            Assert.Equal(se.Y, br.Y);
        }

        // ─── ResolveWithinSelection tests (unchanged) ──────────────

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

        // ─── V4 Task 3A: Expanded full-map coverage tests ──────────

        [Fact]
        public void Resolve_FullMapDiagonalAliasesUseExpandedPercentages()
        {
            var resolver = CreateResolver();

            // northwest should now equal explicit (2,2) — Task 3D tuning
            var nw = resolver.Resolve("northwest");
            var explicit2 = resolver.Resolve(null, 2, 2);
            Assert.Equal(explicit2.X, nw.X);
            Assert.Equal(explicit2.Y, nw.Y);

            // northeast == (98,2)
            var ne = resolver.Resolve("northeast");
            var explicit98_2 = resolver.Resolve(null, 98, 2);
            Assert.Equal(explicit98_2.X, ne.X);
            Assert.Equal(explicit98_2.Y, ne.Y);

            // southwest == (2,98)
            var sw = resolver.Resolve("southwest");
            var explicit2_98 = resolver.Resolve(null, 2, 98);
            Assert.Equal(explicit2_98.X, sw.X);
            Assert.Equal(explicit2_98.Y, sw.Y);

            // southeast == (98,98)
            var se = resolver.Resolve("southeast");
            var explicit98 = resolver.Resolve(null, 98, 98);
            Assert.Equal(explicit98.X, se.X);
            Assert.Equal(explicit98.Y, se.Y);

            // Aliases match their direction names
            var tl = resolver.Resolve("top_left");
            Assert.Equal(nw.X, tl.X);
            Assert.Equal(nw.Y, tl.Y);

            var tr = resolver.Resolve("top_right");
            Assert.Equal(ne.X, tr.X);
            Assert.Equal(ne.Y, tr.Y);

            var bl = resolver.Resolve("bottom_left");
            Assert.Equal(sw.X, bl.X);
            Assert.Equal(sw.Y, bl.Y);

            var br = resolver.Resolve("bottom_right");
            Assert.Equal(se.X, br.X);
            Assert.Equal(se.Y, br.Y);
        }

        [Fact]
        public void Resolve_FullMapCardinalAliasesUseExpandedPercentages()
        {
            var resolver = CreateResolver();

            // north == (50, 5) — Task 3D tuning from 10→5
            var north = resolver.Resolve("north");
            var explicit50_5 = resolver.Resolve(null, 50, 5);
            Assert.Equal(explicit50_5.X, north.X);
            Assert.Equal(explicit50_5.Y, north.Y);

            // south == (50, 95)
            var south = resolver.Resolve("south");
            var explicit50_95 = resolver.Resolve(null, 50, 95);
            Assert.Equal(explicit50_95.X, south.X);
            Assert.Equal(explicit50_95.Y, south.Y);

            // east == (95, 50)
            var east = resolver.Resolve("east");
            var explicit95_50 = resolver.Resolve(null, 95, 50);
            Assert.Equal(explicit95_50.X, east.X);
            Assert.Equal(explicit95_50.Y, east.Y);

            // west == (5, 50)
            var west = resolver.Resolve("west");
            var explicit5_50 = resolver.Resolve(null, 5, 50);
            Assert.Equal(explicit5_50.X, west.X);
            Assert.Equal(explicit5_50.Y, west.Y);

            // Aliases match
            var top = resolver.Resolve("top");
            Assert.Equal(north.X, top.X);
            Assert.Equal(north.Y, top.Y);

            var bottom = resolver.Resolve("bottom");
            Assert.Equal(south.X, bottom.X);
            Assert.Equal(south.Y, bottom.Y);

            var left = resolver.Resolve("left");
            Assert.Equal(west.X, left.X);
            Assert.Equal(west.Y, left.Y);

            var right = resolver.Resolve("right");
            Assert.Equal(east.X, right.X);
            Assert.Equal(east.Y, right.Y);
        }

        [Fact]
        public void Resolve_FullMapSafeRadiusUsesNinetyFivePercent()
        {
            // For 200x200 map: DiamondRadius=99, safeRadius=(int)(99*0.95)=94
            var resolver = CreateResolver();
            int center = resolver.Center;   // 199
            int radius = resolver.DiamondRadius; // 99
            int expectedSafeRadius = (int)(radius * 0.95); // 94

            // (100,50) → screenXOffset = +safeRadius, screenYOffset = 0
            // cellX = center + safeRadius/2, cellY = center - safeRadius/2
            var eastEdge = resolver.Resolve(null, 100, 50);
            int screenX = ScreenX(eastEdge); // cellX - cellY = safeRadius

            // Allow ±2 for integer truncation + ClampToDiamond
            Assert.True(System.Math.Abs(screenX - expectedSafeRadius) <= 2,
                $"Expected ScreenX ≈ {expectedSafeRadius}, got {screenX}");
        }

        [Fact]
        public void Resolve_ExtremePercentageStaysWithinDiamond()
        {
            var resolver = CreateResolver();
            int center = resolver.Center;
            int maxManhattan = resolver.DiamondRadius - 2; // ClampToDiamond limit

            var corners = new[]
            {
                resolver.Resolve(null, 0, 0),
                resolver.Resolve(null, 100, 100),
                resolver.Resolve(null, 0, 100),
                resolver.Resolve(null, 100, 0),
            };

            foreach (var pos in corners)
            {
                int manhattan = System.Math.Abs(pos.X - center) + System.Math.Abs(pos.Y - center);
                Assert.True(manhattan <= maxManhattan,
                    $"({pos.X},{pos.Y}) manhattan={manhattan} exceeds max={maxManhattan}");
            }
        }

        [Fact]
        public void Resolve_FullMapDiagonalDistanceIncreases()
        {
            // Task 3D: diagonal at (2,2) should reach at least manhattan 88 on 200x200
            // Calculated: safeRadius=94, offset=(2-50)/50*94=-90.24, cellX=199+(-90+-90)/2=109
            // manhattan = |109-199| = 90
            var resolver = CreateResolver();
            int center = resolver.Center;

            var nw = resolver.Resolve("northwest");
            int manhattan = System.Math.Abs(nw.X - center) + System.Math.Abs(nw.Y - center);

            Assert.True(manhattan >= 88,
                $"northwest manhattan distance should be >= 88 (Task 3D tuning), got {manhattan}");

            // Also verify it's still safely inside the diamond
            Assert.True(manhattan <= resolver.DiamondRadius - 2,
                $"northwest should stay inside diamond, manhattan={manhattan}, limit={resolver.DiamondRadius - 2}");
        }

        [Fact]
        public void ResolveWithinSelection_DiagonalSemanticsRemainUnchanged()
        {
            // Selection-scoped diagonals use ResolveToPercentages() which still has 15/85.
            // This must NOT change even though full-map diagonals moved to 2/98.
            var resolver = CreateResolver();
            var selection = new AISelection(180, 180, 40, 40);

            var nwSelection = resolver.ResolveWithinSelection(selection, "northwest");

            // ResolveToPercentages("northwest") returns (15,15)
            // Selection mapping: x = 180 + round(15/100 * 39) = 180 + round(5.85) = 186
            //                    y = 180 + round(15/100 * 39) = 180 + round(5.85) = 186
            int expectedX = 180 + (int)System.Math.Round(15.0 / 100.0 * 39);
            int expectedY = 180 + (int)System.Math.Round(15.0 / 100.0 * 39);

            Assert.Equal(expectedX, nwSelection.X);
            Assert.Equal(expectedY, nwSelection.Y);

            // Verify it's NOT at the 2% position (which would be the full-map value)
            int twoPctX = 180 + (int)System.Math.Round(2.0 / 100.0 * 39);
            Assert.NotEqual(twoPctX, nwSelection.X);
        }

        [Fact]
        public void ResolveWithinSelection_CardinalSemanticsRemainUnchanged()
        {
            // Selection-scoped cardinals use ResolveToPercentages() which still has 10/90.
            // This must NOT change even though full-map cardinals moved to 5/95.
            var resolver = CreateResolver();
            var selection = new AISelection(180, 180, 40, 40);

            var northSelection = resolver.ResolveWithinSelection(selection, "north");

            // ResolveToPercentages("north") returns (50, 10)
            // Selection mapping: x = 180 + round(50/100 * 39) = 180 + 20 = 200
            //                    y = 180 + round(10/100 * 39) = 180 + round(3.9) = 184
            int expectedX = 180 + (int)System.Math.Round(50.0 / 100.0 * 39);
            int expectedY = 180 + (int)System.Math.Round(10.0 / 100.0 * 39);

            Assert.Equal(expectedX, northSelection.X);
            Assert.Equal(expectedY, northSelection.Y);

            // Verify it's NOT at the 5% position (which would be the full-map value)
            int fivePctY = 180 + (int)System.Math.Round(5.0 / 100.0 * 39);
            Assert.NotEqual(fivePctY, northSelection.Y);
        }
    }
}
