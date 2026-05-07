using System.Collections.Generic;
using System.Linq;
using TSMapEditor.GameMath;
using TSMapEditor.Models;
using TSMapEditor.UI;

namespace TSMapEditor.Mutations.Classes
{
    /// <summary>
    /// A mutation that places or moves a waypoint on the map.
    /// Waypoints 0-7 are player spawn points (player 1-8).
    /// Used by the AI system's "set_waypoint" operation.
    /// </summary>
    public class AISetWaypointMutation : Mutation
    {
        public AISetWaypointMutation(IMutationTarget mutationTarget,
            int waypointId, Point2D position, string description)
            : base(mutationTarget)
        {
            this.waypointId = waypointId;
            this.position = position;
            this.description = description;
        }

        private readonly int waypointId;
        private readonly Point2D position;
        private readonly string description;

        // Undo data
        private Waypoint existingWaypoint;  // If we're replacing an existing waypoint
        private Waypoint placedWaypoint;
        private bool wasExistingRemoved;

        public override string GetDisplayString()
        {
            return string.IsNullOrEmpty(description)
                ? $"AI: Set waypoint {waypointId} at ({position.X},{position.Y})"
                : $"AI: {description}";
        }

        public override void Perform()
        {
            var map = MutationTarget.Map;

            // Check if this waypoint ID already exists
            existingWaypoint = map.Waypoints.FirstOrDefault(wp => wp.Identifier == waypointId);
            if (existingWaypoint != null)
            {
                // Remove the existing waypoint so we can reposition it
                map.RemoveWaypoint(existingWaypoint);
                wasExistingRemoved = true;
            }

            // Create and place the new waypoint
            placedWaypoint = new Waypoint();
            placedWaypoint.Identifier = waypointId;
            placedWaypoint.Position = position;
            map.AddWaypoint(placedWaypoint);

            MutationTarget.InvalidateMap();
        }

        public override void Undo()
        {
            var map = MutationTarget.Map;

            // Remove the waypoint we placed
            if (placedWaypoint != null)
                map.RemoveWaypoint(placedWaypoint);

            // Restore the original waypoint if we removed one
            if (wasExistingRemoved && existingWaypoint != null)
                map.AddWaypoint(existingWaypoint);

            MutationTarget.InvalidateMap();
        }
    }
}
