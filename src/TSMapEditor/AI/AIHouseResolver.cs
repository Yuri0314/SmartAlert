using System;
using System.Collections.Generic;
using System.Linq;
using TSMapEditor.Models;

namespace TSMapEditor.AI
{
    /// <summary>
    /// Resolves owner (House) names from AI tool calls.
    /// Uses strict exact + case-insensitive matching only.
    /// Does NOT fall back to the first house on mismatch.
    /// </summary>
    public static class AIHouseResolver
    {
        /// <summary>
        /// Resolves an owner name to a House.
        /// - Empty/null/whitespace → "Neutral" (if present), otherwise null.
        /// - Exact case-insensitive match only. No partial matching.
        /// - Returns null if no match found (caller should report error).
        /// </summary>
        public static House ResolveOwner(IReadOnlyList<House> houses, string ownerName)
        {
            if (houses == null || houses.Count == 0)
                return null;

            string requestedOwner = string.IsNullOrWhiteSpace(ownerName) ? "Neutral" : ownerName.Trim();

            return houses.FirstOrDefault(house =>
                string.Equals(house.ININame, requestedOwner, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Formats the list of available owners for error messages.
        /// </summary>
        public static string FormatAvailableOwners(IReadOnlyList<House> houses)
        {
            if (houses == null || houses.Count == 0)
                return "(none)";

            return string.Join(", ", houses.Select(house => house.ININame));
        }
    }
}
