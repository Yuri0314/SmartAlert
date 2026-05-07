using TSMapEditor.AI.Operations;

namespace TSMapEditor.Tests
{
    /// <summary>
    /// Tests for MapOperation — verifies default values are correct for all fields.
    /// </summary>
    public class MapOperationTests
    {
        [Fact]
        public void DefaultValues_AreCorrect()
        {
            var op = new MapOperation();

            Assert.Equal(string.Empty, op.Type);
            Assert.Equal(0, op.X);
            Assert.Equal(0, op.Y);
            Assert.Equal(1, op.Width);
            Assert.Equal(1, op.Height);
            Assert.Equal(string.Empty, op.TileSetName);
            Assert.Equal(0, op.HeightLevel);
            Assert.Equal(string.Empty, op.Description);
            Assert.Equal(string.Empty, op.ObjectName);
            Assert.Equal(string.Empty, op.Owner);
            Assert.Equal(1, op.Count);
            Assert.Equal(0, op.WaypointIndex);
            Assert.Equal(0.35, op.Density, 2);
        }

        [Fact]
        public void AllProperties_CanBeSet()
        {
            var op = new MapOperation
            {
                Type = "set_waypoint",
                X = 100,
                Y = 200,
                Width = 10,
                Height = 20,
                TileSetName = "Water",
                HeightLevel = 5,
                Description = "Test description",
                ObjectName = "TREE01",
                Owner = "Americans",
                Count = 3,
                WaypointIndex = 7,
                Density = 0.8
            };

            Assert.Equal("set_waypoint", op.Type);
            Assert.Equal(100, op.X);
            Assert.Equal(200, op.Y);
            Assert.Equal(10, op.Width);
            Assert.Equal(20, op.Height);
            Assert.Equal("Water", op.TileSetName);
            Assert.Equal(5, op.HeightLevel);
            Assert.Equal("Test description", op.Description);
            Assert.Equal("TREE01", op.ObjectName);
            Assert.Equal("Americans", op.Owner);
            Assert.Equal(3, op.Count);
            Assert.Equal(7, op.WaypointIndex);
            Assert.Equal(0.8, op.Density, 2);
        }
    }
}
